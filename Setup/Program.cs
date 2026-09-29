using System;
using System.IO;
using System.Reflection;
using System.Windows.Forms;
using Microsoft.Win32;

namespace FolderMoveProtector.Setup
{
    internal static class Program
    {
        // Must match the [Guid("...")] attribute on FolderMoveHook in Hook\FolderMoveHook.cs
        private const string Clsid = "678B7C88-AFC9-43A9-83D2-EF0CC0855E58";
        private const string ProgId = "FolderMoveProtector.Hook.FolderMoveHook";
        private const string ClassFullName = "FolderMoveProtector.Hook.FolderMoveHook";
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

            RegisterComServer(installedHookDll);
            RegisterCopyHook();
            CreateStartMenuShortcut(installedSettingsExe);
        }

        // Writes the same registry entries RegAsm.exe /codebase would have written,
        // directly under HKLM so it's guaranteed machine-wide with no external tool.
        private static void RegisterComServer(string dllPath)
        {
            // Read the assembly's own name/version rather than hardcoding it, so
            // this keeps working if the Hook project's version number ever changes.
            string assemblyFullName = AssemblyName.GetAssemblyName(dllPath).FullName;

            using (RegistryKey classes = Registry.LocalMachine.CreateSubKey(@"SOFTWARE\Classes"))
            {
                using (RegistryKey progIdKey = classes.CreateSubKey(ProgId))
                {
                    progIdKey.SetValue(null, ProgId);
                    using (RegistryKey progIdClsidKey = progIdKey.CreateSubKey("CLSID"))
                        progIdClsidKey.SetValue(null, "{" + Clsid + "}");
                }

                using (RegistryKey clsidKey = classes.CreateSubKey(@"CLSID\{" + Clsid + "}"))
                {
                    clsidKey.SetValue(null, ProgId);

                    using (RegistryKey inprocKey = clsidKey.CreateSubKey("InprocServer32"))
                    {
                        inprocKey.SetValue(null, "mscoree.dll");
                        inprocKey.SetValue("ThreadingModel", "Both");
                        inprocKey.SetValue("Class", ClassFullName);
                        inprocKey.SetValue("Assembly", assemblyFullName);
                        inprocKey.SetValue("RuntimeVersion", "v4.0.30319");
                        inprocKey.SetValue("CodeBase", new Uri(dllPath).AbsoluteUri);
                    }

                    using (RegistryKey clsidProgIdKey = clsidKey.CreateSubKey("ProgId"))
                        clsidProgIdKey.SetValue(null, ProgId);
                }
            }
        }

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
            DeleteClassesKey(@"Directory\shellex\CopyHookHandlers\" + HandlerName);
            DeleteClassesKey(@"CLSID\{" + Clsid + "}");
            DeleteClassesKey(ProgId);

            string startMenuPrograms =
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu), "Programs");
            string shortcutPath = Path.Combine(startMenuPrograms, "Folder Move Protector.lnk");
            if (File.Exists(shortcutPath))
                File.Delete(shortcutPath);

            if (Directory.Exists(InstallDir))
                Directory.Delete(InstallDir, recursive: true);
        }

        private static void DeleteClassesKey(string subKeyPath)
        {
            using (RegistryKey classes = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Classes", writable: true))
            {
                try
                {
                    classes?.DeleteSubKeyTree(subKeyPath);
                }
                catch (ArgumentException)
                {
                    // Key didn't exist - nothing to remove.
                }
            }
        }
    }
}
