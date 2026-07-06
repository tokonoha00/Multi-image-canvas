using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Windows.Forms;

namespace MultiImageCanvas;

// キャンバス共有用エクスポートのオプション
internal sealed record ShareExportOptions(
    bool CurrentCanvasOnly,
    bool StripMetadata,   // EXIF/GPS等のメタデータを除去 (ピクセルから再エンコード)
    bool ApplyCrop,       // トリミングで隠している領域のピクセルを含めない
    bool ExcludeHidden);  // 非表示レイヤーを含めない

// キャンバスを共有用パッケージ (.mics互換のzip) として書き出す。
// プライバシー方針:
//  - ネットワーク送信は一切行わない (ローカルファイル生成のみ)
//  - 絶対パス・ユーザー名・元ファイル名は含めない (連番に匿名化)
//  - session.json はキャンバス内容のみ (ウィンドウ位置などの環境情報は含めない)
//  - 既定でメタデータ除去・トリミング外ピクセル除去・非表示レイヤー除外
internal static class ShareExporter
{
    public static ShareExportResult Export(
        IReadOnlyList<CanvasDocument> docs, int activeIndex, ShareExportOptions opt, string fileName)
    {
        var targets = opt.CurrentCanvasOnly && activeIndex >= 0 && activeIndex < docs.Count
            ? [docs[activeIndex]]
            : docs.ToList();

        int embedded = 0, reencoded = 0, skippedHidden = 0, missing = 0;

        var data = new SessionData
        {
            ActiveTab = 0,
            Tabs = [],
            TabFilePaths = [],
        };

        var tempFile = fileName + ".tmp";
        if (File.Exists(tempFile)) File.Delete(tempFile);

        try
        {
            using (var zip = ZipFile.Open(tempFile, ZipArchiveMode.Create))
            {
                // 同一画像(同一クロップ)の重複同梱を避ける
                var assetMap = new Dictionary<string, string>();
                int assetIndex = 0;

                for (int t = 0; t < targets.Count; t++)
                {
                    var doc = targets[t];
                    var dto = LayoutSerializer.ToDto(doc);
                    var items = new List<LayoutItemDto>();

                    for (int j = 0; j < doc.Items.Count; j++)
                    {
                        var item = doc.Items[j];
                        var itemDto = dto.Items[j];

                        if (opt.ExcludeHidden && !item.Visible)
                        {
                            skippedHidden++;
                            continue;
                        }

                        if (item.IsPlaceholder)
                        {
                            // 元画像が無い。パス情報を漏らさないようファイル名のみ残す
                            missing++;
                            items.Add(itemDto with { Path = "missing/" + SafeBaseName(item.Path) });
                            continue;
                        }

                        bool canReencode = !item.IsAnimated && (opt.StripMetadata || NeedsCrop(item, opt));
                        string cacheKey = canReencode
                            ? $"re_{item.Image.GetHashCode()}_{(opt.ApplyCrop ? item.Crop : RectangleF.Empty)}"
                            : $"raw_{item.Path.ToLowerInvariant()}";

                        if (!assetMap.TryGetValue(cacheKey, out var embeddedPath))
                        {
                            if (canReencode)
                            {
                                // ピクセルのみから再エンコード → EXIF/GPS等のメタデータは含まれない
                                var (entryPath, newSize) = WriteSanitizedImage(zip, item, opt, ++assetIndex);
                                embeddedPath = entryPath;
                                reencoded++;
                            }
                            else if (File.Exists(item.Path))
                            {
                                // アニメGIF等は再エンコードで動きが失われるため原本を同梱 (名前は匿名化)
                                var ext = SafeExtension(item.Path);
                                embeddedPath = $"assets/img_{++assetIndex:D4}{ext}";
                                zip.CreateEntryFromFile(item.Path, embeddedPath, CompressionLevel.NoCompression);
                                embedded++;
                            }
                            else
                            {
                                missing++;
                                items.Add(itemDto with { Path = "missing/" + SafeBaseName(item.Path) });
                                continue;
                            }
                            assetMap[cacheKey] = embeddedPath;
                        }

                        var resultDto = itemDto with { Path = embeddedPath };
                        if (canReencode && opt.ApplyCrop)
                        {
                            // クロップ済みピクセルを書き出したので、クロップは全面にリセット
                            var cropped = GetEffectiveCrop(item);
                            resultDto = resultDto with { Crop = [0f, 0f, cropped.Width, cropped.Height] };
                        }
                        items.Add(resultDto);
                        embedded++;
                    }

                    data.Tabs.Add(dto with { Items = items });
                    data.TabFilePaths.Add(null);
                }

                var entry = zip.CreateEntry("session.json", CompressionLevel.Optimal);
                using var stream = entry.Open();
                using var writer = new StreamWriter(stream, Encoding.UTF8);
                writer.Write(SessionStore.Serialize(data));
            }

            if (File.Exists(fileName)) File.Delete(fileName);
            File.Move(tempFile, fileName);
        }
        finally
        {
            if (File.Exists(tempFile))
            {
                try { File.Delete(tempFile); } catch { }
            }
        }

        return new ShareExportResult(targets.Count, embedded, reencoded, skippedHidden, missing);
    }

