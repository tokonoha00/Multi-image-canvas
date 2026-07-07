using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace MultiImageCanvas;

// 共通描画ヘルパー
internal static class ThemePaint
{
    // コントロールの実際の背後の色を返す。
    // 親が Color.Transparent の場合は不透明な祖先まで遡る (角丸UIの角に隙間/黒枠が出る問題の再発防止。
    // 角を塗る際は必ず Parent.BackColor ではなくこれを使うこと)
    public static Color GetBackdrop(Control? control)
    {
        var p = control?.Parent;
        while (p != null)
        {
            if (p.BackColor.A == 255 && p.BackColor != Color.Transparent) return p.BackColor;
            p = p.Parent;
        }
        return Theme.Current.Surface;
    }
}

// メニューバー: MenuStripは「一度開いたらカーソル移動だけで隣のメニューが開く」標準動作を持つ。
// また非アクティブウィンドウでも最初のクリックを食わずに処理する
internal sealed class MenuBarStrip : MenuStrip
{
    protected override void WndProc(ref Message m)
    {
        base.WndProc(ref m);
        // WM_MOUSEACTIVATE: MA_ACTIVATEANDEAT(2) → MA_ACTIVATE(1) に差し替え、
        // ウィンドウ切替クリックでもそのままボタンが反応するようにする
        if (m.Msg == 0x0021 && m.Result == (IntPtr)2) m.Result = (IntPtr)1;
    }
}

// 非アクティブ状態からの最初のクリックを食わないToolStrip (タブバー・ビュアーバー用)
internal class ClickThroughToolStrip : ToolStrip
{
    protected override void WndProc(ref Message m)
    {
        base.WndProc(ref m);
        if (m.Msg == 0x0021 && m.Result == (IntPtr)2) m.Result = (IntPtr)1;
    }
}

// Windows標準風のキャプションボタン (最小化/最大化/閉じる)。レンダラーが専用描画する
internal enum CaptionKind { Minimize, Maximize, Close, Help }

internal sealed class CaptionToolButton : ToolStripButton
{
    public CaptionKind Kind { get; }

    // Segoe MDL2 Assets のキャプショングリフ
    public const string GlyphMinimize = "";
    public const string GlyphMaximize = "";
    public const string GlyphRestore = "";
    public const string GlyphClose = "";
    public const string GlyphFullScreen = "";
    public const string GlyphBackToWindow = "";
    public const string GlyphHelp = "";

    public CaptionToolButton(CaptionKind kind)
    {
        Kind = kind;
        Alignment = ToolStripItemAlignment.Right;
        Margin = Padding.Empty;
        Padding = Padding.Empty;
        AutoSize = false;
        Size = new Size(46, 34);
        Font = new Font("Segoe MDL2 Assets", 9.5f);
        Text = kind switch
        {
            CaptionKind.Minimize => GlyphMinimize,
            CaptionKind.Maximize => GlyphMaximize,
            CaptionKind.Close => GlyphClose,
            _ => GlyphHelp,
        };
    }
}

// 角丸フラットボタン。通常時は背景に溶け込み、ホバー/押下時のみ角丸で強調表示する。
// BaseColor を指定すると常時その色の角丸背景、ActiveColor はトグルON状態の表示に使う。
internal class RoundedFlatButton : Button
{
    private bool _hover;
    private bool _pressed;

    public int CornerRadius { get; set; } = 8;
    public Color? BaseColor { get; set; }
    public Color? ActiveColor { get; set; }
    public Color? HoverColor { get; set; }

