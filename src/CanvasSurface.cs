using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Windows.Forms;

namespace MultiImageCanvas;

internal enum PaintTool
{
    None,
    RedPen,
    YellowMarker,
    Eraser,
}

// 画像配置キャンバス。
// 座標系: screen = world * Zoom - scroll。仮想キャンバスの固定サイズは持たない（無限キャンバス）。
internal sealed class CanvasSurface : Control
{
    private CanvasDocument? _doc;
    private CanvasItem? _selected;

    private DragMode _dragMode = DragMode.None;
    private bool _isPanning;
    private Point _panStart;
    private MouseButtons _panButton;

    // ドラッグ開始時のスナップショット (Undo用)
    private ItemState _dragBefore;
    private RectangleF _startDest;
    private RectangleF _startCrop;
    private PointF _dragStartWorld;
    private PointF _dragStartLocal;
    private PointF _dragOrigCenter;
    private float _dragOrigRotation;
    private PointF _anchorLocal;
    private float _rotStartAngle;
    private float _rotStartRotation;
    private bool _dragMoved;

    // 右クリックメニュー判定用
    private bool _rightDownOnItem;
    private Point _rightDownPos;

    // スクロールバードラッグ
    private bool _isDraggingVScroll;
    private bool _isDraggingHScroll;
    private float _dragScrollStartPos;
    private PointF _dragScrollStartOffset;

    // スナップガイド (ワールド座標)
    private readonly List<float> _snapGuidesX = [];
    private readonly List<float> _snapGuidesY = [];

    private readonly HashSet<Image> _animating = [];
    private PaintStroke? _currentStroke;
    private bool _isErasing;
    private List<PaintStroke>? _eraseBefore;

    private const float HandleWorldSize = 12f;
    private const float DeleteWorldSize = 20f;
    private const float RotateHandleOffset = 30f;
    private const float MinDisplaySize = 24f;
    private const float MinZoom = 0.02f;
    private const float MaxZoom = 16f;
    private const float ScrollBarWidth = 10f;
    private const float ScrollBarMargin = 4f;
    private const float MarkerStrokeWidth = 18f;
    private const float EraserWidth = MarkerStrokeWidth;

    public bool SnapEnabled { get; set; }
    public bool InsertNaturalSize { get; set; }
    public float ImageImportScale { get; set; } = 1.0f;
    public PaintTool PaintTool { get; set; }
    public bool SpacePanning { get; set; }

    // グリッド線は常時表示。グリッドスナップは設定でON/OFF
    public bool GridSnapEnabled { get; set; }

    // 閲覧専用モード (ビュアー起動時)。選択・編集を無効化し、クリックはすべてパンとして扱う。
    // グリッド線も表示しない
    public bool ReadOnlyView { get; set; }

    // ドラッグ・パン等の操作中か (フローティングUIの一時退避に使用)。
    // 単純なクリックで点滅しないよう、アイテム操作は実際に動き始めてからtrueにする
    public bool IsInteracting =>
        (_dragMode != DragMode.None && _dragMoved) || _isPanning || _isDraggingVScroll || _isDraggingHScroll || _currentStroke != null || _isErasing;

    private float _bgOpacity = 1.0f;
    public float BgOpacity
    {
        get => _bgOpacity;
        set { _bgOpacity = Math.Clamp(value, 0f, 1f); Invalidate(); }
    }

    public event EventHandler? ZoomChanged;
    public event EventHandler? CanvasUpdated;
    public event EventHandler? SelectionChanged;
    // 右クリックで画像コンテキストメニューを要求
    public event EventHandler<ItemContextMenuEventArgs>? ItemContextMenuRequested;

    public CanvasDocument? Document
    {
        get => _doc;
        set
        {
            if (ReferenceEquals(_doc, value)) return;
            if (_doc != null)
            {
                _doc.Changed -= OnDocumentChanged;
                StopAllAnimations();
            }
            _doc = value;
            _selected = null;
            _dragMode = DragMode.None;
            if (_doc != null)
            {
                _doc.Changed += OnDocumentChanged;
                foreach (var item in _doc.Items) StartAnimationIfNeeded(item);
            }
            SelectionChanged?.Invoke(this, EventArgs.Empty);
            ZoomChanged?.Invoke(this, EventArgs.Empty);
            Invalidate();
        }
    }

    public IReadOnlyList<CanvasItem> Items => _doc?.Items ?? (IReadOnlyList<CanvasItem>)Array.Empty<CanvasItem>();
    public CanvasItem? Selected => _selected;

    public float Zoom => _doc?.Zoom ?? 1f;
    public PointF ScrollOffset => _doc?.Scroll ?? PointF.Empty;

    public CanvasSurface()
    {
        Dock = DockStyle.Fill;
        BackColor = Theme.Current.CanvasBg;
        DoubleBuffered = true;
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        Cursor = Cursors.Default;
        TabStop = true;
    }

    public new void Invalidate()
    {
        base.Invalidate();
        CanvasUpdated?.Invoke(this, EventArgs.Empty);
    }

    private void OnDocumentChanged(object? sender, EventArgs e)
    {
        // Undo等でアイテムが消えた場合は選択を解除
        if (_selected != null && _doc != null && !_doc.Items.Contains(_selected))
        {
            _selected = null;
            SelectionChanged?.Invoke(this, EventArgs.Empty);
        }
        if (_doc != null)
        {
            foreach (var item in _doc.Items) StartAnimationIfNeeded(item);
        }
        Invalidate();
    }

    // ===== アニメーションGIF =====

    private void StartAnimationIfNeeded(CanvasItem item)
    {
        if (!item.IsAnimated || _animating.Contains(item.Image)) return;
        _animating.Add(item.Image);
        ImageAnimator.Animate(item.Image, OnFrameChanged);
    }

    private void StopAllAnimations()
    {
        foreach (var img in _animating) ImageAnimator.StopAnimate(img, OnFrameChanged);
        _animating.Clear();
    }

    private void OnFrameChanged(object? sender, EventArgs e) => Invalidate();

    // ===== 座標変換・ビュー操作 =====

    public PointF ScreenToWorld(Point p)
    {
        var scroll = ScrollOffset;
        return new PointF((p.X + scroll.X) / Zoom, (p.Y + scroll.Y) / Zoom);
    }

    private RectangleF? GetContentBounds()
    {
        if (_doc == null || (_doc.Items.Count == 0 && _doc.Strokes.Count == 0)) return null;
        RectangleF? rect = null;
        foreach (var item in _doc.Items)
        {
            var b = item.GetWorldBounds();
            rect = rect == null ? b : RectangleF.Union(rect.Value, b);
        }
        foreach (var stroke in _doc.Strokes)
        {
            if (stroke.Points.Count == 0) continue;
            var b = GetStrokeBounds(stroke);
            rect = rect == null ? b : RectangleF.Union(rect.Value, b);
        }
        return rect;
    }

    private static RectangleF GetStrokeBounds(PaintStroke stroke)
    {
        if (stroke.Points.Count == 0) return RectangleF.Empty;
        var pad = stroke.Width / 2f;
        var minX = stroke.Points.Min(p => p.X) - pad;
        var minY = stroke.Points.Min(p => p.Y) - pad;
        var maxX = stroke.Points.Max(p => p.X) + pad;
        var maxY = stroke.Points.Max(p => p.Y) + pad;
        return new RectangleF(minX, minY, maxX - minX, maxY - minY);
    }

    // 画面外に完全に見失わないよう、コンテンツの一部が視界に残る範囲へ緩くクランプする
    private void SoftClampScroll()
    {
        if (_doc == null) return;
        var bounds = GetContentBounds();
        var scroll = _doc.Scroll;

        if (bounds == null)
        {
            float limit = 20000f * Zoom;
            scroll.X = Math.Clamp(scroll.X, -limit, limit);
            scroll.Y = Math.Clamp(scroll.Y, -limit, limit);
            _doc.Scroll = scroll;
            return;
        }

        var b = bounds.Value;
        float vw = ClientSize.Width, vh = ClientSize.Height;
        float keep = 60f; // 最低限視界に残すピクセル数

        float minX = b.Left * Zoom - vw + keep;
        float maxX = b.Right * Zoom - keep;
        float minY = b.Top * Zoom - vh + keep;
        float maxY = b.Bottom * Zoom - keep;

        if (minX <= maxX) scroll.X = Math.Clamp(scroll.X, minX, maxX);
        if (minY <= maxY) scroll.Y = Math.Clamp(scroll.Y, minY, maxY);
        _doc.Scroll = scroll;
    }

