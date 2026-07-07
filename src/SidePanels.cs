using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Threading;
using System.Windows.Forms;

namespace MultiImageCanvas;

// レイヤーパネル: キャンバス内画像の重ね順・表示/非表示・不透明度を管理する
internal sealed class LayerPanel : Panel
{
    private readonly CanvasSurface _canvas;
    private readonly ListBox _list = new();
    private readonly TrackBar _opacity = new();
    private readonly Label _opacityLabel = new();
    private bool _syncing;
    private bool _opacityDragging;

    public LayerPanel(CanvasSurface canvas)
    {
        _canvas = canvas;
        BackColor = Theme.Current.TreeBg;

        _list.Dock = DockStyle.Fill;
        _list.DrawMode = DrawMode.OwnerDrawFixed;
        _list.ItemHeight = 32;
        _list.BorderStyle = BorderStyle.None;
        _list.BackColor = Theme.Current.TreeBg;
        _list.ForeColor = Theme.Current.TextPrimary;
        _list.IntegralHeight = false;
        _list.DrawItem += List_DrawItem;
        _list.MouseDown += List_MouseDown;
        _list.SelectedIndexChanged += (_, _) =>
        {
            if (_syncing) return;
            var item = SelectedItem();
            if (item != null) _canvas.Select(item);
        };

        // 上部ボタン列
        var buttonRow = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            Height = 34,
            BackColor = Theme.Current.TreeBg,
            Padding = new Padding(2),
        };
        buttonRow.Controls.Add(MakeButton("▲", "前面へ", (_, _) => { var i = SelectedItem(); if (i != null) _canvas.ReorderItem(i, +1, false); }));
        buttonRow.Controls.Add(MakeButton("▼", "背面へ", (_, _) => { var i = SelectedItem(); if (i != null) _canvas.ReorderItem(i, -1, false); }));
        buttonRow.Controls.Add(MakeButton("⏫", "最前面へ", (_, _) => { var i = SelectedItem(); if (i != null) _canvas.ReorderItem(i, +1, true); }));
        buttonRow.Controls.Add(MakeButton("⏬", "最背面へ", (_, _) => { var i = SelectedItem(); if (i != null) _canvas.ReorderItem(i, -1, true); }));
        buttonRow.Controls.Add(MakeButton("🗑", "削除", (_, _) => { var i = SelectedItem(); if (i != null) _canvas.DeleteItem(i); }));

        _opacityLabel.Text = "不透明度: -";
        _opacityLabel.Dock = DockStyle.Top;
        _opacityLabel.Height = 18;
        _opacityLabel.ForeColor = Theme.Current.TextSecondary;

        _opacity.Dock = DockStyle.Fill;
        _opacity.Minimum = 5;
        _opacity.Maximum = 100;
        _opacity.Value = 100;
        _opacity.TickStyle = TickStyle.None;
        _opacity.Enabled = false;
        _opacity.MouseDown += (_, _) => { _canvas.BeginOpacityChange(); _opacityDragging = true; };
        _opacity.MouseUp += (_, _) => { if (_opacityDragging) { _canvas.CommitOpacityChange(); _opacityDragging = false; } RefreshList(); };
        _opacity.Scroll += (_, _) =>
        {
            _canvas.SetSelectedOpacityLive(_opacity.Value / 100f);
            _opacityLabel.Text = $"不透明度: {_opacity.Value}%";
        };

        Controls.Add(_list);
        Controls.Add(buttonRow);

