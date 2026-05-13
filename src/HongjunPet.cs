using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Text;
using System.Windows.Forms;
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

        private const int WM_NCHITTEST = 0x84;
        private const int HTTRANSPARENT = -1;
        private const int HTCLIENT = 1;

        // 状态
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
        private string _remoteExpression = "";
        private string _resolvedExpression = "normal";
        private string _bubbleText = "";
        private int _bubbleFrames = 0;
        private bool _bridgeOnline = false;
        private int _offlineCounter = 0;

        // 皮肤
        private Dictionary<string, Bitmap> _skins = new Dictionary<string, Bitmap>();
        private bool _useSkins = false;

        // 新增交互字段
        private int _idleSeconds = 0;
        private int _lastIdleTick = 0;
        private float _petScale = 1.0f;
        private bool _wasDragged = false;
        private int _dragAnimFrame = 0;
        private bool _stretching = false;
        private int _stretchFrame = 0;
        private int _stretchAt = 0;
        private bool _yawning = false;
        private int _yawnFrame = 0;
        private int _yawnAt = 0;
        private bool _wandering = false;
        private int _wanderFrame = 0;
        private int _wanderTargetX = 0;
        private int _wanderTargetY = 0;
        private int _wanderStartX = 0;
        private int _wanderStartY = 0;
        private int _wanderAt = 0;
        private int _randomActionAt = 0;
        private int _randomActionType = 0;
        private int _randomActionFrame = 0;

        // 缓存的绘图对象
        private Font _segoeFont;

        private readonly string _bridgeUrl;
        private readonly bool _httpBridgeEnabled;
        private DateTime _lastHttpSuccessUtc = DateTime.MinValue;
        private volatile bool _bridgePollBusy;

        // 气泡窗口
        private BubbleForm _bubbleWindow;

        private static void ResolveBridgeConfig(out string url, out bool httpEnabled)
        {
            url = "";
            httpEnabled = false;
            try
            {
                string dir = Path.GetDirectoryName(Application.ExecutablePath);
                if (string.IsNullOrEmpty(dir)) return;
                string cfg = Path.Combine(dir, "bridge_url.txt");
                if (!File.Exists(cfg)) return;
                string line = File.ReadAllText(cfg, Encoding.UTF8).Trim();
                if (line.Length == 0) return;
                if (line.Equals("detect", StringComparison.OrdinalIgnoreCase)
                    || line.Equals("none", StringComparison.OrdinalIgnoreCase))
                {
                    httpEnabled = false;
                    url = "";
                    return;
                }
                url = line.TrimEnd('/');
                httpEnabled = true;
            }
            catch { }
        }

        private bool GatewayOfflineAppearance()
        {
            return _httpBridgeEnabled && !_bridgeOnline;
        }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.ExStyle |= WS_EX_LAYERED | WS_EX_TOOLWINDOW;
                return cp;
            }
        }

        public PetForm()
        {
            ResolveBridgeConfig(out _bridgeUrl, out _httpBridgeEnabled);

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

            this.Opacity = 0.88;

            this.SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.DoubleBuffer | ControlStyles.ResizeRedraw, true);
            this.UpdateStyles();

            _segoeFont = new Font("Segoe UI", 7, FontStyle.Bold);

            _animTimer = new System.Windows.Forms.Timer { Interval = 100 };
            _animTimer.Tick += OnAnimTick;
            _animTimer.Start();

            if (_httpBridgeEnabled)
            {
                _bridgeTimer = new System.Windows.Forms.Timer { Interval = 1000 };
                _bridgeTimer.Tick += (s, ev) => PollBridge();
                _bridgeTimer.Start();
            }

            _randomActionAt = _animFrame + _rng.Next(150, 300);
            _stretchAt = _animFrame + 100;
            _yawnAt = _animFrame + 300;
            _wanderAt = _animFrame + 600;

            this.MouseEnter += (s, ev) => { _mouseInside = true; _isSleeping = false; _idleCounter = 0; _idleSeconds = 0; };
            this.MouseLeave += (s, ev) => { _mouseInside = false; };
            this.MouseDown += OnMouseDown;
            this.MouseMove += OnMouseMove;
            this.MouseUp += OnMouseUp;
            this.MouseDoubleClick += OnMouseDoubleClick;
            this.MouseWheel += OnMouseWheel;

            this.Paint += OnPetPaint;
            this.Shown += (s, ev) => { if (_httpBridgeEnabled) PollBridge(); };
            this.FormClosing += OnPetFormClosing;

            LoadSkins();
            _bubbleWindow = new BubbleForm();
            _bubbleWindow.Show();
            UpdateExpression();
        }

        private void LoadSkins()
        {
            string skinDir = Path.Combine(Path.GetDirectoryName(Application.ExecutablePath), "skins");
            _skins.Clear();
            _useSkins = false;
            if (!Directory.Exists(skinDir)) return;
            string normalPath = Path.Combine(skinDir, "normal.png");
            if (File.Exists(normalPath))
            {
                try { _skins["normal"] = (Bitmap)Image.FromFile(normalPath); _useSkins = true; }
                catch { }
            }
        }

        private Bitmap GetSkin(string name)
        {
            if (_skins.ContainsKey(name)) return _skins[name];
            string skinDir = Path.Combine(Path.GetDirectoryName(Application.ExecutablePath), "skins");
            string path = Path.Combine(skinDir, name + ".png");
            if (File.Exists(path))
            {
                try
                {
                    var img = (Bitmap)Image.FromFile(path);
                    _skins[name] = img;
                    return img;
                }
                catch { }
            }
            return null;
        }

        private void SafeBeginInvoke(Action a)
        {
            if (IsDisposed || !IsHandleCreated) return;
            try { BeginInvoke(a); }
            catch (InvalidOperationException) { }
        }

        private void OnPetFormClosing(object sender, FormClosingEventArgs e)
        {
            _animTimer.Stop();
            if (_bridgeTimer != null) _bridgeTimer.Stop();
            foreach (var kv in _skins)
                try { kv.Value.Dispose(); } catch { }
            _skins.Clear();
            if (_segoeFont != null) { _segoeFont.Dispose(); _segoeFont = null; }
            if (_bubbleWindow != null && !_bubbleWindow.IsDisposed)
                _bubbleWindow.Close();
        }

        private void PollBridge()
        {
            if (!_httpBridgeEnabled) return;
            if (_bridgePollBusy || IsDisposed || !IsHandleCreated) return;
            _bridgePollBusy = true;
            string url = _bridgeUrl + "/status";
            System.Threading.ThreadPool.QueueUserWorkItem(_ =>
            {
                try
                {
                    string json;
                    using (var wc = new WebClient { Encoding = Encoding.UTF8 })
                        json = wc.DownloadString(url);
                    var state = JsonParse(json);
                    if (state == null) return;
                    SafeBeginInvoke(() =>
                    {
                        _lastHttpSuccessUtc = DateTime.UtcNow;
                        _bridgeOnline = true;
                        _offlineCounter = 0;
                        _hermesStatus = GetString(state, "status");
                        _hermesMood = GetString(state, "mood");
                        _remoteExpression = FirstNonEmpty(
                            GetString(state, "expression"),
                            GetString(state, "face"),
                            GetString(state, "avatar_expression"));

                        string newBubble = GetString(state, "bubble");
                        if (!string.IsNullOrEmpty(newBubble))
                        {
                            if (newBubble == _dismissedBubble) { /* skip - auto-dismissed */ }
                            else
                            {
                                if (newBubble.Length > 200)
                                    newBubble = newBubble.Substring(0, 198) + "\u2026";
                                _bubbleText = newBubble;
                            }
                        }
                        else _bubbleText = "";
                        UpdateBubbleWindow();
                        UpdateExpression();
                    });
                }
                catch
                {
                    SafeBeginInvoke(() =>
                    {
                        _offlineCounter++;
                        if (_offlineCounter >= 3)
                        {
                            _bridgeOnline = false;
                            _remoteExpression = "";
                            if (_offlineCounter == 3) _bubbleText = "\u7f51\u5173\u8fde\u4e0d\u4e0a\u4e86...";
                            UpdateBubbleWindow();
                            UpdateExpression();
                        }
                    });
                }
                finally
                {
                    _bridgePollBusy = false;
                }
            });
        }

        private string _lastBubbleText = "\0";
        private string _dismissedBubble = "";

        private void UpdateBubbleWindow()
        {
            if (_bubbleWindow == null || _bubbleWindow.IsDisposed) return;
            if (_bubbleText == _lastBubbleText) return;
            _lastBubbleText = _bubbleText;
            _bubbleFrames = 0;

            if (string.IsNullOrEmpty(_bubbleText))
            {
                _bubbleWindow.Hide();
                return;
            }

            _bubbleWindow.SetText(_bubbleText);
            PositionBubbleWindow();
            _bubbleWindow.Show();
            _bubbleWindow.TopMost = true;
        }

        private void PositionBubbleWindow()
        {
            var screen = Screen.PrimaryScreen.WorkingArea;
            int petRight = this.Right;
            int petLeft = this.Left;
            int petTop = this.Top;

            bool placeRight = (petRight + _bubbleWindow.Width + 10 < screen.Right);
            bool placeLeft = (petLeft - _bubbleWindow.Width - 10 > 0);

            int bx, by = petTop;
            if (placeRight) bx = petRight + 8;
            else if (placeLeft) bx = petLeft - _bubbleWindow.Width - 8;
            else bx = Math.Max(0, Math.Min(petLeft, screen.Right - _bubbleWindow.Width));
            _bubbleWindow.Location = new Point(bx, by);
        }

        private void UpdateExpression()
        {
            _isSleeping = false;
            if (_hermesStatus == "thinking") _blinkTimer = 999;
            else if (_hermesStatus == "offline") _isSleeping = true;
            else if (!_mouseInside && string.IsNullOrEmpty(_bubbleText) && !_stretching && !_yawning)
            {
                _idleCounter++;
                if (_idleCounter > 200) _isSleeping = true;
            }
            _resolvedExpression = ResolveSkinFromHermes();
            WriteHermesMirrorFiles();
        }

        private static string FirstNonEmpty(params string[] parts)
        {
            if (parts == null) return "";
            for (int i = 0; i < parts.Length; i++)
                if (!string.IsNullOrEmpty(parts[i])) return parts[i];
            return "";
        }

        private static bool IsKnownSkinKey(string s)
        {
            return s == "normal" || s == "happy" || s == "surprised" || s == "sleepy" || s == "thinking"
                || s == "angry" || s == "shy" || s == "excited" || s == "dizzy" || s == "love"
                || s == "wink" || s == "scared" || s == "proud" || s == "cry";
        }

        private string ResolveSkinFromHermes()
        {
            string hint = string.IsNullOrEmpty(_remoteExpression) ? "" : _remoteExpression.Trim().ToLowerInvariant();
            if (IsKnownSkinKey(hint)) return hint;

            // 本地交互优先
            if (_wasDragged || _dragAnimFrame > 0) return "dizzy";
            if (!_bridgeOnline && _offlineCounter > 10) return "cry";
            if (_offlineCounter >= 3 && _offlineCounter <= 5) return "angry";
            if (_mouseInside && _idleSeconds > 5) return "love";
            if (_randomActionType == 1 && _randomActionFrame > 0 && _randomActionFrame < 5) return "wink";

            string st = string.IsNullOrEmpty(_hermesStatus) ? "" : _hermesStatus.Trim().ToLowerInvariant();
            string mood = string.IsNullOrEmpty(_hermesMood) ? "" : _hermesMood.Trim().ToLowerInvariant();

            if (st == "offline") return "cry";
            if (_isSleeping) return "sleepy";

            if (st == "thinking" || st == "reasoning" || st == "processing" || st == "working") return "thinking";
            if (st == "responding" || st == "speaking" || st == "streaming" || st == "generating" || st == "replying" || st == "typing") return "happy";
            if (st == "error" || st == "failed" || st == "busy" || st == "waiting") return "scared";

            if (st == "idle" || st == "normal" || st == "ready" || st == "")
            {
                if (mood == "sad" || mood == "tired" || mood == "sleepy" || mood == "bored") return "sleepy";
                if (mood == "surprised" || mood == "confused" || mood == "worried") return "scared";
                if (mood == "angry") return "angry";
                if (mood == "excited" || mood == "playful") return "excited";
                if (mood == "love" || mood == "affectionate") return "love";
            }
            return "normal";
        }

        private static string EscapeJsonString(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            return s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\r", "\\r").Replace("\n", "\\n");
        }

        private void WriteHermesMirrorFiles()
        {
            try
            {
                string dir = Path.GetDirectoryName(Application.ExecutablePath);
                if (string.IsNullOrEmpty(dir)) return;
                string bub = _bubbleText ?? "";
                if (bub.Length > 120) bub = bub.Substring(0, 118) + "\u2026";
                string jsonPath = Path.Combine(dir, "hermes_expression.json");
                File.WriteAllText(jsonPath, "{\"expression\":\"" + EscapeJsonString(_resolvedExpression) + "\",\"status\":\"" + EscapeJsonString(_hermesStatus) + "\",\"mood\":\"" + EscapeJsonString(_hermesMood) + "\",\"bubble\":\"" + EscapeJsonString(bub) + "\",\"online\":" + ((_httpBridgeEnabled ? _bridgeOnline : true) ? "true" : "false") + "}", new UTF8Encoding(false));
                File.WriteAllText(Path.Combine(dir, "hermes_face.txt"), _resolvedExpression ?? "normal", new UTF8Encoding(false));
            }
            catch { }
        }

        // ====== 动画 Tick (100ms) ======
        private void OnAnimTick(object sender, EventArgs e)
        {
            _animFrame++;
            _bobPhase = (_bobPhase + 1) % 20;
            _blinkTimer++;

            if (_resolvedExpression != "thinking" && _blinkTimer > 120 && _rng.Next(100) < 25)
            {
                _blinking = true;
                _blinkTimer = 0;
            }
            if (_blinking && _blinkTimer > 4) _blinking = false;

            // 空闲追踪
            if (!_mouseInside && string.IsNullOrEmpty(_bubbleText))
            {
                _lastIdleTick++;
                if (_lastIdleTick >= 10) { _idleSeconds++; _lastIdleTick = 0; }
            }
            else { _idleSeconds = 0; _lastIdleTick = 0; }

            // 拖拽后动画
            if (_dragAnimFrame > 0)
            {
                _dragAnimFrame--;
                if (_dragAnimFrame == 0) _wasDragged = false;
            }

            // 伸懒腰
            if (_stretching)
            {
                _stretchFrame++;
                if (_stretchFrame > 30) { _stretching = false; _stretchFrame = 0; _stretchAt = _animFrame + 100 + _rng.Next(0, 200); }
            }
            else if (_animFrame >= _stretchAt && !_isSleeping && _idleSeconds > 10 && !_mouseInside)
            {
                _stretching = true; _stretchFrame = 0;
            }

            // 打哈欠
            if (_yawning)
            {
                _yawnFrame++;
                if (_yawnFrame > 40) { _yawning = false; _yawnFrame = 0; _yawnAt = _animFrame + 300 + _rng.Next(0, 400); }
            }
            else if (_animFrame >= _yawnAt && !_isSleeping && _idleSeconds > 30 && !_mouseInside && !_stretching)
            {
                _yawning = true; _yawnFrame = 0;
            }

            // 随机走动
            if (_wandering)
            {
                _wanderFrame++;
                var sc = Screen.PrimaryScreen.WorkingArea;
                float t = Math.Min(1, _wanderFrame / 80f);
                float eased = t < 0.5f ? 2f * t * t : -1f + (4f - 2f * t) * t;
                int cx = _wanderStartX + (int)((_wanderTargetX - _wanderStartX) * eased);
                int cy = _wanderStartY + (int)((_wanderTargetY - _wanderStartY) * eased);
                this.Location = new Point(cx, cy);
                PositionBubbleWindow();
                if (_wanderFrame >= 80)
                {
                    _wandering = false; _wanderFrame = 0;
                    _wanderAt = _animFrame + 600 + _rng.Next(0, 400);
                }
            }
            else if (_animFrame >= _wanderAt && _idleSeconds > 60 && !_mouseInside && !_stretching && !_yawning)
            {
                _wandering = true; _wanderFrame = 0;
                _wanderStartX = this.Left; _wanderStartY = this.Top;
                var sc = Screen.PrimaryScreen.WorkingArea;
                _wanderTargetX = _rng.Next(50, sc.Right - PET_W - 50);
                _wanderTargetY = _rng.Next(50, sc.Bottom - PET_H - 50);
            }

            // 随机小动作
            if (_animFrame >= _randomActionAt && !_stretching && !_yawning && !_wandering)
            {
                _randomActionType = _rng.Next(3);
                _randomActionFrame = 0;
                _randomActionAt = _animFrame + _rng.Next(150, 300);
            }
            if (_randomActionFrame > 0) _randomActionFrame++;

            // 气泡8秒自动消失
            if (!string.IsNullOrEmpty(_bubbleText))
            {
                _bubbleFrames++;
                if (_bubbleFrames > 80) { _dismissedBubble = _bubbleText; _bubbleText = ""; _bubbleFrames = 0; UpdateBubbleWindow(); }
            }

            this.Invalidate();
        }

        // ====== 绘制 ======
        private void OnPetPaint(object sender, PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.HighQuality;
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;

            float bobY = (float)Math.Sin(_bobPhase * Math.PI / 10) * 2.5f;

            float stretchS = 1f;
            if (_stretching)
            {
                float st = _stretchFrame / 30f;
                stretchS = 1f + (float)Math.Sin(st * Math.PI) * 0.1f;
            }

            float yawnS = 1f;
            if (_yawning)
            {
                float yt = _yawnFrame / 40f;
                yawnS = 1f + (float)Math.Sin(yt * Math.PI) * 0.08f;
            }

            float dragS = 1f;
            if (_isDragging || _wasDragged)
            {
                dragS = 0.9f;
                float shake = (float)Math.Sin(_animFrame * 0.5f) * 2f;
                g.TranslateTransform(shake, 0);
            }

            bool faceThinking = _resolvedExpression == "thinking";
            float shakeX = faceThinking ? (float)Math.Sin(_animFrame * 0.3f) * 1.5f : 0;
            float breathe = 1 + (float)Math.Sin(_animFrame * 0.05f) * 0.012f;
            if (faceThinking) breathe = 1 + (float)Math.Sin(_animFrame * 0.12f) * 0.018f;

            float totalScale = _petScale * stretchS * yawnS * dragS * breathe;

            g.TranslateTransform(shakeX, bobY);
            g.ScaleTransform(totalScale, totalScale);
            g.TranslateTransform(PET_W / 2, PET_H - 65);

            string expr = _yawning ? "sleepy" : _resolvedExpression;

            if (_useSkins)
            {
                var skin = GetSkin(expr);
                if (skin != null)
                    g.DrawImage(skin, -skin.Width / 2, -skin.Height / 2, skin.Width, skin.Height);
                else
                    DrawFallback(g);
            }
            else
                DrawFallback(g);
        }

        private void DrawFallback(Graphics g)
        {
            Color c1 = Color.FromArgb(88, 166, 255);
            Color c2 = Color.FromArgb(56, 120, 220);
            Color pupil = Color.FromArgb(30, 30, 50);
            Color accent = Color.FromArgb(120, 200, 255);
            const int R = 36;

            Color glowC = GatewayOfflineAppearance() ? Color.FromArgb(180, 60, 60) : Color.FromArgb(40, 80, 180);
            if (_resolvedExpression == "thinking") glowC = Color.FromArgb(100, 180, 255);
            if (_resolvedExpression == "happy" || _mouseInside) glowC = Color.FromArgb(80, 220, 120);
            if (_dragAnimFrame > 0) glowC = Color.FromArgb(255, 200, 60);

            using (var gb = new SolidBrush(Color.FromArgb(20, glowC)))
                for (int i = 0; i < 3; i++)
                    g.FillEllipse(gb, -R - 10 - i * 6, -R - 5 - i * 4, (R + 12 + i * 8) * 2, (R + 8 + i * 6) * 2);

            float r = R * (_isSleeping ? 0.85f : 1f);
            var rect = new RectangleF(-r, -r, r * 2, r * 2);
            Color b1 = _resolvedExpression == "thinking" ? Color.FromArgb(160, 200, 255) : c1;
            Color b2 = _resolvedExpression == "thinking" ? Color.FromArgb(80, 150, 240) : c2;
            if (GatewayOfflineAppearance()) { b1 = Color.FromArgb(140, 140, 160); b2 = Color.FromArgb(100, 100, 120); }

            using (var bb = new LinearGradientBrush(rect, b1, b2, LinearGradientMode.ForwardDiagonal))
                g.FillEllipse(bb, rect);

            using (var hl = new SolidBrush(Color.FromArgb(50, accent)))
                g.FillEllipse(hl, -r * 0.5f, -r * 0.7f, r * 0.9f, r * 0.6f);

            // ZZZ
            if (_isSleeping)
            {
                for (int i = 0; i < 3; i++)
                {
                    float zOff = (float)Math.Sin(_animFrame * 0.08f + i * 1.5f) * 3;
                    using (var zb = new SolidBrush(Color.FromArgb(150 - i * 30, Color.White)))
                        g.DrawString("z", _segoeFont, zb, -r - 8 + i * 8, -r - 10 - i * 11 + zOff);
                }
            }

            // 眼睛
            if (!_isSleeping)
            {
                float ey = -r * 0.2f, es = r * 0.3f, ew = r * 0.28f;
                bool closed = (_blinking && _blinkTimer < 4) || (_yawning && _yawnFrame < 8);
                bool think = _resolvedExpression == "thinking";
                bool surprised = _resolvedExpression == "surprised" || (_dragAnimFrame > 0);

                for (int side = -1; side <= 1; side += 2)
                {
                    float ex = side * es;
                    if (closed)
                    {
                        using (var cp = new Pen(pupil, 2)) g.DrawArc(cp, ex - ew, ey, ew * 2, ew * 1.2f, 0, 180);
                    }
                    else
                    {
                        float eh = think ? ew * 0.8f : (surprised ? ew * 2.5f : ew * 1.8f);
                        using (var eb = new SolidBrush(Color.White))
                            g.FillEllipse(eb, ex - ew, ey - ew * 0.6f, ew * 2, eh);
                        float ps = think ? ew * 0.35f : ew * 0.55f;
                        float py = think ? ey - 1 : ey + 1;
                        using (var pb = new SolidBrush(pupil))
                            g.FillEllipse(pb, ex - ps, py - ps * 0.6f, ps * 2, ps * 1.8f);
                        using (var sb = new SolidBrush(Color.FromArgb(180, Color.White)))
                            g.FillEllipse(sb, ex - ps * 0.6f, py - ps * 0.7f, ps * 0.7f, ps * 0.7f);

                        // 随机单眼眨
                        if (_randomActionType == 1 && _randomActionFrame > 0 && _randomActionFrame < 5 && side == 1)
                        {
                            using (var cp = new Pen(pupil, 2)) g.DrawArc(cp, ex - ew, ey, ew * 2, ew * 1.2f, 0, 180);
                        }
                    }
                }
            }
            else
            {
                for (int side = -1; side <= 1; side += 2)
                    using (var sp = new Pen(Color.FromArgb(180, pupil), 1.8f))
                        g.DrawArc(sp, side * r * 0.3f - 6, -r * 0.15f - 2, 12, 8, 0, -180);
            }

            // 腮红
            if (_resolvedExpression == "happy" || _hermesMood.Equals("happy") || _mouseInside)
                for (int side = -1; side <= 1; side += 2)
                    using (var bl = new SolidBrush(Color.FromArgb(60, 255, 150, 150)))
                        g.FillEllipse(bl, side * r * 0.5f - 6, r * 0.15f, 12, 8);

            // 嘴
            float my = r * 0.3f;
            using (var mp = new Pen(pupil, 1.8f))
            {
                if (_resolvedExpression == "thinking") g.DrawArc(mp, -4, my + 1, 8, 5, 0, -180);
                else if (_resolvedExpression == "surprised" || (_dragAnimFrame > 0))
                {
                    using (var mo = new SolidBrush(pupil))
                        g.FillEllipse(mo, -5, my + 2, 10, 10);
                }
                else if (_yawning)
                {
                    float yp = _yawnFrame < 20 ? _yawnFrame / 20f : (40 - _yawnFrame) / 20f;
                    float yw = 4 + yp * 10;
                    float yh = 3 + yp * 16;
                    using (var yb = new SolidBrush(Color.FromArgb(200, 60, 50)))
                        g.FillEllipse(yb, -yw, my + 1, yw * 2, yh);
                }
                else if (_resolvedExpression == "happy" || _hermesMood.Equals("happy") || _mouseInside)
                    g.DrawArc(mp, -6, my - 2, 12, 8, 0, -180);
                else if (_isSleeping)
                    g.DrawArc(mp, -5, my + 2, 10, 5, 0, 180);
                else
                    g.DrawArc(mp, -4, my + 1, 8, 5, 0, -180);
            }

            // 触角
            float antSpeed = _resolvedExpression == "thinking" ? 0.2f : 0.1f;
            float antAmp = _resolvedExpression == "thinking" ? 3f : 2f;
            if (_randomActionType == 2 && _randomActionFrame > 0 && _randomActionFrame < 10)
                antSpeed = 0.5f;

            float antOff = (float)Math.Sin(_animFrame * antSpeed) * antAmp;
            for (int side = -1; side <= 1; side += 2)
            {
                float ax = side * r * 0.35f, ay = -r - 2 + antOff * side * 0.5f;
                using (var ap = new Pen(c1, 2.5f))
                    g.DrawCurve(ap, new PointF[] {
                        new PointF(ax, ay + 5),
                        new PointF(ax + side * 6, ay - 8 + antOff),
                        new PointF(ax + side * 2, ay - 14 + antOff * 1.5f)
                    });
                using (var tb = new SolidBrush(accent))
                    g.FillEllipse(tb, ax + side * 1 - 3, ay - 16 + antOff * 1.5f, 6, 6);
                using (var tg = new SolidBrush(Color.FromArgb(80, Color.White)))
                    g.FillEllipse(tg, ax + side * 1 - 2, ay - 15 + antOff * 1.5f, 4, 4);
            }
        }

        // ====== 交互 ======
        private void OnMouseDown(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                _isSleeping = false; _idleCounter = 0; _idleSeconds = 0;
                _isDragging = false;
                _dragOffset = new Point(Cursor.Position.X - this.Left, Cursor.Position.Y - this.Top);
                string[] reacts = { "嘿嘿~", "在呢!", "戳我干嘛~", "痒~", "忙着呢", "诶嘿" };
                _bubbleText = reacts[_rng.Next(reacts.Length)];
                UpdateBubbleWindow();
                UpdateExpression();
            }
            else if (e.Button == MouseButtons.Right)
            {
                var menu = new ContextMenuStrip();
                menu.Items.Add("说句话", null, (s2, e2) =>
                {
                    string[] msgs = { "你好呀!", "今天怎么样~", "我在哦", "盯--", "鸿钧在忙呢", "今天天气不错~" };
                    _bubbleText = msgs[_rng.Next(msgs.Length)];
                    UpdateBubbleWindow();
                    UpdateExpression();
                });
                menu.Items.Add("重置位置", null, (s2, e2) =>
                {
                    var sc = Screen.PrimaryScreen.WorkingArea;
                    this.Location = new Point(sc.Right - PET_W - 20, sc.Bottom - PET_H - 30);
                    PositionBubbleWindow();
                });
                menu.Items.Add("重置大小", null, (s2, e2) =>
                {
                    _petScale = 1.0f;
                    this.Invalidate();
                });
                menu.Items.Add("退出", null, (s2, e2) => { Application.Exit(); });
                menu.Show(this, e.Location);
            }
        }

        private void OnMouseDoubleClick(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                _isSleeping = false; _idleCounter = 0; _idleSeconds = 0;
                string[] dblReacts = { "别敲我!>_<", "干嘛啦!", "好痛!", "再敲我生气了!", "呜呜..." };
                _bubbleText = dblReacts[_rng.Next(dblReacts.Length)];
                UpdateBubbleWindow();
                UpdateExpression();
            }
        }

        private void OnMouseUp(object sender, MouseEventArgs e)
        {
            if (_isDragging)
            {
                _wasDragged = true;
                _dragAnimFrame = 15;
                _bubbleText = "晕...";
                UpdateBubbleWindow();
            }
            _isDragging = false;
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

            if (nx <= 0) nx += 8;
            if (ny <= 0) ny += 8;
            if (nx >= screen.Right - PET_W - 5) nx -= 8;
            if (ny >= screen.Bottom - PET_H - 5) ny -= 8;

            this.Location = new Point(nx, ny);
            PositionBubbleWindow();
        }

        private void OnMouseWheel(object sender, MouseEventArgs e)
        {
            if (!_mouseInside) return;
            float delta = e.Delta > 0 ? 0.05f : -0.05f;
            _petScale = Math.Max(0.7f, Math.Min(1.3f, _petScale + delta));
            this.Invalidate();
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WM_NCHITTEST)
            {
                Point pt = this.PointToClient(Cursor.Position);
                int hitY = PET_H - 130;
                if (pt.X > 5 && pt.X < PET_W - 5 && pt.Y > hitY && pt.Y < PET_H - 5)
                {
                    m.Result = (IntPtr)HTCLIENT;
                    return;
                }
                m.Result = (IntPtr)HTTRANSPARENT;
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
                while (i < json.Length && json[i] != ':') i++;
                if (i >= json.Length) break;
                i++;
                while (i < json.Length && json[i] == ' ') i++;
                if (i >= json.Length) break;

                if (json[i] == '"')
                {
                    i++; string val = "";
                    while (i < json.Length && json[i] != '"') { if (json[i] == '\\') { i++; if (i < json.Length) val += json[i]; } else val += json[i]; i++; }
                    i++; r[key] = val;
                }
                else if (json[i] == 't' && json.Substring(i).StartsWith("true")) { r[key] = "true"; i += 4; }
                else if (json[i] == 'f' && json.Substring(i).StartsWith("false")) { r[key] = "false"; i += 5; }
                else if (json[i] == '{')
                {
                    int depth = 0; int start = i;
                    for (; i < json.Length; i++)
                    {
                        if (json[i] == '{') depth++;
                        else if (json[i] == '}') { depth--; if (depth == 0) { i++; break; } }
                    }
                    var innerDict = JsonParse(json.Substring(start, i - start));
                    if (innerDict != null) foreach (var kv in innerDict) r[kv.Key] = kv.Value;
                }
                else break;
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

        private string _text = "";
        private const int MIN_W = 60;
        private const int MAX_W = 380;
        private int _bw, _bh;
        private Font _bubbleFont;

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.ExStyle |= WS_EX_LAYERED | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE;
                return cp;
            }
        }

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
            this.Opacity = 0.92;

            _bubbleFont = new Font("Microsoft YaHei", 9);

            this.SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.DoubleBuffer | ControlStyles.ResizeRedraw, true);
            this.Paint += OnPaint;
            this.FormClosing += OnBubbleClosing;
        }

        private void OnBubbleClosing(object sender, FormClosingEventArgs e)
        {
            if (_bubbleFont != null) { _bubbleFont.Dispose(); _bubbleFont = null; }
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
            if (string.IsNullOrEmpty(_text)) { _bw = MIN_W; _bh = 24; this.Width = _bw; this.Height = _bh; return; }
            var sz = TextRenderer.MeasureText(_text, _bubbleFont, new Size(MAX_W - 20, 500), TextFormatFlags.WordBreak);
            _bw = Math.Max(MIN_W, sz.Width + 20);
            _bh = Math.Max(24, sz.Height + 16);
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

            using (var bd = new Pen(Color.FromArgb(60, 180, 120, 255), 1f))
            using (var bp2 = RoundedRect(r, 8))
                g.DrawPath(bd, bp2);

            var textRect = new Rectangle(8, 5, _bw - 16, _bh - 10);
            TextRenderer.DrawText(g, _text, _bubbleFont, textRect,
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
