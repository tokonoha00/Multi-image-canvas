using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace MultiImageCanvas;

// 設定ダイアログ。左側のカテゴリナビでページを切り替えるジャンル別構成:
//   一般 / 表示 / 編集 / キー割り当て / オーバーレイ / ファイル関連付け
internal sealed class SettingsForm : Form
{
    private readonly KeyMap _keyMap;
    private readonly Dictionary<string, Keys> _editedKeys = [];
    private readonly Dictionary<string, Keys> _appliedKeys = [];
    private string _appliedTheme;
    private bool _appliedRestoreTabs;
    private int _appliedAutosaveSeconds;
    private bool _appliedSnapEnabled;
    private bool _appliedGridSnap;
    private int _appliedImageScalePercent;
    private string _appliedLanguage;
    private string _appliedOverlayAnimation;

    private readonly ListView _keyList = new();
    private readonly ComboBox _themeCombo = new();
    private readonly CheckBox _restoreTabsCheck = new();
    private readonly NumericUpDown _autosaveNum = new();
    private readonly CheckBox _snapCheck = new();
    private readonly CheckBox _gridSnapCheck = new();
    private readonly ComboBox _imageScaleCombo = new();
    private readonly ComboBox _languageCombo = new();
    private readonly ComboBox _animCombo = new();
    private Label? _overlayHotkeyHint;
    private Button? _applyBtn;

    private readonly ListBox _nav = new();
    private readonly Panel _pageHost = new();
    private readonly List<(string Title, Panel Page)> _pages = [];

    public string SelectedTheme => Loc.J(_themeCombo.SelectedItem as string ?? Theme.Current.Name);
    public bool RestoreTabs => _restoreTabsCheck.Checked;
    public int AutosaveSeconds => (int)_autosaveNum.Value;
    public bool SnapEnabled => _snapCheck.Checked;
    public bool GridSnap => _gridSnapCheck.Checked;
    public int ImageImportScalePercent => int.TryParse((_imageScaleCombo.SelectedItem as string)?.TrimEnd('%'), out var value) ? value : 100;
    public string Language => _languageCombo.SelectedItem as string ?? Loc.Japanese;
    public string OverlayAnimation => Loc.J(_animCombo.SelectedItem as string ?? "ブロック");

    public event EventHandler? Applied;

    public SettingsForm(KeyMap keyMap, bool restoreTabs, int autosaveSeconds, bool snapEnabled, bool gridSnap, int imageScalePercent, string language, string overlayAnimation, string autosaveLog)
    {
        _keyMap = keyMap;
        foreach (var b in keyMap.Bindings)
        {
            _editedKeys[b.Id] = b.Current;
            _appliedKeys[b.Id] = b.Current;
        }
        _appliedTheme = Theme.Current.Name;
        _appliedRestoreTabs = restoreTabs;
        _appliedAutosaveSeconds = Math.Clamp(autosaveSeconds, 10, 600);
        _appliedSnapEnabled = snapEnabled;
        _appliedGridSnap = gridSnap;
        _appliedImageScalePercent = new[] { 25, 50, 75, 100, 125, 150, 200 }.OrderBy(v => Math.Abs(v - imageScalePercent)).First();
        _appliedLanguage = string.IsNullOrWhiteSpace(language) ? Loc.Japanese : language;
        _appliedOverlayAnimation = OverlayAnimations.Names.Contains(overlayAnimation) ? overlayAnimation : "ブロック";

        var t = Theme.Current;
        Text = Loc.T("設定");
        Width = 720;
        Height = 620;
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        BackColor = t.Background;
        ForeColor = t.TextPrimary;
        Font = new Font("Segoe UI", 9f);

        var root = new Panel { Dock = DockStyle.Fill, Padding = new Padding(16), BackColor = t.Background };

        // ===== 下部: 自動保存ログ / OK / 適用 / キャンセル =====
        var bottomPanel = new Panel { Dock = DockStyle.Bottom, Height = 76, BackColor = t.Background };
        var autosaveLogLabel = new Label
        {
            Text = autosaveLog,
            Dock = DockStyle.Top,
            Height = 24,
            TextAlign = ContentAlignment.MiddleLeft,
            ForeColor = t.TextSecondary,
            BackColor = t.Background,
        };
        var bottomRow = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            Height = 48,
            FlowDirection = FlowDirection.RightToLeft,
            Padding = new Padding(0, 10, 0, 0),
        };
        var cancelBtn = MakeButton(Loc.T("キャンセル"), (_, _) => { DialogResult = DialogResult.Cancel; Close(); });
        var okBtn = MakeButton("OK", (_, _) => { CommitAndClose(); });
        var applyBtn = MakeButton(Loc.T("適用"), (_, _) => ApplyChanges());
        _applyBtn = applyBtn;
        applyBtn.Enabled = false;
        okBtn.Width = 100;
        applyBtn.Width = 100;
        cancelBtn.Width = 100;
        bottomRow.Controls.Add(applyBtn);
        bottomRow.Controls.Add(cancelBtn);
        bottomRow.Controls.Add(okBtn);
        AcceptButton = okBtn;
        CancelButton = cancelBtn;
        bottomPanel.Controls.Add(autosaveLogLabel);
        bottomPanel.Controls.Add(bottomRow);

