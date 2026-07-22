using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Text.Json;
using System.Windows.Forms;
using MultiImageCanvas;
using Xunit;

namespace MultiImageCanvas.Tests;

public class AppSettingsTests
{
    [Fact]
    public void OverlayAnimation_DefaultsToFade()
    {
        Assert.Equal("フェード", new AppSettingsData().OverlayAnimation);
    }

    [Fact]
    public void ViewerWindowPlacement_RoundTrips()
    {
        var settings = new AppSettingsData { ViewerWindowBounds = [120, 80, 900, 600], ViewerMaximized = true };
        var loaded = JsonSerializer.Deserialize<AppSettingsData>(JsonSerializer.Serialize(settings))!;

        Assert.Equal(settings.ViewerWindowBounds, loaded.ViewerWindowBounds);
        Assert.True(loaded.ViewerMaximized);
    }
}

public class DragDropTests
{
    [Fact]
    public void ExtractUrlsFromData_DecodesHtmlEntities()
    {
        var data = new DataObject();
        data.SetData(DataFormats.Html, "<img src=\"https://pbs.twimg.com/media/test?format=jpg&amp;name=small\">");

        var urls = MainForm.ExtractUrlsFromData(data).ToArray();

        Assert.Contains("https://pbs.twimg.com/media/test?format=jpg&name=small", urls);
    }
}

public class GeometryTests
{
    [Fact]
    public void RotatePoint_90Degrees_RotatesClockwise()
    {
        var center = new PointF(0, 0);
        var p = new PointF(10, 0);
        var rotated = GeometryUtil.RotatePoint(p, center, 90f);
        // 時計回り90°: (10,0) → (0,10)  (Y軸下向きのスクリーン座標系)
        Assert.Equal(0, rotated.X, 3);
        Assert.Equal(10, rotated.Y, 3);
    }

    [Fact]
    public void RotatePoint_AroundNonOriginCenter()
    {
        var center = new PointF(5, 5);
        var p = new PointF(10, 5);
        var rotated = GeometryUtil.RotatePoint(p, center, 180f);
        Assert.Equal(0, rotated.X, 3);
        Assert.Equal(5, rotated.Y, 3);
    }

    [Fact]
    public void RotatePoint_ZeroDegrees_ReturnsSamePoint()
    {
        var p = new PointF(3, 7);
        var rotated = GeometryUtil.RotatePoint(p, new PointF(1, 1), 0f);
        Assert.Equal(p, rotated);
    }
}

public class CanvasItemTests
{
    private static CanvasItem MakeItem(RectangleF dest, float rotation = 0)
    {
        var bmp = new Bitmap(10, 10);
        return new CanvasItem
        {
            Image = bmp,
            Path = "test.png",
            Dest = dest,
            Crop = new RectangleF(0, 0, 10, 10),
            Rotation = rotation,
        };
    }

    [Fact]
    public void GetWorldBounds_NoRotation_EqualsDest()
    {
        var item = MakeItem(new RectangleF(10, 20, 100, 50));
        var b = item.GetWorldBounds();
        Assert.Equal(10, b.Left, 3);
        Assert.Equal(20, b.Top, 3);
        Assert.Equal(100, b.Width, 3);
        Assert.Equal(50, b.Height, 3);
    }

    [Fact]
    public void GetWorldBounds_Rotated90_SwapsWidthHeight()
    {
        var item = MakeItem(new RectangleF(0, 0, 100, 50), rotation: 90);
        var b = item.GetWorldBounds();
        Assert.Equal(50, b.Width, 2);
        Assert.Equal(100, b.Height, 2);
        // 中心は変わらない
        Assert.Equal(50, b.Left + b.Width / 2, 2);
        Assert.Equal(25, b.Top + b.Height / 2, 2);
    }

    [Fact]
    public void HitTest_InsideRotatedRect()
    {
        var item = MakeItem(new RectangleF(0, 0, 100, 20), rotation: 90);
        // 回転後は縦長になる: 中心(50,10) 幅20 高さ100
        Assert.True(item.HitTest(new PointF(50, 10)));   // 中心
        Assert.True(item.HitTest(new PointF(50, 55)));   // 回転後の下端付近
        Assert.False(item.HitTest(new PointF(95, 10)));  // 回転前なら内側だが回転後は外
    }

    [Fact]
    public void SnapshotRestore_RoundTrips()
    {
        var item = MakeItem(new RectangleF(1, 2, 3, 4), rotation: 45);
        item.FlipH = true;
        item.Opacity = 0.5f;
        var snap = item.Snapshot();

        item.Dest = new RectangleF(9, 9, 9, 9);
        item.Rotation = 0;
        item.FlipH = false;
        item.Opacity = 1f;
        item.Visible = false;

        item.Restore(snap);
        Assert.Equal(new RectangleF(1, 2, 3, 4), item.Dest);
        Assert.Equal(45, item.Rotation);
        Assert.True(item.FlipH);
        Assert.Equal(0.5f, item.Opacity);
        Assert.True(item.Visible);
    }
}

