using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.CompilerServices;

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

    public static Image Decode(string path)
    {
        var ext = Path.GetExtension(path).ToLowerInvariant();

        if (GdiExtensions.Contains(ext))
        {
            try { return DecodeGdi(path); }
            catch { /* GDI+失敗時はWICにフォールバック */ }
        }

        try { return DecodeWic(path); }
        catch when (GdiExtensions.Contains(ext) == false)
        {
            // WICも失敗した未知の形式は最後にGDI+を試す
            return DecodeGdi(path);
        }
    }

    private static Image DecodeGdi(string path)
    {
        var bytes = File.ReadAllBytes(path);
        var ms = new MemoryStream(bytes);
        var img = Image.FromStream(ms);

        if (ImageAnimator.CanAnimate(img))
        {
            // アニメーション画像はフレーム保持のため元ストリームごと保持する
            _liveStreams.Add(img, ms);
            return img;
        }

        // 静止画はビットマップに複製してストリームを解放
        var cloned = new Bitmap(img);
        img.Dispose();
        ms.Dispose();
        return cloned;
    }

    private static Image DecodeWic(string path)
    {
        var decoder = System.Windows.Media.Imaging.BitmapDecoder.Create(
            new Uri(Path.GetFullPath(path)),
            System.Windows.Media.Imaging.BitmapCreateOptions.PreservePixelFormat,
            System.Windows.Media.Imaging.BitmapCacheOption.OnLoad);

        var frame = decoder.Frames[0];
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
        }
        return bmp;
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
        using var src = Decode(path);
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
}