    public RoundedFlatButton()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        FlatStyle = FlatStyle.Flat;
        FlatAppearance.BorderSize = 0;
        TabStop = false;
    }

    protected override void OnMouseEnter(EventArgs e) { base.OnMouseEnter(e); _hover = true; Invalidate(); }
    protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); _hover = false; _pressed = false; Invalidate(); }
    protected override void OnMouseDown(MouseEventArgs e) { base.OnMouseDown(e); _pressed = true; Invalidate(); }
    protected override void OnMouseUp(MouseEventArgs e) { base.OnMouseUp(e); _pressed = false; Invalidate(); }

    protected override void OnPaint(PaintEventArgs pevent)
    {
        var g = pevent.Graphics;
        var t = Theme.Current;

        // 背景は「実際に背後にある色」で塗る (透明親でも角に隙間や黒枠を出さない)
        using (var bg = new SolidBrush(ThemePaint.GetBackdrop(this)))
        {
            g.FillRectangle(bg, ClientRectangle);
        }

        Color? fill =
            _pressed ? t.AccentDark
            : _hover ? (HoverColor ?? t.ButtonHover)
            : ActiveColor ?? BaseColor;

        var rect = new Rectangle(0, 0, Width - 1, Height - 1);
        if (fill != null && rect.Width > 0 && rect.Height > 0)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using var brush = new SolidBrush(fill.Value);
            using var path = CreateRounded(rect, Math.Min(CornerRadius, Math.Min(rect.Width, rect.Height) / 2));
            g.FillPath(brush, path);
        }

        var flags = TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis |
                    (TextAlign is ContentAlignment.MiddleLeft or ContentAlignment.TopLeft or ContentAlignment.BottomLeft
                        ? TextFormatFlags.Left
                        : TextFormatFlags.HorizontalCenter);
        var textRect = new Rectangle(Padding.Left, 0, Width - Padding.Left - Padding.Right, Height);
        TextRenderer.DrawText(g, Text, Font, textRect, ForeColor, flags);
    }

    private static GraphicsPath CreateRounded(Rectangle bounds, int radius)
    {
        var path = new GraphicsPath();
        var d = Math.Max(1, radius * 2);
        path.AddArc(bounds.X, bounds.Y, d, d, 180, 90);
        path.AddArc(bounds.Right - d, bounds.Y, d, d, 270, 90);
        path.AddArc(bounds.Right - d, bounds.Bottom - d, d, d, 0, 90);
        path.AddArc(bounds.X, bounds.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }
}

// テーマ対応の ToolStrip レンダラー
internal sealed class ThemedToolStripRenderer : ToolStripProfessionalRenderer
{
    public ThemedToolStripRenderer() : base(new ThemedColorTable()) { }

    protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e)
    {
        var color = e.ToolStrip is ToolStripDropDown ? Theme.Current.Surface : Theme.Current.ToolbarBg;
        using var brush = new SolidBrush(color);
        e.Graphics.FillRectangle(brush, e.AffectedBounds);
    }

    protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
    {
        // 境界線を描画しない
    }

    protected override void OnRenderButtonBackground(ToolStripItemRenderEventArgs e)
    {
        var g = e.Graphics;

        // Windows標準風キャプションボタン: 角丸なし・全面ホバー、閉じるは赤
        if (e.Item is CaptionToolButton caption)
        {
            if (e.Item.Selected || e.Item.Pressed)
            {
                var fill = caption.Kind == CaptionKind.Close
                    ? (e.Item.Pressed ? Color.FromArgb(0xC4, 0x2B, 0x1C) : Color.FromArgb(0xE8, 0x11, 0x23))
                    : (e.Item.Pressed
                        ? Color.FromArgb(60, Theme.Current.TextPrimary)
                        : Color.FromArgb(25, Theme.Current.TextPrimary));
                using var capBrush = new SolidBrush(fill);
                g.FillRectangle(capBrush, new Rectangle(Point.Empty, e.Item.Size));
            }
            return;
        }

        var bounds = new Rectangle(2, 2, e.Item.Width - 4, e.Item.Height - 4);
        if (e.Item is SlidingToolStripButton sliding) bounds.Offset(sliding.RenderOffsetX, 0);
        var button = e.Item as ToolStripButton;
        bool isChecked = button != null && button.Checked;
        bool isCloseBtn = e.Item.Text?.Contains('✕') == true;

        if (e.Item.Selected || e.Item.Pressed || isChecked)
        {
            Color bgColor;
            if (isCloseBtn)
            {
                bgColor = e.Item.Pressed ? Color.FromArgb(172, 25, 16) : Color.FromArgb(232, 17, 35);
            }
            else if (isChecked)
            {
                using var gradientBrush = new LinearGradientBrush(bounds, Theme.Current.Accent, Theme.Current.AccentDark, LinearGradientMode.Horizontal);
                using var gradientPath = CreateRoundedRect(bounds, 10);
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.FillPath(gradientBrush, gradientPath);
                return;
            }
            else
            {
                bgColor = e.Item.Pressed ? Theme.Current.AccentDark : Theme.Current.ButtonHover;
            }

            using var brush = new SolidBrush(bgColor);
            using var path = CreateRoundedRect(bounds, 10);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.FillPath(brush, path);
        }
    }

    protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
    {
        if (e.Item is SlidingToolStripButton sliding && sliding.RenderOffsetX != 0)
        {
            e.TextRectangle = new Rectangle(e.TextRectangle.X + sliding.RenderOffsetX, e.TextRectangle.Y, e.TextRectangle.Width, e.TextRectangle.Height);
        }
        // 閉じるボタンのホバー中はグリフを白にする (赤背景とのコントラスト)
        if (e.Item is CaptionToolButton { Kind: CaptionKind.Close } && (e.Item.Selected || e.Item.Pressed))
        {
            e.TextColor = Color.White;
        }
        else
        {
            e.TextColor = e.Item.Enabled ? Theme.Current.TextPrimary : Theme.Current.TextDisabled;
        }
        base.OnRenderItemText(e);
    }

    protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e)
    {
        var g = e.Graphics;
        using var pen = new Pen(Theme.Current.SurfaceBorder);
        if (e.Item.Owner is ToolStripDropDown)
        {
            var y = e.Item.Height / 2;
            g.DrawLine(pen, 8, y, e.Item.Width - 8, y);
        }
        else
        {
            var x = e.Item.Width / 2;
            g.DrawLine(pen, x, 4, x, e.Item.Height - 4);
        }
    }

    protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
    {
        var g = e.Graphics;

        // 通常時は親バーと同色で枠を見せない (トップレベルはメニューバー色、ドロップダウン内はSurface)
        bool topLevel = e.Item.Owner is MenuStrip;
        var baseColor = topLevel ? Theme.Current.ToolbarBg : Theme.Current.Surface;
        using (var bg = new SolidBrush(baseColor))
        {
            g.FillRectangle(bg, new Rectangle(Point.Empty, e.Item.Size));
        }

        if (e.Item.Selected || e.Item.Pressed)
        {
            var rect = topLevel
                ? new Rectangle(2, 1, e.Item.Width - 5, e.Item.Height - 3)
                : new Rectangle(0, 0, e.Item.Width, e.Item.Height);
            if (rect.Width <= 0 || rect.Height <= 0) return;
            using var brush = new SolidBrush(e.Item.Pressed ? Theme.Current.AccentDark : Theme.Current.ButtonHover);
            if (topLevel)
            {
                using var path = CreateRoundedRect(rect, 8);
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.FillPath(brush, path);
            }
            else
            {
                g.FillRectangle(brush, rect);
            }
        }
    }

    protected override void OnRenderImageMargin(ToolStripRenderEventArgs e)
    {
        using var brush = new SolidBrush(Theme.Current.Surface);
        e.Graphics.FillRectangle(brush, e.AffectedBounds);
    }

    private static GraphicsPath CreateRoundedRect(Rectangle bounds, int radius)
    {
        var path = new GraphicsPath();
        var d = radius * 2;
        path.AddArc(bounds.X, bounds.Y, d, d, 180, 90);
        path.AddArc(bounds.Right - d, bounds.Y, d, d, 270, 90);
        path.AddArc(bounds.Right - d, bounds.Bottom - d, d, d, 0, 90);
        path.AddArc(bounds.X, bounds.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }
}

internal sealed class SlidingToolStripButton(string text) : ToolStripButton(text)
{
    public int RenderOffsetX { get; set; }
}

