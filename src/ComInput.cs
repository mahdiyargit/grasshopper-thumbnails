using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Threading;

namespace GrasshopperFilePreview
{
    internal sealed class ComInput : Stream
    {
        readonly IStream source;
        readonly CancellationToken token;
        IntPtr result;
        public ComInput(IStream source, CancellationToken token)
        {
            if (source == null)
                throw new ArgumentNullException("source");
            this.source = source;
            this.token = token;
            result = Marshal.AllocCoTaskMem(8);
        }
        protected override void Dispose(bool disposing)
        {
            if (result != IntPtr.Zero)
            {
                Marshal.FreeCoTaskMem(result);
                result = IntPtr.Zero;
            }
            base.Dispose(disposing);
        }

        // The caller owns the COM stream. Dispose only our native result buffer.
        void EnsureReadable()
        {
            if (result == IntPtr.Zero)
                throw new ObjectDisposedException("ComInput");
            token.ThrowIfCancellationRequested();
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            EnsureReadable();
            if (buffer == null)
                throw new ArgumentNullException("buffer");
            if (offset < 0 || count < 0 || offset > buffer.Length - count)
                throw new ArgumentOutOfRangeException("count");
            if (count == 0)
                return 0;
            byte[] target = offset == 0 ? buffer : new byte[count];
            Marshal.WriteInt32(result, 0);
            source.Read(target, count, result);
            int n = Marshal.ReadInt32(result);
            if (n < 0 || n > count)
                throw new IOException("Invalid stream response.");
            if (offset != 0)
                Buffer.BlockCopy(target, 0, buffer, offset, n);
            return n;
        }
        public override long Seek(long offset, SeekOrigin origin)
        {
            EnsureReadable();
            if (origin < SeekOrigin.Begin || origin > SeekOrigin.End)
                throw new ArgumentOutOfRangeException("origin");
            source.Seek(offset, (int)origin, result);
            return Marshal.ReadInt64(result);
        }
        public override long Position
        {
            get
            {
                return Seek(0, SeekOrigin.Current);
            }
            set
            {
                Seek(value, SeekOrigin.Begin);
            }
        }
        public override long Length
        {
            get
            {
                EnsureReadable();
                System.Runtime.InteropServices.ComTypes.STATSTG stat;
                source.Stat(out stat, 1);
                if (stat.cbSize < 0)
                    throw new IOException("Invalid stream length.");
                return stat.cbSize;
            }
        }
        public override bool CanRead
        {
            get
            {
                return result != IntPtr.Zero;
            }
        }
        public override bool CanSeek
        {
            get
            {
                return result != IntPtr.Zero;
            }
        }
        public override bool CanWrite
        {
            get
            {
                return false;
            }
        }
        public override void Flush()
        {
        }
        public override void SetLength(long value)
        {
            throw new NotSupportedException();
        }
        public override void Write(byte[] buffer, int offset, int count)
        {
            throw new NotSupportedException();
        }
    }
}
