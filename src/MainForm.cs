using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Forms;

namespace MultiImageCanvas;

internal sealed partial class MainForm : Form
{
    [DllImport("user32.dll")]
    private static extern bool ReleaseCapture();
    [DllImport("user32.dll")]
    private static extern int SendMessage(IntPtr hWnd, int Msg, int wParam, int lParam);
    [DllImport("user32.dll")]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);
    [DllImport("user32.dll")]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    private const int WM_HOTKEY = 0x0312;
    private const uint MOD_ALT = 0x1;
    private const uint MOD_CONTROL = 0x2;
    private const int HotkeyToggleOverlay = 1;
    private const int HotkeyClickThrough = 2;
    private const int HotkeyOpacityDown = 3;
    private const int HotkeyOpacityUp = 4;
    private const int HotkeyCanvasNext = 5;
    private const int HotkeyCanvasPrev = 6;

    private readonly CanvasSurface _canvas = new();
    private readonly KeyMap _keyMap = new();

    // ドキュメント (キャンバスタブ)
    private readonly List<CanvasDocument> _docs = [];
    private int _activeDocIndex = -1;

    // 上部バー: ドロップダウンメニュー + キャンバスタブ
    private readonly MenuBarStrip _menuBar = new() { GripStyle = ToolStripGripStyle.Hidden, Dock = DockStyle.Top };
    private readonly ClickThroughToolStrip _docTabs = new() { GripStyle = ToolStripGripStyle.Hidden, Dock = DockStyle.Top };
    private readonly List<ToolStripMenuItem> _menuButtons = [];
    private readonly Dictionary<string, ToolStripMenuItem> _actionMenuItems = [];

    private ToolStripMenuItem? _undoMi, _redoMi, _naturalMi, _topmostMi;
    private ToolStripMenuItem? _overlayActiveMi, _clickThroughMi;
    private ToolStripMenuItem? _overlayMenuBtn;
    private ToolStripTrackBar? _opacityTrack;
    private ToolStripLabel? _opacityMenuLabel;
    private readonly ToolStripLabel _zoomText = new("100%") { Alignment = ToolStripItemAlignment.Right };
    private ToolStripButton? _zoomInBtn, _zoomOutBtn, _zoomFitBtn;
    private ToolStripButton? _paintRedBtn, _paintMarkerBtn, _paintEraserBtn, _paintClearBtn;
    private readonly Label _sessionTitleLabel = new();
    private readonly string[] _startupArgs;
    private static readonly HttpClient Http = new();

    // 右サイドバー
    private readonly Panel _rightPanel = new();
    private readonly Panel _sidebarContent = new();
    private ThumbnailView? _thumbs;
    private LayerPanel? _layers;
    private Panel? _treeArea;
    private readonly Panel _overlayFrame = new();
    private readonly GamingButton _sidebarOverlayBtn = new();
    private Button? _viewTreeBtn, _viewThumbsBtn, _viewLayersBtn;
    private string _sidebarView = "tree";

    // 選択画像の操作メニュー (画像横にフローティング表示)
    private readonly Panel _itemPanel = new();

    // オーバーレイ設定パネル (歯車で開く。ファイル選択画面に重ねて表示)
    private readonly Panel _overlaySettingsPanel = new();
    private CheckBox? _ovlClickThroughCheck;
    private Label? _ovlOpacityLabel;
    private TrackBar? _ovlOpacitySlider;
    private bool _syncingOverlayPanel;

    private OverlayForm? _overlayForm;
    private float _overlayOpacity = 1.0f;
    private bool _overlayClickThrough;

    private bool _uiHidden;
    private int _sidebarWidth = 296;
    private string? _lastFolderPath;

    private bool _restoreTabsSetting = true;
    private int _autosaveSeconds = 30;
    private string _language = Loc.Japanese;
    private string _overlayAnimation = "ブロック";

    // タブのドラッグ&ドロップ並べ替え
    private int _tabDragSource = -1;
    private int _tabDragStartX;
    private bool _tabDragActive;
    private bool _suppressTabClick;
    private System.Windows.Forms.Timer? _tabSlideTimer;
    private readonly System.Windows.Forms.Timer _autosaveTimer = new() { Interval = 60_000 };
    private SessionData? _pendingSession;
    private AppSettingsData _appSettings = new();
    private string? _sessionFilePath;
    private bool _closingConfirmed;

    // ビュアーモード (エクスプローラーから画像を開いた時の閲覧専用UI)
    private bool _viewerMode;
    private readonly ClickThroughToolStrip _viewerBar = new() { GripStyle = ToolStripGripStyle.Hidden, Dock = DockStyle.Top };
    private ToolStripLabel? _viewerTitle;

    // ビュアーのGIFアニメ操作UI
    private FlowLayoutPanel? _gifPanel;
    private Button? _gifPrevBtn;
    private Button? _gifPlayBtn;
    private Button? _gifNextBtn;
    private TrackBar? _gifTrack;
    private ComboBox? _gifSpeedCombo;
    private Label? _gifFrameLabel;
    private bool _syncingGifUi;
    private readonly Panel _viewerNavPanel = new();
    private FlowLayoutPanel? _navFlow;
    private FlowLayoutPanel? _pageGroup;
    private Label? _viewerPageLabel;
    private Button? _viewerPrevBtn, _viewerNextBtn, _viewerFullscreenBtn;
    private readonly System.Windows.Forms.Timer _viewerChromeTimer = new() { Interval = 80 };
    private DateTime _lastViewerActivity = DateTime.UtcNow;
    private Point _lastCursorScreenPos = Point.Empty;
    private float _viewerChromeOpacity;
    private List<string> _viewerFiles = [];
    private int _viewerFileIndex = -1;
    private string? _viewerCurrentPath;
    private bool _viewerFullscreen;
    private Rectangle _viewerRestoreBounds;
    private FormWindowState _viewerRestoreWindowState;
    private Padding _viewerRestorePadding;

    private const int CornerRadius = 14;

    public MainForm(string[]? startupArgs = null)
    {
        _startupArgs = startupArgs ?? [];
        // テーマはコントロール生成前に適用する
        _pendingSession = SessionStore.Load();
        var savedSettings = AppSettingsStore.Load();
        _appSettings = savedSettings ?? AppSettingsStore.LoadLegacySession() ?? new AppSettingsData();
        if (savedSettings == null) AppSettingsStore.Save(_appSettings);
        Loc.Apply(_appSettings.Language);
        Theme.Apply(_appSettings.Theme);
        _keyMap.Load(_appSettings.KeyBindings);

        Text = "Multi Image Canvas";
        Width = 1450;
        Height = 900;
        MinimumSize = new Size(1050, 650);
        KeyPreview = true;
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.None;
        Padding = new Padding(12, 0, 12, 12);
        BackColor = Theme.Current.Background;
        ForeColor = Theme.Current.TextPrimary;

        try { Font = new Font("Segoe UI Variable Display", 9f); }
        catch { try { Font = new Font("Segoe UI", 9f); } catch { /* フォールバック */ } }

        _viewerMode = DetectViewerMode(_startupArgs);

        BuildMenuBar();
        BuildMainLayout();
        WireCanvasEvents();
        WireDragDrop();
        BuildInitialTree();
        if (_viewerMode) EnterViewerMode();
        else RestoreSession();
        ApplyLanguageToControls();

        Theme.Changed += (_, _) => ApplyThemeToControls();

        _autosaveTimer.Tick += (_, _) => SaveSession();
        if (!_viewerMode) _autosaveTimer.Start();

        Shown += (_, _) =>
        {
            ApplyTreeTheme();
            BeginInvoke(new Action(StartLoadQuickAccessExtras));
            BeginInvoke(new Action(() => _ = OpenStartupInputsAsync()));

            // UI自動テスト用フック: 起動直後に設定画面を開く
            if (Environment.GetEnvironmentVariable("MIC_OPEN_SETTINGS") == "1")
            {
                BeginInvoke(new Action(OpenSettings));
            }
        };

        FormClosing += (_, e) =>
        {
            // ビュアーモードはセッションに一切触れずに閉じる (確認ダイアログも出さない)
            if (_viewerMode)
            {
                ToggleOverlayMode(false);
                return;
            }

            if (!_closingConfirmed)
            {
                var decision = ShowSessionDecision(Loc.T("アプリを閉じる"),
                    Loc.T("アプリを閉じますか？\n外部保存していないセッションは復元できません。"),
                    Loc.T("閉じる"));
                if (decision == SessionDecision.Cancel)
                {
                    e.Cancel = true;
                    return;
                }
                if (decision == SessionDecision.Save && !SaveSessionToFile(showDone: false))
                {
                    e.Cancel = true;
                    return;
                }
                _closingConfirmed = true;
            }
            SaveSession();
            ToggleOverlayMode(false);
        };
    }

    // ===== セッション =====

    private void RestoreSession()
    {
        var s = _pendingSession;
        _pendingSession = null;
        ApplyStoredSettings();

        if (s != null)
        {
            if (s.WindowBounds is { Length: 4 })
            {
                var b = new Rectangle(s.WindowBounds[0], s.WindowBounds[1], s.WindowBounds[2], s.WindowBounds[3]);
                if (Screen.AllScreens.Any(sc => sc.WorkingArea.IntersectsWith(b)))
                {
                    StartPosition = FormStartPosition.Manual;
                    Bounds = b;
                }
            }
            if (s.Maximized) WindowState = FormWindowState.Maximized;

            _canvas.InsertNaturalSize = s.InsertNaturalSize;
            _overlayOpacity = Math.Clamp(s.OverlayOpacity, 0.2f, 1f);
            _overlayClickThrough = s.OverlayClickThrough;
            _sidebarView = s.SidebarView;

            // 前回のタブを復元
            if (_restoreTabsSetting)
            {
                for (int i = 0; i < s.Tabs.Count; i++)
                {
                    try
                    {
                        var doc = LayoutSerializer.FromDto(s.Tabs[i], i < s.TabFilePaths.Count ? s.TabFilePaths[i] : null);
                        AddDocument(doc, select: false);
                    }
                    catch
                    {
                        // 個別タブの復元失敗は無視
                    }
                }
                if (_docs.Count > 0)
                {
                    SelectDocument(Math.Clamp(s.ActiveTab, 0, _docs.Count - 1));
                }
            }
        }

        if (_docs.Count == 0)
        {
            AddDocument(new CanvasDocument(), select: true);
        }

        // 復元した設定をUIコントロールへ反映
        if (_naturalMi != null) _naturalMi.Checked = _canvas.InsertNaturalSize;
        SetOverlayOpacity(_overlayOpacity);
        if (_clickThroughMi != null) _clickThroughMi.Checked = _overlayClickThrough;

        UpdateMenuShortcutTexts();
        SyncMenuState();
        SetSidebarView(_sidebarView);
    }

    private void SaveSession()
    {
        SessionStore.Save(CreateSessionData());
    }

    private SessionData CreateSessionData() => new()
    {
        WindowBounds = WindowState == FormWindowState.Normal
            ? [Bounds.X, Bounds.Y, Bounds.Width, Bounds.Height]
            : [RestoreBounds.X, RestoreBounds.Y, RestoreBounds.Width, RestoreBounds.Height],
        Maximized = WindowState == FormWindowState.Maximized,
        SidebarView = _sidebarView,
        InsertNaturalSize = _canvas.InsertNaturalSize,
        OverlayOpacity = _overlayOpacity,
        OverlayClickThrough = _overlayClickThrough,
        BgOpacity = _canvas.BgOpacity,
        ActiveTab = _activeDocIndex,
        Tabs = _docs.Select(LayoutSerializer.ToDto).ToList(),
        TabFilePaths = _docs.Select(d => d.FilePath).ToList(),
    };

    private void ApplyStoredSettings()
    {
        _restoreTabsSetting = _appSettings.RestoreTabs;
        _autosaveSeconds = Math.Clamp(_appSettings.AutosaveSeconds, 10, 600);
        _autosaveTimer.Interval = _autosaveSeconds * 1000;
        _canvas.SnapEnabled = _appSettings.SnapEnabled;
        _canvas.GridSnapEnabled = _appSettings.GridSnap;
        _canvas.ImageImportScale = Math.Clamp(_appSettings.ImageImportScalePercent, 25, 200) / 100f;
        _language = Loc.Normalize(_appSettings.Language);
        _overlayAnimation = _appSettings.OverlayAnimation;
    }

    private void SaveSettings()
    {
        _appSettings = new AppSettingsData
        {
            Theme = Theme.Current.Name,
            KeyBindings = _keyMap.Save(),
            RestoreTabs = _restoreTabsSetting,
            AutosaveSeconds = _autosaveSeconds,
            SnapEnabled = _canvas.SnapEnabled,
            GridSnap = _canvas.GridSnapEnabled,
            ImageImportScalePercent = (int)Math.Round(_canvas.ImageImportScale * 100),
            Language = Loc.Normalize(_language),
            OverlayAnimation = _overlayAnimation,
        };
        AppSettingsStore.Save(_appSettings);
    }

    // ===== ドキュメント (タブ) 管理 =====

    private CanvasDocument? ActiveDoc => _activeDocIndex >= 0 && _activeDocIndex < _docs.Count ? _docs[_activeDocIndex] : null;

    private void ClearDocuments()
    {
        _canvas.Document = null;
        _layers?.AttachDocument(null);
        foreach (var doc in _docs) doc.Dispose();
        _docs.Clear();
        _activeDocIndex = -1;
        RebuildDocTabs();
        SyncMenuState();
        UpdateItemPanel();
    }

    private void AddDocument(CanvasDocument doc, bool select, bool save = true)
    {
        _docs.Add(doc);
        doc.Changed += (_, _) => { RebuildDocTabs(); SyncMenuState(); UpdateItemPanel(); };
        doc.Undo.StateChanged += (_, _) => SyncMenuState();
        RebuildDocTabs();
        if (select) SelectDocument(_docs.Count - 1);
        if (save && IsHandleCreated) SaveSession();
    }

    private void SelectDocument(int index)
    {
        if (index < 0 || index >= _docs.Count) return;
        _activeDocIndex = index;
        var doc = _docs[index];
        _canvas.Document = doc;
        _layers?.AttachDocument(doc);
        RebuildDocTabs();
        SyncMenuState();
        UpdateZoomText();
        UpdateItemPanel();
    }

    private void SelectNextCanvas(int delta)
    {
        if (_docs.Count <= 1) return;
        SelectDocument((_activeDocIndex + delta + _docs.Count) % _docs.Count);
    }

    private void CloseDocument(int index)
    {
        if (index < 0 || index >= _docs.Count) return;
        var doc = _docs[index];

        if (_docs.Count == 1)
        {
            if (MessageBox.Show(this,
                    Loc.T("最後のキャンバスです。内容をすべて削除して新規状態にしますか？\n外部保存していない内容は復元できません。"),
                    Loc.T("キャンバスを閉じる"), MessageBoxButtons.YesNoCancel, MessageBoxIcon.Warning) != DialogResult.Yes) return;
            _canvas.ClearAll();
            SaveSession();
            return;
        }

        if (MessageBox.Show(this,
                string.Format(Loc.T("「{0}」を閉じますか？\n外部保存していないキャンバスは復元できません。"), doc.Name),
                Loc.T("キャンバスを閉じる"), MessageBoxButtons.YesNoCancel, MessageBoxIcon.Warning) != DialogResult.Yes) return;

        _docs.RemoveAt(index);
        if (ReferenceEquals(_canvas.Document, doc)) _canvas.Document = null;
        doc.Dispose();

        if (_activeDocIndex >= _docs.Count) _activeDocIndex = _docs.Count - 1;
        SelectDocument(Math.Max(0, Math.Min(_activeDocIndex, _docs.Count - 1)));
        if (IsHandleCreated) SaveSession();
    }

    private void RebuildDocTabs()
    {
        _docTabs.SuspendLayout();
        _docTabs.Items.Clear();

        for (int i = 0; i < _docs.Count; i++)
        {
            var doc = _docs[i];
            int captured = i;
            bool isActive = i == _activeDocIndex;

            var text = isActive
                ? $" {doc.Name} "
                : $" {doc.Name} ";
            var btn = new SlidingToolStripButton(text)
            {
                Checked = isActive,
                Margin = new Padding(2, 2, 0, 2),
                Tag = captured,
                ToolTipText = Loc.T("ダブルクリックで名前変更 / ドラッグで並べ替え"),
            };
            btn.DoubleClickEnabled = true;
            btn.DoubleClick += (_, _) =>
            {
                if (_tabDragActive) return;
                SelectDocument(captured);
                RenameActiveDoc();
            };

            // ドラッグ&ドロップで並べ替え (6px以上動いたらドラッグ扱い)
            btn.MouseDown += (_, e) =>
            {
                if (e.Button != MouseButtons.Left) return;
                BeginTabDrag(captured);
            };
            btn.MouseMove += (_, _) =>
            {
                UpdateTabDrag();
            };
            btn.MouseUp += (_, e) =>
            {
                if (e.Button != MouseButtons.Left) return;
                CompleteTabDrag();
            };

            if (!isActive)
            {
                btn.Click += (_, _) =>
                {
                    if (_suppressTabClick) { _suppressTabClick = false; return; }
                    SelectDocument(captured);
                };
            }
            _docTabs.Items.Add(btn);
        }

        var addBtn = new ToolStripButton(" ＋ ") { Margin = new Padding(6, 2, 2, 2), ToolTipText = Loc.T("新規キャンバス (Ctrl+T)") };
        addBtn.Click += (_, _) => AddDocument(new CanvasDocument(), select: true);
        _docTabs.Items.Add(addBtn);

        var closeBtn = new ToolStripButton(" " + Loc.T("キャンバスを閉じる") + " ") { Margin = new Padding(2, 2, 2, 2), ToolTipText = Loc.T("現在のキャンバスを閉じる (Ctrl+W)") };
        closeBtn.Click += (_, _) => CloseDocument(_activeDocIndex);
        _docTabs.Items.Add(closeBtn);

        // 右端: ズーム操作 (Alignment.Rightは先に追加したものが右端に来る)
        _zoomInBtn ??= MakeDocTabButton(" ＋ ", Loc.T("ズームイン"), (_, _) => _canvas.SetZoom(_canvas.Zoom * 1.15f));
        _zoomOutBtn ??= MakeDocTabButton(" － ", Loc.T("ズームアウト"), (_, _) => _canvas.SetZoom(_canvas.Zoom / 1.15f));
        _zoomFitBtn ??= MakeDocTabButton(" 🗺 ", Loc.T("全体表示"), (_, _) => _canvas.ZoomFitAll());
        _paintRedBtn ??= MakePaintButton(PaintIconKind.Pen, Loc.T("赤ペン"), (_, _) => TogglePaintTool(PaintTool.RedPen));
        _paintMarkerBtn ??= MakePaintButton(PaintIconKind.Marker, Loc.T("黄色マーカー"), (_, _) => TogglePaintTool(PaintTool.YellowMarker));
        _paintEraserBtn ??= MakePaintButton(PaintIconKind.Eraser, Loc.T("消しゴム"), (_, _) => TogglePaintTool(PaintTool.Eraser));
        _paintClearBtn ??= MakePaintButton(PaintIconKind.Mop, Loc.T("全消し"), (_, _) => _canvas.ClearPaintStrokes());
        _docTabs.Items.Add(_zoomText);
        _docTabs.Items.Add(_zoomInBtn);
        _docTabs.Items.Add(_zoomOutBtn);
        _docTabs.Items.Add(_zoomFitBtn);
        _docTabs.Items.Add(new ToolStripSeparator { Alignment = ToolStripItemAlignment.Right });
        _docTabs.Items.Add(_paintClearBtn);
        _docTabs.Items.Add(_paintEraserBtn);
        _docTabs.Items.Add(_paintMarkerBtn);
        _docTabs.Items.Add(_paintRedBtn);
        UpdatePaintButtons();

        _docTabs.ResumeLayout();
    }

    private void TogglePaintTool(PaintTool tool)
    {
        _canvas.PaintTool = _canvas.PaintTool == tool ? PaintTool.None : tool;
        UpdatePaintButtons();
    }

    private void UpdatePaintButtons()
    {
        if (_paintRedBtn != null) _paintRedBtn.Checked = _canvas.PaintTool == PaintTool.RedPen;
        if (_paintMarkerBtn != null) _paintMarkerBtn.Checked = _canvas.PaintTool == PaintTool.YellowMarker;
        if (_paintEraserBtn != null) _paintEraserBtn.Checked = _canvas.PaintTool == PaintTool.Eraser;
    }

    private void BeginTabDrag(int index)
    {
        _tabDragSource = index;
        _tabDragStartX = Cursor.Position.X;
        _tabDragActive = false;
        _docTabs.Capture = true;
    }

    private void UpdateTabDrag()
    {
        if (_tabDragSource < 0 || (MouseButtons & MouseButtons.Left) == 0) return;
        if (Math.Abs(Cursor.Position.X - _tabDragStartX) <= 6) return;

        _tabDragActive = true;
        int target = GetTabIndexAtCursor();
        if (target < 0 || target == _tabDragSource) return;

        MoveDocument(_tabDragSource, target);
        _tabDragSource = target;
        _docTabs.Capture = true;
    }

    private void CompleteTabDrag()
    {
        if (_tabDragSource < 0) return;

        var source = _tabDragSource;
        _docTabs.Capture = false;

        if (_tabDragActive)
        {
            _suppressTabClick = true;
            BeginInvoke(new Action(() => _suppressTabClick = false));
        }
        else if (source == _activeDocIndex) { }
        else
        {
            SelectDocument(source);
        }

        _tabDragSource = -1;
        _tabDragActive = false;
    }

    private static ToolStripButton MakeDocTabButton(string text, string tooltip, EventHandler onClick)
    {
        var b = new ToolStripButton(text) { Alignment = ToolStripItemAlignment.Right, Margin = new Padding(2, 2, 2, 2), ToolTipText = tooltip };
        b.Click += onClick;
        return b;
    }

    private ToolStripButton MakePaintButton(PaintIconKind icon, string tooltip, EventHandler onClick)
    {
        var b = MakeDocTabButton("", tooltip, onClick);
        b.AutoSize = false;
        b.Width = 30;
        b.Height = 26;
        b.DisplayStyle = ToolStripItemDisplayStyle.Image;
        b.ImageScaling = ToolStripItemImageScaling.None;
        b.Image = CreatePaintIcon(icon);
        b.Tag = icon;
        return b;
    }

    private enum PaintIconKind
    {
        Pen,
        Marker,
        Eraser,
        Mop,
    }

    private Image CreatePaintIcon(PaintIconKind icon)
    {
        var bmp = new Bitmap(20, 20);
        using var g = Graphics.FromImage(bmp);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(Color.Transparent);

        var fg = Theme.Current.TextPrimary;
        using var fgPen = new Pen(fg, 1.8f) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round };
        using var redPen = new Pen(Color.FromArgb(230, 32, 32), 2.3f) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        using var yellowPen = new Pen(Color.FromArgb(245, 192, 35), 4.0f) { StartCap = LineCap.Square, EndCap = LineCap.Square };

        switch (icon)
        {
            case PaintIconKind.Pen:
                g.DrawLine(redPen, 5, 15, 15, 5);
                using (var brush = new SolidBrush(Color.FromArgb(230, 32, 32)))
                {
                    g.FillPolygon(brush, [new PointF(14, 4), new PointF(16, 6), new PointF(12.8f, 7.2f)]);
                }
                g.DrawLine(fgPen, 4, 16, 7, 15);
                break;
            case PaintIconKind.Marker:
                g.DrawLine(yellowPen, 5, 14, 15, 4);
                g.DrawLine(fgPen, 4, 15, 15, 4);
                g.DrawLine(fgPen, 10, 5, 15, 10);
                g.DrawLine(fgPen, 4, 15, 8, 16);
                break;
            case PaintIconKind.Eraser:
                using (var brush = new SolidBrush(Color.FromArgb(80, fg)))
                {
                    var body = new[] { new PointF(4, 12), new PointF(10, 6), new PointF(16, 12), new PointF(10, 18) };
                    g.FillPolygon(brush, body);
                    g.DrawPolygon(fgPen, body);
                }
                g.DrawLine(fgPen, 7, 9, 13, 15);
                break;
            case PaintIconKind.Mop:
                g.DrawLine(fgPen, 12, 2, 8, 13);
                using (var head = new Pen(fg, 2.2f) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                {
                    g.DrawLine(head, 5, 14, 15, 14);
                    g.DrawLine(head, 6, 16, 14, 16);
                    g.DrawLine(head, 8, 18, 12, 18);
                }
                break;
        }

        return bmp;
    }

    private void RefreshPaintButtonImages()
    {
        foreach (var btn in new[] { _paintRedBtn, _paintMarkerBtn, _paintEraserBtn, _paintClearBtn })
        {
            if (btn?.Tag is not PaintIconKind icon) continue;
            var old = btn.Image;
            btn.Image = CreatePaintIcon(icon);
            old?.Dispose();
        }
    }

    // カーソル位置にあるタブのインデックスを返す (タブ外なら最寄り)
    private int GetTabIndexAtCursor()
    {
        var x = _docTabs.PointToClient(Cursor.Position).X;
        int best = -1;
        float bestDist = float.MaxValue;
        foreach (ToolStripItem item in _docTabs.Items)
        {
            if (item.Tag is not int index) continue;
            if (x >= item.Bounds.Left && x <= item.Bounds.Right) return index;
            float center = item.Bounds.Left + item.Bounds.Width / 2f;
            float dist = Math.Abs(x - center);
            if (dist < bestDist) { bestDist = dist; best = index; }
        }
        return best;
    }

    // タブ(ドキュメント)の並び順を変更。ショートカット切替の順序にもそのまま反映される
    private void MoveDocument(int from, int to)
    {
        if (from < 0 || from >= _docs.Count || to < 0 || to >= _docs.Count || from == to) return;
        var active = ActiveDoc;
        var oldCenters = GetTabCentersByDoc();
        var doc = _docs[from];
        _docs.RemoveAt(from);
        _docs.Insert(to, doc);
        _activeDocIndex = active != null ? _docs.IndexOf(active) : 0;
        RebuildDocTabs();
        StartTabSlideAnimation(oldCenters, doc);
        if (IsHandleCreated) SaveSession();
    }

    private Dictionary<CanvasDocument, int> GetTabCentersByDoc()
    {
        _docTabs.PerformLayout();
        var centers = new Dictionary<CanvasDocument, int>();
        foreach (ToolStripItem item in _docTabs.Items)
        {
            if (item.Tag is int index && index >= 0 && index < _docs.Count)
            {
                centers[_docs[index]] = item.Bounds.Left + item.Bounds.Width / 2;
            }
        }
        return centers;
    }

    private void StartTabSlideAnimation(Dictionary<CanvasDocument, int> oldCenters, CanvasDocument draggedDoc)
    {
        _docTabs.PerformLayout();
        var any = false;
        foreach (ToolStripItem item in _docTabs.Items)
        {
            if (item is not SlidingToolStripButton btn || item.Tag is not int index || index < 0 || index >= _docs.Count) continue;
            var doc = _docs[index];
            if (ReferenceEquals(doc, draggedDoc) || !oldCenters.TryGetValue(doc, out var oldCenter)) continue;
            var newCenter = item.Bounds.Left + item.Bounds.Width / 2;
            btn.RenderOffsetX = oldCenter - newCenter;
            any |= btn.RenderOffsetX != 0;
        }
        if (!any) return;

        _tabSlideTimer ??= new System.Windows.Forms.Timer { Interval = 15 };
        _tabSlideTimer.Tick -= TabSlideTimer_Tick;
        _tabSlideTimer.Tick += TabSlideTimer_Tick;
        _tabSlideTimer.Start();
        _docTabs.Invalidate();
    }

    private void TabSlideTimer_Tick(object? sender, EventArgs e)
    {
        var any = false;
        foreach (ToolStripItem item in _docTabs.Items)
        {
            if (item is not SlidingToolStripButton btn || btn.RenderOffsetX == 0) continue;
            btn.RenderOffsetX = Math.Abs(btn.RenderOffsetX) <= 1 ? 0 : (int)Math.Round(btn.RenderOffsetX * 0.65);
            any |= btn.RenderOffsetX != 0;
        }
        _docTabs.Invalidate();
        if (!any) _tabSlideTimer?.Stop();
    }

    private void RenameActiveDoc()
    {
        var doc = ActiveDoc;
        if (doc == null) return;

        using var dlg = new TextInputForm(Loc.T("キャンバス名の変更"), Loc.T("新しいキャンバス名:"), doc.Name);
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        if (string.IsNullOrWhiteSpace(dlg.Value) || dlg.Value == doc.Name) return;

        doc.Name = dlg.Value;
        doc.NotifyChanged();
        RebuildDocTabs();
    }

    // ===== メニューバー =====

    private void BuildMenuBar()
    {
        foreach (var strip in new ToolStrip[] { _menuBar, _docTabs })
        {
            strip.Renderer = new ThemedToolStripRenderer();
            strip.BackColor = Theme.Current.ToolbarBg;
            strip.ForeColor = Theme.Current.TextPrimary;
            strip.AutoSize = false;
        }
        _menuBar.Padding = new Padding(10, 4, 10, 2);
        _menuBar.Height = 40;
        _docTabs.Padding = new Padding(10, 2, 10, 2);
        _docTabs.Height = 32;

        WireTitleBarDrag(_menuBar);
        WireTitleBarDrag(_docTabs);
        _docTabs.MouseDown += (_, e) =>
        {
            if (e.Button != MouseButtons.Left) return;
            if (_docTabs.GetItemAt(e.Location)?.Tag is int index) BeginTabDrag(index);
        };
        _docTabs.MouseMove += (_, _) => UpdateTabDrag();
        _docTabs.MouseUp += (_, e) => { if (e.Button == MouseButtons.Left) CompleteTabDrag(); };

        RebuildMenuBarItems();

        // メニューのホバー切替は MenuBarStrip (MenuAutoExpand) が担う
    }

    private void RebuildMenuBarItems()
    {
        _menuBar.Items.Clear();
        _menuButtons.Clear();
        _actionMenuItems.Clear();
        _undoMi = null;
        _redoMi = null;
        _naturalMi = null;
        _topmostMi = null;
        _overlayActiveMi = null;
        _clickThroughMi = null;
        _overlayMenuBtn = null;
        _opacityTrack = null;
        _opacityMenuLabel = null;

        _menuBar.Items.Add(BuildFileMenu());
        _menuBar.Items.Add(BuildEditMenu());
        _menuBar.Items.Add(BuildViewMenu());
        _overlayMenuBtn = BuildOverlayMenu();
        _menuBar.Items.Add(_overlayMenuBtn);
        _menuBar.Items.Add(BuildHelpMenu());

        AddCaptionButtons(_menuBar);
        if (_naturalMi != null) _naturalMi.Checked = _canvas.InsertNaturalSize;
        if (_topmostMi != null) _topmostMi.Checked = TopMost;
        if (_overlayActiveMi != null) _overlayActiveMi.Checked = _overlayForm != null;
        if (_clickThroughMi != null) _clickThroughMi.Checked = _overlayClickThrough;
        UpdateMenuShortcutTexts();
        SyncMenuState();
    }

    private ToolStripMenuItem MakeMenuButton(string text)
    {
        var btn = new ToolStripMenuItem($" {text} ")
        {
            Margin = new Padding(0, 2, 0, 2),
            Padding = new Padding(10, 0, 10, 0),
        };
        ConfigureDropDown(btn.DropDown);
        _menuButtons.Add(btn);
        return btn;
    }

    private static void ConfigureDropDown(ToolStripDropDown dropDown)
    {
        dropDown.Renderer = new ThemedToolStripRenderer();
        dropDown.BackColor = Theme.Current.Surface;
        dropDown.ShowItemToolTips = true;
        dropDown.Padding = Padding.Empty;
        dropDown.Margin = Padding.Empty;
        dropDown.ItemAdded += (_, e) => { if (e.Item != null) NormalizeDropDownItem(e.Item); };
        RoundDropDownCorners(dropDown);
    }

    private static void NormalizeDropDownItem(ToolStripItem item)
    {
        item.Margin = Padding.Empty;
        if (item is ToolStripMenuItem menuItem)
        {
            menuItem.Padding = new Padding(10, 5, 12, 5);
        }
        else if (item is ToolStripSeparator separator)
        {
            separator.AutoSize = false;
            separator.Height = 7;
            separator.Padding = Padding.Empty;
        }
        else if (item is ToolStripLabel label)
        {
            label.Padding = new Padding(10, 5, 12, 2);
        }
        else if (item is ToolStripControlHost host)
        {
            host.Padding = new Padding(10, 0, 12, 8);
        }
    }

    private void BuildSessionTitleLabel()
    {
        _sessionTitleLabel.AutoSize = false;
        _sessionTitleLabel.Height = 28;
        _sessionTitleLabel.Width = 360;
        _sessionTitleLabel.TextAlign = ContentAlignment.MiddleCenter;
        _sessionTitleLabel.Font = new Font(Font.FontFamily, 9f, FontStyle.Bold);
        _sessionTitleLabel.BackColor = Theme.Current.ToolbarBg;
        _sessionTitleLabel.ForeColor = Theme.Current.TextSecondary;
        _sessionTitleLabel.MouseDown += (_, e) =>
        {
            if (e.Button != MouseButtons.Left) return;
            if (TryBeginTopResize(_sessionTitleLabel, e.Location)) return;
            ReleaseCapture();
            SendMessage(Handle, 0xA1, 0x2, 0);
        };
        _sessionTitleLabel.MouseMove += (_, e) => _sessionTitleLabel.Cursor = GetTitleBarCursor(_sessionTitleLabel, e.Location);
        _sessionTitleLabel.MouseLeave += (_, _) => _sessionTitleLabel.Cursor = Cursors.Default;
        Controls.Add(_sessionTitleLabel);
        UpdateSessionTitle();
    }

    // ===== ビュアーモード =====

    // 起動引数が「存在する画像ファイルのみ」の場合はビュアーとして起動する
    // (.mics/.micl やフォルダ、URLが含まれる場合は通常の編集画面)
    private static bool DetectViewerMode(string[] args)
    {
        var inputs = args.Select(a => a.Trim().Trim('"')).Where(a => !string.IsNullOrWhiteSpace(a)).ToList();
        if (inputs.Count == 0) return false;
        return inputs.All(a => File.Exists(a) && ImageDecoder.IsSupported(a));
    }

    private async Task OpenStartupInputsAsync()
    {
        if (_viewerMode)
        {
            var first = _startupArgs.Select(a => a.Trim().Trim('"')).FirstOrDefault(File.Exists);
            if (first == null) return;
            InitViewerFileList(first);
            OpenViewerFile(first);
            ShowViewerChrome();
            return;
        }

        await OpenInputsAsync(_startupArgs);
    }

    private void InitViewerFileList(string path)
    {
        _viewerCurrentPath = path;
        _viewerFiles = [];
        _viewerFileIndex = -1;

        var dir = Path.GetDirectoryName(path);
        if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir))
        {
            _viewerFiles.Add(path);
            _viewerFileIndex = 0;
            return;
        }

        try
        {
            _viewerFiles = Directory.EnumerateFiles(dir)
                .Where(ImageDecoder.IsSupported)
                .OrderBy(Path.GetFileName, StringComparer.CurrentCultureIgnoreCase)
                .ToList();
            _viewerFileIndex = _viewerFiles.FindIndex(f => string.Equals(f, path, StringComparison.OrdinalIgnoreCase));
            if (_viewerFileIndex < 0)
            {
                _viewerFiles.Insert(0, path);
                _viewerFileIndex = 0;
            }
        }
        catch
        {
            _viewerFiles = [path];
            _viewerFileIndex = 0;
        }
    }

    private void OpenViewerFile(string path)
    {
        if (!_viewerMode) return;

        _canvas.ShutdownViewerAnimation();
        if (ActiveDoc is { } oldDoc)
        {
            _canvas.Document = null;
            _docs.Remove(oldDoc);
            oldDoc.Dispose();
            _activeDocIndex = -1;
        }

        var doc = new CanvasDocument(Path.GetFileNameWithoutExtension(path));
        AddDocument(doc, select: true, save: false);
        _canvas.AddImage(path);
        _canvas.Select(null);
        _canvas.SetViewerBaseline();
        _canvas.InitViewerAnimation();
        SetGifControlsVisible(_canvas.ViewerFrameCount > 1);
        SyncGifControls();

        _viewerCurrentPath = path;
        if (_viewerTitle != null) _viewerTitle.Text = Path.GetFileName(path);
        Text = Path.GetFileName(path) + " - Multi Image Canvas";
        UpdateViewerNavState();
    }

    private void NavigateViewerImage(int delta)
    {
        if (!_viewerMode || _viewerFiles.Count == 0) return;
        _viewerFileIndex = (_viewerFileIndex + delta + _viewerFiles.Count) % _viewerFiles.Count;
        OpenViewerFile(_viewerFiles[_viewerFileIndex]);
        ShowViewerChrome();
    }

    private void SetGifControlsVisible(bool visible)
    {
        if (_gifPanel != null) _gifPanel.Visible = visible;
        if (_gifSpeedCombo != null) _gifSpeedCombo.Visible = visible;
        UpdateViewerNavPanelBounds(); // 要素の増減に合わせて下部バー幅を詰め直す
    }

    // 再生状態・現在コマをビュアーバーのUIに反映する
    private void SyncGifControls()
    {
        if (_gifPlayBtn == null || _gifTrack == null || _gifSpeedCombo == null || _gifFrameLabel == null) return;
        int count = _canvas.ViewerFrameCount;
        if (count <= 1) return;

        _syncingGifUi = true;
        try
        {
            _gifPlayBtn.Text = _canvas.ViewerAnimationPlaying ? "⏸" : "▶";
            if (_gifTrack.Maximum != count - 1) _gifTrack.Maximum = count - 1;
            var frame = Math.Clamp(_canvas.ViewerCurrentFrame, 0, count - 1);
            if (_gifTrack.Value != frame) _gifTrack.Value = frame;
            var speedText = $"{_canvas.ViewerPlaybackSpeed:0.##}x";
            if (!_gifSpeedCombo.DroppedDown && !_gifSpeedCombo.Focused && !string.Equals(_gifSpeedCombo.Text, speedText, StringComparison.Ordinal))
            {
                _gifSpeedCombo.SelectedItem = speedText;
            }
            _gifFrameLabel.Text = $"{frame + 1} / {count}";
        }
        finally
        {
            _syncingGifUi = false;
        }
    }

    // 画像表示の妨げになるUIをすべて隠し、上部バーのみのビュアーにする
    private void EnterViewerMode()
    {
        _canvas.ReadOnlyView = true;
        _canvas.InsertNaturalSize = true; // 原寸で読み込み、全体表示でフィットさせる

        MinimumSize = new Size(200, 200); // ビュアーは小さく畳めるように

        _menuBar.Visible = false;
        _sessionTitleLabel.Visible = false;
        _docTabs.Visible = false;
        _rightPanel.Visible = false;
        _overlayFrame.Visible = false;
        _viewerBar.Visible = true;
        _viewerNavPanel.Visible = false;
        _viewerChromeTimer.Start();

        var first = _startupArgs.Select(a => a.Trim().Trim('"')).FirstOrDefault(File.Exists);
        if (first != null)
        {
            if (_viewerTitle != null) _viewerTitle.Text = Path.GetFileName(first);
            Text = Path.GetFileName(first) + " - Multi Image Canvas";
        }
    }

    private void BuildViewerBar()
    {
        _viewerBar.Renderer = new ThemedToolStripRenderer();
        _viewerBar.BackColor = Theme.Current.ToolbarBg;
        _viewerBar.ForeColor = Theme.Current.TextPrimary;
        _viewerBar.AutoSize = false;
        _viewerBar.Padding = new Padding(10, 4, 10, 2);
        _viewerBar.Height = 40;
        _viewerBar.Visible = false;
        WireTitleBarDrag(_viewerBar);

        var editBtn = new ToolStripButton(" 🖊 " + Loc.T("キャンバスにて編集") + " ")
        {
            Margin = new Padding(2, 2, 2, 2),
            ToolTipText = Loc.T("通常の編集画面に切り替え、この画像を新しいキャンバスに配置した状態にします。"),
        };
        editBtn.Click += (_, _) => SwitchToEditorFromViewer();
        _viewerBar.Items.Add(editBtn);

        _viewerTitle = new ToolStripLabel("") { ForeColor = Theme.Current.TextSecondary, Margin = new Padding(12, 0, 0, 0) };
        _viewerTitle.MouseDown += (_, e) =>
        {
            if (e.Button == MouseButtons.Left) BeginWindowMove();
        };
        _viewerBar.Items.Add(_viewerTitle);

        AddCaptionButtons(_viewerBar);
    }

    // Windows標準風のキャプションボタン (右端から 閉じる/最大化/最小化 の順)
    private readonly List<CaptionToolButton> _maximizeButtons = [];

    private void AddCaptionButtons(ToolStrip strip)
    {
        var closeBtn = new CaptionToolButton(CaptionKind.Close) { ToolTipText = Loc.T("閉じる") };
        var maxBtn = new CaptionToolButton(CaptionKind.Maximize) { ToolTipText = Loc.T("最大化") };
        var minBtn = new CaptionToolButton(CaptionKind.Minimize) { ToolTipText = Loc.T("最小化") };
        closeBtn.Click += (_, _) => Close();
        maxBtn.Click += (_, _) => WindowState = WindowState == FormWindowState.Maximized ? FormWindowState.Normal : FormWindowState.Maximized;
        minBtn.Click += (_, _) => WindowState = FormWindowState.Minimized;
        strip.Items.Add(closeBtn);
        strip.Items.Add(maxBtn);
        strip.Items.Add(minBtn);
        _maximizeButtons.Add(maxBtn);
    }

    // 最大化状態に応じて 🗖/復元 グリフを切り替える
    private void UpdateCaptionGlyphs()
    {
        var glyph = WindowState == FormWindowState.Maximized
            ? CaptionToolButton.GlyphRestore
            : CaptionToolButton.GlyphMaximize;
        foreach (var btn in _maximizeButtons)
        {
            if (btn.Text != glyph) btn.Text = glyph;
        }
    }

    // 下部バーは「表示中の要素の合計幅」だけのコンパクトな作りにする。
    // GIF操作・ページ送りなどのグループは必要なときだけ表示され、その分だけ横に広がる
    private void BuildViewerNavPanel()
    {
        _viewerNavPanel.Padding = new Padding(10, 8, 10, 8);
        _viewerNavPanel.Visible = false;

        var layout = new FlowLayoutPanel
        {
            Location = new Point(_viewerNavPanel.Padding.Left, _viewerNavPanel.Padding.Top),
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            BackColor = Color.Transparent,
            Margin = Padding.Empty,
            Padding = Padding.Empty,
        };
        _navFlow = layout;

        _gifPanel = new FlowLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            BackColor = Color.Transparent,
            Margin = new Padding(0, 0, 8, 0),
            Padding = Padding.Empty,
            Visible = false,
        };
        _gifPrevBtn = ViewerNavButton("⏮", Loc.T("前のコマ"));
        _gifPlayBtn = ViewerNavButton("⏸", Loc.T("再生 / 停止"));
        _gifNextBtn = ViewerNavButton("⏭", Loc.T("次のコマ"));
        foreach (var button in new[] { _gifPrevBtn, _gifPlayBtn, _gifNextBtn })
        {
            button.Dock = DockStyle.None;
            button.Size = new Size(28, 34);
            button.Margin = new Padding(1, 0, 1, 0);
        }
        _gifTrack = new TrackBar
        {
            Width = 72,
            Height = 34,
            Minimum = 0,
            Maximum = 1,
            Value = 0,
            TickStyle = TickStyle.None,
            Margin = new Padding(2, 4, 2, 0),
        };
        _gifSpeedCombo = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            FlatStyle = FlatStyle.Flat,
            Width = 62,
            Height = 26,
            Margin = new Padding(2, 4, 2, 0),
            Visible = false,
        };
        foreach (var speed in new[] { "0.25x", "0.5x", "1x", "1.5x", "2x", "4x" }) _gifSpeedCombo.Items.Add(speed);
        _gifSpeedCombo.SelectedItem = "1x";
        new ToolTip().SetToolTip(_gifSpeedCombo, Loc.T("再生速度"));
        _gifFrameLabel = new Label
        {
            AutoSize = false,
            Width = 44,
            Height = 34,
            TextAlign = ContentAlignment.MiddleLeft,
            Margin = new Padding(2, 0, 0, 0),
        };

        _viewerPrevBtn = ViewerNavButton("<", Loc.T("前の画像"));
        _viewerNextBtn = ViewerNavButton(">", Loc.T("次の画像"));
        _viewerFullscreenBtn = ViewerNavButton(CaptionToolButton.GlyphFullScreen, Loc.T("全画面表示"));
        _viewerFullscreenBtn.Font = new Font("Segoe MDL2 Assets", 10f);
        _viewerPageLabel = new Label
        {
            AutoSize = false,
            Width = 60,
            Height = 34,
            TextAlign = ContentAlignment.MiddleCenter,
            Font = new Font(Font.FontFamily, 9f, FontStyle.Bold),
            Margin = new Padding(2, 0, 2, 0),
        };

        // ページ送りグループ (複数画像のときだけ表示)
        _pageGroup = new FlowLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            BackColor = Color.Transparent,
            Margin = new Padding(0, 0, 8, 0),
            Padding = Padding.Empty,
            Visible = false,
        };

        foreach (var button in new[] { _viewerPrevBtn, _viewerNextBtn })
        {
            button.Dock = DockStyle.None;
            button.Size = new Size(30, 34);
            button.Margin = new Padding(1, 0, 1, 0);
        }
        _viewerFullscreenBtn.Dock = DockStyle.None;
        _viewerFullscreenBtn.Size = new Size(38, 34);
        _viewerFullscreenBtn.Margin = new Padding(1, 0, 1, 0);

        _gifPrevBtn.Click += (_, _) => _canvas.SetViewerFrame(_canvas.ViewerCurrentFrame - 1);
        _gifNextBtn.Click += (_, _) => _canvas.SetViewerFrame(_canvas.ViewerCurrentFrame + 1);
        _gifPlayBtn.Click += (_, _) => _canvas.SetViewerAnimationPlaying(!_canvas.ViewerAnimationPlaying);
        _gifTrack.Scroll += (_, _) =>
        {
            if (_syncingGifUi) return;
            _canvas.SetViewerFrame(_gifTrack.Value);
        };
        _viewerPrevBtn.Click += (_, _) => NavigateViewerImage(-1);
        _viewerNextBtn.Click += (_, _) => NavigateViewerImage(+1);
        _viewerFullscreenBtn.Click += (_, _) => ToggleViewerFullscreen();
        _gifSpeedCombo.SelectedIndexChanged += (_, _) =>
        {
            if (_syncingGifUi) return;
            var raw = (_gifSpeedCombo.SelectedItem as string ?? "1x").TrimEnd('x');
            if (float.TryParse(raw, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var speed))
            {
                _canvas.SetViewerPlaybackSpeed(speed);
            }
        };
        _canvas.ViewerFrameChanged += (_, _) => SyncGifControls();

        _gifPanel.Controls.Add(_gifPrevBtn);
        _gifPanel.Controls.Add(_gifPlayBtn);
        _gifPanel.Controls.Add(_gifNextBtn);
        _gifPanel.Controls.Add(_gifTrack);
        _gifPanel.Controls.Add(_gifSpeedCombo);
        _gifPanel.Controls.Add(_gifFrameLabel);
        _pageGroup.Controls.Add(_viewerPrevBtn);
        _pageGroup.Controls.Add(_viewerPageLabel);
        _pageGroup.Controls.Add(_viewerNextBtn);

        layout.Controls.Add(_gifPanel);
        layout.Controls.Add(_pageGroup);
        layout.Controls.Add(_viewerFullscreenBtn);
        _viewerNavPanel.Controls.Add(layout);

        _viewerChromeTimer.Tick += (_, _) => UpdateViewerChrome();
        ApplyViewerNavOpacity(0f);
    }

    private Button ViewerNavButton(string text, string tooltip)
    {
        var button = new RoundedFlatButton
        {
            Text = text,
            Dock = DockStyle.Fill,
            Margin = new Padding(4, 0, 4, 0),
            CornerRadius = 8,
            FlatStyle = FlatStyle.Flat,
        };
        button.FlatAppearance.BorderSize = 0;
        new ToolTip().SetToolTip(button, tooltip);
        return button;
    }

    // 表示中の要素の合計幅にぴったり合わせて中央下に配置する
    private void UpdateViewerNavPanelBounds()
    {
        if (_navFlow == null) return;
        if (_canvas.ClientSize.Width <= 0 || _canvas.ClientSize.Height <= 0) return;

        int availableWidth = Math.Max(120, _canvas.ClientSize.Width - 24);
        _navFlow.WrapContents = false;
        _navFlow.MaximumSize = Size.Empty;
        _navFlow.PerformLayout();
        var pref = _navFlow.PreferredSize;

        int contentMaxWidth = Math.Max(1, availableWidth - _viewerNavPanel.Padding.Horizontal);
        if (pref.Width > contentMaxWidth)
        {
            _navFlow.WrapContents = true;
            _navFlow.MaximumSize = new Size(contentMaxWidth, 0);
            _navFlow.PerformLayout();
            pref = _navFlow.PreferredSize;
        }

        int width = Math.Min(pref.Width + _viewerNavPanel.Padding.Horizontal, availableWidth);
        int height = Math.Max(50, pref.Height + _viewerNavPanel.Padding.Vertical);

        _viewerNavPanel.Size = new Size(width, height);
        _viewerNavPanel.Location = new Point(
            (_canvas.ClientSize.Width - width) / 2,
            _canvas.ClientSize.Height - height - 20);
        _viewerNavPanel.BringToFront();
    }

    private void UpdateViewerNavState()
    {
        bool multi = _viewerFiles.Count > 1;
        if (_viewerPageLabel != null)
        {
            var count = Math.Max(1, _viewerFiles.Count);
            var index = _viewerFileIndex >= 0 ? _viewerFileIndex + 1 : 1;
            _viewerPageLabel.Text = $"{index} / {count}";
        }
        if (_pageGroup != null) _pageGroup.Visible = multi; // 1枚だけならページ送りごと隠して幅を詰める
        if (_viewerPrevBtn != null) _viewerPrevBtn.Enabled = multi;
        if (_viewerNextBtn != null) _viewerNextBtn.Enabled = multi;
        if (_viewerFullscreenBtn != null)
        {
            _viewerFullscreenBtn.Text = _viewerFullscreen ? CaptionToolButton.GlyphBackToWindow : CaptionToolButton.GlyphFullScreen;
        }
        SetGifControlsVisible(_canvas.ViewerFrameCount > 1);
        UpdateViewerNavPanelBounds();
    }

    private void ShowViewerChrome()
    {
        if (!_viewerMode) return;
        _lastViewerActivity = DateTime.UtcNow;
        _lastCursorScreenPos = Cursor.Position;
        _viewerNavPanel.Visible = true;
        _viewerNavPanel.BringToFront();
        _viewerChromeOpacity = 1f;
        ApplyViewerNavOpacity(_viewerChromeOpacity);
        if (!_viewerChromeTimer.Enabled) _viewerChromeTimer.Start();
    }

    private void UpdateViewerChrome()
    {
        if (!_viewerMode)
        {
            _viewerNavPanel.Visible = false;
            return;
        }

        // グローバルカーソルの実際の移動だけを「操作あり」とみなす (擬似MouseMoveに惑わされない)
        var cursor = Cursor.Position;
        bool pointerInside = Bounds.Contains(cursor);
        int dx = Math.Abs(cursor.X - _lastCursorScreenPos.X);
        int dy = Math.Abs(cursor.Y - _lastCursorScreenPos.Y);
        if (pointerInside && (dx > 1 || dy > 1))
        {
            _lastViewerActivity = DateTime.UtcNow;
        }
        _lastCursorScreenPos = cursor;

        // ウィンドウ外に出る or 3秒間カーソルが動かない → フェードアウト
        bool active = pointerInside && (DateTime.UtcNow - _lastViewerActivity).TotalSeconds < 3;
        var step = active ? 0.25f : -0.08f;
        _viewerChromeOpacity = Math.Clamp(_viewerChromeOpacity + step, 0f, 1f);
        ApplyViewerNavOpacity(_viewerChromeOpacity);
        _viewerNavPanel.Visible = _viewerChromeOpacity > 0f;
    }

    private void ApplyViewerNavOpacity(float opacity)
    {
        var t = Theme.Current;
        _viewerNavPanel.BackColor = Blend(t.CanvasBg, t.Surface, opacity * 0.95f);
        ApplyRoundedRegion(_viewerNavPanel, 12);

        var fore = Blend(t.CanvasBg, t.TextPrimary, opacity);
        foreach (Control c in _viewerNavPanel.Controls)
        {
            ApplyViewerNavControlOpacity(c, opacity, fore);
        }
    }

    private void ApplyViewerNavControlOpacity(Control control, float opacity, Color fore)
    {
        control.ForeColor = fore;
        if (control is RoundedFlatButton btn)
        {
            btn.BaseColor = Blend(Theme.Current.CanvasBg, Theme.Current.ButtonBg, opacity);
        }
        else if (control is ComboBox combo)
        {
            combo.BackColor = Blend(Theme.Current.CanvasBg, Theme.Current.Surface, opacity);
        }
        foreach (Control child in control.Controls) ApplyViewerNavControlOpacity(child, opacity, fore);
    }

    private static Color Blend(Color from, Color to, float amount)
    {
        amount = Math.Clamp(amount, 0f, 1f);
        return Color.FromArgb(
            (int)(from.R + (to.R - from.R) * amount),
            (int)(from.G + (to.G - from.G) * amount),
            (int)(from.B + (to.B - from.B) * amount));
    }

    private void ToggleViewerFullscreen()
    {
        if (!_viewerMode) return;
        if (_viewerFullscreen) ExitViewerFullscreen();
        else EnterViewerFullscreen();
        ShowViewerChrome();
    }

    private void EnterViewerFullscreen()
    {
        if (_viewerFullscreen) return;
        _viewerRestoreBounds = Bounds;
        _viewerRestoreWindowState = WindowState;
        _viewerRestorePadding = Padding;
        _viewerFullscreen = true;

        // ウィンドウ要素(バー・角丸・余白)をすべて排除し、画面全体に画像表示を張り付ける
        WindowState = FormWindowState.Normal;
        Padding = Padding.Empty;
        _viewerBar.Visible = false;
        Bounds = Screen.FromControl(this).Bounds;
        UpdateWindowRegion(); // Region解除 (角丸なし)
        UpdateViewerNavState();
        _canvas.Focus();
    }

    private void ExitViewerFullscreen()
    {
        if (!_viewerFullscreen) return;
        _viewerFullscreen = false;
        Padding = _viewerRestorePadding;
        _viewerBar.Visible = true;
        WindowState = _viewerRestoreWindowState;
        if (_viewerRestoreWindowState == FormWindowState.Normal) Bounds = _viewerRestoreBounds;
        UpdateWindowRegion();
        UpdateViewerNavState();
    }

    // ビュアー → 通常の編集画面へ。前回セッションのタブを復元した上で、
    // 表示中の画像が載った新しいキャンバスを末尾に追加して選択する
    private void SwitchToEditorFromViewer()
    {
        if (!_viewerMode) return;
        if (_viewerFullscreen) ExitViewerFullscreen();
        _viewerMode = false;

        MinimumSize = new Size(1050, 650); // 編集画面の最小サイズに戻す
        if (Width < 1050 || Height < 650) Size = new Size(Math.Max(Width, 1050), Math.Max(Height, 650));

        var viewerDoc = ActiveDoc;
        _viewerChromeTimer.Stop();
        _viewerNavPanel.Visible = false;
        SetGifControlsVisible(false);
        _canvas.ReadOnlyView = false;
        _canvas.ShutdownViewerAnimation(); // 自前駆動をやめてImageAnimatorへ戻す

        // ビュアー起動中はセッションを読み書きしていないため、ここで前回タブを復元する
        var s = SessionStore.Load();
        _canvas.InsertNaturalSize = s?.InsertNaturalSize ?? false;
        if (s != null && _appSettings.RestoreTabs && viewerDoc != null)
        {
            _docs.Remove(viewerDoc);
            for (int i = 0; i < s.Tabs.Count; i++)
            {
                try
                {
                    var doc = LayoutSerializer.FromDto(s.Tabs[i], i < s.TabFilePaths.Count ? s.TabFilePaths[i] : null);
                    AddDocument(doc, select: false);
                }
                catch
                {
                    // 個別キャンバスの復元失敗は無視
                }
            }
            _docs.Add(viewerDoc);
        }

        _viewerBar.Visible = false;
        _menuBar.Visible = true;
        _sessionTitleLabel.Visible = true;
        _docTabs.Visible = true;
        _rightPanel.Visible = true;
        _overlayFrame.Visible = true;

        RebuildDocTabs();
        SelectDocument(_docs.Count - 1);
        _canvas.ZoomFitAll();
        if (_naturalMi != null) _naturalMi.Checked = _canvas.InsertNaturalSize;
        SetSidebarView(_sidebarView);
        UpdateSidebarBounds();
        UpdateSessionTitle();

        _autosaveTimer.Start();
        SaveSession();
    }

    private void UpdateSessionTitle()
    {
        _sessionTitleLabel.Text = string.IsNullOrEmpty(_sessionFilePath)
            ? Loc.T("未保存セッション")
            : Path.GetFileNameWithoutExtension(_sessionFilePath);
        PositionSessionTitle();
    }

    private void PositionSessionTitle()
    {
        if (_sessionTitleLabel.Parent == null) return;
        _sessionTitleLabel.Left = Math.Max(0, (ClientSize.Width - _sessionTitleLabel.Width) / 2);
        _sessionTitleLabel.Top = _menuBar.Top + Math.Max(0, (_menuBar.Height - _sessionTitleLabel.Height) / 2);
        _sessionTitleLabel.BringToFront();
    }

    // ドロップダウンウィンドウの角を丸める
    private static void RoundDropDownCorners(ToolStripDropDown dropDown)
    {
        void Apply(object? s, EventArgs e)
        {
            if (dropDown.Width <= 0 || dropDown.Height <= 0) return;
            using var path = CreateRoundedRectPath(new Rectangle(0, 0, dropDown.Width, dropDown.Height), 8);
            dropDown.Region = new Region(path);
        }
        dropDown.Opened += Apply;
        dropDown.SizeChanged += Apply;
    }

    private ToolStripMenuItem MI(string text, string? actionId, EventHandler onClick)
    {
        var mi = new ToolStripMenuItem(text)
        {
            Margin = Padding.Empty,
            Padding = new Padding(10, 5, 12, 5),
        };
        mi.Click += onClick;
        if (actionId != null) _actionMenuItems[actionId] = mi;
        return mi;
    }

    private ToolStripMenuItem BuildFileMenu()
    {
        var menu = MakeMenuButton(Loc.T("ファイル"));
        var newMi = MI("🆕 " + Loc.T("新規"), null, (_, _) => NewSession());
        newMi.ToolTipText = Loc.T("現在のセッションを閉じ、新しい未保存セッションを開始します。");
        menu.DropDownItems.Add(newMi);
        var openMi = MI("📂 " + Loc.T("画像、キャンバス、セッションを開く..."), "file.open", (_, _) => OpenFile());
        openMi.ToolTipText = Loc.T("画像、キャンバス設定ファイル、画像同梱セッションを開きます。");
        menu.DropDownItems.Add(openMi);
        var urlMi = MI("🌐 " + Loc.T("URLから画像を開く..."), null, async (_, _) => await OpenImageUrlDialogAsync());
        urlMi.ToolTipText = Loc.T("画像URLをダウンロードしてキャンバスへ配置します。");
        menu.DropDownItems.Add(urlMi);
        menu.DropDownItems.Add(new ToolStripSeparator());
        var saveSessionMi = MI("💾 " + Loc.T("セッションを保存"), "file.save", (_, _) => OverwriteSession());
        saveSessionMi.ToolTipText = Loc.T("最後に保存または開いたセッションファイルへ保存します。未保存の場合は保存先を選びます。");
        menu.DropDownItems.Add(saveSessionMi);
        var saveSessionAsMi = MI("💾 " + Loc.T("セッションを別名で保存..."), "file.saveAs", (_, _) => SaveSessionAs());
        saveSessionAsMi.ToolTipText = Loc.T("現在開いているキャンバスと使用画像を、新しいセッションファイルとして保存します。");
        menu.DropDownItems.Add(saveSessionAsMi);
        var saveCanvasMi = MI("💾 " + Loc.T("キャンバスを保存"), null, (_, _) => ExportCurrentLayoutSettings());
        saveCanvasMi.ToolTipText = Loc.T("現在選択中のキャンバスだけを、画像パス参照の設定ファイルとして保存します。");
        menu.DropDownItems.Add(saveCanvasMi);
        var exportImageMi = MI("🖼 " + Loc.T("キャンバスを画像として出力"), "file.export", (_, _) => ExportPng());
        exportImageMi.ToolTipText = Loc.T("キャンバスに配置されている画像が収まる最小のサイズで出力します。");
        menu.DropDownItems.Add(exportImageMi);
        var shareMi = MI("🔗 " + Loc.T("共有用にエクスポート..."), null, (_, _) => ExportShare());
        shareMi.ToolTipText = Loc.T("個人情報を含まない画像同梱ファイル(.mics)を作成します。受け取った人はこのアプリでそのまま開けます。");
        menu.DropDownItems.Add(shareMi);
        menu.DropDownItems.Add(new ToolStripSeparator());
        var newCanvasMi = MI("➕ " + Loc.T("新規キャンバスタブ"), "file.newTab", (_, _) => AddDocument(new CanvasDocument(), true));
        newCanvasMi.ToolTipText = Loc.T("現在のセッション内に空のキャンバスタブを追加します。");
        menu.DropDownItems.Add(newCanvasMi);
        var closeCanvasMi = MI(Loc.T("キャンバスを閉じる"), "file.closeTab", (_, _) => CloseDocument(_activeDocIndex));
        closeCanvasMi.ToolTipText = Loc.T("現在のキャンバスタブを閉じます。外部保存していない内容は復元できません。");
        menu.DropDownItems.Add(closeCanvasMi);
        menu.DropDownItems.Add(new ToolStripSeparator());
        menu.DropDownItems.Add(MI("⚙ " + Loc.T("設定..."), null, (_, _) => OpenSettings()));
        menu.DropDownItems.Add(new ToolStripSeparator());
        menu.DropDownItems.Add(MI(Loc.T("終了"), null, (_, _) => Close()));
        return menu;
    }

    private ToolStripMenuItem BuildEditMenu()
    {
        var menu = MakeMenuButton(Loc.T("編集"));
        _undoMi = MI("↩ " + Loc.T("元に戻す"), "edit.undo", (_, _) => _canvas.Undo());
        _redoMi = MI("↪ " + Loc.T("やり直す"), "edit.redo", (_, _) => _canvas.Redo());
        menu.DropDownItems.Add(_undoMi);
        menu.DropDownItems.Add(_redoMi);
        menu.DropDownItems.Add(new ToolStripSeparator());
        menu.DropDownItems.Add(MI("⟳ " + Loc.T("右に90°回転"), "edit.rotateCw", (_, _) => _canvas.RotateSelected(90)));
        menu.DropDownItems.Add(MI("⟲ " + Loc.T("左に90°回転"), "edit.rotateCcw", (_, _) => _canvas.RotateSelected(-90)));
        menu.DropDownItems.Add(MI("↔ " + Loc.T("左右反転"), "edit.flipH", (_, _) => _canvas.FlipSelected(true)));
        menu.DropDownItems.Add(MI("↕ " + Loc.T("上下反転"), "edit.flipV", (_, _) => _canvas.FlipSelected(false)));
        menu.DropDownItems.Add(new ToolStripSeparator());
        menu.DropDownItems.Add(MI("📄 " + Loc.T("複製"), "edit.duplicate", (_, _) => _canvas.DuplicateSelected()));
        menu.DropDownItems.Add(MI("🗑 " + Loc.T("削除"), "edit.delete", (_, _) => _canvas.DeleteSelected()));
        menu.DropDownItems.Add(MI("🧹 " + Loc.T("キャンバス内を一括削除"), null, (_, _) => ClearAllWithConfirm()));
        menu.DropDownOpening += (_, _) => SyncMenuState();
        return menu;
    }

    private ToolStripMenuItem BuildViewMenu()
    {
        var menu = MakeMenuButton(Loc.T("表示"));
        menu.DropDownItems.Add(MI("＋ " + Loc.T("ズームイン"), "view.zoomIn", (_, _) => _canvas.SetZoom(_canvas.Zoom * 1.15f)));
        menu.DropDownItems.Add(MI("－ " + Loc.T("ズームアウト"), "view.zoomOut", (_, _) => _canvas.SetZoom(_canvas.Zoom / 1.15f)));
        menu.DropDownItems.Add(MI(Loc.T("100% 表示"), "view.zoom100", (_, _) => _canvas.SetZoom(1.0f)));
        menu.DropDownItems.Add(MI("🗺 " + Loc.T("全体表示"), "view.fitAll", (_, _) => _canvas.ZoomFitAll()));
        menu.DropDownItems.Add(new ToolStripSeparator());
        _naturalMi = new ToolStripMenuItem("📷 " + Loc.T("原寸で配置")) { CheckOnClick = true, ToolTipText = Loc.T("ONの間、画像を原寸ピクセルサイズで配置します") };
        _naturalMi.Click += (_, _) => _canvas.InsertNaturalSize = _naturalMi.Checked;
        menu.DropDownItems.Add(_naturalMi);
        menu.DropDownItems.Add(new ToolStripSeparator());
        _topmostMi = new ToolStripMenuItem("📌 " + Loc.T("ウィンドウを前面固定")) { CheckOnClick = true };
        _topmostMi.Click += (_, _) => TopMost = _topmostMi.Checked;
        menu.DropDownItems.Add(_topmostMi);
        menu.DropDownItems.Add(MI("👁 " + Loc.T("UI非表示 (クリックで復帰)"), "view.hideUi", (_, _) => HideUi()));
        return menu;
    }

    private ToolStripMenuItem BuildHelpMenu()
    {
        var menu = MakeMenuButton(Loc.T("ヘルプ"));
        menu.DropDownItems.Add(MI("❔ " + Loc.T("ショートカット一覧"), "help.shortcuts", (_, _) => ShowHelp()));
        menu.DropDownItems.Add(MI("ℹ " + Loc.T("バージョン情報"), null, (_, _) => ShowAbout()));
        return menu;
    }

    private ToolStripMenuItem BuildOverlayMenu()
    {
        var menu = MakeMenuButton(Loc.T("オーバーレイ"));

        _overlayActiveMi = new ToolStripMenuItem("🔲 " + Loc.T("オーバーレイ有効化")) { CheckOnClick = true, ShortcutKeyDisplayString = "Ctrl+Alt+H" };
        _overlayActiveMi.Click += (_, _) => ToggleOverlayMode(_overlayActiveMi.Checked);
        menu.DropDownItems.Add(_overlayActiveMi);

        _clickThroughMi = new ToolStripMenuItem("🖱 " + Loc.T("クリック透過")) { CheckOnClick = true, ShortcutKeyDisplayString = "Ctrl+Alt+T" };
        _clickThroughMi.Click += (_, _) => SetOverlayClickThrough(_clickThroughMi.Checked);
        menu.DropDownItems.Add(_clickThroughMi);

        menu.DropDownItems.Add(new ToolStripSeparator());

        _opacityMenuLabel = new ToolStripLabel($"{Loc.T("オーバーレイ透過率")}: {(int)(_overlayOpacity * 100)}%")
        {
            ForeColor = Theme.Current.TextSecondary,
            ToolTipText = "Ctrl+Alt+PgUp / PgDn",
        };
        _opacityTrack = new ToolStripTrackBar(20, 100, (int)(_overlayOpacity * 100));
        _opacityTrack.TrackBar.Width = 180;
        _opacityTrack.TrackBar.BackColor = Theme.Current.Surface;
        _opacityTrack.BackColor = Theme.Current.Surface;
        _opacityTrack.TrackBar.Scroll += (_, _) => SetOverlayOpacity(_opacityTrack.TrackBar.Value / 100f);
        menu.DropDownItems.Add(_opacityMenuLabel);
        menu.DropDownItems.Add(_opacityTrack);

        return menu;
    }

    // キーバインド変更をメニューの表示に反映
    private void UpdateMenuShortcutTexts()
    {
        foreach (var (actionId, mi) in _actionMenuItems)
        {
            mi.ShortcutKeyDisplayString = _keyMap.GetDisplay(actionId);
        }
    }

    private void WireTitleBarDrag(ToolStrip strip)
    {
        // ラベル(画像名など)の上はドラッグ可能な領域として扱う
        static bool IsDraggableSpot(ToolStrip s, Point location)
        {
            var item = s.GetItemAt(location);
            return item == null || item is ToolStripLabel;
        }

        strip.MouseMove += (_, e) =>
        {
            if (!IsDraggableSpot(strip, e.Location))
            {
                strip.Cursor = Cursors.Default;
                return;
            }
            strip.Cursor = GetTitleBarCursor(strip, e.Location);
        };
        strip.MouseLeave += (_, _) => strip.Cursor = Cursors.Default;
        strip.MouseDown += (s, e) =>
        {
            if (e.Button != MouseButtons.Left) return;
            const int grip = 8;
            if (TryBeginTopResize(strip, e.Location)) return;

            if (!IsDraggableSpot(strip, e.Location)) return;

            if (e.X < grip)
            {
                ReleaseCapture();
                SendMessage(Handle, 0xA1, 10, 0); // HTLEFT
                return;
            }
            if (e.X > strip.Width - grip)
            {
                ReleaseCapture();
                SendMessage(Handle, 0xA1, 11, 0); // HTRIGHT
                return;
            }

            BeginWindowMove();
        };
    }

    private void BeginWindowMove()
    {
        ReleaseCapture();
        SendMessage(Handle, 0xA1, 0x2, 0); // HTCAPTION: ウィンドウドラッグ
    }

    private Cursor GetTitleBarCursor(Control control, Point localPoint)
    {
        const int grip = 8;
        var p = PointToClient(control.PointToScreen(localPoint));
        var left = p.X < grip;
        var right = p.X > ClientSize.Width - grip;
        var top = p.Y < grip;

        if (top && left) return Cursors.SizeNWSE;
        if (top && right) return Cursors.SizeNESW;
        if (top) return Cursors.SizeNS;
        if (left || right) return Cursors.SizeWE;
        return Cursors.Default;
    }

    private bool TryBeginTopResize(Control control, Point localPoint)
    {
        const int grip = 8;
        var formPoint = PointToClient(control.PointToScreen(localPoint));
        if (formPoint.Y >= grip) return false;

        ReleaseCapture();
        if (formPoint.X < grip) SendMessage(Handle, 0xA1, 13, 0); // HTTOPLEFT
        else if (formPoint.X > ClientSize.Width - grip) SendMessage(Handle, 0xA1, 14, 0); // HTTOPRIGHT
        else SendMessage(Handle, 0xA1, 12, 0); // HTTOP
        return true;
    }

    // Undo/Redoメニューの有効状態・ラベルを最新化
    private void SyncMenuState()
    {
        var doc = ActiveDoc;
        if (_undoMi != null)
        {
            _undoMi.Enabled = doc?.Undo.CanUndo == true;
            _undoMi.Text = doc?.Undo.NextUndoLabel is { } l ? $"↩ {Loc.T("元に戻す")}: {Loc.T(l)}" : $"↩ {Loc.T("元に戻す")}";
        }
        if (_redoMi != null)
        {
            _redoMi.Enabled = doc?.Undo.CanRedo == true;
            _redoMi.Text = doc?.Undo.NextRedoLabel is { } l ? $"↪ {Loc.T("やり直す")}: {Loc.T(l)}" : $"↪ {Loc.T("やり直す")}";
        }
    }

    private void UpdateZoomText() => _zoomText.Text = $"{_canvas.Zoom * 100:0}%";

    private void OpenSettings()
    {
        using var dlg = new SettingsForm(_keyMap, _restoreTabsSetting, _autosaveSeconds, _canvas.SnapEnabled, _canvas.GridSnapEnabled, (int)Math.Round(_canvas.ImageImportScale * 100), _language, _overlayAnimation, GetAutosaveLogText());
        dlg.Applied += (_, _) => ApplySettingsFromDialog(dlg);
        dlg.ShowDialog(this);
    }

    private static string GetAutosaveLogText()
    {
        if (!File.Exists(SessionStore.FilePath)) return Loc.T("自動保存ログはまだありません。");
        var time = File.GetLastWriteTime(SessionStore.FilePath);
        return $"{time:yyyy/MM/dd HH:mm:ss} {Loc.T("現在の設定、キャンバスを自動保存しました。")}";
    }

    private void ApplySettingsFromDialog(SettingsForm dlg)
    {
        _restoreTabsSetting = dlg.RestoreTabs;
        _autosaveSeconds = Math.Clamp(dlg.AutosaveSeconds, 10, 600);
        _autosaveTimer.Interval = _autosaveSeconds * 1000;
        _canvas.SnapEnabled = dlg.SnapEnabled;
        _canvas.GridSnapEnabled = dlg.GridSnap;
        _canvas.ImageImportScale = dlg.ImageImportScalePercent / 100f;
        _language = Loc.Normalize(dlg.Language);
        Loc.Apply(_language);
        _overlayAnimation = dlg.OverlayAnimation;
        Theme.Apply(dlg.SelectedTheme);
        ApplyLanguageToControls();
        UpdateMenuShortcutTexts();
        SaveSettings();
    }

    // ===== メイン配置 =====

    private void BuildMainLayout()
    {
        _canvas.Dock = DockStyle.Fill;

        BuildRightPanel();
        BuildItemPanel();
        BuildOverlayFrame();
        BuildViewerNavPanel();

        _canvas.Controls.Add(_rightPanel);
        _canvas.Controls.Add(_itemPanel);
        _canvas.Controls.Add(_overlaySettingsPanel);
        _canvas.Controls.Add(_overlayFrame);
        _canvas.Controls.Add(_viewerNavPanel);

        ApplyRoundedRegion(_rightPanel, CornerRadius + 4);
        ApplyRoundedRegion(_itemPanel, CornerRadius + 4);
        ApplyRoundedRegion(_overlaySettingsPanel, CornerRadius + 4);
        ApplyRoundedRegion(_overlayFrame, CornerRadius + 4);
        ApplyRoundedRegion(_viewerNavPanel, 12);

        Controls.Add(_canvas);
        Controls.Add(_docTabs);
        Controls.Add(_menuBar);
        BuildViewerBar();
        Controls.Add(_viewerBar);
        Controls.SetChildIndex(_viewerBar, 0);
        Controls.SetChildIndex(_docTabs, 1);
        Controls.SetChildIndex(_menuBar, 2);
        BuildSessionTitleLabel();

        _rightPanel.BringToFront();
        _itemPanel.BringToFront();
        _overlayFrame.BringToFront();
        _viewerNavPanel.BringToFront();

        _canvas.SizeChanged += (_, _) => { UpdateSidebarBounds(); UpdateViewerNavPanelBounds(); };
        UpdateSidebarBounds();
    }

    private void BuildRightPanel()
    {
        _rightPanel.Padding = new Padding(12);
        _rightPanel.BackColor = Theme.Current.Surface;

        // タイトル行 + 表示切替ボタン
        var titlePanel = new Panel { Dock = DockStyle.Top, Height = 36, BackColor = Theme.Current.Surface };
        var titleLabel = new Label
        {
            Text = "📂 " + Loc.T("ファイル"),
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            Font = new Font(Font.FontFamily, 9.5f, FontStyle.Bold),
            ForeColor = Theme.Current.TextPrimary,
            BackColor = Theme.Current.Surface,
            Padding = new Padding(2, 4, 2, 4),
        };

        var switchRow = new FlowLayoutPanel
        {
            Dock = DockStyle.Right,
            Width = 118,
            FlowDirection = FlowDirection.LeftToRight,
            BackColor = Theme.Current.Surface,
            Padding = new Padding(0, 2, 0, 2),
        };

        Button MakeViewBtn(string text, string tip, string mode)
        {
            var b = new RoundedFlatButton
            {
                Text = text,
                Width = 34,
                Height = 28,
                ForeColor = Theme.Current.TextPrimary,
                Margin = new Padding(2, 0, 0, 0),
                CornerRadius = 8,
            };
            b.Click += (_, _) => SetSidebarView(mode);
            new ToolTip().SetToolTip(b, tip);
            return b;
        }

        _viewTreeBtn = MakeViewBtn("🌳", Loc.T("フォルダツリー"), "tree");
        _viewThumbsBtn = MakeViewBtn("🖼", Loc.T("サムネイル一覧"), "thumbs");
        _viewLayersBtn = MakeViewBtn("📚", Loc.T("レイヤー"), "layers");
        switchRow.Controls.Add(_viewTreeBtn);
        switchRow.Controls.Add(_viewThumbsBtn);
        switchRow.Controls.Add(_viewLayersBtn);

        titlePanel.Controls.Add(titleLabel);
        titlePanel.Controls.Add(switchRow);

        var pathPanel = BuildPathPanel();

        // コンテンツ領域 (ツリー / サムネイル / レイヤー)
        _sidebarContent.Dock = DockStyle.Fill;
        _sidebarContent.BackColor = Theme.Current.Surface;

        _treeArea = BuildTreeArea();
        _thumbs = new ThumbnailView { Dock = DockStyle.Fill, Visible = false };
        _thumbs.ImageActivated += (_, path) => _canvas.AddImage(path);
        _thumbs.FolderChanged += (_, folder) => { _lastFolderPath = folder; SetPathText(folder); };
        _layers = new LayerPanel(_canvas) { Dock = DockStyle.Fill, Visible = false };

        _sidebarContent.Controls.Add(_treeArea);
        _sidebarContent.Controls.Add(_thumbs);
        _sidebarContent.Controls.Add(_layers);

        ApplyRoundedRegion(_sidebarContent, CornerRadius);

        _rightPanel.Controls.Add(_sidebarContent);
        _rightPanel.Controls.Add(pathPanel);
        _rightPanel.Controls.Add(titlePanel);
    }

    private void SetSidebarView(string mode)
    {
        _sidebarView = mode;
        if (_treeArea != null) _treeArea.Visible = mode == "tree";
        if (_thumbs != null)
        {
            _thumbs.Visible = mode == "thumbs";
            if (mode == "thumbs")
            {
                var folder = _lastFolderPath ?? KnownFolders.GetPicturesPath();
                if (folder != null && _thumbs.CurrentFolder != folder) _thumbs.LoadFolder(folder);
            }
        }
        if (_layers != null)
        {
            _layers.Visible = mode == "layers";
            if (mode == "layers") _layers.RefreshList();
        }

        // 選択中ボタンをアクセント色に
        void Mark(Button? b, bool active)
        {
            if (b == null) return;
            b.BackColor = active ? Theme.Current.AccentDark : Theme.Current.ButtonBg;
        }
        Mark(_viewTreeBtn, mode == "tree");
        Mark(_viewThumbsBtn, mode == "thumbs");
        Mark(_viewLayersBtn, mode == "layers");
    }

    // ===== 選択画像の操作メニュー (選択した画像の横にフローティング表示) =====
    // 内容は右クリックメニューと同じテキストリスト

    private const int ItemPanelWidth = 176;
    private const int ItemPanelHeight = 278;

    private void BuildItemPanel()
    {
        _itemPanel.BackColor = Theme.Current.Surface;
        _itemPanel.Padding = new Padding(8, 8, 8, 8);
        _itemPanel.Size = new Size(ItemPanelWidth, ItemPanelHeight);
        _itemPanel.Visible = false;

        var list = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            BackColor = Theme.Current.Surface,
            Margin = new Padding(0),
        };

        Button Row(string text, EventHandler onClick)
        {
            var b = new RoundedFlatButton
            {
                Text = text,
                Width = ItemPanelWidth - 16,
                Height = 26,
                ForeColor = Theme.Current.TextPrimary,
                TextAlign = ContentAlignment.MiddleLeft,
                Padding = new Padding(8, 0, 4, 0),
                Margin = new Padding(0),
                CornerRadius = 6,
            };
            b.Click += onClick;
            return b;
        }

        Panel Separator() => new()
        {
            Width = ItemPanelWidth - 16,
            Height = 1,
            BackColor = Theme.Current.SurfaceBorder,
            Margin = new Padding(0, 4, 0, 4),
        };

        list.Controls.Add(Row("複製", (_, _) => _canvas.DuplicateSelected()));
        list.Controls.Add(Row("削除", (_, _) => _canvas.DeleteSelected()));
        list.Controls.Add(Separator());
        list.Controls.Add(Row("右に90°回転", (_, _) => _canvas.RotateSelected(90)));
        list.Controls.Add(Row("左に90°回転", (_, _) => _canvas.RotateSelected(-90)));
        list.Controls.Add(Row("左右反転", (_, _) => _canvas.FlipSelected(true)));
        list.Controls.Add(Row("上下反転", (_, _) => _canvas.FlipSelected(false)));
        list.Controls.Add(Separator());
        list.Controls.Add(Row("最前面へ", (_, _) => _canvas.ReorderSelected(+1, true)));
        list.Controls.Add(Row("最背面へ", (_, _) => _canvas.ReorderSelected(-1, true)));
        list.Controls.Add(Separator());
        list.Controls.Add(Row("トリミング解除", (_, _) => _canvas.ResetCropSelected()));

        _itemPanel.Controls.Add(list);
    }

    // 選択状態に応じてパネルの表示を更新
    private void UpdateItemPanel()
    {
        // ドラッグ・パン中は非表示にして残像 (毎フレームの移動再描画による尾引き) を防ぐ。
        // 操作が終わると CanvasUpdated 経由で再表示される
        bool visible = !_viewerMode && !_canvas.ReadOnlyView && _canvas.Selected != null && !_uiHidden && !_canvas.IsInteracting;

        if (visible) UpdateItemPanelPosition();
        if (_itemPanel.Visible != visible)
        {
            _itemPanel.Visible = visible;
            if (visible) _itemPanel.BringToFront();
        }
    }

    // 選択画像の右横 (入らなければ左横) に追従配置する
    private void UpdateItemPanelPosition()
    {
        var sel = _canvas.Selected;
        if (sel == null) return;

        var world = sel.GetWorldBounds();
        var zoom = _canvas.Zoom;
        var scroll = _canvas.ScrollOffset;

        // ワールド座標 → キャンバスクライアント座標
        var left = world.Left * zoom - scroll.X;
        var right = world.Right * zoom - scroll.X;
        var bottom = world.Bottom * zoom - scroll.Y;

        const int gap = 16;
        int x = (int)(right + gap);
        var rightLimit = _rightPanel.Visible ? _rightPanel.Left - 8 : _canvas.ClientSize.Width - 12;
        if (x + ItemPanelWidth > rightLimit)
        {
            x = (int)(left - gap - ItemPanelWidth);
        }
        x = Math.Clamp(x, 12, Math.Max(12, _canvas.ClientSize.Width - ItemPanelWidth - 12));

        int y = (int)(bottom - ItemPanelHeight + 24);
        y = Math.Clamp(y, 12, Math.Max(12, _canvas.ClientSize.Height - ItemPanelHeight - 12));

        var location = new Point(x, y);
        if (_itemPanel.Location != location)
        {
            _itemPanel.Location = location;
            _itemPanel.BringToFront();
            _itemPanel.Invalidate();
        }
    }

    private void BuildOverlayFrame()
    {
        _overlayFrame.Height = 64;
        _overlayFrame.BackColor = Theme.Current.Surface;
        _overlayFrame.Padding = new Padding(12);

        _sidebarOverlayBtn.Dock = DockStyle.Fill;
        _sidebarOverlayBtn.Font = new Font(Font.FontFamily, 10f, FontStyle.Bold);
        _sidebarOverlayBtn.ForeColor = Color.White;
        _sidebarOverlayBtn.Cursor = Cursors.Hand;
        _sidebarOverlayBtn.CornerRadius = 20;

        UpdateSidebarOverlayButtonState(false);

        _sidebarOverlayBtn.Click += (_, _) => ToggleOverlayMode(_overlayForm == null);

        // オーバーレイ設定を開く歯車ボタン (⚙︎ = テキスト表示形式のクラシックな歯車)
        var gearBtn = new RoundedFlatButton
        {
            Text = "⚙︎",
            Dock = DockStyle.Right,
            Width = 40,
            ForeColor = Theme.Current.TextPrimary,
            Font = new Font("Segoe UI Symbol", 14f),
            Cursor = Cursors.Hand,
            CornerRadius = 12,
            BaseColor = Theme.Current.ButtonBg,
        };
        new ToolTip().SetToolTip(gearBtn, Loc.T("オーバーレイ設定"));
        gearBtn.Click += (_, _) => ToggleOverlaySettingsPanel();

        var spacer = new Panel { Dock = DockStyle.Right, Width = 8, BackColor = Theme.Current.Surface };

        _overlayFrame.Controls.Add(_sidebarOverlayBtn);
        _overlayFrame.Controls.Add(spacer);
        _overlayFrame.Controls.Add(gearBtn);

        BuildOverlaySettingsPanel();
    }

    // ===== オーバーレイ設定パネル (歯車で開閉。ファイル選択画面の上に重ねる) =====

    private const int OverlaySettingsHeight = 192;

    private void BuildOverlaySettingsPanel()
    {
        var t = Theme.Current;
        _overlaySettingsPanel.BackColor = t.Surface;
        _overlaySettingsPanel.Padding = new Padding(14, 8, 14, 10);
        _overlaySettingsPanel.Visible = false;

        // ヘッダー行 (タイトル + 閉じるボタン)
        var header = new Panel { Dock = DockStyle.Top, Height = 30, BackColor = t.Surface };
        var gearIcon = new RoundedFlatButton
        {
            Text = "⚙︎",
            Size = new Size(28, 28),
            Location = new Point(0, 1),
            ForeColor = t.TextPrimary,
            Font = new Font("Segoe UI Symbol", 12f),
            CornerRadius = 8,
            BaseColor = t.ButtonBg,
            TabStop = false,
        };
        var title = new Label
        {
            Text = Loc.T("オーバーレイ設定"),
            Dock = DockStyle.Fill,
            Padding = new Padding(36, 0, 0, 0),
            TextAlign = ContentAlignment.MiddleLeft,
            Font = new Font(Font.FontFamily, 9.5f, FontStyle.Bold),
            ForeColor = t.TextPrimary,
            BackColor = t.Surface,
        };
        var closeBtn = new RoundedFlatButton
        {
            Text = "✕",
            Dock = DockStyle.Right,
            Width = 28,
            ForeColor = t.TextSecondary,
            CornerRadius = 8,
            HoverColor = Color.FromArgb(232, 17, 35),
        };
        closeBtn.Click += (_, _) => ToggleOverlaySettingsPanel();
        header.Controls.Add(title);
        header.Controls.Add(gearIcon);
        header.Controls.Add(closeBtn);

        _ovlClickThroughCheck = new CheckBox
        {
            Text = Loc.T("クリック透過"),
            Dock = DockStyle.Top,
            Height = 32,
            ForeColor = t.TextPrimary,
            BackColor = t.Surface,
            TabStop = false,
        };
        _ovlClickThroughCheck.CheckedChanged += (_, _) =>
        {
            if (_syncingOverlayPanel) return;
            SetOverlayClickThrough(_ovlClickThroughCheck.Checked);
        };

        _ovlOpacityLabel = new Label
        {
            Text = $"{Loc.T("オーバーレイ透過率")}: 100%",
            Dock = DockStyle.Top,
            Height = 22,
            ForeColor = t.TextSecondary,
            BackColor = t.Surface,
        };
        _ovlOpacitySlider = new TrackBar
        {
            Dock = DockStyle.Top,
            Height = 34,
            Minimum = 20,
            Maximum = 100,
            Value = 100,
            TickStyle = TickStyle.None,
            BackColor = t.Surface,
        };
        _ovlOpacitySlider.Scroll += (_, _) =>
        {
            if (_syncingOverlayPanel) return;
            SetOverlayOpacity(_ovlOpacitySlider.Value / 100f);
        };

        // Dock=Top は後から追加したものが上に積まれるため逆順で追加する
        _overlaySettingsPanel.Controls.Add(_ovlOpacitySlider);
        _overlaySettingsPanel.Controls.Add(_ovlOpacityLabel);
        _overlaySettingsPanel.Controls.Add(_ovlClickThroughCheck);
        _overlaySettingsPanel.Controls.Add(header);
    }

    private void ToggleOverlaySettingsPanel()
    {
        bool show = !_overlaySettingsPanel.Visible;
        if (show)
        {
            SyncOverlaySettingsPanel();
            UpdateSidebarBounds();
            _overlaySettingsPanel.Visible = true;
            _overlaySettingsPanel.BringToFront();
        }
        else
        {
            _overlaySettingsPanel.Visible = false;
        }
    }

    // 現在のオーバーレイ状態をパネルのコントロールへ反映
    private void SyncOverlaySettingsPanel()
    {
        _syncingOverlayPanel = true;
        try
        {
            if (_ovlClickThroughCheck != null) _ovlClickThroughCheck.Checked = _overlayClickThrough;
            if (_ovlOpacitySlider != null) _ovlOpacitySlider.Value = (int)(_overlayOpacity * 100);
            if (_ovlOpacityLabel != null) _ovlOpacityLabel.Text = $"{Loc.T("オーバーレイ透過率")}: {(int)(_overlayOpacity * 100)}%";
        }
        finally
        {
            _syncingOverlayPanel = false;
        }
    }

    private void UpdateSidebarBounds()
    {
        if (_canvas == null || _rightPanel == null || _overlayFrame == null) return;
        int margin = 12;
        int overlayHeight = 64;
        int gap = 12;

        int rightX = _canvas.ClientSize.Width - margin - _sidebarWidth;

        _overlayFrame.Size = new Size(_sidebarWidth, overlayHeight);
        _overlayFrame.Location = new Point(rightX, _canvas.ClientSize.Height - margin - overlayHeight);

        int rightTop = margin;
        int rightHeight = _overlayFrame.Top - gap - rightTop;
        _rightPanel.Size = new Size(_sidebarWidth, Math.Max(100, rightHeight));
        _rightPanel.Location = new Point(rightX, rightTop);

        // オーバーレイ設定パネルはファイル選択画面の下部に重ねる
        _overlaySettingsPanel.Size = new Size(_sidebarWidth, OverlaySettingsHeight);
        _overlaySettingsPanel.Location = new Point(rightX, _overlayFrame.Top - gap - OverlaySettingsHeight);

        UpdateItemPanelPosition();
    }

    // ===== キャンバスイベント =====

    private void WireCanvasEvents()
    {
        _canvas.ZoomChanged += (_, _) => { UpdateZoomText(); UpdateItemPanelPosition(); };
        _canvas.MouseDown += (_, _) => { if (_uiHidden) RestoreUi(); };
        _canvas.SelectionChanged += (_, _) => { SyncMenuState(); UpdateItemPanel(); };
        // パンやドラッグ移動に合わせて選択メニューを追従させる
        // 操作中の非表示⇔操作後の再表示も含めて毎回評価する
        _canvas.CanvasUpdated += (_, _) => UpdateItemPanel();
        _canvas.ItemContextMenuRequested += Canvas_ItemContextMenuRequested;
    }

    private void Canvas_ItemContextMenuRequested(object? sender, ItemContextMenuEventArgs e)
    {
        var menu = new ContextMenuStrip { Renderer = new ThemedToolStripRenderer(), BackColor = Theme.Current.Surface, ForeColor = Theme.Current.TextPrimary };
        RoundDropDownCorners(menu);
        menu.Items.Add("複製", null, (_, _) => _canvas.DuplicateSelected());
        menu.Items.Add("削除", null, (_, _) => _canvas.DeleteSelected());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("右に90°回転", null, (_, _) => _canvas.RotateSelected(90));
        menu.Items.Add("左に90°回転", null, (_, _) => _canvas.RotateSelected(-90));
        menu.Items.Add("左右反転", null, (_, _) => _canvas.FlipSelected(true));
        menu.Items.Add("上下反転", null, (_, _) => _canvas.FlipSelected(false));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("最前面へ", null, (_, _) => _canvas.ReorderSelected(+1, true));
        menu.Items.Add("最背面へ", null, (_, _) => _canvas.ReorderSelected(-1, true));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("トリミング解除", null, (_, _) => _canvas.ResetCropSelected());
        menu.Show(_canvas, e.Location);
    }

    private void WireDragDrop()
    {
        async void Handle(DragEventArgs e)
        {
            if (e.Data == null) return;
            await OpenDroppedDataAsync(e.Data);
        }

        foreach (var target in new Control[] { this, _canvas })
        {
            target.AllowDrop = true;
            target.DragEnter += (_, e) =>
                e.Effect = e.Data != null && HasImportableDrop(e.Data) ? DragDropEffects.Copy : DragDropEffects.None;
            target.DragDrop += (_, e) => Handle(e);
        }
    }

    private static bool HasImportableDrop(IDataObject data) =>
        data.GetDataPresent(DataFormats.FileDrop)
        || data.GetDataPresent(DataFormats.Bitmap)
        || data.GetDataPresent(DataFormats.UnicodeText)
        || data.GetDataPresent(DataFormats.Text)
        || data.GetDataPresent(DataFormats.Html)
        || data.GetDataPresent("UniformResourceLocatorW")
        || data.GetDataPresent("UniformResourceLocator");

    private async Task OpenDroppedDataAsync(IDataObject data)
    {
        if (data.GetData(DataFormats.FileDrop) is string[] files)
        {
            await OpenInputsAsync(files);
            return;
        }

        if (data.GetData(DataFormats.Bitmap) is Bitmap bmp)
        {
            var path = SaveImportedBitmap(bmp);
            _canvas.AddImage(path);
            return;
        }

        var urls = ExtractUrlsFromData(data).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        foreach (var url in urls)
        {
            if (Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https"
                && await AddImageFromUrlAsync(uri, showError: false))
            {
                break;
            }
        }
    }

    private void AddImagesFromFolder(string folder)
    {
        try
        {
            foreach (var file in Directory.EnumerateFiles(folder)
                .Where(ImageDecoder.IsSupported)
                .OrderBy(Path.GetFileName)
                .Take(20)) // 一度に大量追加を防ぐ
            {
                _canvas.AddImage(file);
            }
        }
        catch { /* アクセス拒否等は無視 */ }
    }

    private async Task OpenInputsAsync(IEnumerable<string> inputs)
    {
        foreach (var raw in inputs.Select(s => s.Trim().Trim('"')).Where(s => !string.IsNullOrWhiteSpace(s)).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var input = raw;

            if (Uri.TryCreate(input, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https")
            {
                await AddImageFromUrlAsync(uri);
            }
            else if (File.Exists(input))
            {
                OpenPath(input);
            }
            else if (Directory.Exists(input))
            {
                AddImagesFromFolder(input);
            }
        }
    }

    private void OpenPath(string fileName)
    {
        var ext = Path.GetExtension(fileName);
        if (ext.Equals(".mics", StringComparison.OrdinalIgnoreCase))
        {
            LoadSessionPackageFile(fileName);
        }
        else if (ext.Equals(".micl", StringComparison.OrdinalIgnoreCase) || ext.Equals(".json", StringComparison.OrdinalIgnoreCase))
        {
            LoadLayoutFile(fileName);
        }
        else if (ImageDecoder.IsSupported(fileName))
        {
            _canvas.AddImage(fileName);
        }
        else
        {
            MessageBox.Show(this, Loc.T("対応しているセッション、キャンバス、画像ファイルではありません。"), Loc.T("開けません"), MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
    }

    private async Task OpenImageUrlDialogAsync()
    {
        using var dlg = new TextInputForm(Loc.T("URLから画像を開く"), Loc.T("画像URL:"), "");
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        if (!Uri.TryCreate(dlg.Value, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https"))
        {
            MessageBox.Show(this, Loc.T("http または https の画像URLを入力してください。"), Loc.T("URLを開けません"), MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        await AddImageFromUrlAsync(uri);
    }

    private async Task<bool> AddImageFromUrlAsync(Uri uri, bool showError = true)
    {
        try
        {
            var path = await DownloadImageAsync(uri);
            _canvas.AddImage(path);
            return true;
        }
        catch (Exception ex)
        {
            if (showError) MessageBox.Show(this, ex.Message, Loc.T("URL画像を読み込めません"), MessageBoxButtons.OK, MessageBoxIcon.Error);
            return false;
        }
    }

    private static async Task<string> DownloadImageAsync(Uri uri)
    {
        using var response = await Http.GetAsync(uri);
        response.EnsureSuccessStatusCode();
        var mediaType = response.Content.Headers.ContentType?.MediaType ?? "";
        var ext = ExtensionFromContentType(mediaType);
        if (string.IsNullOrEmpty(ext)) ext = Path.GetExtension(uri.LocalPath);
        if (!ImageDecoder.SupportedExtensions.Contains(ext, StringComparer.OrdinalIgnoreCase)) ext = ".png";

        var dir = Path.Combine(SessionStore.Directory, "imports");
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, $"{DateTime.Now:yyyyMMdd_HHmmssfff}_{Guid.NewGuid():N}{ext}");
        await using var src = await response.Content.ReadAsStreamAsync();
        await using var dst = File.Create(path);
        await src.CopyToAsync(dst);
        return path;
    }

    private static string ExtensionFromContentType(string mediaType) => mediaType.ToLowerInvariant() switch
    {
        "image/jpeg" => ".jpg",
        "image/png" => ".png",
        "image/gif" => ".gif",
        "image/bmp" => ".bmp",
        "image/tiff" => ".tif",
        "image/webp" => ".webp",
        "image/heic" => ".heic",
        "image/heif" => ".heif",
        "image/avif" => ".avif",
        _ => "",
    };

    private static string SaveImportedBitmap(Bitmap bitmap)
    {
        var dir = Path.Combine(SessionStore.Directory, "imports");
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, $"{DateTime.Now:yyyyMMdd_HHmmssfff}_{Guid.NewGuid():N}.png");
        bitmap.Save(path, System.Drawing.Imaging.ImageFormat.Png);
        return path;
    }

    private static IEnumerable<string> ExtractUrlsFromData(IDataObject data)
    {
        foreach (var format in new[] { "UniformResourceLocatorW", "UniformResourceLocator", DataFormats.UnicodeText, DataFormats.Text, DataFormats.Html })
        {
            if (!data.GetDataPresent(format)) continue;
            if (data.GetData(format) is not string text) continue;
            foreach (Match match in Regex.Matches(text, @"https?://[^\s""'<>]+", RegexOptions.IgnoreCase))
            {
                yield return match.Value;
            }
        }
    }

    // ===== ファイル操作 =====

    private const string SessionFileFilter = "セッションファイル (*.mics)|*.mics|すべてのファイル (*.*)|*.*";
    private enum SessionDecision { Save, Discard, Cancel }

    private SessionDecision ShowSessionDecision(string title, string message, string discardText)
    {
        var t = Theme.Current;
        var result = SessionDecision.Cancel;
        using var form = new Form
        {
            Text = title,
            Width = 460,
            Height = 190,
            StartPosition = FormStartPosition.CenterParent,
            FormBorderStyle = FormBorderStyle.FixedDialog,
            MaximizeBox = false,
            MinimizeBox = false,
            BackColor = t.Background,
            ForeColor = t.TextPrimary,
            Font = Font,
        };
        var label = new Label
        {
            Text = message,
            Dock = DockStyle.Fill,
            Padding = new Padding(18, 18, 18, 8),
            ForeColor = t.TextPrimary,
            BackColor = t.Background,
        };
        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            Height = 52,
            FlowDirection = FlowDirection.RightToLeft,
            Padding = new Padding(0, 8, 12, 10),
            BackColor = t.Background,
        };
        Button Make(string text, SessionDecision value)
        {
            var btn = new RoundedFlatButton
            {
                Text = text,
                Width = 92,
                Height = 30,
                ForeColor = t.TextPrimary,
                BaseColor = t.ButtonBg,
                CornerRadius = 8,
            };
            btn.Click += (_, _) => { result = value; form.Close(); };
            return btn;
        }
        buttons.Controls.Add(Make(Loc.T("キャンセル"), SessionDecision.Cancel));
        buttons.Controls.Add(Make(discardText, SessionDecision.Discard));
        buttons.Controls.Add(Make(Loc.T("保存"), SessionDecision.Save));
        form.Controls.Add(label);
        form.Controls.Add(buttons);
        form.ShowDialog(this);
        return result;
    }

    private void NewSession()
    {
        var decision = ShowSessionDecision(Loc.T("新規セッション"),
            Loc.T("現在のセッションの状態が失われます。\n外部保存していないキャンバスは復元できません。"),
            Loc.T("新規"));
        if (decision == SessionDecision.Cancel) return;
        if (decision == SessionDecision.Save && !SaveSessionToFile(showDone: false)) return;

        ClearDocuments();
        _sessionFilePath = null;
        UpdateSessionTitle();
        AddDocument(new CanvasDocument(), select: true);
        SaveSession();
    }

    private bool SaveSessionAs(bool showDone = true)
    {
        using var dlg = new SaveFileDialog
        {
            Filter = SessionFileFilter,
            FileName = "session.mics",
            DefaultExt = "mics",
            AddExtension = true,
        };
        if (dlg.ShowDialog(this) != DialogResult.OK) return false;

        try
        {
            SaveSessionPackage(dlg.FileName);
            _sessionFilePath = dlg.FileName;
            UpdateSessionTitle();
            if (showDone) MessageBox.Show(this, Loc.T("セッションを保存しました。"), Loc.T("完了"), MessageBoxButtons.OK, MessageBoxIcon.Information);
            return true;
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, Loc.T("保存失敗"), MessageBoxButtons.OK, MessageBoxIcon.Error);
            return false;
        }
    }

    private bool SaveSessionToFile(bool showDone = true)
    {
        if (string.IsNullOrEmpty(_sessionFilePath))
        {
            return SaveSessionAs(showDone);
        }

        try
        {
            SaveSessionPackage(_sessionFilePath);
            if (showDone) MessageBox.Show(this, Loc.T("セッションを保存しました。"), Loc.T("完了"), MessageBoxButtons.OK, MessageBoxIcon.Information);
            return true;
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, Loc.T("保存失敗"), MessageBoxButtons.OK, MessageBoxIcon.Error);
            return false;
        }
    }

    private void OverwriteSession() => SaveSessionToFile();

    private void SaveSessionPackage(string fileName)
    {
        var data = CreateSessionData();
        var imageMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var dir = Path.GetDirectoryName(fileName);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        var tempFile = fileName + ".tmp";
        if (File.Exists(tempFile)) File.Delete(tempFile);

        using (var zip = ZipFile.Open(tempFile, ZipArchiveMode.Create))
        {
            for (int i = 0; i < data.Tabs.Count; i++)
            {
                var tab = data.Tabs[i];
                var items = new List<LayoutItemDto>(tab.Items.Count);
                for (int j = 0; j < tab.Items.Count; j++)
                {
                    var item = tab.Items[j];
                    var path = item.Path;
                    if (File.Exists(path))
                    {
                        if (!imageMap.TryGetValue(path, out var embeddedPath))
                        {
                            var ext = Path.GetExtension(path);
                            if (string.IsNullOrWhiteSpace(ext)) ext = ".img";
                            embeddedPath = $"assets/{i:D3}_{j:D4}{ext}";
                            zip.CreateEntryFromFile(path, embeddedPath, CompressionLevel.NoCompression);
                            imageMap[path] = embeddedPath;
                        }
                        path = embeddedPath;
                    }
                    items.Add(item with { Path = path });
                }
                data.Tabs[i] = tab with { Items = items };
            }

            data.TabFilePaths = Enumerable.Repeat<string?>(null, data.Tabs.Count).ToList();
            var entry = zip.CreateEntry("session.json", CompressionLevel.Optimal);
            using var stream = entry.Open();
            using var writer = new StreamWriter(stream, Encoding.UTF8);
            writer.Write(SessionStore.Serialize(data));
        }

        if (File.Exists(fileName)) File.Delete(fileName);
        File.Move(tempFile, fileName);
    }

    // 共有用エクスポート: プライバシー処理を通した画像同梱パッケージを作成する
    private void ExportShare()
    {
        if (_docs.Count == 0 || _docs.All(d => d.Items.Count == 0))
        {
            MessageBox.Show(this, Loc.T("共有できる画像がキャンバスにありません。"), Loc.T("共有用にエクスポート"),
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        using var optDlg = new ShareExportForm();
        if (optDlg.ShowDialog(this) != DialogResult.OK) return;

        using var dlg = new SaveFileDialog
        {
            Filter = SessionFileFilter,
            FileName = "shared_canvas.mics",
            DefaultExt = "mics",
            AddExtension = true,
        };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;

        try
        {
            var result = ShareExporter.Export(_docs, _activeDocIndex, optDlg.Options, dlg.FileName);
            var msg = string.Format(
                Loc.T("共有用ファイルを書き出しました。\nキャンバス: {0} / 同梱画像: {1} (うちメタデータ除去 {2})"),
                result.CanvasCount, result.ItemCount, result.ReencodedCount);
            if (result.HiddenSkipped > 0)
                msg += "\n" + string.Format(Loc.T("除外した非表示レイヤー: {0}"), result.HiddenSkipped);
            if (result.MissingCount > 0)
                msg += "\n" + string.Format(Loc.T("元ファイルが見つからず同梱できなかった画像: {0}"), result.MissingCount);
            MessageBox.Show(this, msg, Loc.T("共有用にエクスポート"), MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, Loc.T("出力失敗"), MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void ExportCurrentLayoutSettings()
    {
        var doc = ActiveDoc;
        if (doc == null) return;

        using var dlg = new SaveFileDialog
        {
            Filter = LayoutSerializer.FileFilter,
            FileName = $"{doc.Name}.{LayoutSerializer.DefaultExtension}",
            DefaultExt = LayoutSerializer.DefaultExtension,
            AddExtension = true,
        };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        try
        {
            File.WriteAllText(dlg.FileName, LayoutSerializer.Serialize(doc), Encoding.UTF8);
            MessageBox.Show(this, Loc.T("キャンバスを保存しました。"), Loc.T("完了"), MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, Loc.T("出力失敗"), MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void OpenLayout()
    {
        using var dlg = new OpenFileDialog { Filter = LayoutSerializer.FileFilter };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;

        try
        {
            LoadLayoutFile(dlg.FileName);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, Loc.T("読込失敗"), MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void OpenFile()
    {
        var imageExts = string.Join(";", ImageDecoder.SupportedExtensions.Select(e => "*" + e));
        using var dlg = new OpenFileDialog
        {
            Filter = $"対応ファイル (*.mics;*.micl;*.json;{imageExts})|*.mics;*.micl;*.json;{imageExts}|セッションファイル (*.mics)|*.mics|キャンバスファイル (*.micl;*.json)|*.micl;*.json|画像ファイル ({imageExts})|{imageExts}|すべてのファイル (*.*)|*.*",
        };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;

        try
        {
            OpenPath(dlg.FileName);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, Loc.T("読込失敗"), MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void LoadLayoutFile(string fileName)
    {
        var doc = LayoutSerializer.Load(fileName);
        AddDocument(doc, select: true);

        if (doc.Items.Any(i => i.IsPlaceholder))
        {
            MessageBox.Show(this, Loc.T("一部の画像ファイルが見つからなかったため、プレースホルダで表示しています。"),
                Loc.T("キャンバス読込"), MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
    }

    private void LoadSessionPackageFile(string fileName)
    {
        if (MessageBox.Show(this,
                Loc.T("現在のセッションの状態が失われます。\n外部保存していない内容は復元できません。\n\nこのセッションを開きますか？"),
                Loc.T("セッションを開く"), MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;

        var session = LoadSessionPackage(fileName);
        ClearDocuments();

        _canvas.InsertNaturalSize = session.InsertNaturalSize;
        _overlayOpacity = Math.Clamp(session.OverlayOpacity, 0.2f, 1f);
        _overlayClickThrough = session.OverlayClickThrough;
        _sidebarView = session.SidebarView;

        for (int i = 0; i < session.Tabs.Count; i++)
        {
            try
            {
                var doc = LayoutSerializer.FromDto(session.Tabs[i], i < session.TabFilePaths.Count ? session.TabFilePaths[i] : null);
                AddDocument(doc, select: false);
            }
            catch
            {
                // 個別キャンバスの復元失敗は無視
            }
        }

        if (_docs.Count == 0) AddDocument(new CanvasDocument(), select: true);
        else SelectDocument(Math.Clamp(session.ActiveTab, 0, _docs.Count - 1));

        if (_naturalMi != null) _naturalMi.Checked = _canvas.InsertNaturalSize;
        SetOverlayOpacity(_overlayOpacity);
        if (_clickThroughMi != null) _clickThroughMi.Checked = _overlayClickThrough;
        SetSidebarView(_sidebarView);
        _sessionFilePath = fileName;
        UpdateSessionTitle();
        SaveSession();
    }

    private static SessionData LoadSessionPackage(string fileName)
    {
        using var zip = ZipFile.OpenRead(fileName);

        // 共有で受け取ったファイルは信頼できない入力として扱う (zip爆弾・不正ファイル対策)
        if (zip.Entries.Count > SharePackageSecurity.MaxEntries)
            throw new InvalidDataException(Loc.T("セッションファイルのエントリ数が多すぎます。"));

        var sessionEntry = zip.GetEntry("session.json") ?? throw new InvalidDataException("セッション情報が見つかりません。");
        using var sessionStream = sessionEntry.Open();
        using var reader = new StreamReader(sessionStream, Encoding.UTF8);
        var data = SessionStore.Deserialize(reader.ReadToEnd()) ?? throw new InvalidDataException("セッション情報を読み込めません。");

        var extractDir = Path.Combine(SessionStore.Directory, "session-assets", $"{Path.GetFileNameWithoutExtension(fileName)}_{Guid.NewGuid():N}");
        var extractRoot = Path.GetFullPath(extractDir + Path.DirectorySeparatorChar);
        Directory.CreateDirectory(extractRoot);

        long totalBytes = 0;
        foreach (var entry in zip.Entries.Where(e => e.FullName.StartsWith("assets/", StringComparison.OrdinalIgnoreCase) && !e.FullName.EndsWith("/")))
        {
            // 画像以外 (実行ファイル等) は展開しない
            if (!SharePackageSecurity.IsAllowedAssetName(entry.Name)) continue;

            var target = Path.GetFullPath(Path.Combine(extractRoot, entry.FullName.Replace('/', Path.DirectorySeparatorChar)));
            if (!target.StartsWith(extractRoot, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("セッションファイルの画像パスが不正です。");
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            SharePackageSecurity.ExtractWithLimit(entry, target, ref totalBytes);
        }

        for (int i = 0; i < data.Tabs.Count; i++)
        {
            var tab = data.Tabs[i];
            var items = tab.Items.Select(item =>
            {
                if (Path.IsPathRooted(item.Path)) return item;
                var path = Path.GetFullPath(Path.Combine(extractRoot, item.Path.Replace('/', Path.DirectorySeparatorChar)));
                return item with { Path = path };
            }).ToList();
            data.Tabs[i] = tab with { Items = items };
        }

        data.TabFilePaths = Enumerable.Repeat<string?>(null, data.Tabs.Count).ToList();
        return data;
    }

    private void ExportPng()
    {
        using var dlg = new SaveFileDialog
        {
            Filter = "PNG画像 (*.png)|*.png",
            FileName = "canvas_export.png",
            AddExtension = true,
            DefaultExt = "png",
        };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        try
        {
            _canvas.ExportPng(dlg.FileName);
            MessageBox.Show(this, Loc.T("キャンバスを画像として出力しました。"), Loc.T("完了"), MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, Loc.T("書き出し失敗"), MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void ClearAllWithConfirm()
    {
        if (_canvas.Items.Count == 0) return;
        if (MessageBox.Show(this, string.Format(Loc.T("キャンバス内の画像 {0} 件をすべて削除しますか？\n(Ctrl+Zで元に戻せます)"), _canvas.Items.Count),
                Loc.T("一括削除"), MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
        _canvas.ClearAll();
    }

    // ===== オーバーレイ =====

    private void ToggleOverlayMode(bool enabled)
    {
        if (enabled)
        {
            if (_overlayForm != null)
            {
                _overlayForm.Close();
                _overlayForm = null;
            }

            var clientRect = _canvas.RectangleToScreen(_canvas.ClientRectangle);
            _overlayForm = new OverlayForm(_canvas, _overlayClickThrough, _overlayOpacity, OverlayAnimations.Parse(_overlayAnimation))
            {
                StartPosition = FormStartPosition.Manual,
                Location = new Point(clientRect.Location.X + 20, clientRect.Location.Y + 20),
                Width = clientRect.Width,
                Height = clientRect.Height,
            };

            _overlayForm.FormClosed += (_, _) =>
            {
                _canvas.CanvasUpdated -= Canvas_CanvasUpdated;
                _overlayForm = null;
                UpdateOverlayButtonsState(false);
            };

            _canvas.CanvasUpdated += Canvas_CanvasUpdated;
            _overlayForm.Show();
        }
        else
        {
            if (_overlayForm != null)
            {
                _canvas.CanvasUpdated -= Canvas_CanvasUpdated;
                _overlayForm.Close();
                _overlayForm = null;
            }
        }
        UpdateOverlayButtonsState(enabled);
    }

    private void Canvas_CanvasUpdated(object? sender, EventArgs e) => _overlayForm?.Invalidate();

    private void UpdateOverlayButtonsState(bool active)
    {
        if (_overlayActiveMi != null) _overlayActiveMi.Checked = active;
        UpdateSidebarOverlayButtonState(active);
        if (_overlaySettingsPanel.Visible) SyncOverlaySettingsPanel();
    }

    private void UpdateSidebarOverlayButtonState(bool active)
    {
        if (active)
        {
            _sidebarOverlayBtn.GradientStart = Color.FromArgb(255, 65, 108);
            _sidebarOverlayBtn.GradientEnd = Color.FromArgb(255, 75, 43);
            _sidebarOverlayBtn.Text = Loc.T("オーバーレイ無効化") + " ■";
        }
        else
        {
            _sidebarOverlayBtn.GradientStart = Color.FromArgb(0, 242, 96);
            _sidebarOverlayBtn.GradientEnd = Color.FromArgb(5, 117, 230);
            _sidebarOverlayBtn.Text = Loc.T("オーバーレイ有効化") + " ▶";
        }
        _sidebarOverlayBtn.Invalidate();
    }

    private void SetOverlayOpacity(float value)
    {
        _overlayOpacity = Math.Clamp(value, 0.2f, 1f);
        if (_opacityTrack != null) _opacityTrack.TrackBar.Value = (int)(_overlayOpacity * 100);
        if (_opacityMenuLabel != null) _opacityMenuLabel.Text = $"{Loc.T("オーバーレイ透過率")}: {(int)(_overlayOpacity * 100)}%";
        if (_overlayForm != null) _overlayForm.Opacity = _overlayOpacity;
        if (_overlaySettingsPanel.Visible) SyncOverlaySettingsPanel();
    }

    private void SetOverlayClickThrough(bool value)
    {
        _overlayClickThrough = value;
        if (_clickThroughMi != null) _clickThroughMi.Checked = value;
        if (_overlayForm != null) _overlayForm.ClickThrough = value;
        if (_overlaySettingsPanel.Visible) SyncOverlaySettingsPanel();
    }

    // ===== グローバルホットキー =====

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        UpdateWindowRegion();
        RegisterHotKey(Handle, HotkeyToggleOverlay, MOD_CONTROL | MOD_ALT, (uint)Keys.H);
        RegisterHotKey(Handle, HotkeyClickThrough, MOD_CONTROL | MOD_ALT, (uint)Keys.T);
        RegisterHotKey(Handle, HotkeyOpacityDown, MOD_CONTROL | MOD_ALT, (uint)Keys.Next);
        RegisterHotKey(Handle, HotkeyOpacityUp, MOD_CONTROL | MOD_ALT, (uint)Keys.Prior);
        RegisterHotKey(Handle, HotkeyCanvasNext, MOD_CONTROL | MOD_ALT, (uint)Keys.Right);
        RegisterHotKey(Handle, HotkeyCanvasPrev, MOD_CONTROL | MOD_ALT, (uint)Keys.Left);
    }

    protected override void OnHandleDestroyed(EventArgs e)
    {
        UnregisterHotKey(Handle, HotkeyToggleOverlay);
        UnregisterHotKey(Handle, HotkeyClickThrough);
        UnregisterHotKey(Handle, HotkeyOpacityDown);
        UnregisterHotKey(Handle, HotkeyOpacityUp);
        UnregisterHotKey(Handle, HotkeyCanvasNext);
        UnregisterHotKey(Handle, HotkeyCanvasPrev);
        base.OnHandleDestroyed(e);
    }

    // ===== ウィンドウ枠 =====

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ClassStyle |= 0x00020000; // CS_DROPSHADOW
            return cp;
        }
    }

    protected override void OnSizeChanged(EventArgs e)
    {
        base.OnSizeChanged(e);
        UpdateWindowRegion();
        PositionSessionTitle();
        UpdateCaptionGlyphs();
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        UpdateWindowRegion();
    }

    private void UpdateWindowRegion()
    {
        // 全画面表示中は角丸にせず画面全体を覆う
        if (_viewerFullscreen)
        {
            Region = null;
            UpdateSidebarBounds();
            return;
        }
        if (Width > 0 && Height > 0)
        {
            using var path = CreateRoundedRectPath(ClientRectangle, CornerRadius);
            Region = new Region(path);
        }
        UpdateSidebarBounds();
    }

    protected override void WndProc(ref Message m)
    {
        const int WM_NCHITTEST = 0x0084;
        const int WM_MOUSEACTIVATE = 0x0021;
        const int MA_ACTIVATE = 1;
        const int gripSize = 8;

        if (m.Msg == WM_MOUSEACTIVATE)
        {
            m.Result = MA_ACTIVATE;
            return;
        }

        if (m.Msg == WM_HOTKEY)
        {
            switch (m.WParam.ToInt32())
            {
                case HotkeyToggleOverlay:
                    ToggleOverlayMode(_overlayForm == null);
                    return;
                case HotkeyClickThrough:
                    SetOverlayClickThrough(!_overlayClickThrough);
                    return;
                case HotkeyOpacityDown:
                    SetOverlayOpacity(_overlayOpacity - 0.1f);
                    return;
                case HotkeyOpacityUp:
                    SetOverlayOpacity(_overlayOpacity + 0.1f);
                    return;
                case HotkeyCanvasNext:
                    SelectNextCanvas(+1);
                    return;
                case HotkeyCanvasPrev:
                    SelectNextCanvas(-1);
                    return;
            }
        }

        if (m.Msg == WM_NCHITTEST)
        {
            var pos = new Point(m.LParam.ToInt32() & 0xffff, m.LParam.ToInt32() >> 16);
            pos = PointToClient(pos);

            bool left = pos.X < gripSize;
            bool right = pos.X > ClientSize.Width - gripSize;
            bool top = pos.Y < gripSize;
            bool bottom = pos.Y > ClientSize.Height - gripSize;

            if (top && left) { m.Result = 13; return; }
            if (top && right) { m.Result = 14; return; }
            if (bottom && left) { m.Result = 16; return; }
            if (bottom && right) { m.Result = 17; return; }
            if (top) { m.Result = 12; return; }
            if (bottom) { m.Result = 15; return; }
            if (left) { m.Result = 10; return; }
            if (right) { m.Result = 11; return; }
        }
        base.WndProc(ref m);
    }

    // ===== ショートカット =====

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (_viewerMode)
        {
            switch (keyData & Keys.KeyCode)
            {
                case Keys.Left:
                    NavigateViewerImage(-1);
                    return true;
                case Keys.Right:
                    NavigateViewerImage(+1);
                    return true;
                case Keys.Escape:
                    if (_viewerFullscreen)
                    {
                        ExitViewerFullscreen();
                        ShowViewerChrome();
                        return true;
                    }
                    break;
            }
        }

        // パス欄などテキスト編集中は標準の編集キーを妨げない
        bool textEditing = ActiveControl is TextBoxBase || _pathBox.Focused;
        if (textEditing)
        {
            // 修飾なしキー (Delete, F11等) はテキストボックスに渡す
            if ((keyData & (Keys.Control | Keys.Alt)) == 0) return base.ProcessCmdKey(ref msg, keyData);
            if (keyData is (Keys.Control | Keys.Z) or (Keys.Control | Keys.Y) or (Keys.Control | Keys.C)
                or (Keys.Control | Keys.V) or (Keys.Control | Keys.X) or (Keys.Control | Keys.A)
                or (Keys.Control | Keys.Left) or (Keys.Control | Keys.Right)) // 単語単位のカーソル移動
            {
                return base.ProcessCmdKey(ref msg, keyData);
            }
        }

        // 固定ショートカット
        if (keyData == (Keys.Control | Keys.Shift | Keys.Z)) { _canvas.Redo(); return true; }
        if (keyData == (Keys.Control | Keys.Tab))
        {
            if (_docs.Count > 1) SelectDocument((_activeDocIndex + 1) % _docs.Count);
            return true;
        }

        // 設定で変更可能なショートカット
        if (_keyMap.TryGetAction(keyData, out var actionId))
        {
            // ビュアーモードでは表示系以外のショートカットを無効化
            if (_viewerMode && actionId is not ("view.zoomIn" or "view.zoomOut" or "view.zoom100" or "view.fitAll" or "view.hideUi" or "help.shortcuts"))
            {
                return true;
            }
            ExecuteAction(actionId);
            return true;
        }

        return base.ProcessCmdKey(ref msg, keyData);
    }

    private void ExecuteAction(string id)
    {
        switch (id)
        {
            case "file.save": OverwriteSession(); break;
            case "file.saveAs": SaveSessionAs(); break;
            case "file.open": OpenFile(); break;
            case "file.export": ExportPng(); break;
            case "file.newTab": AddDocument(new CanvasDocument(), true); break;
            case "file.closeTab": CloseDocument(_activeDocIndex); break;
            case "tab.next": SelectNextCanvas(+1); break;
            case "tab.prev": SelectNextCanvas(-1); break;
            case "edit.undo": _canvas.Undo(); break;
            case "edit.redo": _canvas.Redo(); break;
            case "edit.duplicate": _canvas.DuplicateSelected(); break;
            case "edit.rotateCw": _canvas.RotateSelected(90); break;
            case "edit.rotateCcw": _canvas.RotateSelected(-90); break;
            case "edit.flipH": _canvas.FlipSelected(true); break;
            case "edit.flipV": _canvas.FlipSelected(false); break;
            case "edit.delete": _canvas.DeleteSelected(); break;
            case "view.fitAll": _canvas.ZoomFitAll(); break;
            case "view.zoom100": _canvas.SetZoom(1.0f); break;
            case "view.zoomIn": _canvas.SetZoom(_canvas.Zoom * 1.15f); break;
            case "view.zoomOut": _canvas.SetZoom(_canvas.Zoom / 1.15f); break;
            case "view.hideUi": HideUi(); break;
            case "help.shortcuts": ShowHelp(); break;
        }
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (_pathBox.Focused) return;

        switch (e.KeyCode)
        {
            case Keys.Back:
                _canvas.DeleteSelected();
                e.Handled = true;
                break;

            case Keys.Escape:
                _canvas.Select(null);
                e.Handled = true;
                break;

            case Keys.Space:
                _canvas.SpacePanning = true;
                e.Handled = true;
                break;

            case Keys.OemOpenBrackets: // [
                _canvas.ReorderSelected(-1, e.Shift);
                e.Handled = true;
                break;

            case Keys.OemCloseBrackets: // ]
                _canvas.ReorderSelected(+1, e.Shift);
                e.Handled = true;
                break;

            case Keys.Left:
            case Keys.Right:
            case Keys.Up:
            case Keys.Down:
                if (_canvas.Focused && _canvas.Selected != null)
                {
                    float step = e.Shift ? 10f : 1f;
                    float dx = e.KeyCode == Keys.Left ? -step : e.KeyCode == Keys.Right ? step : 0f;
                    float dy = e.KeyCode == Keys.Up ? -step : e.KeyCode == Keys.Down ? step : 0f;
                    _canvas.NudgeSelected(dx, dy);
                    e.Handled = true;
                }
                break;
        }
    }

    protected override void OnKeyUp(KeyEventArgs e)
    {
        base.OnKeyUp(e);
        if (e.KeyCode == Keys.Space)
        {
            _canvas.SpacePanning = false;
        }
    }

    private void ShowHelp()
    {
        using var dlg = new ShortcutHelpForm(_keyMap);
        dlg.EditRequested += (_, _) => BeginInvoke(new Action(OpenSettings));
        dlg.ShowDialog(this);
    }

    private void ShowAbout()
    {
        MessageBox.Show(this,
            $"Multi Image Canvas\n{Loc.T("バージョン情報")}: {Loc.T("リリース前")}",
            Loc.T("バージョン情報"),
            MessageBoxButtons.OK,
            MessageBoxIcon.Information);
    }

    // ===== UI表示切替・テーマ =====

    private void HideUi()
    {
        if (_uiHidden) return;
        _menuBar.Visible = false;
        _viewerBar.Visible = false;
        _sessionTitleLabel.Visible = false;
        _docTabs.Visible = false;
        _rightPanel.Visible = false;
        _itemPanel.Visible = false;
        _overlaySettingsPanel.Visible = false;
        _overlayFrame.Visible = false;
        _uiHidden = true;
        _canvas.Focus();
    }

    private void RestoreUi()
    {
        if (!_uiHidden) return;
        if (_viewerMode)
        {
            // ビュアーモードは上部バーだけ戻す
            _viewerBar.Visible = true;
            _uiHidden = false;
            return;
        }
        _menuBar.Visible = true;
        _sessionTitleLabel.Visible = true;
        _docTabs.Visible = true;
        _rightPanel.Visible = true;
        _overlayFrame.Visible = true;
        _uiHidden = false;
        UpdateItemPanel();
    }

    // テーマ切替時に主要コントロールの色を再適用する
    private void ApplyThemeToControls()
    {
        var t = Theme.Current;
        BackColor = t.Background;
        ForeColor = t.TextPrimary;
        _sessionTitleLabel.BackColor = t.ToolbarBg;
        _sessionTitleLabel.ForeColor = t.TextSecondary;

        foreach (var strip in new ToolStrip[] { _menuBar, _docTabs })
        {
            strip.BackColor = t.ToolbarBg;
            strip.ForeColor = t.TextPrimary;
            strip.Invalidate();
        }
        RefreshPaintButtonImages();
        foreach (var menuBtn in _menuButtons)
        {
            menuBtn.DropDown.BackColor = t.Surface;
        }
        // ドロップダウン内の実コントロール (スライダー) はテーマ変更に自動追随しないため明示更新
        if (_opacityTrack != null)
        {
            _opacityTrack.BackColor = t.Surface;
            _opacityTrack.TrackBar.BackColor = t.Surface;
        }
        _canvas.BackColor = t.CanvasBg;

        ApplyThemeRecursive(_rightPanel, t);
        ApplyThemeRecursive(_itemPanel, t);
        ApplyThemeRecursive(_overlaySettingsPanel, t);
        ApplyThemeRecursive(_overlayFrame, t);
        ApplyTreeTheme2(t);

        SetSidebarView(_sidebarView); // ボタンの選択色を更新
        _canvas.Invalidate();
        Invalidate(true);
        SaveSession();
    }

    private void ApplyLanguageToControls()
    {
        RebuildMenuBarItems();
        RebuildDocTabs();
        UpdateSessionTitle();
        UpdateSidebarOverlayButtonState(_overlayForm != null);
        SetOverlayOpacity(_overlayOpacity);
        ApplyLanguageRecursive(_rightPanel);
        ApplyLanguageRecursive(_itemPanel);
        ApplyLanguageRecursive(_overlaySettingsPanel);
        ApplyLanguageRecursive(_overlayFrame);
        UpdateMenuShortcutTexts();
        _canvas.Invalidate();
    }

    private static void ApplyLanguageRecursive(Control root)
    {
        switch (root)
        {
            case TextBox tb:
                tb.PlaceholderText = Loc.Text(tb.PlaceholderText);
                break;
            case ComboBox:
                break;
            case ButtonBase b:
                b.Text = Loc.Text(b.Text);
                break;
            case Label l:
                l.Text = Loc.Text(l.Text);
                break;
            default:
                if (!string.IsNullOrEmpty(root.Text)) root.Text = Loc.Text(root.Text);
                break;
        }
        foreach (Control c in root.Controls) ApplyLanguageRecursive(c);
        root.Invalidate();
    }

    // 親→子の順で適用する (子が親の背景色を参照するため、順序を逆にすると旧テーマ色が残る)
    private static void ApplyThemeRecursive(Control root, Theme t)
    {
        switch (root)
        {
            case GamingButton:
                break; // グラデーション色は固有
            case RoundedFlatButton rb:
                rb.ForeColor = t.TextPrimary;
                if (rb.BaseColor != null) rb.BaseColor = t.ButtonBg;
                break; // 描画時にThemeを直接参照する
            case Button b:
                b.BackColor = t.ButtonBg;
                b.ForeColor = t.TextPrimary;
                b.FlatAppearance.BorderColor = t.ButtonBorder;
                break;
            case TextBox tb:
                tb.BackColor = t.SurfaceLight;
                tb.ForeColor = t.TextPrimary;
                break;
            case TreeView tv:
                tv.BackColor = t.TreeBg;
                tv.ForeColor = t.TextPrimary;
                break;
            case ListBox lb:
                lb.BackColor = t.TreeBg;
                lb.ForeColor = t.TextPrimary;
                break;
            case ListView lv:
                lv.BackColor = t.TreeBg;
                lv.ForeColor = t.TextPrimary;
                break;
            case TrackBar bar:
                bar.BackColor = ThemePaint.GetBackdrop(root);
                break;
            case CheckBox cb:
                cb.BackColor = ThemePaint.GetBackdrop(root);
                cb.ForeColor = t.TextPrimary;
                break;
            case Label l:
                l.BackColor = ThemePaint.GetBackdrop(root);
                l.ForeColor = t.TextPrimary;
                break;
            case CustomScrollBar sb:
                sb.BackColor = t.TreeBg;
                break;
            case LayerPanel or ThumbnailView:
                root.BackColor = t.TreeBg;
                break;
            case Panel or FlowLayoutPanel or TableLayoutPanel:
                root.BackColor = root.Parent is CanvasSurface ? t.Surface : ThemePaint.GetBackdrop(root);
                break;
        }

        foreach (Control c in root.Controls) ApplyThemeRecursive(c, t);
        root.Invalidate();
    }

    // ===== 共通ヘルパー =====

    // 多重呼び出しでイベントハンドラが増殖しないよう配線済みコントロールを記録する
    private static readonly HashSet<Control> _roundedRegionWired = [];

    private static void ApplyRoundedRegion(Control control, int radius)
    {
        void UpdateRegion(object? sender, EventArgs e)
        {
            if (control.Width <= 0 || control.Height <= 0) return;
            using var path = CreateRoundedRectPath(control.ClientRectangle, radius);
            control.Region = new Region(path);
        }

        if (_roundedRegionWired.Add(control))
        {
            control.HandleCreated += UpdateRegion;
            control.SizeChanged += UpdateRegion;
            control.Disposed += (_, _) => _roundedRegionWired.Remove(control);
        }

        if (control.IsHandleCreated)
        {
            UpdateRegion(control, EventArgs.Empty);
        }
    }

    private static GraphicsPath CreateRoundedRectPath(Rectangle bounds, int radius)
    {
        var path = new GraphicsPath();
        radius = Math.Max(0, Math.Min(radius, Math.Min(bounds.Width, bounds.Height) / 2));
        if (radius <= 0)
        {
            path.AddRectangle(bounds);
            path.CloseFigure();
            return path;
        }

        var d = radius * 2;
        path.AddArc(bounds.X, bounds.Y, d, d, 180, 90);
        path.AddArc(bounds.Right - d, bounds.Y, d, d, 270, 90);
        path.AddArc(bounds.Right - d, bounds.Bottom - d, d, d, 0, 90);
        path.AddArc(bounds.X, bounds.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }
}