internal sealed class ThemedColorTable : ProfessionalColorTable
{
    public override Color ToolStripGradientBegin => Theme.Current.ToolbarBg;
    public override Color ToolStripGradientEnd => Theme.Current.ToolbarBg;
    public override Color ToolStripGradientMiddle => Theme.Current.ToolbarBg;
    public override Color MenuStripGradientBegin => Theme.Current.ToolbarBg;
    public override Color MenuStripGradientEnd => Theme.Current.ToolbarBg;
    public override Color SeparatorDark => Theme.Current.SurfaceBorder;
    public override Color SeparatorLight => Theme.Current.SurfaceBorder;
    public override Color ToolStripDropDownBackground => Theme.Current.Surface;
    public override Color MenuBorder => Theme.Current.SurfaceBorder;
    public override Color MenuItemBorder => Theme.Current.AccentDark;
    public override Color MenuItemSelected => Theme.Current.ButtonHover;
    public override Color ImageMarginGradientBegin => Theme.Current.Surface;
    public override Color ImageMarginGradientMiddle => Theme.Current.Surface;
    public override Color ImageMarginGradientEnd => Theme.Current.Surface;
}

// TreeView用の細身カスタムスクロールバー
internal sealed class CustomScrollBar : Control
{
    private int _min;
    private int _max = 100;
    private int _val;
    private int _largeChange = 10;

    public Orientation Orientation { get; set; } = Orientation.Vertical;

    public int Minimum { get => _min; set { _min = value; Invalidate(); } }
    public int Maximum { get => _max; set { _max = value; Invalidate(); } }
    public int Value
    {
        get => _val;
        set
        {
            var next = Math.Clamp(value, _min, Math.Max(_min, _max - _largeChange + 1));
            if (_val == next) return;
            _val = next;
            Invalidate();
        }
    }
    public int LargeChange { get => _largeChange; set { _largeChange = Math.Max(1, value); Invalidate(); } }

    public event EventHandler? Scroll;

    private bool _isDragging;
    private int _dragStartPos;
    private int _dragStartValue;