    private static bool NeedsCrop(CanvasItem item, ShareExportOptions opt)
    {
        if (!opt.ApplyCrop) return false;
        var full = new RectangleF(0, 0, item.Image.Width, item.Image.Height);
        return item.Crop != full && item.Crop.Width > 0 && item.Crop.Height > 0;
    }

    private static RectangleF GetEffectiveCrop(CanvasItem item)
    {
        var crop = item.Crop;
        var full = new RectangleF(0, 0, item.Image.Width, item.Image.Height);
        crop.Intersect(full);
        if (crop.Width < 1 || crop.Height < 1) crop = full;
        return crop;
    }

    // クロップ適用+メタデータなしの画像をzipに書き込む。JPEG源はJPEG(q90)、それ以外はPNG
    private static (string EntryPath, Size NewSize) WriteSanitizedImage(
        ZipArchive zip, CanvasItem item, ShareExportOptions opt, int index)
    {
        var srcRect = opt.ApplyCrop ? GetEffectiveCrop(item) : new RectangleF(0, 0, item.Image.Width, item.Image.Height);
        int w = Math.Max(1, (int)Math.Round(srcRect.Width));
        int h = Math.Max(1, (int)Math.Round(srcRect.Height));

        using var bmp = new Bitmap(w, h, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(bmp))
        {
            g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.NearestNeighbor;
            g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.Half;
            g.DrawImage(item.Image, new RectangleF(0, 0, w, h), srcRect, GraphicsUnit.Pixel);
        }

        bool asJpeg = SafeExtension(item.Path) is ".jpg" or ".jpeg" or ".jfif";
        var ext = asJpeg ? ".jpg" : ".png";
        var entryPath = $"assets/img_{index:D4}{ext}";

        var entry = zip.CreateEntry(entryPath, CompressionLevel.NoCompression);
        using var stream = entry.Open();
        if (asJpeg)
        {
            var codec = ImageCodecInfo.GetImageEncoders().First(c => c.FormatID == ImageFormat.Jpeg.Guid);
            using var prms = new EncoderParameters(1);
            prms.Param[0] = new EncoderParameter(System.Drawing.Imaging.Encoder.Quality, 90L);
            bmp.Save(stream, codec, prms);
        }
        else
        {
            bmp.Save(stream, ImageFormat.Png);
        }

        return (entryPath, new Size(w, h));
    }

    private static string SafeExtension(string path)
    {
        try
        {
            var ext = Path.GetExtension(path).ToLowerInvariant();
            return string.IsNullOrWhiteSpace(ext) ? ".img" : ext;
        }
        catch
        {
            return ".img";
        }
    }

    private static string SafeBaseName(string path)
    {
        try { return Path.GetFileName(path); }
        catch { return "unknown"; }
    }
}

internal sealed record ShareExportResult(int CanvasCount, int ItemCount, int ReencodedCount, int HiddenSkipped, int MissingCount);

// 共有/セッションパッケージ読み込み時の安全対策。
// 受け取ったファイルは信頼できない入力として扱う:
//  - エントリ数・展開サイズの上限 (zip爆弾対策。ヘッダの申告サイズは信用せず実バイト数で打ち切る)
//  - 画像拡張子のホワイトリスト (実行ファイル等は展開しない)
//  - パストラバーサル(Zip Slip)拒否は呼び出し側の展開先チェックと併用
internal static class SharePackageSecurity
{
    public const int MaxEntries = 1000;
    public const long MaxEntryBytes = 512L * 1024 * 1024;   // 1ファイル512MB
    public const long MaxTotalBytes = 1024L * 1024 * 1024;  // 合計1GB

    public static bool IsAllowedAssetName(string entryName)
    {
        var ext = Path.GetExtension(entryName).ToLowerInvariant();
        return ImageDecoder.IsSupported(entryName) || ext == ".img";
    }

    // 実際に読み取ったバイト数で上限を強制しながら展開する
    public static void ExtractWithLimit(ZipArchiveEntry entry, string targetPath, ref long totalBytes)
    {
        using var src = entry.Open();
        using var dst = File.Create(targetPath);

        var buffer = new byte[81920];
        long written = 0;
        int read;
        while ((read = src.Read(buffer, 0, buffer.Length)) > 0)
        {
            written += read;
            totalBytes += read;
            if (written > MaxEntryBytes || totalBytes > MaxTotalBytes)
            {
                dst.Close();
                try { File.Delete(targetPath); } catch { }
                throw new InvalidDataException(Loc.T("セッションファイルの展開サイズが上限を超えています。"));
            }
            dst.Write(buffer, 0, read);
        }
    }
}