    public void SetZoom(float zoom) =>
        SetZoomAt(new Point(ClientSize.Width / 2, ClientSize.Height / 2), zoom);

    public void SetZoomAt(Point screenPoint, float zoom)
    {
        if (_doc == null) return;
        // ビュアーはフィット表示(=100%)より縮小できない
        float minZoom = ReadOnlyView ? _viewerBaseZoom : MinZoom;
        var next = Math.Clamp(zoom, minZoom, MaxZoom);
        if (Math.Abs(next - _doc.Zoom) < 0.00001f) return;

        var before = ScreenToWorld(screenPoint);
        _doc.Zoom = next;
        // マウスポインタの指すワールド座標がズーム前後で一致するようスクロールを合わせる
        _doc.Scroll = new PointF(before.X * next - screenPoint.X, before.Y * next - screenPoint.Y);

        if (ReadOnlyView) ApplyViewerConstraints();
        else SoftClampScroll();

        ZoomChanged?.Invoke(this, EventArgs.Empty);
        Invalidate();
    }

    // ===== ビュアーモードの表示制約 =====

    private float _viewerBaseZoom = MinZoom;

    // フィット表示をビュアーの基準(ズーム下限)として設定する
    public void SetViewerBaseline()
    {
        if (_doc == null) return;
        ZoomFitAll();
        _viewerBaseZoom = Zoom;
        ApplyViewerConstraints();
        Invalidate();
    }

    // 画像がウィンドウからはみ出しているか (パン可否の判定)
    public bool CanPanViewer()
    {
        var b = GetContentBounds();
        if (b == null) return false;
        return b.Value.Width * Zoom > ClientSize.Width + 0.5f
            || b.Value.Height * Zoom > ClientSize.Height + 0.5f;
    }

    // 軸ごとに「収まるなら中央固定 / はみ出すなら画像の端で停止」を強制する。
    // 縮小してウィンドウに収まった軸は自動的に中央へ戻る
    private void ApplyViewerConstraints()
    {
        if (_doc == null) return;
        var bounds = GetContentBounds();
        if (bounds == null) return;

        var b = bounds.Value;
        var scroll = _doc.Scroll;
        float cw = ClientSize.Width, ch = ClientSize.Height;
        float w = b.Width * Zoom, h = b.Height * Zoom;

        if (w <= cw + 0.5f)
        {
            scroll.X = (b.Left + b.Width / 2f) * Zoom - cw / 2f;
        }
        else
        {
            scroll.X = Math.Clamp(scroll.X, b.Left * Zoom, b.Right * Zoom - cw);
        }

        if (h <= ch + 0.5f)
        {
            scroll.Y = (b.Top + b.Height / 2f) * Zoom - ch / 2f;
        }
        else
        {
            scroll.Y = Math.Clamp(scroll.Y, b.Top * Zoom, b.Bottom * Zoom - ch);
        }

        _doc.Scroll = scroll;
    }

    protected override void OnClientSizeChanged(EventArgs e)
    {
        base.OnClientSizeChanged(e);
        if (!ReadOnlyView || _doc == null || ClientSize.Width <= 0 || ClientSize.Height <= 0) return;

        // ウィンドウサイズ変更時はフィット基準を取り直す。
        // 基準以下になったら再フィット、拡大中なら現在の倍率を保って端だけ揃える
        bool wasAtBase = Math.Abs(Zoom - _viewerBaseZoom) < 0.001f;
        var b = GetContentBounds();
        if (b != null)
        {
            float zw = ClientSize.Width / Math.Max(1f, b.Value.Width);
            float zh = ClientSize.Height / Math.Max(1f, b.Value.Height);
            _viewerBaseZoom = Math.Clamp(Math.Min(zw, zh) * 0.92f, MinZoom, MaxZoom);
        }

        if (wasAtBase || Zoom < _viewerBaseZoom) SetViewerBaseline();
        else ApplyViewerConstraints();
        Invalidate();
    }

    // 全画像が収まるようにズームとスクロールを調整 (Ctrl+0)
    public void ZoomFitAll()
    {
        if (_doc == null) return;
        var bounds = GetContentBounds();
        if (bounds == null)
        {
            _doc.Zoom = 1f;
            _doc.Scroll = new PointF(-ClientSize.Width / 2f, -ClientSize.Height / 2f);
            ZoomChanged?.Invoke(this, EventArgs.Empty);
            Invalidate();
            return;
        }

        var b = bounds.Value;
        float zw = ClientSize.Width / Math.Max(1f, b.Width);
        float zh = ClientSize.Height / Math.Max(1f, b.Height);
        float zoom = Math.Clamp(Math.Min(zw, zh) * 0.92f, MinZoom, MaxZoom);

        _doc.Zoom = zoom;
        float cx = b.Left + b.Width / 2f;
        float cy = b.Top + b.Height / 2f;
        _doc.Scroll = new PointF(cx * zoom - ClientSize.Width / 2f, cy * zoom - ClientSize.Height / 2f);

        ZoomChanged?.Invoke(this, EventArgs.Empty);
        Invalidate();
    }

    // ===== 画像操作 (公開API) =====

