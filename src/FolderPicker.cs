using System;
using System.IO;
using System.Runtime.InteropServices;

namespace Deskmate
{
    // The Explorer-style folder dialog; null when cancelled
    static class FolderPicker
    {
        const uint PickFolders = 0x20, ForceFileSystem = 0x40, PathMustExist = 0x800;
        const uint FileSysPath = 0x80058000;
        const int Cancelled = unchecked((int)0x800704C7);

        [ComImport, Guid("DC1C5A9C-E88A-4dde-A5A1-60F82A20AEF7")]
        class FileOpenDialog { }

        // Vtable order up to GetResult; nothing past it is called
        [ComImport, Guid("42f85136-db7e-439c-85f1-e4075d135fc8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IFileDialog
        {
            [PreserveSig] int Show(IntPtr parent);
            void SetFileTypes(uint count, IntPtr specs);
            void SetFileTypeIndex(uint index);
            void GetFileTypeIndex(out uint index);
            void Advise(IntPtr sink, out uint cookie);
            void Unadvise(uint cookie);
            void SetOptions(uint fos);
            void GetOptions(out uint fos);
            void SetDefaultFolder(IShellItem item);
            void SetFolder(IShellItem item);
            void GetFolder(out IShellItem item);
            void GetCurrentSelection(out IShellItem item);
            void SetFileName([MarshalAs(UnmanagedType.LPWStr)] string name);
            void GetFileName([MarshalAs(UnmanagedType.LPWStr)] out string name);
            void SetTitle([MarshalAs(UnmanagedType.LPWStr)] string title);
            void SetOkButtonLabel([MarshalAs(UnmanagedType.LPWStr)] string text);
            void SetFileNameLabel([MarshalAs(UnmanagedType.LPWStr)] string text);
            void GetResult(out IShellItem item);
        }

        [ComImport, Guid("43826D1E-E718-42EE-BC55-A1E261C37BFE"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IShellItem
        {
            void BindToHandler(IntPtr pbc, [In] ref Guid bhid, [In] ref Guid riid, out IntPtr ppv);
            void GetParent(out IShellItem parent);
            void GetDisplayName(uint sigdn, out IntPtr name);
        }

        [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = false)]
        static extern void SHCreateItemFromParsingName(string path, IntPtr pbc, [In] ref Guid riid, out IShellItem item);

        public static string Show(IntPtr owner, string title, string start)
        {
            IFileDialog d = null;
            try
            {
                d = (IFileDialog)new FileOpenDialog();
                uint fos;
                d.GetOptions(out fos);
                d.SetOptions(fos | PickFolders | ForceFileSystem | PathMustExist);
                if (!string.IsNullOrEmpty(title)) d.SetTitle(title);
                if (!string.IsNullOrEmpty(start) && Directory.Exists(start))
                {
                    try
                    {
                        var iid = typeof(IShellItem).GUID;
                        IShellItem at;
                        SHCreateItemFromParsingName(start, IntPtr.Zero, ref iid, out at);
                        d.SetFolder(at);
                        Marshal.ReleaseComObject(at);
                    }
                    catch (Exception) { }
                }
                int hr = d.Show(owner);
                if (hr == Cancelled) return null;
                if (hr != 0) Marshal.ThrowExceptionForHR(hr);
                IShellItem item;
                d.GetResult(out item);
                try
                {
                    IntPtr p;
                    item.GetDisplayName(FileSysPath, out p);
                    try { return Marshal.PtrToStringUni(p); }
                    finally { Marshal.FreeCoTaskMem(p); }
                }
                finally { Marshal.ReleaseComObject(item); }
            }
            catch (Exception ex)
            {
                Log.Write("folder picker: " + ex.Message);
                return null;
            }
            finally { if (d != null) Marshal.ReleaseComObject(d); }
        }
    }
}
