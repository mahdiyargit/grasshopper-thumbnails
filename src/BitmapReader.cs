using System;
using System.Drawing;
using System.IO;
using System.IO.Compression;
using System.Xml;
using System.Threading;

namespace GrasshopperFilePreview
{
    // Reads only GH1's serialized thumbnail; never loads Grasshopper or executes a definition.
    public static class BitmapReader
    {
        public const int MaxArchive = 256 * 1024 * 1024;
        public const int MaxImage = 16 * 1024 * 1024;
        static readonly byte[] Marker = CreateMarker();
        static readonly int[] Prefix = CreatePrefix();

        static byte[] CreateMarker()
        {
            using (var s = new MemoryStream())
            using (var w = new BinaryWriter(s))
            {
                w.Write("Thumbnail");
                w.Write(-1);
                w.Write(1);
                w.Write(0);
                w.Write("Thumbnail");
                w.Write(-1);
                w.Write(37); // GH drawing bitmap
                return s.ToArray();
            }
        }
        static int[] CreatePrefix()
        {
            var p = new int[Marker.Length];
            for (int i = 1, j = 0; i < Marker.Length; i++)
            {
                while (j > 0 && Marker[i] != Marker[j])
                    j = p[j - 1];
                if (Marker[i] == Marker[j])
                    j++;
                p[i] = j;
            }
            return p;
        }
        public static Bitmap Read(Stream source)
        {
            return Read(source, CancellationToken.None);
        }

        public static Bitmap Read(Stream source, CancellationToken cancellation)
        {
            if (source == null)
                throw new ArgumentNullException("source");
            cancellation.ThrowIfCancellationRequested();
            if (!source.CanRead)
                throw new ArgumentException("A readable stream is required.");
            if (!source.CanSeek)
                throw new ArgumentException("A seekable stream is required.");
            if (source.Length > MaxArchive)
                throw new InvalidDataException("File exceeds the 256 MB preview limit.");
            source.Position = 0;
            int a = source.ReadByte(), b = source.ReadByte();
            source.Position = 0;
            bool xml = a == '<' || a == 0xef && b == 0xbb || a == 0xff && b == 0xfe ||
                a == 0xfe && b == 0xff || a == 0 && b == '<' || a == ' ' || a == '\r' || a == '\n' || a == '\t';
            byte[] bytes;
            if (xml)
            {
                try
                {
                    bytes = ReadXml(source, cancellation);
                }
                catch (XmlException) { source.Position = 0; bytes = ReadBinary(source, cancellation); }
            }
            else
                bytes = ReadBinary(source, cancellation);
            cancellation.ThrowIfCancellationRequested();
            return bytes == null ? null : Decode(bytes);
        }
        static Bitmap Decode(byte[] bytes)
        {
            byte[] signature = { 137, 80, 78, 71, 13, 10, 26, 10, 0, 0, 0, 13, 73, 72, 68, 82 };
            if (bytes.Length < 33)
                throw new InvalidDataException("The embedded bitmap is incomplete.");
            for (int i = 0; i < signature.Length; i++)
                if (bytes[i] != signature[i])
                    throw new InvalidDataException("The embedded bitmap is not a supported PNG image.");
            long w = Dimension(bytes, 16), h = Dimension(bytes, 20);
            if (w < 1 || h < 1 || w > 8192 || h > 8192 || w * h > 16000000)
                throw new InvalidDataException("Embedded bitmap dimensions exceed the preview limit.");
            using (var s = new MemoryStream(bytes, false))
            using (var img = Image.FromStream(s, false, true))
                return new Bitmap(img);
        }
        static long Dimension(byte[] bytes, int start)
        {
            long n = 0;
            for (int i = start; i < start + 4; i++)
                n = n * 256 + bytes[i];
            return n;
        }
        static byte[] ReadXml(Stream source, CancellationToken cancellation)
        {
            var settings = new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Prohibit,
                XmlResolver = null,
                MaxCharactersInDocument = MaxArchive,
                CloseInput = false
            };
            using (var reader = XmlReader.Create(source, settings))
            {
                if (reader.MoveToContent() != XmlNodeType.Element || reader.Name != "Archive")
                    throw new InvalidDataException("This is not a Grasshopper XML archive.");
                while (reader.Read())
                {
                    cancellation.ThrowIfCancellationRequested();
                    if (reader.NodeType != XmlNodeType.Element || reader.Depth != 2 || reader.Name != "chunk" ||
                        reader.GetAttribute("name") != "Thumbnail")
                        continue;
                    using (var section = reader.ReadSubtree())
                    {
                        while (section.Read())
                        {
                            cancellation.ThrowIfCancellationRequested();
                            if (section.NodeType != XmlNodeType.Element || section.Name != "item" ||
                                section.GetAttribute("name") != "Thumbnail")
                                continue;
                            if (!section.ReadToDescendant("bitmap"))
                                return null;
                            using (var output = new MemoryStream())
                            {
                                var buffer = new byte[8192];
                                int n;
                                while ((n = section.ReadElementContentAsBase64(buffer, 0, buffer.Length)) > 0)
                                {
                                    cancellation.ThrowIfCancellationRequested();
                                    if (output.Length + n > MaxImage)
                                        throw new InvalidDataException("Embedded bitmap is too large.");
                                    output.Write(buffer, 0, n);
                                }
                                return output.ToArray();
                            }
                        }
                    }
                }
                return null;
            }
        }
        static byte[] ReadBinary(Stream source, CancellationToken cancellation)
        {
            // A streaming KMP search keeps memory bounded, crosses read boundaries, and allows
            // other archive chunks after Thumbnail. This is a bounded GH1 thumbnail scanner,
            // not a general structural validator for the Grasshopper archive format.
            using (var inflate = new DeflateStream(source, CompressionMode.Decompress, true))
            using (var input = new BufferedStream(inflate, 65536))
            {
                int match = 0, total = 0, value;
                while ((value = input.ReadByte()) >= 0)
                {
                    if (++total > MaxArchive)
                        throw new InvalidDataException("Expanded archive exceeds the 256 MB preview limit.");
                    if ((total & 4095) == 0)
                        cancellation.ThrowIfCancellationRequested();
                    while (match > 0 && value != Marker[match])
                        match = Prefix[match - 1];
                    if (value == Marker[match])
                        match++;
                    if (match != Marker.Length)
                        continue;
                    var lengthBytes = ReadExactly(input, 4, cancellation);
                    int length = BitConverter.ToInt32(lengthBytes, 0);
                    if (length <= 0 || length > MaxImage || (long)total + 4 + length > MaxArchive)
                        throw new InvalidDataException("Invalid embedded bitmap length.");
                    return ReadExactly(input, length, cancellation);
                }
                return null;
            }
        }
        static byte[] ReadExactly(Stream stream, int length, CancellationToken cancellation)
        {
            var bytes = new byte[length];
            for (int offset = 0; offset < length;)
            {
                cancellation.ThrowIfCancellationRequested();
                int n = stream.Read(bytes, offset, Math.Min(65536, length - offset));
                if (n == 0)
                    throw new InvalidDataException("The embedded bitmap is truncated.");
                offset += n;
            }
            return bytes;
        }
    }
}
