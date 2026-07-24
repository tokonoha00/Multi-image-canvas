using System.Drawing;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows.Forms;

namespace MultiImageCanvas;

// キャンバス上の1画像
internal sealed class CanvasItem
{
    public required Image Image { get; set; }
    public required string Path { get; init; }

    // ワールド座標での配置矩形（回転前の軸平行矩形。回転は中心基準で適用）
    public RectangleF Dest { get; set; }
    // 元画像ピクセル座標でのクロップ領域
    public RectangleF Crop { get; set; }
    // 度数法。時計回り
    public float Rotation { get; set; }
    public bool FlipH { get; set; }
    public bool FlipV { get; set; }
    public float Opacity { get; set; } = 1f;
    public bool Visible { get; set; } = true;
    public bool IsPlaceholder { get; set; }
    public bool IsAnimated { get; set; }

    public PointF Center => new(Dest.X + Dest.Width / 2f, Dest.Y + Dest.Height / 2f);

    public string DisplayName => System.IO.Path.GetFileName(Path);

    // ワールド座標 → アイテムローカル座標（回転を打ち消した座標系）
    public PointF ToLocal(PointF world) => GeometryUtil.RotatePoint(world, Center, -Rotation);
    public PointF ToWorld(PointF local) => GeometryUtil.RotatePoint(local, Center, Rotation);

    // 回転適用後の四隅 (UL, UR, LL, LR)
    public PointF[] GetCornersWorld()
    {
        var d = Dest;
        var c = Center;
        return
        [
            GeometryUtil.RotatePoint(new PointF(d.Left, d.Top), c, Rotation),
            GeometryUtil.RotatePoint(new PointF(d.Right, d.Top), c, Rotation),
            GeometryUtil.RotatePoint(new PointF(d.Left, d.Bottom), c, Rotation),
            GeometryUtil.RotatePoint(new PointF(d.Right, d.Bottom), c, Rotation),
        ];
    }

    // DrawImage用の3点 (UL, UR, LL)。反転はテクスチャマッピングの点順序入替で表現する
    public PointF[] GetDrawPoints()
    {
        var c = GetCornersWorld(); // UL UR LL LR
        var ul = c[0]; var ur = c[1]; var ll = c[2]; var lr = c[3];
        if (FlipH) { (ul, ur) = (ur, ul); (ll, lr) = (lr, ll); }
        if (FlipV) { (ul, ll) = (ll, ul); (ur, lr) = (lr, ur); }
        return [ul, ur, ll];
    }

    // 回転を考慮した外接矩形（スナップ・全体表示・書き出しの範囲計算に使用）
    public RectangleF GetWorldBounds()
    {
        var c = GetCornersWorld();
        float minX = c.Min(p => p.X), maxX = c.Max(p => p.X);
        float minY = c.Min(p => p.Y), maxY = c.Max(p => p.Y);
        return new RectangleF(minX, minY, maxX - minX, maxY - minY);
    }

    public bool HitTest(PointF world)
    {
        var local = ToLocal(world);
        return Dest.Contains(local);
    }

    public ItemState Snapshot() => new(Dest, Crop, Rotation, FlipH, FlipV, Opacity, Visible);

    public void Restore(ItemState s)
    {
        Dest = s.Dest;
        Crop = s.Crop;
        Rotation = s.Rotation;
        FlipH = s.FlipH;
        FlipV = s.FlipV;
        Opacity = s.Opacity;
        Visible = s.Visible;
    }
}

// Undo/Redo用の状態スナップショット
internal readonly record struct ItemState(
    RectangleF Dest, RectangleF Crop, float Rotation, bool FlipH, bool FlipV, float Opacity, bool Visible);

internal sealed class PaintStroke
{
    public List<PointF> Points { get; } = [];
    public Color Color { get; init; }
    public float Width { get; init; }
}

