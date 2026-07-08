using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace MultiImageCanvas;

// Windowsのファイル関連付け登録 (HKCUのみ・管理者権限不要)。
// ここでの登録は「開くプログラムの候補」への追加と既定アプリ選択画面への表示まで。
// 実際に既定にするのはWindowsの仕様上、ユーザーがWindows設定で選択する必要がある。
internal static class FileAssociation
{
    public const string AppName = "Multi Image Canvas";
    private const string CapabilitiesPath = @"Software\MultiImageCanvas\Capabilities";

    private const string ImageProgId = "MultiImageCanvas.Image";
    private const string CanvasProgId = "MultiImageCanvas.Canvas";
    private const string SessionProgId = "MultiImageCanvas.Session";

    [DllImport("shell32.dll")]
    private static extern void SHChangeNotify(int wEventId, uint uFlags, IntPtr dwItem1, IntPtr dwItem2);
    private const int SHCNE_ASSOCCHANGED = 0x08000000;

    // IThumbnailProvider インターフェースの GUID (ShellEx サブキー名)
    private const string ThumbnailProviderGuid = "{e357fccd-a995-4576-b01f-234630154e96}";
    // Windows標準の画像サムネイルハンドラ (Photo Thumbnail Provider)。
    // 画像ProgIDに登録すると、サムネイル系ビューでは画像サムネ、詳細/一覧ビューでは
    // DefaultIcon(アプリアイコン)が表示される — 既定アプリにしてもサムネを維持できる。
    private const string PhotoThumbnailProviderClsid = "{C7657C4A-9F68-40FA-A4DF-96BC08EB3551}";

    // 関連付け対象にできる拡張子 (画像 + 本アプリのファイル)
    public static IReadOnlyList<string> AssociableExtensions { get; } =
        [.. ImageDecoder.SupportedExtensions.Where(e => e != ".img"), ".micl", ".mics"];

    private static string ProgIdFor(string ext) => ext.ToLowerInvariant() switch
    {
        ".micl" => CanvasProgId,
        ".mics" => SessionProgId,
        _ => ImageProgId,
    };

    private static string? ExePath => Environment.ProcessPath;

