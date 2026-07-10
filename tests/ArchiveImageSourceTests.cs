using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.IO.Compression;
using MultiImageCanvas;
using Xunit;

namespace MultiImageCanvas.Tests;

public class ArchiveImageSourceTests : IDisposable
{
    private readonly string _tempDir;

    public ArchiveImageSourceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "mic_arc_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch { }
    }

    [Fact]
    public void LoadAndDecode_Cbz_WorksFromEntryStream()
    {
        var archivePath = Path.Combine(_tempDir, "book.cbz");
        using (var archive = ZipFile.Open(archivePath, ZipArchiveMode.Create))
        {
            var entry = archive.CreateEntry("chapter1/page01.png", CompressionLevel.Optimal);
            using var entryStream = entry.Open();
            using var bmp = new Bitmap(24, 12);
            using (var g = Graphics.FromImage(bmp)) g.Clear(Color.DeepSkyBlue);
            bmp.Save(entryStream, ImageFormat.Png);
        }

        var source = new ArchiveImageSource(archivePath);
        var metadata = Assert.Single(source.Entries);

        Assert.Equal("chapter1/page01.png", metadata.Key);
        Assert.Equal("chapter1", metadata.Folder);
        Assert.Equal(new[] { "chapter1/page01.png" }, source.GetImageKeys("chapter1"));

        using var image = source.Decode(metadata.Key);
        Assert.Equal(24, image.Width);
        Assert.Equal(12, image.Height);
    }

    [Fact]
    public void Constructor_RejectsParentRelativeEntryPath()
    {
        var archivePath = Path.Combine(_tempDir, "bad.cbz");
        using (var archive = ZipFile.Open(archivePath, ZipArchiveMode.Create))
        {
            var entry = archive.CreateEntry("../page01.png", CompressionLevel.NoCompression);
            using var entryStream = entry.Open();
            entryStream.WriteByte(1);
        }

        Assert.Throws<InvalidDataException>(() =>
        {
            _ = new ArchiveImageSource(archivePath);
        });
    }

    [Fact]
    public void Constructor_RejectsSuspiciousCompressionRatio()
    {
        var archivePath = Path.Combine(_tempDir, "bomb.cbz");
        using (var archive = ZipFile.Open(archivePath, ZipArchiveMode.Create))
        {
            var entry = archive.CreateEntry("page01.png", CompressionLevel.SmallestSize);
            using var entryStream = entry.Open();
            var bytes = new byte[256 * 1024];
            entryStream.Write(bytes, 0, bytes.Length);
        }

        var options = new ArchiveImageSourceOptions
        {
            MaxCompressionRatio = 4,
            MaxEntryBytes = 512 * 1024,
            MaxTotalUncompressedBytes = 512 * 1024,
            MaxReadBytes = 512 * 1024,
        };

        Assert.Throws<InvalidDataException>(() =>
        {
            _ = new ArchiveImageSource(archivePath, options);
        });
    }
}
