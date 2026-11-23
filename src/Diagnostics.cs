using System;
using System.Diagnostics;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Threading;
using System.Windows.Forms;
using GrasshopperFilePreview;

[StructLayout(LayoutKind.Sequential)]
struct ThumbnailSize
{
    public int Width, Height;
}
[ComImport, Guid("BCC18B79-BA16-442F-80C4-8A59C30C463B"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
interface IShellItemImageFactory
{
    void GetImage(ThumbnailSize size, uint flags, out IntPtr bitmap);
}

static class Diagnostics
{
    [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = false)]
    static extern void SHCreateItemFromParsingName(string path, IntPtr bindContext, ref Guid iid,
        [MarshalAs(UnmanagedType.Interface)] out IShellItemImageFactory item);
    [DllImport("gdi32.dll")] static extern bool DeleteObject(IntPtr bitmap);
    [DllImport("ole32.dll")] static extern int CoCreateInstance(ref Guid cls, IntPtr outer, uint context, ref Guid iid, [MarshalAs(UnmanagedType.IUnknown)] out object result);
    [DllImport("shlwapi.dll", CharSet = CharSet.Unicode, PreserveSig = false)]
    static extern void SHCreateStreamOnFileEx(string path, uint mode, uint attr, bool create, IStream template, out IStream stream);
    [DllImport("user32.dll")] static extern IntPtr GetWindow(IntPtr parent, uint relation);
    [DllImport("user32.dll")] static extern IntPtr GetParent(IntPtr child);
    [DllImport("user32.dll")] static extern bool IsWindow(IntPtr window);
    [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr window, out Rect rect);
    [STAThread]
    static int Main(string[] args)
    {
        try
        {
            if (args.Length == 1 && args[0] == "--install")
            {
                Registration.Install();
                Console.WriteLine("Installed.");
                return 0;
            }
            if (args.Length == 1 && args[0] == "--uninstall")
            {
                Registration.Uninstall();
                Console.WriteLine("Removed.");
                return 0;
            }
            if (args.Length == 1 && args[0] == "--status")
            {
                Console.WriteLine(Registration.IsInstalled() || Registration.IsInstalled(true) ? "Installed" : "Not installed");
                return 0;
            }
            if (args.Length < 2)
            {
                Console.WriteLine("--read FILE | --extract FILE PNG | --probe FILE | --thumbnail FILE [PNG] | --install | --uninstall | --status");
                return 2;
            }
            string path = Path.GetFullPath(args[1]);
            if ((args[0] == "--extract" && args.Length != 3) ||
                (args[0] == "--thumbnail" && args.Length != 2 && args.Length != 3) ||
                ((args[0] == "--read" || args[0] == "--probe") && args.Length != 2))
                return 2;
            if (args[0] == "--thumbnail")
            {
                Guid factoryId = typeof(IShellItemImageFactory).GUID;
                IShellItemImageFactory factory;
                SHCreateItemFromParsingName(path, IntPtr.Zero, ref factoryId, out factory);
                IntPtr bitmap = IntPtr.Zero;
                try
                {
                    // THUMBNAILONLY prevents a generic icon from counting as a successful test.
                    factory.GetImage(new ThumbnailSize { Width = 256, Height = 256 }, 0x8, out bitmap);
                    using (var image = System.Drawing.Image.FromHbitmap(bitmap))
                    {
                        Console.WriteLine("Windows Shell thumbnail: " + image.Width + " x " + image.Height);
                        if (args.Length == 3)
                            image.Save(args[2], ImageFormat.Png);
                    }
                }
                finally { if (bitmap != IntPtr.Zero) DeleteObject(bitmap); Marshal.ReleaseComObject(factory); }
                return 0;
            }
            if (args[0] == "--read" || args[0] == "--extract")
            {
                using (var cancel = new CancellationTokenSource(ProductInfo.ReadTimeoutMilliseconds))
                using (var stream = File.OpenRead(path))
                using (var bitmap = BitmapReader.Read(stream, cancel.Token))
                {
                    Console.WriteLine(bitmap == null ? "No embedded bitmap" : "Embedded bitmap: " + bitmap.Width + " x " + bitmap.Height);
                    if (args[0] == "--extract")
                    {
                        if (bitmap == null)
                            throw new InvalidDataException("Nothing was extracted: the file has no embedded bitmap.");
                        bitmap.Save(args[2], ImageFormat.Png);
                    }
                }
                return 0;
            }
            if (args[0] != "--probe")
                return 2;
            Probe(path);
            return 0;
        }
        catch (Exception e) { Console.Error.WriteLine("0x" + e.HResult.ToString("X8") + ": " + e.Message); return 1; }
    }
    static void WaitFor(Func<bool> condition, string failure)
    {
        var clock = Stopwatch.StartNew();
        while (!condition())
        {
            if (clock.ElapsedMilliseconds > 10000)
                throw new TimeoutException(failure);
            Application.DoEvents();
            System.Threading.Thread.Sleep(15);
        }
    }

    static void Probe(string path)
    {
        Console.WriteLine("Activating registered out-of-process COM preview handler...");
        Guid cls = new Guid(Registration.Id), iid = typeof(IPreviewHandler).GUID;
        object instance;
        Marshal.ThrowExceptionForHR(CoCreateInstance(ref cls, IntPtr.Zero, 4, ref iid, out instance));
        try
        {
            var handler = (IPreviewHandler)instance;
            using (var form = new Form { Text = "Grasshopper preview COM test", ClientSize = new System.Drawing.Size(600, 400), ShowInTaskbar = false })
            {
                IntPtr parent = form.Handle;
                for (int cycle = 1; cycle <= 3; cycle++)
                {
                    IStream stream;
                    SHCreateStreamOnFileEx(path, 0x20, 0, false, null, out stream);
                    try
                    {
                        ((IInitializeWithStream)instance).Initialize(stream, 0);
                        var rect = new Rect { Right = 600, Bottom = 400 };
                        handler.SetWindow(parent, ref rect);
                        handler.DoPreview();
                        handler.DoPreview(); // Repeated requests must not start a second reader/window.
                        WaitFor(() => GetWindow(parent, 5) != IntPtr.Zero, "No preview child window appeared.");
                        IntPtr hwnd;
                        ((IOleWindow)instance).GetWindow(out hwnd);
                        if (GetParent(hwnd) != parent || GetWindow(hwnd, 2) != IntPtr.Zero)
                            throw new InvalidOperationException("Expected exactly one correctly parented preview window.");
                        rect.Right = 350;
                        handler.SetRect(ref rect);
                        WaitFor(() => { Rect actual; return GetWindowRect(hwnd, out actual) && actual.Right - actual.Left == 350; }, "Preview did not resize.");
                        handler.Unload();
                        WaitFor(() => !IsWindow(hwnd), "Preview window survived Unload.");
                        Console.WriteLine("PASS preview lifecycle " + cycle + ": create, repeat, resize, unload");
                    }
                    finally { handler.Unload(); Marshal.ReleaseComObject(stream); }
                }
            }
        }
        finally { Marshal.ReleaseComObject(instance); }
    }
}
