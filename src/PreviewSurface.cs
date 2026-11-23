using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace GrasshopperFilePreview
{
    public sealed class PreviewSurface : Control
    {
        Bitmap picture;
        public string Caption = "Loading embedded bitmap...";

        // Ownership transfers to the surface; replacing or closing it disposes the image.
        public Bitmap Picture
        {
            get
            {
                return picture;
            }
            set
            {
                if (ReferenceEquals(picture, value))
                    return;
                if (picture != null)
                    picture.Dispose();
                picture = value;
                Invalidate();
            }
        }

        public PreviewSurface()
        {
            DoubleBuffered = true;
            BackColor = Color.White;
            ForeColor = Color.FromArgb(75, 75, 75);
            AccessibleName = "Grasshopper embedded bitmap preview";
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            if (Picture == null)
            {
                var bounds = ClientRectangle;
                bounds.Inflate(-16, -16);
                TextRenderer.DrawText(e.Graphics, Caption, Font, bounds, ForeColor,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.WordBreak);
                return;
            }
            double scale = Math.Min((double)Width / Picture.Width, (double)Height / Picture.Height);
            int w = (int)(Picture.Width * scale), h = (int)(Picture.Height * scale);
            if (w < 1 || h < 1)
                return;
            e.Graphics.InterpolationMode = scale == 1 ? InterpolationMode.NearestNeighbor : InterpolationMode.HighQualityBicubic;
            e.Graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
            e.Graphics.DrawImage(Picture, (Width - w) / 2, (Height - h) / 2, w, h);
        }
        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            Invalidate();
        }
        protected override void Dispose(bool disposing)
        {
            if (disposing && picture != null)
            {
                picture.Dispose();
                picture = null;
            }
            base.Dispose(disposing);
        }
    }
}
