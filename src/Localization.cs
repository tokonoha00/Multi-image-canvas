namespace MultiImageCanvas;

internal static class Loc
{
    public const string Japanese = "日本語";
    public const string English = "English";

    private static readonly Dictionary<string, string> En = new()
    {
        ["未保存セッション"] = "Unsaved session",
        ["ファイル"] = "File",
        ["編集"] = "Edit",
        ["表示"] = "View",
        ["オーバーレイ"] = "Overlay",
        ["ヘルプ"] = "Help",
        ["新規"] = "New",
        ["画像、キャンバス、セッションを開く..."] = "Open image, canvas, or session...",
        ["現在のセッションを閉じ、新しい未保存セッションを開始します。"] = "Close the current session and start a new unsaved session.",
        ["画像、キャンバス設定ファイル、画像同梱セッションを開きます。"] = "Open an image, canvas settings file, or packaged session.",
        ["URLから画像を開く..."] = "Open image from URL...",
        ["画像URLをダウンロードしてキャンバスへ配置します。"] = "Download an image URL and place it on the canvas.",
        ["セッションを保存"] = "Save session",
        ["最後に保存または開いたセッションファイルへ保存します。未保存の場合は保存先を選びます。"] = "Save to the last saved or opened session file. If unsaved, choose a destination.",
        ["セッションを別名で保存..."] = "Save session as...",
        ["現在開いているキャンバスと使用画像を、新しいセッションファイルとして保存します。"] = "Save the open canvases and used images as a new session file.",
        ["キャンバスを保存"] = "Save canvas",
        ["現在選択中のキャンバスだけを、画像パス参照の設定ファイルとして保存します。"] = "Save only the current canvas as a settings file that references image paths.",
        ["キャンバスを画像として出力"] = "Export canvas as image",
        ["キャンバスに配置されている画像が収まる最小のサイズで出力します。"] = "Export at the smallest size that fits the images placed on the canvas.",
        ["新規キャンバスタブ"] = "New canvas tab",
        ["現在のセッション内に空のキャンバスタブを追加します。"] = "Add an empty canvas tab to the current session.",
        ["キャンバスを閉じる"] = "Close canvas",
        ["現在のキャンバスタブを閉じます。外部保存していない内容は復元できません。"] = "Close the current canvas tab. Unsaved external content cannot be restored.",
        ["設定..."] = "Settings...",
        ["終了"] = "Exit",
        ["元に戻す"] = "Undo",
        ["やり直す"] = "Redo",
        ["右に90°回転"] = "Rotate 90° right",
        ["左に90°回転"] = "Rotate 90° left",
        ["左右反転"] = "Flip horizontal",
        ["上下反転"] = "Flip vertical",
        ["複製"] = "Duplicate",
        ["削除"] = "Delete",
        ["キャンバス内を一括削除"] = "Clear canvas",
        ["ズームイン"] = "Zoom in",
        ["ズームアウト"] = "Zoom out",
        ["100% 表示"] = "100% view",
        ["全体表示"] = "Fit all",
        ["原寸で配置"] = "Place at original size",
        ["ONの間、画像を原寸ピクセルサイズで配置します"] = "When enabled, place images at their original pixel size.",
        ["ウィンドウを前面固定"] = "Always on top",
        ["UI非表示 (クリックで復帰)"] = "Hide UI (click to restore)",
        ["ショートカット一覧"] = "Shortcuts",
        ["ショートカットを編集..."] = "Edit shortcuts...",
        ["設定で変更可"] = "configurable",
        ["バージョン情報"] = "About",
        ["オーバーレイ有効化"] = "Enable overlay",
        ["クリック透過"] = "Click-through",
        ["オーバーレイ透過率"] = "Overlay opacity",
        ["オーバーレイ設定"] = "Overlay settings",
        ["オーバーレイ無効化"] = "Disable overlay",
        ["フォルダツリー"] = "Folder tree",
        ["サムネイル一覧"] = "Thumbnails",
        ["レイヤー"] = "Layers",
        ["赤ペン"] = "Red pen",
        ["黄色マーカー"] = "Yellow marker",
        ["消しゴム"] = "Eraser",
        ["全消し"] = "Clear paint",
        ["ダブルクリックで名前変更 / ドラッグで並べ替え"] = "Double-click to rename / drag to reorder",
        ["新規キャンバス (Ctrl+T)"] = "New canvas (Ctrl+T)",
        ["現在のキャンバスを閉じる (Ctrl+W)"] = "Close current canvas (Ctrl+W)",
        ["設定"] = "Settings",
        ["一般"] = "General",
        ["UIテーマ:"] = "UI theme:",
        ["起動時に前回のキャンバスタブを復元する"] = "Restore previous canvas tabs on startup",
        ["セッション自動保存間隔:"] = "Session autosave interval:",
        ["秒"] = "sec",
        ["スナップ機能"] = "Snap",
        ["画像スナップ"] = "Image snap",
        ["グリッドスナップ"] = "Grid snap",
        ["画像読み込み倍率:"] = "Initial image scale:",
        ["言語:"] = "Language:",
        ["オーバーレイ登場アニメ:"] = "Overlay entrance animation:",
        ["キー割り当て (ダブルクリックまたは「変更」で編集)"] = "Key bindings (double-click or Edit to change)",
        ["分類"] = "Category",
        ["操作"] = "Action",
        ["キー"] = "Key",
        ["変更..."] = "Edit...",
        ["割り当て解除"] = "Clear binding",
        ["選択を既定に戻す"] = "Reset selected",
        ["すべて既定に戻す"] = "Reset all",
        ["キャンセル"] = "Cancel",
        ["適用"] = "Apply",
        ["キー割り当て"] = "Key binding",
        ["キーの競合"] = "Key conflict",
        ["キーの入力"] = "Key input",
        ["修飾キーなしの英数字キーは割り当てられません。\nCtrl / Alt / Shift と組み合わせてください。"] = "Plain letters and numbers cannot be assigned.\nPlease combine them with Ctrl / Alt / Shift.",
        ["{0} は「{1}」に割り当てられています。\n置き換えますか？（元の操作は割り当てなしになります）"] = "{0} is already assigned to \"{1}\".\nReplace it? (The previous action will be unassigned.)",
        ["「{0}」に割り当てるキーを押してください\n(Escでキャンセル)"] = "Press the key for \"{0}\"\n(Esc to cancel)",
        ["画像を読み込めません"] = "Cannot load image",
        ["タブがありません (Ctrl+T で新規レイアウト)"] = "No tabs (Ctrl+T for a new canvas)",
        ["キャンバス名の変更"] = "Rename canvas",
        ["新しいキャンバス名:"] = "New canvas name:",
        ["自動保存ログはまだありません。"] = "No autosave log yet.",
        ["現在の設定、キャンバスを自動保存しました。"] = "Current settings and canvas were autosaved.",
        ["開けません"] = "Cannot open",
        ["対応しているセッション、キャンバス、画像ファイルではありません。"] = "This is not a supported session, canvas, or image file.",
        ["URLから画像を開く"] = "Open image from URL",
        ["画像URL:"] = "Image URL:",
        ["URLを開けません"] = "Cannot open URL",
        ["http または https の画像URLを入力してください。"] = "Enter an http or https image URL.",
        ["URL画像を読み込めません"] = "Cannot load URL image",
        ["セッションを保存しました。"] = "Session saved.",
        ["キャンバスを保存しました。"] = "Canvas saved.",
        ["キャンバスを画像として出力しました。"] = "Canvas exported as image.",
        ["一部の画像ファイルが見つからなかったため、プレースホルダで表示しています。"] = "Some image files were not found, so placeholders are shown.",
        ["現在のセッションの状態が失われます。\n外部保存していない内容は復元できません。\n\nこのセッションを開きますか？"] = "The current session state will be lost.\nContent not saved externally cannot be restored.\n\nOpen this session?",
        ["キャンバス内の画像 {0} 件をすべて削除しますか？\n(Ctrl+Zで元に戻せます)"] = "Delete all {0} images on the canvas?\n(You can undo with Ctrl+Z.)",
        ["完了"] = "Done",
        ["保存失敗"] = "Save failed",
        ["出力失敗"] = "Export failed",
        ["読込失敗"] = "Load failed",
        ["キャンバス読込"] = "Canvas load",
        ["セッションを開く"] = "Open session",
        ["書き出し失敗"] = "Export failed",
        ["一括削除"] = "Clear all",
        ["保存"] = "Save",
        ["閉じる"] = "Close",
        ["新規セッション"] = "New session",
        ["アプリを閉じる"] = "Close app",
        ["アプリを閉じますか？\n外部保存していないセッションは復元できません。"] = "Close the app?\nSessions not saved externally cannot be restored.",
        ["最後のキャンバスです。内容をすべて削除して新規状態にしますか？\n外部保存していない内容は復元できません。"] = "This is the last canvas. Clear its contents and reset it?\nContent not saved externally cannot be restored.",
        ["「{0}」を閉じますか？\n外部保存していないキャンバスは復元できません。"] = "Close \"{0}\"?\nCanvases not saved externally cannot be restored.",
        ["現在のセッションの状態が失われます。\n外部保存していないキャンバスは復元できません。"] = "The current session state will be lost.\nCanvases not saved externally cannot be restored.",
        ["リリース前"] = "Pre-release",
        ["前面へ"] = "Forward",
        ["背面へ"] = "Backward",
        ["最前面へ"] = "Bring to front",
        ["最背面へ"] = "Send to back",
        ["不透明度"] = "Opacity",
        ["不透明度: -"] = "Opacity: -",
        ["上へ"] = "Up",
        ["アクセス権がありません"] = "Access denied",
        ["読み込みに失敗しました"] = "Failed to load",
        ["移動"] = "Go",
        ["パスを入力して Enter"] = "Enter a path and press Enter",
        ["移動できません"] = "Cannot navigate",
        ["ブロック"] = "Blocks",
        ["フェード"] = "Fade",
        ["スライド"] = "Slide",
        ["ワイプ"] = "Wipe",
        ["なし"] = "None",
        ["ダーク"] = "Dark",
        ["ライト"] = "Light",
        ["ミッドナイト"] = "Midnight",
        ["ハイコントラスト"] = "High contrast",
    };

