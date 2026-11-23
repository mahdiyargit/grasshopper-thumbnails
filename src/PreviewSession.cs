using System;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace GrasshopperFilePreview
{
    internal sealed class PreviewSession
    {
        readonly object gate = new object();
        readonly IStream stream;
        readonly CancellationTokenSource cancel = new CancellationTokenSource();
        IntPtr parent, window;
        Rect rect;
        Color background, foreground;
        bool stopped, focus, disposed;
        uint uiThread;
        public string Error;
        [DllImport("user32.dll")] static extern IntPtr SetParent(IntPtr child, IntPtr parent);
        [DllImport("user32.dll")] static extern bool IsWindow(IntPtr window);
        [DllImport("user32.dll")] static extern IntPtr GetParent(IntPtr window);
        [DllImport("kernel32.dll")] static extern uint GetCurrentThreadId();
        [StructLayout(LayoutKind.Sequential)]
        struct GuiThreadInfo
        {
            public uint Size, Flags;
            public IntPtr Active, Focus, Capture, MenuOwner, MoveSize, Caret;
            public Rect CaretRect;
        }
        [DllImport("user32.dll")] static extern bool GetGUIThreadInfo(uint thread, ref GuiThreadInfo info);
        public PreviewSession(IStream stream, IntPtr parent, Rect rect, Color background, Color foreground)
        {
            this.stream = stream;
            Update(parent, rect, background, foreground);
        }
        public IntPtr Window
        {
            get
            {
                lock (gate)
                    return window;
            }
        }
        public void Update(IntPtr parent, Rect rect, Color background, Color foreground)
        {
            lock (gate)
            {
                this.parent = parent;
                this.rect = rect;
                this.background = background;
                this.foreground = foreground;
            }
        }
        public void Focus()
        {
            lock (gate)
                focus = true;
        }
        public void Stop()
        {
            lock (gate)
            {
                stopped = true;
                if (!disposed)
                    cancel.Cancel();
            }
        }
        public IntPtr FocusedWindow
        {
            get
            {
                uint thread;
                lock (gate)
                    thread = uiThread;
                var info = new GuiThreadInfo { Size = (uint)Marshal.SizeOf(typeof(GuiThreadInfo)) };
                return thread != 0 && GetGUIThreadInfo(thread, ref info) ? info.Focus : IntPtr.Zero;
            }
        }
        public void Start()
        {
            var thread = new Thread(Run) { IsBackground = true, Name = "Grasshopper bitmap preview UI" };
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
        }
        void Run()
        {
            Task<Bitmap> pending = null;
            try
            {
                Application.SetUnhandledExceptionMode(UnhandledExceptionMode.ThrowException, true);
                IntPtr owner;
                lock (gate)
                {
                    if (stopped)
                        return;
                    owner = parent;
                    uiThread = GetCurrentThreadId();
                }
                if (!IsWindow(owner))
                    return;
                cancel.CancelAfter(ProductInfo.ReadTimeoutMilliseconds);
                var token = cancel.Token;
                pending = Task.Run(delegate
                {
                    using (var source = new ComInput(stream, token))
                    using (var buffered = new BufferedStream(source, 65536))
                        return BitmapReader.Read(buffered, token);
                }, token);
                using (var surface = new PreviewSurface())
                using (var context = new ApplicationContext())
                using (var timer = new System.Windows.Forms.Timer { Interval = 40 })
                {
                    // Create on our own STA, then attach the existing HWND to the host.
                    // prevhost's COM apartment does not guarantee a WinForms message loop.
                    var hwnd = surface.Handle;
                    SetParent(hwnd, owner);
                    if (GetParent(hwnd) != owner)
                        throw new InvalidOperationException("Could not attach the preview window.");
                    lock (gate)
                        window = hwnd;
                    surface.Show();
                    timer.Tick += delegate
                    {
                        Rect bounds;
                        Color back, fore;
                        IntPtr nextParent;
                        bool shouldStop, shouldFocus;
                        // Never hold the gate across a window call. Cross-process parenting
                        // can send synchronous messages back to the COM host.
                        lock (gate)
                        {
                            shouldStop = stopped;
                            bounds = rect;
                            nextParent = parent;
                            back = background;
                            fore = foreground;
                            shouldFocus = focus;
                            focus = false;
                        }
                        if (shouldStop || !IsWindow(nextParent))
                        {
                            context.ExitThread();
                            return;
                        }
                        if (owner != nextParent)
                        {
                            SetParent(hwnd, nextParent);
                            if (GetParent(hwnd) != nextParent)
                                throw new InvalidOperationException("Could not reparent the preview window.");
                            owner = nextParent;
                        }
                        surface.SetBounds(bounds.Left, bounds.Top, Math.Max(0, bounds.Right - bounds.Left), Math.Max(0, bounds.Bottom - bounds.Top));
                        surface.BackColor = back;
                        surface.ForeColor = fore;
                        if (shouldFocus)
                            surface.Focus();
                        if (pending == null || !pending.IsCompleted)
                            return;
                        var done = pending;
                        pending = null;
                        try
                        {
                            surface.Picture = done.GetAwaiter().GetResult();
                            surface.Caption = "This file has no embedded bitmap preview.\nSave a thumbnail in Grasshopper to display it here.";
                        }
                        catch (OperationCanceledException) { surface.Caption = "The embedded bitmap took too long to read."; }
                        catch (Exception) { surface.Caption = "Could not read the embedded bitmap.\nThe file may be damaged or exceed the preview limits."; }
                        surface.Invalidate();
                    };
                    timer.Start();
                    Application.Run(context);
                    timer.Stop();
                }
            }
            catch (Exception e) { Error = e.Message; }
            finally
            {
                lock (gate)
                {
                    window = IntPtr.Zero;
                    uiThread = 0;
                    cancel.Cancel();
                    cancel.Dispose();
                    disposed = true;
                }
                if (pending != null)
                    pending.ContinueWith(Discard, TaskScheduler.Default);
            }
        }
        static void Discard(Task<Bitmap> done)
        {
            if (done.Status == TaskStatus.RanToCompletion && done.Result != null)
                done.Result.Dispose();
            else if (done.IsFaulted)
                GC.KeepAlive(done.Exception);
        }
    }
}
