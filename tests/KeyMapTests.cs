using System.Windows.Forms;
using MultiImageCanvas;
using Xunit;

namespace MultiImageCanvas.Tests;

public class KeyMapTests
{
    [Fact]
    public void Defaults_ResolveActions()
    {
        var map = new KeyMap();
        Assert.True(map.TryGetAction(Keys.Control | Keys.S, out var id));
        Assert.Equal("file.save", id);
        Assert.True(map.TryGetAction(Keys.Control | Keys.Z, out id));
        Assert.Equal("edit.undo", id);
        Assert.True(map.TryGetAction(Keys.F11, out id));
        Assert.Equal("view.hideUi", id);
        Assert.False(map.TryGetAction(Keys.Control | Keys.F12, out _));
    }

    [Fact]
    public void SaveLoad_RoundTripsOnlyChanges()
    {
        var map = new KeyMap();
        var undo = map.Bindings.First(b => b.Id == "edit.undo");
        undo.Current = Keys.Control | Keys.Alt | Keys.Z;

        var saved = map.Save();
        Assert.Single(saved); // 変更した1件のみ保存される
        Assert.Equal((int)(Keys.Control | Keys.Alt | Keys.Z), saved["edit.undo"]);

        var restored = new KeyMap();
        restored.Load(saved);
        Assert.Equal(Keys.Control | Keys.Alt | Keys.Z, restored.Get("edit.undo"));
        Assert.Equal(Keys.Control | Keys.S, restored.Get("file.save")); // 未変更は既定のまま
    }

    [Fact]
    public void Load_IgnoresUnknownIds()
    {
        var map = new KeyMap();
        map.Load(new Dictionary<string, int> { ["nonexistent.action"] = (int)Keys.F5 });
        Assert.False(map.TryGetAction(Keys.F5, out _));
    }

    [Fact]
    public void UnassignedKey_DoesNotMatchNone()
    {
        var map = new KeyMap();
        var b = map.Bindings.First(x => x.Id == "edit.delete");
        b.Current = Keys.None; // 割り当て解除
        Assert.False(map.TryGetAction(Keys.None, out _));
        Assert.False(map.TryGetAction(Keys.Delete, out _));
    }

    [Fact]
    public void FindByKeys_DetectsConflict()
    {
        var map = new KeyMap();
        var hit = map.FindByKeys(Keys.Control | Keys.D);
        Assert.NotNull(hit);
        Assert.Equal("edit.duplicate", hit!.Id);
        Assert.Null(map.FindByKeys(Keys.None));
    }

    [Fact]
    public void ResetAll_RestoresDefaults()
    {
        var map = new KeyMap();
        foreach (var b in map.Bindings) b.Current = Keys.None;
        map.ResetAll();
        Assert.All(map.Bindings, b => Assert.Equal(b.Default, b.Current));
    }
}
