using System;
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Threading;

namespace GrasshopperFilePreview
{
    [ComVisible(true), Guid("E357FCCD-A995-4576-B01F-234630154E96"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IThumbnailProvider
    {
        [PreserveSig] int GetThumbnail(uint size, out IntPtr bitmap, out uint alphaType);
    }

    [ComVisible(true), Guid("40E6D89F-3412-4FB6-9D32-1A56D4C4869B"), ClassInterface(ClassInterfaceType.None)]
    public sealed class ThumbnailHandler : IInitializeWithStream, IThumbnailProvider
    {
        IStream input;
        public void Initialize(IStream stream, uint mode)
        {
            if (input != null)
                throw new COMException("Already initialized", unchecked((int)0x800704DF));
            if (stream == null)
                throw new ArgumentNullException("stream");
            input = stream;
        }

        public int GetThumbnail(uint size, out IntPtr bitmap, out uint alphaType)
        {
            bitmap = IntPtr.Zero;
            alphaType = 0;
            if (size == 0)
                return unchecked((int)0x80070057); // E_INVALIDARG
            if (input == null)
                return unchecked((int)0x8000FFFF); // E_UNEXPECTED
            try
            {
                using (var cancel = new CancellationTokenSource())
                {
                    cancel.CancelAfter(ProductInfo.ReadTimeoutMilliseconds);
                    using (var source = new ComInput(input, cancel.Token))
                    using (var buffered = new BufferedStream(source, 65536))
                    using (var image = BitmapReader.Read(buffered, cancel.Token))
                    {
                        // Returning failure lets Explorer retain the ordinary file icon.
                        if (image == null)
                            return unchecked((int)0x80004005);
                        double scale = Math.Min(1.0, (double)size / Math.Max(image.Width, image.Height));
                        int width = Math.Max(1, (int)Math.Round(image.Width * scale));
                        int height = Math.Max(1, (int)Math.Round(image.Height * scale));
                        using (var resized = new Bitmap(width, height, PixelFormat.Format32bppPArgb))
                        {
                            using (var graphics = Graphics.FromImage(resized))
                            using (var attributes = new ImageAttributes())
                            {
                                graphics.CompositingMode = CompositingMode.SourceCopy;
                                graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
                                graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
                                attributes.SetWrapMode(WrapMode.TileFlipXY);
                                if (width == image.Width && height == image.Height)
                                    graphics.DrawImageUnscaled(image, 0, 0);
                                else
                                    graphics.DrawImage(image, new Rectangle(0, 0, width, height), 0, 0,
                                    image.Width, image.Height, GraphicsUnit.Pixel, attributes);
                            }
                            bitmap = ToDib(resized);
                        }
                        alphaType = 2; // WTSAT_ARGB, premultiplied BGRA pixels
                        return 0;
                    }
                }
            }
            catch (Exception e)
            {
                if (bitmap != IntPtr.Zero)
                {
                    DeleteObject(bitmap);
                    bitmap = IntPtr.Zero;
                }
                return e is OperationCanceledException ? unchecked((int)0x800705B4) : Marshal.GetHRForException(e);
            }
        }

        [StructLayout(LayoutKind.Sequential)]
        struct BitmapInfo
        {
            public uint Size;
            public int Width, Height;
            public ushort Planes, BitCount;
            public uint Compression, ImageSize;
            public int XPelsPerMeter, YPelsPerMeter;
            public uint ColorsUsed, ColorsImportant;
        }
        [DllImport("gdi32.dll", SetLastError = true)]
        static extern IntPtr CreateDIBSection(
            IntPtr dc, ref BitmapInfo info, uint usage, out IntPtr bits, IntPtr section, uint offset);
        [DllImport("gdi32.dll")] static extern bool DeleteObject(IntPtr bitmap);

        static IntPtr ToDib(Bitmap image)
        {
            var info = new BitmapInfo
            {
                Size = 40,
                Width = image.Width,
                Height = -image.Height,
                Planes = 1,
                BitCount = 32,
                ImageSize = (uint)(image.Width * image.Height * 4)
            };
            IntPtr bits;
            IntPtr handle = CreateDIBSection(IntPtr.Zero, ref info, 0, out bits, IntPtr.Zero, 0);
            if (handle == IntPtr.Zero)
                throw new Win32Exception(Marshal.GetLastWin32Error());
            try
            {
                var data = image.LockBits(new Rectangle(0, 0, image.Width, image.Height),
                    ImageLockMode.ReadOnly, PixelFormat.Format32bppPArgb);
                try
                {
                    var row = new byte[image.Width * 4];
                    for (int y = 0; y < image.Height; y++)
                    {
                        Marshal.Copy(IntPtr.Add(data.Scan0, y * data.Stride), row, 0, row.Length);
                        Marshal.Copy(row, 0, IntPtr.Add(bits, y * row.Length), row.Length);
                    }
                }
                finally { image.UnlockBits(data); }
                return handle; // Ownership transfers to the Shell; it calls DeleteObject.
            }
            catch { DeleteObject(handle); throw; }
        }
    }
}
