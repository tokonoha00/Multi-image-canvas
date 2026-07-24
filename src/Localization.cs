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
        ["オーバーレイ外枠"] = "Overlay frame",
        ["オーバーレイ不透明度を上げる"] = "Increase overlay opacity",
        ["オーバーレイ不透明度を下げる"] = "Decrease overlay opacity",
        ["オーバーレイ設定"] = "Overlay settings",
        ["オーバーレイの表示位置をリセット"] = "Reset overlay position",
        ["オーバーレイ無効化"] = "Disable overlay",
        ["フォルダツリー"] = "Folder tree",
        ["ファイル選択メニューを最小化"] = "Minimize file picker",
        ["ファイル選択メニューを復元"] = "Restore file picker",
        ["サムネイル一覧"] = "Thumbnails",
        ["レイヤー"] = "Layers",
        ["画像選択"] = "Select images",
        ["赤ペン"] = "Red pen",
        ["黄色マーカー"] = "Yellow marker",
        ["消しゴム"] = "Eraser",
        ["全消し"] = "Clear paint",
        ["右クリックで名前変更 / ドラッグで並べ替え"] = "Right-click to rename / drag to reorder",
        ["切り替えキー: {0}"] = "Switch key: {0}",
        ["切り替えキー: 未設定"] = "Switch key: Not set",
        ["切り替えキーを設定..."] = "Set switch key...",
        ["{0}へ切り替え"] = "Switch to {0}",
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
        ["キャンバス切り替えには修飾キーを含む組み合わせ、またはファンクションキーを割り当ててください。"] = "Assign a shortcut with a modifier, or use a function key, to switch canvases.",
        ["{0} は「{1}」に割り当てられています。\n置き換えますか？（元の操作は割り当てなしになります）"] = "{0} is already assigned to \"{1}\".\nReplace it? (The previous action will be unassigned.)",
        ["{0} は「{1}」に割り当てられています。設定画面で先に割り当てを変更してください。"] = "{0} is assigned to \"{1}\". Change that binding in Settings first.",
        ["{0} は「{1}」の切り替えに割り当てられています。移動しますか？"] = "{0} switches to \"{1}\". Move the binding?",
        ["このキーはアプリの固定操作に使用されています。"] = "This key is used by a fixed app command.",
        ["このキーはWindowsまたは別のアプリで使用されているため登録できませんでした。"] = "This key could not be registered because Windows or another app is using it.",
        ["設定したキーと競合したため、次のキャンバスの切り替えキーを解除しました:\n{0}"] = "The switch keys for these canvases were cleared because they conflict with the configured bindings:\n{0}",
        ["「{0}」に割り当てるキーを押してください\n(Escでキャンセル)"] = "Press the key for \"{0}\"\n(Esc to cancel)",
        ["画像を読み込めません"] = "Cannot load image",
        ["圧縮ファイル"] = "Archive",
        ["フォルダ、画像を検索"] = "Search folders and images",
        ["圧縮ファイルを読み込めません"] = "Cannot load archive",
        ["圧縮ファイル内に対応画像がありません。"] = "The archive contains no supported images.",
        ["圧縮ファイル内の画像はビュアーでのみ表示できます。"] = "Images in archives can only be viewed in the viewer.",
        ["ビュアーを起動できません。"] = "Cannot start the viewer.",
        ["タブがありません (Ctrl+T で新規レイアウト)"] = "No tabs (Ctrl+T for a new canvas)",
        ["キャンバス名の変更"] = "Rename canvas",
        ["新しいキャンバス名:"] = "New canvas name:",
        ["自動保存ログはまだありません。"] = "No autosave log yet.",
        ["現在の設定、キャンバスを自動保存しました。"] = "Current settings and canvas were autosaved.",
        ["開けません"] = "Cannot open",
        ["対応しているセッション、キャンバス、画像ファイルではありません。"] = "This is not a supported session, canvas, or image file.",
        ["対応しているセッション、キャンバス、画像、圧縮ファイルではありません。"] = "This is not a supported session, canvas, image, or archive file.",
        ["URLから画像を開く"] = "Open image from URL",
        ["画像URL:"] = "Image URL:",
        ["URLを開けません"] = "Cannot open URL",
        ["http または https の画像URLを入力してください。"] = "Enter an http or https image URL.",
        ["URL画像を読み込めません"] = "Cannot load URL image",
        ["URLの応答は画像ではありません。"] = "The URL response is not an image.",
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
        ["最大化"] = "Maximize",
        ["最小化"] = "Minimize",
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
        // 共有機能
        ["共有用にエクスポート..."] = "Export for sharing...",
        ["共有用にエクスポート"] = "Export for sharing",
        ["個人情報を含まない画像同梱ファイル(.mics)を作成します。受け取った人はこのアプリでそのまま開けます。"] = "Create a packaged file (.mics) without personal information. Recipients can open it directly with this app.",
        ["キャンバスを共有用にエクスポート"] = "Export canvas for sharing",
        ["共有する範囲"] = "What to share",
        ["現在のキャンバスのみ"] = "Current canvas only",
        ["すべてのキャンバスタブ"] = "All canvas tabs",
        ["プライバシー保護 (推奨: すべてON)"] = "Privacy protection (recommended: all ON)",
        ["メタデータを除去 (EXIF・位置情報・撮影機材など)"] = "Strip metadata (EXIF, GPS, camera info)",
        ["トリミングで隠した部分のピクセルを含めない"] = "Exclude pixels hidden by cropping",
        ["非表示レイヤーを含めない"] = "Exclude hidden layers",
        ["・元のファイル名/フォルダ/PC名は含まれません (連番に匿名化)\n・ネットワークへの送信は行いません (ファイルを書き出すだけ)\n・アニメーションGIFは動きを保つため原本のまま同梱されます"] = "- Original file names, folders, and PC name are not included (anonymized)\n- Nothing is sent over the network (only writes a file)\n- Animated GIFs are embedded as-is to preserve motion",
        ["エクスポート..."] = "Export...",
        ["共有できる画像がキャンバスにありません。"] = "There are no images on the canvas to share.",
        ["共有用ファイルを書き出しました。\nキャンバス: {0} / 同梱画像: {1} (うちメタデータ除去 {2})"] = "Share file created.\nCanvases: {0} / embedded images: {1} ({2} with metadata stripped)",
        ["除外した非表示レイヤー: {0}"] = "Hidden layers excluded: {0}",
        ["元ファイルが見つからず同梱できなかった画像: {0}"] = "Images not embedded (source file missing): {0}",
        ["セッションファイルのエントリ数が多すぎます。"] = "The session file contains too many entries.",
        ["セッションファイルの展開サイズが上限を超えています。"] = "The session file exceeds the extraction size limit.",
        // ビュアーモード
        ["前のコマ"] = "Previous frame",
        ["次のコマ"] = "Next frame",
        ["再生 / 停止"] = "Play / Pause",
        ["再生速度"] = "Playback speed",
        ["前の画像"] = "Previous image",
        ["次の画像"] = "Next image",
        ["全画面"] = "Fullscreen",
        ["全画面表示"] = "Enter fullscreen",
        ["解除"] = "Exit",
        ["キャンバスにて編集"] = "Edit on canvas",
        ["通常の編集画面に切り替え、この画像を新しいキャンバスに配置した状態にします。"] = "Switch to the normal editor with this image placed on a new canvas.",
        // 設定タブ・ファイル関連付け
        ["キー割り当て"] = "Key bindings",
        ["ファイル関連付け"] = "File associations",
        ["ダブルクリックまたは「変更」で編集"] = "Double-click or use Change to edit",
        ["グローバルホットキー:"] = "Global hotkeys:",
        ["Windows 11 では、既定のアプリは設定画面でユーザーが選択する必要があります。\n下のボタンを押すと、Multi Image Canvas の登録を整えたうえで、このアプリの既定のアプリ設定画面を開きます。\n(登録は現在のユーザーのみ・管理者権限不要)"] = "On Windows 11, the default app must be chosen by the user in Settings.\nClick the button below to prepare Multi Image Canvas registration and open this app's default app settings page.\n(Registered for the current user only; no admin rights required)",
        ["Multi Image Canvasを既定のアプリに設定"] = "Set Multi Image Canvas as the default app",
        ["Windowsの既定のアプリ設定を開けませんでした。"] = "Could not open Windows default app settings.",
        ["このアプリで開けるようにする拡張子を選んで「登録」を押してください。\n登録後、「Windowsの既定のアプリ設定」で Multi Image Canvas を既定に選べます。\n(登録は現在のユーザーのみ・管理者権限不要)"] = "Select the extensions to open with this app and press Register.\nAfter registering, choose Multi Image Canvas in Windows default app settings.\n(Registered for the current user only; no admin rights required)",
        ["選択した拡張子を登録"] = "Register selected extensions",
        ["すべて解除"] = "Unregister all",
        ["Windowsの既定のアプリ設定を開く"] = "Open Windows default app settings",
        ["{0} 件の拡張子を登録しました。\n既定のアプリにするには「Windowsの既定のアプリ設定を開く」から Multi Image Canvas を選択してください。"] = "Registered {0} extensions.\nTo make it the default, choose Multi Image Canvas in Windows default app settings.",
        ["ファイル関連付けをすべて解除しました。"] = "All file associations have been removed.",
        ["Multi Image Canvas 画像"] = "Multi Image Canvas image",
        ["Multi Image Canvas キャンバス"] = "Multi Image Canvas canvas",
        ["Multi Image Canvas セッション"] = "Multi Image Canvas session",
        ["画像のオーバーレイ表示・キャンバス編集ツール"] = "Image overlay viewer and canvas editor",
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