    public void AddImage(string path)
    {
        if (_doc == null) return;
        Image image;
        bool animated = false;
        try
        {
            image = ImageDecoder.Decode(path);
            animated = ImageAnimator.CanAnimate(image);
        }
        catch (Exception ex)
        {
            MessageBox.Show(FindForm(), ex.Message, Loc.T("画像を読み込めません"), MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }
        _doc.RegisterImage(image);

        float ratio = image.Width / (float)image.Height;
        float width, height;
        if (InsertNaturalSize)
        {
            width = image.Width;
            height = image.Height;
        }
        else
        {
            width = Math.Min(420f, Math.Max(120f, image.Width));
            height = width / ratio;
            if (height > 420f) { height = 420f; width = height * ratio; }
        }
        var importScale = Math.Clamp(ImageImportScale, 0.25f, 2.0f);
        width *= importScale;
        height *= importScale;
        SetZoom(importScale);

        // 現在のビューポート中央に配置する
        var center = ScreenToWorld(new Point(ClientSize.Width / 2, ClientSize.Height / 2));
        var item = new CanvasItem
        {
            Image = image,
            Path = path,
            Dest = new RectangleF(center.X - width / 2f, center.Y - height / 2f, width, height),
            Crop = new RectangleF(0, 0, image.Width, image.Height),
            IsAnimated = animated,
        };

        _doc.Items.Add(item);
        _doc.Undo.Push(new AddItemsCommand(_doc, [item]));
        StartAnimationIfNeeded(item);
        Select(item);
        _doc.NotifyChanged();
    }

    public void DuplicateSelected()
    {
        if (_doc == null || _selected == null) return;
        var src = _selected;
        var offset = 24f / Zoom;
        var clone = new CanvasItem
        {
            Image = src.Image, // ビットマップは共有 (破棄はドキュメント単位のため安全)
            Path = src.Path,
            Dest = new RectangleF(src.Dest.X + offset, src.Dest.Y + offset, src.Dest.Width, src.Dest.Height),
            Crop = src.Crop,
            Rotation = src.Rotation,
            FlipH = src.FlipH,
            FlipV = src.FlipV,
            Opacity = src.Opacity,
            IsPlaceholder = src.IsPlaceholder,
            IsAnimated = src.IsAnimated,
        };
        _doc.Items.Add(clone);
        _doc.Undo.Push(new AddItemsCommand(_doc, [clone]));
        Select(clone);
        _doc.NotifyChanged();
    }

    public void DeleteSelected()
    {
        if (_doc == null || _selected == null) return;
        var cmd = new RemoveItemsCommand(_doc, [_selected]);
        _doc.Items.Remove(_selected);
        _doc.Undo.Push(cmd);
        _selected = null;
        SelectionChanged?.Invoke(this, EventArgs.Empty);
        _doc.NotifyChanged();
    }

    public void DeleteItem(CanvasItem item)
    {
        if (_doc == null || !_doc.Items.Contains(item)) return;
        var cmd = new RemoveItemsCommand(_doc, [item]);
        _doc.Items.Remove(item);
        _doc.Undo.Push(cmd);
        if (ReferenceEquals(_selected, item))
        {
            _selected = null;
            SelectionChanged?.Invoke(this, EventArgs.Empty);
        }
        _doc.NotifyChanged();
    }

    // キャンバス内一括削除
    public void ClearAll()
    {
        if (_doc == null || _doc.Items.Count == 0) return;
        var cmd = new RemoveItemsCommand(_doc, _doc.Items.ToList(), "全画像の削除");
        _doc.Items.Clear();
        _doc.Undo.Push(cmd);
        _selected = null;
        SelectionChanged?.Invoke(this, EventArgs.Empty);
        _doc.NotifyChanged();
    }

    public void ClearPaintStrokes()
    {
        if (_doc == null || _doc.Strokes.Count == 0) return;
        var cmd = new ClearStrokesCommand(_doc, _doc.Strokes.ToList());
        _doc.Strokes.Clear();
        _doc.Undo.Push(cmd);
        _doc.NotifyChanged();
    }

    public void RotateSelected(float degrees) => ApplyTransform("回転", item =>
    {
        var rot = (item.Rotation + degrees) % 360f;
        if (rot < 0) rot += 360f;
        item.Rotation = rot;
    });

    public void FlipSelected(bool horizontal) => ApplyTransform(horizontal ? "左右反転" : "上下反転", item =>
    {
        if (horizontal) item.FlipH = !item.FlipH;
        else item.FlipV = !item.FlipV;
    });

    public void ResetCropSelected() => ApplyTransform("クロップ解除", item =>
    {
        var natural = new RectangleF(0, 0, item.Image.Width, item.Image.Height);
        if (item.Crop == natural) return;
        // 表示中の高さ基準でアスペクトを元画像に合わせ直す
        float ratio = item.Image.Width / (float)item.Image.Height;
        var center = item.Center;
        float h = item.Dest.Height;
        float w = h * ratio;
        item.Crop = natural;
        item.Dest = new RectangleF(center.X - w / 2f, center.Y - h / 2f, w, h);
    });

    public void NudgeSelected(float dxScreen, float dyScreen)
    {
        if (_doc == null || _selected == null) return;
        var before = _selected.Snapshot();
        var d = _selected.Dest;
        _selected.Dest = new RectangleF(d.X + dxScreen / Zoom, d.Y + dyScreen / Zoom, d.Width, d.Height);
        _doc.Undo.Push(new TransformCommand(_doc, _selected, before, _selected.Snapshot(), "移動"));
        _doc.NotifyChanged();
    }

    public void SetSelectedVisible(CanvasItem item, bool visible)
    {
        if (_doc == null) return;
        var before = item.Snapshot();
        item.Visible = visible;
        _doc.Undo.Push(new TransformCommand(_doc, item, before, item.Snapshot(), visible ? "表示" : "非表示"));
        _doc.NotifyChanged();
    }

    // 不透明度スライダー用: Begin→Live×N→Commit の流れで1回のUndo単位にする
    private ItemState? _opacityBefore;

    public void BeginOpacityChange()
    {
        if (_selected == null) return;
        _opacityBefore = _selected.Snapshot();
    }

    public void SetSelectedOpacityLive(float opacity)
    {
        if (_doc == null || _selected == null) return;
        _selected.Opacity = Math.Clamp(opacity, 0.05f, 1f);
        Invalidate();
    }

    public void CommitOpacityChange()
    {
        if (_doc == null || _selected == null || _opacityBefore == null) return;
        var after = _selected.Snapshot();
        if (after != _opacityBefore.Value)
        {
            _doc.Undo.Push(new TransformCommand(_doc, _selected, _opacityBefore.Value, after, "不透明度"));
            _doc.NotifyChanged();
        }
        _opacityBefore = null;
    }

    public void ReorderSelected(int direction, bool toEnd)
    {
        if (_doc == null || _selected == null) return;
        ReorderItem(_selected, direction, toEnd);
    }

    // direction: +1 = 前面(リスト末尾方向), -1 = 背面
    public void ReorderItem(CanvasItem item, int direction, bool toEnd)
    {
        if (_doc == null) return;
        int from = _doc.Items.IndexOf(item);
        if (from < 0) return;
        int to = toEnd
            ? (direction > 0 ? _doc.Items.Count - 1 : 0)
            : Math.Clamp(from + direction, 0, _doc.Items.Count - 1);
        if (to == from) return;

        _doc.Items.RemoveAt(from);
        _doc.Items.Insert(to, item);
        _doc.Undo.Push(new ReorderCommand(_doc, item, from, to));
        _doc.NotifyChanged();
    }

    public void Undo()
    {
        _doc?.Undo.Undo();
        Invalidate();
    }

    public void Redo()
    {
        _doc?.Undo.Redo();
        Invalidate();
    }

    public void Select(CanvasItem? item)
    {
        if (ReferenceEquals(_selected, item)) { Invalidate(); return; }
        _selected = item;
        SelectionChanged?.Invoke(this, EventArgs.Empty);
        Invalidate();
    }

    private void ApplyTransform(string label, Action<CanvasItem> action)
    {
        if (_doc == null || _selected == null) return;
        var before = _selected.Snapshot();
        action(_selected);
        var after = _selected.Snapshot();
        if (before != after)
        {
            _doc.Undo.Push(new TransformCommand(_doc, _selected, before, after, label));
            _doc.NotifyChanged();
        }
    }

    // ===== PNG書き出し =====

    public void ExportPng(string fileName)
    {
        if (_doc == null || (_doc.Items.Count == 0 && _doc.Strokes.Count == 0)) throw new InvalidOperationException("キャンバスに画像がありません。");
        var bounds = GetContentBounds()!.Value;
        const int margin = 40;
        var width = Math.Max(1, (int)Math.Ceiling(bounds.Width + margin * 2));
        var height = Math.Max(1, (int)Math.Ceiling(bounds.Height + margin * 2));

        using var bmp = new Bitmap(width, height);
        using var g = Graphics.FromImage(bmp);
        g.Clear(Color.Transparent);
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TranslateTransform(-bounds.Left + margin, -bounds.Top + margin);
        foreach (var item in _doc.Items)
        {
            if (!item.Visible) continue;
            using var attrs = CreateAttributes(item);
            g.DrawImage(item.Image, item.GetDrawPoints(), item.Crop, GraphicsUnit.Pixel, attrs);
        }
        DrawPaintStrokes(g);
        bmp.Save(fileName, ImageFormat.Png);
    }

    private static ImageAttributes CreateAttributes(CanvasItem item)
    {
        var attrs = new ImageAttributes();
        attrs.SetWrapMode(WrapMode.TileFlipXY);
        if (item.Opacity < 1f)
        {
            var cm = new ColorMatrix { Matrix33 = item.Opacity };
            attrs.SetColorMatrix(cm, ColorMatrixFlag.Default, ColorAdjustType.Bitmap);
        }
        return attrs;
    }

    // ===== マウス操作 =====

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        var factor = e.Delta > 0 ? 1.1f : 1f / 1.1f;
        SetZoomAt(e.Location, Zoom * factor);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        Focus();
        if (_doc == null) return;

        // 1. スクロールバー
        if (HandleScrollBarMouseDown(e)) return;

        var world = ScreenToWorld(e.Location);
        _dragStartWorld = world;
        _dragMoved = false;

        // 閲覧専用モード: どのボタンでもパンのみ (選択・編集・メニューなし)。
        // 画像がウィンドウに収まっている間はドラッグ移動できない
        if (ReadOnlyView)
        {
            if (CanPanViewer() && e.Button is MouseButtons.Left or MouseButtons.Middle or MouseButtons.Right)
            {
                StartPan(e.Location, e.Button);
            }
            return;
        }

        // 2. 中ボタン / Space+左 = ドラッグパン
        if (e.Button == MouseButtons.Middle || (SpacePanning && e.Button == MouseButtons.Left))
        {
            StartPan(e.Location, e.Button);
            return;
        }

        // 3. 右ボタン: 画像上ならコンテキストメニュー候補、空白ならパン
        if (e.Button == MouseButtons.Right)
        {
            var hitR = HitImageItem(world);
            if (hitR != null)
            {
                _rightDownOnItem = true;
                _rightDownPos = e.Location;
                Select(hitR);
            }
            else
            {
                StartPan(e.Location, e.Button);
            }
            return;
        }

        if (e.Button != MouseButtons.Left) return;

        if (PaintTool != PaintTool.None)
        {
            if (PaintTool == PaintTool.Eraser) BeginErasing(world);
            else BeginPaintStroke(world);
            return;
        }

        // 4. 選択中アイテムのハンドル
        if (_selected != null)
        {
            var handle = HitHandle(_selected, world);
            if (handle == HandleKind.Delete)
            {
                DeleteSelected();
                return;
            }
            if (handle == HandleKind.Rotate)
            {
                _dragMode = DragMode.Rotate;
                BeginItemDrag(_selected, world);
                _rotStartAngle = AngleDeg(_selected.Center, world);
                _rotStartRotation = _selected.Rotation;
                Capture = true;
                return;
            }
            if (handle != HandleKind.None)
            {
                _dragMode = HandleToDragMode(handle);
                BeginItemDrag(_selected, world);
                _anchorLocal = OppositeAnchor(_startDest, handle);
                Capture = true;
                return;
            }
        }

        // 5. 画像本体
        var hit = HitImageItem(world);
        if (hit != null)
        {
            Select(hit);
            _dragMode = DragMode.Move;
            BeginItemDrag(hit, world);
            Capture = true;
        }
        else
        {
            Select(null);
            _dragMode = DragMode.None;
        }
    }

