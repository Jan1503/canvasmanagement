namespace CanvasManagement.Tests;

public class CanvasZOrderTests
{
    [Fact]
    public void GetCanvas_keeps_insertion_z_order()
    {
        using var manager = new CanvasManager(32, 16);
        var back = manager.GetCanvas(0, "back");
        var mid = manager.GetCanvas(1, "mid");
        var front = manager.GetCanvas(2, "front");

        Assert.Equal(["back", "mid", "front"], NamesByZ(manager));
        Assert.Equal(0, back.ZOrder);
        Assert.Equal(1, mid.ZOrder);
        Assert.Equal(2, front.ZOrder);
    }

    [Fact]
    public void BringToFront_places_canvas_above_all_others()
    {
        using var manager = new CanvasManager(32, 16);
        var a = manager.GetCanvas(0, "A");
        manager.GetCanvas(1, "B");
        manager.GetCanvas(2, "C");

        manager.BringToFront(a);

        Assert.Equal(["B", "C", "A"], NamesByZ(manager));
        Assert.True(a.ZOrder > manager.GetCanvasByName("C")!.ZOrder);
    }

    [Fact]
    public void SendToBack_places_canvas_below_all_others()
    {
        using var manager = new CanvasManager(32, 16);
        manager.GetCanvas(0, "A");
        manager.GetCanvas(1, "B");
        var c = manager.GetCanvas(2, "C");

        manager.SendToBack(c);

        Assert.Equal(["C", "A", "B"], NamesByZ(manager));
        Assert.True(c.ZOrder < manager.GetCanvasByName("A")!.ZOrder);
    }

    [Fact]
    public void MoveUp_swaps_with_next_higher_layer()
    {
        using var manager = new CanvasManager(32, 16);
        var a = manager.GetCanvas(0, "A");
        manager.GetCanvas(1, "B");
        manager.GetCanvas(2, "C");

        manager.MoveUp(a);

        Assert.Equal(["B", "A", "C"], NamesByZ(manager));
    }

    [Fact]
    public void MoveDown_swaps_with_next_lower_layer()
    {
        using var manager = new CanvasManager(32, 16);
        manager.GetCanvas(0, "A");
        manager.GetCanvas(1, "B");
        var c = manager.GetCanvas(2, "C");

        manager.MoveDown(c);

        Assert.Equal(["A", "C", "B"], NamesByZ(manager));
    }

    [Fact]
    public void MoveUp_on_front_and_MoveDown_on_back_are_no_ops()
    {
        using var manager = new CanvasManager(32, 16);
        var back = manager.GetCanvas(0, "back");
        manager.GetCanvas(1, "mid");
        var front = manager.GetCanvas(2, "front");

        manager.MoveDown(back);
        manager.MoveUp(front);

        Assert.Equal(["back", "mid", "front"], NamesByZ(manager));
    }

    private static string[] NamesByZ(CanvasManager manager) =>
        manager.GetCanvasesByZOrder().Select(c => c.Name).ToArray();
}