public class UndoStackTests
{
    private sealed class FakeCommand : IUndoCommand
    {
        public int UndoCount, RedoCount;
        public string Label => "fake";
        public void Undo() => UndoCount++;
        public void Redo() => RedoCount++;
    }

    [Fact]
    public void UndoRedo_BasicFlow()
    {
        var stack = new UndoStack();
        var cmd = new FakeCommand();

        Assert.False(stack.CanUndo);
        stack.Push(cmd);
        Assert.True(stack.CanUndo);
        Assert.False(stack.CanRedo);

        stack.Undo();
        Assert.Equal(1, cmd.UndoCount);
        Assert.False(stack.CanUndo);
        Assert.True(stack.CanRedo);

        stack.Redo();
        Assert.Equal(1, cmd.RedoCount);
        Assert.True(stack.CanUndo);
        Assert.False(stack.CanRedo);
    }

    [Fact]
    public void Push_ClearsRedoHistory()
    {
        var stack = new UndoStack();
        stack.Push(new FakeCommand());
        stack.Undo();
        Assert.True(stack.CanRedo);

        stack.Push(new FakeCommand());
        Assert.False(stack.CanRedo);
    }
}

public class CommandTests
{
    private static (CanvasDocument doc, CanvasItem a, CanvasItem b, CanvasItem c) MakeDoc()
    {
        var doc = new CanvasDocument("test");
        CanvasItem Make(string name)
        {
            var bmp = new Bitmap(4, 4);
            doc.RegisterImage(bmp);
            var item = new CanvasItem
            {
                Image = bmp,
                Path = name,
                Dest = new RectangleF(0, 0, 10, 10),
                Crop = new RectangleF(0, 0, 4, 4),
            };
            doc.Items.Add(item);
            return item;
        }
        return (doc, Make("a"), Make("b"), Make("c"));
    }

    [Fact]
    public void RemoveItems_UndoRestoresOriginalOrder()
    {
        var (doc, a, b, c) = MakeDoc();
        var cmd = new RemoveItemsCommand(doc, [b]);
        doc.Items.Remove(b);
        Assert.Equal(new[] { a, c }, doc.Items);

        cmd.Undo();
        Assert.Equal(new[] { a, b, c }, doc.Items);

        cmd.Redo();
        Assert.Equal(new[] { a, c }, doc.Items);
    }

    [Fact]
    public void RemoveAll_UndoRestoresAll()
    {
        var (doc, a, b, c) = MakeDoc();
        var cmd = new RemoveItemsCommand(doc, doc.Items.ToList(), "全画像の削除");
        doc.Items.Clear();
        Assert.Empty(doc.Items);

        cmd.Undo();
        Assert.Equal(new[] { a, b, c }, doc.Items);
    }

    [Fact]
    public void TransformCommand_UndoRedo()
    {
        var (doc, a, _, _) = MakeDoc();
        var before = a.Snapshot();
        a.Dest = new RectangleF(50, 50, 20, 20);
        a.Rotation = 90;
        var after = a.Snapshot();
        var cmd = new TransformCommand(doc, a, before, after, "移動");

        cmd.Undo();
        Assert.Equal(new RectangleF(0, 0, 10, 10), a.Dest);
        Assert.Equal(0, a.Rotation);

        cmd.Redo();
        Assert.Equal(new RectangleF(50, 50, 20, 20), a.Dest);
        Assert.Equal(90, a.Rotation);
    }

    [Fact]
    public void ReorderCommand_UndoRedo()
    {
        var (doc, a, b, c) = MakeDoc();
        // b を最前面 (末尾) へ
        doc.Items.RemoveAt(1);
        doc.Items.Insert(2, b);
        var cmd = new ReorderCommand(doc, b, 1, 2);
        Assert.Equal(new[] { a, c, b }, doc.Items);

        cmd.Undo();
        Assert.Equal(new[] { a, b, c }, doc.Items);

        cmd.Redo();
        Assert.Equal(new[] { a, c, b }, doc.Items);
    }
}

public class LayoutSerializerTests : IDisposable
{
    private readonly string _tempDir;

