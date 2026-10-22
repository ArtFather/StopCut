using StopCut.Mvvm;

namespace StopCut.Models;

/// <summary>One line of a material's cut list: a part length and how many are needed.</summary>
public sealed class CutItem : ObservableObject
{
    private string _label = "";
    private double _length;
    private int _quantity = 1;
    private int _done;
    private string _job = "";

    /// <summary>Job this cut belongs to (e.g. "12345 EXAMPLE"); empty when not part of a job.</summary>
    public string Job { get => _job; set => Set(ref _job, value); }

    public string Label { get => _label; set => Set(ref _label, value); }
    public double Length { get => _length; set => Set(ref _length, value); }
    public int Quantity { get => _quantity; set => Set(ref _quantity, value); }

    /// <summary>How many of this part have been cut so far.</summary>
    public int Done { get => _done; set => Set(ref _done, value); }
}
