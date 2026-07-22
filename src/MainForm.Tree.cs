using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace MultiImageCanvas;

// MainFormのエクスプローラー風ツリーとパス欄
internal sealed partial class MainForm
{
    [DllImport("uxtheme.dll", ExactSpelling = true, CharSet = CharSet.Unicode)]
    private static extern int SetWindowTheme(IntPtr hWnd, string pszSubAppName, string? pszSubIdList);
    [DllImport("user32.dll")]
    private static extern int GetScrollInfo(IntPtr hwnd, int fnBar, ref SCROLLINFO lpsi);
    [DllImport("user32.dll")]
    private static extern int SetScrollInfo(IntPtr hwnd, int fnBar, ref SCROLLINFO lpsi, bool fRedraw);
    private const int SB_VERT = 1;
    private const int SB_HORZ = 0;
    private const int SIF_RANGE = 0x0001;
    private const int SIF_PAGE = 0x0002;
    private const int SIF_POS = 0x0004;
    private const int SIF_ALL = SIF_RANGE | SIF_PAGE | SIF_POS;
    private const int WM_VSCROLL = 0x0115;
    private const int WM_HSCROLL = 0x0114;
    private const int SB_THUMBTRACK = 5;

    [StructLayout(LayoutKind.Sequential)]
    private struct SCROLLINFO
    {
        public uint cbSize;
        public uint fMask;
        public int nMin;
        public int nMax;
        public uint nPage;
        public int nPos;
        public int nTrackPos;
    }

    private readonly TreeView _tree = new();
    private readonly TextBox _pathBox = new();
    private readonly CustomScrollBar _treeScrollBar = new();
    private readonly CustomScrollBar _treeHScrollBar = new() { Orientation = Orientation.Horizontal };
    private readonly Panel _treeScrollCorner = new();
    private readonly System.Windows.Forms.Timer _treeScrollTimer = new() { Interval = 250 };

    private bool _pathEditInternalChange;

    private TreeNode? _quickAccessNode;
    private readonly HashSet<string> _quickAccessPaths = new(StringComparer.OrdinalIgnoreCase);
    private bool _quickAccessExtrasStarted;

    private void ApplyTreeTheme()
    {
        if (_tree.IsHandleCreated) SetWindowTheme(_tree.Handle, "Explorer", null);
    }

    private void ApplyTreeTheme2(Theme t)
    {
        _tree.BackColor = t.TreeBg;
        _tree.ForeColor = t.TextPrimary;
        _treeScrollBar.BackColor = t.TreeBg;
        _treeHScrollBar.BackColor = t.TreeBg;
        _treeScrollCorner.BackColor = t.TreeBg;
    }