    private static readonly Dictionary<string, string> JaByEn = En
        .GroupBy(kv => kv.Value)
        .ToDictionary(g => g.Key, g => g.First().Key);

    public static string Language { get; private set; } = Japanese;
    public static bool IsEnglish => Language == English;

    public static void Apply(string language) => Language = Normalize(language);

    public static string Normalize(string? language)
    {
        if (string.Equals(language, English, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(language, "en", StringComparison.OrdinalIgnoreCase))
        {
            return English;
        }
        return Japanese;
    }

    public static string T(string japanese)
    {
        if (!IsEnglish) return japanese;
        return En.TryGetValue(japanese, out var english) ? english : japanese;
    }

    public static string Display(string text)
    {
        if (IsEnglish) return En.TryGetValue(text, out var english) ? english : text;
        return JaByEn.TryGetValue(text, out var japanese) ? japanese : text;
    }

    public static string Text(string text)
    {
        var exact = Display(text);
        if (exact != text) return exact;

        var map = IsEnglish ? En : JaByEn;
        foreach (var (from, to) in map.OrderByDescending(kv => kv.Key.Length))
        {
            if (text.Contains(from, StringComparison.Ordinal)) text = text.Replace(from, to);
        }
        return text;
    }

    public static string J(string text) => JaByEn.TryGetValue(text, out var japanese) ? japanese : text;
}
