using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MultiImageCanvas;

// 自動保存セッション。終了時/一定間隔で保存し、次回起動時に前回キャンバスを復元する。
// 保存先: %APPDATA%\MultiImageCanvas\session.json
internal sealed class SessionData
{
    public int Version { get; set; } = 1;
    public int[]? WindowBounds { get; set; }
    public bool Maximized { get; set; }
    public string SidebarView { get; set; } = "tree"; // tree | thumbs | layers
    public bool InsertNaturalSize { get; set; }
    public float OverlayOpacity { get; set; } = 1.0f;
    public bool OverlayClickThrough { get; set; }
    public bool OverlayFrameVisible { get; set; } = true;
    public float BgOpacity { get; set; } = 1.0f;
    public int ActiveTab { get; set; }
    public List<LayoutDto> Tabs { get; set; } = [];
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<int[]?>? OverlayLocations { get; set; }
    // タブごとの保存先パス (未保存タブは null)
    public List<string?> TabFilePaths { get; set; } = [];
    // キャンバスのグリッド線表示 (現在は常時ON・後方互換のため残置)
    public bool GridVisible { get; set; } = true;
}

// 設定画面で保存する永続設定。自動保存間隔には依存しない。
// 保存先: %APPDATA%\MultiImageCanvas\settings.json
internal sealed class AppSettingsData
{
    public int Version { get; set; } = 1;
    public string Theme { get; set; } = "ダーク";
    // 既定から変更されたキー割り当て (アクションID → Keys値)
    public Dictionary<string, int>? KeyBindings { get; set; }
    // 起動時に前回タブを復元するか
    public bool RestoreTabs { get; set; } = true;
    // セッション自動保存間隔 (秒)
    public int AutosaveSeconds { get; set; } = 30;
    public bool SnapEnabled { get; set; }
    public bool GridSnap { get; set; }
    public int ImageImportScalePercent { get; set; } = 100;
    public string Language { get; set; } = "日本語";
    public string OverlayAnimation { get; set; } = "フェード";
    public int[]? ViewerWindowBounds { get; set; }
    public bool ViewerMaximized { get; set; }
}

internal static class SessionStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    // MIC_SESSION_DIR 環境変数で保存先を上書き可能 (ポータブル運用・テスト隔離用)
    public static string Directory =>
        Environment.GetEnvironmentVariable("MIC_SESSION_DIR")
        ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MultiImageCanvas");

    public static string FilePath => Path.Combine(Directory, "session.json");
    public static string StartupBackupPath => Path.Combine(Directory, "session.startup.bak");

    public static SessionData? Load()
    {
        try
        {
            if (!File.Exists(FilePath)) return null;

            // 起動時点のセッションをバックアップしておく (誤上書き・破損からの復旧用)
            try { File.Copy(FilePath, StartupBackupPath, overwrite: true); }
            catch { /* バックアップ失敗は無視 */ }

            return Deserialize(File.ReadAllText(FilePath, Encoding.UTF8));
        }
        catch
        {
            return null; // 壊れたセッションは無視して新規開始
        }
    }

    public static string Serialize(SessionData data) => JsonSerializer.Serialize(data, JsonOptions);

    public static SessionData? Deserialize(string json) => JsonSerializer.Deserialize<SessionData>(json, JsonOptions);

    public static void Save(SessionData data)
    {
        try
        {
            System.IO.Directory.CreateDirectory(Directory);
            File.WriteAllText(FilePath, Serialize(data), Encoding.UTF8);
        }
        catch
        {
            // 保存失敗で起動や終了を妨げない
        }
    }
}

internal static class AppSettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public static string FilePath => Path.Combine(SessionStore.Directory, "settings.json");

    public static AppSettingsData? Load()
    {
        try
        {
            if (!File.Exists(FilePath)) return null;
            return JsonSerializer.Deserialize<AppSettingsData>(File.ReadAllText(FilePath, Encoding.UTF8), JsonOptions);
        }
        catch
        {
            return null;
        }
    }

    public static AppSettingsData? LoadLegacySession()
    {
        try
        {
            if (!File.Exists(SessionStore.FilePath)) return null;
            return JsonSerializer.Deserialize<AppSettingsData>(File.ReadAllText(SessionStore.FilePath, Encoding.UTF8), JsonOptions);
        }
        catch
        {
            return null;
        }
    }

    public static void Save(AppSettingsData data)
    {
        try
        {
            System.IO.Directory.CreateDirectory(SessionStore.Directory);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(data, JsonOptions), Encoding.UTF8);
        }
        catch
        {
            // 設定保存失敗で操作を妨げない
        }
    }
}
