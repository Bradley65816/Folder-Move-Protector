using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32;

namespace FolderMoveProtector.Hook
{
    /// <summary>
    /// Single source of truth for the protected-folder list. FolderMoveHook
    /// (the Explorer copy-hook, running inside explorer.exe) and the Settings
    /// GUI (a completely separate program) both call into this class rather
    /// than each keeping their own copy of the list or the registry layout.
    ///
    /// Note that this is a plain .NET class with no COM attributes on it -
    /// the Settings GUI references this project normally, as an ordinary
    /// assembly reference, and calls these methods as regular in-process
    /// method calls. COM only enters the picture for the FolderMoveHook
    /// class itself, which is what Explorer specifically loads.
    /// </summary>
    public static class ProtectedRootsStore
    {
        private const string RegistryKeyPath = @"SOFTWARE\Folder Move Protector";
        private const string ValueName = "ProtectedRoots";

        /// <summary>
        /// Reads the current protected-folder list from HKEY_LOCAL_MACHINE.
        /// Returns an empty array if nothing has been configured yet. This is
        /// a single registry read - cheap enough to call on every move, which
        /// is exactly what FolderMoveHook does, so a change made in the GUI
        /// takes effect on the very next move with no Explorer restart needed.
        /// </summary>
        public static string[] GetProtectedRoots()
        {
            using (RegistryKey key = Registry.LocalMachine.OpenSubKey(RegistryKeyPath))
            {
                if (key?.GetValue(ValueName) is string[] values)
                    return values;
                return new string[0];
            }
        }

        /// <summary>
        /// Overwrites the protected-folder list. This writes under
        /// HKEY_LOCAL_MACHINE, so the calling process must be elevated -
        /// the Settings app's manifest requests that automatically.
        /// </summary>
        public static void SetProtectedRoots(IEnumerable<string> roots)
        {
            string[] values = roots
                .Where(r => !string.IsNullOrWhiteSpace(r))
                .Select(r => r.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();

            using (RegistryKey key = Registry.LocalMachine.CreateSubKey(RegistryKeyPath))
            {
                key.SetValue(ValueName, values, RegistryValueKind.MultiString);
            }
        }

        /// <summary>
        /// True if 'path' is one of the protected roots, or anything under one.
        /// </summary>
        public static bool IsUnderAnyProtectedRoot(string path)
        {
            string resolved = ResolveMappedDriveToUnc(path);
            if (string.IsNullOrEmpty(resolved))
                return false;

            // Trailing "\" on both sides anchors this as a real path-segment
            // match, so "\\server\share\FaxesArchive" can't wrongly match
            // a protected root of "\\server\share\Faxes".
            string normalized = resolved.TrimEnd('\\') + "\\";

            foreach (string root in GetProtectedRoots())
            {
                string normalizedRoot = root.TrimEnd('\\') + "\\";
                if (normalized.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            return false;
        }

        /// <summary>
        /// If 'path' starts with a mapped drive letter (e.g. Z:\Faxes), resolves
        /// it to the real UNC path that letter points to (\\server\share\Faxes).
        /// Left unchanged if it's already UNC, or isn't a recognizable path.
        /// This is what lets the protected-folder list be defined once, in UNC
        /// form, and still match no matter which drive letter (if any) a given
        /// user happens to have that share mapped to.
        /// </summary>
        public static string ResolveMappedDriveToUnc(string path)
        {
            if (string.IsNullOrEmpty(path) || path.Length < 2 || path[1] != ':')
                return path;

            var remoteName = new StringBuilder(260);
            int length = remoteName.Capacity;
            string driveRoot = path.Substring(0, 2); // e.g. "Z:"

            int result = WNetGetConnection(driveRoot, remoteName, ref length);
            return result == 0 ? remoteName.ToString() + path.Substring(2) : path;
        }

        [DllImport("mpr.dll", CharSet = CharSet.Unicode)]
        private static extern int WNetGetConnection(
            string localName, StringBuilder remoteName, ref int length);
    }
}