    // 現在このアプリに登録済みの拡張子
    public static HashSet<string> GetRegisteredExtensions()
    {
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            using var fa = Registry.CurrentUser.OpenSubKey(CapabilitiesPath + @"\FileAssociations");
            if (fa != null)
            {
                foreach (var name in fa.GetValueNames())
                {
                    if (!string.IsNullOrEmpty(name)) result.Add(name);
                }
            }
        }
        catch
        {
            // レジストリ読み取り失敗は「未登録」扱い
        }
        return result;
    }

    // 選択された拡張子だけを登録し、外された拡張子は登録解除する
    public static void Apply(IReadOnlyCollection<string> selectedExtensions)
    {
        var exe = ExePath ?? throw new InvalidOperationException("実行ファイルのパスを取得できません。");
        var selected = new HashSet<string>(selectedExtensions, StringComparer.OrdinalIgnoreCase);

        // 1. ProgID (開くコマンド + アイコン)
        // 画像ProgIDだけはサムネイルハンドラを併せて登録し、既定アプリにしても
        // サムネイル表示を維持する (詳細/一覧ビューのみ DefaultIcon が使われる)。
        WriteProgId(ImageProgId, Loc.T("Multi Image Canvas 画像"), exe, keepThumbnails: true);
        WriteProgId(CanvasProgId, Loc.T("Multi Image Canvas キャンバス"), exe);
        WriteProgId(SessionProgId, Loc.T("Multi Image Canvas セッション"), exe);

        // 2. 既定アプリ選択画面用の Capabilities
        using (var caps = Registry.CurrentUser.CreateSubKey(CapabilitiesPath))
        {
            caps.SetValue("ApplicationName", AppName);
            caps.SetValue("ApplicationDescription", Loc.T("画像のオーバーレイ表示・キャンバス編集ツール"));
            using var fa = caps.CreateSubKey("FileAssociations");
            foreach (var name in fa.GetValueNames())
            {
                if (!selected.Contains(name)) fa.DeleteValue(name, throwOnMissingValue: false);
            }
            foreach (var ext in selected)
            {
                fa.SetValue(ext, ProgIdFor(ext));
            }
        }

        // 3. アプリ登録 (Windowsの「既定のアプリ」にアプリ名を表示させる)
        using (var ra = Registry.CurrentUser.CreateSubKey(@"Software\RegisteredApplications"))
        {
            ra.SetValue(AppName, CapabilitiesPath);
        }

        // 4. 拡張子ごとの「このアプリで開く」候補
        foreach (var ext in AssociableExtensions)
        {
            using var extKey = Registry.CurrentUser.CreateSubKey($@"Software\Classes\{ext}\OpenWithProgids");
            var progId = ProgIdFor(ext);
            if (selected.Contains(ext))
            {
                extKey.SetValue(progId, Array.Empty<byte>(), RegistryValueKind.None);
            }
            else
            {
                extKey.DeleteValue(progId, throwOnMissingValue: false);
            }
        }

        NotifyShell();
    }

    // すべての関連付け登録を解除する
    public static void UnregisterAll()
    {
        foreach (var ext in AssociableExtensions)
        {
            try
            {
                using var extKey = Registry.CurrentUser.OpenSubKey($@"Software\Classes\{ext}\OpenWithProgids", writable: true);
                extKey?.DeleteValue(ProgIdFor(ext), throwOnMissingValue: false);
            }
            catch { }
        }
        try { Registry.CurrentUser.DeleteSubKeyTree(@"Software\MultiImageCanvas", throwOnMissingSubKey: false); } catch { }
        try
        {
            using var ra = Registry.CurrentUser.OpenSubKey(@"Software\RegisteredApplications", writable: true);
            ra?.DeleteValue(AppName, throwOnMissingValue: false);
        }
        catch { }
        foreach (var progId in new[] { ImageProgId, CanvasProgId, SessionProgId })
        {
            try { Registry.CurrentUser.DeleteSubKeyTree($@"Software\Classes\{progId}", throwOnMissingSubKey: false); } catch { }
        }
        NotifyShell();
    }

    private static void WriteProgId(string progId, string description, string exe, bool keepThumbnails = false)
    {
        using var k = Registry.CurrentUser.CreateSubKey($@"Software\Classes\{progId}");
        k.SetValue("", description);
        using (var icon = k.CreateSubKey("DefaultIcon"))
        {
            icon.SetValue("", $"\"{exe}\",0");
        }
        using (var cmd = k.CreateSubKey(@"shell\open\command"))
        {
            cmd.SetValue("", $"\"{exe}\" \"%1\"");
        }

        // 画像ProgID: Windows標準サムネイルハンドラを紐付け、サムネイル系ビューでは
        // 画像サムネが出るようにする。詳細/一覧ビューでは DefaultIcon が使われる。
        if (keepThumbnails)
        {
            using var thumb = k.CreateSubKey($@"ShellEx\{ThumbnailProviderGuid}");
            thumb.SetValue("", PhotoThumbnailProviderClsid);
        }
        else
        {
            // 独自ファイル(.micl/.mics)は常にアプリアイコン: 誤った継承を残さない
            k.DeleteSubKeyTree($@"ShellEx\{ThumbnailProviderGuid}", throwOnMissingSubKey: false);
        }
    }

    private static void NotifyShell() => SHChangeNotify(SHCNE_ASSOCCHANGED, 0, IntPtr.Zero, IntPtr.Zero);

    // Windowsの「既定のアプリ」設定を開く (既定にする最終操作はユーザーが行う)
    public static void OpenWindowsDefaultAppsSettings()
    {
        try
        {
            Process.Start(new ProcessStartInfo("ms-settings:defaultapps") { UseShellExecute = true });
        }
        catch
        {
            // 設定アプリが開けない環境では無視
        }
    }
}