    public CustomScrollBar()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer, true);
        BackColor = Theme.Current.Surface;
        Width = 10;
        Height = 10;
        Cursor = Cursors.Hand;
    }

    private RectangleF GetThumbRect()
    {
        if (_max <= _min) return RectangleF.Empty;
        float totalRange = _max - _min;

        if (Orientation == Orientation.Vertical)
        {
            float h = Height;
            float thumbHeight = Math.Max(20f, h * (_largeChange / totalRange));
            float trackHeight = h - thumbHeight;
            float scrollRange = totalRange - _largeChange;
            float thumbY = scrollRange <= 0 ? 0 : ((_val - _min) / scrollRange) * trackHeight;
            float thumbW = Math.Min(6f, Width);
            float thumbX = (Width - thumbW) / 2f;
            return new RectangleF(thumbX, thumbY, thumbW, thumbHeight);
        }
        else
        {
            float w = Width;
            float thumbWidth = Math.Max(20f, w * (_largeChange / totalRange));
            float trackWidth = w - thumbWidth;
            float scrollRange = totalRange - _largeChange;
            float thumbX = scrollRange <= 0 ? 0 : ((_val - _min) / scrollRange) * trackWidth;
            float thumbH = Math.Min(6f, Height);
            float thumbY = (Height - thumbH) / 2f;
            return new RectangleF(thumbX, thumbY, thumbWidth, thumbH);
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        e.Graphics.Clear(BackColor);

        var thumb = GetThumbRect();
        if (thumb.IsEmpty) return;

        Color thumbColor = _isDragging ? Color.FromArgb(180, 180, 180) : (ClientRectangle.Contains(PointToClient(Cursor.Position)) ? Color.FromArgb(150, 150, 150) : Color.FromArgb(100, 100, 100));
        using var brush = new SolidBrush(thumbColor);
        using var path = new GraphicsPath();
        const float r = 1.5f;
        path.AddArc(thumb.X, thumb.Y, r * 2, r * 2, 180, 90);
        path.AddArc(thumb.Right - r * 2, thumb.Y, r * 2, r * 2, 270, 90);
        path.AddArc(thumb.Right - r * 2, thumb.Bottom - r * 2, r * 2, r * 2, 0, 90);
        path.AddArc(thumb.X, thumb.Bottom - r * 2, r * 2, r * 2, 90, 90);
        path.CloseFigure();
        e.Graphics.FillPath(brush, path);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button != MouseButtons.Left) return;

        var thumb = GetThumbRect();
        if (thumb.Contains(e.Location))
        {
            _isDragging = true;
            _dragStartPos = Orientation == Orientation.Vertical ? e.Y : e.X;
            _dragStartValue = _val;
            Capture = true;
            Invalidate();
        }
        else
        {
            float totalRange = _max - _min;
            if (Orientation == Orientation.Vertical)
            {
                float thumbHeight = Math.Max(20f, Height * (_largeChange / totalRange));
                float trackHeight = Height - thumbHeight;
                float clickRatio = (e.Y - thumbHeight / 2f) / trackHeight;
                Value = (int)(_min + clickRatio * (totalRange - _largeChange));
            }
            else
            {
                float thumbWidth = Math.Max(20f, Width * (_largeChange / totalRange));
                float trackWidth = Width - thumbWidth;
                float clickRatio = (e.X - thumbWidth / 2f) / trackWidth;
                Value = (int)(_min + clickRatio * (totalRange - _largeChange));
            }
            Scroll?.Invoke(this, EventArgs.Empty);
        }
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (!_isDragging)
        {
            Invalidate();
            return;
        }

        float totalRange = _max - _min;
        if (Orientation == Orientation.Vertical)
        {
            float thumbHeight = Math.Max(20f, Height * (_largeChange / totalRange));
            float trackHeight = Height - thumbHeight;
            if (trackHeight > 0)
            {
                float dy = e.Y - _dragStartPos;
                float valChange = (dy / trackHeight) * (totalRange - _largeChange);
                Value = (int)(_dragStartValue + valChange);
                Scroll?.Invoke(this, EventArgs.Empty);
            }
        }
        else
        {
            float thumbWidth = Math.Max(20f, Width * (_largeChange / totalRange));
            float trackWidth = Width - thumbWidth;
            if (trackWidth > 0)
            {
                float dx = e.X - _dragStartPos;
                float valChange = (dx / trackWidth) * (totalRange - _largeChange);
                Value = (int)(_dragStartValue + valChange);
                Scroll?.Invoke(this, EventArgs.Empty);
            }
        }
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        _isDragging = false;
        Capture = false;
        Invalidate();
    }
}

// グラデーション角丸ボタン
internal class GamingButton : Button
{
    private bool _isHovered;
    private bool _isPressed;

    public Color GradientStart { get; set; } = Color.FromArgb(0, 198, 255);
    public Color GradientEnd { get; set; } = Color.FromArgb(0, 114, 255);
    public int CornerRadius { get; set; } = 12;

