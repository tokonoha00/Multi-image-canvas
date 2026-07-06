using System.Drawing;

namespace MultiImageCanvas;

// UIテーマ定義。プリセットから選択し、Theme.Current 経由で全UIが参照する。
internal sealed class Theme
{
    public required string Name { get; init; }

    public Color Background { get; init; }
    public Color Surface { get; init; }
    public Color SurfaceLight { get; init; }
    public Color SurfaceBorder { get; init; }
    public Color ToolbarBg { get; init; }
    public Color ToolbarBorder { get; init; }
    public Color TextPrimary { get; init; }
    public Color TextSecondary { get; init; }
    public Color TextDisabled { get; init; }
    public Color Accent { get; init; }
    public Color AccentDark { get; init; }
    public Color AccentHover { get; init; }
    public Color CanvasBg { get; init; }
    public Color CanvasGrid { get; init; }
    public Color TreeBg { get; init; }
    public Color TreeImageFile { get; init; }
    public Color ButtonBg { get; init; }
    public Color ButtonHover { get; init; }
    public Color ButtonBorder { get; init; }
    public Color DeleteBg { get; init; }
    public Color DeleteBorder { get; init; }
    public Color Selection { get; init; }

    public static readonly Theme Dark = new()
    {
        Name = "ダーク",
        Background = Color.FromArgb(32, 32, 32),
        Surface = Color.FromArgb(44, 44, 44),
        SurfaceLight = Color.FromArgb(56, 56, 56),
        SurfaceBorder = Color.FromArgb(72, 72, 72),
        ToolbarBg = Color.FromArgb(38, 38, 38),
        ToolbarBorder = Color.FromArgb(60, 60, 60),
        TextPrimary = Color.FromArgb(245, 245, 245),
        TextSecondary = Color.FromArgb(170, 170, 170),
        TextDisabled = Color.FromArgb(110, 110, 110),
        Accent = Color.FromArgb(96, 205, 255),
        AccentDark = Color.FromArgb(0, 120, 212),
        AccentHover = Color.FromArgb(76, 194, 255),
        CanvasBg = Color.FromArgb(28, 28, 28),
        CanvasGrid = Color.FromArgb(42, 42, 42),
        TreeBg = Color.FromArgb(40, 40, 40),
        TreeImageFile = Color.FromArgb(96, 205, 255),
        ButtonBg = Color.FromArgb(55, 55, 55),
        ButtonHover = Color.FromArgb(70, 70, 70),
        ButtonBorder = Color.FromArgb(80, 80, 80),
        DeleteBg = Color.FromArgb(196, 43, 28),
        DeleteBorder = Color.FromArgb(140, 30, 20),
        Selection = Color.FromArgb(0, 120, 212),
    };

    public static readonly Theme Light = new()
    {
        Name = "ライト",
        Background = Color.FromArgb(243, 243, 243),
        Surface = Color.FromArgb(251, 251, 251),
        SurfaceLight = Color.FromArgb(255, 255, 255),
        SurfaceBorder = Color.FromArgb(210, 210, 210),
        ToolbarBg = Color.FromArgb(238, 238, 238),
        ToolbarBorder = Color.FromArgb(215, 215, 215),
        TextPrimary = Color.FromArgb(28, 28, 28),
        TextSecondary = Color.FromArgb(95, 95, 95),
        TextDisabled = Color.FromArgb(160, 160, 160),
        Accent = Color.FromArgb(0, 103, 192),
        AccentDark = Color.FromArgb(0, 120, 212),
        AccentHover = Color.FromArgb(25, 128, 220),
        CanvasBg = Color.FromArgb(230, 230, 230),
        CanvasGrid = Color.FromArgb(214, 214, 214),
        TreeBg = Color.FromArgb(248, 248, 248),
        TreeImageFile = Color.FromArgb(0, 103, 192),
        ButtonBg = Color.FromArgb(228, 228, 228),
        ButtonHover = Color.FromArgb(214, 214, 214),
        ButtonBorder = Color.FromArgb(200, 200, 200),
        DeleteBg = Color.FromArgb(196, 43, 28),
        DeleteBorder = Color.FromArgb(140, 30, 20),
        Selection = Color.FromArgb(0, 120, 212),
    };

    public static readonly Theme Midnight = new()
    {
        Name = "ミッドナイト",
        Background = Color.FromArgb(13, 17, 28),
        Surface = Color.FromArgb(22, 27, 41),
        SurfaceLight = Color.FromArgb(32, 39, 58),
        SurfaceBorder = Color.FromArgb(48, 58, 84),
        ToolbarBg = Color.FromArgb(17, 22, 35),
        ToolbarBorder = Color.FromArgb(40, 48, 70),
        TextPrimary = Color.FromArgb(226, 232, 245),
        TextSecondary = Color.FromArgb(148, 160, 185),
        TextDisabled = Color.FromArgb(90, 100, 125),
        Accent = Color.FromArgb(110, 168, 254),
        AccentDark = Color.FromArgb(52, 106, 210),
        AccentHover = Color.FromArgb(140, 186, 255),
        CanvasBg = Color.FromArgb(10, 13, 22),
        CanvasGrid = Color.FromArgb(24, 30, 46),
        TreeBg = Color.FromArgb(18, 23, 36),
        TreeImageFile = Color.FromArgb(110, 168, 254),
        ButtonBg = Color.FromArgb(34, 42, 62),
        ButtonHover = Color.FromArgb(46, 56, 82),
        ButtonBorder = Color.FromArgb(56, 68, 98),
        DeleteBg = Color.FromArgb(196, 43, 28),
        DeleteBorder = Color.FromArgb(140, 30, 20),
        Selection = Color.FromArgb(52, 106, 210),
    };

    public static readonly Theme HighContrast = new()
    {
        Name = "ハイコントラスト",
        Background = Color.Black,
        Surface = Color.FromArgb(16, 16, 16),
        SurfaceLight = Color.FromArgb(32, 32, 32),
        SurfaceBorder = Color.FromArgb(255, 255, 255),
        ToolbarBg = Color.Black,
        ToolbarBorder = Color.White,
        TextPrimary = Color.White,
        TextSecondary = Color.FromArgb(255, 255, 0),
        TextDisabled = Color.FromArgb(128, 128, 128),
        Accent = Color.FromArgb(0, 255, 255),
        AccentDark = Color.FromArgb(0, 160, 160),
        AccentHover = Color.FromArgb(128, 255, 255),
        CanvasBg = Color.Black,
        CanvasGrid = Color.FromArgb(40, 40, 40),
        TreeBg = Color.Black,
        TreeImageFile = Color.FromArgb(0, 255, 255),
        ButtonBg = Color.FromArgb(24, 24, 24),
        ButtonHover = Color.FromArgb(64, 64, 64),
        ButtonBorder = Color.White,
        DeleteBg = Color.FromArgb(255, 0, 0),
        DeleteBorder = Color.White,
        Selection = Color.FromArgb(0, 255, 255),
    };

    public static readonly Theme[] Presets = [Dark, Light, Midnight, HighContrast];

    public static Theme Current { get; private set; } = Dark;

    public static event EventHandler? Changed;

    public static void Apply(string name)
    {
        var next = Array.Find(Presets, t => t.Name == name) ?? Dark;
        if (ReferenceEquals(next, Current)) return;
        Current = next;
        Changed?.Invoke(null, EventArgs.Empty);
    }
}