    private TableLayoutPanel BuildPathPanel()
    {
        var pathPanel = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            Height = 42,
            ColumnCount = 2,
            RowCount = 1,
            BackColor = Theme.Current.Surface,
            Padding = new Padding(0, 4, 0, 8),
        };
        pathPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        pathPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 70f));

        var goButton = new GamingButton
        {
            Text = "移動",
            Dock = DockStyle.Fill,
            GradientStart = Color.FromArgb(0, 198, 255),
            GradientEnd = Color.FromArgb(0, 114, 255),
            CornerRadius = 10,
            ForeColor = Color.White,
            Font = new Font(Font.FontFamily, 9f, FontStyle.Bold),
            Cursor = Cursors.Hand,
            Margin = new Padding(8, 0, 0, 0),
        };

        _pathBox.Dock = DockStyle.Fill;
        _pathBox.BorderStyle = BorderStyle.None;
        _pathBox.BackColor = Theme.Current.SurfaceLight;
        _pathBox.ForeColor = Theme.Current.TextPrimary;
        _pathBox.PlaceholderText = "パスを入力して Enter";
        _pathBox.Margin = new Padding(0);
        _pathBox.Click += (_, _) => SelectAllPathTextSoon();
        _pathBox.Enter += (_, _) => SelectAllPathTextSoon();
        _pathBox.KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Enter)
            {
                NavigatePathFromEditor();
                e.SuppressKeyPress = true;
                e.Handled = true;
            }
            else if (e.KeyCode == Keys.Escape)
            {
                _tree.Focus();
                e.SuppressKeyPress = true;
                e.Handled = true;
            }
        };
        goButton.Click += (_, _) => NavigatePathFromEditor();
        pathPanel.Controls.Add(_pathBox, 0, 0);
        pathPanel.Controls.Add(goButton, 1, 0);

        return pathPanel;
    }

    private Panel BuildTreeArea()
    {
        var treeContainer = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = Theme.Current.TreeBg,
        };

        var treeClipPanel = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = Theme.Current.TreeBg,
        };

        _tree.Dock = DockStyle.None;
        _tree.HideSelection = false;
        _tree.PathSeparator = "\\";
        _tree.BackColor = Theme.Current.TreeBg;
        _tree.ForeColor = Theme.Current.TextPrimary;
        _tree.BorderStyle = BorderStyle.None;
        _tree.Indent = 20;
        _tree.ItemHeight = 26;
        _tree.FullRowSelect = true;
        _tree.ShowLines = false;
        _tree.Font = new Font(Font.FontFamily, 9f);
        _tree.BeforeExpand += (_, e) => LoadNodeChildren(e.Node);
        _tree.AfterSelect += (_, e) => OnTreeSelected(e.Node);
        _tree.NodeMouseDoubleClick += (_, e) => OnTreeSelected(e.Node);

        treeClipPanel.Controls.Add(_tree);
        void UpdateTreeBounds(bool hideHorizontal = false) => _tree.SetBounds(
            0,
            0,
            treeClipPanel.ClientSize.Width + SystemInformation.VerticalScrollBarWidth,
            treeClipPanel.ClientSize.Height + (hideHorizontal ? SystemInformation.HorizontalScrollBarHeight : 0));
        treeClipPanel.SizeChanged += (_, _) => UpdateTreeBounds(_treeHScrollBar.Visible);
        UpdateTreeBounds();

        _treeScrollBar.Width = 10;
        _treeScrollBar.BackColor = Theme.Current.TreeBg;
        _treeScrollBar.Scroll += (s, e) =>
        {
            var info = new SCROLLINFO { cbSize = (uint)Marshal.SizeOf<SCROLLINFO>(), fMask = SIF_POS, nPos = _treeScrollBar.Value };
            SetScrollInfo(_tree.Handle, SB_VERT, ref info, true);
            SendMessage(_tree.Handle, WM_VSCROLL, (SB_THUMBTRACK << 16) | (_treeScrollBar.Value & 0xffff), 0);
        };

        _treeHScrollBar.Height = 10;
        _treeHScrollBar.BackColor = Theme.Current.TreeBg;
        _treeHScrollBar.Scroll += (s, e) =>
        {
            var info = new SCROLLINFO { cbSize = (uint)Marshal.SizeOf<SCROLLINFO>(), fMask = SIF_POS, nPos = _treeHScrollBar.Value };
            SetScrollInfo(_tree.Handle, SB_HORZ, ref info, true);
            SendMessage(_tree.Handle, WM_HSCROLL, (SB_THUMBTRACK << 16) | (_treeHScrollBar.Value & 0xffff), 0);
        };

        // ツリー操作中のみスクロールバー状態を同期する (常時ポーリングを避ける)
        void UpdateScrollbarLayout()
        {
            if (!_tree.IsHandleCreated || !IsHandleCreated || !_tree.Visible) return;

            bool needV = false;
            var infoV = new SCROLLINFO { cbSize = (uint)Marshal.SizeOf<SCROLLINFO>(), fMask = SIF_ALL };
            if (GetScrollInfo(_tree.Handle, SB_VERT, ref infoV) != 0)
            {
                needV = infoV.nMax - infoV.nMin + 1 > infoV.nPage && infoV.nPage > 0;
            }

            bool needH = false;
            var infoH = new SCROLLINFO { cbSize = (uint)Marshal.SizeOf<SCROLLINFO>(), fMask = SIF_ALL };
            if (GetScrollInfo(_tree.Handle, SB_HORZ, ref infoH) != 0)
            {
                needH = infoH.nMax - infoH.nMin + 1 > infoH.nPage && infoH.nPage > 0;
            }

            int sbWidth = SystemInformation.VerticalScrollBarWidth;
            int sbHeight = SystemInformation.HorizontalScrollBarHeight;

            int vHeight = needH ? treeContainer.Height - sbHeight : treeContainer.Height;
            int hWidth = needV ? treeContainer.Width - sbWidth : treeContainer.Width;

            _treeScrollBar.SetBounds(treeContainer.Width - sbWidth, 0, sbWidth, vHeight);
            _treeHScrollBar.SetBounds(0, treeContainer.Height - sbHeight, hWidth, sbHeight);

            _treeScrollBar.Visible = needV;
            _treeHScrollBar.Visible = needH;
            UpdateTreeBounds(needH);

            _treeScrollCorner.Visible = needV && needH;
            _treeScrollCorner.SetBounds(treeContainer.Width - sbWidth, treeContainer.Height - sbHeight, sbWidth, sbHeight);

            if (needV)
            {
                _treeScrollBar.Minimum = infoV.nMin;
                _treeScrollBar.Maximum = infoV.nMax;
                _treeScrollBar.LargeChange = Math.Max(1, (int)infoV.nPage);
                _treeScrollBar.Value = infoV.nPos;
            }
            if (needH)
            {
                _treeHScrollBar.Minimum = infoH.nMin;
                _treeHScrollBar.Maximum = infoH.nMax;
                _treeHScrollBar.LargeChange = Math.Max(1, (int)infoH.nPage);
                _treeHScrollBar.Value = infoH.nPos;
            }
        }

        _treeScrollTimer.Tick += (s, e) =>
        {
            if (!_tree.Visible || !_tree.IsHandleCreated) return;
            var mouseInTree = _tree.ClientRectangle.Contains(_tree.PointToClient(Cursor.Position));
            if (!_tree.Focused && !mouseInTree) return;
            UpdateScrollbarLayout();
        };
        _treeScrollTimer.Start();

        _treeScrollBar.BackColor = Theme.Current.TreeBg;
        _treeHScrollBar.BackColor = Theme.Current.TreeBg;
        _treeScrollCorner.BackColor = Theme.Current.TreeBg;

        treeContainer.Controls.Add(treeClipPanel);
        treeContainer.Controls.Add(_treeScrollBar);
        treeContainer.Controls.Add(_treeHScrollBar);
        treeContainer.Controls.Add(_treeScrollCorner);

        _treeScrollBar.BringToFront();
        _treeHScrollBar.BringToFront();
        _treeScrollCorner.BringToFront();

        treeContainer.SizeChanged += (s, e) => UpdateScrollbarLayout();

        return treeContainer;
    }

    // ===== ツリー構築 =====

    private void BuildInitialTree()
    {
        _tree.BeginUpdate();
        try
        {
            _tree.Nodes.Clear();
            _quickAccessPaths.Clear();

            var quick = new TreeNode("⭐ ホーム / クイックアクセス") { Tag = new RootTag("quick") };
            _quickAccessNode = quick;

            // 起動時は確実に速い既知フォルダだけを同期追加する。
            // Shellのピン留め項目やRecentショートカットは環境によって数秒固まるため、Shown後に遅延追加する。
            TryAddQuickAccessKnownFolder(quick, "デスクトップ", KnownFolders.GetDesktopPath());
            TryAddQuickAccessKnownFolder(quick, "ダウンロード", KnownFolders.GetDownloadsPath());
            TryAddQuickAccessKnownFolder(quick, "ドキュメント", KnownFolders.GetDocumentsPath());
            TryAddQuickAccessKnownFolder(quick, "ピクチャ", KnownFolders.GetPicturesPath());
            TryAddQuickAccessKnownFolder(quick, "ミュージック", KnownFolders.GetMusicPath());
            TryAddQuickAccessKnownFolder(quick, "ビデオ", KnownFolders.GetVideosPath());
            TryAddQuickAccessKnownFolder(quick, "ユーザーフォルダ", Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));

            _tree.Nodes.Add(quick);
            quick.Expand();

            AddOneDriveRoots();

            var userRootPath = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            if (Directory.Exists(userRootPath))
            {
                var userNode = new TreeNode("👤 " + Environment.UserName) { Tag = userRootPath };
                userNode.Nodes.Add(MakeDummy());
                _tree.Nodes.Add(userNode);
            }

            var pc = new TreeNode("💻 PC") { Tag = new RootTag("pc") };
            AddKnownFolderNode(pc, "デスクトップ", KnownFolders.GetDesktopPath());
            AddKnownFolderNode(pc, "ダウンロード", KnownFolders.GetDownloadsPath());
            AddKnownFolderNode(pc, "ドキュメント", KnownFolders.GetDocumentsPath());
            AddKnownFolderNode(pc, "ピクチャ", KnownFolders.GetPicturesPath());
            AddKnownFolderNode(pc, "ミュージック", KnownFolders.GetMusicPath());
            AddKnownFolderNode(pc, "ビデオ", KnownFolders.GetVideosPath());
            AddKnownFolderNode(pc, "3D オブジェクト", KnownFolders.Get3DObjectsPath());

            // ドライブのVolumeLabel/IsReady確認は遅いことがあるので、起動時は種類とドライブ文字だけで表示する。
            foreach (var drive in DriveInfo.GetDrives().OrderBy(d => d.Name))
            {
                var node = new TreeNode("💾 " + GetDriveLabelFast(drive)) { Tag = drive.RootDirectory.FullName };
                node.Nodes.Add(MakeDummy());
                pc.Nodes.Add(node);
            }
            _tree.Nodes.Add(pc);
            pc.Expand();

            var network = new TreeNode("🌐 ネットワーク") { Tag = new NetworkTag() };
            network.Nodes.Add(MakeDummy());
            _tree.Nodes.Add(network);

            var recycle = new TreeNode("🗑️ ごみ箱")
            {
                Tag = new VirtualOnlyTag("ごみ箱は画像配置元としては使用できません"),
                ForeColor = Theme.Current.TextDisabled,
            };
            _tree.Nodes.Add(recycle);
        }
        finally
        {
            _tree.EndUpdate();
        }
    }

    private void TryAddQuickAccessKnownFolder(TreeNode parent, string label, string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path)) return;
        var normalized = NormalizeDirectoryPath(path);
        if (!_quickAccessPaths.Add(normalized)) return;
        var node = new TreeNode("📁 " + label) { Tag = normalized };
        node.Nodes.Add(MakeDummy());
        parent.Nodes.Add(node);
    }

    private void StartLoadQuickAccessExtras()
    {
        if (_quickAccessExtrasStarted) return;
        _quickAccessExtrasStarted = true;

        var thread = new Thread(() =>
        {
            var entries = new List<QuickAccessEntry>();
            GatherPinnedQuickAccessFolders(entries, entry =>
            {
                if (IsDisposed || !IsHandleCreated) return;
                try { BeginInvoke(new Action(() => AddQuickAccessExtraToTree(entry))); }
                catch { }
            });

            entries.Clear();
            GatherRecentFolders(entries);

            if (entries.Count == 0 || IsDisposed || !IsHandleCreated) return;
            try
            {
                BeginInvoke(new Action(() => AddQuickAccessExtrasToTree(entries)));
            }
            catch
            {
                // フォーム終了中は無視
            }
        })
        {
            IsBackground = true,
            Name = "QuickAccessLoader",
        };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
    }

    private static void GatherPinnedQuickAccessFolders(List<QuickAccessEntry> entries, Action<QuickAccessEntry>? onEntry = null)
    {
        try
        {
            var shellType = Type.GetTypeFromProgID("Shell.Application");
            if (shellType == null) return;

            dynamic shell = Activator.CreateInstance(shellType)!;
            dynamic quickAccess = shell.Namespace("shell:::{679f85cb-0220-4080-b29b-5540cc05aab6}");
            if (quickAccess == null) return;

            foreach (dynamic item in quickAccess.Items())
            {
                try
                {
                    if (!item.IsFolder) continue;
                    string path = item.Path;
                    string name = item.Name;
                    var entry = new QuickAccessEntry(string.IsNullOrWhiteSpace(name) ? GetFolderDisplayName(path) : name, NormalizeDirectoryPath(path), "📌 ");
                    entries.Add(entry);
                    onEntry?.Invoke(entry);
                }
                catch
                {
                    // Shell item単位の失敗は無視
                }
            }
        }
        catch
        {
            // COMエラーは起動体験を優先して無視
        }
    }

    private static void GatherRecentFolders(List<QuickAccessEntry> entries)
    {
        try
        {
            var wshShellType = Type.GetTypeFromProgID("WScript.Shell");
            var recentPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), @"Microsoft\Windows\Recent");
            if (wshShellType == null || !Directory.Exists(recentPath)) return;

            dynamic wshShell = Activator.CreateInstance(wshShellType)!;
            var recentFolders = new List<(string Name, string Path, DateTime LastWrite)>();

            foreach (var file in Directory.EnumerateFiles(recentPath, "*.lnk"))
            {
                try
                {
                    dynamic shortcut = wshShell.CreateShortcut(file);
                    string target = shortcut.TargetPath;
                    if (string.IsNullOrWhiteSpace(target) || !Directory.Exists(target)) continue;
                    var norm = NormalizeDirectoryPath(target);
                    recentFolders.Add((GetFolderDisplayName(norm), norm, File.GetLastWriteTime(file)));
                }
                catch
                {
                    // 個別ショートカットのパース失敗は無視
                }
            }

            foreach (var folder in recentFolders
                         .GroupBy(f => f.Path, StringComparer.OrdinalIgnoreCase)
                         .Select(g => g.OrderByDescending(f => f.LastWrite).First())
                         .OrderByDescending(f => f.LastWrite)
                         .Take(10))
            {
                entries.Add(new QuickAccessEntry(folder.Name, folder.Path, "⏱️ "));
            }
        }
        catch
        {
            // Recent取得失敗は無視
        }
    }

    private void AddQuickAccessExtrasToTree(List<QuickAccessEntry> entries)
    {
        if (_quickAccessNode == null || IsDisposed) return;
        _tree.BeginUpdate();
        try
        {
            foreach (var entry in entries)
            {
                if (!_quickAccessPaths.Add(entry.Path)) continue;
                var node = new TreeNode(entry.IconPrefix + entry.Name) { Tag = entry.Path };
                node.Nodes.Add(MakeDummy());
                _quickAccessNode.Nodes.Add(node);
            }
        }
        finally
        {
            _tree.EndUpdate();
            _tree.Refresh(); // Explorerテーマ適用直後の描画抜け対策
        }
    }

    private void AddQuickAccessExtraToTree(QuickAccessEntry entry)
    {
        if (_quickAccessNode == null || IsDisposed) return;
        if (!_quickAccessPaths.Add(entry.Path)) return;
        var node = new TreeNode(entry.IconPrefix + entry.Name) { Tag = entry.Path };
        node.Nodes.Add(MakeDummy());
        _quickAccessNode.Nodes.Add(node);
    }

    private readonly record struct QuickAccessEntry(string Name, string Path, string IconPrefix);

    private static string GetFolderDisplayName(string path)
    {
        var trimmed = path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var name = Path.GetFileName(trimmed);
        return string.IsNullOrWhiteSpace(name) ? trimmed : name;
    }

    private void AddOneDriveRoots()
    {
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        void AddPath(string? path)
        {
            if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path)) return;
            paths.Add(NormalizeDirectoryPath(path));
        }

        AddPath(Environment.GetEnvironmentVariable("OneDrive"));
        AddPath(Environment.GetEnvironmentVariable("OneDriveConsumer"));
        AddPath(Environment.GetEnvironmentVariable("OneDriveCommercial"));

        var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (Directory.Exists(profile))
        {
            foreach (var dir in Directory.EnumerateDirectories(profile, "OneDrive*")) AddPath(dir);
        }

        foreach (var path in paths.OrderBy(p => p))
        {
            var name = Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            if (string.IsNullOrWhiteSpace(name)) name = "OneDrive";
            var node = new TreeNode("☁️ " + name) { Tag = path };
            node.Nodes.Add(MakeDummy());
            _tree.Nodes.Add(node);
        }
    }

    private static string GetDriveLabelFast(DriveInfo drive)
    {
        var root = drive.Name.TrimEnd('\\');
        var type = drive.DriveType switch
        {
            DriveType.Fixed => "ローカル ディスク",
            DriveType.Removable => "リムーバブル ディスク",
            DriveType.CDRom => "DVD ドライブ",
            DriveType.Network => "ネットワーク ドライブ",
            DriveType.Ram => "RAM ディスク",
            _ => "ドライブ",
        };
        return $"{type} ({root})";
    }

    private static string GetDriveLabel(DriveInfo drive)
    {
        var root = drive.Name.TrimEnd('\\');
        var type = drive.DriveType switch
        {
            DriveType.Fixed => "ローカル ディスク",
            DriveType.Removable => "リムーバブル ディスク",
            DriveType.CDRom => "DVD ドライブ",
            DriveType.Network => "ネットワーク ドライブ",
            DriveType.Ram => "RAM ディスク",
            _ => "ドライブ",
        };

        try
        {
            if (drive.IsReady && !string.IsNullOrWhiteSpace(drive.VolumeLabel))
            {
                return $"{drive.VolumeLabel} ({root})";
            }
        }
        catch
        {
            // ignored
        }

        return $"{type} ({root})";
    }

    private void AddKnownFolderNode(TreeNode parent, string label, string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path)) return;
        var node = new TreeNode("📁 " + label) { Tag = NormalizeDirectoryPath(path) };
        node.Nodes.Add(MakeDummy());
        parent.Nodes.Add(node);
    }

    private static TreeNode MakeDummy() => new("読み込み中...") { Tag = new DummyTag() };

    private void LoadNodeChildren(TreeNode? node)
    {
        if (node == null) return;
        switch (node.Tag)
        {
            case string path:
                LoadFileSystemChildren(node, path);
                break;
            case NetworkTag:
                LoadNetworkChildren(node);
                break;
        }
    }

    private void LoadNetworkChildren(TreeNode node)
    {
        if (node.Nodes.Count != 1 || node.Nodes[0].Tag is not DummyTag) return;
        node.Nodes.Clear();

        var networkDrives = DriveInfo.GetDrives()
            .Where(d => d.DriveType == DriveType.Network)
            .OrderBy(d => d.Name)
            .ToArray();

        foreach (var drive in networkDrives)
        {
            var child = new TreeNode("💾 " + GetDriveLabel(drive)) { Tag = drive.RootDirectory.FullName };
            child.Nodes.Add(MakeDummy());
            node.Nodes.Add(child);
        }

        if (node.Nodes.Count == 0)
        {
            node.Nodes.Add(new TreeNode("UNCパスは上のパス欄に \\\\server\\share の形式で入力してください")
            {
                ForeColor = Theme.Current.TextDisabled,
                Tag = new VirtualOnlyTag("network-hint"),
            });
        }
    }

    private void LoadFileSystemChildren(TreeNode node, string path)
    {
        if (node.Nodes.Count != 1 || node.Nodes[0].Tag is not DummyTag) return;

        node.Nodes.Clear();
        try
        {
            foreach (var dir in Directory.EnumerateDirectories(path).OrderBy(Path.GetFileName))
            {
                var name = Path.GetFileName(dir.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
                if (string.IsNullOrWhiteSpace(name)) name = dir;
                var normalized = NormalizeDirectoryPath(dir);
                var child = new TreeNode("📁 " + name) { Tag = normalized };
                // Explorer同様、展開時に実体を確認する遅延方式
                child.Nodes.Add(MakeDummy());
                node.Nodes.Add(child);
            }

            foreach (var file in Directory.EnumerateFiles(path)
                         .Where(ImageDecoder.IsSupported)
                         .OrderBy(Path.GetFileName))
            {
                var imgNode = new TreeNode("🖼️ " + Path.GetFileName(file))
                {
                    Tag = new ImageFileTag(file),
                    ForeColor = Theme.Current.TreeImageFile,
                };
                node.Nodes.Add(imgNode);
            }
        }
        catch (UnauthorizedAccessException)
        {
            node.Nodes.Add(new TreeNode("アクセス権がありません") { ForeColor = Theme.Current.TextDisabled });
        }
        catch (IOException ex)
        {
            node.Nodes.Add(new TreeNode($"読み込み失敗: {ex.Message}") { ForeColor = Theme.Current.TextDisabled });
        }
    }

    private void OnTreeSelected(TreeNode? node)
    {
        switch (node?.Tag)
        {
            case ImageFileTag imageFile:
                SetPathText(imageFile.Path);
                _lastFolderPath = Path.GetDirectoryName(imageFile.Path);
                _canvas.AddImage(imageFile.Path);
                break;
            case string path:
                SetPathText(path);
                if (Directory.Exists(path)) _lastFolderPath = path;
                break;
            case VirtualOnlyTag virtualOnly:
                SetPathText(virtualOnly.Message);
                break;
        }
    }

    private void SetPathText(string text)
    {
        _pathEditInternalChange = true;
        try
        {
            _pathBox.Text = text;
        }
        finally
        {
            _pathEditInternalChange = false;
        }
    }

    private void SelectAllPathTextSoon()
    {
        if (_pathEditInternalChange) return;
        BeginInvoke(new Action(() => _pathBox.SelectAll()));
    }

    private void NavigatePathFromEditor()
    {
        var raw = _pathBox.Text.Trim().Trim('"');
        if (string.IsNullOrWhiteSpace(raw)) return;
        raw = Environment.ExpandEnvironmentVariables(raw);

        try
        {
            if (File.Exists(raw))
            {
                if (!ImageDecoder.IsSupported(raw))
                {
                    MessageBox.Show(this, "対応している画像ファイルではありません。", "移動できません", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }

                _canvas.AddImage(raw);
                var parent = Path.GetDirectoryName(raw);
                if (!string.IsNullOrWhiteSpace(parent) && Directory.Exists(parent)) NavigateFolder(parent);
                return;
            }

            var normalized = NormalizeDirectoryPath(raw);
            if (Directory.Exists(normalized))
            {
                NavigateFolder(normalized);
                return;
            }

            MessageBox.Show(this, "指定されたパスが見つかりません。", "移動できません", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "移動できません", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    // 現在のサイドバー表示に応じてツリー/サムネイルへナビゲート
    private void NavigateFolder(string path)
    {
        _lastFolderPath = path;
        if (_sidebarView == "thumbs" && _thumbs != null)
        {
            _thumbs.LoadFolder(path);
        }
        else
        {
            SelectDirectoryInTree(path);
        }
    }

    private void SelectDirectoryInTree(string path)
    {
        path = NormalizeDirectoryPath(path);
        _tree.BeginUpdate();
        try
        {
            var root = FindBestRootNode(path);
            if (root == null)
            {
                var directRoot = GetOrCreateDirectInputRoot();
                root = new TreeNode(path) { Tag = path };
                root.Nodes.Add(MakeDummy());
                directRoot.Nodes.Add(root);
            }

            var target = EnsurePathLoaded(root, path);
            target.EnsureVisible();
            _tree.SelectedNode = target;
            SetPathText(path);
        }
        finally
        {
            _tree.EndUpdate();
        }
    }

    private TreeNode GetOrCreateDirectInputRoot()
    {
        foreach (TreeNode n in _tree.Nodes)
        {
            if (n.Tag is RootTag { Kind: "direct" }) return n;
        }

        var root = new TreeNode("直接入力した場所") { Tag = new RootTag("direct") };
        _tree.Nodes.Insert(0, root);
        root.Expand();
        return root;
    }

    private TreeNode? FindBestRootNode(string targetPath)
    {
        return EnumerateNodes(_tree.Nodes)
            .Where(n => n.Tag is string p && IsSameOrParent(NormalizeDirectoryPath(p), targetPath))
            .OrderByDescending(n => ((string)n.Tag).Length)
            .FirstOrDefault();
    }

    private TreeNode EnsurePathLoaded(TreeNode root, string targetPath)
    {
        if (root.Tag is not string) return root;

        var current = root;
        current.Expand();
        LoadNodeChildren(current);

        while (current.Tag is string currentPath && !PathsEqual(currentPath, targetPath))
        {
            var rel = Path.GetRelativePath(currentPath, targetPath);
            if (string.IsNullOrWhiteSpace(rel) || rel == ".") break;
            var nextSegment = rel.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                .FirstOrDefault(s => !string.IsNullOrWhiteSpace(s));
            if (string.IsNullOrWhiteSpace(nextSegment)) break;

            var nextPath = NormalizeDirectoryPath(Path.Combine(currentPath, nextSegment));
            var child = current.Nodes.Cast<TreeNode>()
                .FirstOrDefault(n => n.Tag is string p && PathsEqual(p, nextPath));

            if (child == null)
            {
                child = new TreeNode(nextSegment) { Tag = nextPath };
                child.Nodes.Add(MakeDummy());
                current.Nodes.Add(child);
            }

            current = child;
            current.Expand();
            LoadNodeChildren(current);
        }

        return current;
    }

    private static IEnumerable<TreeNode> EnumerateNodes(TreeNodeCollection nodes)
    {
        foreach (TreeNode node in nodes)
        {
            yield return node;
            foreach (var child in EnumerateNodes(node.Nodes)) yield return child;
        }
    }

    private static bool PathsEqual(string a, string b)
    {
        return string.Equals(NormalizeDirectoryPath(a), NormalizeDirectoryPath(b), StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsSameOrParent(string parent, string child)
    {
        parent = NormalizeDirectoryPath(parent);
        child = NormalizeDirectoryPath(child);
        if (string.Equals(parent, child, StringComparison.OrdinalIgnoreCase)) return true;
        var withSlash = parent.EndsWith(Path.DirectorySeparatorChar) ? parent : parent + Path.DirectorySeparatorChar;
        return child.StartsWith(withSlash, StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeDirectoryPath(string path)
    {
        var full = Path.GetFullPath(path.Trim().Trim('"'));
        if (full.Length <= 3) return full;
        return full.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }

    private void AddFolderRoot()
    {
        using var dlg = new FolderBrowserDialog
        {
            Description = "右側の階層に追加するフォルダを選択してください",
            UseDescriptionForTitle = true,
            ShowNewFolderButton = false,
        };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        var selected = NormalizeDirectoryPath(dlg.SelectedPath);
        var node = new TreeNode(Path.GetFileName(selected.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)) ?? selected)
        {
            Tag = selected,
        };
        node.Nodes.Add(MakeDummy());
        _tree.Nodes.Insert(0, node);
        node.Expand();
        _tree.SelectedNode = node;
    }

    private readonly record struct RootTag(string Kind);
    private sealed class DummyTag { }
    private sealed class NetworkTag { }
    private readonly record struct VirtualOnlyTag(string Message);
    private readonly record struct ImageFileTag(string Path);
}