    public GamingButton()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        FlatStyle = FlatStyle.Flat;
        FlatAppearance.BorderSize = 0;
        BackColor = Color.Transparent;
    }

    protected override void OnMouseEnter(EventArgs e)
    {
        base.OnMouseEnter(e);
        _isHovered = true;
        Invalidate();
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        _isHovered = false;
        _isPressed = false;
        Invalidate();
    }

    protected override void OnMouseDown(MouseEventArgs mevent)
    {
        base.OnMouseDown(mevent);
        _isPressed = true;
        Invalidate();
    }

    protected override void OnMouseUp(MouseEventArgs mevent)
    {
        base.OnMouseUp(mevent);
        _isPressed = false;
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs pevent)
    {
        var g = pevent.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

        var parentColor = ThemePaint.GetBackdrop(this);
        using (var bgBrush = new SolidBrush(parentColor))
        {
            g.FillRectangle(bgBrush, ClientRectangle);
        }

        var rect = new Rectangle(0, 0, Width - 1, Height - 1);
        if (rect.Width <= 0 || rect.Height <= 0) return;

        using var path = CreateRoundedPath(rect, CornerRadius);

        Color cStart = GradientStart;
        Color cEnd = GradientEnd;

        if (_isPressed)
        {
            cStart = Color.FromArgb(Math.Max(0, GradientStart.R - 30), Math.Max(0, GradientStart.G - 30), Math.Max(0, GradientStart.B - 30));
            cEnd = Color.FromArgb(Math.Max(0, GradientEnd.R - 30), Math.Max(0, GradientEnd.G - 30), Math.Max(0, GradientEnd.B - 30));
        }
        else if (_isHovered)
        {
            cStart = Color.FromArgb(Math.Min(255, GradientStart.R + 40), Math.Min(255, GradientStart.G + 40), Math.Min(255, GradientStart.B + 40));
            cEnd = Color.FromArgb(Math.Min(255, GradientEnd.R + 40), Math.Min(255, GradientEnd.G + 40), Math.Min(255, GradientEnd.B + 40));
        }

        using (var brush = new LinearGradientBrush(rect, cStart, cEnd, LinearGradientMode.Horizontal))
        {
            g.FillPath(brush, path);
        }

        var borderColor = _isHovered ? Color.FromArgb(200, 255, 255, 255) : Color.FromArgb(50, 255, 255, 255);
        using (var borderPen = new Pen(borderColor, 1.5f))
        {
            g.DrawPath(borderPen, path);
        }

        TextFormatFlags flags = TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis;
        TextRenderer.DrawText(g, Text, Font, rect, ForeColor, flags);
    }

    private static GraphicsPath CreateRoundedPath(Rectangle bounds, int radius)
    {
        var path = new GraphicsPath();
        var d = radius * 2;
        if (d <= 0)
        {
            path.AddRectangle(bounds);
            return path;
        }
        path.AddArc(bounds.X, bounds.Y, d, d, 180, 90);
        path.AddArc(bounds.Right - d, bounds.Y, d, d, 270, 90);
        path.AddArc(bounds.Right - d, bounds.Bottom - d, d, d, 0, 90);
        path.AddArc(bounds.X, bounds.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }
}

internal sealed class FlatSlider : Control
{
    private int _value = 100;
    private bool _dragging;

    public int Minimum { get; set; } = 20;
    public int Maximum { get; set; } = 100;
    public int Value
    {
        get => _value;
        set
        {
            var next = Math.Clamp(value, Minimum, Maximum);
            if (_value == next) return;
            _value = next;
            Invalidate();
        }
    }

    public event EventHandler? Scroll;

    public FlatSlider()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        Height = 24;
        Width = 120;
        BackColor = Theme.Current.Surface;
        TabStop = false;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(BackColor);
        g.SmoothingMode = SmoothingMode.AntiAlias;

        var track = new Rectangle(8, Height / 2 - 2, Math.Max(1, Width - 22), 4);
        using (var bg = new SolidBrush(BackColor))
        {
            g.FillRectangle(bg, track);
        }

        float ratio = (Value - Minimum) / (float)Math.Max(1, Maximum - Minimum);
        int thumbX = track.Left + (int)Math.Round(ratio * track.Width);
        using var active = new SolidBrush(Theme.Current.AccentDark);
        g.FillRectangle(active, track.Left, track.Top, Math.Max(0, thumbX - track.Left), track.Height);
        using var thumb = new SolidBrush(Theme.Current.Accent);
        g.FillRectangle(thumb, thumbX - 5, 4, 10, Height - 8);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button != MouseButtons.Left) return;
        _dragging = true;
        Capture = true;
        SetValueFromX(e.X);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (_dragging) SetValueFromX(e.X);
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        _dragging = false;
        Capture = false;
    }

    private void SetValueFromX(int x)
    {
        var trackLeft = 8;
        var trackWidth = Math.Max(1, Width - 22);
        var ratio = Math.Clamp((x - trackLeft) / (float)trackWidth, 0f, 1f);
        Value = Minimum + (int)Math.Round(ratio * (Maximum - Minimum));
        Scroll?.Invoke(this, EventArgs.Empty);
    }
}