internal static class GeometryUtil
{
    public static PointF RotatePoint(PointF p, PointF center, float degrees)
    {
        if (degrees == 0f) return p;
        double rad = degrees * Math.PI / 180.0;
        double cos = Math.Cos(rad), sin = Math.Sin(rad);
        float dx = p.X - center.X, dy = p.Y - center.Y;
        return new PointF(
            (float)(center.X + dx * cos - dy * sin),
            (float)(center.Y + dx * sin + dy * cos));
    }
}

// 1タブ = 1ドキュメント。画像リスト・ビュー状態・Undo履歴を持つ
internal sealed class CanvasDocument : IDisposable
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; }
    public string? FilePath { get; set; }
    public bool Dirty { get; set; }
    public Keys SwitchShortcut { get; set; } = Keys.None;

    public List<CanvasItem> Items { get; } = [];
    public List<PaintStroke> Strokes { get; } = [];
    public float Zoom { get; set; } = 1f;
    public PointF Scroll { get; set; }
    public Point? OverlayLocation { get; set; }

    public UndoStack Undo { get; } = new();

    // ドキュメントが生成した全ビットマップ。Undo復元に備え、破棄はドキュメント破棄時のみ行う
    private readonly HashSet<Image> _ownedImages = [];

    public event EventHandler? Changed;

    public CanvasDocument(string? name)
    {
        Name = string.IsNullOrWhiteSpace(name) ? "キャンバス1" : name;
    }

    internal static string FindAvailableDefaultName(IEnumerable<string> names)
    {
        const string prefix = "キャンバス";
        var used = new HashSet<int>();
        foreach (var name in names)
        {
            if (name.StartsWith(prefix, StringComparison.Ordinal)
                && int.TryParse(name.AsSpan(prefix.Length), out int number)
                && number > 0)
            {
                used.Add(number);
            }
        }

        int available = 1;
        while (used.Contains(available)) available++;
        return $"{prefix}{available}";
    }

    public void NotifyChanged()
    {
        Dirty = true;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void RegisterImage(Image image) => _ownedImages.Add(image);

    public bool UnregisterImage(Image image) => _ownedImages.Remove(image);

    public void Dispose()
    {
        foreach (var img in _ownedImages)
        {
            ImageAnimator.StopAnimate(img, OnNoopFrameChanged);
            img.Dispose();
        }
        _ownedImages.Clear();
        Items.Clear();
        Strokes.Clear();
        Undo.Clear();
    }

    private static void OnNoopFrameChanged(object? s, EventArgs e) { }
}

// ===== キャンバス保存 (JSON) =====

internal sealed record LayoutItemDto(
    string Path,
    float[] Dest,
    float[] Crop,
    float Rotation,
    bool FlipH,
    bool FlipV,
    float Opacity,
    bool Visible);

internal sealed record LayoutDto(
    int Version,
    string? Name,
    float Zoom,
    float[] Scroll,
    List<LayoutItemDto> Items,
    List<PaintStrokeDto>? Strokes = null);

internal sealed record PaintStrokeDto(
    int ColorArgb,
    float Width,
    List<float[]> Points);

