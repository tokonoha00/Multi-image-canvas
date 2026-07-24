using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.IO.Compression;
using System.Text;
using MultiImageCanvas;
using Xunit;

namespace MultiImageCanvas.Tests;

public class ShareExportTests : IDisposable
{
    private readonly string _tempDir;

    public ShareExportTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "mic_share_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch { }
    }

    private CanvasDocument MakeDoc(out CanvasItem item, int w = 100, int h = 80)
    {
        var doc = new CanvasDocument("shared");
        var bmp = new Bitmap(w, h);
        using (var g = Graphics.FromImage(bmp)) g.Clear(Color.Orange);
        doc.RegisterImage(bmp);
        item = new CanvasItem
        {
            Image = bmp,
            Path = Path.Combine(_tempDir, "secret_username_photo.png"),
            Dest = new RectangleF(0, 0, 200, 160),
            Crop = new RectangleF(0, 0, w, h),
        };
        doc.Items.Add(item);
        return doc;
    }

    private static (SessionData Session, List<string> Entries) ReadPackage(string file)
    {
        using var zip = ZipFile.OpenRead(file);
        var entries = zip.Entries.Select(e => e.FullName).ToList();
        var sessionEntry = zip.GetEntry("session.json")!;
        using var reader = new StreamReader(sessionEntry.Open(), Encoding.UTF8);
        var session = SessionStore.Deserialize(reader.ReadToEnd())!;
        return (session, entries);
    }

    [Fact]
    public void Export_AnonymizesPathsAndAppliesCrop()
    {
        var doc = MakeDoc(out var item);
        item.Crop = new RectangleF(10, 10, 50, 40); // トリミング済み

        var file = Path.Combine(_tempDir, "share.mics");
        var result = ShareExporter.Export([doc], 0, new ShareExportOptions(true, true, true, true), file);

        Assert.Equal(1, result.CanvasCount);
        Assert.Equal(1, result.ItemCount);
        Assert.Equal(1, result.ReencodedCount);

        var (session, entries) = ReadPackage(file);
        var dto = Assert.Single(Assert.Single(session.Tabs).Items);

        // パスは匿名化された相対パスのみ
        Assert.StartsWith("assets/img_", dto.Path);
        Assert.DoesNotContain("secret_username", dto.Path);

        // クロップはピクセルに反映され、DTO上はリセットされる
        Assert.Equal(new float[] { 0f, 0f, 50f, 40f }, dto.Crop);

        // 同梱画像の実寸 = クロップ後サイズ
        using var zip = ZipFile.OpenRead(file);
        using var stream = zip.GetEntry(dto.Path)!.Open();
        using var ms = new MemoryStream();
        stream.CopyTo(ms);
        ms.Position = 0;
        using var img = Image.FromStream(ms);
        Assert.Equal(50, img.Width);
        Assert.Equal(40, img.Height);
    }

    [Fact]
    public void Export_ContainsNoAbsolutePathsOrUsername()
    {
        var doc = MakeDoc(out _);
        var file = Path.Combine(_tempDir, "share.mics");
        ShareExporter.Export([doc], 0, new ShareExportOptions(true, true, true, true), file);

        using var zip = ZipFile.OpenRead(file);
        using var reader = new StreamReader(zip.GetEntry("session.json")!.Open(), Encoding.UTF8);
        var json = reader.ReadToEnd();

        Assert.DoesNotContain(":\\\\", json);   // ドライブ絶対パス
        Assert.DoesNotContain("secret_username", json);
        Assert.DoesNotContain(Environment.UserName, json, StringComparison.OrdinalIgnoreCase);
        // 環境情報 (ウィンドウ位置) を含めない
        var (session, _) = ReadPackage(file);
        Assert.Null(session.WindowBounds);
        Assert.Null(session.OverlayLocations);
        Assert.Null(session.CanvasIds);
        Assert.Null(session.CanvasSwitchShortcuts);
    }

    [Fact]
    public void Export_ExcludesHiddenLayers()
    {
        var doc = MakeDoc(out var visibleItem);
        var hiddenBmp = new Bitmap(10, 10);
        doc.RegisterImage(hiddenBmp);
        doc.Items.Add(new CanvasItem
        {
            Image = hiddenBmp,
            Path = Path.Combine(_tempDir, "hidden.png"),
            Dest = new RectangleF(300, 0, 10, 10),
            Crop = new RectangleF(0, 0, 10, 10),
            Visible = false,
        });

        var file = Path.Combine(_tempDir, "share.mics");
        var result = ShareExporter.Export([doc], 0, new ShareExportOptions(true, true, true, ExcludeHidden: true), file);

        Assert.Equal(1, result.HiddenSkipped);
        var (session, entries) = ReadPackage(file);
        Assert.Single(session.Tabs[0].Items);
        Assert.Single(entries.Where(e => e.StartsWith("assets/")));
    }

    [Fact]
    public void Export_CurrentCanvasOnly_SelectsActiveTab()
    {
        var doc1 = MakeDoc(out _);
        var doc2 = MakeDoc(out _);

        var file = Path.Combine(_tempDir, "share.mics");
        ShareExporter.Export([doc1, doc2], 1, new ShareExportOptions(CurrentCanvasOnly: true, true, true, true), file);

        var (session, _) = ReadPackage(file);
        Assert.Single(session.Tabs);
    }

    [Theory]
    [InlineData("assets/a.png", true)]
    [InlineData("assets/a.webp", true)]
    [InlineData("assets/a.img", true)]
    [InlineData("assets/a.exe", false)]
    [InlineData("assets/a.dll", false)]
    [InlineData("assets/a.ps1", false)]
    [InlineData("assets/a.lnk", false)]
    public void PackageSecurity_AllowsOnlyImageAssets(string name, bool allowed)
    {
        Assert.Equal(allowed, SharePackageSecurity.IsAllowedAssetName(name));
    }
}
