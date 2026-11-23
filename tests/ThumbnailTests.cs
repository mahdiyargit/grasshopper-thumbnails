using System;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using GrasshopperFilePreview;

static class ThumbnailTests
{
    [StructLayout(LayoutKind.Sequential)]
    struct NativeBitmap
    {
        public int Type, Width, Height, WidthBytes;
        public ushort Planes, BitsPixel;
        public IntPtr Bits;
    }
    [DllImport("gdi32.dll", EntryPoint = "GetObjectW")]
    static extern int GetObject(IntPtr handle, int size, out NativeBitmap data);
    [DllImport("gdi32.dll")] static extern bool DeleteObject(IntPtr handle);
    [DllImport("shlwapi.dll", CharSet = CharSet.Unicode, PreserveSig = false)]
    static extern void SHCreateStreamOnFileEx(string path, uint mode, uint attributes, bool create, IStream template, out IStream stream);

    public static int Run(string fixtureFolder)
    {
        int passed = 0;
        foreach (string extension in new[] { ".gh", ".ghx" })
        {
            IStream stream;
            SHCreateStreamOnFileEx(Path.Combine(fixtureFolder, "Embedded bitmap" + extension), 0x20, 0, false, null, out stream);
            try
            {
                var handler = new ThumbnailHandler();
                handler.Initialize(stream, 0);
                foreach (uint size in new uint[] { 16, 96, 256, 1024 })
                {
                    IntPtr bitmap;
                    uint alpha;
                    Marshal.ThrowExceptionForHR(handler.GetThumbnail(size, out bitmap, out alpha));
                    try
                    {
                        NativeBitmap data;
                        if (GetObject(bitmap, Marshal.SizeOf(typeof(NativeBitmap)), out data) == 0)
                            throw new Exception("Invalid HBITMAP.");
                        if (data.BitsPixel != 32 || data.Bits == IntPtr.Zero || alpha != 2)
                            throw new Exception("Expected a 32-bit alpha DIB section.");
                        double scale = Math.Min(1, size / 300.0);
                        if (data.Width != (int)Math.Round(300 * scale) || data.Height != (int)Math.Round(200 * scale))
                            throw new Exception("Thumbnail aspect ratio, size, or no-upscaling contract failed.");
                        if (size == 1024)
                            using (var file = File.OpenRead(Path.Combine(fixtureFolder, "Embedded bitmap" + extension)))
                            using (var expected = BitmapReader.Read(file))
                            using (var actual = Image.FromHbitmap(bitmap))
                                for (int y = 0; y < expected.Height; y++)
                                    for (int x = 0; x < expected.Width; x++)
                                        if ((expected.GetPixel(x, y).ToArgb() & 0xffffff) != (actual.GetPixel(x, y).ToArgb() & 0xffffff))
                                            throw new Exception("Native thumbnail pixel differs at " + x + "," + y + ": expected=" + expected.GetPixel(x, y).ToArgb().ToString("X8") + " actual=" + actual.GetPixel(x, y).ToArgb().ToString("X8"));
                    }
                    finally { DeleteObject(bitmap); }
                    Console.WriteLine("PASS native thumbnail " + extension + " size " + size);
                    passed++;
                }
                IntPtr invalid;
                uint invalidAlpha;
                if (handler.GetThumbnail(0, out invalid, out invalidAlpha) >= 0 || invalid != IntPtr.Zero)
                    throw new Exception("Zero size must fail.");
                Console.WriteLine("PASS zero thumbnail size rejected " + extension);
                passed++;
            }
            finally { Marshal.ReleaseComObject(stream); }
            SHCreateStreamOnFileEx(Path.Combine(fixtureFolder, "No bitmap" + extension), 0x20, 0, false, null, out stream);
            try
            {
                var handler = new ThumbnailHandler();
                handler.Initialize(stream, 0);
                IntPtr bitmap;
                uint alpha;
                if (handler.GetThumbnail(256, out bitmap, out alpha) >= 0 || bitmap != IntPtr.Zero || alpha != 0)
                    throw new Exception("Missing bitmap must fail without returning a handle.");
                Console.WriteLine("PASS missing thumbnail falls back to icon " + extension);
                passed++;
            }
            finally { Marshal.ReleaseComObject(stream); }
        }
        CheckAlpha();
        Console.WriteLine("PASS native premultiplied alpha and top-down orientation");
        return passed + 1;
    }

    static void CheckAlpha()
    {
        string file = Path.Combine(Path.GetTempPath(), "GrasshopperFilePreview-" + Guid.NewGuid().ToString("N") + ".ghx");
        try
        {
            using (var image = new Bitmap(2, 2))
            using (var png = new MemoryStream())
            {
                image.SetPixel(0, 0, Color.FromArgb(128, 200, 100, 50));
                image.SetPixel(1, 0, Color.Transparent);
                image.SetPixel(0, 1, Color.Red);
                image.SetPixel(1, 1, Color.Blue);
                image.Save(png, System.Drawing.Imaging.ImageFormat.Png);
                File.WriteAllText(file, "<Archive><chunks><chunk name='Thumbnail'><item name='Thumbnail'><bitmap>" +
                    Convert.ToBase64String(png.ToArray()) + "</bitmap></item></chunk></chunks></Archive>");
            }
            IStream stream;
            SHCreateStreamOnFileEx(file, 0x20, 0, false, null, out stream);
            try
            {
                var handler = new ThumbnailHandler();
                handler.Initialize(stream, 0);
                IntPtr bitmap;
                uint alpha;
                Marshal.ThrowExceptionForHR(handler.GetThumbnail(256, out bitmap, out alpha));
                try
                {
                    NativeBitmap data;
                    if (GetObject(bitmap, Marshal.SizeOf(typeof(NativeBitmap)), out data) == 0 || data.Width != 2 || data.Height != 2 || alpha != 2)
                        throw new Exception("Unexpected alpha bitmap layout.");
                    var pixels = new byte[16];
                    Marshal.Copy(data.Bits, pixels, 0, pixels.Length);
                    byte[] expected = { 25, 50, 100, 128, 0, 0, 0, 0, 0, 0, 255, 255, 255, 0, 0, 255 };
                    for (int i = 0; i < pixels.Length; i++)
                        if (Math.Abs(pixels[i] - expected[i]) > 1)
                            throw new Exception("Premultiplied BGRA/orientation mismatch at byte " + i);
                }
                finally { DeleteObject(bitmap); }
            }
            finally { Marshal.ReleaseComObject(stream); }
        }
        finally { File.Delete(file); }
    }
}
