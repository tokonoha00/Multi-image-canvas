using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace MultiImageCanvas;

// オーバーレイの登場アニメーション
internal enum OverlayAnimationKind
{
    Blocks, // ブロックが徐々に現れる
    Fade,   // フェードイン
    Slide,  // 下からスライドイン
    Wipe,   // 左から右へワイプ
    None,   // アニメーションなし
}

internal static class OverlayAnimations
{
    public static readonly string[] Names = ["ブロック", "フェード", "スライド", "ワイプ", "なし"];

    public static OverlayAnimationKind Parse(string? name) => name switch
    {
        "フェード" => OverlayAnimationKind.Fade,
        "スライド" => OverlayAnimationKind.Slide,
        "ワイプ" => OverlayAnimationKind.Wipe,
        "なし" => OverlayAnimationKind.None,
        _ => OverlayAnimationKind.Blocks,
    };

    public static string ToName(OverlayAnimationKind kind) => kind switch
    {
        OverlayAnimationKind.Fade => "フェード",
        OverlayAnimationKind.Slide => "スライド",
        OverlayAnimationKind.Wipe => "ワイプ",
        OverlayAnimationKind.None => "なし",
        _ => "ブロック",
    };
}

// キャンバス内容をゲーム画面上などに重ねて表示する最前面ウィンドウ。
// フォーカスを奪わない (WS_EX_NOACTIVATE) ため、ゲーム操作を中断させない。
internal sealed class OverlayForm : Form
{
    private static readonly Color TransparentKeyColor = Color.FromArgb(1, 2, 3);
    private readonly CanvasSurface _canvas;

    [DllImport("user32.dll")]
    private static extern int GetWindowLong(IntPtr hWnd, int nIndex);
    [DllImport("user32.dll")]
    private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_TRANSPARENT = 0x20;
    private const int WS_EX_NOACTIVATE = 0x08000000;
    private const int WS_EX_TOOLWINDOW = 0x80;
    private const int WM_NCHITTEST = 0x0084;
    private const int HTTRANSPARENT = -1;

    private bool _isDraggingWindow;
    private Point _dragMouseStart;
    private Point _dragWindowStart;

    private bool _clickThrough;
    public bool ClickThrough
    {
        get => _clickThrough;
        set
        {
            _clickThrough = value;
            ApplyClickThrough();
        }
    }

    private readonly System.Windows.Forms.Timer _animTimer = new() { Interval = 16 };
    private float _animProgress;
    private readonly float _targetOpacity;
    private readonly int _gridSeed;
    private readonly OverlayAnimationKind _animation;
    private bool _showFrame;
    private Point _baseLocation;

    public OverlayForm(CanvasSurface canvas, bool clickThrough, float opacity, OverlayAnimationKind animation = OverlayAnimationKind.Blocks, bool showFrame = true)
    {
        _canvas = canvas;
        _clickThrough = clickThrough;
        _animation = animation;
        _showFrame = showFrame;

        FormBorderStyle = FormBorderStyle.None;
        TopMost = true;
        ShowInTaskbar = false;
        BackColor = TransparentKeyColor;
        TransparencyKey = TransparentKeyColor;
        DoubleBuffered = true;

        _targetOpacity = opacity;
        Opacity = 0;
        _gridSeed = Environment.TickCount;

        Paint += OverlayForm_Paint;
        MouseDown += OverlayForm_MouseDown;
        MouseMove += OverlayForm_MouseMove;
        MouseUp += OverlayForm_MouseUp;
        DoubleClick += OverlayForm_DoubleClick;

        _animTimer.Tick += (s, e) =>
        {
            _animProgress += 0.05f;
            if (_animProgress >= 1f)
            {
                _animProgress = 1f;
                _animTimer.Stop();
            }
            Opacity = _targetOpacity * Math.Min(1f, _animProgress * 1.5f);

            if (_animation == OverlayAnimationKind.Slide)
            {
                // 下から浮き上がりながらフェードイン
                float ease = 1f - (1f - _animProgress) * (1f - _animProgress);
                Location = new Point(_baseLocation.X, _baseLocation.Y + (int)((1f - ease) * 42f));
            }

            Invalidate();
        };

        Shown += (s, e) =>
        {
            ApplyClickThrough();
            _baseLocation = Location;

            if (_animation == OverlayAnimationKind.None)
            {
                _animProgress = 1f;
                Opacity = _targetOpacity;
                Invalidate();
            }
            else
            {
                _animTimer.Start();
            }
        };
    }

