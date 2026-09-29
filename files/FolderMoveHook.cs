using System;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace FolderMoveProtector.Hook
{
    // Values of SHFILEOPSTRUCT.wFunc, from shellapi.h.
    internal static class FileOp
    {
        public const uint FO_MOVE = 0x0001;
        public const uint FO_COPY = 0x0002;
        public const uint FO_DELETE = 0x0003;
        public const uint FO_RENAME = 0x0004;
    }

    // Relevant SHFILEOPSTRUCT.fFlags bit.
    internal static class FileOpFlags
    {
        public const uint FOF_SILENT = 0x0004;
    }

    // The Shell reads CopyCallback's return value as one of these.
    internal static class HookResult
    {
        public const uint IDYES = 6;     // allow the operation
        public const uint IDNO = 7;      // block just this item; continue with any others in the batch
        public const uint IDCANCEL = 2;  // block this item and cancel any remaining pending operations
    }

    /// <summary>
    /// Registered under HKEY_CLASSES_ROOT\Directory\shellex\CopyHookHandlers,
    /// this only ever gets invoked for folders, never plain files.
    ///
    /// The [Guid] below is this class's own CLSID - generate your own with
    /// [System.Guid]::NewGuid() and keep Install.ps1's $Clsid in sync with it.
    /// </summary>
    [ComVisible(true)]
    [Guid("678B7C88-AFC9-43A9-83D2-EF0CC0855E58")]
    [ClassInterface(ClassInterfaceType.None)]
    [ProgId("FolderMoveProtector.Hook.FolderMoveHook")]
    public class FolderMoveHook : ICopyHook
    {
        public uint CopyCallback(
            IntPtr hwnd,
            uint wFunc,
            uint wFlags,
            string pszSrcFile,
            uint dwSrcAttribs,
            string pszDestFile,
            uint dwDestAttribs)
        {
            try
            {
                if (wFunc != FileOp.FO_MOVE)
                    return HookResult.IDYES;

                if ((wFlags & FileOpFlags.FOF_SILENT) != 0)
                    return HookResult.IDYES;

                // Reads the current list from the registry every time - see
                // ProtectedRootsStore for why that's deliberate and cheap.
                bool srcProtected = ProtectedRootsStore.IsUnderAnyProtectedRoot(pszSrcFile);
                bool destProtected = ProtectedRootsStore.IsUnderAnyProtectedRoot(pszDestFile);
                if (!srcProtected && !destProtected)
                    return HookResult.IDYES;

                string message =
                    "Are you sure you want to move this folder?" + Environment.NewLine +
                    Environment.NewLine +
                    "From: " + pszSrcFile + Environment.NewLine +
                    "To:   " + pszDestFile;

                DialogResult choice = MessageBox.Show(
                    new ExplorerWindow(hwnd),
                    message,
                    "Confirm Folder Move",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Warning,
                    MessageBoxDefaultButton.Button2);

                return choice == DialogResult.Yes ? HookResult.IDYES : HookResult.IDNO;
            }
            catch
            {
                // This runs inside Explorer's own process - never let an
                // exception escape and risk taking Explorer down with it.
                return HookResult.IDYES;
            }
        }
    }

    /// <summary>
    /// MessageBox.Show wants an IWin32Window; Explorer hands us a raw HWND,
    /// so this just wraps it.
    /// </summary>
    internal sealed class ExplorerWindow : IWin32Window
    {
        public ExplorerWindow(IntPtr handle) => Handle = handle;
        public IntPtr Handle { get; }
    }
}
