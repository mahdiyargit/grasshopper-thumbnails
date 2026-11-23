using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Threading;
using GrasshopperFilePreview;

static class StreamTests
{
    static void Expect<T>(Action action) where T : Exception
    {
        try
        {
            action();
        }
        catch (T) { return; }
        throw new Exception("Expected " + typeof(T).Name);
    }

    public static int Run()
    {
        var source = new MemoryComStream();
        using (var stream = new ComInput(source, CancellationToken.None))
        {
            var buffer = new byte[5];
            if (stream.Read(buffer, 1, 3) != 3 || buffer[0] != 0 || buffer[1] != 10 || buffer[3] != 30 || buffer[4] != 0)
                throw new Exception("Offset read copied outside its destination range.");
            if (stream.Length != 3 || stream.Seek(-2, SeekOrigin.End) != 1 || stream.ReadByte() != 20)
                throw new Exception("Stream seek/length contract failed.");
            int calls = source.ReadCalls;
            if (stream.Read(buffer, 0, 0) != 0 || source.ReadCalls != calls)
                throw new Exception("Empty reads must not call the COM source.");
            Expect<ArgumentOutOfRangeException>(() => stream.Read(buffer, 4, 2));
            Expect<ArgumentOutOfRangeException>(() => stream.Seek(0, (SeekOrigin)99));
        }
        Console.WriteLine("PASS COM stream offset, seek, bounds, and empty reads");

        var disposed = new ComInput(source, CancellationToken.None);
        disposed.Dispose();
        disposed.Dispose();
        if (disposed.CanRead || disposed.CanSeek)
            throw new Exception("Disposed stream still reports readable/seekable.");
        Expect<ObjectDisposedException>(() => disposed.ReadByte());
        Expect<ObjectDisposedException>(() => disposed.Seek(0, SeekOrigin.Begin));
        Expect<ObjectDisposedException>(() => { long ignored = disposed.Length; });
        // Adapter disposal must not release or dispose the borrowed COM stream.
        using (var second = new ComInput(source, CancellationToken.None))
            if (second.Length != 3)
                throw new Exception("Underlying stream was disposed.");
        Console.WriteLine("PASS COM stream disposal preserves its borrowed source");

        using (var cancel = new CancellationTokenSource())
        using (var stream = new ComInput(source, cancel.Token))
        {
            cancel.Cancel();
            Expect<OperationCanceledException>(() => stream.ReadByte());
            Expect<OperationCanceledException>(() => stream.Seek(0, SeekOrigin.Begin));
            Expect<OperationCanceledException>(() => { long ignored = stream.Length; });
        }
        Console.WriteLine("PASS COM stream cancellation applies to all reads");

        using (var stream = new ComInput(source, CancellationToken.None))
        {
            source.InvalidResponse = true;
            Expect<IOException>(() => stream.Read(new byte[2], 0, 2));
            Expect<IOException>(() => { long ignored = stream.Length; });
        }
        Console.WriteLine("PASS malformed COM response rejected");
        return 4;
    }

    sealed class MemoryComStream : IStream
    {
        readonly MemoryStream data = new MemoryStream(new byte[] { 10, 20, 30 });
        public int ReadCalls;
        public bool InvalidResponse;
        public void Read(byte[] buffer, int count, IntPtr result)
        {
            ReadCalls++;
            int read = data.Read(buffer, 0, count);
            Marshal.WriteInt32(result, InvalidResponse ? count + 1 : read);
        }
        public void Seek(long offset, int origin, IntPtr result)
        {
            Marshal.WriteInt64(result, data.Seek(offset, (SeekOrigin)origin));
        }
        public void Stat(out System.Runtime.InteropServices.ComTypes.STATSTG stat, int flags)
        {
            stat = new System.Runtime.InteropServices.ComTypes.STATSTG { cbSize = InvalidResponse ? -1 : data.Length };
        }
        public void Clone(out IStream stream)
        {
            throw new NotSupportedException();
        }
        public void Commit(int flags)
        {
            throw new NotSupportedException();
        }
        public void CopyTo(IStream stream, long count, IntPtr read, IntPtr written)
        {
            throw new NotSupportedException();
        }
        public void LockRegion(long offset, long count, int type)
        {
            throw new NotSupportedException();
        }
        public void Revert()
        {
            throw new NotSupportedException();
        }
        public void SetSize(long size)
        {
            throw new NotSupportedException();
        }
        public void UnlockRegion(long offset, long count, int type)
        {
            throw new NotSupportedException();
        }
        public void Write(byte[] buffer, int count, IntPtr result)
        {
            throw new NotSupportedException();
        }
    }
}
