using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Windows.Forms;

namespace PortWatch
{
    // 32-bit Per-Pixel Alpha Mini Always-On-Top Network Speed Pill (0px flush + Unlock & Drag)
    internal sealed class NetHudForm : Form
    {
        private static readonly Color ColBg = Color.FromArgb(12, 16, 23);
        private static readonly Color ColBorder = Color.FromArgb(56, 70, 92);
        private static readonly Color ColCyan = Color.FromArgb(56, 189, 248);
        private static readonly Color ColEmerald = Color.FromArgb(52, 211, 153);
        private static readonly Color ColAmber = Color.FromArgb(251, 191, 36);
        private static readonly Color ColMuted = Color.FromArgb(148, 163, 184);

        private PortWatchSettings _cfg = new PortWatchSettings();
        private PortSnapshot _snap = new PortSnapshot();
        private float _scale = 1.0f;
        private bool _dragUnlocked;
        private bool _dragging;
        private Point _dragMouse;
        private Point _dragWin;
        private int _lastW;
        private int _lastH;

        public event Action<int, int> CustomPositionChanged;
        public event Action LockRequested;
        public bool DragUnlocked { get { return _dragUnlocked; } }

        public NetHudForm()
        {
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            StartPosition = FormStartPosition.Manual;
        }

        protected override bool ShowWithoutActivation { get { return true; } }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.ExStyle |= WinNative.WS_EX_TOPMOST
                            | WinNative.WS_EX_LAYERED
                            | WinNative.WS_EX_NOACTIVATE
                            | WinNative.WS_EX_TOOLWINDOW;
                if (!_dragUnlocked) cp.ExStyle |= WinNative.WS_EX_TRANSPARENT;
                return cp;
            }
        }

        public void SetDragUnlocked(bool unlocked)
        {
            _dragUnlocked = unlocked;
            if (!unlocked)
            {
                _dragging = false;
                try { Capture = false; } catch { }
            }
            Cursor = unlocked ? Cursors.SizeAll : Cursors.Default;
            ApplyStyle();
            if (IsHandleCreated) UpdateHud(_cfg, _snap, _scale);
        }

        private void ApplyStyle()
        {
            if (!IsHandleCreated) return;
            try
            {
                long ex = WinNative.GetWindowLongPtr(Handle, WinNative.GWL_EXSTYLE).ToInt64();
                if (_dragUnlocked) ex &= ~(long)WinNative.WS_EX_TRANSPARENT;
                else ex |= (long)WinNative.WS_EX_TRANSPARENT;
                WinNative.SetWindowLongPtr(Handle, WinNative.GWL_EXSTYLE, new IntPtr(ex));
                WinNative.SetWindowPos(Handle, WinNative.HWND_TOPMOST, 0, 0, 0, 0,
                    WinNative.SWP_NOMOVE | WinNative.SWP_NOSIZE | WinNative.SWP_NOACTIVATE | WinNative.SWP_FRAMECHANGED);
            }
            catch { }
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            ApplyStyle();
            UpdateHud(_cfg, _snap, _scale);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (!_dragUnlocked) return;
            if (e.Button == MouseButtons.Right) { if (LockRequested != null) LockRequested(); return; }
            if (e.Button == MouseButtons.Left)
            {
                _dragging = true;
                _dragMouse = Cursor.Position;
                _dragWin = Location;
                Capture = true;
            }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (!_dragging || !_dragUnlocked) return;
            Point cur = Cursor.Position;
            Point pt = ClampVirtual(_dragWin.X + (cur.X - _dragMouse.X), _dragWin.Y + (cur.Y - _dragMouse.Y), _lastW > 0 ? _lastW : Width, _lastH > 0 ? _lastH : Height);
            WinNative.SetWindowPos(Handle, WinNative.HWND_TOPMOST, pt.X, pt.Y, 0, 0, WinNative.SWP_NOSIZE | WinNative.SWP_NOACTIVATE);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (_dragging && e.Button == MouseButtons.Left)
            {
                _dragging = false;
                Capture = false;
                Point pos = Location;
                _cfg.NetHudCustomPos = true;
                _cfg.NetHudX = pos.X;
                _cfg.NetHudY = pos.Y;
                if (CustomPositionChanged != null) CustomPositionChanged(pos.X, pos.Y);
            }
        }

        public static Point ClampVirtual(int x, int y, int w, int h)
        {
            Rectangle vs = SystemInformation.VirtualScreen;
            if (vs.Width <= 0 || vs.Height <= 0) vs = new Rectangle(0, 0, 1920, 1080);
            if (x < vs.Left) x = vs.Left;
            if (y < vs.Top) y = vs.Top;
            if (x + w > vs.Right) x = Math.Max(vs.Left, vs.Right - w);
            if (y + h > vs.Bottom) y = Math.Max(vs.Top, vs.Bottom - h);
            return new Point(x, y);
        }

        public void UpdateHud(PortWatchSettings cfg, PortSnapshot snap, float scale)
        {
            _cfg = cfg ?? new PortWatchSettings();
            _snap = snap ?? new PortSnapshot();
            _scale = scale > 0.5f ? scale : 1.0f;

            using (Bitmap bmp = RenderBitmap(_cfg, _snap, _scale, _dragUnlocked))
            {
                _lastW = bmp.Width;
                _lastH = bmp.Height;
                Point dst;
                if (_dragging) dst = Location;
                else if (_cfg.NetHudCustomPos) dst = ClampVirtual(_cfg.NetHudX, _cfg.NetHudY, bmp.Width, bmp.Height);
                else
                {
                    Screen sc = Screen.PrimaryScreen;
                    Rectangle b = sc != null ? sc.Bounds : new Rectangle(0, 0, 1920, 1080);
                    int mg = (int)Math.Round(Math.Max(0, _cfg.NetHudMargin) * _scale);
                    dst = new Point(Math.Max(b.Left, b.Right - bmp.Width - mg), Math.Max(b.Top, b.Top + mg));
                }

                if (IsHandleCreated)
                {
                    PushBitmap(bmp, dst);
                    if (Visible)
                        WinNative.SetWindowPos(Handle, WinNative.HWND_TOPMOST, 0, 0, 0, 0,
                            WinNative.SWP_NOMOVE | WinNative.SWP_NOSIZE | WinNative.SWP_NOACTIVATE);
                }
                else
                {
                    SetBounds(dst.X, dst.Y, bmp.Width, bmp.Height);
                }
            }
        }

        private void PushBitmap(Bitmap bmp, Point dst)
        {
            IntPtr sDc = WinNative.GetDC(IntPtr.Zero);
            if (sDc == IntPtr.Zero) return;
            IntPtr mDc = WinNative.CreateCompatibleDC(sDc);
            IntPtr hBmp = IntPtr.Zero;
            IntPtr oldBmp = IntPtr.Zero;
            try
            {
                if (mDc == IntPtr.Zero) return;
                hBmp = bmp.GetHbitmap(Color.FromArgb(0));
                oldBmp = WinNative.SelectObject(mDc, hBmp);
                WinNative.SIZE sz = new WinNative.SIZE(bmp.Width, bmp.Height);
                WinNative.POINT src = new WinNative.POINT(0, 0);
                WinNative.POINT pt = new WinNative.POINT(dst.X, dst.Y);
                WinNative.BLENDFUNCTION bf = new WinNative.BLENDFUNCTION();
                bf.BlendOp = WinNative.AC_SRC_OVER;
                bf.SourceConstantAlpha = 255;
                bf.AlphaFormat = WinNative.AC_SRC_ALPHA;
                WinNative.UpdateLayeredWindow(Handle, sDc, ref pt, ref sz, mDc, ref src, 0, ref bf, WinNative.ULW_ALPHA);
            }
            finally
            {
                if (oldBmp != IntPtr.Zero && mDc != IntPtr.Zero) WinNative.SelectObject(mDc, oldBmp);
                if (hBmp != IntPtr.Zero) WinNative.DeleteObject(hBmp);
                if (mDc != IntPtr.Zero) WinNative.DeleteDC(mDc);
                WinNative.ReleaseDC(IntPtr.Zero, sDc);
            }
        }

        public static Bitmap RenderBitmap(PortWatchSettings cfg, PortSnapshot snap, float scale, bool unlocked)
        {
            float sf = scale > 0.5f ? scale : 1.0f;
            PortWatchSettings safeCfg = cfg ?? new PortWatchSettings();
            PortSnapshot safeSnap = snap ?? new PortSnapshot();
            NetSpeedSnapshot safeSpeed = safeSnap.Speed ?? new NetSpeedSnapshot();
            string downTxt = "↓ " + NetworkEngine.FormatSpeed(safeSpeed.DownBytesPerSec);
            string upTxt = "↑ " + NetworkEngine.FormatSpeed(safeSpeed.UpBytesPerSec);
            string portTxt = safeSnap.ListeningCount + " " + Loc.T(safeCfg.Language, "PORTS", "PORT", "端口");

            using (Font fVal = new Font("Segoe UI", 8.8f * (96f / 72f) * sf, FontStyle.Bold, GraphicsUnit.Pixel))
            using (Font fLbl = new Font(Loc.FontFamily(safeCfg.Language), 7.6f * (96f / 72f) * sf, FontStyle.Bold, GraphicsUnit.Pixel))
            {
                float w1, w2, w3;
                using (Bitmap tmp = new Bitmap(8, 8))
                using (Graphics tg = Graphics.FromImage(tmp))
                using (StringFormat typo = new StringFormat(StringFormat.GenericTypographic))
                {
                    typo.FormatFlags |= StringFormatFlags.NoWrap;
                    w1 = tg.MeasureString(downTxt, fVal, PointF.Empty, typo).Width + 4f * sf;
                    w2 = tg.MeasureString(upTxt, fVal, PointF.Empty, typo).Width + 4f * sf;
                    w3 = tg.MeasureString(portTxt, fLbl, PointF.Empty, typo).Width + 4f * sf;
                }

                int w = Math.Max((int)Math.Ceiling(180f * sf), (int)Math.Ceiling(w1 + w2 + w3 + 46f * sf));
                int h = (int)Math.Ceiling(30f * sf);

                Bitmap bmp = new Bitmap(w, h, PixelFormat.Format32bppPArgb);
                using (Graphics g = Graphics.FromImage(bmp))
                {
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
                    g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                    g.Clear(Color.Transparent);

                    int alpha = Math.Max(140, Math.Min(245, (int)Math.Round((safeCfg.NetHudOpacity / 100.0) * 238.0)));
                    RectangleF rc = new RectangleF(0.5f, 0.5f, w - 1.5f, h - 1.5f);
                    using (GraphicsPath gp = Rounded(rc, 8f * sf))
                    using (SolidBrush bg = new SolidBrush(Color.FromArgb(alpha, ColBg)))
                    using (Pen bd = new Pen(unlocked ? Color.FromArgb(255, ColCyan) : Color.FromArgb(Math.Min(255, alpha + 20), ColBorder), unlocked ? 1.8f : 1.1f))
                    {
                        g.FillPath(bg, gp);
                        g.DrawPath(bd, gp);
                    }

                    using (StringFormat sfT = new StringFormat(StringFormat.GenericTypographic))
                    using (SolidBrush bDown = new SolidBrush(Color.FromArgb(255, ColEmerald)))
                    using (SolidBrush bUp = new SolidBrush(Color.FromArgb(255, ColCyan)))
                    using (SolidBrush bPort = new SolidBrush(Color.FromArgb(240, ColMuted)))
                    using (Pen sep = new Pen(Color.FromArgb(160, 46, 58, 76), 1f))
                    {
                        sfT.Alignment = StringAlignment.Near;
                        sfT.LineAlignment = StringAlignment.Center;
                        sfT.FormatFlags |= StringFormatFlags.NoWrap;

                        float x = 11f * sf;
                        g.DrawString(downTxt, fVal, bDown, new RectangleF(x, 0, w1, h), sfT);
                        x += w1 + 10f * sf;
                        g.DrawLine(sep, x - 5f * sf, 6f * sf, x - 5f * sf, h - 6f * sf);

                        g.DrawString(upTxt, fVal, bUp, new RectangleF(x, 0, w2, h), sfT);
                        x += w2 + 10f * sf;
                        g.DrawLine(sep, x - 5f * sf, 6f * sf, x - 5f * sf, h - 6f * sf);

                        g.DrawString(portTxt, fLbl, bPort, new RectangleF(x, 0, w3, h), sfT);
                    }
                }
                return bmp;
            }
        }

        private static GraphicsPath Rounded(RectangleF r, float rad)
        {
            GraphicsPath p = new GraphicsPath();
            float d = rad * 2f;
            if (d > r.Width) d = r.Width;
            if (d > r.Height) d = r.Height;
            p.AddArc(r.X, r.Y, d, d, 180f, 90f); p.AddArc(r.Right - d, r.Y, d, d, 270f, 90f);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0f, 90f); p.AddArc(r.X, r.Bottom - d, d, d, 90f, 90f);
            p.CloseFigure(); return p;
        }
    }
}
