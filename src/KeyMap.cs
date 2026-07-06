using System.Windows.Forms;

namespace MultiImageCanvas;

// 変更可能なキーバインド1件
internal sealed class KeyBinding(string id, string category, string label, Keys defaultKeys)
{
    public string Id { get; } = id;
    public string Category { get; } = category;
    public string Label { get; } = label;
    public Keys Default { get; } = defaultKeys;
    public Keys Current { get; set; } = defaultKeys;
}

// アクションIDとキーの対応表。設定ダイアログから変更でき、セッションに保存される。
internal sealed class KeyMap
{
    private static readonly KeysConverter Converter = new();

    public List<KeyBinding> Bindings { get; } =
    [
        new("file.save", "ファイル", "セッションを保存", Keys.Control | Keys.S),
        new("file.saveAs", "ファイル", "セッションを別名で保存", Keys.Control | Keys.Shift | Keys.S),
        new("file.open", "ファイル", "画像、キャンバス、セッションを開く", Keys.Control | Keys.O),
        new("file.export", "ファイル", "キャンバスを画像として出力", Keys.Control | Keys.E),
        new("file.newTab", "ファイル", "新規キャンバスタブ", Keys.Control | Keys.T),
        new("file.closeTab", "ファイル", "キャンバスを閉じる", Keys.Control | Keys.W),
        new("tab.next", "タブ", "次のキャンバスへ", Keys.Control | Keys.Alt | Keys.Right),
        new("tab.prev", "タブ", "前のキャンバスへ", Keys.Control | Keys.Alt | Keys.Left),
        new("edit.undo", "編集", "元に戻す", Keys.Control | Keys.Z),
        new("edit.redo", "編集", "やり直す", Keys.Control | Keys.Y),
        new("edit.duplicate", "編集", "複製", Keys.Control | Keys.D),
        new("edit.rotateCw", "編集", "右に90°回転", Keys.Control | Keys.R),
        new("edit.rotateCcw", "編集", "左に90°回転", Keys.Control | Keys.Shift | Keys.R),
        new("edit.flipH", "編集", "左右反転", Keys.Control | Keys.H),
        new("edit.flipV", "編集", "上下反転", Keys.Control | Keys.Shift | Keys.H),
        new("edit.delete", "編集", "選択画像を削除", Keys.Delete),
        new("view.fitAll", "表示", "全体表示", Keys.Control | Keys.D0),
        new("view.zoom100", "表示", "100%表示", Keys.Control | Keys.D1),
        new("view.zoomIn", "表示", "ズームイン", Keys.Control | Keys.Oemplus),
        new("view.zoomOut", "表示", "ズームアウト", Keys.Control | Keys.OemMinus),
        new("view.hideUi", "表示", "UI非表示", Keys.F11),
        new("help.shortcuts", "ヘルプ", "ショートカット一覧", Keys.F1),
    ];

    public Keys Get(string id) => Bindings.FirstOrDefault(b => b.Id == id)?.Current ?? Keys.None;

    public bool TryGetAction(Keys keyData, out string id)
    {
        var hit = Bindings.FirstOrDefault(b => b.Current != Keys.None && b.Current == keyData);
        id = hit?.Id ?? "";
        return hit != null;
    }

    public KeyBinding? FindByKeys(Keys keys) =>
        keys == Keys.None ? null : Bindings.FirstOrDefault(b => b.Current == keys);

    public string GetDisplay(string id)
    {
        var keys = Get(id);
        return keys == Keys.None ? "" : ToDisplay(keys);
    }

    public static string ToDisplay(Keys keys)
    {
        if (keys == Keys.None) return Loc.IsEnglish ? "(None)" : "(なし)";
        try { return Converter.ConvertToString(keys) ?? keys.ToString(); }
        catch { return keys.ToString(); }
    }

    // セッションから復元。未知IDは無視し、存在しないIDは既定のまま
    public void Load(Dictionary<string, int>? saved)
    {
        if (saved == null) return;
        foreach (var binding in Bindings)
        {
            if (saved.TryGetValue(binding.Id, out var value))
            {
                var keys = (Keys)value;
                if (binding.Id == "tab.next" && keys == (Keys.Control | Keys.Right)) keys = binding.Default;
                if (binding.Id == "tab.prev" && keys == (Keys.Control | Keys.Left)) keys = binding.Default;
                binding.Current = keys;
            }
        }
    }

    // 既定と異なるものだけ保存
    public Dictionary<string, int> Save()
    {
        var dict = new Dictionary<string, int>();
        foreach (var b in Bindings)
        {
            if (b.Current != b.Default) dict[b.Id] = (int)b.Current;
        }
        return dict;
    }

    public void ResetAll()
    {
        foreach (var b in Bindings) b.Current = b.Default;
    }
}