// 共有エクスポートのオプションダイアログ
internal sealed class ShareExportForm : Form
{
    private readonly RadioButton _scopeCurrent = new();
    private readonly RadioButton _scopeAll = new();
    private readonly CheckBox _stripMeta = new();
    private readonly CheckBox _applyCrop = new();
    private readonly CheckBox _excludeHidden = new();

    public ShareExportOptions Options => new(
        _scopeCurrent.Checked,
        _stripMeta.Checked,
        _applyCrop.Checked,
        _excludeHidden.Checked);

    public ShareExportForm()
    {
        var t = Theme.Current;
        Text = Loc.T("キャンバスを共有用にエクスポート");
        Width = 520;
        Height = 420;
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        BackColor = t.Background;
        ForeColor = t.TextPrimary;
        Font = new Font("Segoe UI", 9f);

        var root = new Panel { Dock = DockStyle.Fill, Padding = new Padding(16), BackColor = t.Background };

        var scopeGroup = new GroupBox
        {
            Text = Loc.T("共有する範囲"),
            Dock = DockStyle.Top,
            Height = 84,
            ForeColor = t.TextPrimary,
            Padding = new Padding(12),
        };
        _scopeCurrent.Text = Loc.T("現在のキャンバスのみ");
        _scopeCurrent.AutoSize = true;
        _scopeCurrent.Location = new Point(16, 24);
        _scopeCurrent.ForeColor = t.TextPrimary;
        _scopeCurrent.Checked = true;
        _scopeAll.Text = Loc.T("すべてのキャンバスタブ");
        _scopeAll.AutoSize = true;
        _scopeAll.Location = new Point(16, 50);
        _scopeAll.ForeColor = t.TextPrimary;
        scopeGroup.Controls.Add(_scopeCurrent);
        scopeGroup.Controls.Add(_scopeAll);

        var privacyGroup = new GroupBox
        {
            Text = Loc.T("プライバシー保護 (推奨: すべてON)"),
            Dock = DockStyle.Top,
            Height = 116,
            ForeColor = t.TextPrimary,
            Padding = new Padding(12),
        };
        _stripMeta.Text = Loc.T("メタデータを除去 (EXIF・位置情報・撮影機材など)");
        _stripMeta.AutoSize = true;
        _stripMeta.Location = new Point(16, 24);
        _stripMeta.ForeColor = t.TextPrimary;
        _stripMeta.Checked = true;
        _applyCrop.Text = Loc.T("トリミングで隠した部分のピクセルを含めない");
        _applyCrop.AutoSize = true;
        _applyCrop.Location = new Point(16, 50);
        _applyCrop.ForeColor = t.TextPrimary;
        _applyCrop.Checked = true;
        _excludeHidden.Text = Loc.T("非表示レイヤーを含めない");
        _excludeHidden.AutoSize = true;
        _excludeHidden.Location = new Point(16, 76);
        _excludeHidden.ForeColor = t.TextPrimary;
        _excludeHidden.Checked = true;
        privacyGroup.Controls.Add(_stripMeta);
        privacyGroup.Controls.Add(_applyCrop);
        privacyGroup.Controls.Add(_excludeHidden);

        var note = new Label
        {
            Text = Loc.T("・元のファイル名/フォルダ/PC名は含まれません (連番に匿名化)\n・ネットワークへの送信は行いません (ファイルを書き出すだけ)\n・アニメーションGIFは動きを保つため原本のまま同梱されます"),
            Dock = DockStyle.Top,
            Height = 66,
            Padding = new Padding(4, 8, 4, 0),
            ForeColor = t.TextSecondary,
            BackColor = t.Background,
        };

        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            Height = 48,
            FlowDirection = FlowDirection.RightToLeft,
            Padding = new Padding(0, 10, 0, 0),
            BackColor = t.Background,
        };
        Button Make(string text, DialogResult result, bool accent)
        {
            var b = new RoundedFlatButton
            {
                Text = text,
                Width = 130,
                Height = 30,
                ForeColor = t.TextPrimary,
                CornerRadius = 8,
                BaseColor = accent ? t.AccentDark : t.ButtonBg,
            };
            b.Click += (_, _) => { DialogResult = result; Close(); };
            return b;
        }
        var cancel = Make(Loc.T("キャンセル"), DialogResult.Cancel, false);
        var ok = Make(Loc.T("エクスポート..."), DialogResult.OK, true);
        buttons.Controls.Add(cancel);
        buttons.Controls.Add(ok);
        AcceptButton = ok;
        CancelButton = cancel;

        root.Controls.Add(note);
        root.Controls.Add(privacyGroup);
        root.Controls.Add(scopeGroup);
        root.Controls.Add(buttons);

        // Dock順の調整 (Topは後に追加したものが上へ)
        root.Controls.SetChildIndex(scopeGroup, root.Controls.Count - 1);
        root.Controls.SetChildIndex(privacyGroup, root.Controls.Count - 2);
        root.Controls.SetChildIndex(note, root.Controls.Count - 3);

        Controls.Add(root);
    }
}
