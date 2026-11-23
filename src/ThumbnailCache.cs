using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace GrasshopperFilePreview
{
    internal static class ThumbnailCache
    {
        // Use only Windows' Thumbnails cleanup handler, never other Disk Cleanup categories.
        // A handler cannot repair a cached failure itself: Windows does not call it again.
        internal static void Reset()
        {
            using (var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\VolumeCaches\Thumbnail Cache"))
            {
                if (key == null)
                    throw new InvalidOperationException("Windows' thumbnail cache cleanup component is unavailable.");
                var cleaner = (IEmptyVolumeCache2)Activator.CreateInstance(Type.GetTypeFromCLSID(new Guid("889900C3-59F3-4C2F-AE21-A409EA01E605")));
                IntPtr name = IntPtr.Zero, description = IntPtr.Zero, button = IntPtr.Zero;
                bool initialized = false;
                uint flags = 0;
                try
                {
                    string volume = Path.GetPathRoot(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)).TrimEnd('\\');
                    Marshal.ThrowExceptionForHR(cleaner.InitializeEx(key.Handle.DangerousGetHandle(), volume,
                        "Thumbnail Cache", out name, out description, out button, ref flags));
                    initialized = true;
                    var progress = new ThumbnailCleanupProgress();
                    ulong bytes;
                    Marshal.ThrowExceptionForHR(cleaner.GetSpaceUsed(out bytes, progress));
                    Marshal.ThrowExceptionForHR(cleaner.Purge(ulong.MaxValue, progress));
                    GC.KeepAlive(progress);
                }
                finally
                {
                    // Deactivate returns flags for Disk Cleanup's UI; do not apply them to the registry.
                    if (initialized)
                        cleaner.Deactivate(out flags);
                    Marshal.ReleaseComObject(cleaner);
                    Marshal.FreeCoTaskMem(name);
                    Marshal.FreeCoTaskMem(description);
                    Marshal.FreeCoTaskMem(button);
                }
            }
        }

        // Flatten inherited IEmptyVolumeCache methods to preserve the native vtable order.
        [ComImport, Guid("02B7E3BA-4DB3-11D2-B2D9-00C04F8EEC8C"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IEmptyVolumeCache2
        {
            [PreserveSig] int Initialize(IntPtr key, [MarshalAs(UnmanagedType.LPWStr)] string volume, out IntPtr name, out IntPtr description, ref uint flags);
            [PreserveSig] int GetSpaceUsed(out ulong used, IThumbnailCleanupProgress progress);
            [PreserveSig] int Purge(ulong space, IThumbnailCleanupProgress progress);
            [PreserveSig] int ShowProperties(IntPtr window);
            [PreserveSig] int Deactivate(out uint flags);
            [PreserveSig] int InitializeEx(IntPtr key, [MarshalAs(UnmanagedType.LPWStr)] string volume,
                [MarshalAs(UnmanagedType.LPWStr)] string keyName, out IntPtr name, out IntPtr description, out IntPtr button, ref uint flags);
        }
    }

    [ComVisible(true), Guid("6E793361-73C6-11D0-8469-00AA00442901"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IThumbnailCleanupProgress
    {
        [PreserveSig] int ScanProgress(ulong used, uint flags, [MarshalAs(UnmanagedType.LPWStr)] string status);
        [PreserveSig] int PurgeProgress(ulong freed, ulong remaining, uint flags, [MarshalAs(UnmanagedType.LPWStr)] string status);
    }

    [ComVisible(true), ClassInterface(ClassInterfaceType.None)]
    public sealed class ThumbnailCleanupProgress : IThumbnailCleanupProgress
    {
        readonly Stopwatch clock = Stopwatch.StartNew();
        // Windows calls these during cleanup; abort an unexpectedly long operation.
        public int ScanProgress(ulong used, uint flags, string status)
        {
            return clock.ElapsedMilliseconds < 30000 ? 0 : unchecked((int)0x80004004);
        }
        public int PurgeProgress(ulong freed, ulong remaining, uint flags, string status)
        {
            return ScanProgress(freed, flags, status);
        }
    }
}
