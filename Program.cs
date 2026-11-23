using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.IO;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace PortWatch
{
    internal sealed class LaunchConfig
    {
        public bool DemoMode;
        public string ForceLang;
        public string ForceFilter;
        public bool ForceHud;
        public bool StartMinimized;
        public bool HandoffWait;

        public static LaunchConfig Parse(string[] args)
        {
            LaunchConfig c = new LaunchConfig();
            if (args == null) return c;
            for (int i = 0; i < args.Length; i++)
            {
                string a = (args[i] ?? "").Trim();
                if (string.Equals(a, "--demo", StringComparison.OrdinalIgnoreCase)) c.DemoMode = true;
                else if (string.Equals(a, "--hud", StringComparison.OrdinalIgnoreCase)) c.ForceHud = true;
                else if (string.Equals(a, "--minimized", StringComparison.OrdinalIgnoreCase)) c.StartMinimized = true;
                else if (string.Equals(a, "--handoff", StringComparison.OrdinalIgnoreCase)) c.HandoffWait = true;
                else if (a.StartsWith("--lang=", StringComparison.OrdinalIgnoreCase)) c.ForceLang = a.Substring(7).Trim().ToLowerInvariant();
                else if (a.StartsWith("--filter=", StringComparison.OrdinalIgnoreCase)) c.ForceFilter = a.Substring(9).Trim().ToLowerInvariant();
            }
            return c;
        }
    }

    internal static class UiTheme
    {
        public static readonly Color Bg = Color.FromArgb(11, 15, 22);
        public static readonly Color Surface = Color.FromArgb(17, 23, 33);
        public static readonly Color SurfaceAlt = Color.FromArgb(21, 29, 42);
        public static readonly Color HoverRow = Color.FromArgb(27, 37, 54);
        public static readonly Color Border = Color.FromArgb(36, 48, 68);
        public static readonly Color BorderLight = Color.FromArgb(52, 68, 94);
        public static readonly Color Text = Color.FromArgb(241, 245, 249);
        public static readonly Color Muted = Color.FromArgb(148, 163, 184);
        public static readonly Color Subtle = Color.FromArgb(100, 116, 139);
        public static readonly Color Emerald = Color.FromArgb(52, 211, 153);
        public static readonly Color Cyan = Color.FromArgb(56, 189, 248);
        public static readonly Color Amber = Color.FromArgb(251, 191, 36);
        public static readonly Color Coral = Color.FromArgb(248, 113, 113);
        public static readonly Color Violet = Color.FromArgb(167, 139, 250);

        public static GraphicsPath RoundedRect(RectangleF r, float radius)
        {
            GraphicsPath p = new GraphicsPath();
            float d = radius * 2f;
            if (d > r.Width) d = r.Width;
            if (d > r.Height) d = r.Height;
            if (d <= 0.5f) { p.AddRectangle(r); return p; }
            p.AddArc(r.X, r.Y, d, d, 180f, 90f);
            p.AddArc(r.Right - d, r.Y, d, d, 270f, 90f);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0f, 90f);
            p.AddArc(r.X, r.Bottom - d, d, d, 90f, 90f);
            p.CloseFigure();
            return p;
        }

        public static void FillRounded(Graphics g, RectangleF r, float rad, Color fill)
        {
            using (GraphicsPath p = RoundedRect(r, rad))
            using (SolidBrush b = new SolidBrush(fill))
                g.FillPath(b, p);
        }

        public static void DrawRounded(Graphics g, RectangleF r, float rad, Color stroke, float width)
        {
            using (GraphicsPath p = RoundedRect(r, rad))
            using (Pen pen = new Pen(stroke, width))
                g.DrawPath(pen, p);
        }
    }

    internal enum RowActionKind { None, Kill, BlockToggle, OpenFolder }

    internal sealed class PortListPanel : Control
    {
        private List<PortEntry> _items = new List<PortEntry>();
        private string _lang = "en";
        private float _scale = 1.0f;
        private int _scrollY;
        private int _hoverRow = -1;
        private int _selectedRow = -1;
        private RowActionKind _hoverAction = RowActionKind.None;
        private bool _draggingThumb;
        private int _dragStartY;
        private int _dragStartScroll;
        private Rectangle _thumbRect = Rectangle.Empty;

        private Font _fHdr;
        private Font _fMain;
        private Font _fBold;
        private Font _fSmall;
        private Font _fMono;
        private Font _fMonoBold;
        private Font _fBtn;

        public event Action<PortEntry, RowActionKind> RowActionClicked;
        public event Action<PortEntry> RowDoubleClicked;
        public event Action<PortEntry, Point> RowRightClicked;

        public PortListPanel()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw |
                     ControlStyles.Selectable, true);
            BackColor = UiTheme.Surface;
            RebuildFonts();
        }

        private int S(float v) { return (int)Math.Round(v * _scale); }

        public void SetScaleAndLang(float scale, string lang)
        {
            _scale = scale > 0.5f ? scale : 1.0f;
            _lang = lang ?? "en";
            RebuildFonts();
            ClampScroll();
            Invalidate();
        }

        public void SetItems(List<PortEntry> items)
        {
            _items = items ?? new List<PortEntry>();
            if (_hoverRow >= _items.Count) _hoverRow = -1;
            if (_selectedRow >= _items.Count) _selectedRow = -1;
            ClampScroll();
            Invalidate();
        }

        private void RebuildFonts()
        {
            DisposeFonts();
            string fam = Loc.FontFamily(_lang);
            float px = (96f / 72f) * _scale;
            _fHdr = new Font(fam, 7.6f * px, FontStyle.Bold, GraphicsUnit.Pixel);
            _fMain = new Font(fam, 8.8f * px, FontStyle.Regular, GraphicsUnit.Pixel);
            _fBold = new Font(fam, 9.0f * px, FontStyle.Bold, GraphicsUnit.Pixel);
            _fSmall = new Font(fam, 7.5f * px, FontStyle.Regular, GraphicsUnit.Pixel);
            _fMono = new Font("Consolas", 8.6f * px, FontStyle.Regular, GraphicsUnit.Pixel);
            _fMonoBold = new Font("Consolas", 9.0f * px, FontStyle.Bold, GraphicsUnit.Pixel);
            _fBtn = new Font(fam, 7.8f * px, FontStyle.Bold, GraphicsUnit.Pixel);
        }

        private void DisposeFonts()
        {
            if (_fHdr != null) { _fHdr.Dispose(); _fHdr = null; }
            if (_fMain != null) { _fMain.Dispose(); _fMain = null; }
            if (_fBold != null) { _fBold.Dispose(); _fBold = null; }
            if (_fSmall != null) { _fSmall.Dispose(); _fSmall = null; }
            if (_fMono != null) { _fMono.Dispose(); _fMono = null; }
            if (_fMonoBold != null) { _fMonoBold.Dispose(); _fMonoBold = null; }
            if (_fBtn != null) { _fBtn.Dispose(); _fBtn = null; }
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            if (disposing) DisposeFonts();
        }

        private int HeaderH { get { return S(30); } }
        private int RowH { get { return S(44); } }
        private int ScrollBarW { get { return S(10); } }

        private int MaxScroll
        {
            get
            {
                int viewH = Math.Max(0, Height - HeaderH - 2);
                int totalH = _items.Count * RowH;
                return Math.Max(0, totalH - viewH);
            }
        }

        private void ClampScroll()
        {
            int max = MaxScroll;
            if (_scrollY < 0) _scrollY = 0;
            if (_scrollY > max) _scrollY = max;
        }

        private void ComputeColumns(out int xProto, out int wProto, out int xLocal, out int wLocal,
            out int xRemote, out int wRemote, out int xState, out int wState,
            out int xProc, out int wProc, out int xAct, out int wAct)
        {
            int innerRight = Width - ScrollBarW - S(6);
            int pad = S(10);
            wProto = S(58);
            wLocal = S(196);
            wRemote = S(160);
            wState = S(106);
            wAct = S(222);

            int fixedSum = pad + wProto + wLocal + wRemote + wState + wAct + S(8);
            int avail = Math.Max(S(150), innerRight - fixedSum);
            if (avail < S(165))
            {
                wRemote = Math.Max(S(125), wRemote - (S(165) - avail));
                avail = Math.Max(S(130), innerRight - (pad + wProto + wLocal + wRemote + wState + wAct + S(8)));
            }

            xProto = pad;
            xLocal = xProto + wProto;
            xRemote = xLocal + wLocal;
            xState = xRemote + wRemote;
            xProc = xState + wState;
            wProc = Math.Max(S(130), avail);
            xAct = innerRight - wAct;
        }

        private void GetRowActionRects(int rowY, int xAct, int wAct, out Rectangle rKill, out Rectangle rBlock, out Rectangle rDir)
        {
            int btnH = S(24);
            int by = rowY + (RowH - btnH) / 2;
            int gap = S(5);
            int wDir = S(52);
            int wKill = S(62);
            int wBlk = Math.Max(S(70), wAct - wDir - wKill - gap * 2 - S(6));

            int rx = xAct + wAct - S(4);
            rDir = new Rectangle(rx - wDir, by, wDir, btnH);
            rBlock = new Rectangle(rDir.Left - gap - wBlk, by, wBlk, btnH);
            rKill = new Rectangle(rBlock.Left - gap - wKill, by, wKill, btnH);
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);
            int delta = e.Delta > 0 ? -RowH * 2 : RowH * 2;
            _scrollY += delta;
            ClampScroll();
            UpdateHover(PointToClient(Cursor.Position));
            Invalidate();
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (e.KeyCode == Keys.Down) { _scrollY += RowH; ClampScroll(); Invalidate(); }
            else if (e.KeyCode == Keys.Up) { _scrollY -= RowH; ClampScroll(); Invalidate(); }
            else if (e.KeyCode == Keys.PageDown) { _scrollY += Math.Max(RowH, Height - HeaderH - RowH); ClampScroll(); Invalidate(); }
            else if (e.KeyCode == Keys.PageUp) { _scrollY -= Math.Max(RowH, Height - HeaderH - RowH); ClampScroll(); Invalidate(); }
            else if (e.KeyCode == Keys.Home) { _scrollY = 0; Invalidate(); }
            else if (e.KeyCode == Keys.End) { _scrollY = MaxScroll; Invalidate(); }
        }

        private void UpdateHover(Point pt)
        {
            int prevRow = _hoverRow;
            RowActionKind prevAct = _hoverAction;
            _hoverRow = -1;
            _hoverAction = RowActionKind.None;

            if (pt.Y > HeaderH && pt.X < Width - ScrollBarW - S(4))
            {
                int idx = (pt.Y - HeaderH + _scrollY) / RowH;
                if (idx >= 0 && idx < _items.Count)
                {
                    _hoverRow = idx;
                    int xP, wP, xL, wL, xR, wR, xS, wS, xPr, wPr, xA, wA;
                    ComputeColumns(out xP, out wP, out xL, out wL, out xR, out wR, out xS, out wS, out xPr, out wPr, out xA, out wA);
                    int ry = HeaderH + idx * RowH - _scrollY;
                    Rectangle rKill, rBlock, rDir;
                    GetRowActionRects(ry, xA, wA, out rKill, out rBlock, out rDir);
                    if (rKill.Contains(pt)) _hoverAction = RowActionKind.Kill;
                    else if (rBlock.Contains(pt)) _hoverAction = RowActionKind.BlockToggle;
                    else if (rDir.Contains(pt)) _hoverAction = RowActionKind.OpenFolder;
                }
            }

            bool onBtn = _hoverAction != RowActionKind.None;
            Cursor = onBtn ? Cursors.Hand : Cursors.Default;
            if (prevRow != _hoverRow || prevAct != _hoverAction) Invalidate();
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (_draggingThumb)
            {
                int trackH = Math.Max(1, Height - HeaderH - S(8) - _thumbRect.Height);
                int dy = e.Y - _dragStartY;
                _scrollY = _dragStartScroll + (int)Math.Round(((double)dy / trackH) * MaxScroll);
                ClampScroll();
                Invalidate();
                return;
            }
            UpdateHover(e.Location);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            if (_hoverRow != -1 || _hoverAction != RowActionKind.None)
            {
                _hoverRow = -1;
                _hoverAction = RowActionKind.None;
                Cursor = Cursors.Default;
                Invalidate();
            }
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            Focus();
            if (e.Button == MouseButtons.Left && !_thumbRect.IsEmpty && _thumbRect.Contains(e.Location))
            {
                _draggingThumb = true;
                _dragStartY = e.Y;
                _dragStartScroll = _scrollY;
                Capture = true;
                return;
            }

            UpdateHover(e.Location);
            if (_hoverRow >= 0 && _hoverRow < _items.Count)
            {
                _selectedRow = _hoverRow;
                PortEntry entry = _items[_hoverRow];
                if (e.Button == MouseButtons.Right)
                {
                    Invalidate();
                    if (RowRightClicked != null) RowRightClicked(entry, PointToScreen(e.Location));
                    return;
                }
                if (e.Button == MouseButtons.Left && _hoverAction != RowActionKind.None)
                {
                    if (RowActionClicked != null) RowActionClicked(entry, _hoverAction);
                    return;
                }
                Invalidate();
            }
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (_draggingThumb)
            {
                _draggingThumb = false;
                Capture = false;
            }
        }

        protected override void OnMouseDoubleClick(MouseEventArgs e)
        {
            base.OnMouseDoubleClick(e);
            if (e.Button != MouseButtons.Left) return;
            UpdateHover(e.Location);
            if (_hoverRow >= 0 && _hoverRow < _items.Count && _hoverAction == RowActionKind.None)
            {
                if (RowDoubleClicked != null) RowDoubleClicked(_items[_hoverRow]);
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;

            g.Clear(UiTheme.Surface);

            int xP, wP, xL, wL, xR, wR, xS, wS, xPr, wPr, xA, wA;
            ComputeColumns(out xP, out wP, out xL, out wL, out xR, out wR, out xS, out wS, out xPr, out wPr, out xA, out wA);

            // Header bar
            using (SolidBrush bHdr = new SolidBrush(UiTheme.SurfaceAlt))
            using (Pen pSep = new Pen(UiTheme.Border, 1f))
            {
                g.FillRectangle(bHdr, 0, 0, Width, HeaderH);
                g.DrawLine(pSep, 0, HeaderH - 1, Width, HeaderH - 1);
            }

            using (SolidBrush bHdrTxt = new SolidBrush(UiTheme.Muted))
            using (StringFormat sfH = new StringFormat(StringFormatFlags.NoWrap))
            {
                sfH.LineAlignment = StringAlignment.Center;
                sfH.Trimming = StringTrimming.EllipsisCharacter;
                g.DrawString(Loc.T(_lang, "PROTO", "PROTO", "协议"), _fHdr, bHdrTxt, new RectangleF(xP, 0, wP, HeaderH), sfH);
                g.DrawString(Loc.T(_lang, "LOCAL ENDPOINT", "YEREL PORT", "本地端口"), _fHdr, bHdrTxt, new RectangleF(xL, 0, wL, HeaderH), sfH);
                g.DrawString(Loc.T(_lang, "REMOTE ENDPOINT", "UZAK ADRES", "远程地址"), _fHdr, bHdrTxt, new RectangleF(xR, 0, wR, HeaderH), sfH);
                g.DrawString(Loc.T(_lang, "STATE", "DURUM", "状态"), _fHdr, bHdrTxt, new RectangleF(xS, 0, wS, HeaderH), sfH);
                g.DrawString(Loc.T(_lang, "PROCESS (PID)", "İŞLEM (PID)", "进程 (PID)"), _fHdr, bHdrTxt, new RectangleF(xPr, 0, wPr, HeaderH), sfH);
                sfH.Alignment = StringAlignment.Far;
                g.DrawString(Loc.T(_lang, "ACTIONS", "İŞLEMLER", "操作"), _fHdr, bHdrTxt, new RectangleF(xA, 0, wA - S(6), HeaderH), sfH);
            }

            // Rows clip region
            Rectangle listRect = new Rectangle(0, HeaderH, Width, Math.Max(0, Height - HeaderH));
            GraphicsState st = g.Save();
            g.SetClip(listRect);

            if (_items.Count == 0)
            {
                using (SolidBrush bm = new SolidBrush(UiTheme.Muted))
                using (StringFormat sfc = new StringFormat())
                {
                    sfc.Alignment = StringAlignment.Center;
                    sfc.LineAlignment = StringAlignment.Center;
                    g.DrawString(Loc.T(_lang, "No connections match this filter.", "Bu filtreyle eşleşen bağlantı yok.", "没有匹配当前筛选条件的连接。"),
                        _fMain, bm, listRect, sfc);
                }
            }
            else
            {
                int first = Math.Max(0, _scrollY / RowH);
                int visibleCount = (listRect.Height / RowH) + 2;
                int last = Math.Min(_items.Count - 1, first + visibleCount);

                using (Pen pRow = new Pen(Color.FromArgb(30, 40, 56), 1f))
                using (StringFormat sfNear = new StringFormat(StringFormatFlags.NoWrap))
                using (StringFormat sfCenter = new StringFormat(StringFormatFlags.NoWrap))
                {
                    sfNear.LineAlignment = StringAlignment.Center;
                    sfNear.Trimming = StringTrimming.EllipsisCharacter;
                    sfCenter.Alignment = StringAlignment.Center;
                    sfCenter.LineAlignment = StringAlignment.Center;
                    sfCenter.Trimming = StringTrimming.EllipsisCharacter;

                    string tKill = Loc.T(_lang, "Kill", "Kapat", "结束");
                    string tBlock = Loc.T(_lang, "Block", "Engelle", "拦截");
                    string tUnblock = Loc.T(_lang, "Unblock", "Kaldır", "放行");
                    string tDir = Loc.T(_lang, "Dir", "Klasör", "目录");
                    string tProtected = Loc.T(_lang, "SYSTEM", "SİSTEM", "系统");
                    string tBlockedTag = Loc.T(_lang, "BLOCKED", "ENGELLİ", "已拦截");

                    for (int i = first; i <= last; i++)
                    {
                        PortEntry it = _items[i];
                        int ry = HeaderH + i * RowH - _scrollY;
                        Rectangle rowRect = new Rectangle(0, ry, Width - ScrollBarW - S(2), RowH);

                        Color rowBg = (i == _hoverRow) ? UiTheme.HoverRow
                            : (i == _selectedRow ? Color.FromArgb(24, 34, 50)
                            : ((i & 1) == 1 ? Color.FromArgb(15, 21, 30) : UiTheme.Surface));
                        using (SolidBrush br = new SolidBrush(rowBg))
                            g.FillRectangle(br, rowRect);

                        if (it.IsFirewallBlocked)
                        {
                            using (SolidBrush bBar = new SolidBrush(UiTheme.Coral))
                                g.FillRectangle(bBar, 0, ry + S(4), S(3), RowH - S(8));
                        }

                        g.DrawLine(pRow, S(8), ry + RowH - 1, Width - ScrollBarW - S(8), ry + RowH - 1);

                        // 1) Protocol pill
                        bool isUdp = it.Protocol != null && it.Protocol.StartsWith("UDP");
                        Color protoCol = isUdp ? UiTheme.Amber : UiTheme.Cyan;
                        RectangleF protoBadge = new RectangleF(xP, ry + (RowH - S(20)) / 2f, S(44), S(20));
                        UiTheme.FillRounded(g, protoBadge, S(5), Color.FromArgb(28, protoCol));
                        UiTheme.DrawRounded(g, protoBadge, S(5), Color.FromArgb(95, protoCol), 1f);
                        using (SolidBrush bp = new SolidBrush(protoCol))
                            g.DrawString(it.Protocol ?? "TCP", _fSmall, bp, protoBadge, sfCenter);

                        // 2) Local Endpoint + optional Service Tag
                        string localTxt = it.LocalEndpoint;
                        if (!string.IsNullOrEmpty(it.ServiceTag))
                        {
                            using (SolidBrush bl = new SolidBrush(UiTheme.Text))
                                g.DrawString(localTxt, _fMonoBold, bl, new RectangleF(xL, ry + S(4), wL - S(8), S(18)), sfNear);

                            SizeF tagSz = g.MeasureString(it.ServiceTag, _fSmall);
                            float tw = Math.Min(wL - S(16), tagSz.Width + S(10));
                            RectangleF tagRc = new RectangleF(xL, ry + S(23), tw, S(16));
                            UiTheme.FillRounded(g, tagRc, S(4), Color.FromArgb(32, UiTheme.Violet));
                            using (SolidBrush bv = new SolidBrush(UiTheme.Violet))
                                g.DrawString(it.ServiceTag, _fSmall, bv, tagRc, sfCenter);
                        }
                        else
                        {
                            using (SolidBrush bl = new SolidBrush(UiTheme.Text))
                                g.DrawString(localTxt, _fMonoBold, bl, new RectangleF(xL, ry, wL - S(8), RowH), sfNear);
                        }

                        // 3) Remote Endpoint
                        Color remCol = it.RemotePort > 0 ? UiTheme.Muted : UiTheme.Subtle;
                        using (SolidBrush brem = new SolidBrush(remCol))
                            g.DrawString(it.RemoteEndpoint, _fMono, brem, new RectangleF(xR, ry, wR - S(8), RowH), sfNear);

                        // 4) State dot + label
                        Color stCol = StateColor(it.State);
                        float dotSz = S(7);
                        using (SolidBrush bDot = new SolidBrush(stCol))
                            g.FillEllipse(bDot, xS, ry + (RowH - dotSz) / 2f, dotSz, dotSz);
                        using (SolidBrush bSt = new SolidBrush(stCol))
                            g.DrawString(FormatStateLabel(it.State), _fSmall, bSt, new RectangleF(xS + S(12), ry, wS - S(16), RowH), sfNear);

                        // 5) Process + PID + badge + path
                        string procTitle = (it.ProcessName ?? "unknown") + "  #" + it.Pid;
                        int badgeOffsetX = 0;
                        SizeF pTitleSz = g.MeasureString(procTitle, _fBold);
                        float titleMaxW = wPr - S(10);
                        if (it.IsFirewallBlocked || it.IsCriticalSystem) titleMaxW = Math.Max(S(70), wPr - S(74));

                        using (SolidBrush bPr = new SolidBrush(UiTheme.Text))
                            g.DrawString(procTitle, _fBold, bPr, new RectangleF(xPr, ry + S(4), titleMaxW, S(19)), sfNear);

                        if (it.IsFirewallBlocked || it.IsCriticalSystem)
                        {
                            string bTxt = it.IsFirewallBlocked ? tBlockedTag : tProtected;
                            Color bCol = it.IsFirewallBlocked ? UiTheme.Coral : UiTheme.Amber;
                            SizeF bSz = g.MeasureString(bTxt, _fSmall);
                            float bw = bSz.Width + S(8);
                            float bx = xPr + Math.Min(titleMaxW, pTitleSz.Width) + S(6);
                            if (bx + bw <= xPr + wPr - S(4))
                            {
                                RectangleF bRc = new RectangleF(bx, ry + S(6), bw, S(15));
                                UiTheme.FillRounded(g, bRc, S(4), Color.FromArgb(35, bCol));
                                using (SolidBrush bb = new SolidBrush(bCol))
                                    g.DrawString(bTxt, _fSmall, bb, bRc, sfCenter);
                            }
                            badgeOffsetX = (int)bw;
                        }
                        if (badgeOffsetX < 0) badgeOffsetX = 0;

                        string subPath = !string.IsNullOrEmpty(it.ExePath)
                            ? ShortenPath(it.ExePath)
                            : (it.IsCriticalSystem ? Loc.T(_lang, "Protected Windows system process", "Korumalı Windows sistem işlemi", "受保护的 Windows 系统进程") : "—");
                        using (SolidBrush bSub = new SolidBrush(UiTheme.Subtle))
                            g.DrawString(subPath, _fSmall, bSub, new RectangleF(xPr, ry + S(23), wPr - S(10), S(16)), sfNear);

                        // 6) Inline Action Buttons
                        Rectangle rKill, rBlock, rDir;
                        GetRowActionRects(ry, xA, wA, out rKill, out rBlock, out rDir);

                        bool killEnabled = !it.IsCriticalSystem;
                        bool blkEnabled = !it.IsCriticalSystem && !string.IsNullOrEmpty(it.ExePath);
                        bool dirEnabled = !string.IsNullOrEmpty(it.ExePath);

                        bool hovKill = (i == _hoverRow && _hoverAction == RowActionKind.Kill && killEnabled);
                        bool hovBlk = (i == _hoverRow && _hoverAction == RowActionKind.BlockToggle && blkEnabled);
                        bool hovDir = (i == _hoverRow && _hoverAction == RowActionKind.OpenFolder && dirEnabled);

                        DrawActionPill(g, rKill, tKill, UiTheme.Coral, killEnabled, hovKill, false, sfCenter);
                        DrawActionPill(g, rBlock, it.IsFirewallBlocked ? tUnblock : tBlock,
                            it.IsFirewallBlocked ? UiTheme.Coral : UiTheme.Amber, blkEnabled, hovBlk, it.IsFirewallBlocked, sfCenter);
                        DrawActionPill(g, rDir, tDir, UiTheme.Cyan, dirEnabled, hovDir, false, sfCenter);
                    }
                }
            }
            g.Restore(st);

            // Scrollbar
            DrawScrollBar(g);

            // Outer border
            using (Pen pOut = new Pen(UiTheme.Border, 1f))
                g.DrawRectangle(pOut, 0, 0, Width - 1, Height - 1);
        }

        private void DrawActionPill(Graphics g, Rectangle rc, string text, Color accent, bool enabled, bool hovered, bool solid, StringFormat sfCenter)
        {
            Color fill, stroke, txt;
            if (!enabled)
            {
                fill = Color.FromArgb(20, 27, 38);
                stroke = Color.FromArgb(34, 44, 60);
                txt = Color.FromArgb(72, 85, 105);
            }
            else if (solid)
            {
                fill = hovered ? Color.FromArgb(220, accent) : Color.FromArgb(185, accent);
                stroke = accent;
                txt = Color.FromArgb(15, 23, 42);
            }
            else
            {
                fill = hovered ? Color.FromArgb(55, accent) : Color.FromArgb(22, accent);
                stroke = hovered ? accent : Color.FromArgb(115, accent);
                txt = hovered ? UiTheme.Text : accent;
            }
            UiTheme.FillRounded(g, rc, S(5), fill);
            UiTheme.DrawRounded(g, rc, S(5), stroke, 1f);
            using (SolidBrush bt = new SolidBrush(txt))
                g.DrawString(text, _fBtn, bt, rc, sfCenter);
        }

        private void DrawScrollBar(Graphics g)
        {
            _thumbRect = Rectangle.Empty;
            int max = MaxScroll;
            if (max <= 0) return;

            int trackTop = HeaderH + S(4);
            int trackH = Math.Max(S(24), Height - HeaderH - S(8));
            int totalH = _items.Count * RowH;
            int viewH = Math.Max(1, Height - HeaderH);
            int thumbH = Math.Max(S(28), (int)Math.Round((double)viewH / totalH * trackH));
            int thumbY = trackTop + (int)Math.Round(((double)_scrollY / max) * (trackH - thumbH));

            int tx = Width - ScrollBarW - S(2);
            _thumbRect = new Rectangle(tx, thumbY, S(6), thumbH);
            UiTheme.FillRounded(g, _thumbRect, S(3), _draggingThumb ? UiTheme.Cyan : Color.FromArgb(70, 88, 114));
        }

        private Color StateColor(string state)
        {
            if (state == "LISTENING") return UiTheme.Emerald;
            if (state == "ESTABLISHED") return UiTheme.Cyan;
            if (state == "UDP") return UiTheme.Amber;
            if (state == "TIME_WAIT" || state == "CLOSE_WAIT") return UiTheme.Muted;
            return UiTheme.Subtle;
        }

        private string FormatStateLabel(string state)
        {
            if (state == "LISTENING") return Loc.T(_lang, "LISTENING", "DİNLİYOR", "监听中");
            if (state == "ESTABLISHED") return Loc.T(_lang, "ESTABLISHED", "BAĞLI", "已连接");
            if (state == "UDP") return "UDP";
            if (state == "TIME_WAIT") return "TIME_WAIT";
            if (state == "CLOSE_WAIT") return "CLOSE_WAIT";
            return state ?? "";
        }

        private static string ShortenPath(string p)
        {
            if (string.IsNullOrEmpty(p) || p.Length <= 52) return p ?? "";
            try
            {
                string file = Path.GetFileName(p);
                string dir = Path.GetDirectoryName(p) ?? "";
                if (dir.Length > 34) dir = dir.Substring(0, 16) + "…" + dir.Substring(dir.Length - 16);
                return dir + "\\" + file;
            }
            catch { return p; }
        }
    }

    internal sealed class MainForm : Form
    {
        private const string AppVersion = "1.0.0";

        private readonly LaunchConfig _launch;
        private readonly PortWatchSettings _cfg;
        private readonly NetworkEngine _engine = new NetworkEngine();
        private PortSnapshot _snapshot = new PortSnapshot();
        private List<PortEntry> _filtered = new List<PortEntry>();

        private string _lang = "en";
        private string _filterMode = "all"; // all, listening, established, tcp, udp, blocked
        private bool _paused;
        private int _busy;
        private bool _allowRealClose;
        private string _statusToast = "";
        private DateTime _toastUntil = DateTime.MinValue;

        private float _scale = 1.0f;
        private string _hoverId = "";
        private readonly Dictionary<string, Rectangle> _hitRects = new Dictionary<string, Rectangle>();

        private TextBox _searchBox;
        private PortListPanel _table;
        private System.Windows.Forms.Timer _pollTimer;
        private NetHudForm _hudForm;
        private NotifyIcon _trayIcon;
        private ContextMenuStrip _trayMenu;
        private ContextMenuStrip _rowMenu;
        private PortEntry _ctxEntry;

        // Tracked GDI Fonts (zero leaks)
        private Font _fTitle;
        private Font _fVer;
        private Font _fSub;
        private Font _fKpiLbl;
        private Font _fKpiVal;
        private Font _fKpiSub;
        private Font _fPill;
        private Font _fFooter;
        private Font _fSearch;

        public MainForm(LaunchConfig launch)
        {
            _launch = launch ?? new LaunchConfig();
            _cfg = PortWatchSettings.Load();
            if (!string.IsNullOrEmpty(_launch.ForceLang) && (_launch.ForceLang == "en" || _launch.ForceLang == "tr" || _launch.ForceLang == "zh"))
                _lang = _launch.ForceLang;
            else
                _lang = _cfg.Language;

            if (!string.IsNullOrEmpty(_launch.ForceFilter))
                _filterMode = _launch.ForceFilter;

            if (_launch.ForceHud) _cfg.NetHudEnabled = true;

            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);

            Text = "PortWatch v" + AppVersion;
            BackColor = UiTheme.Bg;
            ForeColor = UiTheme.Text;
            StartPosition = FormStartPosition.CenterScreen;
            KeyPreview = true;

            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }

            _scale = ComputeScale();
            RebuildFonts();

            ClientSize = new Size(S(1040), S(680));
            MinimumSize = new Size(S(900), S(570));

            InitControls();
            InitTray();
            InitRowContextMenu();

            _pollTimer = new System.Windows.Forms.Timer();
            _pollTimer.Interval = Math.Max(1000, _cfg.RefreshMs);
            _pollTimer.Tick += delegate { TriggerRefresh(); };

            if (_launch.DemoMode)
            {
                _snapshot = NetworkEngine.BuildDemoSnapshot();
                ApplyFilterAndBind();
            }
            else
            {
                _pollTimer.Start();
            }
        }

        private int S(float v) { return (int)Math.Round(v * _scale); }

        private float ComputeScale()
        {
            try
            {
                if (IsHandleCreated)
                {
                    uint d = WinNative.GetDpiForWindow(Handle);
                    if (d >= 72 && d <= 480) return d / 96f;
                }
            }
            catch { }
            try
            {
                using (Graphics g = CreateGraphics())
                    if (g.DpiX >= 72f) return g.DpiX / 96f;
            }
            catch { }
            return 1.0f;
        }

        private void RebuildFonts()
        {
            string fam = Loc.FontFamily(_lang);
            float px = (96f / 72f) * _scale;
            Font oldTitle = _fTitle;
            Font oldVer = _fVer;
            Font oldSub = _fSub;
            Font oldKpiLbl = _fKpiLbl;
            Font oldKpiVal = _fKpiVal;
            Font oldKpiSub = _fKpiSub;
            Font oldPill = _fPill;
            Font oldFooter = _fFooter;

            _fTitle = new Font("Segoe UI", 14.2f * px, FontStyle.Bold, GraphicsUnit.Pixel);
            _fVer = new Font("Consolas", 8.0f * px, FontStyle.Bold, GraphicsUnit.Pixel);
            _fSub = new Font(fam, 8.2f * px, FontStyle.Regular, GraphicsUnit.Pixel);
            _fKpiLbl = new Font(fam, 7.6f * px, FontStyle.Bold, GraphicsUnit.Pixel);
            _fKpiVal = new Font("Segoe UI", 16.5f * px, FontStyle.Bold, GraphicsUnit.Pixel);
            _fKpiSub = new Font(fam, 7.8f * px, FontStyle.Regular, GraphicsUnit.Pixel);
            _fPill = new Font(fam, 8.2f * px, FontStyle.Bold, GraphicsUnit.Pixel);
            _fFooter = new Font(fam, 8.0f * px, FontStyle.Regular, GraphicsUnit.Pixel);

            // Guard against WinForms Control.Font.Equals() short-circuiting when EN<->TR share Segoe UI
            float searchPx = 9.2f * px;
            bool needNewSearch = (_fSearch == null)
                || !string.Equals(_fSearch.Name, fam, StringComparison.OrdinalIgnoreCase)
                || Math.Abs(_fSearch.Size - searchPx) > 0.05f;
            if (needNewSearch)
            {
                Font oldSearch = _fSearch;
                _fSearch = new Font(fam, searchPx, FontStyle.Regular, GraphicsUnit.Pixel);
                if (_searchBox != null && !_searchBox.IsDisposed) _searchBox.Font = _fSearch;
                if (oldSearch != null) oldSearch.Dispose();
            }

            if (_table != null && !_table.IsDisposed) _table.SetScaleAndLang(_scale, _lang);

            if (oldTitle != null) oldTitle.Dispose();
            if (oldVer != null) oldVer.Dispose();
            if (oldSub != null) oldSub.Dispose();
            if (oldKpiLbl != null) oldKpiLbl.Dispose();
            if (oldKpiVal != null) oldKpiVal.Dispose();
            if (oldKpiSub != null) oldKpiSub.Dispose();
            if (oldPill != null) oldPill.Dispose();
            if (oldFooter != null) oldFooter.Dispose();
        }

        private void DisposeFonts()
        {
            if (_fTitle != null) { _fTitle.Dispose(); _fTitle = null; }
            if (_fVer != null) { _fVer.Dispose(); _fVer = null; }
            if (_fSub != null) { _fSub.Dispose(); _fSub = null; }
            if (_fKpiLbl != null) { _fKpiLbl.Dispose(); _fKpiLbl = null; }
            if (_fKpiVal != null) { _fKpiVal.Dispose(); _fKpiVal = null; }
            if (_fKpiSub != null) { _fKpiSub.Dispose(); _fKpiSub = null; }
            if (_fPill != null) { _fPill.Dispose(); _fPill = null; }
            if (_fFooter != null) { _fFooter.Dispose(); _fFooter = null; }
            if (_fSearch != null) { _fSearch.Dispose(); _fSearch = null; }
        }

        private void InitControls()
        {
            _searchBox = new TextBox();
            _searchBox.BorderStyle = BorderStyle.None;
            _searchBox.BackColor = UiTheme.SurfaceAlt;
            _searchBox.ForeColor = UiTheme.Text;
            _searchBox.Font = _fSearch;
            _searchBox.Visible = false;
            _searchBox.TextChanged += delegate
            {
                ApplyFilterAndBind();
                Invalidate();
            };
            _searchBox.GotFocus += delegate { Invalidate(); };
            _searchBox.LostFocus += delegate
            {
                if (_searchBox.Text.Length == 0) _searchBox.Visible = false;
                Invalidate();
            };
            Controls.Add(_searchBox);

            _table = new PortListPanel();
            _table.SetScaleAndLang(_scale, _lang);
            _table.RowActionClicked += HandleRowAction;
            _table.RowDoubleClicked += HandleRowCopy;
            _table.RowRightClicked += HandleRowContextMenu;
            Controls.Add(_table);
        }

        private void InitTray()
        {
            _trayMenu = new ContextMenuStrip();
            _trayIcon = new NotifyIcon();
            _trayIcon.Icon = Icon ?? SystemIcons.Application;
            _trayIcon.Text = "PortWatch v" + AppVersion;
            _trayIcon.ContextMenuStrip = _trayMenu;
            _trayIcon.Visible = !_launch.DemoMode;
            _trayIcon.DoubleClick += delegate { RestoreFromTray(); };
            RebuildTrayMenu();
        }

        private void RebuildTrayMenu()
        {
            if (_trayMenu == null) return;
            _trayMenu.Items.Clear();
            _trayMenu.Items.Add(Loc.T(_lang, "Open PortWatch", "PortWatch'u Aç", "打开 PortWatch"), null, delegate { RestoreFromTray(); });
            _trayMenu.Items.Add(Loc.T(_lang, _cfg.NetHudEnabled ? "Hide Mini Net HUD" : "Show Mini Net HUD",
                _cfg.NetHudEnabled ? "Mini Ağ HUD Gizle" : "Mini Ağ HUD Göster",
                _cfg.NetHudEnabled ? "隐藏迷你网速浮窗" : "显示迷你网速浮窗"), null, delegate { ToggleHud(); });
            _trayMenu.Items.Add(new ToolStripSeparator());
            _trayMenu.Items.Add(Loc.T(_lang, "Exit", "Çıkış", "退出"), null, delegate { ExitAppCleanly(); });
        }

        private void InitRowContextMenu()
        {
            _rowMenu = new ContextMenuStrip();
        }

        private void HandleRowContextMenu(PortEntry entry, Point screenPt)
        {
            if (entry == null || _rowMenu == null) return;
            _ctxEntry = entry;
            _rowMenu.Items.Clear();
            _rowMenu.Items.Add(Loc.T(_lang, "Copy Connection Info", "Bağlantı Bilgisini Kopyala", "复制连接信息"), null, delegate { HandleRowCopy(_ctxEntry); });
            _rowMenu.Items.Add(Loc.T(_lang, "Copy Port (" + entry.LocalPort + ")", "Portu Kopyala (" + entry.LocalPort + ")", "复制端口 (" + entry.LocalPort + ")"), null, delegate
            {
                try { Clipboard.SetText(entry.LocalPort.ToString()); ShowToast(Loc.T(_lang, "Copied port " + entry.LocalPort, "Port kopyalandı: " + entry.LocalPort, "已复制端口 " + entry.LocalPort)); } catch { }
            });
            if (!string.IsNullOrEmpty(entry.ExePath))
                _rowMenu.Items.Add(Loc.T(_lang, "Open Executable Folder", "Uygulama Klasörünü Aç", "打开程序所在目录"), null, delegate { HandleRowAction(_ctxEntry, RowActionKind.OpenFolder); });
            if (!entry.IsCriticalSystem && !string.IsNullOrEmpty(entry.ExePath))
            {
                _rowMenu.Items.Add(new ToolStripSeparator());
                _rowMenu.Items.Add(entry.IsFirewallBlocked
                    ? Loc.T(_lang, "Unblock in Windows Firewall", "Güvenlik Duvarı Engelini Kaldır", "从 Windows 防火墙放行")
                    : Loc.T(_lang, "Block in Windows Firewall", "Güvenlik Duvarında Engelle", "在 Windows 防火墙中拦截"),
                    null, delegate { HandleRowAction(_ctxEntry, RowActionKind.BlockToggle); });
            }
            if (!entry.IsCriticalSystem)
            {
                _rowMenu.Items.Add(Loc.T(_lang, "Kill Process #" + entry.Pid, "İşlemi Sonlandır #" + entry.Pid, "结束进程 #" + entry.Pid),
                    null, delegate { HandleRowAction(_ctxEntry, RowActionKind.Kill); });
            }
            _rowMenu.Show(screenPt);
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            _scale = ComputeScale();
            RebuildFonts();
            MinimumSize = new Size(S(900), S(570));
            WinNative.ApplyDarkTitleBar(Handle, UiTheme.Bg, UiTheme.Text);
            WinNative.AllowWakeMessage(Handle);
            UpdateSearchCue();
            LayoutChildren();
            if (!_launch.DemoMode) TriggerRefresh();
            SyncHudWindow();
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            LayoutChildren();
            if (_table != null) _table.Focus();
            if (_launch.StartMinimized && !_launch.DemoMode)
            {
                Hide();
                ShowInTaskbar = false;
            }
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            LayoutChildren();
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WinNative.WM_DPICHANGED)
            {
                int newDpi = m.WParam.ToInt32() & 0xFFFF;
                if (newDpi >= 72 && newDpi <= 480)
                {
                    _scale = newDpi / 96f;
                    RebuildFonts();
                    MinimumSize = new Size(S(900), S(570));
                    LayoutChildren();
                    Invalidate();
                    SyncHudWindow();
                }
            }
            else if (WinNative.WM_SHOW_EXISTING != 0 && (uint)m.Msg == WinNative.WM_SHOW_EXISTING)
            {
                RestoreFromTray();
                return;
            }
            base.WndProc(ref m);
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (!_allowRealClose && !_launch.DemoMode && _cfg.MinimizeToTray && e.CloseReason == CloseReason.UserClosing)
            {
                e.Cancel = true;
                Hide();
                ShowInTaskbar = false;
                return;
            }
            base.OnFormClosing(e);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (e.KeyCode == Keys.F5) { TriggerRefresh(); e.Handled = true; }
            else if (e.Control && e.KeyCode == Keys.F)
            {
                _searchBox.Visible = true;
                _searchBox.Focus();
                _searchBox.SelectAll();
                Invalidate();
                e.Handled = true;
            }
            else if (e.KeyCode == Keys.Escape && _searchBox.Focused && !string.IsNullOrEmpty(_searchBox.Text))
            {
                _searchBox.Text = "";
                e.Handled = true;
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (_pollTimer != null) { _pollTimer.Stop(); _pollTimer.Dispose(); _pollTimer = null; }
                if (_hudForm != null) { _hudForm.Close(); _hudForm.Dispose(); _hudForm = null; }
                if (_trayIcon != null) { _trayIcon.Visible = false; _trayIcon.Dispose(); _trayIcon = null; }
                if (_trayMenu != null) { _trayMenu.Dispose(); _trayMenu = null; }
                if (_rowMenu != null) { _rowMenu.Dispose(); _rowMenu = null; }
            }
            base.Dispose(disposing);
            if (disposing) DisposeFonts();
        }

        private void LayoutChildren()
        {
            if (_searchBox == null || _table == null) return;
            int pad = S(16);
            int toolbarY = S(142);
            int searchW = S(230);
            int searchH = S(32);

            _searchBox.SetBounds(pad + S(28), toolbarY + S(7), searchW - S(50), S(20));

            int tableY = toolbarY + searchH + S(12);
            int footerH = S(42);
            int tableH = Math.Max(S(140), ClientSize.Height - tableY - footerH - S(8));
            _table.SetBounds(pad, tableY, Math.Max(S(200), ClientSize.Width - pad * 2), tableH);
        }

        private void UpdateSearchCue()
        {
            string cue = Loc.T(_lang, "Search port, app, IP, PID...", "Port, uygulama, IP, PID ara...", "搜索端口、程序、IP 或 PID...");
            if (_searchBox != null && _searchBox.IsHandleCreated)
                WinNative.SetCueBanner(_searchBox.Handle, cue);
        }

        private void TriggerRefresh()
        {
            if (_paused || _launch.DemoMode) return;
            if (Interlocked.CompareExchange(ref _busy, 1, 0) != 0) return;

            ThreadPool.QueueUserWorkItem(delegate
            {
                PortSnapshot snap = null;
                try { snap = _engine.CaptureLiveSnapshot(); }
                catch { }
                finally { Interlocked.Exchange(ref _busy, 0); }

                if (snap == null) return;
                if (!IsDisposed && !Disposing && IsHandleCreated)
                {
                    try
                    {
                        BeginInvoke((MethodInvoker)delegate
                        {
                            if (IsDisposed || Disposing) return;
                            _snapshot = snap;
                            ApplyFilterAndBind();
                            UpdateHudData();
                            Invalidate();
                        });
                    }
                    catch { }
                }
            });
        }

        private void ApplyFilterAndBind()
        {
            string q = (_searchBox != null ? _searchBox.Text : "").Trim();
            List<PortEntry> list = new List<PortEntry>();
            if (_snapshot != null && _snapshot.Entries != null)
            {
                for (int i = 0; i < _snapshot.Entries.Count; i++)
                {
                    PortEntry e = _snapshot.Entries[i];
                    if (_filterMode == "listening" && e.State != "LISTENING") continue;
                    if (_filterMode == "established" && e.State != "ESTABLISHED") continue;
                    if (_filterMode == "tcp" && !(e.Protocol == "TCP" || e.Protocol == "TCP6")) continue;
                    if (_filterMode == "udp" && !(e.Protocol == "UDP" || e.Protocol == "UDP6")) continue;
                    if (_filterMode == "blocked" && !e.IsFirewallBlocked) continue;

                    if (q.Length > 0)
                    {
                        bool hit = e.LocalPort.ToString().IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0
                            || (e.RemotePort > 0 && e.RemotePort.ToString().IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0)
                            || (e.ProcessName != null && e.ProcessName.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0)
                            || (e.LocalAddress != null && e.LocalAddress.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0)
                            || (e.RemoteAddress != null && e.RemoteAddress.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0)
                            || (e.ServiceTag != null && e.ServiceTag.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0)
                            || e.Pid.ToString().IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0;
                        if (!hit) continue;
                    }
                    list.Add(e);
                }
            }
            _filtered = list;
            if (_table != null) _table.SetItems(_filtered);
        }

        private void ShowToast(string msg)
        {
            _statusToast = msg ?? "";
            _toastUntil = DateTime.UtcNow.AddSeconds(4.0);
            Invalidate();
        }

        private void HandleRowCopy(PortEntry e)
        {
            if (e == null) return;
            string line = Loc.F("{0}  {1} -> {2}  [{3}]  {4} (PID {5})  {6}",
                e.Protocol, e.LocalEndpoint, e.RemoteEndpoint, e.State, e.ProcessName, e.Pid, e.ExePath ?? "");
            try
            {
                Clipboard.SetText(line.Trim());
                ShowToast(Loc.T(_lang, "Copied: " + e.ProcessName + " :" + e.LocalPort,
                    "Kopyalandı: " + e.ProcessName + " :" + e.LocalPort,
                    "已复制: " + e.ProcessName + " :" + e.LocalPort));
            }
            catch { }
        }

        private void HandleRowAction(PortEntry e, RowActionKind kind)
        {
            if (e == null) return;
            if (kind == RowActionKind.OpenFolder)
            {
                if (string.IsNullOrEmpty(e.ExePath)) return;
                if (_launch.DemoMode)
                {
                    ShowToast(Loc.T(_lang, "Demo: Explorer -> " + Path.GetFileName(e.ExePath),
                        "Demo: Klasör -> " + Path.GetFileName(e.ExePath),
                        "演示: 打开目录 -> " + Path.GetFileName(e.ExePath)));
                    return;
                }
                if (File.Exists(e.ExePath))
                {
                    try { Process.Start("explorer.exe", "/select,\"" + e.ExePath + "\""); } catch { }
                }
                else ShowToast(Loc.T(_lang, "Executable path not accessible.", "Dosya yoluna erişilemedi.", "无法访问该程序路径。"));
                return;
            }

            if (kind == RowActionKind.Kill)
            {
                if (e.IsCriticalSystem)
                {
                    ShowToast(Loc.T(_lang, "Protected Windows system process.", "Korumalı Windows sistem işlemi.", "受保护的系统进程，无法结束。"));
                    return;
                }
                string msg = Loc.T(_lang,
                    Loc.F("Terminate process \"{0}\" (PID {1}) holding port {2}?", e.ProcessName, e.Pid, e.LocalPort),
                    Loc.F("\"{0}\" (PID {1}) işlemini sonlandırıp {2} portunu serbest bırakmak istiyor musunuz?", e.ProcessName, e.Pid, e.LocalPort),
                    Loc.F("确定要结束占用端口 {2} 的进程 \"{0}\" (PID {1}) 吗？", e.ProcessName, e.Pid, e.LocalPort));
                DialogResult dr = MessageBox.Show(this, msg, "PortWatch", MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2);
                if (dr != DialogResult.Yes) return;

                if (_launch.DemoMode)
                {
                    _snapshot.Entries.RemoveAll(delegate(PortEntry x) { return x.Pid == e.Pid; });
                    RecountDemoSnapshot();
                    ApplyFilterAndBind();
                    ShowToast(Loc.T(_lang, "Demo: Terminated " + e.ProcessName, "Demo: Sonlandırıldı -> " + e.ProcessName, "演示: 已结束 " + e.ProcessName));
                    return;
                }

                string err;
                if (NetworkEngine.KillProcessSafely(e.Pid, e.ProcessName, out err))
                {
                    ShowToast(Loc.T(_lang, "Killed " + e.ProcessName + " (#" + e.Pid + ")",
                        "Sonlandırıldı: " + e.ProcessName + " (#" + e.Pid + ")",
                        "已结束进程: " + e.ProcessName + " (#" + e.Pid + ")"));
                    TriggerRefresh();
                }
                else
                {
                    if (!WinNative.IsAdministrator())
                    {
                        PromptElevate(Loc.T(_lang,
                            "Access denied killing this process. Restart PortWatch as Administrator?",
                            "Bu işlemi sonlandırmak için Yönetici izni gerekiyor. Yönetici olarak yeniden başlatılsın mı?",
                            "结束此进程需要管理员权限，是否以管理员身份重启 PortWatch？"));
                    }
                    else
                    {
                        ShowToast(Loc.T(_lang, "Kill failed: " + err, "Sonlandırılamadı: " + err, "结束失败: " + err));
                    }
                }
                return;
            }

            if (kind == RowActionKind.BlockToggle)
            {
                if (e.IsCriticalSystem || string.IsNullOrEmpty(e.ExePath)) return;
                bool wantBlock = !e.IsFirewallBlocked;

                if (_launch.DemoMode)
                {
                    foreach (PortEntry x in _snapshot.Entries)
                        if (string.Equals(x.ExePath, e.ExePath, StringComparison.OrdinalIgnoreCase))
                            x.IsFirewallBlocked = wantBlock;
                    RecountDemoSnapshot();
                    ApplyFilterAndBind();
                    ShowToast(wantBlock
                        ? Loc.T(_lang, "Demo: Blocked " + e.ProcessName + " in Windows Firewall", "Demo: Engellendi -> " + e.ProcessName, "演示: 已拦截 " + e.ProcessName)
                        : Loc.T(_lang, "Demo: Unblocked " + e.ProcessName, "Demo: Engel kaldırıldı -> " + e.ProcessName, "演示: 已放行 " + e.ProcessName));
                    return;
                }

                if (!WinNative.IsAdministrator())
                {
                    PromptElevate(Loc.T(_lang,
                        "Windows Firewall rules require Administrator privileges. Restart PortWatch as Administrator?",
                        "Windows Güvenlik Duvarı kuralları için Yönetici izni gerekiyor. Yönetici olarak yeniden başlatılsın mı?",
                        "修改 Windows 防火墙规则需要管理员权限，是否以管理员身份重启 PortWatch？"));
                    return;
                }

                string targetPath = e.ExePath;
                string targetName = e.ProcessName;
                ShowToast(Loc.T(_lang, "Updating Windows Firewall...", "Güvenlik Duvarı güncelleniyor...", "正在更新 Windows 防火墙..."));
                ThreadPool.QueueUserWorkItem(delegate
                {
                    string det;
                    bool ok = wantBlock ? FirewallEngine.BlockExecutable(targetPath, out det)
                                        : FirewallEngine.UnblockExecutable(targetPath, out det);
                    if (!IsDisposed && !Disposing && IsHandleCreated)
                    {
                        try
                        {
                            BeginInvoke((MethodInvoker)delegate
                            {
                                if (IsDisposed || Disposing) return;
                                if (ok)
                                {
                                    ShowToast(wantBlock
                                        ? Loc.T(_lang, "Firewall blocked: " + targetName, "Güvenlik Duvarı engelledi: " + targetName, "已通过防火墙拦截: " + targetName)
                                        : Loc.T(_lang, "Firewall unblocked: " + targetName, "Engel kaldırıldı: " + targetName, "已从防火墙放行: " + targetName));
                                    TriggerRefresh();
                                }
                                else ShowToast(Loc.T(_lang, "Firewall update failed.", "Güvenlik Duvarı güncellenemedi.", "防火墙规则更新失败。"));
                            });
                        }
                        catch { }
                    }
                });
            }
        }

        private void RecountDemoSnapshot()
        {
            _snapshot.ListeningCount = 0;
            _snapshot.EstablishedCount = 0;
            _snapshot.UdpCount = 0;
            HashSet<string> blks = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (PortEntry e in _snapshot.Entries)
            {
                if (e.State == "LISTENING") _snapshot.ListeningCount++;
                else if (e.State == "ESTABLISHED") _snapshot.EstablishedCount++;
                if (e.Protocol == "UDP" || e.Protocol == "UDP6") _snapshot.UdpCount++;
                if (e.IsFirewallBlocked && !string.IsNullOrEmpty(e.ExePath)) blks.Add(e.ExePath);
            }
            _snapshot.BlockedRuleCount = blks.Count;
        }

        private void PromptElevate(string prompt)
        {
            if (MessageBox.Show(this, prompt, "PortWatch", MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) == DialogResult.Yes)
            {
                if (WinNative.RestartAsAdministrator("--handoff --lang=" + _lang))
                {
                    ExitAppCleanly();
                }
            }
        }

        private void ToggleHud()
        {
            _cfg.NetHudEnabled = !_cfg.NetHudEnabled;
            if (!_launch.DemoMode) _cfg.Save();
            SyncHudWindow();
            RebuildTrayMenu();
            Invalidate();
        }

        private void SyncHudWindow()
        {
            if (_cfg.NetHudEnabled)
            {
                if (_hudForm == null || _hudForm.IsDisposed)
                {
                    _hudForm = new NetHudForm();
                    _hudForm.CustomPositionChanged += delegate(int x, int y)
                    {
                        _cfg.NetHudCustomPos = true;
                        _cfg.NetHudX = x;
                        _cfg.NetHudY = y;
                        if (!_launch.DemoMode) _cfg.Save();
                        Invalidate();
                    };
                    _hudForm.LockRequested += delegate
                    {
                        if (_hudForm != null) _hudForm.SetDragUnlocked(false);
                        Invalidate();
                    };
                    _hudForm.Show();
                }
                UpdateHudData();
                if (!_hudForm.Visible) _hudForm.Show();
            }
            else if (_hudForm != null && !_hudForm.IsDisposed)
            {
                _hudForm.SetDragUnlocked(false);
                _hudForm.Hide();
            }
        }

        private void UpdateHudData()
        {
            if (_hudForm != null && !_hudForm.IsDisposed && _cfg.NetHudEnabled)
                _hudForm.UpdateHud(_cfg, _snapshot, _scale);
        }

        private void SwitchLanguage(string lang)
        {
            if (_lang == lang) return;
            _lang = lang;
            _cfg.Language = lang;
            if (!_launch.DemoMode) _cfg.Save();
            RebuildFonts();
            UpdateSearchCue();
            RebuildTrayMenu();
            UpdateHudData();
            Invalidate();
        }

        private void CopyFilteredTable()
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("PROTO\tLOCAL\tREMOTE\tSTATE\tPROCESS\tPID\tBLOCKED\tPATH");
            foreach (PortEntry e in _filtered)
            {
                sb.AppendLine(Loc.F("{0}\t{1}\t{2}\t{3}\t{4}\t{5}\t{6}\t{7}",
                    e.Protocol, e.LocalEndpoint, e.RemoteEndpoint, e.State, e.ProcessName, e.Pid,
                    e.IsFirewallBlocked ? "YES" : "NO", e.ExePath ?? ""));
            }
            try
            {
                Clipboard.SetText(sb.ToString());
                ShowToast(Loc.T(_lang,
                    Loc.F("Copied {0} connections to clipboard.", _filtered.Count),
                    Loc.F("{0} bağlantı panoya kopyalandı.", _filtered.Count),
                    Loc.F("已复制 {0} 条连接到剪贴板。", _filtered.Count)));
            }
            catch { }
        }

        private void RestoreFromTray()
        {
            ShowInTaskbar = true;
            Show();
            if (WindowState == FormWindowState.Minimized) WindowState = FormWindowState.Normal;
            Activate();
            BringToFront();
        }

        private void ExitAppCleanly()
        {
            _allowRealClose = true;
            Close();
        }

        private int MeasurePillW(Graphics g, string text, Font f, int padX)
        {
            return (int)Math.Ceiling(g.MeasureString(text, f).Width) + S(padX * 2);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
            g.Clear(UiTheme.Bg);
            _hitRects.Clear();

            int pad = S(16);
            int right = ClientSize.Width - pad;

            using (StringFormat sfNear = new StringFormat(StringFormatFlags.NoWrap))
            using (StringFormat sfCenter = new StringFormat(StringFormatFlags.NoWrap))
            using (StringFormat sfFar = new StringFormat(StringFormatFlags.NoWrap))
            {
                sfNear.LineAlignment = StringAlignment.Center;
                sfNear.Trimming = StringTrimming.EllipsisCharacter;
                sfCenter.Alignment = StringAlignment.Center;
                sfCenter.LineAlignment = StringAlignment.Center;
                sfFar.Alignment = StringAlignment.Far;
                sfFar.LineAlignment = StringAlignment.Center;

                // ── 1. HEADER BAR ──
                int hdrH = S(56);
                using (SolidBrush bHdr = new SolidBrush(UiTheme.Surface))
                using (Pen pB = new Pen(UiTheme.Border, 1f))
                {
                    g.FillRectangle(bHdr, 0, 0, ClientSize.Width, hdrH);
                    g.DrawLine(pB, 0, hdrH - 1, ClientSize.Width, hdrH - 1);
                }

                // Radar icon
                RectangleF icoRc = new RectangleF(pad, S(13), S(30), S(30));
                UiTheme.FillRounded(g, icoRc, S(8), Color.FromArgb(20, 34, 48));
                UiTheme.DrawRounded(g, icoRc, S(8), UiTheme.Emerald, 1.3f);
                using (Pen pR1 = new Pen(Color.FromArgb(140, UiTheme.Cyan), 1.3f))
                using (Pen pR2 = new Pen(UiTheme.Emerald, 1.4f))
                using (SolidBrush bDot = new SolidBrush(UiTheme.Cyan))
                {
                    float cx = icoRc.X + icoRc.Width / 2f;
                    float cy = icoRc.Y + icoRc.Height / 2f;
                    g.DrawEllipse(pR1, cx - S(9), cy - S(9), S(18), S(18));
                    g.DrawEllipse(pR2, cx - S(5), cy - S(5), S(10), S(10));
                    g.FillEllipse(bDot, cx - S(2), cy - S(2), S(4), S(4));
                }

                // Title + version pill
                int titleX = pad + S(38);
                SizeF tSz = g.MeasureString("PortWatch", _fTitle);
                using (SolidBrush bt = new SolidBrush(UiTheme.Text))
                    g.DrawString("PortWatch", _fTitle, bt, new RectangleF(titleX, S(6), tSz.Width + S(8), S(26)), sfNear);

                string verTxt = "v" + AppVersion;
                float vw = g.MeasureString(verTxt, _fVer).Width + S(10);
                RectangleF verRc = new RectangleF(titleX + tSz.Width + S(4), S(11), vw, S(17));
                UiTheme.FillRounded(g, verRc, S(5), Color.FromArgb(26, UiTheme.Emerald));
                using (SolidBrush bv = new SolidBrush(UiTheme.Emerald))
                    g.DrawString(verTxt, _fVer, bv, verRc, sfCenter);

                string subTxt = Loc.T(_lang, "TCP/UDP Ports · Live Speed · Reversible Firewall",
                    "TCP/UDP Portlar · Canlı Hız · Güvenlik Duvarı",
                    "TCP/UDP 端口 · 实时网速 · 可逆防火墙拦截");
                using (SolidBrush bs = new SolidBrush(UiTheme.Muted))
                    g.DrawString(subTxt, _fSub, bs, new RectangleF(titleX, S(31), S(340), S(18)), sfNear);

                // Header Right Controls (right-to-left dynamic layout)
                int rx = right;
                int btnH = S(28);
                int btnY = (hdrH - btnH) / 2;

                // Language segmented pill: EN | TR | 中文
                int lW1 = S(32), lW2 = S(32), lW3 = S(40);
                int langTotal = lW1 + lW2 + lW3;
                Rectangle langOuter = new Rectangle(rx - langTotal, btnY, langTotal, btnH);
                UiTheme.FillRounded(g, langOuter, S(7), UiTheme.SurfaceAlt);
                UiTheme.DrawRounded(g, langOuter, S(7), UiTheme.Border, 1f);
                DrawLangSeg(g, new Rectangle(langOuter.X, btnY, lW1, btnH), "EN", "en", sfCenter);
                DrawLangSeg(g, new Rectangle(langOuter.X + lW1, btnY, lW2, btnH), "TR", "tr", sfCenter);
                DrawLangSeg(g, new Rectangle(langOuter.X + lW1 + lW2, btnY, lW3, btnH), "中文", "zh", sfCenter);
                rx = langOuter.Left - S(8);

                // Admin badge / Elevate button
                bool isAdmin = WinNative.IsAdministrator();
                string admTxt = isAdmin
                    ? Loc.T(_lang, "Admin", "Yönetici", "管理员")
                    : Loc.T(_lang, "Admin ↗", "Yönetici ↗", "管理员 ↗");
                int admW = MeasurePillW(g, admTxt, _fPill, 10);
                Rectangle admRc = new Rectangle(rx - admW, btnY, admW, btnH);
                DrawHeaderPill(g, "hdr_admin", admRc, admTxt, isAdmin ? UiTheme.Emerald : UiTheme.Amber, isAdmin, sfCenter);
                rx = admRc.Left - S(8);

                // Unlock HUD Drag button (only shown when Mini HUD is ON)
                if (_cfg.NetHudEnabled)
                {
                    bool unlocked = _hudForm != null && !_hudForm.IsDisposed && _hudForm.DragUnlocked;
                    string dragTxt = unlocked
                        ? Loc.T(_lang, "Lock HUD", "HUD Kilitle", "锁定浮窗")
                        : Loc.T(_lang, "Move HUD", "HUD Taşı", "拖动浮窗");
                    int dragW = MeasurePillW(g, dragTxt, _fPill, 10);
                    Rectangle dragRc = new Rectangle(rx - dragW, btnY, dragW, btnH);
                    DrawHeaderPill(g, "hdr_hud_drag", dragRc, dragTxt, unlocked ? UiTheme.Cyan : UiTheme.Muted, unlocked, sfCenter);
                    rx = dragRc.Left - S(8);
                }

                // Mini HUD toggle button
                string hudTxt = _cfg.NetHudEnabled
                    ? Loc.T(_lang, "Mini HUD: ON", "Mini HUD: AÇIK", "网速浮窗: 开")
                    : Loc.T(_lang, "Mini HUD", "Mini HUD", "网速浮窗");
                int hudW = MeasurePillW(g, hudTxt, _fPill, 10);
                Rectangle hudRc = new Rectangle(rx - hudW, btnY, hudW, btnH);
                DrawHeaderPill(g, "hdr_hud", hudRc, hudTxt, _cfg.NetHudEnabled ? UiTheme.Emerald : UiTheme.Cyan, _cfg.NetHudEnabled, sfCenter);

                // ── 2. KPI SUMMARY CARDS (4 clickable cards) ──
                int kpiY = hdrH + S(12);
                int kpiH = S(64);
                int kpiGap = S(10);
                int cardW = (ClientSize.Width - pad * 2 - kpiGap * 3) / 4;

                NetSpeedSnapshot sp = (_snapshot != null && _snapshot.Speed != null) ? _snapshot.Speed : new NetSpeedSnapshot();
                DrawKpiCard(g, "kpi_listening", new Rectangle(pad, kpiY, cardW, kpiH),
                    Loc.T(_lang, "LISTENING PORTS", "DİNLENEN PORTLAR", "监听端口").ToUpper(Loc.Culture(_lang)),
                    (_snapshot != null ? _snapshot.ListeningCount : 0).ToString(),
                    Loc.T(_lang, "Local servers & dev ports", "Yerel sunucu & geliştirme", "本地服务与开发端口"),
                    UiTheme.Emerald, _filterMode == "listening", sfNear);

                DrawKpiCard(g, "kpi_established", new Rectangle(pad + (cardW + kpiGap), kpiY, cardW, kpiH),
                    Loc.T(_lang, "ESTABLISHED", "AKTİF BAĞLANTI", "活跃连接").ToUpper(Loc.Culture(_lang)),
                    (_snapshot != null ? _snapshot.EstablishedCount : 0).ToString(),
                    Loc.T(_lang, "Live remote TCP sessions", "Anlık uzak TCP oturumları", "实时远程 TCP 会话"),
                    UiTheme.Cyan, _filterMode == "established", sfNear);

                string speedVal = "↓ " + NetworkEngine.FormatSpeed(sp.DownBytesPerSec);
                string speedSub = "↑ " + NetworkEngine.FormatSpeed(sp.UpBytesPerSec) +
                    (string.IsNullOrEmpty(sp.ActiveAdapter) ? "" : "  ·  " + sp.ActiveAdapter);
                DrawKpiCard(g, "kpi_speed", new Rectangle(pad + (cardW + kpiGap) * 2, kpiY, cardW, kpiH),
                    Loc.T(_lang, "NETWORK THROUGHPUT", "CANLI AĞ HIZI", "实时网络速率").ToUpper(Loc.Culture(_lang)),
                    speedVal, speedSub, UiTheme.Violet, _cfg.NetHudEnabled, sfNear);

                DrawKpiCard(g, "kpi_blocked", new Rectangle(pad + (cardW + kpiGap) * 3, kpiY, cardW, kpiH),
                    Loc.T(_lang, "FIREWALL BLOCKED", "DUVARDA ENGELLİ", "防火墙已拦截").ToUpper(Loc.Culture(_lang)),
                    (_snapshot != null ? _snapshot.BlockedRuleCount : 0).ToString(),
                    Loc.T(_lang, "Reversible PortWatch rules", "Tek tık geri alınabilir", "一键可逆防火墙规则"),
                    UiTheme.Coral, _filterMode == "blocked", sfNear);

                // ── 3. TOOLBAR (Search + 6 Filters + Pause + Refresh) ──
                int tbY = S(142);
                int tbH = S(32);

                // Search box rounded frame + crisp vector search icon + GDI+ placeholder
                Rectangle searchFrame = new Rectangle(pad, tbY, S(230), tbH);
                _hitRects["search_box"] = searchFrame;
                UiTheme.FillRounded(g, searchFrame, S(7), UiTheme.SurfaceAlt);
                UiTheme.DrawRounded(g, searchFrame, S(7), _searchBox.Focused ? UiTheme.Cyan : UiTheme.Border, 1f);
                using (Pen pMag = new Pen(_searchBox.Focused ? UiTheme.Cyan : UiTheme.Subtle, 1.4f))
                {
                    float mx = searchFrame.X + S(10);
                    float my = searchFrame.Y + S(10);
                    g.DrawEllipse(pMag, mx, my, S(9), S(9));
                    g.DrawLine(pMag, mx + S(7.5f), my + S(7.5f), mx + S(11.5f), my + S(11.5f));
                }
                if (!_searchBox.Visible && _searchBox.Text.Length == 0)
                {
                    string ph = Loc.T(_lang, "Search port, app, IP, PID...", "Port, uygulama, IP, PID ara...", "搜索端口、程序、IP 或 PID...");
                    using (SolidBrush bPh = new SolidBrush(UiTheme.Subtle))
                        g.DrawString(ph, _fSearch, bPh, new RectangleF(searchFrame.X + S(27), searchFrame.Y, searchFrame.Width - S(34), searchFrame.Height), sfNear);
                }
                if (!string.IsNullOrEmpty(_searchBox.Text))
                {
                    Rectangle clrRc = new Rectangle(searchFrame.Right - S(24), tbY + S(5), S(20), S(22));
                    _hitRects["search_clear"] = clrRc;
                    using (SolidBrush bc = new SolidBrush(_hoverId == "search_clear" ? UiTheme.Text : UiTheme.Muted))
                        g.DrawString("×", _fPill, bc, clrRc, sfCenter);
                }

                // Filter pills
                int fx = searchFrame.Right + S(10);
                string[,] filters = new string[,]
                {
                    { "all", Loc.T(_lang, "All", "Tümü", "全部") },
                    { "listening", Loc.T(_lang, "Listening", "Dinleyen", "监听") },
                    { "established", Loc.T(_lang, "Established", "Bağlı", "已连接") },
                    { "tcp", "TCP" },
                    { "udp", "UDP" },
                    { "blocked", Loc.T(_lang, "Blocked", "Engelli", "已拦截") }
                };

                // Right-side Pause + Refresh measured first so we never collide
                string refTxt = Loc.T(_lang, "Refresh", "Yenile", "刷新");
                int refW = MeasurePillW(g, refTxt, _fPill, 10);
                Rectangle refRc = new Rectangle(right - refW, tbY, refW, tbH);

                string pauseTxt = _paused
                    ? Loc.T(_lang, "Resume", "Devam", "继续")
                    : Loc.T(_lang, "Pause", "Duraklat", "暂停");
                int pauseW = MeasurePillW(g, pauseTxt, _fPill, 10);
                Rectangle pauseRc = new Rectangle(refRc.Left - S(6) - pauseW, tbY, pauseW, tbH);

                int maxFilterRight = pauseRc.Left - S(8);
                for (int i = 0; i < filters.GetLength(0); i++)
                {
                    string id = filters[i, 0];
                    string lbl = filters[i, 1];
                    int pw = MeasurePillW(g, lbl, _fPill, 9);
                    if (fx + pw > maxFilterRight) break;
                    Rectangle pr = new Rectangle(fx, tbY, pw, tbH);
                    DrawFilterPill(g, "flt_" + id, pr, lbl, _filterMode == id, id == "blocked" ? UiTheme.Coral : UiTheme.Cyan, sfCenter);
                    fx = pr.Right + S(5);
                }

                DrawHeaderPill(g, "tb_pause", pauseRc, pauseTxt, _paused ? UiTheme.Amber : UiTheme.Muted, _paused, sfCenter);
                DrawHeaderPill(g, "tb_refresh", refRc, refTxt, UiTheme.Emerald, false, sfCenter);

                // ── 4. FOOTER BAR ──
                int footY = ClientSize.Height - S(42);
                int footH = S(34);

                // Right footer actions
                int frx = right;
                string copyTxt = Loc.T(_lang, "Copy List", "Listeyi Kopyala", "复制列表");
                int copyW = MeasurePillW(g, copyTxt, _fPill, 9);
                Rectangle copyRc = new Rectangle(frx - copyW, footY + S(3), copyW, S(26));
                DrawHeaderPill(g, "ft_copy", copyRc, copyTxt, UiTheme.Cyan, false, sfCenter);
                frx = copyRc.Left - S(8);

                string trayTxt = Loc.T(_lang, "Close to Tray", "Tepsiye Küçült", "关闭到托盘") + ": " +
                    (_cfg.MinimizeToTray ? Loc.T(_lang, "ON", "AÇIK", "开") : Loc.T(_lang, "OFF", "KAPALI", "关"));
                int trayW = MeasurePillW(g, trayTxt, _fPill, 9);
                Rectangle trayRc = new Rectangle(frx - trayW, footY + S(3), trayW, S(26));
                DrawHeaderPill(g, "ft_tray", trayRc, trayTxt, _cfg.MinimizeToTray ? UiTheme.Emerald : UiTheme.Muted, _cfg.MinimizeToTray, sfCenter);
                frx = trayRc.Left - S(8);

                if (_cfg.NetHudEnabled)
                {
                    string mgTxt = _cfg.NetHudCustomPos
                        ? Loc.T(_lang, "Reset HUD Pos", "HUD Konum Sıfırla", "重置浮窗位置")
                        : Loc.T(_lang, "HUD Edge: " + _cfg.NetHudMargin + "px", "HUD Kenar: " + _cfg.NetHudMargin + "px", "浮窗边距: " + _cfg.NetHudMargin + "px");
                    int mgW = MeasurePillW(g, mgTxt, _fPill, 9);
                    Rectangle mgRc = new Rectangle(frx - mgW, footY + S(3), mgW, S(26));
                    DrawHeaderPill(g, "ft_hud_edge", mgRc, mgTxt, UiTheme.Violet, false, sfCenter);
                    frx = mgRc.Left - S(10);
                }

                // Left footer status / toast
                bool showToast = !string.IsNullOrEmpty(_statusToast) && DateTime.UtcNow < _toastUntil;
                int totalCnt = _snapshot != null && _snapshot.Entries != null ? _snapshot.Entries.Count : 0;
                string footLeft = showToast ? _statusToast :
                    Loc.T(_lang,
                        Loc.F("Showing {0} of {1} sockets · {2} · Double-click row to copy", _filtered.Count, totalCnt, _paused ? "Paused" : (_launch.DemoMode ? "Demo Mode" : "Live 1.5s")),
                        Loc.F("{1} bağlantıdan {0} tanesi gösteriliyor · {2} · Kopyalamak için çift tıkla", _filtered.Count, totalCnt, _paused ? "Duraklatıldı" : (_launch.DemoMode ? "Demo Modu" : "Canlı 1.5s")),
                        Loc.F("显示 {0} / {1} 条连接 · {2} · 双击行复制详情", _filtered.Count, totalCnt, _paused ? "已暂停" : (_launch.DemoMode ? "演示模式" : "实时 1.5s")));

                using (SolidBrush bF = new SolidBrush(showToast ? UiTheme.Emerald : UiTheme.Muted))
                    g.DrawString(footLeft, _fFooter, bF, new RectangleF(pad, footY, Math.Max(S(120), frx - pad), footH), sfNear);
            }
        }

        private void DrawLangSeg(Graphics g, Rectangle rc, string label, string code, StringFormat sfCenter)
        {
            string id = "lang_" + code;
            _hitRects[id] = rc;
            bool active = string.Equals(_lang, code, StringComparison.OrdinalIgnoreCase);
            bool hov = _hoverId == id;
            if (active)
                UiTheme.FillRounded(g, new Rectangle(rc.X + 2, rc.Y + 2, rc.Width - 4, rc.Height - 4), S(5), UiTheme.Cyan);
            else if (hov)
                UiTheme.FillRounded(g, new Rectangle(rc.X + 2, rc.Y + 2, rc.Width - 4, rc.Height - 4), S(5), UiTheme.HoverRow);
            using (SolidBrush b = new SolidBrush(active ? Color.FromArgb(12, 18, 28) : (hov ? UiTheme.Text : UiTheme.Muted)))
                g.DrawString(label, _fPill, b, rc, sfCenter);
        }

        private void DrawHeaderPill(Graphics g, string id, Rectangle rc, string text, Color accent, bool active, StringFormat sfCenter)
        {
            _hitRects[id] = rc;
            bool hov = _hoverId == id;
            Color fill = active ? Color.FromArgb(34, accent) : (hov ? UiTheme.HoverRow : UiTheme.SurfaceAlt);
            Color stroke = active ? accent : (hov ? UiTheme.BorderLight : UiTheme.Border);
            Color fg = active ? accent : (hov ? UiTheme.Text : UiTheme.Muted);
            UiTheme.FillRounded(g, rc, S(7), fill);
            UiTheme.DrawRounded(g, rc, S(7), stroke, 1f);
            using (SolidBrush bt = new SolidBrush(fg))
                g.DrawString(text, _fPill, bt, rc, sfCenter);
        }

        private void DrawFilterPill(Graphics g, string id, Rectangle rc, string text, bool active, Color accent, StringFormat sfCenter)
        {
            _hitRects[id] = rc;
            bool hov = _hoverId == id;
            Color fill = active ? Color.FromArgb(34, accent) : (hov ? UiTheme.HoverRow : UiTheme.Surface);
            Color stroke = active ? accent : (hov ? UiTheme.BorderLight : UiTheme.Border);
            Color fg = active ? UiTheme.Text : (hov ? UiTheme.Text : UiTheme.Muted);
            UiTheme.FillRounded(g, rc, S(7), fill);
            UiTheme.DrawRounded(g, rc, S(7), stroke, 1f);
            using (SolidBrush bt = new SolidBrush(fg))
                g.DrawString(text, _fPill, bt, rc, sfCenter);
        }

        private void DrawKpiCard(Graphics g, string id, Rectangle rc, string label, string val, string sub, Color accent, bool active, StringFormat sfNear)
        {
            _hitRects[id] = rc;
            bool hov = _hoverId == id;
            UiTheme.FillRounded(g, rc, S(9), hov ? UiTheme.SurfaceAlt : UiTheme.Surface);
            UiTheme.DrawRounded(g, rc, S(9), active ? accent : (hov ? UiTheme.BorderLight : UiTheme.Border), active ? 1.4f : 1f);

            // Left accent bar
            UiTheme.FillRounded(g, new RectangleF(rc.X + S(1), rc.Y + S(10), S(3.5f), rc.Height - S(20)), S(2), accent);

            int ix = rc.X + S(14);
            int iw = rc.Width - S(22);
            using (SolidBrush bl = new SolidBrush(UiTheme.Muted))
            using (SolidBrush bv = new SolidBrush(UiTheme.Text))
            using (SolidBrush bs = new SolidBrush(UiTheme.Subtle))
            {
                g.DrawString(label, _fKpiLbl, bl, new RectangleF(ix, rc.Y + S(6), iw, S(14)), sfNear);
                g.DrawString(val, _fKpiVal, bv, new RectangleF(ix, rc.Y + S(19), iw, S(24)), sfNear);
                g.DrawString(sub, _fKpiSub, bs, new RectangleF(ix, rc.Y + S(44), iw, S(15)), sfNear);
            }
        }

        private string HitTest(Point pt)
        {
            Rectangle clr;
            if (_hitRects.TryGetValue("search_clear", out clr) && clr.Contains(pt)) return "search_clear";
            foreach (KeyValuePair<string, Rectangle> kv in _hitRects)
                if (kv.Value.Contains(pt)) return kv.Key;
            return "";
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            string h = HitTest(e.Location);
            if (h != _hoverId)
            {
                _hoverId = h;
                Cursor = string.IsNullOrEmpty(h) ? Cursors.Default : (h == "search_box" ? Cursors.IBeam : Cursors.Hand);
                Invalidate();
            }
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            if (!string.IsNullOrEmpty(_hoverId))
            {
                _hoverId = "";
                Cursor = Cursors.Default;
                Invalidate();
            }
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button != MouseButtons.Left) return;
            string id = HitTest(e.Location);
            if (string.IsNullOrEmpty(id)) return;

            if (id == "lang_en") SwitchLanguage("en");
            else if (id == "lang_tr") SwitchLanguage("tr");
            else if (id == "lang_zh") SwitchLanguage("zh");
            else if (id == "hdr_hud" || id == "kpi_speed") ToggleHud();
            else if (id == "hdr_hud_drag")
            {
                if (_hudForm != null && !_hudForm.IsDisposed)
                {
                    bool next = !_hudForm.DragUnlocked;
                    _hudForm.SetDragUnlocked(next);
                    ShowToast(next
                        ? Loc.T(_lang, "HUD unlocked: drag pill anywhere (right-click to lock).",
                            "HUD serbest: İstediğiniz yere sürükleyin (kilitlemek için sağ tık).",
                            "浮窗已解锁：可自由拖动到任意位置（右键重新锁定）。")
                        : Loc.T(_lang, "HUD locked (click-through).", "HUD kilitlendi (tıklama geçirgen).", "浮窗已锁定（鼠标穿透）。"));
                    Invalidate();
                }
            }
            else if (id == "hdr_admin")
            {
                if (!WinNative.IsAdministrator())
                    PromptElevate(Loc.T(_lang, "Restart PortWatch with Administrator privileges for full Firewall & Process control?",
                        "Tam Güvenlik Duvarı ve İşlem kontrolü için PortWatch Yönetici olarak yeniden başlatılsın mı?",
                        "是否以管理员身份重启 PortWatch 以启用完整防火墙与进程控制？"));
                else
                    ShowToast(Loc.T(_lang, "Running as Administrator (full access).", "Yönetici olarak çalışıyor (tam yetki).", "已以管理员身份运行（完整权限）。"));
            }
            else if (id == "kpi_listening") { _filterMode = (_filterMode == "listening") ? "all" : "listening"; ApplyFilterAndBind(); Invalidate(); }
            else if (id == "kpi_established") { _filterMode = (_filterMode == "established") ? "all" : "established"; ApplyFilterAndBind(); Invalidate(); }
            else if (id == "kpi_blocked") { _filterMode = (_filterMode == "blocked") ? "all" : "blocked"; ApplyFilterAndBind(); Invalidate(); }
            else if (id.StartsWith("flt_")) { _filterMode = id.Substring(4); ApplyFilterAndBind(); Invalidate(); }
            else if (id == "search_box") { _searchBox.Visible = true; _searchBox.Focus(); Invalidate(); }
            else if (id == "search_clear") { _searchBox.Text = ""; _searchBox.Visible = true; _searchBox.Focus(); Invalidate(); }
            else if (id == "tb_pause") { _paused = !_paused; Invalidate(); }
            else if (id == "tb_refresh")
            {
                bool wasPaused = _paused;
                _paused = false;
                TriggerRefresh();
                _paused = wasPaused;
                ShowToast(Loc.T(_lang, "Refreshed.", "Yenilendi.", "已刷新。"));
            }
            else if (id == "ft_tray")
            {
                _cfg.MinimizeToTray = !_cfg.MinimizeToTray;
                if (!_launch.DemoMode) _cfg.Save();
                Invalidate();
            }
            else if (id == "ft_copy") CopyFilteredTable();
            else if (id == "ft_hud_edge")
            {
                if (_cfg.NetHudCustomPos)
                {
                    _cfg.NetHudCustomPos = false;
                    _cfg.NetHudMargin = 0;
                }
                else
                {
                    _cfg.NetHudMargin = (_cfg.NetHudMargin == 0) ? 4 : ((_cfg.NetHudMargin == 4) ? 10 : 0);
                }
                if (!_launch.DemoMode) _cfg.Save();
                UpdateHudData();
                Invalidate();
            }
        }
    }

    internal static class Program
    {
        [STAThread]
        private static void Main(string[] args)
        {
            WinNative.EnableDpiAwareness();
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            LaunchConfig launch = LaunchConfig.Parse(args);
            Mutex mutex = null;
            bool ownsMutex = false;

            if (!launch.DemoMode)
            {
                try
                {
                    mutex = new Mutex(true, @"Local\PortWatch_SingleInstance_v1", out ownsMutex);
                    if (!ownsMutex && launch.HandoffWait)
                    {
                        try { ownsMutex = mutex.WaitOne(3500, false); }
                        catch (AbandonedMutexException) { ownsMutex = true; }
                    }
                    if (!ownsMutex)
                    {
                        if (WinNative.WM_SHOW_EXISTING != 0)
                            WinNative.PostMessage(WinNative.HWND_BROADCAST, WinNative.WM_SHOW_EXISTING, IntPtr.Zero, IntPtr.Zero);
                        return;
                    }
                }
                catch { }
            }

            try
            {
                Application.Run(new MainForm(launch));
            }
            finally
            {
                if (mutex != null)
                {
                    if (ownsMutex) { try { mutex.ReleaseMutex(); } catch { } }
                    mutex.Dispose();
                }
            }
        }
    }
}
