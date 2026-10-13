using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;

namespace VpnSifreleri
{
    /// <summary>
    /// Draws the app icon in code: the tray gets an exact-size icon at any DPI, and build.ps1 asks for
    /// a multi-size .ico to embed in the .exe, so there is no image file to maintain.
    /// </summary>
    internal static class AppIcon
    {
        private static readonly int[] IcoSizes = { 16, 20, 24, 32, 40, 48, 64, 256 };
        // The Icon class does not read PNG frames on every .NET Framework version, so the window icon stops at 64 px
        private static readonly int[] WindowSizes = { 16, 20, 24, 32, 40, 48, 64 };
        private static readonly Color Top = Color.FromArgb(16, 185, 129);
        private static readonly Color Bottom = Color.FromArgb(4, 120, 87);

        public static Icon ForTray(int size)
        {
            using (MemoryStream ico = new MemoryStream(BuildIco(new[] { size })))
                return new Icon(ico, size, size);
        }

        /// <summary>Cockpit title bar and taskbar button; Windows picks the size it needs.</summary>
        public static Icon ForWindow()
        {
            using (MemoryStream ico = new MemoryStream(BuildIco(WindowSizes)))
                return new Icon(ico);
        }

        public static void SaveIco(string path)
        {
            File.WriteAllBytes(path, BuildIco(IcoSizes));
        }

        /// <summary>Green rounded tile with a white padlock.</summary>
        public static Bitmap Draw(int size)
        {
            Bitmap bmp = new Bitmap(size, size, PixelFormat.Format32bppArgb);
            using (Graphics g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                float s = size;

                RectangleF tile = new RectangleF(0, 0, s, s);
                using (GraphicsPath shape = RoundedRect(tile, s * 0.22f))
                using (LinearGradientBrush fill = new LinearGradientBrush(RectangleF.Inflate(tile, 1, 1), Top, Bottom, LinearGradientMode.Vertical))
                    g.FillPath(fill, shape);

                // Shackle: an upside-down U whose legs disappear into the body
                RectangleF arc = new RectangleF(s * 0.33f, s * 0.16f, s * 0.34f, s * 0.34f);
                float legTop = arc.Top + arc.Height / 2f;
                using (Pen shackle = new Pen(Color.White, Math.Max(1.5f, s * 0.1f)))
                using (GraphicsPath u = new GraphicsPath())
                {
                    u.AddLine(arc.Left, s * 0.5f, arc.Left, legTop);
                    u.AddArc(arc, 180, 180);
                    u.AddLine(arc.Right, legTop, arc.Right, s * 0.5f);
                    g.DrawPath(shackle, u);
                }

                RectangleF body = new RectangleF(s * 0.21f, s * 0.46f, s * 0.58f, s * 0.40f);
                using (GraphicsPath block = RoundedRect(body, s * 0.08f))
                    g.FillPath(Brushes.White, block);

                // Keyhole in the darker green; the slot under it only where there are pixels for it
                using (SolidBrush hole = new SolidBrush(Bottom))
                {
                    float d = Math.Max(1.5f, s * 0.12f);
                    float cy = body.Top + body.Height * 0.38f;
                    g.FillEllipse(hole, s / 2f - d / 2f, cy - d / 2f, d, d);
                    if (size >= 24) g.FillRectangle(hole, s / 2f - d * 0.2f, cy, d * 0.4f, body.Height * 0.32f);
                }
            }
            return bmp;
        }

        private static GraphicsPath RoundedRect(RectangleF r, float radius)
        {
            float d = radius * 2;
            GraphicsPath path = new GraphicsPath();
            path.AddArc(r.X, r.Y, d, d, 180, 90);
            path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }

        // ICO container: small sizes as 32-bit DIBs (read by every Windows API), 256 px as PNG
        private static byte[] BuildIco(int[] sizes)
        {
            List<byte[]> images = new List<byte[]>();
            foreach (int size in sizes)
                using (Bitmap bmp = Draw(size))
                    images.Add(size >= 256 ? ToPng(bmp) : ToDib(bmp));

            using (MemoryStream ms = new MemoryStream())
            using (BinaryWriter w = new BinaryWriter(ms))
            {
                w.Write((short)0);
                w.Write((short)1);                        // type: icon
                w.Write((short)sizes.Length);
                int offset = 6 + 16 * sizes.Length;
                for (int i = 0; i < sizes.Length; i++)
                {
                    byte dimension = (byte)(sizes[i] >= 256 ? 0 : sizes[i]); // 0 means 256
                    w.Write(dimension);
                    w.Write(dimension);
                    w.Write((byte)0);                     // palette size
                    w.Write((byte)0);                     // reserved
                    w.Write((short)1);                    // planes
                    w.Write((short)32);                   // bits per pixel
                    w.Write(images[i].Length);
                    w.Write(offset);
                    offset += images[i].Length;
                }
                foreach (byte[] image in images) w.Write(image);
                w.Flush();
                return ms.ToArray();
            }
        }

        private static byte[] ToDib(Bitmap bmp)
        {
            int width = bmp.Width, height = bmp.Height;
            int maskStride = (width + 31) / 32 * 4;
            using (MemoryStream ms = new MemoryStream())
            using (BinaryWriter w = new BinaryWriter(ms))
            {
                w.Write(40);                              // BITMAPINFOHEADER size
                w.Write(width);
                w.Write(height * 2);                      // color rows + mask rows
                w.Write((short)1);
                w.Write((short)32);
                w.Write(0);                               // BI_RGB
                w.Write(width * height * 4 + maskStride * height);
                w.Write(0L);                              // resolution
                w.Write(0L);                              // palette counts
                for (int y = height - 1; y >= 0; y--)     // rows bottom-up, BGRA
                    for (int x = 0; x < width; x++)
                    {
                        Color c = bmp.GetPixel(x, y);
                        w.Write(new[] { c.B, c.G, c.R, c.A });
                    }
                for (int y = height - 1; y >= 0; y--)     // AND mask, 1 = transparent
                {
                    byte[] row = new byte[maskStride];
                    for (int x = 0; x < width; x++)
                        if (bmp.GetPixel(x, y).A == 0) row[x >> 3] |= (byte)(0x80 >> (x & 7));
                    w.Write(row);
                }
                w.Flush();
                return ms.ToArray();
            }
        }

        private static byte[] ToPng(Bitmap bmp)
        {
            using (MemoryStream ms = new MemoryStream())
            {
                bmp.Save(ms, ImageFormat.Png);
                return ms.ToArray();
            }
        }
    }
}