        // ===== 左ナビ =====
        _nav.Dock = DockStyle.Left;
        _nav.Width = 150;
        _nav.BorderStyle = BorderStyle.None;
        _nav.BackColor = t.Surface;
        _nav.ForeColor = t.TextPrimary;
        _nav.DrawMode = DrawMode.OwnerDrawFixed;
        _nav.ItemHeight = 36;
        _nav.IntegralHeight = false;
        _nav.DrawItem += Nav_DrawItem;
        _nav.SelectedIndexChanged += (_, _) => ShowPage(_nav.SelectedIndex);

        var navHost = new Panel { Dock = DockStyle.Left, Width = 162, Padding = new Padding(0, 0, 12, 0), BackColor = t.Background };
        navHost.Controls.Add(_nav);

        _pageHost.Dock = DockStyle.Fill;
        _pageHost.BackColor = t.Background;

        // ===== 各ページ =====
        AddPage(Loc.T("一般"), BuildGeneralPage(t));
        AddPage(Loc.T("表示"), BuildDisplayPage(t));
        AddPage(Loc.T("編集"), BuildEditPage(t));
        AddPage(Loc.T("キー割り当て"), BuildKeysPage(t));
        AddPage(Loc.T("オーバーレイ"), BuildOverlayPage(t));
        AddPage(Loc.T("ファイル関連付け"), BuildAssociationPage(t));

        root.Controls.Add(_pageHost);
        root.Controls.Add(navHost);
        root.Controls.Add(bottomPanel);
        Controls.Add(root);