// ToolStripに載せるTrackBarホスト
internal sealed class ToolStripTrackBar : ToolStripControlHost
{
    public FlatSlider TrackBar => (FlatSlider)Control;

    public ToolStripTrackBar(int min = 20, int max = 100, int value = 100) : base(new FlatSlider())
    {
        TrackBar.Minimum = min;
        TrackBar.Maximum = max;
        TrackBar.Value = value;
        TrackBar.Height = 24;
        TrackBar.Width = 120;
        TrackBar.BackColor = Theme.Current.Surface;
        BackColor = Theme.Current.Surface;
    }
}

// 1行テキスト入力ダイアログ (キャンバス名の変更などに使用)
internal sealed class TextInputForm : Form
{
    private readonly TextBox _box = new();

    public string Value => _box.Text.Trim();

    public TextInputForm(string title, string label, string initial)
    {
        var t = Theme.Current;
        Text = title;
        Width = 420;
        Height = 180;
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        BackColor = t.Background;
        ForeColor = t.TextPrimary;
        Font = new Font("Segoe UI", 9f);

        var caption = new Label
        {
            Text = label,
            Dock = DockStyle.Top,
            Height = 28,
            Padding = new Padding(14, 8, 14, 0),
            ForeColor = t.TextPrimary,
        };

        var boxHost = new Panel { Dock = DockStyle.Top, Height = 36, Padding = new Padding(14, 4, 14, 4), BackColor = t.Background };
        _box.Dock = DockStyle.Fill;
        _box.BackColor = t.SurfaceLight;
        _box.ForeColor = t.TextPrimary;
        _box.BorderStyle = BorderStyle.FixedSingle;
        _box.Text = initial;
        boxHost.Controls.Add(_box);

        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            Height = 46,
            FlowDirection = FlowDirection.RightToLeft,
            Padding = new Padding(10),
            BackColor = t.Background,
        };
        Button Make(string text, DialogResult result)
        {
            var b = new RoundedFlatButton
            {
                Text = text,
                Width = 90,
                ForeColor = t.TextPrimary,
                CornerRadius = 8,
                BaseColor = t.ButtonBg,
            };
            b.Click += (_, _) => { DialogResult = result; Close(); };
            return b;
        }
        var cancel = Make(Loc.T("キャンセル"), DialogResult.Cancel);
        var ok = Make("OK", DialogResult.OK);
        buttons.Controls.Add(cancel);
        buttons.Controls.Add(ok);
        AcceptButton = ok;
        CancelButton = cancel;

        Controls.Add(buttons);
        Controls.Add(boxHost);
        Controls.Add(caption);

        Shown += (_, _) => { _box.SelectAll(); _box.Focus(); };
    }
}

// ショートカット一覧を表示するヘルプダイアログ (F1)。キー割り当て設定を反映する
internal sealed class ShortcutHelpForm : Form
{
    public event EventHandler? EditRequested;

