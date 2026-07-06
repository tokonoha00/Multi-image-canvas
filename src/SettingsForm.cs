using System.Drawing;
using System.Windows.Forms;

namespace MultiImageCanvas;

// 設定ダイアログ: テーマ / セッション / 自動保存 / キー割り当て
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
    private Button? _applyBtn;

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
        Width = 600;
        Height = 780;
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        BackColor = t.Background;
        ForeColor = t.TextPrimary;
        Font = new Font("Segoe UI", 9f);

        var root = new Panel { Dock = DockStyle.Fill, Padding = new Padding(16), BackColor = t.Background };

        // ===== 一般 =====
        var generalGroup = new GroupBox
        {
            Text = Loc.T("一般"),
            Dock = DockStyle.Top,
            Height = 324,
            ForeColor = t.TextPrimary,
            Padding = new Padding(12),
        };

        var themeLabel = new Label { Text = Loc.T("UIテーマ:"), AutoSize = true, Location = new Point(16, 168), ForeColor = t.TextPrimary };
        _themeCombo.DropDownStyle = ComboBoxStyle.DropDownList;
        _themeCombo.Location = new Point(150, 164);
        _themeCombo.Width = 180;
        _themeCombo.FlatStyle = FlatStyle.Flat;
        _themeCombo.BackColor = t.SurfaceLight;
        _themeCombo.ForeColor = t.TextPrimary;
        foreach (var preset in Theme.Presets) _themeCombo.Items.Add(Loc.T(preset.Name));
        _themeCombo.SelectedItem = Loc.T(Theme.Current.Name);

        _restoreTabsCheck.Text = Loc.T("起動時に前回のキャンバスタブを復元する");
        _restoreTabsCheck.AutoSize = true;
        _restoreTabsCheck.Location = new Point(16, 32);
        _restoreTabsCheck.ForeColor = t.TextPrimary;
        _restoreTabsCheck.Checked = restoreTabs;

        var autosaveLabel = new Label { Text = Loc.T("セッション自動保存間隔:"), AutoSize = true, Location = new Point(16, 68), ForeColor = t.TextPrimary };
        _autosaveNum.Minimum = 10;
        _autosaveNum.Maximum = 600;
        _autosaveNum.Value = _appliedAutosaveSeconds;
        _autosaveNum.Location = new Point(150, 64);
        _autosaveNum.Width = 70;
        _autosaveNum.BackColor = t.SurfaceLight;
        _autosaveNum.ForeColor = t.TextPrimary;
        var secLabel = new Label { Text = Loc.T("秒"), AutoSize = true, Location = new Point(226, 68), ForeColor = t.TextPrimary };

        var snapGroup = new GroupBox
        {
            Text = Loc.T("スナップ機能"),
            Location = new Point(16, 98),
            Size = new Size(330, 54),
            ForeColor = t.TextPrimary,
        };

        _snapCheck.Text = Loc.T("画像スナップ");
        _snapCheck.AutoSize = true;
        _snapCheck.Location = new Point(16, 22);
        _snapCheck.ForeColor = t.TextPrimary;
        _snapCheck.Checked = snapEnabled;

        _gridSnapCheck.Text = Loc.T("グリッドスナップ");
        _gridSnapCheck.AutoSize = true;
        _gridSnapCheck.Location = new Point(150, 22);
        _gridSnapCheck.ForeColor = t.TextPrimary;
        _gridSnapCheck.Checked = gridSnap;

        snapGroup.Controls.Add(_snapCheck);
        snapGroup.Controls.Add(_gridSnapCheck);

        var scaleLabel = new Label { Text = Loc.T("画像読み込み倍率:"), AutoSize = true, Location = new Point(16, 196), ForeColor = t.TextPrimary };
        _imageScaleCombo.DropDownStyle = ComboBoxStyle.DropDownList;
        _imageScaleCombo.Location = new Point(150, 192);
        _imageScaleCombo.Width = 100;
        _imageScaleCombo.FlatStyle = FlatStyle.Flat;
        _imageScaleCombo.BackColor = t.SurfaceLight;
        _imageScaleCombo.ForeColor = t.TextPrimary;
        foreach (var value in new[] { 25, 50, 75, 100, 125, 150, 200 }) _imageScaleCombo.Items.Add($"{value}%");
        _imageScaleCombo.SelectedItem = $"{_appliedImageScalePercent}%";

        var languageLabel = new Label { Text = Loc.T("言語:"), AutoSize = true, Location = new Point(16, 236), ForeColor = t.TextPrimary };
        _languageCombo.DropDownStyle = ComboBoxStyle.DropDownList;
        _languageCombo.Location = new Point(150, 232);
        _languageCombo.Width = 180;
        _languageCombo.FlatStyle = FlatStyle.Flat;
        _languageCombo.BackColor = t.SurfaceLight;
        _languageCombo.ForeColor = t.TextPrimary;
        _languageCombo.Items.Add(Loc.Japanese);
        _languageCombo.Items.Add(Loc.English);
        _languageCombo.SelectedItem = Loc.Normalize(_appliedLanguage);

        var animLabel = new Label { Text = Loc.T("オーバーレイ登場アニメ:"), AutoSize = true, Location = new Point(16, 276), ForeColor = t.TextPrimary };
        _animCombo.DropDownStyle = ComboBoxStyle.DropDownList;
        _animCombo.Location = new Point(150, 272);
        _animCombo.Width = 180;
        _animCombo.FlatStyle = FlatStyle.Flat;
        _animCombo.BackColor = t.SurfaceLight;
        _animCombo.ForeColor = t.TextPrimary;
        foreach (var name in OverlayAnimations.Names) _animCombo.Items.Add(Loc.T(name));
        _animCombo.SelectedItem = Loc.T(_appliedOverlayAnimation);

        generalGroup.Controls.Add(themeLabel);
        generalGroup.Controls.Add(_themeCombo);
        generalGroup.Controls.Add(_restoreTabsCheck);
        generalGroup.Controls.Add(autosaveLabel);
        generalGroup.Controls.Add(_autosaveNum);
        generalGroup.Controls.Add(secLabel);
        generalGroup.Controls.Add(snapGroup);
        generalGroup.Controls.Add(scaleLabel);
        generalGroup.Controls.Add(_imageScaleCombo);
        generalGroup.Controls.Add(languageLabel);
        generalGroup.Controls.Add(_languageCombo);
        generalGroup.Controls.Add(animLabel);
        generalGroup.Controls.Add(_animCombo);

        // ===== キー割り当て =====
        var keyGroup = new GroupBox
        {
            Text = Loc.T("キー割り当て (ダブルクリックまたは「変更」で編集)"),
            Dock = DockStyle.Fill,
            ForeColor = t.TextPrimary,
            Padding = new Padding(12),
        };

        _keyList.View = View.Details;
        _keyList.FullRowSelect = true;
        _keyList.MultiSelect = false;
        _keyList.HeaderStyle = ColumnHeaderStyle.Nonclickable;
        _keyList.BackColor = t.SurfaceLight;
        _keyList.ForeColor = t.TextPrimary;
        _keyList.BorderStyle = BorderStyle.FixedSingle;
        _keyList.Dock = DockStyle.Fill;
        _keyList.Columns.Add(Loc.T("分類"), 80);
        _keyList.Columns.Add(Loc.T("操作"), 220);
        _keyList.Columns.Add(Loc.T("キー"), 190);
        _keyList.DoubleClick += (_, _) => ChangeSelectedKey();

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

        keyGroup.Controls.Add(_keyList);
        keyGroup.Controls.Add(keyButtons);

        // ===== 自動保存ログ / OK / 適用 / キャンセル =====
        var bottomPanel = new Panel
        {
            Dock = DockStyle.Bottom,
            Height = 76,
            BackColor = t.Background,
        };
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

        var spacer = new Panel { Dock = DockStyle.Top, Height = 10, BackColor = t.Background };

        root.Controls.Add(keyGroup);
        root.Controls.Add(spacer);
        root.Controls.Add(generalGroup);
        root.Controls.Add(bottomPanel);
        Controls.Add(root);

        RefreshKeyList();
        WireDirtyTracking();
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
