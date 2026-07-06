using System.IO;
using System.Runtime.InteropServices;

namespace MultiImageCanvas;

internal static class KnownFolders
{
    private static readonly Guid DesktopGuid = new("B4BFCC3A-DB2C-424C-B029-7FE99A87C641");
    private static readonly Guid DocumentsGuid = new("FDD39AD0-238F-46AF-ADB4-6C85480369C7");
    private static readonly Guid DownloadsGuid = new("374DE290-123F-4565-9164-39C4925E467B");
    private static readonly Guid MusicGuid = new("4BD8D571-6D19-48D3-BE97-422220080E43");
    private static readonly Guid PicturesGuid = new("33E28130-4E1E-4676-835A-98395C3BC3BB");
    private static readonly Guid VideosGuid = new("18989B1D-99B5-455B-841C-AB7C74E4DDFC");
    private static readonly Guid Objects3DGuid = new("31C0DD25-9439-4F12-BF41-7FF4EDA38722");

    public static string? GetDesktopPath() => GetKnownFolderPath(DesktopGuid) ?? Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
    public static string? GetDocumentsPath() => GetKnownFolderPath(DocumentsGuid) ?? Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
    public static string? GetDownloadsPath() => GetKnownFolderPath(DownloadsGuid) ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
    public static string? GetMusicPath() => GetKnownFolderPath(MusicGuid) ?? Environment.GetFolderPath(Environment.SpecialFolder.MyMusic);
    public static string? GetPicturesPath() => GetKnownFolderPath(PicturesGuid) ?? Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);
    public static string? GetVideosPath() => GetKnownFolderPath(VideosGuid) ?? Environment.GetFolderPath(Environment.SpecialFolder.MyVideos);
    public static string? Get3DObjectsPath() => GetKnownFolderPath(Objects3DGuid);

    private static string? GetKnownFolderPath(Guid guid)
    {
        try
        {
            var result = SHGetKnownFolderPath(guid, 0, IntPtr.Zero, out var outPath);
            if (result != 0) return null;
            var path = Marshal.PtrToStringUni(outPath);
            Marshal.FreeCoTaskMem(outPath);
            return path;
        }
        catch
        {
            return null;
        }
    }

    [DllImport("shell32.dll")]
    private static extern int SHGetKnownFolderPath([MarshalAs(UnmanagedType.LPStruct)] Guid rfid, uint dwFlags, IntPtr hToken, out IntPtr ppszPath);
}