    public ShortcutHelpForm(KeyMap keyMap)
    {
        Text = Loc.T("ショートカット一覧");
        Width = 560;
        Height = 640;
        StartPosition = FormStartPosition.CenterParent;
        BackColor = Theme.Current.Background;
        ForeColor = Theme.Current.TextPrimary;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;

        var box = new TextBox
        {
            Multiline = true,
            ReadOnly = true,
            Dock = DockStyle.Fill,
            BackColor = Theme.Current.Surface,
            ForeColor = Theme.Current.TextPrimary,
            BorderStyle = BorderStyle.None,
            ScrollBars = ScrollBars.Vertical,
            Font = new Font("Segoe UI", 10f),
            TabStop = false,
        };
        // 設定で変更可能なキーは現在の割り当てを表示する
        var lines = new List<string>();
        string? currentCategory = null;
        foreach (var binding in keyMap.Bindings)
        {
            if (binding.Category != currentCategory)
            {
                if (currentCategory != null) lines.Add("");
                lines.Add($"◆ {Loc.T(binding.Category)} ({Loc.T("設定で変更可")})");
                currentCategory = binding.Category;
            }
            lines.Add($"  {KeyMap.ToDisplay(binding.Current),-18} {Loc.T(binding.Label)}");
        }

        lines.AddRange(Loc.IsEnglish
            ?
            [
                "",
                "◆ Fixed shortcuts",
                "  Ctrl+Shift+Z       Redo (alternate binding)",
                "  Ctrl+Tab           Next tab",
                "  Back               Delete selected image",
                "  Arrow keys         Move 1px (Shift for 10px)",
                "  ] / [              Forward / backward (Shift for front/back)",
                "  Esc                Clear selection",
                "",
                "◆ Mouse",
                "  Wheel              Zoom around cursor",
                "  Middle drag        Pan canvas",
                "  Right drag         Pan canvas (outside images)",
                "  Space+drag         Pan canvas",
                "  Right click image  Action menu",
                "",
                "◆ Overlay (global hotkeys, active in other apps)",
                "  Ctrl+Alt+H         Show/hide overlay",
                "  Ctrl+Alt+T         Toggle click-through",
                "  Ctrl+Alt+PgUp      Increase overlay opacity",
                "  Ctrl+Alt+PgDn      Decrease overlay opacity",
                "",
                "Exclusive fullscreen games cannot show the overlay.",
                "Set the game to borderless windowed mode.",
            ]
            :
            [
                "",
                "◆ 固定ショートカット",
                "  Ctrl+Shift+Z       やり直す (別割り当て)",
                "  Ctrl+Tab           次のタブへ",
                "  Back               選択画像を削除",
                "  矢印キー            1px移動 (Shiftで10px)",
                "  ] / [              前面へ / 背面へ (Shiftで最前面/最背面)",
                "  Esc                選択解除",
                "",
                "◆ マウス操作",
                "  ホイール            カーソル位置基準ズーム",
                "  中ボタンドラッグ     キャンバスをパン",
                "  右ボタンドラッグ     キャンバスをパン (画像外)",
                "  Space+ドラッグ      キャンバスをパン",
                "  右クリック(画像上)   操作メニュー",
                "",
                "◆ オーバーレイ (グローバルホットキー・他アプリ操作中も有効)",
                "  Ctrl+Alt+H         オーバーレイ表示/非表示",
                "  Ctrl+Alt+T         クリック透過の切替",
                "  Ctrl+Alt+PgUp      オーバーレイ不透明度を上げる",
                "  Ctrl+Alt+PgDn      オーバーレイ不透明度を下げる",
                "",
                "※フルスクリーン排他モードのゲーム上には表示できません。",
                "  ゲーム側を「ボーダレスウィンドウ」に設定してください。",
            ]);
        box.Text = string.Join(Environment.NewLine, lines);

        var pad = new Panel { Dock = DockStyle.Fill, Padding = new Padding(16), BackColor = Theme.Current.Background };
        pad.Controls.Add(box);
        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            Height = 48,
            FlowDirection = FlowDirection.RightToLeft,
            Padding = new Padding(0, 8, 16, 10),
            BackColor = Theme.Current.Background,
        };
        var editBtn = new RoundedFlatButton
        {
            Text = Loc.T("ショートカットを編集..."),
            AutoSize = true,
            ForeColor = Theme.Current.TextPrimary,
            Padding = new Padding(8, 2, 8, 2),
            CornerRadius = 8,
            BaseColor = Theme.Current.ButtonBg,
        };
        editBtn.Click += (_, _) =>
        {
            EditRequested?.Invoke(this, EventArgs.Empty);
            Close();
        };
        buttons.Controls.Add(editBtn);
        Controls.Add(pad);
        Controls.Add(buttons);

        KeyPreview = true;
        KeyDown += (_, e) => { if (e.KeyCode is Keys.Escape or Keys.F1) Close(); };
        Shown += (_, _) => { box.SelectionStart = 0; box.SelectionLength = 0; };
    }
}
