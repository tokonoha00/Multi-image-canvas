using System.Buffers;
using System.Drawing;
using System.IO;
using SharpCompress.Archives;
using SharpCompress.Common;
using SharpCompress.Readers;

namespace MultiImageCanvas;

internal sealed record ArchiveImageEntry(
    string Key,
    string Folder,
    string FileName,
    long CompressedSize,
    long UncompressedSize);

internal sealed class ArchiveImageSourceOptions
{
    public int MaxEntries { get; init; } = 4096;
    public long MaxArchiveBytes { get; init; } = 512L * 1024 * 1024;
    public long MaxEntryBytes { get; init; } = 128L * 1024 * 1024;
    public long MaxTotalUncompressedBytes { get; init; } = 512L * 1024 * 1024;
    public long MaxReadBytes { get; init; } = 128L * 1024 * 1024;
    public long MaxPixels { get; init; } = 80_000_000;
    public int MaxCompressionRatio { get; init; } = 200;
}

internal sealed class ArchiveImageSource
{
    private static readonly string[] ArchiveExtensions = [".zip", ".rar", ".7z", ".cbz", ".cbr", ".cb7"];

    private readonly Dictionary<string, ArchiveImageEntry> _entriesByKey;
    private readonly Dictionary<string, IReadOnlyList<string>> _keysByFolder;

    public ArchiveImageSource(string archivePath, ArchiveImageSourceOptions? options = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(archivePath);

        ArchivePath = Path.GetFullPath(archivePath);
        Options = options ?? new ArchiveImageSourceOptions();
        ValidateOptions(Options);

        var info = new FileInfo(ArchivePath);
        if (!info.Exists) throw new FileNotFoundException("Archive file was not found.", ArchivePath);
        if (info.Length > Options.MaxArchiveBytes) throw new InvalidDataException("Archive file size limit exceeded.");

        using var archive = OpenArchive(ArchivePath);
        var entries = new List<ArchiveImageEntry>();
        var entriesByKey = new Dictionary<string, ArchiveImageEntry>(StringComparer.Ordinal);
        var keysByFolder = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        long totalUncompressedBytes = 0;
        int totalEntries = 0;

        foreach (var entry in archive.Entries)
        {
            if (entry.IsDirectory) continue;

            totalEntries++;
            if (totalEntries > Options.MaxEntries) throw new InvalidDataException("Archive entry limit exceeded.");

            var key = NormalizeEntryKey(entry.Key);
            ValidateMetadata(entry, key);

            totalUncompressedBytes = checked(totalUncompressedBytes + entry.Size);
            if (totalUncompressedBytes > Options.MaxTotalUncompressedBytes)
            {
                throw new InvalidDataException("Archive total uncompressed size limit exceeded.");
            }

            if (!ImageDecoder.IsSupported(key)) continue;

            if (entry.IsEncrypted) throw new InvalidDataException("Encrypted image entries are not supported.");
            if (!entry.IsComplete) throw new InvalidDataException("Incomplete archive entries are not supported.");

            var folder = GetFolderName(key);
            var imageEntry = new ArchiveImageEntry(
                key,
                folder,
                Path.GetFileName(key),
                entry.CompressedSize,
                entry.Size);

            entries.Add(imageEntry);
            entriesByKey.Add(key, imageEntry);

            if (!keysByFolder.TryGetValue(folder, out var keys))
            {
                keys = [];
                keysByFolder.Add(folder, keys);
            }
            keys.Add(key);
        }

        Entries = entries;
        _entriesByKey = entriesByKey;
        _keysByFolder = keysByFolder.ToDictionary(
            pair => pair.Key,
            pair => (IReadOnlyList<string>)pair.Value.AsReadOnly(),
            StringComparer.Ordinal);
    }

    public string ArchivePath { get; }

    public ArchiveImageSourceOptions Options { get; }

    public IReadOnlyList<ArchiveImageEntry> Entries { get; }

