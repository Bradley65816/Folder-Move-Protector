using System;
using System.Runtime.InteropServices;

namespace FolderMoveProtector.Hook
{
    /// <summary>
    /// Managed projection of the classic Windows Shell "copy hook" interface
    /// (ICopyHookW in shlobj.h). Explorer calls this before it moves, copies,
    /// deletes, or renames a folder, and your return value tells it whether
    /// to go ahead.
    ///
    /// The Guid below is the real, fixed IID_ICopyHookW that Windows itself
    /// uses (000214FC-0000-0000-C000-000000000046) - not a value to change.
    /// </summary>
    [ComImport]
    [Guid("000214FC-0000-0000-C000-000000000046")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface ICopyHook
    {
        [PreserveSig]
        uint CopyCallback(
            IntPtr hwnd,
            uint wFunc,
            uint wFlags,
            [MarshalAs(UnmanagedType.LPWStr)] string pszSrcFile,
            uint dwSrcAttribs,
            [MarshalAs(UnmanagedType.LPWStr)] string pszDestFile,
            uint dwDestAttribs);
    }
}