    private void BeginPaintStroke(PointF world)
    {
        if (_doc == null) return;
        _currentStroke = new PaintStroke
        {
            Color = PaintTool == PaintTool.RedPen ? Color.FromArgb(235, 230, 32, 32) : Color.FromArgb(120, 255, 214, 40),
            Width = PaintTool == PaintTool.RedPen ? 4f : MarkerStrokeWidth,
        };
        _currentStroke.Points.Add(world);
        _currentStroke.Points.Add(world);
        _doc.Strokes.Add(_currentStroke);
        Capture = true;
        Invalidate();
    }

    private void AddPaintPoint(PointF world)
    {
        if (_currentStroke == null) return;
        var last = _currentStroke.Points[^1];
        var threshold = 1.5f / Zoom;
        var dx = world.X - last.X;
        var dy = world.Y - last.Y;
        if (dx * dx + dy * dy < threshold * threshold) return;
        _currentStroke.Points.Add(world);
        Invalidate();
    }

    private void EndPaintStroke()
    {
        if (_doc == null || _currentStroke == null) return;
        var stroke = _currentStroke;
        _currentStroke = null;

        if (stroke.Points.Count < 3)
        {
            _doc.Strokes.Remove(stroke);
            Invalidate();
            return;
        }

        _doc.Undo.Push(new AddStrokeCommand(_doc, stroke));
        _doc.NotifyChanged();
    }

    private void BeginErasing(PointF world)
    {
        if (_doc == null) return;
        _eraseBefore = _doc.Strokes.ToList();
        _isErasing = true;
        Capture = true;
        EraseAt(world);
    }

    private void EraseAt(PointF world)
    {
        if (_doc == null) return;
        var changed = false;
        for (var i = _doc.Strokes.Count - 1; i >= 0; i--)
        {
            var stroke = _doc.Strokes[i];
            var parts = SplitStrokeByEraser(stroke, world, EraserWidth / 2f);
            if (parts == null) continue;

            _doc.Strokes.RemoveAt(i);
            for (var p = parts.Count - 1; p >= 0; p--) _doc.Strokes.Insert(i, parts[p]);
            changed = true;
        }
        if (changed) Invalidate();
    }

    private void EndErasing()
    {
        if (_doc == null) return;
        _isErasing = false;
        var before = _eraseBefore;
        _eraseBefore = null;
        if (before != null && !before.SequenceEqual(_doc.Strokes))
        {
            _doc.Undo.Push(new StrokeListCommand(_doc, before, _doc.Strokes.ToList(), "消しゴム"));
            _doc.NotifyChanged();
        }
        Invalidate();
    }

    private static List<PaintStroke>? SplitStrokeByEraser(PaintStroke stroke, PointF point, float eraserRadius)
    {
        if (stroke.Points.Count < 2) return null;
        var samples = DensifyStrokePoints(stroke.Points, Math.Max(1.5f, stroke.Width / 4f));
        var radius = eraserRadius + stroke.Width / 2f;
        var radiusSq = radius * radius;
        if (!samples.Any(p => DistanceSquared(p, point) <= radiusSq)) return null;

        var parts = new List<PaintStroke>();
        var current = new List<PointF>();
        foreach (var p in samples)
        {
            if (DistanceSquared(p, point) <= radiusSq)
            {
                AddStrokePart(parts, stroke, current);
                current.Clear();
            }
            else
            {
                current.Add(p);
            }
        }
        AddStrokePart(parts, stroke, current);
        return parts;
    }

    private static List<PointF> DensifyStrokePoints(IReadOnlyList<PointF> points, float step)
    {
        var result = new List<PointF> { points[0] };
        for (var i = 1; i < points.Count; i++)
        {
            var a = points[i - 1];
            var b = points[i];
            var dx = b.X - a.X;
            var dy = b.Y - a.Y;
            var length = MathF.Sqrt(dx * dx + dy * dy);
            var count = Math.Max(1, (int)MathF.Ceiling(length / step));
            for (var j = 1; j <= count; j++)
            {
                var t = j / (float)count;
                result.Add(new PointF(a.X + dx * t, a.Y + dy * t));
            }
        }
        return result;
    }

    private static void AddStrokePart(List<PaintStroke> parts, PaintStroke source, List<PointF> points)
    {
        if (points.Count < 2) return;
        var part = new PaintStroke
        {
            Color = source.Color,
            Width = source.Width,
        };
        part.Points.AddRange(points);
        parts.Add(part);
    }

    private static float DistanceSquared(PointF a, PointF b)
    {
        var dx = a.X - b.X;
        var dy = a.Y - b.Y;
        return dx * dx + dy * dy;
    }

    private void BeginItemDrag(CanvasItem item, PointF world)
    {
        _dragBefore = item.Snapshot();
        _startDest = item.Dest;
        _startCrop = item.Crop;
        _dragOrigCenter = item.Center;
        _dragOrigRotation = item.Rotation;
        _dragStartLocal = GeometryUtil.RotatePoint(world, _dragOrigCenter, -_dragOrigRotation);
    }

    private void StartPan(Point location, MouseButtons button)
    {
        _isPanning = true;
        _panStart = location;
        _panButton = button;
        Cursor = Cursors.NoMove2D;
        Capture = true;
    }

    private bool HandleScrollBarMouseDown(MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left || _doc == null) return false;

        var (minY, maxY) = GetScrollRangeY();
        if (maxY > minY && GetVScrollTrackRect().Contains(e.Location))
        {
            var thumb = GetVScrollThumbRect();
            if (thumb.Contains(e.Location))
            {
                _isDraggingVScroll = true;
                _dragScrollStartPos = e.Y;
                _dragScrollStartOffset = _doc.Scroll;
            }
            else
            {
                var track = GetVScrollTrackRect();
                float trackH = track.Height - thumb.Height;
                float ratio = Math.Clamp((e.Y - track.Y - thumb.Height / 2f) / Math.Max(1f, trackH), 0f, 1f);
                _doc.Scroll = new PointF(_doc.Scroll.X, minY + ratio * (maxY - minY));
            }
            Capture = true;
            Invalidate();
            return true;
        }