        RefreshKeyList();
        WireDirtyTracking();
        _nav.SelectedIndex = 0;
    }

    private void AddPage(string title, Panel page)
    {
        page.Dock = DockStyle.Fill;
        page.Visible = false;
        _pageHost.Controls.Add(page);
        _pages.Add((title, page));
        _nav.Items.Add(title);
    }

    private void ShowPage(int index)
    {
        for (int i = 0; i < _pages.Count; i++)
        {
            _pages[i].Page.Visible = i == index;
        }
    }

    private void Nav_DrawItem(object? sender, DrawItemEventArgs e)
    {
        var t = Theme.Current;
        var g = e.Graphics;
        using (var bg = new SolidBrush(t.Surface))
        {
            g.FillRectangle(bg, e.Bounds);
        }

        if (e.Index < 0 || e.Index >= _nav.Items.Count) return;
        bool selected = (e.State & DrawItemState.Selected) != 0;

        if (selected)
        {
            var rect = new Rectangle(e.Bounds.X + 4, e.Bounds.Y + 3, e.Bounds.Width - 8, e.Bounds.Height - 6);
            using var brush = new SolidBrush(t.AccentDark);
            using var path = RoundedRect(rect, 8);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.FillPath(brush, path);
        }

        TextRenderer.DrawText(g, _nav.Items[e.Index]?.ToString(), Font,
            new Rectangle(e.Bounds.X + 14, e.Bounds.Y, e.Bounds.Width - 14, e.Bounds.Height),
            t.TextPrimary, TextFormatFlags.Left | TextFormatFlags.VerticalCenter);
    }

    private static GraphicsPath RoundedRect(Rectangle bounds, int radius)
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

    // ===== ページ構築 =====

    private Panel BuildGeneralPage(Theme t)
    {
        var page = new Panel { BackColor = t.Background };

        _restoreTabsCheck.Text = Loc.T("起動時に前回のキャンバスタブを復元する");
        _restoreTabsCheck.AutoSize = true;
        _restoreTabsCheck.Location = new Point(8, 16);
        _restoreTabsCheck.ForeColor = t.TextPrimary;
        _restoreTabsCheck.Checked = _appliedRestoreTabs;

        var autosaveLabel = new Label { Text = Loc.T("セッション自動保存間隔:"), AutoSize = true, Location = new Point(8, 56), ForeColor = t.TextPrimary };
        _autosaveNum.Minimum = 10;
        _autosaveNum.Maximum = 600;
        _autosaveNum.Value = _appliedAutosaveSeconds;
        _autosaveNum.Location = new Point(180, 52);
        _autosaveNum.Width = 70;
        _autosaveNum.BackColor = t.SurfaceLight;
        _autosaveNum.ForeColor = t.TextPrimary;
        var secLabel = new Label { Text = Loc.T("秒"), AutoSize = true, Location = new Point(256, 56), ForeColor = t.TextPrimary };

        var languageLabel = new Label { Text = Loc.T("言語:"), AutoSize = true, Location = new Point(8, 96), ForeColor = t.TextPrimary };
        _languageCombo.DropDownStyle = ComboBoxStyle.DropDownList;
        _languageCombo.Location = new Point(180, 92);
        _languageCombo.Width = 180;
        _languageCombo.FlatStyle = FlatStyle.Flat;
        _languageCombo.BackColor = t.SurfaceLight;
        _languageCombo.ForeColor = t.TextPrimary;
        _languageCombo.Items.Add(Loc.Japanese);
        _languageCombo.Items.Add(Loc.English);
        _languageCombo.SelectedItem = Loc.Normalize(_appliedLanguage);

        page.Controls.Add(_restoreTabsCheck);
        page.Controls.Add(autosaveLabel);
        page.Controls.Add(_autosaveNum);
        page.Controls.Add(secLabel);
        page.Controls.Add(languageLabel);
        page.Controls.Add(_languageCombo);
        return page;
    }

    private Panel BuildDisplayPage(Theme t)
    {
        var page = new Panel { BackColor = t.Background };

        var themeLabel = new Label { Text = Loc.T("UIテーマ:"), AutoSize = true, Location = new Point(8, 20), ForeColor = t.TextPrimary };
        _themeCombo.DropDownStyle = ComboBoxStyle.DropDownList;
        _themeCombo.Location = new Point(180, 16);
        _themeCombo.Width = 180;
        _themeCombo.FlatStyle = FlatStyle.Flat;
        _themeCombo.BackColor = t.SurfaceLight;
        _themeCombo.ForeColor = t.TextPrimary;
        foreach (var preset in Theme.Presets) _themeCombo.Items.Add(Loc.T(preset.Name));
        _themeCombo.SelectedItem = Loc.T(Theme.Current.Name);

        page.Controls.Add(themeLabel);
        page.Controls.Add(_themeCombo);
        return page;
    }

    private Panel BuildEditPage(Theme t)
    {
        var page = new Panel { BackColor = t.Background };

        var snapGroup = new GroupBox
        {
            Text = Loc.T("スナップ機能"),
            Location = new Point(8, 12),
            Size = new Size(360, 56),
            ForeColor = t.TextPrimary,
        };
        _snapCheck.Text = Loc.T("画像スナップ");
        _snapCheck.AutoSize = true;
        _snapCheck.Location = new Point(16, 22);
        _snapCheck.ForeColor = t.TextPrimary;
        _snapCheck.Checked = _appliedSnapEnabled;
        _gridSnapCheck.Text = Loc.T("グリッドスナップ");
        _gridSnapCheck.AutoSize = true;
        _gridSnapCheck.Location = new Point(160, 22);
        _gridSnapCheck.ForeColor = t.TextPrimary;
        _gridSnapCheck.Checked = _appliedGridSnap;
        snapGroup.Controls.Add(_snapCheck);
        snapGroup.Controls.Add(_gridSnapCheck);

        var scaleLabel = new Label { Text = Loc.T("画像読み込み倍率:"), AutoSize = true, Location = new Point(8, 92), ForeColor = t.TextPrimary };
        _imageScaleCombo.DropDownStyle = ComboBoxStyle.DropDownList;
        _imageScaleCombo.Location = new Point(180, 88);
        _imageScaleCombo.Width = 100;
        _imageScaleCombo.FlatStyle = FlatStyle.Flat;
        _imageScaleCombo.BackColor = t.SurfaceLight;
        _imageScaleCombo.ForeColor = t.TextPrimary;
        foreach (var value in new[] { 25, 50, 75, 100, 125, 150, 200 }) _imageScaleCombo.Items.Add($"{value}%");
        _imageScaleCombo.SelectedItem = $"{_appliedImageScalePercent}%";

        page.Controls.Add(snapGroup);
        page.Controls.Add(scaleLabel);
        page.Controls.Add(_imageScaleCombo);
        return page;
    }

    private Panel BuildKeysPage(Theme t)
    {
        var page = new Panel { BackColor = t.Background };

        _keyList.View = View.Details;
        _keyList.FullRowSelect = true;
        _keyList.MultiSelect = false;
        _keyList.HeaderStyle = ColumnHeaderStyle.Nonclickable;
        _keyList.BackColor = t.SurfaceLight;
        _keyList.ForeColor = t.TextPrimary;
        _keyList.BorderStyle = BorderStyle.FixedSingle;
        _keyList.Dock = DockStyle.Fill;
        _keyList.Columns.Add(Loc.T("分類"), 76);
        _keyList.Columns.Add(Loc.T("操作"), 220);
        _keyList.Columns.Add(Loc.T("キー"), 160);
        _keyList.DoubleClick += (_, _) => ChangeSelectedKey();

        var hint = new Label
        {
            Text = Loc.T("ダブルクリックまたは「変更」で編集"),
            Dock = DockStyle.Top,
            Height = 24,
            ForeColor = t.TextSecondary,
            BackColor = t.Background,
        };

        var keyButtons = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            Height = 40,
            FlowDirection = FlowDirection.LeftToRight,
            Padding = new Padding(0, 6, 0, 0),
        };
        keyButtons.Controls.Add(MakeButton(Loc.T("変更..."), (_, _) => ChangeSelectedKey()));
        keyButtons.Controls.Add(MakeButton(Loc.T("割り当て解除"), (_, _) => ClearSelectedKey()));
        keyButtons.Controls.Add(MakeButton(Loc.T("選択を既定に戻す"), (_, _) => ResetSelectedKey()));
        keyButtons.Controls.Add(MakeButton(Loc.T("すべて既定に戻す"), (_, _) => ResetAllKeys()));

        page.Controls.Add(_keyList);
        page.Controls.Add(keyButtons);
        page.Controls.Add(hint);
        page.Controls.SetChildIndex(hint, page.Controls.Count - 1);
        return page;
    }

    private Panel BuildOverlayPage(Theme t)
    {
        var page = new Panel { BackColor = t.Background };

        var animLabel = new Label { Text = Loc.T("オーバーレイ登場アニメ:"), AutoSize = true, Location = new Point(8, 20), ForeColor = t.TextPrimary };
        _animCombo.DropDownStyle = ComboBoxStyle.DropDownList;
        _animCombo.Location = new Point(180, 16);
        _animCombo.Width = 180;
        _animCombo.FlatStyle = FlatStyle.Flat;
        _animCombo.BackColor = t.SurfaceLight;
        _animCombo.ForeColor = t.TextPrimary;
        foreach (var name in OverlayAnimations.Names) _animCombo.Items.Add(Loc.T(name));
        _animCombo.SelectedItem = Loc.T(_appliedOverlayAnimation);

        _overlayHotkeyHint = new Label
        {
            Text = BuildOverlayHotkeyHint(),
            AutoSize = true,
            Location = new Point(8, 64),
            ForeColor = t.TextSecondary,
        };

        page.Controls.Add(animLabel);
        page.Controls.Add(_animCombo);
        page.Controls.Add(_overlayHotkeyHint);
        return page;
    }

    private string BuildOverlayHotkeyHint() => string.Join(Environment.NewLine,
    [
        Loc.T("グローバルホットキー:"),
        $"  {KeyMap.ToDisplay(_editedKeys["overlay.toggle"])}  {Loc.T("オーバーレイ表示/非表示")}",
        $"  {KeyMap.ToDisplay(_editedKeys["overlay.clickThrough"])}  {Loc.T("クリック透過")}",
        $"  {KeyMap.ToDisplay(_editedKeys["overlay.opacityUp"])}  {Loc.T("オーバーレイ不透明度を上げる")}",
        $"  {KeyMap.ToDisplay(_editedKeys["overlay.opacityDown"])}  {Loc.T("オーバーレイ不透明度を下げる")}",
    ]);

    private void UpdateOverlayHotkeyHint()
    {
        if (_overlayHotkeyHint != null) _overlayHotkeyHint.Text = BuildOverlayHotkeyHint();
    }

    private Panel BuildAssociationPage(Theme t)
    {
        var page = new Panel { BackColor = t.Background };

        var desc = new Label
        {
            Text = Loc.T("Windows 11 では、既定のアプリは設定画面でユーザーが選択する必要があります。\n下のボタンを押すと、Multi Image Canvas の登録を整えたうえで、このアプリの既定のアプリ設定画面を開きます。\n(登録は現在のユーザーのみ・管理者権限不要)"),
            Dock = DockStyle.Top,
            Height = 74,
            ForeColor = t.TextSecondary,
            BackColor = t.Background,
        };

        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            Height = 52,
            FlowDirection = FlowDirection.LeftToRight,
            Padding = new Padding(0, 6, 0, 0),
        };
        buttons.Controls.Add(MakeButton(Loc.T("Multi Image Canvasを既定のアプリに設定"), (_, _) => OpenDefaultAppSettings()));

        page.Controls.Add(buttons);
        page.Controls.Add(desc);
        page.Controls.SetChildIndex(desc, page.Controls.Count - 1);
        return page;
    }

    private void OpenDefaultAppSettings()
    {
        try
        {
            if (!FileAssociation.OpenWindowsDefaultAppsSettings())
            {
                MessageBox.Show(this, Loc.T("Windowsの既定のアプリ設定を開けませんでした。"),
                    Loc.T("ファイル関連付け"), MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, Loc.T("ファイル関連付け"), MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private Button MakeButton(string text, EventHandler onClick)
    {
        var t = Theme.Current;
        var btn = new RoundedFlatButton
        {
            Text = text,
            AutoSize = true,
            ForeColor = t.TextPrimary,
            Padding = new Padding(6, 2, 6, 2),
            CornerRadius = 8,
            BaseColor = t.ButtonBg,
        };
        btn.Click += onClick;
        return btn;
    }

    private void WireDirtyTracking()
    {
        _themeCombo.SelectedIndexChanged += (_, _) => UpdateApplyEnabled();
        _restoreTabsCheck.CheckedChanged += (_, _) => UpdateApplyEnabled();
        _autosaveNum.ValueChanged += (_, _) => UpdateApplyEnabled();
        _snapCheck.CheckedChanged += (_, _) => UpdateApplyEnabled();
        _gridSnapCheck.CheckedChanged += (_, _) => UpdateApplyEnabled();
        _imageScaleCombo.SelectedIndexChanged += (_, _) => UpdateApplyEnabled();
        _languageCombo.SelectedIndexChanged += (_, _) => UpdateApplyEnabled();
        _animCombo.SelectedIndexChanged += (_, _) => UpdateApplyEnabled();
    }

    private void UpdateApplyEnabled()
    {
        if (_applyBtn != null) _applyBtn.Enabled = HasChanges();
    }

    private bool HasChanges()
    {
        if (SelectedTheme != _appliedTheme) return true;
        if (RestoreTabs != _appliedRestoreTabs) return true;
        if (AutosaveSeconds != _appliedAutosaveSeconds) return true;
        if (SnapEnabled != _appliedSnapEnabled) return true;
        if (GridSnap != _appliedGridSnap) return true;
        if (ImageImportScalePercent != _appliedImageScalePercent) return true;
        if (Loc.Normalize(Language) != Loc.Normalize(_appliedLanguage)) return true;
        if (OverlayAnimation != _appliedOverlayAnimation) return true;
        return _editedKeys.Any(kv => !_appliedKeys.TryGetValue(kv.Key, out var value) || value != kv.Value);
    }

    private void RefreshKeyList()
    {
        _keyList.BeginUpdate();
        _keyList.Items.Clear();
        foreach (var b in _keyMap.Bindings)
        {
            var keys = _editedKeys[b.Id];
            var item = new ListViewItem([Loc.T(b.Category), Loc.T(b.Label), KeyMap.ToDisplay(keys)]) { Tag = b.Id };
            if (keys != b.Default) item.ForeColor = Theme.Current.Accent;
            _keyList.Items.Add(item);
        }
        _keyList.EndUpdate();
        UpdateOverlayHotkeyHint();
    }

    private string? SelectedId => _keyList.SelectedItems.Count > 0 ? _keyList.SelectedItems[0].Tag as string : null;

    private void ChangeSelectedKey()
    {
        var id = SelectedId;
        if (id == null) return;
        var binding = _keyMap.Bindings.First(b => b.Id == id);

        using var dlg = new KeyCaptureForm(binding.Label);
        if (dlg.ShowDialog(this) != DialogResult.OK || dlg.CapturedKeys == Keys.None) return;
        var keys = dlg.CapturedKeys;

        // 修飾なしの英数字は誤爆しやすいので拒否
        var baseKey = keys & Keys.KeyCode;
        bool hasModifier = (keys & (Keys.Control | Keys.Alt | Keys.Shift)) != 0;
        bool isPlainAlnum = !hasModifier && ((baseKey >= Keys.A && baseKey <= Keys.Z) || (baseKey >= Keys.D0 && baseKey <= Keys.D9));
        if (isPlainAlnum)
        {
            MessageBox.Show(this, Loc.T("修飾キーなしの英数字キーは割り当てられません。\nCtrl / Alt / Shift と組み合わせてください。"),
                Loc.T("キー割り当て"), MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        // 競合確認
        var conflict = _editedKeys.FirstOrDefault(kv => kv.Key != id && kv.Value == keys);
        if (conflict.Key != null)
        {
            var other = _keyMap.Bindings.First(b => b.Id == conflict.Key);
            var result = MessageBox.Show(this,
                string.Format(Loc.T("{0} は「{1}」に割り当てられています。\n置き換えますか？（元の操作は割り当てなしになります）"), KeyMap.ToDisplay(keys), Loc.T(other.Label)),
                Loc.T("キーの競合"), MessageBoxButtons.YesNo, MessageBoxIcon.Question);
            if (result != DialogResult.Yes) return;
            _editedKeys[conflict.Key] = Keys.None;
        }

        _editedKeys[id] = keys;
        RefreshKeyList();
        UpdateApplyEnabled();
    }

    private void ClearSelectedKey()
    {
        var id = SelectedId;
        if (id == null) return;
        _editedKeys[id] = Keys.None;
        RefreshKeyList();
        UpdateApplyEnabled();
    }

    private void ResetSelectedKey()
    {
        var id = SelectedId;
        if (id == null) return;
        _editedKeys[id] = _keyMap.Bindings.First(b => b.Id == id).Default;
        RefreshKeyList();
        UpdateApplyEnabled();
    }

    private void ResetAllKeys()
    {
        foreach (var b in _keyMap.Bindings) _editedKeys[b.Id] = b.Default;
        RefreshKeyList();
        UpdateApplyEnabled();
    }

    private void CommitAndClose()
    {
        ApplyChanges();
        DialogResult = DialogResult.OK;
        Close();
    }

    private void ApplyChanges()
    {
        foreach (var b in _keyMap.Bindings)
        {
            b.Current = _editedKeys[b.Id];
            _appliedKeys[b.Id] = b.Current;
        }
        Applied?.Invoke(this, EventArgs.Empty);
        _appliedTheme = SelectedTheme;
        _appliedRestoreTabs = RestoreTabs;
        _appliedAutosaveSeconds = AutosaveSeconds;
        _appliedSnapEnabled = SnapEnabled;
        _appliedGridSnap = GridSnap;
        _appliedImageScalePercent = ImageImportScalePercent;
        _appliedLanguage = Loc.Normalize(Language);
        _appliedOverlayAnimation = OverlayAnimation;
        UpdateApplyEnabled();
    }
}

// 「新しいキーを押してください」ダイアログ
internal sealed class KeyCaptureForm : Form
{
    public Keys CapturedKeys { get; private set; } = Keys.None;

    public KeyCaptureForm(string actionLabel)
    {
        var t = Theme.Current;
        Text = Loc.T("キーの入力");
        Width = 420;
        Height = 170;
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        BackColor = t.Background;
        ForeColor = t.TextPrimary;
        KeyPreview = true;

        var label = new Label
        {
            Text = string.Format(Loc.T("「{0}」に割り当てるキーを押してください\n(Escでキャンセル)"), Loc.T(actionLabel)),
            Dock = DockStyle.Top,
            Height = 50,
            TextAlign = ContentAlignment.MiddleCenter,
            ForeColor = t.TextPrimary,
        };
        var display = new Label
        {
            Text = "...",
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleCenter,
            Font = new Font("Segoe UI", 14f, FontStyle.Bold),
            ForeColor = t.Accent,
        };
        Controls.Add(display);
        Controls.Add(label);

        KeyDown += (_, e) =>
        {
            e.Handled = true;
            e.SuppressKeyPress = true;

            if (e.KeyCode == Keys.Escape)
            {
                DialogResult = DialogResult.Cancel;
                Close();
                return;
            }

            // 修飾キー単独は確定しない
            if (e.KeyCode is Keys.ControlKey or Keys.ShiftKey or Keys.Menu or Keys.LWin or Keys.RWin)
            {
                display.Text = KeyMap.ToDisplay(e.Modifiers | Keys.None) + " + ...";
                return;
            }

            CapturedKeys = e.KeyData;
            DialogResult = DialogResult.OK;
            Close();
        };
    }
}