        _canvas.SelectionChanged += (_, _) => SyncSelectionFromCanvas();
    }

    private Button MakeButton(string text, string tooltip, EventHandler onClick)
    {
        var btn = new RoundedFlatButton
        {
            Text = text,
            Width = 34,
            Height = 28,
            ForeColor = Theme.Current.TextPrimary,
            Margin = new Padding(1),
            CornerRadius = 8,
        };
        btn.Click += onClick;
        new ToolTip().SetToolTip(btn, tooltip);
        return btn;
    }

    private CanvasDocument? _doc;

    public void AttachDocument(CanvasDocument? doc)
    {
        if (_doc != null) _doc.Changed -= OnDocChanged;
        _doc = doc;
        if (_doc != null) _doc.Changed += OnDocChanged;
        RefreshList();
    }

    private void OnDocChanged(object? sender, EventArgs e)
    {
        if (!_opacityDragging) RefreshList();
    }

    // リストは最前面が先頭に来るよう逆順で表示する
    public void RefreshList()
    {
        _syncing = true;
        try
        {
            _list.BeginUpdate();
            _list.Items.Clear();
            if (_doc != null)
            {
                foreach (var item in _doc.Items.AsEnumerable().Reverse())
                {
                    _list.Items.Add(item);
                }
            }
            SyncSelectionCore();
            _list.EndUpdate();
        }
        finally
        {
            _syncing = false;
        }
        UpdateOpacityControls();
    }

    private void SyncSelectionFromCanvas()
    {
        _syncing = true;
        try { SyncSelectionCore(); }
        finally { _syncing = false; }
        UpdateOpacityControls();
    }

    private void SyncSelectionCore()
    {
        var sel = _canvas.Selected;
        _list.SelectedIndex = sel == null ? -1 : _list.Items.IndexOf(sel);
    }

    private void UpdateOpacityControls()
    {
        var sel = _canvas.Selected;
        _opacity.Enabled = sel != null;
        if (sel != null)
        {
            var v = (int)Math.Round(Math.Clamp(sel.Opacity, 0.05f, 1f) * 100);
            _opacity.Value = Math.Clamp(v, _opacity.Minimum, _opacity.Maximum);
            _opacityLabel.Text = $"不透明度: {v}%";
        }
        else
        {
            _opacityLabel.Text = "不透明度: -";
        }
    }

    private CanvasItem? SelectedItem() => _list.SelectedItem as CanvasItem;

    private void List_MouseDown(object? sender, MouseEventArgs e)
    {
        int index = _list.IndexFromPoint(e.Location);
        if (index < 0 || index >= _list.Items.Count) return;

        // 左端の目玉アイコン領域クリックで表示切替
        if (e.X < 28 && _list.Items[index] is CanvasItem item)
        {
            _canvas.SetSelectedVisible(item, !item.Visible);
            _list.Invalidate();
        }
    }

    private void List_DrawItem(object? sender, DrawItemEventArgs e)
    {
        var t = Theme.Current;
        var g = e.Graphics;
        bool selected = (e.State & DrawItemState.Selected) != 0;

        using (var bg = new SolidBrush(selected ? t.AccentDark : t.TreeBg))
        {
            g.FillRectangle(bg, e.Bounds);
        }

        if (e.Index < 0 || e.Index >= _list.Items.Count) return;
        if (_list.Items[e.Index] is not CanvasItem item) return;

        // 表示状態アイコン
        var eyeRect = new Rectangle(e.Bounds.X + 4, e.Bounds.Y + (e.Bounds.Height - 18) / 2, 20, 18);
        TextRenderer.DrawText(g, item.Visible ? "👁" : "－", Font, eyeRect,
            item.Visible ? t.TextPrimary : t.TextDisabled, TextFormatFlags.Left | TextFormatFlags.VerticalCenter);

        // 名前
        var nameRect = new Rectangle(e.Bounds.X + 30, e.Bounds.Y, e.Bounds.Width - 76, e.Bounds.Height);
        var nameColor = item.Visible ? t.TextPrimary : t.TextDisabled;
        TextRenderer.DrawText(g, item.DisplayName, Font, nameRect, nameColor,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);

        // 不透明度
        if (item.Opacity < 1f)
        {
            var opRect = new Rectangle(e.Bounds.Right - 44, e.Bounds.Y, 40, e.Bounds.Height);
            TextRenderer.DrawText(g, $"{(int)(item.Opacity * 100)}%", Font, opRect, t.TextSecondary,
                TextFormatFlags.Right | TextFormatFlags.VerticalCenter);
        }
    }
}

// サムネイルグリッド: フォルダ内画像を一覧表示する (ツリー表示との切替用)
internal sealed class ThumbnailView : Panel
{
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern int SendMessage(IntPtr hWnd, int msg, int wParam, int lParam);
    private const int LVM_SETICONSPACING = 0x1000 + 53;

    private readonly ListView _listView = new();
    private readonly ImageList _imageList = new();
    private string? _currentFolder;
    private CancellationTokenSource? _loadCts;

    // サイドバー幅(約270px)で最低3列並ぶサイズ
    private const int ThumbSize = 64;

    public event EventHandler<string>? ImageActivated;
    public event EventHandler<string>? FolderChanged;

    public string? CurrentFolder => _currentFolder;

    public ThumbnailView()
    {
        BackColor = Theme.Current.TreeBg;

        _imageList.ImageSize = new Size(ThumbSize, ThumbSize);
        _imageList.ColorDepth = ColorDepth.Depth32Bit;

        _listView.Dock = DockStyle.Fill;
        _listView.View = View.LargeIcon;
        _listView.LargeImageList = _imageList;
        _listView.BackColor = Theme.Current.TreeBg;
        _listView.ForeColor = Theme.Current.TextPrimary;
        _listView.BorderStyle = BorderStyle.None;
        _listView.MultiSelect = false;
        _listView.MouseClick += ListView_MouseClick;
        _listView.ItemActivate += (_, _) => ActivateSelected();
        // タイル間隔を詰めてサイドバー幅で3列表示できるようにする (MAKELONG(幅, 高さ))
        _listView.HandleCreated += (_, _) =>
            SendMessage(_listView.Handle, LVM_SETICONSPACING, 0, (100 << 16) | 82);

        Controls.Add(_listView);
    }