        var (minX, maxX) = GetScrollRangeX();
        if (maxX > minX && GetHScrollTrackRect().Contains(e.Location))
        {
            var thumb = GetHScrollThumbRect();
            if (thumb.Contains(e.Location))
            {
                _isDraggingHScroll = true;
                _dragScrollStartPos = e.X;
                _dragScrollStartOffset = _doc.Scroll;
            }
            else
            {
                var track = GetHScrollTrackRect();
                float trackW = track.Width - thumb.Width;
                float ratio = Math.Clamp((e.X - track.X - thumb.Width / 2f) / Math.Max(1f, trackW), 0f, 1f);
                _doc.Scroll = new PointF(minX + ratio * (maxX - minX), _doc.Scroll.Y);
            }
            Capture = true;
            Invalidate();
            return true;
        }

        return false;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (_doc == null) return;

        if (_isDraggingVScroll)
        {
            var (minY, maxY) = GetScrollRangeY();
            var track = GetVScrollTrackRect();
            float trackH = track.Height - GetVScrollThumbRect().Height;
            if (trackH > 0 && maxY > minY)
            {
                float ratio = (e.Y - _dragScrollStartPos) / trackH;
                _doc.Scroll = new PointF(_doc.Scroll.X, Math.Clamp(_dragScrollStartOffset.Y + ratio * (maxY - minY), minY, maxY));
                Invalidate();
            }
            return;
        }

        if (_isDraggingHScroll)
        {
            var (minX, maxX) = GetScrollRangeX();
            var track = GetHScrollTrackRect();
            float trackW = track.Width - GetHScrollThumbRect().Width;
            if (trackW > 0 && maxX > minX)
            {
                float ratio = (e.X - _dragScrollStartPos) / trackW;
                _doc.Scroll = new PointF(Math.Clamp(_dragScrollStartOffset.X + ratio * (maxX - minX), minX, maxX), _doc.Scroll.Y);
                Invalidate();
            }
            return;
        }

        if (_isPanning)
        {
            var dx = e.Location.X - _panStart.X;
            var dy = e.Location.Y - _panStart.Y;
            _doc.Scroll = new PointF(_doc.Scroll.X - dx, _doc.Scroll.Y - dy);
            if (ReadOnlyView) ApplyViewerConstraints(); // ビュアーは画像の端で停止
            else SoftClampScroll();
            _panStart = e.Location;
            Invalidate();
            return;
        }

        // ビュアーモードのカーソル: パン可能なときだけ手のひら相当を出す
        if (ReadOnlyView)
        {
            Cursor = CanPanViewer() ? Cursors.SizeAll : Cursors.Default;
            return;
        }

        var world = ScreenToWorld(e.Location);

        if (_currentStroke != null)
        {
            AddPaintPoint(world);
            return;
        }
        if (_isErasing)
        {
            EraseAt(world);
            return;
        }

        if (_dragMode == DragMode.None || _selected == null)
        {
            Cursor = PaintTool != PaintTool.None ? Cursors.Cross : (SpacePanning ? Cursors.NoMove2D : GetCursor(world));
            return;
        }

        if (Math.Abs(world.X - _dragStartWorld.X) > 0.5f / Zoom || Math.Abs(world.Y - _dragStartWorld.Y) > 0.5f / Zoom)
        {
            _dragMoved = true;
        }

        switch (_dragMode)
        {
            case DragMode.Move:
                MoveSelected(world);
                break;
            case DragMode.Rotate:
                RotateByDrag(world);
                break;
            case DragMode.ResizeTopLeft:
            case DragMode.ResizeBottomLeft:
            case DragMode.ResizeBottomRight:
                ResizeFromCorner(world);
                break;
            case DragMode.CropLeft:
            case DragMode.CropRight:
            case DragMode.CropTop:
            case DragMode.CropBottom:
                CropByDrag(world);
                break;
        }

        Invalidate();
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        _isDraggingVScroll = false;
        _isDraggingHScroll = false;

        if (_isPanning && (e.Button == _panButton || e.Button == MouseButtons.Left))
        {
            _isPanning = false;
            Cursor = Cursors.Default;
        }

        // 右クリック (移動なし) → コンテキストメニュー
        if (e.Button == MouseButtons.Right && _rightDownOnItem)
        {
            _rightDownOnItem = false;
            var moved = Math.Abs(e.Location.X - _rightDownPos.X) + Math.Abs(e.Location.Y - _rightDownPos.Y) > 4;
            if (!moved && _selected != null)
            {
                ItemContextMenuRequested?.Invoke(this, new ItemContextMenuEventArgs(_selected, e.Location));
            }
        }

        if (e.Button == MouseButtons.Left && _currentStroke != null)
        {
            EndPaintStroke();
        }
        if (e.Button == MouseButtons.Left && _isErasing)
        {
            EndErasing();
        }

        // ドラッグ完了 → Undoコマンド登録
        if (_dragMode != DragMode.None && _selected != null && _doc != null && _dragMoved)
        {
            var after = _selected.Snapshot();
            if (after != _dragBefore)
            {
                var label = _dragMode switch
                {
                    DragMode.Move => "移動",
                    DragMode.Rotate => "回転",
                    DragMode.ResizeTopLeft or DragMode.ResizeBottomLeft or DragMode.ResizeBottomRight => "リサイズ",
                    _ => "トリミング",
                };
                _doc.Undo.Push(new TransformCommand(_doc, _selected, _dragBefore, after, label));
                _doc.NotifyChanged();
            }
        }

