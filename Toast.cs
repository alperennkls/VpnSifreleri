using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Microsoft.Win32;

namespace VpnSifreleri
{
    /// <summary>
    /// Short notice above the tray. It never takes focus, so the app being pasted into stays active,
    /// and it does not pile up in the Windows notification center the way balloon tips do.
    /// </summary>
    internal sealed class Toast : Form
    {
        private const int WS_EX_TOPMOST = 0x00000008;
        private const int WS_EX_TOOLWINDOW = 0x00000080;
        private const int WS_EX_NOACTIVATE = 0x08000000;
        private const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
        private const int DWMWCP_ROUND = 2;
        private const TextFormatFlags TextFlags = TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine;

        // Layout in 96-DPI pixels
        private const int PadX = 16, PadY = 14, BadgeSize = 24, BadgeGap = 12, LineGap = 2, MinWidth = 260, ScreenGap = 12;
        private const int DefaultVisibleMs = 1800;

        private static Toast current;

        private readonly string caption;
        private readonly string text;
        private readonly bool success;
        private readonly float scale;
        private readonly Font captionFont = new Font("Segoe UI", 9f);
        private readonly Font textFont = new Font("Segoe UI Semibold", 12f);
        private readonly Size captionSize;
        private readonly Color textColor, captionColor, badgeColor;
        private readonly Timer closeTimer = new Timer();

        internal Toast(string caption, string text, bool success, int visibleMs = DefaultVisibleMs)
        {
            this.caption = caption;
            this.text = text;
            this.success = success;

            using (Graphics screen = Graphics.FromHwnd(IntPtr.Zero))
                scale = screen.DpiX / 96f;

            bool light = UsesLightTheme();
            BackColor = light ? Color.FromArgb(249, 249, 249) : Color.FromArgb(44, 44, 44);
            textColor = light ? Color.FromArgb(26, 26, 26) : Color.White;
            captionColor = light ? Color.FromArgb(96, 96, 96) : Color.FromArgb(200, 200, 200);
            badgeColor = success
                ? (light ? Color.FromArgb(15, 123, 15) : Color.FromArgb(108, 203, 95))
                : (light ? Color.FromArgb(157, 93, 0) : Color.FromArgb(252, 225, 0));

            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            DoubleBuffered = true;

            captionSize = TextRenderer.MeasureText(caption, captionFont, Size.Empty, TextFlags);
            Size textSize = TextRenderer.MeasureText(text, textFont, Size.Empty, TextFlags);
            int width = Math.Max(Px(MinWidth), Px(PadX + BadgeSize + BadgeGap + PadX) + Math.Max(captionSize.Width, textSize.Width));
            int height = Px(PadY * 2 + LineGap) + captionSize.Height + textSize.Height;
            ClientSize = new Size(width, height);

            Rectangle area = Screen.PrimaryScreen.WorkingArea;
            Location = new Point(area.Right - width - Px(ScreenGap), area.Bottom - height - Px(ScreenGap));

            closeTimer.Interval = visibleMs;
            closeTimer.Tick += delegate { Close(); };
        }

        /// <summary>Shows a notice, replacing the previous one if it is still on screen.</summary>
        public static void Popup(string caption, string text, bool success, int visibleMs = DefaultVisibleMs)
        {
            if (current != null && !current.IsDisposed) current.Close();
            current = new Toast(caption, text, success, visibleMs);
            current.Show();
        }

        protected override bool ShowWithoutActivation
        {
            get { return true; }
        }

        // Topmost goes in the window style: setting the TopMost property would activate the window
        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.ExStyle |= WS_EX_TOPMOST | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE;
                return cp;
            }
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            int preference = DWMWCP_ROUND; // Windows 11 rounded corners, ignored on older versions
            DwmSetWindowAttribute(Handle, DWMWA_WINDOW_CORNER_PREFERENCE, ref preference, sizeof(int));
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            closeTimer.Start();
        }

        protected override void OnMouseClick(MouseEventArgs e)
        {
            base.OnMouseClick(e);
            Close();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            float badge = Px(BadgeSize);
            RectangleF circle = new RectangleF(Px(PadX), (ClientSize.Height - badge) / 2f, badge, badge);
            using (SolidBrush fill = new SolidBrush(badgeColor))
                g.FillEllipse(fill, circle);

            using (Pen mark = new Pen(BackColor, Math.Max(2f, badge / 11f)))
            using (SolidBrush dot = new SolidBrush(BackColor))
            {
                mark.StartCap = LineCap.Round;
                mark.EndCap = LineCap.Round;
                mark.LineJoin = LineJoin.Round;
                if (success)
                {
                    g.DrawLines(mark, new[] { At(circle, 0.29f, 0.52f), At(circle, 0.44f, 0.67f), At(circle, 0.72f, 0.36f) });
                }
                else
                {
                    g.DrawLine(mark, At(circle, 0.5f, 0.27f), At(circle, 0.5f, 0.56f));
                    PointF p = At(circle, 0.5f, 0.74f);
                    float r = mark.Width * 0.6f;
                    g.FillEllipse(dot, p.X - r, p.Y - r, r * 2, r * 2);
                }
            }

            int x = Px(PadX + BadgeSize + BadgeGap);
            int y = Px(PadY);
            TextRenderer.DrawText(g, caption, captionFont, new Point(x, y), captionColor, TextFlags);
            TextRenderer.DrawText(g, text, textFont, new Point(x, y + captionSize.Height + Px(LineGap)), textColor, TextFlags);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                closeTimer.Dispose();
                captionFont.Dispose();
                textFont.Dispose();
            }
            base.Dispose(disposing);
        }

        private int Px(int logical)
        {
            return (int)Math.Round(logical * scale);
        }

        private static PointF At(RectangleF r, float fx, float fy)
        {
            return new PointF(r.X + r.Width * fx, r.Y + r.Height * fy);
        }

        // The taskbar follows the Windows mode setting, so the notice next to it does too
        private static bool UsesLightTheme()
        {
            using (RegistryKey key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize"))
            {
                object value = key == null ? null : key.GetValue("SystemUsesLightTheme");
                return value is int && (int)value != 0;
            }
        }

        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
    }
}
