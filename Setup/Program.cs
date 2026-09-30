using System;
using System.Diagnostics;
using System.IO;
using System.Windows.Forms;
using Microsoft.Win32;

namespace FolderMoveProtector.Setup
{
    internal static class Program
    {
        // Must match the [Guid("...")] attribute on FolderMoveHook in Hook\FolderMoveHook.cs
        private const string Clsid = "678B7C88-AFC9-43A9-83D2-EF0CC0855E58";
        private const string HandlerName = "Folder Move Protector";
        private const string InstallDir = @"C:\Program Files\Folder Move Protector";

        [STAThread]
        private static void Main(string[] args)
        {
            try
            {
                bool uninstall = args.Length > 0 &&
                                  args[0].Equals("/uninstall", StringComparison.OrdinalIgnoreCase);

                if (uninstall)
                {
                    Uninstall();
                    MessageBox.Show(
                        "Folder Move Protector was uninstalled. Restart Explorer (or sign out) to finish.",
                        "Folder Move Protector", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                else
                {
                    Install();
                    MessageBox.Show(
                        "Folder Move Protector installed. Restart Explorer (or sign out) for it to take " +
                        "effect, then find it in the Start Menu to add protected folders.",
                        "Folder Move Protector", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("Setup failed: " + ex.Message,
                    "Folder Move Protector", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private static void Install()
        {
            string sourceDir = AppDomain.CurrentDomain.BaseDirectory;
            string sourceHookDll = Path.Combine(sourceDir, "FolderMoveProtector.Hook.dll");
            string sourceSettingsExe = Path.Combine(sourceDir, "FolderMoveProtector.Settings.exe");

            if (!File.Exists(sourceHookDll) || !File.Exists(sourceSettingsExe))
            {
                throw new FileNotFoundException(
                    "FolderMoveProtector.Hook.dll and FolderMoveProtector.Settings.exe must be in the " +
                    "same folder as Setup.exe.");
            }

            Directory.CreateDirectory(InstallDir);
            string installedHookDll = Path.Combine(InstallDir, "FolderMoveProtector.Hook.dll");
            string installedSettingsExe = Path.Combine(InstallDir, "FolderMoveProtector.Settings.exe");

            File.Copy(sourceHookDll, installedHookDll, overwrite: true);
            File.Copy(sourceSettingsExe, installedSettingsExe, overwrite: true);

            // Files that traveled through a browser download (e.g. a GitHub zip)
            // are often tagged "downloaded from the internet" by Windows, which
            // can interfere with loading them. This strips that tag defensively -
            // it's a no-op if the tag was never there, so it's safe to always run.
            RemoveDownloadBlock(installedHookDll);
            RemoveDownloadBlock(installedSettingsExe);

            RunRegAsm(installedHookDll, "/codebase");
            RegisterCopyHook();
            CreateStartMenuShortcut(installedSettingsExe);
        }

        // Removes the hidden "Zone.Identifier" marker Windows attaches to files
        // that came from a browser download (this is what Unblock-File in
        // PowerShell also does). .NET's own File.Delete refuses paths shaped
        // like this (a second colon in the path), so this calls the underlying
        // Win32 function directly instead, which has no such restriction.
        [System.Runtime.InteropServices.DllImport("kernel32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode, SetLastError = true)]
        private static extern bool DeleteFile(string lpFileName);

        private static void RemoveDownloadBlock(string filePath)
        {
            DeleteFile(filePath + ":Zone.Identifier");
            // No error check: if there was no such stream (the common case for
            // a file that was never flagged), this simply does nothing.
        }

        // Locates the 64-bit RegAsm.exe that ships as part of the .NET Framework
        // itself (not a separate download) and runs it against the installed DLL.
        // This is the same tool Install.ps1 already used successfully - rather
        // than hand-writing the CLSID/ProgId registry entries ourselves (which
        // turned out to have a bug that's not worth chasing further when a
        // known-good tool already exists for exactly this).
        private static void RunRegAsm(string dllPath, string arguments)
        {
            string regAsmPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.Windows),
                @"Microsoft.NET\Framework64\v4.0.30319\RegAsm.exe");

            if (!File.Exists(regAsmPath))
                throw new FileNotFoundException("Could not find 64-bit RegAsm.exe at " + regAsmPath);

            var startInfo = new ProcessStartInfo(regAsmPath, "\"" + dllPath + "\" " + arguments)
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            using (Process process = Process.Start(startInfo))
            {
                string output = process.StandardOutput.ReadToEnd();
                string error = process.StandardError.ReadToEnd();
                process.WaitForExit();

                // RegAsm exits 0 even when it printed a warning (e.g. the expected
                // "unsigned assembly with /codebase" notice) - only a non-zero exit
                // code is treated as a real failure.
                if (process.ExitCode != 0)
                    throw new InvalidOperationException("RegAsm failed:" + Environment.NewLine + output + error);
            }
        }

        // Explorer-specific - RegAsm has no concept of shell copy-hook handlers,
        // so this one registry entry is still written directly.
        private static void RegisterCopyHook()
        {
            using (RegistryKey key = Registry.LocalMachine.CreateSubKey(
                @"SOFTWARE\Classes\Directory\shellex\CopyHookHandlers\" + HandlerName))
            {
                key.SetValue(null, "{" + Clsid + "}");
            }
        }

        private static void CreateStartMenuShortcut(string settingsExePath)
        {
            string startMenuPrograms =
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu), "Programs");
            string shortcutPath = Path.Combine(startMenuPrograms, "Folder Move Protector.lnk");

            // Late-bound COM call to the classic WScript.Shell shortcut writer -
            // avoids needing a type-library reference just for this one call.
            Type shellType = Type.GetTypeFromProgID("WScript.Shell");
            dynamic shell = Activator.CreateInstance(shellType);
            dynamic shortcut = shell.CreateShortcut(shortcutPath);
            shortcut.TargetPath = settingsExePath;
            shortcut.WorkingDirectory = InstallDir;
            shortcut.Description = "Configure which folders prompt for confirmation before being moved";
            shortcut.Save();
        }

        private static void Uninstall()
        {
            string installedHookDll = Path.Combine(InstallDir, "FolderMoveProtector.Hook.dll");
            if (File.Exists(installedHookDll))
                RunRegAsm(installedHookDll, "/unregister");

            using (RegistryKey classes = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Classes", writable: true))
            {
                try
                {
                    classes?.DeleteSubKeyTree(@"Directory\shellex\CopyHookHandlers\" + HandlerName);
                }
                catch (ArgumentException)
                {
                    // Key didn't exist - nothing to remove.
                }
            }

            string startMenuPrograms =
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu), "Programs");
            string shortcutPath = Path.Combine(startMenuPrograms, "Folder Move Protector.lnk");
            if (File.Exists(shortcutPath))
                File.Delete(shortcutPath);

            if (Directory.Exists(InstallDir))
                Directory.Delete(InstallDir, recursive: true);
        }
    }
}
