namespace MultiImageCanvas;

// コマンドパターンによるUndo/Redo基盤
internal interface IUndoCommand
{
    string Label { get; }
    void Undo();
    void Redo();
}

internal sealed class UndoStack
{
    private readonly List<IUndoCommand> _undo = [];
    private readonly List<IUndoCommand> _redo = [];
    private const int Limit = 200;

    public event EventHandler? StateChanged;

    public bool CanUndo => _undo.Count > 0;
    public bool CanRedo => _redo.Count > 0;

    public string? NextUndoLabel => _undo.Count > 0 ? _undo[^1].Label : null;
    public string? NextRedoLabel => _redo.Count > 0 ? _redo[^1].Label : null;

    // 新しい操作を記録。Redo履歴は破棄される
    public void Push(IUndoCommand command)
    {
        _undo.Add(command);
        if (_undo.Count > Limit) _undo.RemoveAt(0);
        _redo.Clear();
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Undo()
    {
        if (_undo.Count == 0) return;
        var cmd = _undo[^1];
        _undo.RemoveAt(_undo.Count - 1);
        cmd.Undo();
        _redo.Add(cmd);
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Redo()
    {
        if (_redo.Count == 0) return;
        var cmd = _redo[^1];
        _redo.RemoveAt(_redo.Count - 1);
        cmd.Redo();
        _undo.Add(cmd);
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Clear()
    {
        _undo.Clear();
        _redo.Clear();
        StateChanged?.Invoke(this, EventArgs.Empty);
    }
}

// 画像の追加
internal sealed class AddItemsCommand(CanvasDocument doc, IReadOnlyList<CanvasItem> items) : IUndoCommand
{
    public string Label => "画像の追加";

    public void Undo()
    {
        foreach (var item in items) doc.Items.Remove(item);
        doc.NotifyChanged();
    }

    public void Redo()
    {
        foreach (var item in items)
        {
            if (!doc.Items.Contains(item)) doc.Items.Add(item);
        }
        doc.NotifyChanged();
    }
}

// 画像の削除（元のZ順位置に戻せるようインデックスを保持）
internal sealed class RemoveItemsCommand : IUndoCommand
{
    private readonly CanvasDocument _doc;
    private readonly List<(CanvasItem Item, int Index)> _removed;

    public string Label { get; }

    public RemoveItemsCommand(CanvasDocument doc, IReadOnlyList<CanvasItem> items, string label = "画像の削除")
    {
        _doc = doc;
        Label = label;
        _removed = items
            .Select(i => (Item: i, Index: doc.Items.IndexOf(i)))
            .Where(t => t.Index >= 0)
            .OrderBy(t => t.Index)
            .ToList();
    }

    public void Undo()
    {
        foreach (var (item, index) in _removed)
        {
            _doc.Items.Insert(Math.Min(index, _doc.Items.Count), item);
        }
        _doc.NotifyChanged();
    }

    public void Redo()
    {
        foreach (var (item, _) in _removed) _doc.Items.Remove(item);
        _doc.NotifyChanged();
    }
}

// 移動・リサイズ・クロップ・回転・反転・不透明度・表示切替 (状態スナップショットの差し替え)
internal sealed class TransformCommand(CanvasDocument doc, CanvasItem item, ItemState before, ItemState after, string label) : IUndoCommand
{
    public string Label => label;

    public void Undo()
    {
        item.Restore(before);
        doc.NotifyChanged();
    }

    public void Redo()
    {
        item.Restore(after);
        doc.NotifyChanged();
    }
}

// Z順変更
internal sealed class ReorderCommand(CanvasDocument doc, CanvasItem item, int fromIndex, int toIndex) : IUndoCommand
{
    public string Label => "重ね順の変更";

    public void Undo() => Move(toIndex, fromIndex);
    public void Redo() => Move(fromIndex, toIndex);

    private void Move(int from, int to)
    {
        if (from < 0 || from >= doc.Items.Count) return;
        if (doc.Items[from] != item)
        {
            from = doc.Items.IndexOf(item);
            if (from < 0) return;
        }
        doc.Items.RemoveAt(from);
        doc.Items.Insert(Math.Clamp(to, 0, doc.Items.Count), item);
        doc.NotifyChanged();
    }
}

internal sealed class AddStrokeCommand(CanvasDocument doc, PaintStroke stroke) : IUndoCommand
{
    public string Label => "描画";

    public void Undo()
    {
        doc.Strokes.Remove(stroke);
        doc.NotifyChanged();
    }

    public void Redo()
    {
        if (!doc.Strokes.Contains(stroke)) doc.Strokes.Add(stroke);
        doc.NotifyChanged();
    }
}

internal sealed class ClearStrokesCommand : IUndoCommand
{
    private readonly CanvasDocument _doc;
    private readonly List<(PaintStroke Stroke, int Index)> _strokes;

    public string Label { get; }

    public ClearStrokesCommand(CanvasDocument doc, IReadOnlyList<PaintStroke> strokes, string label = "描画の一括削除")
    {
        _doc = doc;
        Label = label;
        _strokes = strokes
            .Select(s => (Stroke: s, Index: doc.Strokes.IndexOf(s)))
            .Where(t => t.Index >= 0)
            .OrderBy(t => t.Index)
            .ToList();
    }

    public ClearStrokesCommand(CanvasDocument doc, IReadOnlyList<(PaintStroke Stroke, int Index)> strokes, string label)
    {
        _doc = doc;
        Label = label;
        _strokes = strokes
            .Where(t => t.Index >= 0)
            .OrderBy(t => t.Index)
            .ToList();
    }

    public void Undo()
    {
        foreach (var (stroke, index) in _strokes)
        {
            if (!_doc.Strokes.Contains(stroke)) _doc.Strokes.Insert(Math.Min(index, _doc.Strokes.Count), stroke);
        }
        _doc.NotifyChanged();
    }

    public void Redo()
    {
        foreach (var (stroke, _) in _strokes) _doc.Strokes.Remove(stroke);
        _doc.NotifyChanged();
    }
}

internal sealed class StrokeListCommand(CanvasDocument doc, IReadOnlyList<PaintStroke> before, IReadOnlyList<PaintStroke> after, string label) : IUndoCommand
{
    private readonly List<PaintStroke> _before = before.ToList();
    private readonly List<PaintStroke> _after = after.ToList();

    public string Label => label;

    public void Undo()
    {
        doc.Strokes.Clear();
        doc.Strokes.AddRange(_before);
        doc.NotifyChanged();
    }

    public void Redo()
    {
        doc.Strokes.Clear();
        doc.Strokes.AddRange(_after);
        doc.NotifyChanged();
    }
}