    public LayoutSerializerTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "mic_tests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch { }
    }

    private string CreateTestPng(string name, int w = 32, int h = 16)
    {
        var path = Path.Combine(_tempDir, name);
        using var bmp = new Bitmap(w, h);
        using (var g = Graphics.FromImage(bmp)) g.Clear(Color.Red);
        bmp.Save(path, ImageFormat.Png);
        return path;
    }

    [Fact]
    public void SaveLoad_RoundTripsAllProperties()
    {
        var imgPath = CreateTestPng("test.png");
        var doc = new CanvasDocument("roundtrip");
        var bmp = new Bitmap(32, 16);
        doc.RegisterImage(bmp);
        doc.Items.Add(new CanvasItem
        {
            Image = bmp,
            Path = imgPath,
            Dest = new RectangleF(10.5f, -20f, 300f, 150f),
            Crop = new RectangleF(2, 3, 20, 10),
            Rotation = 45.5f,
            FlipH = true,
            FlipV = false,
            Opacity = 0.75f,
            Visible = false,
        });
        doc.Zoom = 1.5f;
        doc.Scroll = new PointF(123, -456);

        var layoutPath = Path.Combine(_tempDir, "layout.micl");
        LayoutSerializer.Save(doc, layoutPath);
        Assert.False(doc.Dirty);

        var loaded = LayoutSerializer.Load(layoutPath);
        Assert.Single(loaded.Items);
        Assert.Equal(1.5f, loaded.Zoom);
        Assert.Equal(new PointF(123, -456), loaded.Scroll);

        var item = loaded.Items[0];
        Assert.Equal(imgPath, item.Path);
        Assert.Equal(new RectangleF(10.5f, -20f, 300f, 150f), item.Dest);
        Assert.Equal(new RectangleF(2, 3, 20, 10), item.Crop);
        Assert.Equal(45.5f, item.Rotation);
        Assert.True(item.FlipH);
        Assert.False(item.FlipV);
        Assert.Equal(0.75f, item.Opacity);
        Assert.False(item.Visible);
        Assert.False(item.IsPlaceholder);

        loaded.Dispose();
    }

    [Fact]
    public void Load_MissingImage_CreatesPlaceholderAndKeepsLayout()
    {
        var imgPath = CreateTestPng("will_delete.png");
        var doc = new CanvasDocument("missing");
        var bmp = new Bitmap(32, 16);
        doc.RegisterImage(bmp);
        doc.Items.Add(new CanvasItem
        {
            Image = bmp,
            Path = imgPath,
            Dest = new RectangleF(5, 6, 70, 80),
            Crop = new RectangleF(0, 0, 32, 16),
        });

        var layoutPath = Path.Combine(_tempDir, "layout2.micl");
        LayoutSerializer.Save(doc, layoutPath);
        File.Delete(imgPath);

        var loaded = LayoutSerializer.Load(layoutPath);
        var item = Assert.Single(loaded.Items);
        Assert.True(item.IsPlaceholder);
        // 配置情報は維持される
        Assert.Equal(new RectangleF(5, 6, 70, 80), item.Dest);
        // 元パスも維持され、再保存しても失われない
        Assert.Equal(imgPath, item.Path);

        loaded.Dispose();
    }

    [Fact]
    public void Deserialize_BrokenJson_ReturnsNullOrThrows()
    {
        Assert.ThrowsAny<Exception>(() => LayoutSerializer.Deserialize("{not json"));
    }

    [Fact]
    public void SessionSerialization_RoundTripsOverlayLocations()
    {
        var session = new SessionData
        {
            OverlayLocations = [new[] { 120, 240 }, null, new[] { -50, 30 }],
        };

        var loaded = SessionStore.Deserialize(SessionStore.Serialize(session));

        Assert.NotNull(loaded?.OverlayLocations);
        Assert.Equal(new[] { 120, 240 }, loaded.OverlayLocations[0]);
        Assert.Null(loaded.OverlayLocations[1]);
        Assert.Equal(new[] { -50, 30 }, loaded.OverlayLocations[2]);
    }

    [Fact]
    public void LayoutSerialization_DoesNotIncludeOverlayLocation()
    {
        using var doc = new CanvasDocument("private placement")
        {
            OverlayLocation = new Point(120, 240),
        };

        var json = LayoutSerializer.Serialize(doc);

        Assert.DoesNotContain("OverlayLocation", json, StringComparison.Ordinal);
    }
}

public class ImageDecoderTests : IDisposable
{
    private readonly string _tempDir;

    public ImageDecoderTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "mic_dec_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch { }
    }

    [Theory]
    [InlineData("a.png", true)]
    [InlineData("a.JPG", true)]
    [InlineData("a.webp", true)]
    [InlineData("a.heic", true)]
    [InlineData("a.avif", true)]
    [InlineData("a.gif", true)]
    [InlineData("a.txt", false)]
    [InlineData("a.exe", false)]
    public void IsSupported_ChecksExtension(string name, bool expected)
    {
        Assert.Equal(expected, ImageDecoder.IsSupported(name));
    }

    [Fact]
    public void Decode_Png_ReturnsImage()
    {
        var path = Path.Combine(_tempDir, "x.png");
        using (var bmp = new Bitmap(20, 30))
        {
            bmp.Save(path, ImageFormat.Png);
        }

        using var decoded = ImageDecoder.Decode(path);
        Assert.Equal(20, decoded.Width);
        Assert.Equal(30, decoded.Height);
    }

    [Fact]
    public void CreatePlaceholder_HasRequestedSize()
    {
        using var img = ImageDecoder.CreatePlaceholder(@"C:\missing\file.png");
        Assert.Equal(320, img.Width);
        Assert.Equal(200, img.Height);
    }

    [Fact]
    public void CreateThumbnail_FitsWithinSize()
    {
        var path = Path.Combine(_tempDir, "wide.png");
        using (var bmp = new Bitmap(200, 50))
        {
            bmp.Save(path, ImageFormat.Png);
        }

        using var thumb = ImageDecoder.CreateThumbnail(path, 88);
        Assert.Equal(88, thumb.Width);
        Assert.Equal(88, thumb.Height);
    }
}