        _dragMode = DragMode.None;
        _snapGuidesX.Clear();
        _snapGuidesY.Clear();
        Capture = false;
        Invalidate();
    }

    // ===== ドラッグ変形の実装 =====

    private void MoveSelected(PointF world)
    {
        if (_selected == null) return;
        float dx = world.X - _dragStartWorld.X;
        float dy = world.Y - _dragStartWorld.Y;

        _snapGuidesX.Clear();
        _snapGuidesY.Clear();

        if ((SnapEnabled || GridSnapEnabled) && (ModifierKeys & Keys.Alt) == 0)
        {
            (dx, dy) = ApplySnap(dx, dy);
        }

        _selected.Dest = new RectangleF(_startDest.X + dx, _startDest.Y + dy, _startDest.Width, _startDest.Height);
    }

    // 他画像のエッジ/中心・グリッド線へのスナップ。ドラッグ量(dx,dy)を補正して返す
    private (float dx, float dy) ApplySnap(float dx, float dy)
    {
        if (_doc == null || _selected == null) return (dx, dy);
        float threshold = 8f / Zoom;

        // 移動中アイテムの外接矩形 (開始時矩形 + 移動量)
        var savedDest = _selected.Dest;
        _selected.Dest = new RectangleF(_startDest.X + dx, _startDest.Y + dy, _startDest.Width, _startDest.Height);
        var moving = _selected.GetWorldBounds();
        _selected.Dest = savedDest;

        float[] movingXs = [moving.Left, moving.Left + moving.Width / 2f, moving.Right];
        float[] movingYs = [moving.Top, moving.Top + moving.Height / 2f, moving.Bottom];

        float bestDx = float.MaxValue, bestDy = float.MaxValue;
        float snapLineX = 0, snapLineY = 0;

        if (SnapEnabled)
        {
            foreach (var other in _doc.Items)
            {
                if (ReferenceEquals(other, _selected) || !other.Visible) continue;
                var b = other.GetWorldBounds();
                float[] xs = [b.Left, b.Left + b.Width / 2f, b.Right];
                float[] ys = [b.Top, b.Top + b.Height / 2f, b.Bottom];

                foreach (var cand in xs)
                    foreach (var mv in movingXs)
                    {
                        float diff = cand - mv;
                        if (Math.Abs(diff) < threshold && Math.Abs(diff) < Math.Abs(bestDx)) { bestDx = diff; snapLineX = cand; }
                    }
                foreach (var cand in ys)
                    foreach (var mv in movingYs)
                    {
                        float diff = cand - mv;
                        if (Math.Abs(diff) < threshold && Math.Abs(diff) < Math.Abs(bestDy)) { bestDy = diff; snapLineY = cand; }
                    }
            }
        }

        if (GridSnapEnabled)
        {
            float step = GetGridWorldStep();
            foreach (var mv in movingXs)
            {
                float cand = (float)Math.Round(mv / step) * step;
                float diff = cand - mv;
                if (Math.Abs(diff) < threshold && Math.Abs(diff) < Math.Abs(bestDx)) { bestDx = diff; snapLineX = cand; }
            }
            foreach (var mv in movingYs)
            {
                float cand = (float)Math.Round(mv / step) * step;
                float diff = cand - mv;
                if (Math.Abs(diff) < threshold && Math.Abs(diff) < Math.Abs(bestDy)) { bestDy = diff; snapLineY = cand; }
            }
        }

        if (bestDx != float.MaxValue) dx += bestDx;
        if (bestDy != float.MaxValue) dy += bestDy;
        return (dx, dy);
    }

    private void RotateByDrag(PointF world)
    {
        if (_selected == null) return;
        float angleNow = AngleDeg(_dragOrigCenter, world);
        float rot = _rotStartRotation + (angleNow - _rotStartAngle);

        // Shift: 15°刻みスナップ / 常時: 0,90,180,270 の近傍3°でスナップ
        if ((ModifierKeys & Keys.Shift) != 0)
        {
            rot = (float)(Math.Round(rot / 15.0) * 15.0);
        }
        else
        {
            float mod = ((rot % 90f) + 90f) % 90f;
            if (mod < 3f) rot -= mod;
            else if (mod > 87f) rot += 90f - mod;
        }

        rot %= 360f;
        if (rot < 0) rot += 360f;
        _selected.Rotation = rot;
    }

    private static float AngleDeg(PointF center, PointF p) =>
        (float)(Math.Atan2(p.Y - center.Y, p.X - center.X) * 180.0 / Math.PI);

    // 角ハンドルによる比率固定リサイズ (回転対応: ドラッグ開始時のローカル座標系で計算)
    private void ResizeFromCorner(PointF world)
    {
        if (_selected == null) return;
        var local = GeometryUtil.RotatePoint(world, _dragOrigCenter, -_dragOrigRotation);

        var cropAspect = _startCrop.Width / Math.Max(1f, _startCrop.Height);
        var sx = local.X - _anchorLocal.X;
        var sy = local.Y - _anchorLocal.Y;
        var signX = Math.Sign(sx == 0 ? 1 : sx);
        var signY = Math.Sign(sy == 0 ? 1 : sy);
        var widthCandidate = Math.Abs(sx);
        var heightCandidate = Math.Abs(sy);

        var widthFromHeight = heightCandidate * cropAspect;
        float w, h;
        if (widthFromHeight <= widthCandidate)
        {
            w = widthCandidate;
            h = w / cropAspect;
        }
        else
        {
            h = heightCandidate;
            w = widthFromHeight;
        }

        w = Math.Max(MinDisplaySize, w);
        h = Math.Max(MinDisplaySize, h);
        var left = signX < 0 ? _anchorLocal.X - w : _anchorLocal.X;
        var top = signY < 0 ? _anchorLocal.Y - h : _anchorLocal.Y;

        ApplyLocalRect(new RectangleF(left, top, w, h));
    }

    // 辺ハンドルによるトリミング (回転対応・開始状態からの累積差分で計算)
    private void CropByDrag(PointF world)
    {
        if (_selected == null) return;
        var local = GeometryUtil.RotatePoint(world, _dragOrigCenter, -_dragOrigRotation);
        float dx = local.X - _dragStartLocal.X;
        float dy = local.Y - _dragStartLocal.Y;

        float imgW = _selected.Image.Width;
        float imgH = _selected.Image.Height;
        RectangleF dest = _startDest, crop = _startCrop;

        switch (_dragMode)
        {
            case DragMode.CropLeft:
            {
                float maxDx = dest.Width - MinDisplaySize;
                float minDx = -crop.Left * (dest.Width / crop.Width);
                dx = Math.Clamp(dx, minDx, maxDx);
                float cropDx = dx * (crop.Width / dest.Width);
                crop = new RectangleF(crop.Left + cropDx, crop.Top, crop.Width - cropDx, crop.Height);
                dest = new RectangleF(dest.Left + dx, dest.Top, dest.Width - dx, dest.Height);
                break;
            }
            case DragMode.CropRight:
            {
                float minDx = -(dest.Width - MinDisplaySize);
                float maxDx = (imgW - crop.Right) * (dest.Width / crop.Width);
                dx = Math.Clamp(dx, minDx, maxDx);
                float cropDx = dx * (crop.Width / dest.Width);
                crop = new RectangleF(crop.Left, crop.Top, crop.Width + cropDx, crop.Height);
                dest = new RectangleF(dest.Left, dest.Top, dest.Width + dx, dest.Height);
                break;
            }
            case DragMode.CropTop:
            {
                float maxDy = dest.Height - MinDisplaySize;
                float minDy = -crop.Top * (dest.Height / crop.Height);
                dy = Math.Clamp(dy, minDy, maxDy);
                float cropDy = dy * (crop.Height / dest.Height);
                crop = new RectangleF(crop.Left, crop.Top + cropDy, crop.Width, crop.Height - cropDy);
                dest = new RectangleF(dest.Left, dest.Top + dy, dest.Width, dest.Height - dy);
                break;
            }
            case DragMode.CropBottom:
            {
                float minDy = -(dest.Height - MinDisplaySize);
                float maxDy = (imgH - crop.Bottom) * (dest.Height / crop.Height);
                dy = Math.Clamp(dy, minDy, maxDy);
                float cropDy = dy * (crop.Height / dest.Height);
                crop = new RectangleF(crop.Left, crop.Top, crop.Width, crop.Height + cropDy);
                dest = new RectangleF(dest.Left, dest.Top, dest.Width, dest.Height + dy);
                break;
            }
        }

        if (crop.Width < 1f || crop.Height < 1f) return;
        _selected.Crop = crop;
        ApplyLocalRect(dest);
    }

    // ドラッグ開始時ローカル座標系の矩形を、回転を保ったままワールドのDestへ反映する。
    // 中心が移動しても、回転の基準を新しい中心に取り直すことでアンカー点は固定される。
    private void ApplyLocalRect(RectangleF localRect)
    {
        if (_selected == null) return;
        var cLocal = new PointF(localRect.X + localRect.Width / 2f, localRect.Y + localRect.Height / 2f);
        var cWorld = GeometryUtil.RotatePoint(cLocal, _dragOrigCenter, _dragOrigRotation);
        _selected.Dest = new RectangleF(cWorld.X - localRect.Width / 2f, cWorld.Y - localRect.Height / 2f, localRect.Width, localRect.Height);
    }

    // ===== ヒットテスト =====

    private CanvasItem? HitImageItem(PointF world)
    {
        if (_doc == null) return null;
        return _doc.Items.AsEnumerable().Reverse().FirstOrDefault(item => item.Visible && item.HitTest(world));
    }

    private IEnumerable<(PointF Center, float Size, HandleKind Kind)> GetHandleCenters(CanvasItem item)
    {
        var d = item.Dest;
        var s = HandleWorldSize / Zoom;
        var del = DeleteWorldSize / Zoom;
        var rotOff = RotateHandleOffset / Zoom;

        yield return (new PointF(d.Left, d.Top), s, HandleKind.TopLeft);
        yield return (new PointF(d.Left, d.Bottom), s, HandleKind.BottomLeft);
        yield return (new PointF(d.Right, d.Bottom), s, HandleKind.BottomRight);

        yield return (new PointF(d.Left, d.Top + d.Height / 2f), s, HandleKind.Left);
        yield return (new PointF(d.Right, d.Top + d.Height / 2f), s, HandleKind.Right);
        yield return (new PointF(d.Left + d.Width / 2f, d.Top), s, HandleKind.Top);
        yield return (new PointF(d.Left + d.Width / 2f, d.Bottom), s, HandleKind.Bottom);

        yield return (new PointF(d.Left + d.Width / 2f, d.Top - rotOff), s * 1.2f, HandleKind.Rotate);
        yield return (new PointF(d.Right - del * 0.15f, d.Top - del * 0.15f), del, HandleKind.Delete);
    }

    private HandleKind HitHandle(CanvasItem item, PointF world)
    {
        var local = item.ToLocal(world);
        foreach (var (center, size, kind) in GetHandleCenters(item).Reverse())
        {
            float half = size * 0.7f;
            if (Math.Abs(local.X - center.X) <= half && Math.Abs(local.Y - center.Y) <= half) return kind;
        }
        return HandleKind.None;
    }

    private Cursor GetCursor(PointF world)
    {
        if (_selected != null)
        {
            var h = HitHandle(_selected, world);
            var cursor = h switch
            {
                HandleKind.Delete => Cursors.Hand,
                HandleKind.Rotate => Cursors.Cross,
                HandleKind.TopLeft or HandleKind.BottomRight => Cursors.SizeNWSE,
                HandleKind.BottomLeft => Cursors.SizeNESW,
                HandleKind.Left or HandleKind.Right => Cursors.SizeWE,
                HandleKind.Top or HandleKind.Bottom => Cursors.SizeNS,
                _ when _selected.HitTest(world) => Cursors.SizeAll,
                _ => (Cursor?)null,
            };
            if (cursor != null) return cursor;
        }
        return HitImageItem(world) != null ? Cursors.SizeAll : Cursors.Default;
    }

    private static DragMode HandleToDragMode(HandleKind handle) => handle switch
    {
        HandleKind.TopLeft => DragMode.ResizeTopLeft,
        HandleKind.BottomLeft => DragMode.ResizeBottomLeft,
        HandleKind.BottomRight => DragMode.ResizeBottomRight,
        HandleKind.Left => DragMode.CropLeft,
        HandleKind.Right => DragMode.CropRight,
        HandleKind.Top => DragMode.CropTop,
        HandleKind.Bottom => DragMode.CropBottom,
        _ => DragMode.None,
    };

    private static PointF OppositeAnchor(RectangleF r, HandleKind handle) => handle switch
    {
        HandleKind.TopLeft => new PointF(r.Right, r.Bottom),
        HandleKind.BottomLeft => new PointF(r.Right, r.Top),
        HandleKind.BottomRight => new PointF(r.Left, r.Top),
        _ => new PointF(r.Left, r.Top),
    };

    // ===== 描画 =====

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var g = e.Graphics;
        var t = Theme.Current;
        var fastRender = _dragMode != DragMode.None && _dragMode != DragMode.Move || _isPanning || _isDraggingVScroll || _isDraggingHScroll;
        g.SmoothingMode = fastRender ? SmoothingMode.None : SmoothingMode.AntiAlias;
        g.InterpolationMode = fastRender ? InterpolationMode.NearestNeighbor : InterpolationMode.HighQualityBicubic;

        // 背景 (透過度に応じてチェッカーボード)
        if (_bgOpacity == 0.0f)
        {
            g.Clear(Color.LimeGreen); // TransparencyKey 用
        }
        else if (_bgOpacity < 1.0f)
        {
            g.Clear(Color.FromArgb(20, 20, 20));
            DrawCheckerboard(g);
            using var brush = new SolidBrush(Color.FromArgb((int)(_bgOpacity * 255), t.CanvasBg));
            g.FillRectangle(brush, ClientRectangle);
        }
        else
        {
            g.Clear(t.CanvasBg);
        }

        if (_bgOpacity > 0.0f && !ReadOnlyView) DrawGrid(g);

        if (_doc == null)
        {
            DrawEmptyHint(g, Loc.T("タブがありません (Ctrl+T で新規レイアウト)"));
            return;
        }

        var scroll = _doc.Scroll;
        g.ScaleTransform(Zoom, Zoom);
        g.TranslateTransform(-scroll.X, -scroll.Y, MatrixOrder.Append);

        foreach (var item in _doc.Items)
        {
            if (!item.Visible) continue;
            if (item.IsAnimated) ImageAnimator.UpdateFrames(item.Image);

            using var attrs = CreateAttributes(item);
            g.DrawImage(item.Image, item.GetDrawPoints(), item.Crop, GraphicsUnit.Pixel, attrs);

            if (item.IsPlaceholder)
            {
                using var dashPen = new Pen(Color.FromArgb(160, 200, 120, 40), 2f / Zoom) { DashStyle = DashStyle.Dash };
                var c = item.GetCornersWorld();
                g.DrawPolygon(dashPen, [c[0], c[1], c[3], c[2]]);
            }
        }

        DrawPaintStrokes(g);

        if (_selected != null && _doc.Items.Contains(_selected)) DrawSelection(g, _selected);

        g.ResetTransform();
        DrawCustomScrollbars(g);
    }

    private void DrawPaintStrokes(Graphics g)
    {
        if (_doc == null) return;
        foreach (var stroke in _doc.Strokes)
        {
            if (stroke.Points.Count < 2) continue;
            using var pen = new Pen(stroke.Color, stroke.Width)
            {
                StartCap = LineCap.Round,
                EndCap = LineCap.Round,
                LineJoin = LineJoin.Round,
            };
            g.DrawLines(pen, stroke.Points.ToArray());
        }
    }

    private void DrawEmptyHint(Graphics g, string text)
    {
        using var font = new Font(Font.FontFamily, 11f);
        using var brush = new SolidBrush(Theme.Current.TextDisabled);
        var size = g.MeasureString(text, font);
        g.DrawString(text, font, brush, (ClientSize.Width - size.Width) / 2f, (ClientSize.Height - size.Height) / 2f);
    }

    private void DrawCheckerboard(Graphics g)
    {
        const int size = 16;
        var w = ClientSize.Width;
        var h = ClientSize.Height;
        using var brushDark = new SolidBrush(Color.FromArgb(34, 34, 34));
        using var brushLight = new SolidBrush(Color.FromArgb(44, 44, 44));

        for (int y = 0; y < h; y += size)
        {
            for (int x = 0; x < w; x += size)
            {
                bool isLight = ((x / size) + (y / size)) % 2 == 0;
                g.FillRectangle(isLight ? brushLight : brushDark, x, y, size, size);
            }
        }
    }

    // グリッドの間隔 (ワールド座標)。ズームに応じて2倍/半分に調整し、見た目の密度を保つ
    private float GetGridWorldStep()
    {
        float step = 50f;
        while (step * Zoom < 24f) step *= 2f;
        while (step * Zoom > 240f && step > 6.25f) step /= 2f;
        return step;
    }

    // ワールド座標に固定されたグリッド (ズーム・スクロールしても画像との位置関係が変わらない)
    private void DrawGrid(Graphics g)
    {
        var scroll = ScrollOffset;
        float screenStep = GetGridWorldStep() * Zoom;
        if (screenStep < 4f) return;
        using var pen = new Pen(Theme.Current.CanvasGrid);

        float offsetX = (((-scroll.X) % screenStep) + screenStep) % screenStep;
        float offsetY = (((-scroll.Y) % screenStep) + screenStep) % screenStep;

        for (var x = offsetX; x < Width; x += screenStep) g.DrawLine(pen, x, 0, x, Height);
        for (var y = offsetY; y < Height; y += screenStep) g.DrawLine(pen, 0, y, Width, y);
    }

    private void DrawSelection(Graphics g, CanvasItem item)
    {
        var t = Theme.Current;
        var corners = item.GetCornersWorld(); // UL UR LL LR

        using var outlinePen = new Pen(t.Accent, 2f / Zoom);
        g.DrawPolygon(outlinePen, [corners[0], corners[1], corners[3], corners[2]]);

        foreach (var (centerLocal, size, kind) in GetHandleCenters(item))
        {
            var centerWorld = item.ToWorld(centerLocal);
            var rect = new RectangleF(centerWorld.X - size / 2f, centerWorld.Y - size / 2f, size, size);

            if (kind == HandleKind.Rotate)
            {
                // 回転ハンドル: 上辺中央から伸びる線と円
                var topCenter = item.ToWorld(new PointF(item.Dest.Left + item.Dest.Width / 2f, item.Dest.Top));
                using var linePen = new Pen(t.Accent, 1.2f / Zoom) { DashStyle = DashStyle.Dot };
                g.DrawLine(linePen, topCenter, centerWorld);
                using var rBrush = new SolidBrush(t.AccentDark);
                using var rPen = new Pen(t.Accent, 1.5f / Zoom);
                g.FillEllipse(rBrush, rect);
                g.DrawEllipse(rPen, rect);
                continue;
            }

            using var brush = new SolidBrush(kind == HandleKind.Delete ? t.DeleteBg : t.AccentDark);
            using var pen = new Pen(kind == HandleKind.Delete ? t.DeleteBorder : t.Accent, 1.2f / Zoom);

            if (kind == HandleKind.Delete)
            {
                g.FillEllipse(brush, rect);
                g.DrawEllipse(pen, rect);
                float inset = size * 0.28f;
                using var xPen = new Pen(Color.White, 2f / Zoom);
                g.DrawLine(xPen, rect.Left + inset, rect.Top + inset, rect.Right - inset, rect.Bottom - inset);
                g.DrawLine(xPen, rect.Right - inset, rect.Top + inset, rect.Left + inset, rect.Bottom - inset);
            }
            else
            {
                g.FillRectangle(brush, rect);
                g.DrawRectangle(pen, rect.X, rect.Y, rect.Width, rect.Height);
            }
        }

        // 回転角の表示
        if (_dragMode == DragMode.Rotate)
        {
            g.ResetTransform();
            var screenCenter = new PointF(item.Center.X * Zoom - ScrollOffset.X, item.Center.Y * Zoom - ScrollOffset.Y);
            using var font = new Font(Font.FontFamily, 9f);
            using var bg = new SolidBrush(Color.FromArgb(200, 20, 20, 20));
            using var fg = new SolidBrush(Color.White);
            var text = $"{item.Rotation:0.#}°";
            var size = g.MeasureString(text, font);
            g.FillRectangle(bg, screenCenter.X - size.Width / 2 - 4, screenCenter.Y - size.Height / 2 - 2, size.Width + 8, size.Height + 4);
            g.DrawString(text, font, fg, screenCenter.X - size.Width / 2, screenCenter.Y - size.Height / 2);
            g.ScaleTransform(Zoom, Zoom);
            g.TranslateTransform(-ScrollOffset.X, -ScrollOffset.Y, MatrixOrder.Append);
        }
    }

    private void DrawSnapGuides(Graphics g)
    {
        if (_snapGuidesX.Count == 0 && _snapGuidesY.Count == 0) return;
        var scroll = ScrollOffset;
        float worldTop = scroll.Y / Zoom;
        float worldBottom = (scroll.Y + ClientSize.Height) / Zoom;
        float worldLeft = scroll.X / Zoom;
        float worldRight = (scroll.X + ClientSize.Width) / Zoom;

        using var pen = new Pen(Color.FromArgb(220, 255, 90, 200), 1f / Zoom) { DashStyle = DashStyle.Dash };
        foreach (var x in _snapGuidesX) g.DrawLine(pen, x, worldTop, x, worldBottom);
        foreach (var y in _snapGuidesY) g.DrawLine(pen, worldLeft, y, worldRight, y);
    }

    // ===== カスタムスクロールバー =====

    private (float min, float max) GetScrollRangeX()
    {
        var scroll = ScrollOffset;
        var b = GetContentBounds();
        if (b == null) return (scroll.X, scroll.X);
        float min = Math.Min(b.Value.Left * Zoom, scroll.X);
        float max = Math.Max(b.Value.Right * Zoom - ClientSize.Width, scroll.X);
        return (min, max);
    }

    private (float min, float max) GetScrollRangeY()
    {
        var scroll = ScrollOffset;
        var b = GetContentBounds();
        if (b == null) return (scroll.Y, scroll.Y);
        float min = Math.Min(b.Value.Top * Zoom, scroll.Y);
        float max = Math.Max(b.Value.Bottom * Zoom - ClientSize.Height, scroll.Y);
        return (min, max);
    }

    private RectangleF GetVScrollTrackRect() => new(ClientSize.Width - ScrollBarWidth - ScrollBarMargin, ScrollBarMargin, ScrollBarWidth, ClientSize.Height - ScrollBarMargin * 2f);
    private RectangleF GetHScrollTrackRect() => new(ScrollBarMargin, ClientSize.Height - ScrollBarWidth - ScrollBarMargin, ClientSize.Width - ScrollBarMargin * 2f - ScrollBarWidth, ScrollBarWidth);

    private RectangleF GetVScrollThumbRect()
    {
        var (min, max) = GetScrollRangeY();
        if (max <= min) return RectangleF.Empty;

        var track = GetVScrollTrackRect();
        float total = max - min + ClientSize.Height;
        float thumbH = Math.Max(24f, track.Height * (ClientSize.Height / total));
        float trackH = track.Height - thumbH;
        float ratio = (ScrollOffset.Y - min) / (max - min);
        float y = ratio * trackH + track.Y;
        return new RectangleF(track.X, y, track.Width, thumbH);
    }

    private RectangleF GetHScrollThumbRect()
    {
        var (min, max) = GetScrollRangeX();
        if (max <= min) return RectangleF.Empty;

        var track = GetHScrollTrackRect();
        float total = max - min + ClientSize.Width;
        float thumbW = Math.Max(24f, track.Width * (ClientSize.Width / total));
        float trackW = track.Width - thumbW;
        float ratio = (ScrollOffset.X - min) / (max - min);
        float x = ratio * trackW + track.X;
        return new RectangleF(x, track.Y, thumbW, track.Height);
    }

    private void DrawCustomScrollbars(Graphics g)
    {
        var (minY, maxY) = GetScrollRangeY();
        if (maxY > minY)
        {
            var track = GetVScrollTrackRect();
            var thumb = GetVScrollThumbRect();

            using var bgBrush = new SolidBrush(Color.FromArgb(30, 255, 255, 255));
            g.FillRectangle(bgBrush, track);

            Color thumbColor = _isDraggingVScroll ? Color.FromArgb(180, 180, 180) : (track.Contains(PointToClient(Cursor.Position)) ? Color.FromArgb(150, 150, 150) : Color.FromArgb(90, 90, 90));
            using var brush = new SolidBrush(thumbColor);
            using var path = CreateRoundedRectPath(thumb, 3f);
            g.FillPath(brush, path);
        }

        var (minX, maxX) = GetScrollRangeX();
        if (maxX > minX)
        {
            var track = GetHScrollTrackRect();
            var thumb = GetHScrollThumbRect();

            using var bgBrush = new SolidBrush(Color.FromArgb(30, 255, 255, 255));
            g.FillRectangle(bgBrush, track);

            Color thumbColor = _isDraggingHScroll ? Color.FromArgb(180, 180, 180) : (track.Contains(PointToClient(Cursor.Position)) ? Color.FromArgb(150, 150, 150) : Color.FromArgb(90, 90, 90));
            using var brush = new SolidBrush(thumbColor);
            using var path = CreateRoundedRectPath(thumb, 3f);
            g.FillPath(brush, path);
        }
    }

    private static GraphicsPath CreateRoundedRectPath(RectangleF bounds, float r)
    {
        var path = new GraphicsPath();
        float d = r * 2f;
        path.AddArc(bounds.X, bounds.Y, d, d, 180, 90);
        path.AddArc(bounds.Right - d, bounds.Y, d, d, 270, 90);
        path.AddArc(bounds.Right - d, bounds.Bottom - d, d, d, 0, 90);
        path.AddArc(bounds.X, bounds.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }

    protected override bool IsInputKey(Keys keyData)
    {
        var key = keyData & Keys.KeyCode;
        return key is Keys.Delete or Keys.Back or Keys.Left or Keys.Right or Keys.Up or Keys.Down or Keys.Space
            || base.IsInputKey(keyData);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            StopAllAnimations();
        }
        base.Dispose(disposing);
    }

    private enum DragMode
    {
        None,
        Move,
        Rotate,
        ResizeTopLeft,
        ResizeBottomLeft,
        ResizeBottomRight,
        CropLeft,
        CropRight,
        CropTop,
        CropBottom,
    }

    private enum HandleKind
    {
        None,
        TopLeft,
        BottomLeft,
        BottomRight,
        Left,
        Right,
        Top,
        Bottom,
        Rotate,
        Delete,
    }
}

internal sealed class ItemContextMenuEventArgs(CanvasItem item, Point location) : EventArgs
{
    public CanvasItem Item { get; } = item;
    public Point Location { get; } = location;
}
