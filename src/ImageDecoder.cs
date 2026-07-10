using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace MultiImageCanvas;

// 画像デコード層。
// GDI+ (PNG/JPEG/BMP/GIF/TIFF/ICO) → WIC (WebP/HEIC/AVIF/JXR 等、OSコーデック依存) の順で試す。
// アニメGIFは元ストリームを保持して ImageAnimator で再生できる形で返す。
internal static class ImageDecoder
{
    // GDI+が直接読める形式
    private static readonly string[] GdiExtensions =
        [".png", ".jpg", ".jpeg", ".jfif", ".bmp", ".gif", ".tif", ".tiff", ".ico"];

    // WIC(Windows Imaging Component)経由。WebPはWin11標準、HEIC/AVIFはStore拡張がある場合に読める。
    private static readonly string[] WicExtensions =
        [".webp", ".heic", ".heif", ".avif", ".jxr", ".wdp", ".dds"];

    public static readonly string[] SupportedExtensions = [.. GdiExtensions, .. WicExtensions];

    // アニメGIF等はストリームを生かしておく必要があるため、Imageと紐付けて保持する
    private static readonly ConditionalWeakTable<Image, MemoryStream> _liveStreams = new();

    public static bool IsSupported(string path) =>
        SupportedExtensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);

    // fixTransparency: 透明縁のRGBにじみ補正を行うか。
    //   編集/オーバーレイでは拡大縮小で縁がにじむため true。
    //   ビュアーは等倍～フィット表示のみで補正が不要なうえ、大きな透明PNGでは
    //   この走査が非常に重い (数十秒) ため false にして即時表示する。
    public static Image Decode(string path, bool fixTransparency = true)
    {
        var ext = Path.GetExtension(path).ToLowerInvariant();

        if (GdiExtensions.Contains(ext))
        {
            try
            {
                using var fs = File.OpenRead(path);
                return DecodeGdi(fs, path, fixTransparency, maxPixels: null);
            }
            catch { /* GDI+失敗時はWICにフォールバック */ }
        }

        try
        {
            using var fs = File.OpenRead(path);
            return DecodeWic(fs, path, fixTransparency, maxPixels: null);
        }
        catch when (GdiExtensions.Contains(ext) == false)
        {
            // WICも失敗した未知の形式は最後にGDI+を試す
            using var fs = File.OpenRead(path);
            return DecodeGdi(fs, path, fixTransparency, maxPixels: null);
        }
    }

    public static Image Decode(Stream stream, string sourceName, bool fixTransparency = true, long? maxPixels = null)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceName);

        if (!stream.CanRead) throw new ArgumentException("The source stream must be readable.", nameof(stream));

        var ext = Path.GetExtension(sourceName).ToLowerInvariant();
        using var buffered = CopyToMemory(stream);

        if (GdiExtensions.Contains(ext))
        {
            try
            {
                buffered.Position = 0;
                return DecodeGdi(buffered, sourceName, fixTransparency, maxPixels);
            }
            catch { /* GDI+失敗時はWICにフォールバック */ }
        }

        buffered.Position = 0;
        try { return DecodeWic(buffered, sourceName, fixTransparency, maxPixels); }
        catch when (GdiExtensions.Contains(ext) == false)
        {
            buffered.Position = 0;
            return DecodeGdi(buffered, sourceName, fixTransparency, maxPixels);
        }
    }

    private static Image DecodeGdi(Stream stream, string sourceName, bool fixTransparency, long? maxPixels)
    {
        var ms = CopyToMemory(stream);
        ms.Position = 0;
        var img = Image.FromStream(ms, useEmbeddedColorManagement: false, validateImageData: true);

        if (ImageAnimator.CanAnimate(img))
        {
            // アニメーション画像はフレーム保持のため元ストリームごと保持する
            ValidatePixelCount(img, maxPixels, sourceName);
            _liveStreams.Add(img, ms);
            return img;
        }

        // 静止画はビットマップに複製してストリームを解放
        ValidatePixelCount(img, maxPixels, sourceName);
        var sourceHasAlpha = Image.IsAlphaPixelFormat(img.PixelFormat);
        var cloned = new Bitmap(img);
        img.Dispose();
        ms.Dispose();
        // JPEG等アルファを持たない画像は補正不要 (全ピクセル走査を省く)
        if (fixTransparency && sourceHasAlpha) FixTransparentRgb(cloned);
        return cloned;
    }

    private static Image DecodeWic(Stream stream, string sourceName, bool fixTransparency, long? maxPixels)
    {
        var ms = CopyToMemory(stream);
        ms.Position = 0;
        var decoder = System.Windows.Media.Imaging.BitmapDecoder.Create(
            ms,
            System.Windows.Media.Imaging.BitmapCreateOptions.PreservePixelFormat,
            System.Windows.Media.Imaging.BitmapCacheOption.OnLoad);

        var frame = decoder.Frames[0];
        ValidatePixelCount(frame.PixelWidth, frame.PixelHeight, maxPixels, sourceName);
        var converted = new System.Windows.Media.Imaging.FormatConvertedBitmap(
            frame, System.Windows.Media.PixelFormats.Bgra32, null, 0);

        var bmp = new Bitmap(converted.PixelWidth, converted.PixelHeight, PixelFormat.Format32bppArgb);
        var data = bmp.LockBits(new Rectangle(0, 0, bmp.Width, bmp.Height), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
        try
        {
            converted.CopyPixels(System.Windows.Int32Rect.Empty, data.Scan0, data.Stride * data.Height, data.Stride);
        }
        finally
        {
            bmp.UnlockBits(data);
            ms.Dispose();
        }
        if (fixTransparency) FixTransparentRgb(bmp);
        return bmp;
    }

    // 透明/低アルファピクセルのRGBが緑などのキー色のままだと、拡大縮小の補間で境界に色がにじむ。
    // アルファは変えず、境界付近のRGBだけ近傍の見える色へ寄せる。
    private static void FixTransparentRgb(Bitmap bmp)
    {
        if (!Image.IsAlphaPixelFormat(bmp.PixelFormat)) return;
        const byte AlphaThreshold = 24;
        var rect = new Rectangle(0, 0, bmp.Width, bmp.Height);
        var data = bmp.LockBits(rect, ImageLockMode.ReadWrite, PixelFormat.Format32bppArgb);
        try
        {
            int stride = Math.Abs(data.Stride);
            byte[] pixels = new byte[stride * bmp.Height];
            Marshal.Copy(data.Scan0, pixels, 0, pixels.Length);

            bool wroteAnything = false;
            bool changed;
            for (int pass = 0; pass < 4; pass++)
            {
                changed = false;
                for (int y = 0; y < bmp.Height; y++)
                {
                    int row = y * stride;
                    for (int x = 0; x < bmp.Width; x++)
                    {
                        int offset = row + x * 4;
                        if (pixels[offset + 3] > AlphaThreshold) continue;

                        int b = 0, g = 0, r = 0, count = 0;
                        for (int yy = Math.Max(0, y - 1); yy <= Math.Min(bmp.Height - 1, y + 1); yy++)
                        {
                            int nrow = yy * stride;
                            for (int xx = Math.Max(0, x - 1); xx <= Math.Min(bmp.Width - 1, x + 1); xx++)
                            {
                                if (xx == x && yy == y) continue;
                                int n = nrow + xx * 4;
                                if (pixels[n + 3] <= AlphaThreshold) continue;
                                b += pixels[n];
                                g += pixels[n + 1];
                                r += pixels[n + 2];
                                count++;
                            }
                        }
                        if (count == 0) continue;
                        pixels[offset] = (byte)(b / count);
                        pixels[offset + 1] = (byte)(g / count);
                        pixels[offset + 2] = (byte)(r / count);
                        changed = true;
                        wroteAnything = true;
                    }
                }
                if (!changed) break;
            }
            // 補正対象が無ければ書き戻し(全画素コピー)を省く (不透明画像の無駄を排除)
            if (wroteAnything)
            {
                Marshal.Copy(pixels, 0, data.Scan0, pixels.Length);
            }
        }
        finally
        {
            bmp.UnlockBits(data);
        }
    }

    // 読み込み失敗・ファイル欠落時のプレースホルダ画像
    public static Image CreatePlaceholder(string path, int width = 320, int height = 200)
    {
        var bmp = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(bmp);
        g.Clear(Color.FromArgb(60, 60, 60));
        using var borderPen = new Pen(Color.FromArgb(120, 120, 120), 2) { DashStyle = System.Drawing.Drawing2D.DashStyle.Dash };
        g.DrawRectangle(borderPen, 4, 4, width - 8, height - 8);

        using var font = new Font("Segoe UI", 9f);
        using var brush = new SolidBrush(Color.FromArgb(200, 200, 200));
        var text = "画像が見つかりません\n" + Path.GetFileName(path);
        var rect = new RectangleF(10, 10, width - 20, height - 20);
        using var fmt = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center, Trimming = StringTrimming.EllipsisPath };
        g.DrawString(text, font, brush, rect, fmt);
        return bmp;
    }

    // サムネイル生成 (縦横比維持・中央配置)
    public static Bitmap CreateThumbnail(string path, int size)
    {
        using var src = Decode(path, fixTransparency: false);
        var thumb = new Bitmap(size, size, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(thumb);
        g.Clear(Color.Transparent);
        g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBilinear;

        float scale = Math.Min(size / (float)src.Width, size / (float)src.Height);
        float w = src.Width * scale;
        float h = src.Height * scale;
        g.DrawImage(src, (size - w) / 2f, (size - h) / 2f, w, h);
        return thumb;
    }

    private static MemoryStream CopyToMemory(Stream stream)
    {
        if (stream is MemoryStream memory && memory.Position == 0)
        {
            return new MemoryStream(memory.ToArray(), writable: false);
        }

        var copy = new MemoryStream();
        stream.CopyTo(copy);
        copy.Position = 0;
        return copy;
    }

    private static void ValidatePixelCount(Image image, long? maxPixels, string sourceName) =>
        ValidatePixelCount(image.Width, image.Height, maxPixels, sourceName);

    private static void ValidatePixelCount(int width, int height, long? maxPixels, string sourceName)
    {
        if (maxPixels is null) return;

        long pixels = (long)width * height;
        if (pixels > maxPixels.Value)
        {
            throw new InvalidDataException($"Image pixel limit exceeded: {sourceName}");
        }
    }
}
