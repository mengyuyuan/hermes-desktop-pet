using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using System.Runtime.InteropServices;
using System.Collections.Generic;
using System.Net;

namespace HongjunPet
{
    static class Program
    {
        [STAThread]
        static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new PetForm());
        }
    }

    public class PetForm : Form
    {
        private const int WS_EX_LAYERED = 0x80000;
        private const int WS_EX_TOOLWINDOW = 0x80;
        private const int PET_W = 140;
        private const int PET_H = 200;

        [DllImport("user32.dll")]
        static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);
        [DllImport("user32.dll")]
        static extern int GetWindowLong(IntPtr hWnd, int nIndex);

        // ====== 状态 ======
        private System.Windows.Forms.Timer _animTimer;
        private System.Windows.Forms.Timer _bridgeTimer;
        private int _animFrame = 0;
        private int _bobPhase = 0;
        private bool _blinking = false;
        private int _blinkTimer = 0;
        private bool _isSleeping = false;
        private int _idleCounter = 0;
        private bool _mouseInside = false;
        private bool _isDragging = false;
        private Point _dragOffset;
        private Random _rng = new Random();

        private string _hermesStatus = "idle";
        private string _hermesMood = "happy";
        private string _bubbleText = "";
        private bool _bridgeOnline = false;
        private int _offlineCounter = 0;

        // ====== 皮肤 ======
        private Dictionary<string, Bitmap> _skins = new Dictionary<string, Bitmap>();
        private bool _useSkins = false;

        private const string BRIDGE_URL = "http://localhost:9101";
        private WebClient _web;

        // ====== 气泡独立窗口 ======
        private BubbleForm _bubbleWindow;

        public PetForm()
        {
            _web = new WebClient();
            _web.Encoding = System.Text.Encoding.UTF8;

            this.FormBorderStyle = FormBorderStyle.None;
            this.ShowInTaskbar = false;
            this.TopMost = true;
            this.Width = PET_W;
            this.Height = PET_H;
            this.BackColor = Color.Black;
            this.TransparencyKey = Color.Black;
            this.StartPosition = FormStartPosition.Manual;

            var screen = Screen.PrimaryScreen.WorkingArea;
            this.Location = new Point(screen.Right - PET_W - 20, screen.Bottom - PET_H - 30);

            int exStyle = GetWindowLong(this.Handle, -20);
            SetWindowLong(this.Handle, -20, exStyle | WS_EX_LAYERED | WS_EX_TOOLWINDOW);
            this.Opacity = 0.92;

            this.SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.DoubleBuffer | ControlStyles.ResizeRedraw, true);
            this.UpdateStyles();

            _animTimer = new System.Windows.Forms.Timer { Interval = 50 };
            _animTimer.Tick += OnAnimTick;
            _animTimer.Start();

            _bridgeTimer = new System.Windows.Forms.Timer { Interval = 500 };
            _bridgeTimer.Tick += (s, e) => PollBridge();
            _bridgeTimer.Start();

            this.MouseEnter += (s, e) => { _mouseInside = true; _isSleeping = false; _idleCounter = 0; };
            this.MouseLeave += (s, e) => { _mouseInside = false; };
            this.MouseDown += OnMouseDown;
            this.MouseMove += OnMouseMove;
            this.MouseUp += (s, e) => { _isDragging = false; };

            this.Paint += OnPetPaint;
            this.Shown += (s, e) => PollBridge();
            this.FormClosing += (s, e) => { if (_bubbleWindow != null && !_bubbleWindow.IsDisposed) _bubbleWindow.Close(); };

            LoadSkins();
            _bubbleWindow = new BubbleForm();
            _bubbleWindow.Show();
        }

        private void LoadSkins()
        {
            string skinDir = System.IO.Path.Combine(
                System.IO.Path.GetDirectoryName(Application.ExecutablePath), "skins");
            _skins.Clear();
            _useSkins = false;
            if (!System.IO.Directory.Exists(skinDir)) return;

            string[] names = { "normal", "happy", "surprised", "sleepy", "thinking" };
            foreach (string n in names)
            {
                string path = System.IO.Path.Combine(skinDir, n + ".png");
                if (System.IO.File.Exists(path))
                {
                    try { var img = (Bitmap)Image.FromFile(path); _skins[n] = img; _useSkins = true; }
                    catch { }
                }
            }
        }

        private void PollBridge()
        {
            try
            {
                string json = _web.DownloadString(BRIDGE_URL + "/status");
                var state = JsonParse(json);
                if (state == null) return;

                _bridgeOnline = true;
                _offlineCounter = 0;
                _hermesStatus = GetString(state, "status");
                _hermesMood = GetString(state, "mood");

                string newBubble = GetString(state, "bubble");
                _bubbleText = !string.IsNullOrEmpty(newBubble)
                    ? (newBubble.Length > 200 ? newBubble.Substring(0, 198) + "…" : newBubble)
                    : "";
                UpdateBubbleWindow();

                UpdateExpression();
            }
            catch
            {
                _offlineCounter++;
                if (_offlineCounter >= 3)
                {
                    _bridgeOnline = false;
                    _hermesStatus = "offline";
                    if (_offlineCounter == 3) _bubbleText = "网关连不上了...";
                    UpdateBubbleWindow();
                }
            }
        }

        // ====== 气泡窗口定位 ======
        private string _lastBubbleText = "\0";

        private void UpdateBubbleWindow()
        {
            if (_bubbleWindow == null || _bubbleWindow.IsDisposed) return;
            if (_bubbleText == _lastBubbleText) return;
            _lastBubbleText = _bubbleText;

            if (string.IsNullOrEmpty(_bubbleText))
            {
                _bubbleWindow.Hide();
                return;
            }

            _bubbleWindow.SetText(_bubbleText);
            PositionBubbleWindow();
            _bubbleWindow.Show();
            _bubbleWindow.TopMost = true;  // 确保在顶层
        }

        private void PositionBubbleWindow()
        {
            var screen = Screen.PrimaryScreen.WorkingArea;
            int petRight = this.Right;
            int petLeft = this.Left;
            int petTop = this.Top;

            // 优先放在右边
            bool placeRight = (petRight + _bubbleWindow.Width + 10 < screen.Right);
            bool placeLeft = (petLeft - _bubbleWindow.Width - 10 > 0);

            int bx, by = petTop;

            if (placeRight)
            {
                bx = petRight + 8;
            }
            else if (placeLeft)
            {
                bx = petLeft - _bubbleWindow.Width - 8;
            }
            else
            {
                bx = Math.Max(0, Math.Min(petLeft, screen.Right - _bubbleWindow.Width));
            }

            _bubbleWindow.Location = new Point(bx, by);
        }

        private void UpdateExpression()
        {
            _isSleeping = false;
            switch (_hermesStatus)
            {
                case "thinking": _blinkTimer = 999; break;
                case "offline": _isSleeping = true; break;
                default:
                    if (!_mouseInside && string.IsNullOrEmpty(_bubbleText))
                    {
                        _idleCounter++;
                        if (_idleCounter > 200) _isSleeping = true;
                    }
                    break;
            }
        }

        // ====== 动画 ======
        private void OnAnimTick(object sender, EventArgs e)
        {
            _animFrame++;
            _bobPhase = (_bobPhase + 1) % 20;
            _blinkTimer++;
            if (!_hermesStatus.Equals("thinking") && _blinkTimer > 120 && _rng.Next(100) < 25)
            {
                _blinking = true;
                _blinkTimer = 0;
            }
            if (_blinking && _blinkTimer > 4) _blinking = false;
            this.Invalidate();
        }

        // ====== 绘制 ======
        private void OnPetPaint(object sender, PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.HighQuality;
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;

            float bobY = (float)Math.Sin(_bobPhase * Math.PI / 10) * 2.5f;
            float shakeX = _hermesStatus.Equals("thinking") ? (float)Math.Sin(_animFrame * 0.3f) * 1.5f : 0;
            float breathe = 1 + (float)Math.Sin(_animFrame * 0.05f) * 0.012f;
            if (_hermesStatus.Equals("thinking")) breathe = 1 + (float)Math.Sin(_animFrame * 0.12f) * 0.018f;

            g.TranslateTransform(shakeX, bobY);
            g.ScaleTransform(breathe, breathe);
            g.TranslateTransform(PET_W / 2, PET_H - 65);

            string expr;
            if (_isSleeping || _hermesStatus == "offline") expr = "sleepy";
            else if (_hermesStatus == "thinking") expr = "thinking";
            else if (_hermesStatus == "responding") expr = "happy";
            else expr = "normal";

            if (_useSkins && _skins.ContainsKey(expr))
            {
                var skin = _skins[expr];
                g.DrawImage(skin, -skin.Width / 2, -skin.Height / 2, skin.Width, skin.Height);
            }
            else
            {
                DrawFallback(g);
            }
        }

        private void DrawFallback(Graphics g)
        {
            Color c1 = Color.FromArgb(88, 166, 255);
            Color c2 = Color.FromArgb(56, 120, 220);
            Color glowC = Color.FromArgb(40, 80, 180);
            Color pupil = Color.FromArgb(30, 30, 50);
            Color accent = Color.FromArgb(120, 200, 255);
            const int R = 36;

            Color glow = _bridgeOnline ? glowC : Color.FromArgb(180, 60, 60);
            if (_hermesStatus.Equals("thinking")) glow = Color.FromArgb(100, 180, 255);
            if (_hermesStatus.Equals("responding")) glow = Color.FromArgb(80, 220, 120);

            using (var gb = new SolidBrush(Color.FromArgb(20, glow)))
                for (int i = 0; i < 3; i++)
                    g.FillEllipse(gb, -R - 10 - i * 6, -R - 5 - i * 4, (R + 12 + i * 8) * 2, (R + 8 + i * 6) * 2);

            float r = R * (_isSleeping ? 0.85f : 1f);
            var rect = new RectangleF(-r, -r, r * 2, r * 2);
            Color b1 = _hermesStatus.Equals("thinking") ? Color.FromArgb(160, 200, 255) : c1;
            Color b2 = _hermesStatus.Equals("thinking") ? Color.FromArgb(80, 150, 240) : c2;
            if (!_bridgeOnline) { b1 = Color.FromArgb(140, 140, 160); b2 = Color.FromArgb(100, 100, 120); }
            using (var bb = new LinearGradientBrush(rect, b1, b2, LinearGradientMode.ForwardDiagonal))
                g.FillEllipse(bb, rect);
            using (var hl = new SolidBrush(Color.FromArgb(50, accent)))
                g.FillEllipse(hl, -r * 0.5f, -r * 0.7f, r * 0.9f, r * 0.6f);

            if (_isSleeping)
            {
                for (int i = 0; i < 3; i++)
                {
                    float zOff = (float)Math.Sin(_animFrame * 0.08f + i * 1.5f) * 3;
                    using (var zb = new SolidBrush(Color.FromArgb(150 - i * 30, Color.White)))
                    using (var zf = new Font("Segoe UI", (7 + i * 4) * 0.6f, FontStyle.Bold))
                        g.DrawString("z", zf, zb, -r - 8 + i * 8, -r - 10 - i * 11 + zOff);
                }
            }

            if (!_isSleeping)
            {
                float ey = -r * 0.2f, es = r * 0.3f, ew = r * 0.28f;
                bool closed = _blinking && _blinkTimer < 4;
                bool think = _hermesStatus.Equals("thinking");
                for (int side = -1; side <= 1; side += 2)
                {
                    float ex = side * es;
                    if (closed) { using (var cp = new Pen(pupil, 2)) g.DrawArc(cp, ex - ew, ey, ew * 2, ew * 1.2f, 0, 180); }
                    else
                    {
                        float eh = think ? ew * 0.8f : ew * 1.8f;
                        using (var eb = new SolidBrush(Color.White)) g.FillEllipse(eb, ex - ew, ey - ew * 0.6f, ew * 2, eh);
                        float ps = think ? ew * 0.35f : ew * 0.55f;
                        float py = think ? ey - 1 : ey + 1;
                        using (var pb = new SolidBrush(pupil)) g.FillEllipse(pb, ex - ps, py - ps * 0.6f, ps * 2, ps * 1.8f);
                        using (var sb = new SolidBrush(Color.FromArgb(180, Color.White))) g.FillEllipse(sb, ex - ps * 0.6f, py - ps * 0.7f, ps * 0.7f, ps * 0.7f);
                    }
                }
            }
            else
            {
                for (int side = -1; side <= 1; side += 2)
                    using (var sp = new Pen(Color.FromArgb(180, pupil), 1.8f))
                        g.DrawArc(sp, side * r * 0.3f - 6, -r * 0.15f - 2, 12, 8, 0, -180);
            }

            if (_hermesStatus.Equals("responding") || _hermesMood.Equals("happy") || _mouseInside)
                for (int side = -1; side <= 1; side += 2)
                    using (var bb = new SolidBrush(Color.FromArgb(60, 255, 150, 150)))
                        g.FillEllipse(bb, side * r * 0.5f - 6, r * 0.15f, 12, 8);

            float my = r * 0.3f;
            using (var mp = new Pen(pupil, 1.8f))
            {
                if (_hermesStatus.Equals("thinking")) g.DrawArc(mp, -4, my + 1, 8, 5, 0, -180);
                else if (_hermesStatus.Equals("responding") || _hermesMood.Equals("happy") || _mouseInside) g.DrawArc(mp, -6, my - 2, 12, 8, 0, -180);
                else if (_isSleeping) g.DrawArc(mp, -5, my + 2, 10, 5, 0, 180);
                else g.DrawArc(mp, -4, my + 1, 8, 5, 0, -180);
            }

            float antOff = (float)Math.Sin(_animFrame * (_hermesStatus.Equals("thinking") ? 0.2f : 0.1f)) * (_hermesStatus.Equals("thinking") ? 3 : 2);
            for (int side = -1; side <= 1; side += 2)
            {
                float ax = side * r * 0.35f, ay = -r - 2 + antOff * side * 0.5f;
                using (var ap = new Pen(c1, 2.5f))
                    g.DrawCurve(ap, new PointF[] { new PointF(ax, ay + 5), new PointF(ax + side * 6, ay - 8 + antOff), new PointF(ax + side * 2, ay - 14 + antOff * 1.5f) });
                using (var tb = new SolidBrush(accent)) g.FillEllipse(tb, ax + side * 1 - 3, ay - 16 + antOff * 1.5f, 6, 6);
                using (var tg = new SolidBrush(Color.FromArgb(80, Color.White))) g.FillEllipse(tg, ax + side * 1 - 2, ay - 15 + antOff * 1.5f, 4, 4);
            }
        }

        // ====== 交互 ======
        private void OnMouseDown(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                _isSleeping = false; _idleCounter = 0;
                _isDragging = false;
                _dragOffset = new Point(Cursor.Position.X - this.Left, Cursor.Position.Y - this.Top);
                string[] reacts = { "嘿嘿～", "在呢！", "戳我干嘛～", "痒～", "忙着呢", "诶嘿" };
                _bubbleText = reacts[_rng.Next(reacts.Length)];
                UpdateBubbleWindow();
            }
            else if (e.Button == MouseButtons.Right)
            {
                var menu = new ContextMenuStrip();
                menu.Items.Add("说句话", null, (s2, e2) => {
                    string[] msgs = { "你好呀！", "今天怎么样～", "我在哦", "盯——" };
                    _bubbleText = msgs[_rng.Next(msgs.Length)]; UpdateBubbleWindow();
                });
                menu.Items.Add("重置位置", null, (s2, e2) => {
                    var sc = Screen.PrimaryScreen.WorkingArea;
                    this.Location = new Point(sc.Right - PET_W - 20, sc.Bottom - PET_H - 30);
                    PositionBubbleWindow();
                });
                menu.Items.Add("退出", null, (s2, e2) => { Application.Exit(); });
                menu.Show(this, e.Location);
            }
        }

        private void OnMouseMove(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left) return;
            if (!_isDragging)
            {
                int dx = Cursor.Position.X - (this.Left + _dragOffset.X);
                int dy = Cursor.Position.Y - (this.Top + _dragOffset.Y);
                if (Math.Abs(dx) < 5 && Math.Abs(dy) < 5) return;
                _isDragging = true;
            }
            var screen = Screen.PrimaryScreen.WorkingArea;
            int nx = Math.Max(0, Math.Min(screen.Right - PET_W - 5, Cursor.Position.X - _dragOffset.X));
            int ny = Math.Max(0, Math.Min(screen.Bottom - PET_H - 5, Cursor.Position.Y - _dragOffset.Y));
            this.Location = new Point(nx, ny);
            PositionBubbleWindow();
        }

        // ====== 鼠标穿透 ======
        protected override void WndProc(ref Message m)
        {
            if (m.Msg == 0x84)
            {
                Point pt = this.PointToClient(Cursor.Position);
                int hitY = PET_H - 130;
                if (pt.X > 5 && pt.X < PET_W - 5 && pt.Y > hitY && pt.Y < PET_H - 5)
                { m.Result = (IntPtr)1; return; }
                m.Result = (IntPtr)(-1);
                return;
            }
            base.WndProc(ref m);
        }

        // ====== JSON ======
        private string GetString(Dictionary<string, object> dict, string key)
        {
            if (dict == null) return "";
            object val;
            return dict.TryGetValue(key, out val) && val != null ? val.ToString() : "";
        }

        private Dictionary<string, object> JsonParse(string json)
        {
            var r = new Dictionary<string, object>();
            json = json.Trim();
            if (!json.StartsWith("{") || !json.EndsWith("}")) return null;
            json = json.Substring(1, json.Length - 2);
            int i = 0;
            while (i < json.Length)
            {
                while (i < json.Length && (json[i] == ' ' || json[i] == ',' || json[i] == '\n' || json[i] == '\r')) i++;
                if (i >= json.Length) break;
                if (json[i] != '"') break;
                i++; string key = "";
                while (i < json.Length && json[i] != '"') { if (json[i] == '\\') { i++; if (i < json.Length) key += json[i]; } else key += json[i]; i++; }
                i++;
                while (i < json.Length && json[i] != ':') i++; i++;
                while (i < json.Length && json[i] == ' ') i++;
                if (i < json.Length && json[i] == '"')
                {
                    i++; string val = "";
                    while (i < json.Length && json[i] != '"') { if (json[i] == '\\') { i++; if (i < json.Length) val += json[i]; } else val += json[i]; i++; }
                    i++; r[key] = val;
                }
                else if (i < json.Length && (json[i] == 't' || json[i] == 'f'))
                { if (json.Substring(i).StartsWith("true")) { r[key] = "true"; i += 4; } else { r[key] = "false"; i += 5; } }
            }
            return r;
        }
    }

    // ====== 独立气泡窗口 ======
    public class BubbleForm : Form
    {
        private const int WS_EX_LAYERED = 0x80000;
        private const int WS_EX_TOOLWINDOW = 0x80;
        private const int WS_EX_NOACTIVATE = 0x8000000;

        [DllImport("user32.dll")]
        static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);
        [DllImport("user32.dll")]
        static extern int GetWindowLong(IntPtr hWnd, int nIndex);

        private string _text = "";
        private const int MIN_W = 60;
        private const int MAX_W = 380;
        private int _bw, _bh;

        public BubbleForm()
        {
            this.FormBorderStyle = FormBorderStyle.None;
            this.ShowInTaskbar = false;
            this.TopMost = true;
            this.BackColor = Color.Black;
            this.TransparencyKey = Color.Black;
            this.StartPosition = FormStartPosition.Manual;
            this.ShowIcon = false;
            this.Width = MIN_W;
            this.Height = 30;

            int exStyle = GetWindowLong(this.Handle, -20);
            SetWindowLong(this.Handle, -20, exStyle | WS_EX_LAYERED | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE);
            this.Opacity = 0.92;

            this.SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.DoubleBuffer | ControlStyles.ResizeRedraw, true);
            this.Paint += OnPaint;
        }

        public void SetText(string text)
        {
            if (text == _text) return;
            _text = text;
            CalculateSize();
            this.Invalidate();
        }

        private void CalculateSize()
        {
            using (var font = new Font("Microsoft YaHei", 9))
            using (var g = this.CreateGraphics())
            {
                var sz = g.MeasureString(_text, font, MAX_W - 20);
                _bw = Math.Max(MIN_W, (int)sz.Width + 20);
                _bh = Math.Max(24, (int)sz.Height + 16);
            }
            this.Width = _bw;
            this.Height = _bh;
        }

        private void OnPaint(object sender, PaintEventArgs e)
        {
            if (string.IsNullOrEmpty(_text)) return;
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.HighQuality;

            var r = new RectangleF(2, 2, _bw - 4, _bh - 4);

            using (var sb = new SolidBrush(Color.FromArgb(30, 0, 0, 0)))
            using (var sp = RoundedRect(new RectangleF(4, 4, _bw - 8, _bh - 8), 8))
                g.FillPath(sb, sp);

            using (var bb = new SolidBrush(Color.FromArgb(235, 248, 250, 255)))
            using (var bp = RoundedRect(r, 8))
                g.FillPath(bb, bp);

            using (var borderPen = new Pen(Color.FromArgb(60, 180, 120, 255), 1f))
            using (var bp = RoundedRect(r, 8))
                g.DrawPath(borderPen, bp);

            var textRect = new Rectangle(8, 5, _bw - 16, _bh - 10);
            TextRenderer.DrawText(g, _text, new Font("Microsoft YaHei", 9), textRect,
                Color.FromArgb(35, 25, 60), TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix);
        }

        private GraphicsPath RoundedRect(RectangleF r, float radius)
        {
            var path = new GraphicsPath();
            float d = radius * 2;
            path.AddArc(r.X, r.Y, d, d, 180, 90);
            path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }
    }
}
