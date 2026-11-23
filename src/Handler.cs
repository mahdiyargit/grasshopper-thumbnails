using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;

namespace GrasshopperFilePreview
{
    [ComVisible(true), Guid("9A67F1FF-DC27-4647-8BBD-81789C9BB7FC"), ClassInterface(ClassInterfaceType.None)]
    public sealed class Handler : IPreviewHandler, IInitializeWithStream, IObjectWithSite, IOleWindow, IPreviewHandlerVisuals
    {
        IntPtr parent;
        Rect rect;
        IStream input;
        object site;
        PreviewSession session;
        Color background = Color.White, foreground = Color.FromArgb(75, 75, 75);
        public void Initialize(IStream stream, uint mode)
        {
            if (stream == null)
                throw new ArgumentNullException("stream");
            if (input != null)
                throw new COMException("Already initialized", unchecked((int)0x800704DF));
            input = stream;
        }
        void Update()
        {
            if (session != null)
                session.Update(parent, rect, background, foreground);
        }
        public void SetWindow(IntPtr hwnd, ref Rect bounds)
        {
            parent = hwnd;
            rect = bounds;
            Update();
        }
        public void SetRect(ref Rect bounds)
        {
            rect = bounds;
            Update();
        }
        public void DoPreview()
        {
            if (input == null || parent == IntPtr.Zero)
                throw new InvalidOperationException("Preview is not initialized.");
            if (session != null)
                return; // Do not seek the same stream concurrently on repeated calls.
            session = new PreviewSession(input, parent, rect, background, foreground);
            session.Start();
        }
        public void Unload()
        {
            if (session != null)
            {
                session.Stop();
                session = null;
            }
            input = null;
            parent = IntPtr.Zero;
        }
        public void SetFocus()
        {
            if (session != null)
                session.Focus();
        }
        public void QueryFocus(out IntPtr hwnd)
        {
            hwnd = session == null ? IntPtr.Zero : session.FocusedWindow;
        }
        public int TranslateAccelerator(IntPtr message)
        {
            return 1;
        }
        public void GetWindow(out IntPtr hwnd)
        {
            hwnd = session == null ? IntPtr.Zero : session.Window;
            if (hwnd == IntPtr.Zero)
                throw new COMException(session == null ? "No preview window" : session.Error ?? "Preview is starting", unchecked((int)0x80004005));
        }
        public int ContextSensitiveHelp(bool enterMode)
        {
            return unchecked((int)0x80004001);
        }
        public void SetSite(object value)
        {
            site = value;
        }
        public void GetSite(ref Guid iid, out IntPtr result)
        {
            result = IntPtr.Zero;
            if (site == null)
                throw new COMException("No site", unchecked((int)0x80004005));
            IntPtr ptr = Marshal.GetIUnknownForObject(site);
            try
            {
                Marshal.ThrowExceptionForHR(Marshal.QueryInterface(ptr, ref iid, out result));
            }
            finally { Marshal.Release(ptr); }
        }
        public void SetBackgroundColor(uint color)
        {
            background = ColorTranslator.FromWin32((int)color);
            Update();
        }
        public void SetTextColor(uint color)
        {
            foreground = ColorTranslator.FromWin32((int)color);
            Update();
        }
        public void SetFont(IntPtr logfont)
        {
        }
    }
}
