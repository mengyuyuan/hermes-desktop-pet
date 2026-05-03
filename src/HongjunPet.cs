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
        // ====== 窗口样式 ======
        private const int WS_EX_LAYERED = 0x80000;
        private const int WS_EX_TRANSPARENT = 0x20;
        private const int WS_EX_TOOLWINDOW = 0x80;

        [DllImport("user32.dll")]
        static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);
        [DllImport("user32.dll")]
        static extern int GetWindowLong(IntPtr hWnd, int nIndex);


        // ====== 宠物状态 ======
        private System.Windows.Forms.Timer _animTimer;      // 20fps 动画
        private System.Windows.Forms.Timer _bridgeTimer;    // 每2秒轮询桥接
        private int _animFrame = 0;
        private int _bobPhase = 0;
        private bool _blinking = false;
        private int _blinkTimer = 0;
        private bool _isSleeping = false;
        private int _idleCounter = 0;
        private bool _mouseInside = false;
        private bool _isFollowingMouse = false;
        private Point _followTarget;
        private Random _rng = new Random();
        
        // ====== 空闲小动作 ======
        private int _idleActionTimer = 0;
        private int _idleActionDuration = 0;
        private string _idleActionType = "";
        private bool _idleActionActive = false;
        private int _idleTimerSinceReply = 999;  // 回复后等待计数器

        // ====== 桥接同步状态 ======
        private string _hermesStatus = "idle";      // idle | thinking | responding | offline
        private string _hermesMood = "happy";        // happy | normal | surprised | sleepy | thinking
        private string _bubbleText = "";
        private bool _bridgeOnline = false;
        private string _lastReply = "";
        private string _lastReplyTime = "";
        private string _hermesTimestamp = "";
        private int _offlineCounter = 0;

        // ====== 皮肤系统 ======
        private Dictionary<string, Bitmap> _skins = new Dictionary<string, Bitmap>();
        private bool _useSkins = false;
        private string _skinDir = "";

        // ====== 尺寸 ======
        private const int PET_W = 140;
        private const int PET_H = 200;
        private const int BODY_R = 36;

        // ====== 颜色 ======
        private readonly Color _bodyColor1 = Color.FromArgb(88, 166, 255);
        private readonly Color _bodyColor2 = Color.FromArgb(56, 120, 220);
        private readonly Color _glowColor = Color.FromArgb(40, 80, 180);
        private readonly Color _eyePupil = Color.FromArgb(30, 30, 50);
        private readonly Color _accentColor = Color.FromArgb(120, 200, 255);
        private readonly Color _blushColor = Color.FromArgb(255, 150, 150);

        // ====== 桥接 API ======
        private const string BRIDGE_URL = "http://localhost:9101";
        private WebClient _web;

        public PetForm()
        {
            _web = new WebClient();
            _web.Encoding = System.Text.Encoding.UTF8;
            // WebClient 默认100秒超时，设短一点
            _web.BaseAddress = "";  // 没用但无害

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

            // 分层窗口
            int exStyle = GetWindowLong(this.Handle, -20);
            SetWindowLong(this.Handle, -20, exStyle | WS_EX_LAYERED | WS_EX_TOOLWINDOW);
            this.Opacity = 0.92;

            this.SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.DoubleBuffer | ControlStyles.ResizeRedraw, true);
            this.UpdateStyles();

            // 20fps 动画
            _animTimer = new System.Windows.Forms.Timer { Interval = 50 };
            _animTimer.Tick += OnAnimTick;
            _animTimer.Start();

            // 每0.5秒轮询桥接服务
            _bridgeTimer = new System.Windows.Forms.Timer { Interval = 500 };
            _bridgeTimer.Tick += (s, e) => PollBridge();
            _bridgeTimer.Start();

            // 鼠标事件
            this.MouseEnter += (s, e) => { _mouseInside = true; _isSleeping = false; _idleCounter = 0; };
            this.MouseLeave += (s, e) => { _mouseInside = false; };
            this.MouseDown += OnPetClick;
            this.MouseMove += OnPetDrag;
            this.MouseUp += (s, e) => { _isFollowingMouse = false; };

            this.Paint += OnPetPaint;

            // 启动后立即查一次
            this.Shown += (s, e) => PollBridge();

            // 加载皮肤
            LoadSkins();
        }

        // ====== 轮询桥接 ======
        private void PollBridge()
        {
            try
            {
                string json = _web.DownloadString(BRIDGE_URL + "/status");
                var state = JsonParse(json);
                if (state != null)
                {
                    _bridgeOnline = true;
                    _offlineCounter = 0;

                    _hermesStatus = GetString(state, "status");
                    _hermesMood = GetString(state, "mood");

                    string newBubble = GetString(state, "bubble");
                    if (!string.IsNullOrEmpty(newBubble))
                    {
                        // 随文字变宽，最大支持约100字
                        _bubbleText = newBubble.Length > 100
                            ? newBubble.Substring(0, 98) + "…"
                            : newBubble;
                    }

                    _lastReply = GetString(state, "last_reply");
                    _lastReplyTime = GetString(state, "last_reply_time");
                    _hermesTimestamp = GetString(state, "timestamp");

                    // 根据桥接状态更新表情
                    UpdateExpression();
                }
            }
            catch
            {
                _offlineCounter++;
                if (_offlineCounter >= 3)
                {
                    _bridgeOnline = false;
                    _hermesStatus = "offline";
                    if (_offlineCounter == 3)
                        _bubbleText = "网关连不上了...";
                }
            }
        }

        private void UpdateExpression()
        {
            _isSleeping = false;
            switch (_hermesStatus)
            {
                case "thinking":
                    // 思考中 - 眯眼专注
                    _blinkTimer = 999; // 不眨眼
                    break;
                case "responding":
                    // 回复中 - 开心
                    break;
                case "offline":
                    _isSleeping = true;
                    break;
                default: // idle
                    if (!_mouseInside && string.IsNullOrEmpty(_bubbleText))
                    {
                        _idleCounter++;
                        if (_idleCounter > 200)
                            _isSleeping = true;
                    }
                    break;
            }
        }

        // ====== 加载皮肤 ======
        private void LoadSkins()
        {
            _skinDir = System.IO.Path.Combine(
                System.IO.Path.GetDirectoryName(Application.ExecutablePath), "skins");
            _skins.Clear();
            _useSkins = false;

            if (!System.IO.Directory.Exists(_skinDir)) return;

            string[] names = { "normal", "happy", "surprised", "sleepy", "thinking" };
            bool any = false;
            foreach (string n in names)
            {
                string path = System.IO.Path.Combine(_skinDir, n + ".png");
                if (System.IO.File.Exists(path))
                {
                    try
                    {
                        var img = (Bitmap)Image.FromFile(path);
                        _skins[n] = img;
                        any = true;
                    }
                    catch { }
                }
            }
            _useSkins = any;
        }

        // ====== 简易 JSON 解析（C# 5 兼容） ======
        private string GetString(Dictionary<string, object> dict, string key)
        {
            if (dict == null) return "";
            object val;
            if (dict.TryGetValue(key, out val) && val != null)
                return val.ToString();
            return "";
        }

        private Dictionary<string, object> JsonParse(string json)
        {
            var result = new Dictionary<string, object>();
            json = json.Trim();
            if (!json.StartsWith("{") || !json.EndsWith("}")) return null;
            json = json.Substring(1, json.Length - 2);

            int i = 0;
            while (i < json.Length)
            {
                // Skip whitespace / commas
                while (i < json.Length && (json[i] == ' ' || json[i] == ',' || json[i] == '\n' || json[i] == '\r'))
                    i++;
                if (i >= json.Length) break;

                // Read key
                if (json[i] != '"') break;
                i++;
                string key = "";
                while (i < json.Length && json[i] != '"')
                {
                    if (json[i] == '\\') { i++; if (i < json.Length) key += json[i]; }
                    else key += json[i];
                    i++;
                }
                i++; // skip closing "

                // Skip :
                while (i < json.Length && json[i] != ':') i++;
                i++;
                while (i < json.Length && json[i] == ' ') i++;

                // Read value
                if (i < json.Length && json[i] == '"')
                {
                    i++;
                    string val = "";
                    while (i < json.Length && json[i] != '"')
                    {
                        if (json[i] == '\\') { i++; if (i < json.Length) val += json[i]; }
                        else val += json[i];
                        i++;
                    }
                    i++;
                    result[key] = val;
                }
                else if (i < json.Length && (json[i] == 't' || json[i] == 'f'))
                {
                    if (json.Substring(i).StartsWith("true")) { result[key] = "true"; i += 4; }
                    else { result[key] = "false"; i += 5; }
                }
                else if (i < json.Length && json[i] == '{')
                {
                    int depth = 1; i++;
                    string sub = "{";
                    while (i < json.Length && depth > 0)
                    {
                        if (json[i] == '{') depth++;
                        else if (json[i] == '}') depth--;
                        sub += json[i];
                        i++;
                    }
                    result[key] = sub;
                }
            }
            return result;
        }

        // ====== 动画循环 ======
        private void OnAnimTick(object sender, EventArgs e)
        {
            _animFrame++;
            _bobPhase = (_bobPhase + 1) % 20;

            // 眨眼控制
            _blinkTimer++;
            if (!_hermesStatus.Equals("thinking") && _blinkTimer > _rng.Next(80, 200))
            {
                _blinking = true;
                _blinkTimer = 0;
            }
            if (_blinking && _blinkTimer > 4) _blinking = false;

            // 聊天时的等待计数器
            if (_hermesStatus == "responding" && !string.IsNullOrEmpty(_bubbleText))
                _idleTimerSinceReply++;
            else if (_hermesStatus == "thinking" || _hermesStatus == "idle")
                _idleTimerSinceReply = 0;

            // 空闲小动作：发呆或回复完等一会儿，随机切换表情
            bool canIdleAction = (_hermesStatus == "idle" && string.IsNullOrEmpty(_bubbleText) && !_mouseInside)
                || (_hermesStatus == "responding" && !_mouseInside && _idleTimerSinceReply > 150); // 回复完3秒后也能动
            if (canIdleAction)
            {
                if (_idleActionActive)
                {
                    _idleActionDuration--;
                    if (_idleActionDuration <= 0)
                    {
                        _idleActionActive = false;
                        _idleActionTimer = _rng.Next(150, 400); // 下次动作间隔 3-8 秒
                    }
                }
                else
                {
                    _idleActionTimer--;
                    if (_idleActionTimer <= 0)
                    {
                        // 随机选一个空闲表情
                        string[] actions = { "surprised", "happy", "thinking", "normal" };
                        _idleActionType = actions[_rng.Next(actions.Length)];
                        _idleActionDuration = _rng.Next(15, 35); // 动作持续 0.3-0.7 秒
                        _idleActionActive = true;
                    }
                }
            }
            else
            {
                // 不空闲时重置小动作定时器
                _idleActionActive = false;
                _idleActionTimer = _rng.Next(200, 500);
            }

            // 鼠标跟随：实际移动窗口
            if (_isFollowingMouse)
            {
                var screen = Screen.PrimaryScreen.WorkingArea;
                int newX = _followTarget.X - PET_W / 2;
                int newY = _followTarget.Y - 20;
                newX = Math.Max(0, Math.Min(screen.Right - PET_W - 5, newX));
                newY = Math.Max(0, Math.Min(screen.Bottom - PET_H - 5, newY));
                this.Location = new Point(newX, newY);
            }

            this.Invalidate();
        }

        // ====== 绘制宠物 ======
        private void OnPetPaint(object sender, PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.HighQuality;
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;

            float bobY = (float)Math.Sin(_bobPhase * Math.PI / 10) * 2.5f;
            // 思考时轻微颤抖
            float thinkShakeX = _hermesStatus.Equals("thinking") ? (float)Math.Sin(_animFrame * 0.3f) * 1.5f : 0;
            float breathe = 1 + (float)Math.Sin(_animFrame * 0.05f) * 0.012f;
            if (_hermesStatus.Equals("thinking")) breathe = 1 + (float)Math.Sin(_animFrame * 0.12f) * 0.018f;

            g.TranslateTransform(thinkShakeX, bobY);
            g.ScaleTransform(breathe, breathe);
            g.TranslateTransform(PET_W / 2, PET_H - 65);

            // ---- 确定当前表情 ---- 
            string expr = _hermesMood;
            if (_isSleeping || _hermesStatus == "offline") expr = "sleepy";
            else if (_hermesStatus == "thinking") expr = "thinking";
            else if (_hermesStatus == "responding") expr = "happy";
            else if (_idleActionActive) expr = _idleActionType;  // 空闲小动作覆盖
            else if (string.IsNullOrEmpty(expr) || expr == "normal") expr = "normal";

            // ---- 皮肤模式：画 PNG ---- 
            if (_useSkins && _skins.ContainsKey(expr))
            {
                var skin = _skins[expr];
                g.DrawImage(skin, -skin.Width / 2, -skin.Height / 2, skin.Width, skin.Height);
            }
            else
            {
                // ---- 代码绘制备用 ---- 
                // ---- 状态光晕 ----
            Color glow = _bridgeOnline ? _glowColor : Color.FromArgb(180, 60, 60);
            if (_hermesStatus.Equals("thinking")) glow = Color.FromArgb(100, 180, 255);
            if (_hermesStatus.Equals("responding")) glow = Color.FromArgb(80, 220, 120);

            using (var glowBrush = new SolidBrush(Color.FromArgb(20, glow)))
            {
                for (int i = 0; i < 3; i++)
                    g.FillEllipse(glowBrush, -BODY_R - 10 - i * 6, -BODY_R - 5 - i * 4,
                        (BODY_R + 12 + i * 8) * 2, (BODY_R + 8 + i * 6) * 2);
            }

            // ---- 身体 ----
            float bodyR = BODY_R * (_isSleeping ? 0.85f : 1f);
            var bodyRect = new RectangleF(-bodyR, -bodyR, bodyR * 2, bodyR * 2);
            Color c1 = _hermesStatus.Equals("thinking") ? Color.FromArgb(160, 200, 255) : _bodyColor1;
            Color c2 = _hermesStatus.Equals("thinking") ? Color.FromArgb(80, 150, 240) : _bodyColor2;
            if (!_bridgeOnline) { c1 = Color.FromArgb(140, 140, 160); c2 = Color.FromArgb(100, 100, 120); }

            using (var bodyBrush = new LinearGradientBrush(bodyRect, c1, c2, LinearGradientMode.ForwardDiagonal))
                g.FillEllipse(bodyBrush, bodyRect);

            using (var highlightBrush = new SolidBrush(Color.FromArgb(50, _accentColor)))
                g.FillEllipse(highlightBrush, -bodyR * 0.5f, -bodyR * 0.7f, bodyR * 0.9f, bodyR * 0.6f);

            // ---- 睡觉 ZZZ ----
            if (_isSleeping)
            {
                float zBase = -bodyR - 10;
                for (int i = 0; i < 3; i++)
                {
                    float zOffset = (float)Math.Sin(_animFrame * 0.08f + i * 1.5f) * 3;
                    float zSize = 7 + i * 4;
                    using (var zBrush = new SolidBrush(Color.FromArgb(150 - i * 30, Color.White)))
                        g.DrawString("z", new Font("Segoe UI", zSize * 0.6f, FontStyle.Bold), zBrush,
                            -bodyR - 8 + i * 8, zBase - i * 11 + zOffset);
                }
            }

            // ---- 眼睛 ----
            if (!_isSleeping)
            {
                float eyeY = -bodyR * 0.2f;
                float eyeSpacing = bodyR * 0.3f;
                float eyeSize = bodyR * 0.28f;
                bool closed = _blinking && _blinkTimer < 4;
                bool thinking = _hermesStatus.Equals("thinking");

                for (int side = -1; side <= 1; side += 2)
                {
                    float ex = side * eyeSpacing;
                    if (closed)
                    {
                        using (var closePen = new Pen(_eyePupil, 2))
                            g.DrawArc(closePen, ex - eyeSize, eyeY, eyeSize * 2, eyeSize * 1.2f, 0, 180);
                    }
                    else
                    {
                        float eyeH = thinking ? eyeSize * 0.8f : eyeSize * 1.8f;
                        using (var eyeBrush = new SolidBrush(Color.White))
                            g.FillEllipse(eyeBrush, ex - eyeSize, eyeY - eyeSize * 0.6f, eyeSize * 2, eyeH);

                        float ps = thinking ? eyeSize * 0.35f : eyeSize * 0.55f;
                        float pupilY = thinking ? eyeY - 1 : eyeY + 1;
                        using (var pupilBrush = new SolidBrush(_eyePupil))
                            g.FillEllipse(pupilBrush, ex - ps, pupilY - ps * 0.6f, ps * 2, ps * 1.8f);

                        using (var sparkleBrush = new SolidBrush(Color.FromArgb(180, Color.White)))
                            g.FillEllipse(sparkleBrush, ex - ps * 0.6f, pupilY - ps * 0.7f, ps * 0.7f, ps * 0.7f);
                    }
                }
            }
            else
            {
                float eyeY = -bodyR * 0.15f;
                for (int side = -1; side <= 1; side += 2)
                {
                    using (var sleepPen = new Pen(Color.FromArgb(180, _eyePupil), 1.8f))
                        g.DrawArc(sleepPen, side * bodyR * 0.3f - 6, eyeY - 2, 12, 8, 0, -180);
                }
            }

            // ---- 腮红 ----
            if (_hermesStatus.Equals("responding") || _hermesMood.Equals("happy") || _mouseInside)
            {
                for (int side = -1; side <= 1; side += 2)
                {
                    using (var blushBrush = new SolidBrush(Color.FromArgb(60, _blushColor)))
                        g.FillEllipse(blushBrush, side * bodyR * 0.5f - 6, bodyR * 0.15f, 12, 8);
                }
            }

            // ---- 嘴巴 ----
            float mouthY = bodyR * 0.3f;
            using (var mouthPen = new Pen(_eyePupil, 1.8f))
            {
                if (_hermesStatus.Equals("thinking") || _hermesMood.Equals("thinking"))
                    g.DrawArc(mouthPen, -4, mouthY + 1, 8, 5, 0, -180); // 微张
                else if (_hermesStatus.Equals("responding") || _hermesMood.Equals("happy") || _mouseInside)
                    g.DrawArc(mouthPen, -6, mouthY - 2, 12, 8, 0, -180); // 微笑
                else if (_isSleeping)
                    g.DrawArc(mouthPen, -5, mouthY + 2, 10, 5, 0, 180); // 张嘴
                else if (_hermesStatus.Equals("surprised"))
                    g.DrawEllipse(mouthPen, -4, mouthY - 2, 8, 8); // 惊讶
                else
                    g.DrawArc(mouthPen, -4, mouthY + 1, 8, 5, 0, -180); // 正常
            }

            // ---- 触角 ----
            float antOffset = (float)Math.Sin(_animFrame * 0.1f) * 2;
            if (_hermesStatus.Equals("thinking")) antOffset = (float)Math.Sin(_animFrame * 0.2f) * 3;

            for (int side = -1; side <= 1; side += 2)
            {
                float ax = side * bodyR * 0.35f;
                float ay = -bodyR - 2 + antOffset * side * 0.5f;
                using (var antPen = new Pen(_bodyColor1, 2.5f))
                    g.DrawCurve(antPen,
                        new PointF[] {
                            new PointF(ax, ay + 5),
                            new PointF(ax + side * 6, ay - 8 + antOffset),
                            new PointF(ax + side * 2, ay - 14 + antOffset * 1.5f)
                        });
                using (var tipBrush = new SolidBrush(_accentColor))
                    g.FillEllipse(tipBrush, ax + side * 1 - 3, ay - 16 + antOffset * 1.5f, 6, 6);
                using (var tipGlow = new SolidBrush(Color.FromArgb(80, Color.White)))
                    g.FillEllipse(tipGlow, ax + side * 1 - 2, ay - 15 + antOffset * 1.5f, 4, 4);
            }
            } // 结束备用绘制else块

            g.ResetTransform();

            // ---- 气泡 ----
            if (!string.IsNullOrEmpty(_bubbleText))
                DrawBubble(g, _bubbleText);

            // ---- 离线标识 ----
            if (!_bridgeOnline && _offlineCounter > 5)
            {
                using (var offlineFont = new Font("Microsoft YaHei", 7))
                using (var offlineBrush = new SolidBrush(Color.FromArgb(200, 255, 80, 80)))
                    g.DrawString("离线", offlineFont, offlineBrush, PET_W / 2 - 14, PET_H - 30);
            }

            }

            // ====== 气泡 ======
        private void DrawBubble(Graphics g, string text)
        {
            var font = new Font("Microsoft YaHei", 9);
            // 跟随对话变宽，最大不超出窗口（留边距）
            float maxW = PET_W - 16;
            var sz = g.MeasureString(text, font, (int)maxW);

            float bw = Math.Min(sz.Width + 18, maxW);
            float bh = Math.Max(sz.Height + 14, 22);
            // 不让气泡太高完全盖住星澜
            float maxBh = PET_H - 115;
            if (bh > maxBh) bh = maxBh;
            float bx = (PET_W - bw) / 2;
            float by = 4;

            var bubbleRect = new RectangleF(bx, by, bw, bh);

            // 阴影
            using (var sb = new SolidBrush(Color.FromArgb(30, 0, 0, 0)))
            using (var sp = RoundedRect(new RectangleF(bx + 2, by + 2, bw, bh), 8))
                g.FillPath(sb, sp);

            // 气泡背景
            using (var bb = new SolidBrush(Color.FromArgb(235, 248, 250, 255)))
            using (var bp = RoundedRect(bubbleRect, 8))
                g.FillPath(bb, bp);

            // 边框
            using (var borderPen = new Pen(Color.FromArgb(60, 180, 120, 255), 1f))
            using (var bp = RoundedRect(bubbleRect, 8))
                g.DrawPath(borderPen, bp);

            // 文字 - 在气泡矩形内留边距
            var textRect = new RectangleF(bx + 8, by + 5, bw - 16, bh - 10);
            using (var tb = new SolidBrush(Color.FromArgb(35, 25, 60)))
                g.DrawString(text, font, tb, textRect);

            font.Dispose();
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

        // ====== 交互 ======
        private void OnPetClick(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                _isSleeping = false;
                _idleCounter = 0;

                if (!_bridgeOnline)
                {
                    _bubbleText = "网关离线了...";
                }
                else if (_hermesStatus.Equals("thinking"))
                {
                    _bubbleText = "在思考呢，等一下～";
                }
                else
                {
                    string[] reacts = {
                        "嘿嘿～", "在呢！", "戳我干嘛～",
                        "痒～", "忙着呢", "诶嘿"
                    };
                    _bubbleText = reacts[_rng.Next(reacts.Length)];
                }

                _isFollowingMouse = true;
                _followTarget = Cursor.Position;
            }
            else if (e.Button == MouseButtons.Right)
                ShowContextMenu(e.Location);
        }

        private void OnPetDrag(object sender, MouseEventArgs e)
        {
            if (_isFollowingMouse)
                _followTarget = Cursor.Position;
        }

        private void ShowContextMenu(Point loc)
        {
            var menu = new ContextMenuStrip();
            menu.Items.Add("说句话", null, (s, e) => {
                string[] msgs = { "你好呀！", "今天怎么样～", "我在哦", "盯——", "继续摸鱼中..." };
                _bubbleText = msgs[_rng.Next(msgs.Length)];
            });
            menu.Items.Add("鸿钧状态", null, (s, e) => {
                if (_bridgeOnline)
                    _bubbleText = _hermesStatus.Equals("idle") ? "闲着～" :
                                   _hermesStatus.Equals("thinking") ? "正在思考..." :
                                   _hermesStatus.Equals("responding") ? "刚回完消息" :
                                   "忙碌中";
                else
                    _bubbleText = "网关未连接";
            });
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("重置位置", null, (s, e) => {
                var screen = Screen.PrimaryScreen.WorkingArea;
                this.Location = new Point(screen.Right - PET_W - 20, screen.Bottom - PET_H - 30);
                _bubbleText = "回到原位～";
            });
            menu.Items.Add("退出", null, (s, e) => {
                _bubbleText = "拜拜～";
                var t = new System.Windows.Forms.Timer { Interval = 500 };
                t.Tick += (ss, ee) => { t.Stop(); Application.Exit(); };
                t.Start();
            });
            menu.Show(this, loc);
        }

        // ====== 鼠标穿透处理 ======
        private const int WM_NCHITTEST = 0x84;
        private const int HTTRANSPARENT = -1;
        private const int HTCLIENT = 1;
        private const int HTCAPTION = 2;

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WM_NCHITTEST)
            {
                // 获取鼠标在窗口内的位置
                Point pt = this.PointToClient(Cursor.Position);

                // 检查是否在宠物身体范围内
                int cx = PET_W / 2, cy = PET_H - 65;
                int dx = pt.X - cx, dy = pt.Y - cy;
                float dist = (float)Math.Sqrt(dx * dx + dy * dy);

                if (dist < 50)
                {
                    // 在身体范围内 - 允许点击交互
                    m.Result = (IntPtr)HTCLIENT;
                    return;
                }

                // 不在身体范围内 - 鼠标穿透
                m.Result = (IntPtr)HTTRANSPARENT;
                return;
            }
            base.WndProc(ref m);
        }
    }
}