    public static bool IsSupportedArchivePath(string path) =>
        ArchiveExtensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);

    public IReadOnlyList<string> GetImageKeys(string folder)
    {
        folder ??= string.Empty;
        return _keysByFolder.TryGetValue(folder, out var keys) ? keys : [];
    }

    public Image Decode(string key, bool fixTransparency = true)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        if (!_entriesByKey.TryGetValue(key, out var metadata))
        {
            throw new KeyNotFoundException($"Archive image key was not found: {key}");
        }

        using var archive = OpenArchive(ArchivePath);
        foreach (var entry in archive.Entries)
        {
            if (entry.IsDirectory || !string.Equals(NormalizeEntryKey(entry.Key), key, StringComparison.Ordinal))
            {
                continue;
            }

            ValidateMetadata(entry, key);
            if (entry.IsEncrypted) throw new InvalidDataException("Encrypted image entries are not supported.");
            if (!entry.IsComplete) throw new InvalidDataException("Incomplete archive entries are not supported.");

            using var entryStream = entry.OpenEntryStream();
            using var buffered = ReadEntryToMemory(entryStream, metadata);
            return ImageDecoder.Decode(buffered, metadata.FileName, fixTransparency, Options.MaxPixels);
        }

        throw new KeyNotFoundException($"Archive image entry was not found: {key}");
    }

    private static void ValidateOptions(ArchiveImageSourceOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (options.MaxEntries <= 0) throw new ArgumentOutOfRangeException(nameof(options.MaxEntries));
        if (options.MaxArchiveBytes <= 0) throw new ArgumentOutOfRangeException(nameof(options.MaxArchiveBytes));
        if (options.MaxEntryBytes <= 0) throw new ArgumentOutOfRangeException(nameof(options.MaxEntryBytes));
        if (options.MaxTotalUncompressedBytes <= 0) throw new ArgumentOutOfRangeException(nameof(options.MaxTotalUncompressedBytes));
        if (options.MaxReadBytes <= 0) throw new ArgumentOutOfRangeException(nameof(options.MaxReadBytes));
        if (options.MaxPixels <= 0) throw new ArgumentOutOfRangeException(nameof(options.MaxPixels));
        if (options.MaxCompressionRatio <= 0) throw new ArgumentOutOfRangeException(nameof(options.MaxCompressionRatio));
    }

    private static IArchive OpenArchive(string archivePath)
    {
        var options = new ReaderOptions { LeaveStreamOpen = false };
        return ArchiveFactory.OpenArchive(archivePath, options);
    }

    private void ValidateMetadata(IArchiveEntry entry, string normalizedKey)
    {
        if (string.IsNullOrEmpty(normalizedKey)) throw new InvalidDataException("Archive entry path is empty.");
        if (entry.Size < 0 || entry.CompressedSize < 0) throw new InvalidDataException("Archive entry size metadata is invalid.");
        if (entry.Size > Options.MaxEntryBytes) throw new InvalidDataException("Archive entry size limit exceeded.");

        if (entry.CompressedSize > 0)
        {
            long maxByRatio = checked(entry.CompressedSize * (long)Options.MaxCompressionRatio);
            if (entry.Size > maxByRatio) throw new InvalidDataException("Archive entry compression ratio is suspicious.");
        }
    }

    private MemoryStream ReadEntryToMemory(Stream stream, ArchiveImageEntry metadata)
    {
        long limit = Math.Min(Options.MaxReadBytes, Options.MaxEntryBytes);
        long ratioLimit = metadata.CompressedSize > 0
            ? checked(metadata.CompressedSize * (long)Options.MaxCompressionRatio)
            : long.MaxValue;
        long hardLimit = Math.Min(limit, ratioLimit);
        int capacity = metadata.UncompressedSize > 0 && metadata.UncompressedSize <= int.MaxValue
            ? (int)Math.Min(metadata.UncompressedSize, 1024 * 1024)
            : 0;
        var output = capacity > 0 ? new MemoryStream(capacity) : new MemoryStream();
        byte[] buffer = ArrayPool<byte>.Shared.Rent(81920);
        long totalRead = 0;
        try
        {
            while (true)
            {
                int read = stream.Read(buffer, 0, buffer.Length);
                if (read <= 0) break;

                totalRead = checked(totalRead + read);
                if (totalRead > hardLimit)
                {
                    throw new InvalidDataException("Archive entry read limit exceeded.");
                }

                output.Write(buffer, 0, read);
            }
        }
        catch
        {
            output.Dispose();
            throw;
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }

        if (metadata.UncompressedSize > 0 && totalRead > metadata.UncompressedSize)
        {
            output.Dispose();
            throw new InvalidDataException("Archive entry expanded beyond declared size.");
        }

        output.Position = 0;
        return output;
    }

    private static string NormalizeEntryKey(string? key)
    {
        if (string.IsNullOrWhiteSpace(key)) throw new InvalidDataException("Archive entry path is empty.");

        var normalizedSeparators = key.Replace('\\', '/');
        if (normalizedSeparators.StartsWith('/')) throw new InvalidDataException("Absolute archive entry paths are not allowed.");
        if (normalizedSeparators.Length >= 2 && normalizedSeparators[1] == ':') throw new InvalidDataException("Absolute archive entry paths are not allowed.");

        var segments = normalizedSeparators.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length == 0) throw new InvalidDataException("Archive entry path is empty.");

        var clean = new List<string>(segments.Length);
        foreach (var segment in segments)
        {
            if (segment == ".") continue;
            if (segment == "..") throw new InvalidDataException("Parent-relative archive entry paths are not allowed.");
            if (segment.IndexOf('\0') >= 0) throw new InvalidDataException("Archive entry path contains invalid characters.");
            clean.Add(segment);
        }

        if (clean.Count == 0) throw new InvalidDataException("Archive entry path is empty.");
        return string.Join('/', clean);
    }

    private static string GetFolderName(string key)
    {
        var index = key.LastIndexOf('/');
        return index >= 0 ? key[..index] : string.Empty;
    }
}
