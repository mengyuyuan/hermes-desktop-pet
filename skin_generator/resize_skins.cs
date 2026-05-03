using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;

class SkinResizer
{
    static string[] exprs = { "happy2", "thinking", "sleepy", "surprised", "normal", "angry" };
    static string[] targetNames = { "happy", "thinking", "sleepy", "surprised", "normal", "angry" };

    static void Main()
    {
        string srcDir = @"C:\tmp\skins_src\";
        string outDir = @"D:\鸿钧浮窗\skins\";
        int targetW = 100, targetH = 130;  // 稍高一点，让角色居中

        for (int i = 0; i < exprs.Length; i++)
        {
            string src = srcDir + exprs[i] + ".png";
            string dst = outDir + targetNames[i] + ".png";

            if (!File.Exists(src))
            {
                Console.WriteLine($"  Skip (not found): {src}");
                continue;
            }

            using (var srcImg = Image.FromFile(src))
            using (var bmp = new Bitmap(targetW, targetH, PixelFormat.Format32bppArgb))
            {
                bmp.SetResolution(96, 96);
                using (var g = Graphics.FromImage(bmp))
                {
                    g.SmoothingMode = SmoothingMode.HighQuality;
                    g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                    g.Clear(Color.Transparent);

                    // 计算缩放比例，保持宽高比，裁剪居中
                    float srcRatio = (float)srcImg.Width / srcImg.Height;
                    float dstRatio = (float)targetW / targetH;
                    int sx, sy, sw, sh;

                    if (srcRatio > dstRatio)
                    {
                        sh = srcImg.Height;
                        sw = (int)(sh * dstRatio);
                        sx = (srcImg.Width - sw) / 2;
                        sy = 0;
                    }
                    else
                    {
                        sw = srcImg.Width;
                        sh = (int)(sw / dstRatio);
                        sx = 0;
                        sy = (srcImg.Height - sh) / 2;
                    }

                    g.DrawImage(srcImg,
                        new Rectangle(0, 0, targetW, targetH),
                        new Rectangle(sx, sy, sw, sh),
                        GraphicsUnit.Pixel);
                }

                bmp.Save(dst, ImageFormat.Png);
                Console.WriteLine($"  {targetNames[i]}: {srcImg.Width}x{srcImg.Height} → {targetW}x{targetH}");
            }
        }

        Console.WriteLine("Done!");
    }
}