    public bool ShowFrame
    {
        get => _showFrame;
        set
        {
            if (_showFrame == value) return;
            _showFrame = value;
            Invalidate();
        }
    }

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            // ゲームからフォーカスを奪わない + Alt+Tabに出さない
            cp.ExStyle |= WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW;
            return cp;
        }
    }

    // クリックしてもアクティブ化しない
    protected override bool ShowWithoutActivation => true;

    private void ApplyClickThrough()
    {
        if (!IsHandleCreated) return;
        int extendedStyle = GetWindowLong(Handle, GWL_EXSTYLE);
        if (_clickThrough)
        {
            SetWindowLong(Handle, GWL_EXSTYLE, extendedStyle | WS_EX_TRANSPARENT);
        }
        else
        {
            SetWindowLong(Handle, GWL_EXSTYLE, extendedStyle & ~WS_EX_TRANSPARENT);
        }
    }

    private void OverlayForm_Paint(object? sender, PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.None;
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;

        g.Clear(TransparentKeyColor);

        var offset = _canvas.ScrollOffset;
        var zoom = _canvas.Zoom;

        g.ScaleTransform(zoom, zoom);
        g.TranslateTransform(-offset.X, -offset.Y, MatrixOrder.Append);

        foreach (var item in _canvas.Items)
        {
            if (!item.Visible) continue;
            if (item.IsAnimated) ImageAnimator.UpdateFrames(item.Image);

            using var attrs = new ImageAttributes();
            attrs.SetWrapMode(WrapMode.TileFlipXY);
            if (item.Opacity < 1f)
            {
                var cm = new ColorMatrix { Matrix33 = item.Opacity };
                attrs.SetColorMatrix(cm, ColorMatrixFlag.Default, ColorAdjustType.Bitmap);
            }
            g.DrawImage(item.Image, item.GetDrawPoints(), item.Crop, GraphicsUnit.Pixel, attrs);

            if (_showFrame)
            {
                var c = item.GetCornersWorld();
                float penWidth = 4.0f / zoom;
                var bounds = item.GetWorldBounds();
                if (bounds.Width > 1 && bounds.Height > 1)
                {
                    using var brush = new LinearGradientBrush(
                        bounds,
                        Color.FromArgb(90, 90, 90),
                        Color.FromArgb(15, 15, 15),
                        LinearGradientMode.ForwardDiagonal);
                    using var pen = new Pen(brush, penWidth);
                    g.DrawPolygon(pen, [c[0], c[1], c[3], c[2]]);
                }
            }
        }

        // 登場アニメーションの演出 (透過キー色で塗った部分は非表示になる)
        if (_animProgress < 1f)
        {
            g.ResetTransform();
            using var brush = new SolidBrush(TransparentKeyColor);

            switch (_animation)
            {
                case OverlayAnimationKind.Blocks:
                {
                    var rnd = new Random(_gridSeed);
                    int blockSize = 32;
                    int cols = (Width + blockSize - 1) / blockSize;
                    int rows = (Height + blockSize - 1) / blockSize;

                    for (int y = 0; y < rows; y++)
                    {
                        for (int x = 0; x < cols; x++)
                        {
                            if (rnd.NextDouble() > _animProgress)
                            {
                                g.FillRectangle(brush, x * blockSize, y * blockSize, blockSize, blockSize);
                            }
                        }
                    }
                    break;
                }
                case OverlayAnimationKind.Wipe:
                {
                    // 左から右へ表示領域を広げる
                    float revealX = Width * _animProgress;
                    if (revealX < Width)
                    {
                        g.FillRectangle(brush, revealX, 0, Width - revealX, Height);
                    }
                    break;
                }
                // Fade / Slide はOpacityと位置の変化のみ
            }
        }
    }

    private void OverlayForm_MouseDown(object? sender, MouseEventArgs e)
    {
        if (_clickThrough) return;

        if (e.Button == MouseButtons.Left)
        {
            var offset = _canvas.ScrollOffset;
            var zoom = _canvas.Zoom;
            var world = new PointF((e.X + offset.X) / zoom, (e.Y + offset.Y) / zoom);

            bool hitImage = _canvas.Items.Any(item => item.Visible && item.HitTest(world));
            if (hitImage)
            {
                _isDraggingWindow = true;
                _dragMouseStart = Cursor.Position;
                _dragWindowStart = Location;
                Capture = true;
            }
        }
    }

    private void OverlayForm_MouseMove(object? sender, MouseEventArgs e)
    {
        if (_isDraggingWindow)
        {
            var curMouse = Cursor.Position;
            var dx = curMouse.X - _dragMouseStart.X;
            var dy = curMouse.Y - _dragMouseStart.Y;
            Location = new Point(_dragWindowStart.X + dx, _dragWindowStart.Y + dy);
        }
    }

    private void OverlayForm_MouseUp(object? sender, MouseEventArgs e)
    {
        _isDraggingWindow = false;
        Capture = false;
    }

    private void OverlayForm_DoubleClick(object? sender, EventArgs e)
    {
        if (_clickThrough) return;
        Close();
    }

    private bool HitVisibleOverlayPixel(Point client)
    {
        var offset = _canvas.ScrollOffset;
        var zoom = _canvas.Zoom;
        var world = new PointF((client.X + offset.X) / zoom, (client.Y + offset.Y) / zoom);

        foreach (var item in _canvas.Items.Reverse())
        {
            if (!item.Visible || !item.HitTest(world)) continue;
            if (HitVisibleItemPixel(item, world)) return true;
        }
        return false;
    }

    private static bool HitVisibleItemPixel(CanvasItem item, PointF world)
    {
        var local = item.ToLocal(world);
        if (item.Dest.Width <= 0 || item.Dest.Height <= 0) return false;

        var u = (local.X - item.Dest.X) / item.Dest.Width;
        var v = (local.Y - item.Dest.Y) / item.Dest.Height;
        if (u < 0f || u > 1f || v < 0f || v > 1f) return false;
        if (item.FlipH) u = 1f - u;
        if (item.FlipV) v = 1f - v;

        var sx = (int)Math.Clamp(item.Crop.X + item.Crop.Width * u, 0, item.Image.Width - 1);
        var sy = (int)Math.Clamp(item.Crop.Y + item.Crop.Height * v, 0, item.Image.Height - 1);
        return item.Image is not Bitmap bmp || bmp.GetPixel(sx, sy).A > 24;
    }

    protected override void WndProc(ref Message m)
    {
        const int WM_MOUSEACTIVATE = 0x0021;
        const int MA_ACTIVATE = 1;
        if (m.Msg == WM_NCHITTEST)
        {
            var screen = new Point((short)(m.LParam.ToInt64() & 0xffff), (short)((m.LParam.ToInt64() >> 16) & 0xffff));
            if (_clickThrough || !HitVisibleOverlayPixel(PointToClient(screen)))
            {
                m.Result = HTTRANSPARENT;
                return;
            }
        }
        if (m.Msg == WM_MOUSEACTIVATE)
        {
            m.Result = MA_ACTIVATE;
            return;
        }
        base.WndProc(ref m);
    }
}