    private void ListView_MouseClick(object? sender, MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left) return;
        var item = _listView.GetItemAt(e.X, e.Y);
        if (item?.Tag is not string path) return;
        if (Directory.Exists(path)) LoadFolder(path);
        else if (File.Exists(path)) ImageActivated?.Invoke(this, path);
    }

    private void ActivateSelected()
    {
        if (_listView.SelectedItems.Count == 0) return;
        var item = _listView.SelectedItems[0];
        if (item.Tag is not string path) return;

        if (Directory.Exists(path))
        {
            LoadFolder(path);
        }
        else if (File.Exists(path))
        {
            ImageActivated?.Invoke(this, path);
        }
    }

    public void LoadFolder(string folder)
    {
        if (!Directory.Exists(folder)) return;
        _currentFolder = folder;
        FolderChanged?.Invoke(this, folder);

        _loadCts?.Cancel();
        _loadCts = new CancellationTokenSource();
        var ct = _loadCts.Token;

        _listView.BeginUpdate();
        _listView.Items.Clear();
        _imageList.Images.Clear();

        // 標準アイコン (0: フォルダ, 1: 画像プレースホルダ)
        _imageList.Images.Add(CreateIconTile("📁"));
        _imageList.Images.Add(CreateIconTile("🖼"));

        try
        {
            var parent = Directory.GetParent(folder)?.FullName;
            if (parent != null)
            {
                _listView.Items.Add(new ListViewItem("⬆ 上へ", 0) { Tag = parent });
            }

            foreach (var dir in Directory.EnumerateDirectories(folder).OrderBy(Path.GetFileName).Take(200))
            {
                _listView.Items.Add(new ListViewItem(Path.GetFileName(dir), 0) { Tag = dir });
            }

            var imageFiles = Directory.EnumerateFiles(folder)
                .Where(ImageDecoder.IsSupported)
                .OrderBy(Path.GetFileName)
                .Take(300)
                .ToList();

            foreach (var file in imageFiles)
            {
                _listView.Items.Add(new ListViewItem(Path.GetFileName(file), 1) { Tag = file });
            }

            // サムネイルは背景スレッドで順次生成してUIへ反映する
            if (imageFiles.Count > 0)
            {
                var listSnapshot = _listView.Items.Cast<ListViewItem>()
                    .Where(i => i.Tag is string p && ImageDecoder.IsSupported(p))
                    .ToList();

                ThreadPool.QueueUserWorkItem(_ =>
                {
                    try
                    {
                        Parallel.ForEach(listSnapshot, new ParallelOptions { CancellationToken = ct, MaxDegreeOfParallelism = Math.Min(4, Environment.ProcessorCount) }, lvi =>
                    {
                        var path = (string)lvi.Tag!;
                        Bitmap thumb;
                        try { thumb = ImageDecoder.CreateThumbnail(path, ThumbSize); }
                        catch { return; }

                        try
                        {
                            BeginInvoke(() =>
                            {
                                if (ct.IsCancellationRequested || IsDisposed || !ReferenceEquals(lvi.ListView, _listView)) { thumb.Dispose(); return; }
                                var copy = (Bitmap)thumb.Clone();
                                _imageList.Images.Add(copy);
                                lvi.ImageIndex = _imageList.Images.Count - 1;
                                copy.Dispose();
                                thumb.Dispose(); // ImageListが複製を保持する
                            });
                        }
                        catch
                        {
                            thumb.Dispose();
                            return; // フォーム破棄中
                        }
                        });
                    }
                    catch (OperationCanceledException)
                    {
                    }
                });
            }
        }
        catch (UnauthorizedAccessException)
        {
            _listView.Items.Add(new ListViewItem("アクセス権がありません", 0));
        }
        catch (IOException)
        {
            _listView.Items.Add(new ListViewItem("読み込みに失敗しました", 0));
        }
        finally
        {
            _listView.EndUpdate();
        }
    }

    private Bitmap CreateIconTile(string emoji)
    {
        var bmp = new Bitmap(ThumbSize, ThumbSize);
        using var g = Graphics.FromImage(bmp);
        g.Clear(Color.Transparent);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using (var bg = new SolidBrush(Color.FromArgb(28, 0, 0, 0)))
        using (var border = new Pen(Color.FromArgb(55, 0, 0, 0), 1f))
        using (var path = new GraphicsPath())
        {
            var rect = new RectangleF(8, 8, ThumbSize - 16, ThumbSize - 16);
            path.AddArc(rect.X, rect.Y, 10, 10, 180, 90);
            path.AddArc(rect.Right - 10, rect.Y, 10, 10, 270, 90);
            path.AddArc(rect.Right - 10, rect.Bottom - 10, 10, 10, 0, 90);
            path.AddArc(rect.X, rect.Bottom - 10, 10, 10, 90, 90);
            path.CloseFigure();
            g.FillPath(bg, path);
            g.DrawPath(border, path);
        }
        using var font = new Font("Segoe UI Emoji", 34f);
        var size = g.MeasureString(emoji, font);
        g.DrawString(emoji, font, Brushes.White, (ThumbSize - size.Width) / 2f, (ThumbSize - size.Height) / 2f);
        return bmp;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _loadCts?.Cancel();
            _imageList.Dispose();
        }
        base.Dispose(disposing);
    }
}
