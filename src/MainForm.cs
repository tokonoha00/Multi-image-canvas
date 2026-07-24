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
    [DllImport("user32.dll")]
    private static extern bool TrackMouseEvent(ref TRACKMOUSEEVENT lpEventTrack);
    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int dwAttribute, ref int pvAttribute, int cbAttribute);

    private const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
    private const int DWMWCP_DONOTROUND = 1;
    private const int DWMWCP_ROUND = 2;
    private const int WM_SETREDRAW = 0x000B;

    [StructLayout(LayoutKind.Sequential)]
    private struct TRACKMOUSEEVENT
    {
        public int cbSize;
        public int dwFlags;
        public IntPtr hwndTrack;
        public int dwHoverTime;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINTL { public int x; public int y; }

    [StructLayout(LayoutKind.Sequential)]
    private struct MINMAXINFO
    {
        public POINTL ptReserved;
        public POINTL ptMaxSize;
        public POINTL ptMaxPosition;
        public POINTL ptMinTrackSize;
        public POINTL ptMaxTrackSize;
    }

    // ウィンドウスタイル (Aeroスナップ/最大化/最小化を有効化するため付与)
    private const int WS_MINIMIZEBOX = 0x00020000;
    private const int WS_MAXIMIZEBOX = 0x00010000;
    private const int WS_THICKFRAME = 0x00040000; // WS_SIZEBOX

    // WndProc メッセージ/ヒットテスト定数
    private const int WM_NCCALCSIZE = 0x0083;
    private const int WM_GETMINMAXINFO = 0x0024;
    private const int WM_NCHITTEST = 0x0084;
    private const int WM_NCACTIVATE = 0x0086;
    private const int WM_NCPAINT = 0x0085;
    private const int WM_NCMOUSEMOVE = 0x00A0;
    private const int WM_NCMOUSELEAVE = 0x02A2;
    private const int WM_NCLBUTTONDOWN = 0x00A1;
    private const int WM_NCLBUTTONUP = 0x00A2;
    private const int HTMAXBUTTON = 9;
    private const int TME_LEAVE = 0x00000002;
    private const int TME_NONCLIENT = 0x00000010;

    // スナップレイアウトのフライアウト表示中に最大化ボタンをホバー追跡しているか
    private bool _trackingNcLeave;

    private const int WM_HOTKEY = 0x0312;
    private const uint MOD_ALT = 0x1;
    private const uint MOD_CONTROL = 0x2;
    private const uint MOD_SHIFT = 0x4;
    private const int HotkeyToggleOverlay = 1;
    private const int HotkeyClickThrough = 2;
    private const int HotkeyOpacityDown = 3;
    private const int HotkeyOpacityUp = 4;
    private const int HotkeyCanvasNext = 5;
    private const int HotkeyCanvasPrev = 6;
    private const int HotkeyCanvasDirectBase = 1000;

    private readonly CanvasSurface _canvas = new();
    private readonly KeyMap _keyMap = new();

    // ドキュメント (キャンバスタブ)
    private readonly List<CanvasDocument> _docs = [];
    private readonly Dictionary<int, CanvasDocument> _directCanvasHotkeys = [];
    private int _activeDocIndex = -1;

    // 上部バー: ドロップダウンメニュー + キャンバスタブ
    private readonly MenuBarStrip _menuBar = new() { GripStyle = ToolStripGripStyle.Hidden, Dock = DockStyle.Top };
    private readonly Panel _docBar = new() { Dock = DockStyle.Top, Height = 32 };
    private readonly Panel _docTabsViewport = new();
    private readonly ClickThroughToolStrip _docTabs = new() { GripStyle = ToolStripGripStyle.Hidden, Dock = DockStyle.None };
    private readonly ClickThroughToolStrip _docTools = new() { GripStyle = ToolStripGripStyle.Hidden, Dock = DockStyle.None };
    private readonly List<ToolStripMenuItem> _menuButtons = [];
    private readonly Dictionary<string, ToolStripMenuItem> _actionMenuItems = [];

    private ToolStripMenuItem? _undoMi, _redoMi, _naturalMi, _topmostMi;
    private ToolStripMenuItem? _overlayActiveMi, _clickThroughMi, _overlayFrameMi;
    private ToolStripMenuItem? _overlayMenuBtn;
    private ToolStripTrackBar? _opacityTrack;
    private ToolStripLabel? _opacityMenuLabel;
    private readonly ToolStripLabel _zoomText = new("100%")
    {
        Alignment = ToolStripItemAlignment.Left,
        AutoSize = false,
        Width = 52,
        TextAlign = ContentAlignment.MiddleCenter,
        Overflow = ToolStripItemOverflow.Never,
    };
    private ToolStripButton? _zoomInBtn, _zoomOutBtn, _zoomFitBtn;
    private ToolStripButton? _paintSelectBtn, _paintRedBtn, _paintMarkerBtn, _paintEraserBtn, _paintClearBtn;
    private int _tabScrollOffset;
    private int[] _tabItemOffsets = [];
    private int _tabContentWidth;
    private int _tabScrollTargetX;
    private readonly System.Windows.Forms.Timer _tabScrollTimer = new() { Interval = 15 };
    private bool _rebuildingDocTabs;
    private readonly Label _sessionTitleLabel = new();
    private readonly string[] _startupArgs;
    private static readonly HttpClient Http = new();

    // 右サイドバー
    private readonly Panel _rightPanel = new();
    private readonly Panel _sidebarContent = new();
    private Panel? _sidebarHeader;
    private Control? _sidebarPathPanel;
    private FlowLayoutPanel? _sidebarSwitchRow;
    private Button? _sidebarCollapseBtn;
    private readonly ToolTip _sidebarToolTip = new();
    private ThumbnailView? _thumbs;
    private LayerPanel? _layers;
    private Panel? _treeArea;
    private readonly Panel _overlayFrame = new();
    private readonly GamingButton _sidebarOverlayBtn = new();
    private Button? _viewTreeBtn, _viewThumbsBtn, _viewLayersBtn;
    private string _sidebarView = "tree";

    // 選択画像の操作メニュー (画像横にフローティング表示)
    private readonly Panel _itemPanel = new();
    private bool _itemPanelRequested;

    // オーバーレイ設定パネル (歯車で開く。ファイル選択画面に重ねて表示)
    private readonly Panel _overlaySettingsPanel = new();
    private CheckBox? _ovlClickThroughCheck;
    private CheckBox? _ovlFrameCheck;
    private Label? _ovlOpacityLabel;
    private TrackBar? _ovlOpacitySlider;
    private bool _syncingOverlayPanel;

    private OverlayForm? _overlayForm;
    private float _overlayOpacity = 1.0f;
    private bool _overlayClickThrough;
    private bool _overlayFrameVisible = true;

    private bool _uiHidden;
    private bool _sidebarCollapsed;
    private int _sidebarWidth = 296;
    private string? _lastFolderPath;

    private bool _restoreTabsSetting = true;
    private int _autosaveSeconds = 30;
    private string _language = Loc.Japanese;
    private string _overlayAnimation = "フェード";

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

    // ビュアーモード (エクスプローラーから画像を開いた時の閲覧専用UI)
    private bool _viewerMode;
    private readonly ClickThroughToolStrip _viewerBar = new() { GripStyle = ToolStripGripStyle.Hidden, Dock = DockStyle.Top };
    private ToolStripLabel? _viewerTitle;
    private ToolStripButton? _viewerEditBtn;
    private string _viewerEditFullText = "";

    // ビュアーのGIFアニメ操作UI
    private FlowLayoutPanel? _gifPanel;
    private Button? _gifPrevBtn;
    private Button? _gifPlayBtn;
    private Button? _gifNextBtn;
    private FlatSlider? _gifTrack;
    private ComboBox? _gifSpeedCombo;
    private Label? _gifFrameLabel;
    private bool _syncingGifUi;
    private readonly Panel _viewerNavPanel = new();
    private Panel? _navFlow;
    private FlowLayoutPanel? _pageGroup;
    private Label? _viewerPageLabel;
    private Button? _viewerPrevBtn, _viewerNextBtn, _viewerFullscreenBtn;
    private readonly System.Windows.Forms.Timer _viewerChromeTimer = new() { Interval = 16 };
    private DateTime _lastViewerActivity = DateTime.UtcNow;
    private Point _lastCursorScreenPos = Point.Empty;
    private float _viewerChromeOpacity;
    private List<string> _viewerFiles = [];
    private int _viewerFileIndex = -1;
    private string? _viewerCurrentPath;
    private bool _viewerFullscreen;
    private readonly object _viewerPreloadLock = new();
    private readonly Dictionary<string, Task<Image>> _viewerPreloads = new(StringComparer.OrdinalIgnoreCase);
    private int _viewerOpenVersion;
    private ArchiveImageSource? _archiveSource;
    private readonly Panel _archiveBrowserPanel = new();
    private readonly TextBox _archiveSearchBox = new();
    private readonly TreeView _archiveTree = new();
    private Rectangle _viewerRestoreBounds;
    private FormWindowState _viewerRestoreWindowState;
    private Padding _viewerRestorePadding;
    private IntPtr _nativeCornerHandle;
    private bool? _nativeCornersRounded;
    private bool _liveResizing;
    private bool _overlayResizeSyncPending;
    private bool _editorUiBuilt;
    private bool _archiveBrowserBuilt;

    private const int CornerRadius = 14;

    public MainForm(string[]? startupArgs = null)
    {
        _startupArgs = startupArgs ?? [];
        _viewerMode = DetectViewerMode(_startupArgs);
        // テーマはコントロール生成前に適用する
        if (!_viewerMode) _pendingSession = SessionStore.Load();
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
        DoubleBuffered = true;

        try { Font = new Font("Segoe UI Variable Display", 9f); }
        catch { try { Font = new Font("Segoe UI", 9f); } catch { /* フォールバック */ } }

        BuildMainLayout();
        WireDragDrop();
        if (_viewerMode) EnterViewerMode();
        else
        {
            BuildEditorUi();
            RestoreSession();
            ApplyLanguageToControls();
        }

        Theme.Changed += (_, _) => ApplyThemeToControls();

        _autosaveTimer.Tick += (_, _) => SaveSession();
        if (!_viewerMode) _autosaveTimer.Start();

        Shown += (_, _) =>
        {
            if (!_viewerMode)
            {
                ApplyTreeTheme();
                BeginInvoke(new Action(() => SetSidebarView(_sidebarView)));
                BeginInvoke(new Action(StartLoadQuickAccessExtras));
            }
            if (_viewerMode) _ = OpenStartupInputsAsync();
            else BeginInvoke(new Action(() => _ = OpenStartupInputsAsync()));

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
                SaveViewerWindowPlacement();
                ToggleOverlayMode(false);
                return;
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
            _overlayFrameVisible = s.OverlayFrameVisible;
            _sidebarView = s.SidebarView;

            // 前回のタブを復元
            if (_restoreTabsSetting)
            {
                for (int i = 0; i < s.Tabs.Count; i++)
                {
                    try
                    {
                        var doc = LayoutSerializer.FromDto(s.Tabs[i], i < s.TabFilePaths.Count ? s.TabFilePaths[i] : null);
                        RestoreCanvasSessionState(s, i, doc);
                        if (s.OverlayLocations is { } locations && i < locations.Count && locations[i] is { Length: 2 } location)
                        {
                            doc.OverlayLocation = new Point(location[0], location[1]);
                        }
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
            AddDocument(CreateNewCanvasDocument(), select: true);
        }

        // 復元した設定をUIコントロールへ反映
        if (_naturalMi != null) _naturalMi.Checked = _canvas.InsertNaturalSize;
        SetOverlayOpacity(_overlayOpacity);
        if (_clickThroughMi != null) _clickThroughMi.Checked = _overlayClickThrough;

        UpdateMenuShortcutTexts();
        SyncMenuState();
        SetSidebarView(_sidebarView, loadThumbs: false);
    }

    private void SaveSession()
    {
        SessionStore.Save(CreateSessionData());
    }

    private SessionData CreateSessionData(bool includeOverlayLocations = true) => new()
    {
        WindowBounds = WindowState == FormWindowState.Normal
            ? [Bounds.X, Bounds.Y, Bounds.Width, Bounds.Height]
            : [RestoreBounds.X, RestoreBounds.Y, RestoreBounds.Width, RestoreBounds.Height],
        Maximized = WindowState == FormWindowState.Maximized,
        SidebarView = _sidebarView,
        InsertNaturalSize = _canvas.InsertNaturalSize,
        OverlayOpacity = _overlayOpacity,
        OverlayClickThrough = _overlayClickThrough,
        OverlayFrameVisible = _overlayFrameVisible,
        BgOpacity = _canvas.BgOpacity,
        ActiveTab = _activeDocIndex,
        Tabs = _docs.Select(LayoutSerializer.ToDto).ToList(),
        CanvasIds = _docs.Select(d => d.Id).ToList(),
        CanvasSwitchShortcuts = _docs
            .Where(d => d.SwitchShortcut != Keys.None)
            .ToDictionary(d => d.Id, d => (int)d.SwitchShortcut),
        OverlayLocations = includeOverlayLocations
            ? _docs.Select(d => d.OverlayLocation is { } p ? new[] { p.X, p.Y } : null).ToList()
            : null,
        TabFilePaths = _docs.Select(d => d.FilePath).ToList(),
    };

    private void RestoreCanvasSessionState(SessionData session, int index, CanvasDocument doc)
    {
        if (session.CanvasIds is { } ids && index < ids.Count && ids[index] != Guid.Empty
            && _docs.All(existing => existing.Id != ids[index]))
        {
            doc.Id = ids[index];
        }

        if (session.CanvasSwitchShortcuts?.TryGetValue(doc.Id, out var saved) != true) return;
        var keys = (Keys)saved;
        if (!IsCanvasShortcutAllowed(keys) || IsShortcutReserved(keys)
            || _docs.Any(existing => existing.SwitchShortcut == keys)) return;
        doc.SwitchShortcut = keys;
    }

    private void ApplyStoredSettings()
    {
        _restoreTabsSetting = _appSettings.RestoreTabs;
        _autosaveSeconds = Math.Clamp(_appSettings.AutosaveSeconds, 10, 600);
        _autosaveTimer.Interval = _autosaveSeconds * 1000;
        _canvas.SnapEnabled = _appSettings.SnapEnabled;
        _canvas.GridSnapEnabled = _appSettings.GridSnap;
        _canvas.ImageImportScale = Math.Clamp(_appSettings.ImageImportScalePercent, 25, 200) / 100f;
        _language = Loc.Normalize(_appSettings.Language);
        _overlayAnimation = OverlayAnimations.Names.Contains(_appSettings.OverlayAnimation) ? _appSettings.OverlayAnimation : "フェード";
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
            ViewerWindowBounds = _appSettings.ViewerWindowBounds,
            ViewerMaximized = _appSettings.ViewerMaximized,
        };
        AppSettingsStore.Save(_appSettings);
    }

    // ===== ドキュメント (タブ) 管理 =====

    private CanvasDocument? ActiveDoc => _activeDocIndex >= 0 && _activeDocIndex < _docs.Count ? _docs[_activeDocIndex] : null;

    private void ClearDocuments()
    {
        _itemPanelRequested = false;
        _canvas.Document = null;
        _layers?.AttachDocument(null);
        foreach (var doc in _docs) doc.Dispose();
        _docs.Clear();
        _activeDocIndex = -1;
        ReregisterGlobalHotkeys();
        RebuildDocTabs();
        SyncMenuState();
        UpdateItemPanel();
    }

    private void AddDocument(CanvasDocument doc, bool select, bool save = true)
    {
        if (doc.Id == Guid.Empty || _docs.Any(existing => existing.Id == doc.Id)) doc.Id = Guid.NewGuid();
        if (doc.SwitchShortcut != Keys.None && (IsShortcutReserved(doc.SwitchShortcut)
            || _docs.Any(existing => existing.SwitchShortcut == doc.SwitchShortcut)))
        {
            doc.SwitchShortcut = Keys.None;
        }
        _docs.Add(doc);
        doc.Changed += (_, _) =>
        {
            if (!_editorUiBuilt) return;
            RebuildDocTabs();
            SyncMenuState();
            UpdateItemPanel();
        };
        doc.Undo.StateChanged += (_, _) => { if (_editorUiBuilt) SyncMenuState(); };
        ReregisterGlobalHotkeys();
        if (_editorUiBuilt) RebuildDocTabs();
        if (select) SelectDocument(_docs.Count - 1);
        if (save && IsHandleCreated) SaveSession();
    }

    private CanvasDocument CreateNewCanvasDocument() =>
        new(CanvasDocument.FindAvailableDefaultName(_docs.Select(doc => doc.Name)));

    private void SelectDocument(int index)
    {
        if (index < 0 || index >= _docs.Count) return;
        _itemPanelRequested = false;
        if (_overlayForm != null) StoreOverlayLocation(ActiveDoc);
        _activeDocIndex = index;
        var doc = _docs[index];
        if (_overlayForm != null) SwitchOverlayToDocument(doc);
        else _canvas.Document = doc;
        _layers?.AttachDocument(doc);
        if (!_editorUiBuilt) return;
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

        var activeDoc = ActiveDoc;
        if (_overlayForm != null) StoreOverlayLocation(activeDoc);
        _docs.RemoveAt(index);
        if (ReferenceEquals(_canvas.Document, doc)) _canvas.Document = null;
        doc.Dispose();
        ReregisterGlobalHotkeys();

        int nextIndex = ReferenceEquals(activeDoc, doc)
            ? Math.Min(index, _docs.Count - 1)
            : Math.Max(0, _docs.IndexOf(activeDoc!));
        _activeDocIndex = -1;
        SelectDocument(nextIndex);
        if (IsHandleCreated) SaveSession();
    }

    private void RebuildDocTabs(bool ensureActiveVisible = true)
    {
        if (_rebuildingDocTabs) return;
        _rebuildingDocTabs = true;
        _docTabs.SuspendLayout();
        try
        {
            EnsureDocTools();
            UpdateDocBarLayout();
            _docTabs.Items.Clear();

            if (_docs.Count == 0)
            {
                _tabScrollOffset = 0;
                return;
            }

            int available = Math.Max(1, _docTabsViewport.ClientSize.Width - _docTabs.Padding.Horizontal);
            var widths = _docs.Select(doc => Math.Clamp(
                TextRenderer.MeasureText($" {doc.Name} ", _docTabs.Font).Width + 8,
                56,
                220)).ToArray();
            const int trailingActionsWidth = 168;

            int start = Math.Clamp(_tabScrollOffset, 0, _docs.Count - 1);
            if (ensureActiveVisible && _activeDocIndex >= 0)
            {
                start = Math.Min(start, _activeDocIndex);
                int activeWidth = widths[_activeDocIndex] + 2
                    + (_activeDocIndex == _docs.Count - 1 ? trailingActionsWidth : 0);
                for (int i = _activeDocIndex - 1; i >= start; i--) activeWidth += widths[i] + 2;
                while (start < _activeDocIndex && activeWidth > available)
                {
                    activeWidth -= widths[start] + 2;
                    start++;
                }
                while (start > 0 && activeWidth + widths[start - 1] + 2 <= available)
                {
                    start--;
                    activeWidth += widths[start] + 2;
                }
            }
            _tabScrollOffset = start;

            _tabItemOffsets = new int[_docs.Count];
            int used = _docTabs.Padding.Left;
            for (int i = 0; i < _docs.Count; i++)
            {
                int itemWidth = widths[i];
                _tabItemOffsets[i] = used;

                var doc = _docs[i];
                int captured = i;
                bool isActive = i == _activeDocIndex;
                var btn = new SlidingToolStripButton($" {doc.Name} ")
                {
                    AutoSize = false,
                    Width = itemWidth,
                    Checked = isActive,
                    Margin = new Padding(2, 2, 0, 2),
                    Overflow = ToolStripItemOverflow.Never,
                    Tag = captured,
                    ToolTipText = BuildCanvasTabToolTip(doc),
                };
                btn.MouseDown += (_, e) =>
                {
                    if (e.Button == MouseButtons.Left) BeginTabDrag(captured);
                };
                btn.MouseMove += (_, _) => UpdateTabDrag();
                btn.MouseUp += (_, e) =>
                {
                    if (e.Button == MouseButtons.Left) CompleteTabDrag();
                    else if (e.Button == MouseButtons.Right) ShowCanvasShortcutMenu(doc);
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
                used += itemWidth + btn.Margin.Horizontal;
            }

            var addBtn = new ToolStripButton(" ＋ ")
            {
                AutoSize = false,
                Width = 38,
                Margin = new Padding(6, 2, 2, 2),
                Overflow = ToolStripItemOverflow.Never,
                ToolTipText = Loc.T("新規キャンバス (Ctrl+T)"),
            };
            addBtn.Click += (_, _) => AddDocument(CreateNewCanvasDocument(), select: true);
            _docTabs.Items.Add(addBtn);
            used += addBtn.Width + addBtn.Margin.Horizontal;

            var closeBtn = new ToolStripButton(" " + Loc.T("キャンバスを閉じる") + " ")
            {
                AutoSize = false,
                Width = 118,
                Margin = new Padding(2),
                Overflow = ToolStripItemOverflow.Never,
                ToolTipText = Loc.T("現在のキャンバスを閉じる (Ctrl+W)"),
            };
            closeBtn.Click += (_, _) => CloseDocument(_activeDocIndex);
            _docTabs.Items.Add(closeBtn);
            used += closeBtn.Width + closeBtn.Margin.Horizontal + _docTabs.Padding.Right;

            _tabContentWidth = Math.Max(used, _docTabsViewport.ClientSize.Width);
            _docTabs.SetBounds(_docTabs.Left, 0, _tabContentWidth, _docTabsViewport.ClientSize.Height);
            SetTabScrollPosition(animate: false);
            UpdatePaintButtons();
        }
        finally
        {
            _docTabs.ResumeLayout();
            _rebuildingDocTabs = false;
        }
    }

    private static string BuildCanvasTabToolTip(CanvasDocument doc)
    {
        var text = Loc.T("右クリックで名前変更 / ドラッグで並べ替え");
        return doc.SwitchShortcut == Keys.None
            ? text
            : text + Environment.NewLine + string.Format(Loc.T("切り替えキー: {0}"), KeyMap.ToDisplay(doc.SwitchShortcut));
    }

    private void ShowCanvasShortcutMenu(CanvasDocument doc)
    {
        var menu = new ContextMenuStrip
        {
            Renderer = new ThemedToolStripRenderer(),
            BackColor = Theme.Current.Surface,
            ForeColor = Theme.Current.TextPrimary,
        };
        var current = new ToolStripMenuItem(doc.SwitchShortcut == Keys.None
            ? Loc.T("切り替えキー: 未設定")
            : string.Format(Loc.T("切り替えキー: {0}"), KeyMap.ToDisplay(doc.SwitchShortcut)))
        {
            Enabled = false,
        };
        var rename = new ToolStripMenuItem(Loc.T("キャンバス名の変更"));
        rename.Click += (_, _) => BeginInvoke(() =>
        {
            int index = _docs.IndexOf(doc);
            if (index < 0) return;
            SelectDocument(index);
            RenameActiveDoc();
        });
        var assign = new ToolStripMenuItem(Loc.T("切り替えキーを設定..."));
        assign.Click += (_, _) => BeginInvoke(() => AssignCanvasShortcut(doc));
        var clear = new ToolStripMenuItem(Loc.T("割り当て解除")) { Enabled = doc.SwitchShortcut != Keys.None };
        clear.Click += (_, _) => ClearCanvasShortcut(doc);
        menu.Items.AddRange([rename, new ToolStripSeparator(), current, assign, clear]);
        menu.Closed += (_, _) => BeginInvoke(menu.Dispose);
        menu.Show(Cursor.Position);
    }

    private void AssignCanvasShortcut(CanvasDocument doc)
    {
        if (!_docs.Contains(doc)) return;
        using var dialog = new KeyCaptureForm(string.Format(Loc.T("{0}へ切り替え"), doc.Name));
        if (dialog.ShowDialog(this) != DialogResult.OK || dialog.CapturedKeys == Keys.None) return;
        var keys = dialog.CapturedKeys;

        if (!IsCanvasShortcutAllowed(keys))
        {
            MessageBox.Show(this,
                Loc.T("キャンバス切り替えには修飾キーを含む組み合わせ、またはファンクションキーを割り当ててください。"),
                Loc.T("キー割り当て"), MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        if (_keyMap.FindByKeys(keys) is { } binding)
        {
            MessageBox.Show(this,
                string.Format(Loc.T("{0} は「{1}」に割り当てられています。設定画面で先に割り当てを変更してください。"),
                    KeyMap.ToDisplay(keys), Loc.T(binding.Label)),
                Loc.T("キーの競合"), MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        if (IsFixedShortcut(keys))
        {
            MessageBox.Show(this, Loc.T("このキーはアプリの固定操作に使用されています。"),
                Loc.T("キーの競合"), MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var previousOwner = _docs.FirstOrDefault(other => !ReferenceEquals(other, doc) && other.SwitchShortcut == keys);
        if (previousOwner != null && MessageBox.Show(this,
                string.Format(Loc.T("{0} は「{1}」の切り替えに割り当てられています。移動しますか？"),
                    KeyMap.ToDisplay(keys), previousOwner.Name),
                Loc.T("キーの競合"), MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
        {
            return;
        }

        var oldShortcut = doc.SwitchShortcut;
        if (previousOwner != null) previousOwner.SwitchShortcut = Keys.None;
        doc.SwitchShortcut = keys;
        ReregisterGlobalHotkeys();
        if (!_directCanvasHotkeys.Values.Contains(doc))
        {
            doc.SwitchShortcut = oldShortcut;
            if (previousOwner != null) previousOwner.SwitchShortcut = keys;
            ReregisterGlobalHotkeys();
            MessageBox.Show(this,
                Loc.T("このキーはWindowsまたは別のアプリで使用されているため登録できませんでした。"),
                Loc.T("キー割り当て"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        RebuildDocTabs();
        SaveSession();
    }

    private void ClearCanvasShortcut(CanvasDocument doc)
    {
        if (!_docs.Contains(doc) || doc.SwitchShortcut == Keys.None) return;
        doc.SwitchShortcut = Keys.None;
        ReregisterGlobalHotkeys();
        RebuildDocTabs();
        SaveSession();
    }

    internal static bool IsCanvasShortcutAllowed(Keys keys)
    {
        if (keys == Keys.None || (keys & Keys.KeyCode) == Keys.None) return false;
        bool hasModifier = (keys & (Keys.Control | Keys.Alt | Keys.Shift)) != 0;
        var keyCode = keys & Keys.KeyCode;
        return hasModifier || keyCode is >= Keys.F1 and <= Keys.F24;
    }

    private bool IsShortcutReserved(Keys keys) => _keyMap.FindByKeys(keys) != null || IsFixedShortcut(keys);

    private static bool IsFixedShortcut(Keys keys) =>
        keys is (Keys.Control | Keys.Tab) or (Keys.Control | Keys.Shift | Keys.Z);

    private void EnsureDocTools()
    {
        if (_docTools.Items.Count == 0)
        {
            _zoomInBtn = MakeFixedDocToolButton(" ＋ ", Loc.T("ズームイン"), (_, _) => _canvas.SetZoom(_canvas.Zoom * 1.15f));
            _zoomOutBtn = MakeFixedDocToolButton(" － ", Loc.T("ズームアウト"), (_, _) => _canvas.SetZoom(_canvas.Zoom / 1.15f));
            _zoomFitBtn = MakeFixedDocToolButton(" 🗺 ", Loc.T("全体表示"), (_, _) => _canvas.ZoomFitAll());
            _paintSelectBtn = MakePaintButton(PaintIconKind.Select, Loc.T("画像選択"), (_, _) => SetPaintTool(PaintTool.None));
            _paintRedBtn = MakePaintButton(PaintIconKind.Pen, Loc.T("赤ペン"), (_, _) => TogglePaintTool(PaintTool.RedPen));
            _paintMarkerBtn = MakePaintButton(PaintIconKind.Marker, Loc.T("黄色マーカー"), (_, _) => TogglePaintTool(PaintTool.YellowMarker));
            _paintEraserBtn = MakePaintButton(PaintIconKind.Eraser, Loc.T("消しゴム"), (_, _) => TogglePaintTool(PaintTool.Eraser));
            _paintClearBtn = MakePaintButton(PaintIconKind.Mop, Loc.T("全消し"), (_, _) => _canvas.ClearPaintStrokes());

            _docTools.Items.Add(_paintSelectBtn);
            _docTools.Items.Add(_paintRedBtn);
            _docTools.Items.Add(_paintMarkerBtn);
            _docTools.Items.Add(_paintEraserBtn);
            _docTools.Items.Add(_paintClearBtn);
            _docTools.Items.Add(new ToolStripSeparator { Overflow = ToolStripItemOverflow.Never });
            _docTools.Items.Add(_zoomFitBtn);
            _docTools.Items.Add(_zoomOutBtn);
            _docTools.Items.Add(_zoomInBtn);
            _docTools.Items.Add(_zoomText);
        }
    }

    private void UpdateDocBarLayout()
    {
        if (_docBar.ClientSize.Width <= 0) return;
        _docTools.PerformLayout();
        int toolsWidth = _docTools.Padding.Horizontal + _docTools.Items.Cast<ToolStripItem>()
            .Sum(item => item.Width + item.Margin.Horizontal);
        toolsWidth = Math.Min(toolsWidth, Math.Max(0, _docBar.ClientSize.Width - 80));
        _docTabsViewport.SetBounds(0, 0, _docBar.ClientSize.Width - toolsWidth, _docBar.ClientSize.Height);
        _docTools.SetBounds(_docBar.ClientSize.Width - toolsWidth, 0, toolsWidth, _docBar.ClientSize.Height);
    }

    private void ScrollCanvasTabs(int delta)
    {
        if (_docs.Count <= 1 || _tabDragActive) return;
        int next = Math.Clamp(_tabScrollOffset + delta, 0, _docs.Count);
        if (next == _tabScrollOffset) return;
        _tabScrollOffset = next;
        SetTabScrollPosition(animate: true);
    }

    private void SetTabScrollPosition(bool animate)
    {
        if (_tabItemOffsets.Length == 0) return;
        int minimumX = Math.Min(0, _docTabsViewport.ClientSize.Width - _tabContentWidth);
        _tabScrollTargetX = _tabScrollOffset >= _tabItemOffsets.Length
            ? minimumX
            : Math.Clamp(-_tabItemOffsets[Math.Max(0, _tabScrollOffset)], minimumX, 0);
        if (!animate)
        {
            _tabScrollTimer.Stop();
            _docTabs.Left = _tabScrollTargetX;
            return;
        }
        _tabScrollTimer.Start();
    }

    private void AnimateTabScroll()
    {
        int distance = _tabScrollTargetX - _docTabs.Left;
        if (Math.Abs(distance) <= 1)
        {
            _docTabs.Left = _tabScrollTargetX;
            _tabScrollTimer.Stop();
            return;
        }
        _docTabs.Left += Math.Sign(distance) * Math.Max(1, (int)Math.Ceiling(Math.Abs(distance) * 0.24));
    }

    private void TogglePaintTool(PaintTool tool)
    {
        SetPaintTool(_canvas.PaintTool == tool ? PaintTool.None : tool);
    }

    private void SetPaintTool(PaintTool tool)
    {
        _canvas.PaintTool = tool;
        UpdatePaintButtons();
    }

    private void UpdatePaintButtons()
    {
        if (_paintSelectBtn != null) _paintSelectBtn.Checked = _canvas.PaintTool == PaintTool.None;
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
        var b = new ToolStripButton(text)
        {
            Alignment = ToolStripItemAlignment.Left,
            Margin = new Padding(2, 2, 2, 2),
            Overflow = ToolStripItemOverflow.Never,
            ToolTipText = tooltip,
        };
        b.Click += onClick;
        return b;
    }

    private static ToolStripButton MakeFixedDocToolButton(string text, string tooltip, EventHandler onClick, int width = 34)
    {
        var button = MakeDocTabButton(text, tooltip, onClick);
        button.AutoSize = false;
        button.Size = new Size(width, 26);
        return button;
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
        Select,
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
            case PaintIconKind.Select:
                using (var dash = new Pen(fg, 1.8f) { DashStyle = DashStyle.Dash, DashCap = DashCap.Round })
                {
                    g.DrawRectangle(dash, 4, 4, 12, 12);
                }
                break;
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
        foreach (var btn in new[] { _paintSelectBtn, _paintRedBtn, _paintMarkerBtn, _paintEraserBtn, _paintClearBtn })
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
        foreach (var strip in new ToolStrip[] { _menuBar, _docTabs, _docTools })
        {
            strip.Renderer = new ThemedToolStripRenderer();
            strip.BackColor = Theme.Current.ToolbarBg;
            strip.ForeColor = Theme.Current.TextPrimary;
            strip.AutoSize = false;
        }
        _menuBar.Padding = new Padding(10, 4, 10, 2);
        _menuBar.Height = 40;
        _docTabs.Padding = new Padding(10, 2, 0, 2);
        _docTabs.CanOverflow = false;
        _docTabs.Height = 32;
        _docTabsViewport.BackColor = Theme.Current.ToolbarBg;
        _docTabsViewport.Controls.Add(_docTabs);
        _docTools.Padding = new Padding(0, 2, 10, 2);
        _docTools.CanOverflow = false;
        _docTools.Height = 32;
        _docBar.BackColor = Theme.Current.ToolbarBg;
        _docBar.Controls.Add(_docTabsViewport);
        _docBar.Controls.Add(_docTools);
        _docBar.SizeChanged += (_, _) =>
        {
            UpdateDocBarLayout();
            RebuildDocTabs();
        };

        WireTitleBarDrag(_menuBar);
        WireTitleBarDrag(_docTabs);
        _docTabs.MouseDown += (_, e) =>
        {
            if (e.Button != MouseButtons.Left) return;
            if (_docTabs.GetItemAt(e.Location)?.Tag is int index) BeginTabDrag(index);
        };
        _docTabs.MouseMove += (_, _) => UpdateTabDrag();
        _docTabs.MouseUp += (_, e) => { if (e.Button == MouseButtons.Left) CompleteTabDrag(); };
        _docTabs.MouseWheel += (_, e) => ScrollCanvasTabs(e.Delta < 0 ? 1 : -1);
        _docTabsViewport.MouseWheel += (_, e) => ScrollCanvasTabs(e.Delta < 0 ? 1 : -1);
        _tabScrollTimer.Tick += (_, _) => AnimateTabScroll();

        RebuildMenuBarItems();
        EnsureDocTools();
        UpdateDocBarLayout();

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
        _overlayFrameMi = null;
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

    // 起動引数が「存在する画像または対応圧縮ファイルのみ」の場合はビュアーとして起動する
    // (.mics/.micl やフォルダ、URLが含まれる場合は通常の編集画面)
    private static bool DetectViewerMode(string[] args)
    {
        var inputs = args.Select(a => a.Trim().Trim('"')).Where(a => !string.IsNullOrWhiteSpace(a)).ToList();
        if (inputs.Count == 0) return false;
        if (inputs.Any(ArchiveImageSource.IsSupportedArchivePath))
        {
            return inputs.Count == 1 && File.Exists(inputs[0]) && ArchiveImageSource.IsSupportedArchivePath(inputs[0]);
        }
        return inputs.All(a => File.Exists(a) && ImageDecoder.IsSupported(a));
    }

    private async Task OpenStartupInputsAsync()
    {
        if (_viewerMode)
        {
            var first = _startupArgs.Select(a => a.Trim().Trim('"')).FirstOrDefault(File.Exists);
            if (first == null) return;
            if (ArchiveImageSource.IsSupportedArchivePath(first))
            {
                await OpenArchiveViewerAsync(first);
                ShowViewerChrome();
                return;
            }
            PrepareViewerFile(first);
            await OpenViewerFileAsync(first, ++_viewerOpenVersion);
            ShowViewerChrome();
            _ = LoadViewerFileListAsync(first, _viewerOpenVersion);
            return;
        }

        await OpenInputsAsync(_startupArgs);
    }

    private void PrepareViewerFile(string path)
    {
        _archiveSource = null;
        HideArchiveBrowser();
        _viewerCurrentPath = path;
        _viewerFiles = [path];
        _viewerFileIndex = 0;
    }

    private async Task LoadViewerFileListAsync(string path, int version)
    {
        var dir = Path.GetDirectoryName(path);
        if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) return;

        try
        {
            var files = await Task.Run(() => Directory.EnumerateFiles(dir)
                .Where(ImageDecoder.IsSupported)
                .OrderBy(Path.GetFileName, StringComparer.CurrentCultureIgnoreCase)
                .ToList());
            if (version != _viewerOpenVersion || !string.Equals(_viewerCurrentPath, path, StringComparison.OrdinalIgnoreCase)) return;

            var index = files.FindIndex(f => string.Equals(f, path, StringComparison.OrdinalIgnoreCase));
            if (index < 0)
            {
                files.Insert(0, path);
                index = 0;
            }
            _viewerFiles = files;
            _viewerFileIndex = index;
            UpdateViewerNavState();
            QueueViewerPreloads();
        }
        catch
        {
            // 初回画像はすでに表示済みなので、フォルダ一覧の失敗は無視する
        }
    }

    private async Task OpenViewerFileAsync(string path, int version)
    {
        if (!_viewerMode) return;

        Image image;
        try
        {
            image = await GetViewerImageAsync(path);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, Loc.T("画像を読み込めません"), MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }

        if (version != _viewerOpenVersion)
        {
            image.Dispose();
            return;
        }

        List<Image> disposeLater;
        _canvas.ShutdownViewerAnimation();
        if (ActiveDoc is not { } doc)
        {
            doc = new CanvasDocument(Path.GetFileNameWithoutExtension(path.Replace('/', Path.DirectorySeparatorChar)));
            AddDocument(doc, select: true, save: false);
        }
        else
        {
            doc.Name = Path.GetFileNameWithoutExtension(path.Replace('/', Path.DirectorySeparatorChar));
        }
        disposeLater = _canvas.ReplaceViewerImage(image, path);
        _canvas.Select(null);
        _canvas.SetViewerBaseline();
        _canvas.InitViewerAnimation();
        SetGifControlsVisible(_canvas.ViewerFrameCount > 1);
        SyncGifControls();

        _viewerCurrentPath = path;
        var displayName = _archiveSource == null ? Path.GetFileName(path) : path;
        if (_viewerTitle != null) _viewerTitle.Text = displayName;
        Text = displayName + " - Multi Image Canvas";
        UpdateViewerNavState();
        DisposeViewerImagesLater(disposeLater);
        QueueViewerPreloads();
    }

    private static void DisposeViewerImagesLater(List<Image> images)
    {
        if (images.Count == 0) return;
        System.Threading.ThreadPool.QueueUserWorkItem(static state =>
        {
            try
            {
                foreach (var image in (List<Image>)state!) image.Dispose();
            }
            catch { /* viewer navigation should not be interrupted by cleanup failure */ }
        }, images);
    }

    private async void NavigateViewerImage(int delta)
    {
        if (!_viewerMode || _viewerFiles.Count == 0) return;
        _viewerFileIndex = (_viewerFileIndex + delta + _viewerFiles.Count) % _viewerFiles.Count;
        await OpenViewerFileAsync(_viewerFiles[_viewerFileIndex], ++_viewerOpenVersion);
        ShowViewerChrome();
    }

    private Task<Image> GetViewerImageAsync(string path)
    {
        lock (_viewerPreloadLock)
        {
            if (_viewerPreloads.Remove(path, out var preloaded)) return preloaded;
        }
        var archive = _archiveSource;
        return Task.Run(() => DecodeViewerImage(path, archive));
    }

    private static Image DecodeViewerImage(string path, ArchiveImageSource? archive)
    {
        // ビュアーは表示のみ。透明にじみ補正を省いて即時表示する
        return archive != null
            ? archive.Decode(path, fixTransparency: false)
            : ImageDecoder.Decode(path, fixTransparency: false);
    }

    private void QueueViewerPreloads()
    {
        if (!_viewerMode || _viewerFiles.Count <= 1 || _viewerFileIndex < 0) return;

        var targets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        targets.Add(_viewerFiles[(_viewerFileIndex - 1 + _viewerFiles.Count) % _viewerFiles.Count]);
        targets.Add(_viewerFiles[(_viewerFileIndex + 1) % _viewerFiles.Count]);
        if (_viewerCurrentPath != null) targets.Add(_viewerCurrentPath);

        lock (_viewerPreloadLock)
        {
            var archive = _archiveSource;
            foreach (var key in _viewerPreloads.Keys.ToList())
            {
                if (targets.Contains(key)) continue;
                var task = _viewerPreloads[key];
                _viewerPreloads.Remove(key);
                DisposeViewerPreloadWhenDone(task);
            }

            foreach (var target in targets)
            {
                if (string.Equals(target, _viewerCurrentPath, StringComparison.OrdinalIgnoreCase)) continue;
                if (_viewerPreloads.ContainsKey(target)) continue;
                _viewerPreloads[target] = Task.Run(() => DecodeViewerImage(target, archive));
            }
        }
    }

    private void ClearViewerPreloads()
    {
        List<Task<Image>> tasks;
        lock (_viewerPreloadLock)
        {
            tasks = _viewerPreloads.Values.ToList();
            _viewerPreloads.Clear();
        }
        foreach (var task in tasks) DisposeViewerPreloadWhenDone(task);
    }

    private static void DisposeViewerPreloadWhenDone(Task<Image> task)
    {
        task.ContinueWith(static t =>
        {
            if (t.Status == TaskStatus.RanToCompletion) t.Result.Dispose();
            else _ = t.Exception;
        }, TaskContinuationOptions.ExecuteSynchronously);
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
        RestoreViewerWindowPlacement();

        _menuBar.Visible = false;
        _sessionTitleLabel.Visible = false;
        _docBar.Visible = false;
        _rightPanel.Visible = false;
        _archiveBrowserPanel.Visible = false;
        _overlayFrame.Visible = false;
        _viewerBar.Visible = true;
        _viewerNavPanel.Visible = false;
        _viewerChromeTimer.Start();
        AdjustViewerBarLayout();

        var first = _startupArgs.Select(a => a.Trim().Trim('"')).FirstOrDefault(File.Exists);
        if (first != null)
        {
            if (_viewerTitle != null) _viewerTitle.Text = Path.GetFileName(first);
            Text = Path.GetFileName(first) + " - Multi Image Canvas";
            if (!ArchiveImageSource.IsSupportedArchivePath(first))
            {
                lock (_viewerPreloadLock)
                {
                    _viewerPreloads[first] = Task.Run(() => DecodeViewerImage(first, null));
                }
            }
        }
    }

    private void RestoreViewerWindowPlacement()
    {
        if (_appSettings.ViewerWindowBounds is not { Length: 4 } saved || saved[2] <= 0 || saved[3] <= 0) return;

        var bounds = new Rectangle(saved[0], saved[1], saved[2], saved[3]);
        var area = Screen.FromRectangle(bounds).WorkingArea;
        bounds.Width = Math.Min(Math.Max(bounds.Width, MinimumSize.Width), area.Width);
        bounds.Height = Math.Min(Math.Max(bounds.Height, MinimumSize.Height), area.Height);
        bounds.X = Math.Clamp(bounds.X, area.Left, area.Right - bounds.Width);
        bounds.Y = Math.Clamp(bounds.Y, area.Top, area.Bottom - bounds.Height);

        StartPosition = FormStartPosition.Manual;
        Bounds = bounds;
        if (_appSettings.ViewerMaximized) WindowState = FormWindowState.Maximized;
    }

    private void SaveViewerWindowPlacement()
    {
        var bounds = _viewerFullscreen
            ? _viewerRestoreBounds
            : WindowState == FormWindowState.Normal ? Bounds : RestoreBounds;
        if (bounds.Width <= 0 || bounds.Height <= 0) return;

        _appSettings.ViewerWindowBounds = [bounds.X, bounds.Y, bounds.Width, bounds.Height];
        _appSettings.ViewerMaximized = _viewerFullscreen
            ? _viewerRestoreWindowState == FormWindowState.Maximized
            : WindowState == FormWindowState.Maximized;
        AppSettingsStore.Save(_appSettings);
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
        // 幅が足りないときにキャプションボタンをオーバーフロー(▼)へ畳ませない。
        // 代わりに AdjustViewerBarLayout で編集ボタン/タイトルを縮める。
        _viewerBar.CanOverflow = false;
        _viewerBar.LayoutStyle = ToolStripLayoutStyle.HorizontalStackWithOverflow;
        WireTitleBarDrag(_viewerBar);

        _viewerEditFullText = " 🖊 " + Loc.T("キャンバスにて編集") + " ";
        var editBtn = new ToolStripButton(_viewerEditFullText)
        {
            Margin = new Padding(2, 2, 2, 2),
            ToolTipText = Loc.T("通常の編集画面に切り替え、この画像を新しいキャンバスに配置した状態にします。"),
        };
        editBtn.Click += (_, _) => SwitchToEditorFromViewer();
        _viewerBar.Items.Add(editBtn);
        _viewerEditBtn = editBtn;
        _viewerBar.SizeChanged += (_, _) => AdjustViewerBarLayout();

        _viewerTitle = new ToolStripLabel("") { ForeColor = Theme.Current.TextSecondary, Margin = new Padding(12, 0, 0, 0) };
        _viewerTitle.MouseDown += (_, e) =>
        {
            if (e.Button == MouseButtons.Left) BeginWindowMove();
        };
        _viewerBar.Items.Add(_viewerTitle);

        AddCaptionButtons(_viewerBar);
    }

    // ビュアーバーの幅に応じてレイアウトを調整する。狭いときはタイトルを隠し、
    // さらに狭ければ編集ボタンをアイコンのみに縮めて、キャプションボタンを常に表示に保つ。
    private void AdjustViewerBarLayout()
    {
        if (_viewerEditBtn == null || _viewerTitle == null) return;

        // 右上のウィンドウ操作は常に優先し、残り幅だけを編集ボタンとタイトルで使う。
        int captionWidth = _viewerBar.Items.OfType<CaptionToolButton>().Sum(button => button.Width + button.Margin.Horizontal);
        int available = _viewerBar.Width - _viewerBar.Padding.Horizontal - captionWidth;

        bool showEdit = available >= 34;
        bool showFullEdit = available >= 210;
        bool showTitle = available >= 210 + 90; // 編集ボタン全文 + タイトルの余地

        if (_viewerEditBtn.Visible != showEdit) _viewerEditBtn.Visible = showEdit;
        var wantedEditText = showFullEdit ? _viewerEditFullText : " 🖊 ";
        if (_viewerEditBtn.Text != wantedEditText) _viewerEditBtn.Text = wantedEditText;
        _viewerEditBtn.AutoToolTip = !showFullEdit;
        if (_viewerTitle.Visible != showTitle) _viewerTitle.Visible = showTitle;
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

        var layout = new Panel
        {
            Location = new Point(_viewerNavPanel.Padding.Left, _viewerNavPanel.Padding.Top),
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
        _gifTrack = new FlatSlider
        {
            Width = 72,
            Height = 26,
            Minimum = 0,
            Maximum = 1,
            Value = 0,
            Margin = new Padding(2, 4, 2, 0),
            BackColor = Theme.Current.Surface,
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
        int contentMaxWidth = Math.Max(1, availableWidth - _viewerNavPanel.Padding.Horizontal);
        int gap = 8;

        bool hasGif = _canvas.ViewerFrameCount > 1;
        bool hasPage = _viewerFiles.Count > 1;
        bool hasFull = _viewerFullscreenBtn != null;
        var gifSize = hasGif && _gifPanel != null ? _gifPanel.PreferredSize : Size.Empty;
        var pageSize = hasPage && _pageGroup != null ? _pageGroup.PreferredSize : Size.Empty;
        var fullSize = _viewerFullscreenBtn?.Size ?? Size.Empty;
        int height = Math.Max(50, Math.Max(Math.Max(gifSize.Height, pageSize.Height), fullSize.Height) + _viewerNavPanel.Padding.Vertical);

        int sideWidth = Math.Max(gifSize.Width, fullSize.Width);
        int desiredContentWidth = hasPage
            ? sideWidth * 2 + pageSize.Width + gap * 2
            : (hasGif ? gifSize.Width + gap : 0) + (hasFull ? fullSize.Width : 0);
        int contentWidth = Math.Min(Math.Max(desiredContentWidth, fullSize.Width), contentMaxWidth);
        int width = contentWidth + _viewerNavPanel.Padding.Horizontal;

        _viewerNavPanel.Size = new Size(width, height);
        _viewerNavPanel.Location = new Point(
            (_canvas.ClientSize.Width - width) / 2,
            _canvas.ClientSize.Height - height - 20);

        _navFlow.Bounds = new Rectangle(_viewerNavPanel.Padding.Left, _viewerNavPanel.Padding.Top, contentWidth, height - _viewerNavPanel.Padding.Vertical);

        if (desiredContentWidth <= contentMaxWidth && hasPage)
        {
            if (_gifPanel != null) _gifPanel.Location = new Point(0, 0);
            if (_pageGroup != null) _pageGroup.Location = new Point((contentWidth - pageSize.Width) / 2, 0);
            if (_viewerFullscreenBtn != null) _viewerFullscreenBtn.Location = new Point(contentWidth - fullSize.Width, 0);
        }
        else
        {
            int x = 0;
            if (hasGif && _gifPanel != null) { _gifPanel.Location = new Point(x, 0); x += gifSize.Width + gap; }
            if (hasPage && _pageGroup != null) { _pageGroup.Location = new Point(x, 0); x += pageSize.Width + gap; }
            if (_viewerFullscreenBtn != null) _viewerFullscreenBtn.Location = new Point(Math.Min(x, Math.Max(0, contentWidth - fullSize.Width)), 0);
        }
        ApplyRoundedRegion(_viewerNavPanel, 12);
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
        UpdateViewerNavPanelBounds();
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
        var step = active ? 0.05f : -0.04f;
        _viewerChromeOpacity = Math.Clamp(_viewerChromeOpacity + step, 0f, 1f);

        if (_viewerChromeOpacity <= 0f)
        {
            var oldBounds = _viewerNavPanel.Bounds;
            _viewerNavPanel.Visible = false;
            _canvas.Invalidate(oldBounds, true);
            _canvas.Update();
            return;
        }

        _viewerNavPanel.Visible = true;
        ApplyViewerNavOpacity(_viewerChromeOpacity);
    }

    private void ApplyViewerNavOpacity(float opacity)
    {
        var t = Theme.Current;
        _viewerNavPanel.BackColor = Blend(t.CanvasBg, t.Surface, opacity * 0.95f);

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
        else if (control is FlatSlider slider)
        {
            slider.BackColor = Blend(Theme.Current.CanvasBg, Theme.Current.Surface, opacity);
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
        _archiveBrowserPanel.Visible = false;
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
        if (_archiveSource != null) _archiveBrowserPanel.Visible = true;
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
        ClearViewerPreloads();

        MinimumSize = new Size(1050, 650); // 編集画面の最小サイズに戻す
        if (Width < 1050 || Height < 650) Size = new Size(Math.Max(Width, 1050), Math.Max(Height, 650));

        var viewerDoc = ActiveDoc;
        _viewerChromeTimer.Stop();
        _viewerNavPanel.Visible = false;
        SetGifControlsVisible(false);
        _canvas.ReadOnlyView = false;
        _canvas.ShutdownViewerAnimation(); // 自前駆動をやめてImageAnimatorへ戻す
        BuildEditorUi();

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
                    if (s.OverlayLocations is { } locations && i < locations.Count && locations[i] is { Length: 2 } location)
                    {
                        doc.OverlayLocation = new Point(location[0], location[1]);
                    }
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
        _docBar.Visible = true;
        _rightPanel.Visible = true;
        _overlayFrame.Visible = true;
        ApplyTreeTheme();
        SetSidebarView(_sidebarView);
        StartLoadQuickAccessExtras();
        EnsureTopBarOrder();

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
        var newCanvasMi = MI("➕ " + Loc.T("新規キャンバスタブ"), "file.newTab", (_, _) => AddDocument(CreateNewCanvasDocument(), true));
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

        _overlayActiveMi = new ToolStripMenuItem("🔲 " + Loc.T("オーバーレイ有効化")) { CheckOnClick = true, ShortcutKeyDisplayString = _keyMap.GetDisplay("overlay.toggle") };
        _overlayActiveMi.Click += (_, _) => ToggleOverlayMode(_overlayActiveMi.Checked);
        menu.DropDownItems.Add(_overlayActiveMi);

        _clickThroughMi = new ToolStripMenuItem("🖱 " + Loc.T("クリック透過")) { CheckOnClick = true, ShortcutKeyDisplayString = _keyMap.GetDisplay("overlay.clickThrough") };
        _clickThroughMi.Click += (_, _) => SetOverlayClickThrough(_clickThroughMi.Checked);
        menu.DropDownItems.Add(_clickThroughMi);

        _overlayFrameMi = new ToolStripMenuItem("▣ " + Loc.T("オーバーレイ外枠")) { CheckOnClick = true, Checked = _overlayFrameVisible };
        _overlayFrameMi.Click += (_, _) => SetOverlayFrameVisible(_overlayFrameMi.Checked);
        menu.DropDownItems.Add(_overlayFrameMi);

        menu.DropDownItems.Add(new ToolStripSeparator());

        _opacityMenuLabel = new ToolStripLabel($"{Loc.T("オーバーレイ透過率")}: {(int)(_overlayOpacity * 100)}%")
        {
            ForeColor = Theme.Current.TextSecondary,
            ToolTipText = $"{_keyMap.GetDisplay("overlay.opacityUp")} / {_keyMap.GetDisplay("overlay.opacityDown")}",
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
        if (_overlayActiveMi != null) _overlayActiveMi.ShortcutKeyDisplayString = _keyMap.GetDisplay("overlay.toggle");
        if (_clickThroughMi != null) _clickThroughMi.ShortcutKeyDisplayString = _keyMap.GetDisplay("overlay.clickThrough");
        if (_opacityMenuLabel != null) _opacityMenuLabel.ToolTipText = $"{_keyMap.GetDisplay("overlay.opacityUp")} / {_keyMap.GetDisplay("overlay.opacityDown")}";
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
        RemoveCanvasShortcutConflictsWithKeyMap();
        ReregisterGlobalHotkeys();
        SaveSettings();
    }

    // ===== メイン配置 =====

    private void BuildMainLayout()
    {
        _canvas.Dock = DockStyle.Fill;

        BuildViewerNavPanel();

        _canvas.Controls.Add(_viewerNavPanel);

        ApplyRoundedRegion(_viewerNavPanel, 12);

        Controls.Add(_canvas);
        BuildViewerBar();
        Controls.Add(_viewerBar);

        _viewerNavPanel.BringToFront();

        _canvas.SizeChanged += (_, _) =>
        {
            if (_editorUiBuilt) UpdateSidebarBounds();
            UpdateViewerNavPanelBounds();
            if (_archiveBrowserBuilt) UpdateArchiveBrowserBounds();
        };
    }

    private void BuildEditorUi()
    {
        if (_editorUiBuilt) return;
        _editorUiBuilt = true;

        BuildMenuBar();
        BuildRightPanel();
        BuildItemPanel();
        BuildOverlayFrame();

        _canvas.Controls.Add(_rightPanel);
        _canvas.Controls.Add(_itemPanel);
        _canvas.Controls.Add(_overlaySettingsPanel);
        _canvas.Controls.Add(_overlayFrame);

        ApplyRoundedRegion(_rightPanel, CornerRadius + 4);
        ApplyRoundedRegion(_itemPanel, CornerRadius + 4);
        ApplyRoundedRegion(_overlaySettingsPanel, CornerRadius + 4);
        ApplyRoundedRegion(_overlayFrame, CornerRadius + 4);

        Controls.Add(_docBar);
        Controls.Add(_menuBar);
        BuildSessionTitleLabel();
        EnsureTopBarOrder();

        _rightPanel.BringToFront();
        _itemPanel.BringToFront();
        _overlayFrame.BringToFront();
        _viewerNavPanel.BringToFront();
        _archiveBrowserPanel.BringToFront();

        WireCanvasEvents();
        BuildInitialTree();
        UpdateSidebarBounds();
    }

    private void EnsureTopBarOrder()
    {
        Controls.SetChildIndex(_canvas, 0);
        Controls.SetChildIndex(_viewerBar, 1);
        Controls.SetChildIndex(_docBar, 2);
        Controls.SetChildIndex(_menuBar, 3);
    }

    private void BuildRightPanel()
    {
        _rightPanel.Padding = new Padding(12);
        _rightPanel.BackColor = Theme.Current.Surface;
        _rightPanel.AutoScroll = true;

        // 表示切替は左、最小化は右にまとめる。
        _sidebarHeader = new Panel { Dock = DockStyle.Top, Height = 36, BackColor = Theme.Current.Surface };
        _sidebarSwitchRow = new FlowLayoutPanel
        {
            Dock = DockStyle.Left,
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
            _sidebarToolTip.SetToolTip(b, tip);
            return b;
        }

        _viewTreeBtn = MakeViewBtn("▤", Loc.T("フォルダツリー"), "tree");
        _viewThumbsBtn = MakeViewBtn("🖼", Loc.T("サムネイル一覧"), "thumbs");
        _viewLayersBtn = MakeViewBtn("📚", Loc.T("レイヤー"), "layers");
        _sidebarSwitchRow.Controls.Add(_viewTreeBtn);
        _sidebarSwitchRow.Controls.Add(_viewThumbsBtn);
        _sidebarSwitchRow.Controls.Add(_viewLayersBtn);

        _sidebarCollapseBtn = new RoundedFlatButton
        {
            Text = "−",
            Dock = DockStyle.Right,
            Width = 34,
            Height = 28,
            ForeColor = Theme.Current.TextPrimary,
            CornerRadius = 8,
            Cursor = Cursors.Hand,
        };
        _sidebarToolTip.SetToolTip(_sidebarCollapseBtn, Loc.T("ファイル選択メニューを最小化"));
        _sidebarCollapseBtn.Click += (_, _) => SetSidebarCollapsed(!_sidebarCollapsed);

        _sidebarHeader.Controls.Add(_sidebarSwitchRow);
        _sidebarHeader.Controls.Add(_sidebarCollapseBtn);

        _sidebarPathPanel = BuildPathPanel();

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
        _rightPanel.Controls.Add(_sidebarPathPanel);
        _rightPanel.Controls.Add(_sidebarHeader);
    }

    private void SetSidebarCollapsed(bool collapsed)
    {
        Rectangle previousBounds = _rightPanel.Bounds;
        bool redrawSuspended = _rightPanel.IsHandleCreated;
        bool headerRedrawSuspended = _sidebarHeader?.IsHandleCreated == true;
        if (redrawSuspended) SendMessage(_rightPanel.Handle, WM_SETREDRAW, 0, 0);
        if (headerRedrawSuspended) SendMessage(_sidebarHeader!.Handle, WM_SETREDRAW, 0, 0);
        try
        {
            _rightPanel.SuspendLayout();
            _sidebarHeader?.SuspendLayout();

            _sidebarCollapsed = collapsed;
            _sidebarContent.Visible = false;
            if (_sidebarPathPanel != null) _sidebarPathPanel.Visible = false;
            if (_sidebarSwitchRow != null) _sidebarSwitchRow.Visible = false;
            _rightPanel.AutoScroll = false;
            _rightPanel.Padding = new Padding(12);

            if (_sidebarHeader != null) _sidebarHeader.Dock = collapsed ? DockStyle.Fill : DockStyle.Top;
            if (_sidebarCollapseBtn != null)
            {
                _sidebarCollapseBtn.Text = collapsed ? "▣" : "−";
                _sidebarCollapseBtn.Dock = collapsed ? DockStyle.Fill : DockStyle.Right;
                _sidebarCollapseBtn.Width = 34;
                _sidebarToolTip.SetToolTip(_sidebarCollapseBtn,
                    Loc.T(collapsed ? "ファイル選択メニューを復元" : "ファイル選択メニューを最小化"));
            }

            if (!collapsed)
            {
                _sidebarContent.Visible = true;
                if (_sidebarPathPanel != null) _sidebarPathPanel.Visible = true;
                if (_sidebarSwitchRow != null) _sidebarSwitchRow.Visible = true;
                _rightPanel.AutoScroll = true;
            }

            UpdateSidebarBounds();

            _sidebarHeader?.ResumeLayout(true);
            _rightPanel.ResumeLayout(true);
            _rightPanel.BackColor = Theme.Current.Surface;
        }
        finally
        {
            if (headerRedrawSuspended) SendMessage(_sidebarHeader!.Handle, WM_SETREDRAW, 1, 0);
            if (redrawSuspended) SendMessage(_rightPanel.Handle, WM_SETREDRAW, 1, 0);
            _sidebarHeader?.Invalidate(true);
            _rightPanel.Invalidate(true);
            _canvas.Invalidate(Rectangle.Union(previousBounds, _rightPanel.Bounds), true);
            _canvas.Update();
        }
    }

    private void BuildArchiveBrowserPanel()
    {
        _archiveBrowserPanel.Visible = false;
        _archiveBrowserPanel.BackColor = Theme.Current.Surface;
        _archiveBrowserPanel.Padding = new Padding(12);

        var title = new Label
        {
            Text = Loc.T("圧縮ファイル"),
            Dock = DockStyle.Top,
            Height = 30,
            TextAlign = ContentAlignment.MiddleLeft,
            Font = new Font(Font.FontFamily, 9.5f, FontStyle.Bold),
            ForeColor = Theme.Current.TextPrimary,
            BackColor = Theme.Current.Surface,
        };

        _archiveSearchBox.Dock = DockStyle.Top;
        _archiveSearchBox.Height = 30;
        _archiveSearchBox.PlaceholderText = Loc.T("フォルダ、画像を検索");
        _archiveSearchBox.BorderStyle = BorderStyle.FixedSingle;
        _archiveSearchBox.TextChanged += (_, _) => RebuildArchiveTree();

        var searchSpacer = new Panel { Dock = DockStyle.Top, Height = 8, BackColor = Theme.Current.Surface };
        _archiveTree.Dock = DockStyle.Fill;
        _archiveTree.BorderStyle = BorderStyle.None;
        _archiveTree.HideSelection = false;
        _archiveTree.ShowLines = true;
        _archiveTree.ShowRootLines = false;
        _archiveTree.BackColor = Theme.Current.Surface;
        _archiveTree.ForeColor = Theme.Current.TextPrimary;
        _archiveTree.AfterSelect += (_, e) => HandleArchiveTreeSelection(e.Node);

        _archiveBrowserPanel.Controls.Add(_archiveTree);
        _archiveBrowserPanel.Controls.Add(searchSpacer);
        _archiveBrowserPanel.Controls.Add(_archiveSearchBox);
        _archiveBrowserPanel.Controls.Add(title);
    }

    private void EnsureArchiveBrowserBuilt()
    {
        if (_archiveBrowserBuilt) return;
        _archiveBrowserBuilt = true;
        BuildArchiveBrowserPanel();
        _canvas.Controls.Add(_archiveBrowserPanel);
        ApplyRoundedRegion(_archiveBrowserPanel, CornerRadius + 4);
        _archiveBrowserPanel.BringToFront();
    }

    private void ShowArchiveBrowser(ArchiveImageSource source)
    {
        _archiveSearchBox.Clear();
        _archiveBrowserPanel.Visible = true;
        _archiveBrowserPanel.BringToFront();
        if (_viewerEditBtn != null)
        {
            _viewerEditBtn.Enabled = false;
            _viewerEditBtn.ToolTipText = Loc.T("圧縮ファイル内の画像はビュアーでのみ表示できます。");
        }
        RebuildArchiveTree();
        UpdateArchiveBrowserBounds();
    }

    private void HideArchiveBrowser()
    {
        _archiveBrowserPanel.Visible = false;
        if (_viewerEditBtn != null)
        {
            _viewerEditBtn.Enabled = true;
            _viewerEditBtn.ToolTipText = Loc.T("通常の編集画面に切り替え、この画像を新しいキャンバスに配置した状態にします。");
        }
    }

    private async Task OpenArchiveViewerAsync(string path)
    {
        try
        {
            EnsureArchiveBrowserBuilt();
            ClearViewerPreloads();
            var source = await Task.Run(() => new ArchiveImageSource(path));
            if (source.Entries.Count == 0) throw new InvalidDataException(Loc.T("圧縮ファイル内に対応画像がありません。"));

            _archiveSource = source;
            _viewerFiles = source.Entries.Select(e => e.Key)
                .OrderBy(k => k, StringComparer.CurrentCultureIgnoreCase)
                .ToList();
            _viewerFileIndex = 0;
            _viewerCurrentPath = null;
            ShowArchiveBrowser(source);
            await OpenViewerFileAsync(_viewerFiles[0], ++_viewerOpenVersion);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, Loc.T("圧縮ファイルを読み込めません"), MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void RebuildArchiveTree()
    {
        if (_archiveSource == null) return;
        var query = _archiveSearchBox.Text.Trim();
        var entries = _archiveSource.Entries
            .Where(e => query.Length == 0 || e.Key.Contains(query, StringComparison.CurrentCultureIgnoreCase))
            .OrderBy(e => e.Key, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        _archiveTree.BeginUpdate();
        try
        {
            _archiveTree.Nodes.Clear();
            var root = new TreeNode(Path.GetFileName(_archiveSource.ArchivePath)) { Tag = "A:" };
            _archiveTree.Nodes.Add(root);

            foreach (var entry in entries)
            {
                var parent = root;
                var folder = string.Empty;
                foreach (var segment in entry.Folder.Split('/', StringSplitOptions.RemoveEmptyEntries))
                {
                    folder = folder.Length == 0 ? segment : $"{folder}/{segment}";
                    var child = parent.Nodes.Cast<TreeNode>().FirstOrDefault(n => string.Equals(n.Text, segment, StringComparison.Ordinal));
                    if (child == null)
                    {
                        child = new TreeNode(segment) { Tag = "D:" + folder };
                        parent.Nodes.Add(child);
                    }
                    parent = child;
                }
                parent.Nodes.Add(new TreeNode(entry.FileName) { Tag = "I:" + entry.Key });
            }

            root.Expand();
            if (query.Length > 0) ExpandArchiveTree(root);
        }
        finally
        {
            _archiveTree.EndUpdate();
        }
    }

    private static void ExpandArchiveTree(TreeNode node)
    {
        node.Expand();
        foreach (TreeNode child in node.Nodes)
        {
            if (child.Tag is string tag && tag.StartsWith("D:", StringComparison.Ordinal)) ExpandArchiveTree(child);
        }
    }

    private void HandleArchiveTreeSelection(TreeNode? node)
    {
        if (_archiveSource == null || node?.Tag is not string tag) return;
        if (tag == "A:")
        {
            SetArchivePageList(_archiveSource.Entries.Select(e => e.Key));
        }
        else if (tag.StartsWith("D:", StringComparison.Ordinal))
        {
            var folder = tag[2..];
            var keys = _archiveSource.GetImageKeys(folder);
            if (keys.Count == 0)
            {
                var prefix = folder + "/";
                keys = _archiveSource.Entries.Where(e => e.Folder.StartsWith(prefix, StringComparison.Ordinal)).Select(e => e.Key).ToList();
            }
            SetArchivePageList(keys);
        }
        else if (tag.StartsWith("I:", StringComparison.Ordinal))
        {
            var key = tag[2..];
            var folder = _archiveSource.Entries.FirstOrDefault(e => e.Key == key)?.Folder ?? string.Empty;
            SetArchivePageList(_archiveSource.GetImageKeys(folder), key);
        }
    }

    private void SetArchivePageList(IEnumerable<string> keys, string? selectedKey = null)
    {
        var files = keys.OrderBy(Path.GetFileName, StringComparer.CurrentCultureIgnoreCase).ToList();
        if (files.Count == 0) return;
        _viewerFiles = files;
        _viewerFileIndex = selectedKey == null ? 0 : files.FindIndex(k => string.Equals(k, selectedKey, StringComparison.Ordinal));
        if (_viewerFileIndex < 0) _viewerFileIndex = 0;
        ClearViewerPreloads();
        _ = OpenViewerFileAsync(_viewerFiles[_viewerFileIndex], ++_viewerOpenVersion);
        ShowViewerChrome();
    }

    private void UpdateArchiveBrowserBounds()
    {
        if (!_archiveBrowserPanel.Visible) return;
        const int margin = 12;
        int width = Math.Min(280, Math.Max(160, _canvas.ClientSize.Width - margin * 2));
        int height = Math.Max(80, _canvas.ClientSize.Height - margin * 2);
        _archiveBrowserPanel.Bounds = new Rectangle(Math.Max(margin, _canvas.ClientSize.Width - margin - width), margin, width, height);
        ApplyRoundedRegion(_archiveBrowserPanel, CornerRadius + 4);
    }

    private void SetSidebarView(string mode, bool loadThumbs = true)
    {
        _sidebarView = mode;
        if (_treeArea != null) _treeArea.Visible = mode == "tree";
        if (_thumbs != null)
        {
            _thumbs.Visible = mode == "thumbs";
            if (loadThumbs && mode == "thumbs")
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
        bool visible = _itemPanelRequested && !_viewerMode && !_canvas.ReadOnlyView && _canvas.Selected != null && !_uiHidden && !_canvas.IsInteracting;

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
        _sidebarOverlayBtn.CornerRadius = 12;

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

    private const int OverlaySettingsHeight = 232;

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

        _ovlFrameCheck = new CheckBox
        {
            Text = Loc.T("オーバーレイ外枠"),
            Dock = DockStyle.Top,
            Height = 32,
            ForeColor = t.TextPrimary,
            BackColor = t.Surface,
            TabStop = false,
        };
        _ovlFrameCheck.CheckedChanged += (_, _) =>
        {
            if (_syncingOverlayPanel) return;
            SetOverlayFrameVisible(_ovlFrameCheck.Checked);
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

        var resetPositionBtn = new RoundedFlatButton
        {
            Text = Loc.T("オーバーレイの表示位置をリセット"),
            Dock = DockStyle.Top,
            Height = 32,
            ForeColor = t.TextPrimary,
            BaseColor = t.ButtonBg,
            CornerRadius = 8,
            Cursor = Cursors.Hand,
        };
        resetPositionBtn.Click += (_, _) => ResetOverlayLocation();

        // Dock=Top は後から追加したものが上に積まれるため逆順で追加する
        _overlaySettingsPanel.Controls.Add(resetPositionBtn);
        _overlaySettingsPanel.Controls.Add(_ovlOpacitySlider);
        _overlaySettingsPanel.Controls.Add(_ovlOpacityLabel);
        _overlaySettingsPanel.Controls.Add(_ovlFrameCheck);
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
            if (_ovlFrameCheck != null) _ovlFrameCheck.Checked = _overlayFrameVisible;
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
        int clientWidth = Math.Max(1, _canvas.ClientSize.Width);
        int clientHeight = Math.Max(1, _canvas.ClientSize.Height);
        int scrollbarInset = Math.Max(margin, CanvasSurface.ScrollbarSafeInset);
        int panelWidth = Math.Min(_sidebarWidth, Math.Max(120, clientWidth - margin - scrollbarInset));
        int rightX = Math.Clamp(clientWidth - scrollbarInset - panelWidth, margin, Math.Max(margin, clientWidth - panelWidth));

        _overlayFrame.Size = new Size(panelWidth, overlayHeight);
        _overlayFrame.Location = new Point(rightX, Math.Max(margin, clientHeight - scrollbarInset - overlayHeight));

        int rightTop = margin;
        int rightHeight = Math.Max(40, _overlayFrame.Top - gap - rightTop);
        int pickerWidth = _sidebarCollapsed ? 58 : panelWidth;
        int pickerHeight = _sidebarCollapsed ? 58 : rightHeight;
        _rightPanel.Size = new Size(pickerWidth, pickerHeight);
        _rightPanel.Location = new Point(clientWidth - scrollbarInset - pickerWidth, rightTop);
        _rightPanel.BackColor = Theme.Current.Surface;
        _sidebarContent.BackColor = Theme.Current.Surface;
        if (_treeArea != null) _treeArea.BackColor = Theme.Current.TreeBg;
        ApplyRoundedRegion(_rightPanel, CornerRadius + 4);

        // オーバーレイ設定パネルはファイル選択画面の下部に重ねる
        int settingsHeight = Math.Min(OverlaySettingsHeight, Math.Max(60, clientHeight - margin * 2));
        int settingsY = Math.Clamp(_overlayFrame.Top - gap - settingsHeight, margin, Math.Max(margin, clientHeight - margin - settingsHeight));
        _overlaySettingsPanel.Size = new Size(panelWidth, settingsHeight);
        _overlaySettingsPanel.Location = new Point(rightX, settingsY);

        UpdateItemPanelPosition();
    }

    // ===== キャンバスイベント =====

    private void WireCanvasEvents()
    {
        _canvas.ZoomChanged += (_, _) => { UpdateZoomText(); UpdateItemPanelPosition(); };
        _canvas.MouseDown += (_, _) =>
        {
            _itemPanelRequested = false;
            UpdateItemPanel();
            if (_uiHidden) RestoreUi();
        };
        _canvas.SelectionChanged += (_, _) =>
        {
            _itemPanelRequested = false;
            SyncMenuState();
            UpdateItemPanel();
        };
        // パンやドラッグ移動に合わせて選択メニューを追従させる
        // 操作中の非表示⇔操作後の再表示も含めて毎回評価する
        _canvas.CanvasUpdated += (_, _) => UpdateItemPanel();
        _canvas.ItemContextMenuRequested += Canvas_ItemContextMenuRequested;
    }

    private void Canvas_ItemContextMenuRequested(object? sender, ItemContextMenuEventArgs e)
    {
        _itemPanelRequested = true;
        UpdateItemPanel();
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
        else if (ArchiveImageSource.IsSupportedArchivePath(fileName))
        {
            OpenArchiveInViewer(fileName);
        }
        else
        {
            MessageBox.Show(this, Loc.T("対応しているセッション、キャンバス、画像、圧縮ファイルではありません。"), Loc.T("開けません"), MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
    }

    private void OpenArchiveInViewer(string fileName)
    {
        var executable = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(executable)) throw new InvalidOperationException(Loc.T("ビュアーを起動できません。"));
        var startInfo = new System.Diagnostics.ProcessStartInfo(executable) { UseShellExecute = true };
        startInfo.ArgumentList.Add(fileName);
        System.Diagnostics.Process.Start(startInfo);
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
        string? path = null;
        try
        {
            path = await DownloadImageAsync(uri);
            var image = ImageDecoder.Decode(path);
            _canvas.AddImage(image, path);
            return true;
        }
        catch (Exception ex)
        {
            if (path != null)
            {
                try { File.Delete(path); } catch { }
            }
            if (showError) MessageBox.Show(this, ex.Message, Loc.T("URL画像を読み込めません"), MessageBoxButtons.OK, MessageBoxIcon.Error);
            return false;
        }
    }

    private static async Task<string> DownloadImageAsync(Uri uri)
    {
        using var response = await Http.GetAsync(uri);
        response.EnsureSuccessStatusCode();
        var mediaType = response.Content.Headers.ContentType?.MediaType ?? "";
        if (mediaType.StartsWith("text/", StringComparison.OrdinalIgnoreCase)
            || mediaType.Equals("application/xhtml+xml", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(Loc.T("URLの応答は画像ではありません。"));
        }
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

    internal static IEnumerable<string> ExtractUrlsFromData(IDataObject data)
    {
        foreach (var format in new[] { "UniformResourceLocatorW", "UniformResourceLocator", DataFormats.UnicodeText, DataFormats.Text, DataFormats.Html })
        {
            if (!data.GetDataPresent(format)) continue;
            if (data.GetData(format) is not string text) continue;
            foreach (Match match in Regex.Matches(text, @"https?://[^\s""'<>]+", RegexOptions.IgnoreCase))
            {
                yield return System.Net.WebUtility.HtmlDecode(match.Value);
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
        if (decision == SessionDecision.Save && !SaveSessionToFile()) return;

        ClearDocuments();
        _sessionFilePath = null;
        UpdateSessionTitle();
        AddDocument(CreateNewCanvasDocument(), select: true);
        SaveSession();
    }

    private bool SaveSessionAs()
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
            return true;
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, Loc.T("保存失敗"), MessageBoxButtons.OK, MessageBoxIcon.Error);
            return false;
        }
    }

    private bool SaveSessionToFile()
    {
        if (string.IsNullOrEmpty(_sessionFilePath))
        {
            return SaveSessionAs();
        }

        try
        {
            SaveSessionPackage(_sessionFilePath);
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
        var data = CreateSessionData(includeOverlayLocations: false);
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
        const string archiveExts = "*.zip;*.rar;*.7z;*.cbz;*.cbr;*.cb7";
        using var dlg = new OpenFileDialog
        {
            Filter = $"対応ファイル (*.mics;*.micl;*.json;{imageExts};{archiveExts})|*.mics;*.micl;*.json;{imageExts};{archiveExts}|セッションファイル (*.mics)|*.mics|キャンバスファイル (*.micl;*.json)|*.micl;*.json|画像ファイル ({imageExts})|{imageExts}|圧縮ファイル ({archiveExts})|{archiveExts}|すべてのファイル (*.*)|*.*",
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
        _overlayFrameVisible = session.OverlayFrameVisible;
        _sidebarView = session.SidebarView;

        for (int i = 0; i < session.Tabs.Count; i++)
        {
            try
            {
                var doc = LayoutSerializer.FromDto(session.Tabs[i], i < session.TabFilePaths.Count ? session.TabFilePaths[i] : null);
                RestoreCanvasSessionState(session, i, doc);
                AddDocument(doc, select: false);
            }
            catch
            {
                // 個別キャンバスの復元失敗は無視
            }
        }

        if (_docs.Count == 0) AddDocument(CreateNewCanvasDocument(), select: true);
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
                StoreOverlayLocation(ActiveDoc, _overlayForm);
                _overlayForm.Close();
                _overlayForm = null;
            }

            var clientRect = _canvas.RectangleToScreen(_canvas.ClientRectangle);
            var doc = ActiveDoc;
            var location = GetOverlayLocation(doc, clientRect);
            var overlay = new OverlayForm(_canvas, _overlayClickThrough, _overlayOpacity, OverlayAnimations.Parse(_overlayAnimation), _overlayFrameVisible)
            {
                StartPosition = FormStartPosition.Manual,
                Location = location,
                Width = clientRect.Width,
                Height = clientRect.Height,
            };
            _overlayForm = overlay;
            if (doc != null) doc.OverlayLocation = location;

            overlay.UserMoved += (_, _) =>
            {
                StoreOverlayLocation(ActiveDoc);
                SaveSession();
            };
            overlay.FormClosed += (_, _) =>
            {
                if (ReferenceEquals(_overlayForm, overlay)) StoreOverlayLocation(ActiveDoc, overlay);
                _canvas.CanvasUpdated -= Canvas_CanvasUpdated;
                if (ReferenceEquals(_overlayForm, overlay)) _overlayForm = null;
                UpdateOverlayButtonsState(false);
            };

            _canvas.CanvasUpdated += Canvas_CanvasUpdated;
            overlay.Show();
            SaveSession();
        }
        else
        {
            if (_overlayForm is { } overlay)
            {
                var doc = ActiveDoc;
                StoreOverlayLocation(doc, overlay);
                _canvas.CanvasUpdated -= Canvas_CanvasUpdated;
                _overlayForm = null;
                overlay.Close();
                SaveSession();
            }
        }
        UpdateOverlayButtonsState(enabled);
    }

    private Point GetOverlayLocation(CanvasDocument? doc, Rectangle clientRect)
    {
        if (doc?.OverlayLocation is { } saved)
        {
            var savedBounds = new Rectangle(saved, clientRect.Size);
            if (Screen.AllScreens.Any(screen => screen.WorkingArea.IntersectsWith(savedBounds))) return saved;
        }
        return new Point(clientRect.X + 20, clientRect.Y + 20);
    }

    private void SwitchOverlayToDocument(CanvasDocument doc)
    {
        if (_overlayForm == null) return;
        var clientRect = _canvas.RectangleToScreen(_canvas.ClientRectangle);
        var location = GetOverlayLocation(doc, clientRect);
        doc.OverlayLocation ??= location;
        _overlayForm.SwitchCanvas(location, () => _canvas.Document = doc);
    }

    private void StoreOverlayLocation(CanvasDocument? doc, OverlayForm? overlay = null)
    {
        overlay ??= _overlayForm;
        if (doc != null && overlay != null) doc.OverlayLocation = overlay.Location;
    }

    private void ResetOverlayLocation()
    {
        var doc = ActiveDoc;
        if (doc == null) return;

        doc.OverlayLocation = null;
        if (_overlayForm != null)
        {
            var clientRect = _canvas.RectangleToScreen(_canvas.ClientRectangle);
            var location = GetOverlayLocation(doc, clientRect);
            _overlayForm.Location = location;
            doc.OverlayLocation = location;
        }
        SaveSession();
    }

    private void Canvas_CanvasUpdated(object? sender, EventArgs e) => _overlayForm?.Invalidate();

    private void UpdateOverlayButtonsState(bool active)
    {
        if (_overlayActiveMi != null) _overlayActiveMi.Checked = active;
        if (_overlayFrameMi != null) _overlayFrameMi.Checked = _overlayFrameVisible;
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
        if (_overlayForm != null) _overlayForm.SetTargetOpacity(_overlayOpacity);
        if (_overlaySettingsPanel.Visible) SyncOverlaySettingsPanel();
    }

    private void SetOverlayClickThrough(bool value)
    {
        _overlayClickThrough = value;
        if (_clickThroughMi != null) _clickThroughMi.Checked = value;
        if (_overlayForm != null) _overlayForm.ClickThrough = value;
        if (_overlaySettingsPanel.Visible) SyncOverlaySettingsPanel();
    }

    private void SetOverlayFrameVisible(bool value)
    {
        _overlayFrameVisible = value;
        if (_overlayFrameMi != null) _overlayFrameMi.Checked = value;
        if (_overlayForm != null) _overlayForm.ShowFrame = value;
        if (_overlaySettingsPanel.Visible) SyncOverlaySettingsPanel();
        if (IsHandleCreated) SaveSession();
    }

    // ===== グローバルホットキー =====

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        UpdateWindowRegion();
        RegisterGlobalHotkeys();
    }

    protected override void OnHandleDestroyed(EventArgs e)
    {
        UnregisterGlobalHotkeys();
        base.OnHandleDestroyed(e);
    }

    private void ReregisterGlobalHotkeys()
    {
        if (!IsHandleCreated) return;
        UnregisterGlobalHotkeys();
        RegisterGlobalHotkeys();
    }

    private void RegisterGlobalHotkeys()
    {
        RegisterConfiguredHotKey(HotkeyToggleOverlay, "overlay.toggle");
        RegisterConfiguredHotKey(HotkeyClickThrough, "overlay.clickThrough");
        RegisterConfiguredHotKey(HotkeyOpacityDown, "overlay.opacityDown");
        RegisterConfiguredHotKey(HotkeyOpacityUp, "overlay.opacityUp");
        RegisterConfiguredHotKey(HotkeyCanvasNext, "tab.next");
        RegisterConfiguredHotKey(HotkeyCanvasPrev, "tab.prev");
        RegisterCanvasHotkeys();
    }

    private void UnregisterGlobalHotkeys()
    {
        UnregisterHotKey(Handle, HotkeyToggleOverlay);
        UnregisterHotKey(Handle, HotkeyClickThrough);
        UnregisterHotKey(Handle, HotkeyOpacityDown);
        UnregisterHotKey(Handle, HotkeyOpacityUp);
        UnregisterHotKey(Handle, HotkeyCanvasNext);
        UnregisterHotKey(Handle, HotkeyCanvasPrev);
        foreach (var id in _directCanvasHotkeys.Keys) UnregisterHotKey(Handle, id);
        _directCanvasHotkeys.Clear();
    }

    private void RegisterConfiguredHotKey(int id, string actionId)
    {
        TryRegisterHotKey(id, _keyMap.Get(actionId));
    }

    private void RegisterCanvasHotkeys()
    {
        int id = HotkeyCanvasDirectBase;
        var failed = new List<CanvasDocument>();
        foreach (var doc in _docs.Where(d => d.SwitchShortcut != Keys.None))
        {
            if (TryRegisterHotKey(id, doc.SwitchShortcut)) _directCanvasHotkeys[id] = doc;
            else failed.Add(doc);
            id++;
        }

        if (failed.Count == 0) return;
        foreach (var doc in failed) doc.SwitchShortcut = Keys.None;
        if (!IsHandleCreated) return;
        BeginInvoke(() =>
        {
            if (IsDisposed) return;
            RebuildDocTabs();
            SaveSession();
        });
    }

    private bool TryRegisterHotKey(int id, Keys keys)
    {
        var keyCode = keys & Keys.KeyCode;
        if (keys == Keys.None || keyCode == Keys.None) return false;

        uint modifiers = 0;
        if ((keys & Keys.Control) != 0) modifiers |= MOD_CONTROL;
        if ((keys & Keys.Alt) != 0) modifiers |= MOD_ALT;
        if ((keys & Keys.Shift) != 0) modifiers |= MOD_SHIFT;
        return RegisterHotKey(Handle, id, modifiers, (uint)keyCode);
    }

    private void RemoveCanvasShortcutConflictsWithKeyMap()
    {
        var conflicts = _docs.Where(d => d.SwitchShortcut != Keys.None && _keyMap.FindByKeys(d.SwitchShortcut) != null).ToList();
        if (conflicts.Count == 0) return;
        foreach (var doc in conflicts) doc.SwitchShortcut = Keys.None;
        RebuildDocTabs();
        SaveSession();
        MessageBox.Show(this,
            string.Format(Loc.T("設定したキーと競合したため、次のキャンバスの切り替えキーを解除しました:\n{0}"),
                string.Join(Environment.NewLine, conflicts.Select(d => d.Name))),
            Loc.T("キーの競合"), MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    // ===== ウィンドウ枠 =====

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ClassStyle |= 0x00020000; // CS_DROPSHADOW
            // OSのウィンドウ管理(Aeroスナップ/Win+矢印/スナップレイアウト/最大化・最小化)を
            // 有効化する。枠自体は WM_NCCALCSIZE で消してborderless外観を維持する。
            cp.Style |= WS_THICKFRAME | WS_MAXIMIZEBOX | WS_MINIMIZEBOX;
            return cp;
        }
    }

    protected override void OnSizeChanged(EventArgs e)
    {
        base.OnSizeChanged(e);
        if (!_liveResizing)
        {
            UpdateWindowRegion();
            QueueOverlaySizeSync();
        }
        PositionSessionTitle();
        UpdateCaptionGlyphs();
    }

    protected override void OnResizeBegin(EventArgs e)
    {
        _liveResizing = true;
        base.OnResizeBegin(e);
    }

    protected override void OnResizeEnd(EventArgs e)
    {
        base.OnResizeEnd(e);
        _liveResizing = false;
        UpdateWindowRegion();
        QueueOverlaySizeSync();
    }

    private void QueueOverlaySizeSync()
    {
        if (_overlayForm == null || _overlayResizeSyncPending || !IsHandleCreated) return;
        _overlayResizeSyncPending = true;
        BeginInvoke(() =>
        {
            _overlayResizeSyncPending = false;
            if (IsDisposed || _liveResizing) return;
            _overlayForm?.ResizeCanvas(_canvas.ClientSize);
        });
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        UpdateWindowRegion();
    }

    private void UpdateWindowRegion()
    {
        // 全画面表示中・最大化中は角丸にせず画面全体を覆う
        // (スナップ最大化時に画面隅へ隙間が出るのを防ぐ)
        if (_viewerFullscreen || WindowState == FormWindowState.Maximized)
        {
            if (Region != null) Region = null;
            SetNativeWindowCorners(false);
            return;
        }

        // Win11ではDWMの角丸を使う。Region切り抜きと違い、外周にもアンチエイリアスが効く。
        if (SetNativeWindowCorners(true))
        {
            if (Region != null) Region = null;
        }
        else if (Width > 0 && Height > 0)
        {
            using var path = CreateRoundedRectPath(ClientRectangle, CornerRadius);
            Region = new Region(path);
        }
    }

    private bool SetNativeWindowCorners(bool rounded)
    {
        if (!IsHandleCreated || !OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000)) return false;
        if (_nativeCornerHandle == Handle && _nativeCornersRounded == rounded) return true;
        var preference = rounded ? DWMWCP_ROUND : DWMWCP_DONOTROUND;
        if (DwmSetWindowAttribute(Handle, DWMWA_WINDOW_CORNER_PREFERENCE,
                ref preference, Marshal.SizeOf<int>()) < 0) return false;
        _nativeCornerHandle = Handle;
        _nativeCornersRounded = rounded;
        return true;
    }

    protected override void WndProc(ref Message m)
    {
        const int WM_MOUSEACTIVATE = 0x0021;
        const int MA_ACTIVATE = 1;
        const int gripSize = 8;

        // 非クライアント枠を消し、ウィンドウ全体をクライアント領域にする。
        // WS_THICKFRAME を付けてもborderless外観を保つための要。
        if (m.Msg == WM_NCCALCSIZE)
        {
            m.Result = IntPtr.Zero;
            return;
        }
        if (m.Msg == WM_NCACTIVATE)
        {
            m.Result = new IntPtr(1);
            return;
        }
        if (m.Msg == WM_NCPAINT)
        {
            m.Result = IntPtr.Zero;
            return;
        }

        // 最大化時に作業領域(タスクバーを除く)へ収める。
        if (m.Msg == WM_GETMINMAXINFO)
        {
            var screen = Screen.FromHandle(Handle);
            var wa = screen.WorkingArea;
            var mon = screen.Bounds;
            var mmi = Marshal.PtrToStructure<MINMAXINFO>(m.LParam);
            mmi.ptMaxPosition.x = wa.Left - mon.Left;
            mmi.ptMaxPosition.y = wa.Top - mon.Top;
            mmi.ptMaxSize.x = wa.Width;
            mmi.ptMaxSize.y = wa.Height;
            mmi.ptMinTrackSize.x = MinimumSize.Width;
            mmi.ptMinTrackSize.y = MinimumSize.Height;
            Marshal.StructureToPtr(mmi, m.LParam, false);
            m.Result = IntPtr.Zero;
            return;
        }

        if (m.Msg == WM_MOUSEACTIVATE)
        {
            m.Result = MA_ACTIVATE;
            return;
        }

        if (m.Msg == WM_HOTKEY)
        {
            int hotkeyId = m.WParam.ToInt32();
            if (_directCanvasHotkeys.TryGetValue(hotkeyId, out var directCanvas))
            {
                int index = _docs.IndexOf(directCanvas);
                if (index >= 0) SelectDocument(index);
                return;
            }

            switch (hotkeyId)
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

        // ===== スナップレイアウト対応: 最大化ボタンを HTMAXBUTTON として扱う =====
        // Win11 は HTMAXBUTTON 上のホバーでスナップレイアウトのフライアウトを表示する。
        if (m.Msg == WM_NCMOUSEMOVE)
        {
            SetMaxButtonHover(m.WParam.ToInt32() == HTMAXBUTTON);
            base.WndProc(ref m);
            return;
        }
        if (m.Msg == WM_NCMOUSELEAVE)
        {
            _trackingNcLeave = false;
            SetMaxButtonHover(false);
            SetMaxButtonPressed(false);
            base.WndProc(ref m);
            return;
        }
        if (m.Msg == WM_NCLBUTTONDOWN && m.WParam.ToInt32() == HTMAXBUTTON)
        {
            SetMaxButtonPressed(true);
            return; // 既定のウィンドウドラッグ開始を抑止
        }
        if (m.Msg == WM_NCLBUTTONUP && m.WParam.ToInt32() == HTMAXBUTTON)
        {
            SetMaxButtonPressed(false);
            ToggleMaximizeRestore();
            return;
        }

        if (m.Msg == WM_NCHITTEST)
        {
            var screenPt = DecodeScreenPoint(m.LParam);

            // 最大化ボタン上ならスナップレイアウトを出すため HTMAXBUTTON を返す
            var maxRect = GetMaximizeButtonScreenRect();
            if (maxRect is { } r && r.Contains(screenPt))
            {
                m.Result = HTMAXBUTTON;
                return;
            }

            var pos = PointToClient(screenPt);

            // 最大化中は端リサイズを無効化する
            if (WindowState != FormWindowState.Maximized)
            {
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
        }
        base.WndProc(ref m);
    }

    internal static Point DecodeScreenPoint(IntPtr lParam)
    {
        int packedPoint = lParam.ToInt32();
        return new Point(
            unchecked((short)(packedPoint & 0xffff)),
            unchecked((short)((packedPoint >> 16) & 0xffff)));
    }

    private void ToggleMaximizeRestore()
    {
        WindowState = WindowState == FormWindowState.Maximized
            ? FormWindowState.Normal
            : FormWindowState.Maximized;
    }

    // 現在表示中の最大化ボタン (編集/ビュアーで別インスタンス) を返す
    private CaptionToolButton? GetVisibleMaximizeButton()
    {
        foreach (var btn in _maximizeButtons)
        {
            if (btn.Owner is { Visible: true, IsHandleCreated: true })
                return btn;
        }
        return null;
    }

    private Rectangle? GetMaximizeButtonScreenRect()
    {
        var btn = GetVisibleMaximizeButton();
        if (btn?.Owner == null) return null;
        try
        {
            var topLeft = btn.Owner.PointToScreen(btn.Bounds.Location);
            return new Rectangle(topLeft, btn.Bounds.Size);
        }
        catch
        {
            return null;
        }
    }

    private void SetMaxButtonHover(bool hover)
    {
        var btn = GetVisibleMaximizeButton();
        if (btn == null) return;
        if (hover && !_trackingNcLeave)
        {
            // NCマウス離脱を受け取れるよう追跡を開始する
            var tme = new TRACKMOUSEEVENT
            {
                cbSize = Marshal.SizeOf<TRACKMOUSEEVENT>(),
                dwFlags = TME_LEAVE | TME_NONCLIENT,
                hwndTrack = Handle,
                dwHoverTime = 0,
            };
            TrackMouseEvent(ref tme);
            _trackingNcLeave = true;
        }
        if (btn.SnapHover != hover)
        {
            btn.SnapHover = hover;
            if (!hover) btn.SnapPressed = false;
            btn.Owner?.Invalidate(btn.Bounds);
        }
    }

    private void SetMaxButtonPressed(bool pressed)
    {
        var btn = GetVisibleMaximizeButton();
        if (btn == null || btn.SnapPressed == pressed) return;
        btn.SnapPressed = pressed;
        btn.Owner?.Invalidate(btn.Bounds);
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
            case "file.newTab": AddDocument(CreateNewCanvasDocument(), true); break;
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
            case "overlay.toggle": ToggleOverlayMode(_overlayForm == null); break;
            case "overlay.clickThrough": SetOverlayClickThrough(!_overlayClickThrough); break;
            case "overlay.opacityUp": SetOverlayOpacity(_overlayOpacity + 0.1f); break;
            case "overlay.opacityDown": SetOverlayOpacity(_overlayOpacity - 0.1f); break;
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
        var version = typeof(MainForm).Assembly.GetName().Version?.ToString(3) ?? "1.0.5";
        MessageBox.Show(this,
            $"Multi Image Canvas\n{Loc.T("バージョン情報")}: {version}",
            Loc.T("バージョン情報"),
            MessageBoxButtons.OK,
            MessageBoxIcon.Information);
    }

    // ===== UI表示切替・テーマ =====

    private void HideUi()
    {
        if (_uiHidden) return;
        _itemPanelRequested = false;
        _menuBar.Visible = false;
        _viewerBar.Visible = false;
        _sessionTitleLabel.Visible = false;
        _docBar.Visible = false;
        _rightPanel.Visible = false;
        _itemPanel.Visible = false;
        _archiveBrowserPanel.Visible = false;
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
            // ビュアーモードは上部バーと圧縮ファイルブラウザーだけ戻す
            _viewerBar.Visible = true;
            if (_archiveSource != null) _archiveBrowserPanel.Visible = true;
            _uiHidden = false;
            return;
        }
        _menuBar.Visible = true;
        _sessionTitleLabel.Visible = true;
        _docBar.Visible = true;
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

        _docBar.BackColor = t.ToolbarBg;
        _docTabsViewport.BackColor = t.ToolbarBg;
        foreach (var strip in new ToolStrip[] { _menuBar, _docTabs, _docTools })
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
        ApplyThemeRecursive(_archiveBrowserPanel, t);
        _archiveTree.BackColor = t.Surface;
        _archiveTree.ForeColor = t.TextPrimary;
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
        ApplyLanguageRecursive(_archiveBrowserPanel);
        if (_sidebarCollapseBtn != null)
        {
            _sidebarToolTip.SetToolTip(_sidebarCollapseBtn,
                Loc.T(_sidebarCollapsed ? "ファイル選択メニューを復元" : "ファイル選択メニューを最小化"));
        }
        if (_viewTreeBtn != null) _sidebarToolTip.SetToolTip(_viewTreeBtn, Loc.T("フォルダツリー"));
        if (_viewThumbsBtn != null) _sidebarToolTip.SetToolTip(_viewThumbsBtn, Loc.T("サムネイル一覧"));
        if (_viewLayersBtn != null) _sidebarToolTip.SetToolTip(_viewLayersBtn, Loc.T("レイヤー"));
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
