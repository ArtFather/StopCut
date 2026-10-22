using StopCut.Models;
using StopCut.Mvvm;

namespace StopCut.ViewModels;

public enum CutStatus { Pending, Done, Skipped }

/// <summary>One bar (new stock or a leftover) in a cutting plan.</summary>
public sealed class PlannedBar
{
    public int Number { get; set; }
    public string Source { get; set; } = "";
    public Guid? LeftoverId { get; set; }
    /// <summary>The leftover's ID (e.g. "A3") when this stick is a leftover.</summary>
    public string LeftoverTag { get; set; } = "";
    public double StartLength { get; set; }
    public double Remaining { get; set; }
    public bool Finished { get; set; }
    public List<PlannedCut> Cuts { get; } = new();
}

/// <summary>One part to cut from a planned bar.</summary>
public sealed class PlannedCut : ObservableObject
{
    private CutStatus _status;

    public PlannedCut(PlannedBar bar, CutItem item)
    {
        Bar = bar;
        Item = item;
        Length = item.Length;
    }

    public PlannedBar Bar { get; }
    public CutItem Item { get; }
    public double Length { get; }
    public string Label => Item.Label;
    public int BarNumber => Bar.Number;
    public string Source => Bar.Source;
    public CutStatus Status { get => _status; set => Set(ref _status, value); }
}
