using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

class SkinGenerator
{
    static int W = 100, H = 110;

    static void Main()
    {
        string dir = @"D:\鸿钧浮窗\skins\";
        Draw("normal", dir);
        Draw("happy", dir);
        Draw("surprised", dir);
        Draw("sleepy", dir);
        Draw("thinking", dir);
        Draw("angry", dir);
        Draw("shy", dir);
        Console.WriteLine("Done!");
    }

    static void Draw(string expr, string dir)
    {
        var bmp = new Bitmap(W, H, PixelFormat.Format32bppArgb);
        bmp.SetResolution(96, 96);
        var g = Graphics.FromImage(bmp);
        g.SmoothingMode = SmoothingMode.HighQuality;

        // 透明
        g.Clear(Color.Transparent);

        float cx = W / 2f, cy = H / 2f + 5;
        float bodyR = 32;

        // ===== 身体（渐变蓝色圆球）=====
        var bodyRect = new RectangleF(cx - bodyR, cy - bodyR + 2, bodyR * 2, bodyR * 2);
        using (var grad = new LinearGradientBrush(bodyRect,
            Color.FromArgb(255, 100, 180, 255),
            Color.FromArgb(255, 50, 120, 220),
            LinearGradientMode.ForwardDiagonal))
        {
            g.FillEllipse(grad, bodyRect);
        }

        // 身体高光
        using (var hl = new SolidBrush(Color.FromArgb(80, 200, 230, 255)))
            g.FillEllipse(hl, cx - bodyR * 0.4f, cy - bodyR * 0.6f, bodyR * 0.6f, bodyR * 0.4f);

        // ===== 刘海 =====
        using (var hair = new SolidBrush(Color.FromArgb(255, 160, 210, 255)))
            g.FillPie(hair, cx - bodyR * 0.9f, cy - bodyR * 0.9f - 3, bodyR * 1.8f, bodyR * 1.3f, 200, 140);

        using (var star = new SolidBrush(Color.FromArgb(220, 255, 255, 200)))
        {
            g.FillEllipse(star, cx + 8, cy - bodyR * 0.8f - 2, 6, 6);
            g.FillEllipse(star, cx - 14, cy - bodyR * 0.85f, 4, 4);
        }

        // ===== 触角 =====
        using (var antPen = new Pen(Color.FromArgb(200, 120, 200, 255), 2f))
        {
            g.DrawCurve(antPen, new PointF[] {
                new PointF(cx - 8, cy - bodyR * 0.65f),
                new PointF(cx - 16, cy - bodyR * 0.9f),
                new PointF(cx - 14, cy - bodyR * 1.05f)
            });
            g.DrawCurve(antPen, new PointF[] {
                new PointF(cx + 8, cy - bodyR * 0.65f),
                new PointF(cx + 16, cy - bodyR * 0.9f),
                new PointF(cx + 14, cy - bodyR * 1.05f)
            });
        }

        using (var tip = new SolidBrush(Color.FromArgb(220, 180, 230, 255)))
        {
            g.FillEllipse(tip, cx - 16, cy - bodyR * 1.1f, 5, 5);
            g.FillEllipse(tip, cx + 12, cy - bodyR * 1.1f, 5, 5);
        }

        using (var glow = new SolidBrush(Color.FromArgb(60, 255, 255, 255)))
        {
            g.FillEllipse(glow, cx - 17, cy - bodyR * 1.15f, 7, 7);
            g.FillEllipse(glow, cx + 11, cy - bodyR * 1.15f, 7, 7);
        }

        // ===== 眼睛 =====
        float eyeY = cy - bodyR * 0.1f;
        float eyeSpacing = 10;
        float eyeSize = 10;

        using (var white = new SolidBrush(Color.White))
        {
            for (int side = -1; side <= 1; side += 2)
            {
                float ex = cx + side * eyeSpacing;

                if (expr == "sleepy")
                {
                    using (var sleepPen = new Pen(Color.FromArgb(200, 60, 60, 80), 2f))
                        g.DrawArc(sleepPen, ex - 6, eyeY - 1, 12, 6, 0, -180);
                }
                else if (expr == "thinking")
                {
                    float eyeH = eyeSize * 1.0f;
                    g.FillEllipse(white, ex - eyeSize, eyeY - 2, eyeSize * 2, eyeH);
                    using (var pupil = new SolidBrush(Color.FromArgb(200, 60, 80, 140)))
                        g.FillEllipse(pupil, ex - 4, eyeY - 1, 8, 7);
                }
                else
                {
                    float eyeH = (expr == "normal") ? eyeSize * 1.5f : eyeSize * 1.6f;
                    g.FillEllipse(white, ex - eyeSize, eyeY - eyeSize * 0.4f, eyeSize * 2, eyeH);

                    using (var pupil = new SolidBrush(Color.FromArgb(220, 50, 80, 160)))
                        g.FillEllipse(pupil, ex - 5, eyeY, 10, 8);

                    using (var sparkle = new SolidBrush(Color.FromArgb(200, 255, 255, 255)))
                        g.FillEllipse(sparkle, ex - 4, eyeY - 2, 4, 4);
                }
            }
        }

        // ===== 腮红 =====
        int blushA = (expr == "happy" || expr == "shy") ? 80 :
                     (expr == "normal" || expr == "surprised") ? 40 : 0;
        if (blushA > 0)
        {
            using (var blush = new SolidBrush(Color.FromArgb(blushA, 255, 150, 150)))
            {
                g.FillEllipse(blush, cx - 24, cy + 7, 12, 7);
                g.FillEllipse(blush, cx + 12, cy + 7, 12, 7);
            }
        }

        // ===== 嘴巴 =====
        float mouthY = cy + 12;
        using (var mouthPen = new Pen(Color.FromArgb(200, 80, 60, 80), 1.5f))
        {
            if (expr == "happy")      g.DrawArc(mouthPen, cx - 7, mouthY - 3, 14, 9, 0, -180);
            else if (expr == "surprised") g.DrawEllipse(mouthPen, cx - 4, mouthY - 2, 8, 8);
            else if (expr == "sleepy")    g.DrawArc(mouthPen, cx - 5, mouthY + 1, 10, 6, 0, 180);
            else if (expr == "thinking")  g.DrawArc(mouthPen, cx - 4, mouthY, 8, 5, 0, -180);
            else if (expr == "angry")     g.DrawArc(mouthPen, cx - 5, mouthY + 1, 10, 5, 0, 180);
            else if (expr == "shy")       g.DrawArc(mouthPen, cx - 6, mouthY - 2, 12, 7, 0, -180);
            else                          g.DrawArc(mouthPen, cx - 5, mouthY - 1, 10, 6, 0, -180);
        }

        g.Dispose();
        bmp.Save(dir + expr + ".png", ImageFormat.Png);
        bmp.Dispose();
    }
}