internal static class LayoutSerializer
{
    public const string FileFilter = "キャンバスファイル (*.micl;*.json)|*.micl;*.json|すべてのファイル (*.*)|*.*";
    public const string DefaultExtension = "micl";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
    };

    public static LayoutDto ToDto(CanvasDocument doc) => new(
        Version: 2,
        Name: doc.Name,
        Zoom: doc.Zoom,
        Scroll: [doc.Scroll.X, doc.Scroll.Y],
        Items: doc.Items.Select(i => new LayoutItemDto(
            i.Path,
            [i.Dest.X, i.Dest.Y, i.Dest.Width, i.Dest.Height],
            [i.Crop.X, i.Crop.Y, i.Crop.Width, i.Crop.Height],
            i.Rotation, i.FlipH, i.FlipV, i.Opacity, i.Visible)).ToList(),
        Strokes: doc.Strokes.Select(s => new PaintStrokeDto(
            s.Color.ToArgb(),
            s.Width,
            s.Points.Select(p => new[] { p.X, p.Y }).ToList())).ToList());

    public static string Serialize(CanvasDocument doc) => JsonSerializer.Serialize(ToDto(doc), JsonOptions);

    public static void Save(CanvasDocument doc, string path)
    {
        File.WriteAllText(path, Serialize(doc));
        doc.FilePath = path;
        doc.Name = System.IO.Path.GetFileNameWithoutExtension(path);
        doc.Dirty = false;
    }

    public static LayoutDto? Deserialize(string json) => JsonSerializer.Deserialize<LayoutDto>(json, JsonOptions);

    // DTOからドキュメントを復元。欠落画像はプレースホルダで配置を維持する
    public static CanvasDocument FromDto(LayoutDto dto, string? filePath = null)
    {
        var doc = new CanvasDocument(dto.Name);
        doc.FilePath = filePath;
        doc.Zoom = dto.Zoom > 0 ? dto.Zoom : 1f;
        if (dto.Scroll is { Length: 2 }) doc.Scroll = new PointF(dto.Scroll[0], dto.Scroll[1]);

        foreach (var itemDto in dto.Items)
        {
            if (itemDto.Dest is not { Length: 4 }) continue;

            Image image;
            bool placeholder = false;
            bool animated = false;
            try
            {
                if (!File.Exists(itemDto.Path)) throw new FileNotFoundException();
                image = ImageDecoder.Decode(itemDto.Path);
                animated = ImageAnimator.CanAnimate(image);
            }
            catch
            {
                image = ImageDecoder.CreatePlaceholder(itemDto.Path);
                placeholder = true;
            }
            doc.RegisterImage(image);

            var crop = itemDto.Crop is { Length: 4 }
                ? new RectangleF(itemDto.Crop[0], itemDto.Crop[1], itemDto.Crop[2], itemDto.Crop[3])
                : new RectangleF(0, 0, image.Width, image.Height);

            // プレースホルダは元画像サイズと合わないため、クロップは全面にリセット
            if (placeholder) crop = new RectangleF(0, 0, image.Width, image.Height);

            doc.Items.Add(new CanvasItem
            {
                Image = image,
                Path = itemDto.Path,
                Dest = new RectangleF(itemDto.Dest[0], itemDto.Dest[1], itemDto.Dest[2], itemDto.Dest[3]),
                Crop = crop,
                Rotation = itemDto.Rotation,
                FlipH = itemDto.FlipH,
                FlipV = itemDto.FlipV,
                Opacity = Math.Clamp(itemDto.Opacity <= 0 ? 1f : itemDto.Opacity, 0.05f, 1f),
                Visible = itemDto.Visible,
                IsPlaceholder = placeholder,
                IsAnimated = animated,
            });
        }

        foreach (var strokeDto in dto.Strokes ?? [])
        {
            if (strokeDto.Points.Count < 2) continue;
            var stroke = new PaintStroke
            {
                Color = Color.FromArgb(strokeDto.ColorArgb),
                Width = Math.Clamp(strokeDto.Width, 1f, 80f),
            };
            foreach (var p in strokeDto.Points)
            {
                if (p is { Length: 2 }) stroke.Points.Add(new PointF(p[0], p[1]));
            }
            if (stroke.Points.Count >= 2) doc.Strokes.Add(stroke);
        }

        doc.Dirty = false;
        return doc;
    }

    public static CanvasDocument Load(string path)
    {
        var dto = Deserialize(File.ReadAllText(path)) ?? throw new InvalidDataException("キャンバスファイルを解析できません。");
        var doc = FromDto(dto, path);
        doc.Name = System.IO.Path.GetFileNameWithoutExtension(path);
        return doc;
    }
}
