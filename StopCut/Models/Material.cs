using System.Collections.ObjectModel;
using StopCut.Mvvm;

namespace StopCut.Models;

/// <summary>A stock material (profile, board, extrusion...) with its own cut list.</summary>
public sealed class Material : ObservableObject
{
    private string _name = "New material";
    private double _stockLength = 144;
    private double _kerf = 0.125;
    private double _trimCut;
    private double _minLeftover = 12;

    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>ERP material code (e.g. 101-0003), set when the material came from an import.</summary>
    public string Code { get; set; } = "";

    private string _letter = "";

    /// <summary>Letter that starts this material's leftover IDs (A → A1, A2...). Assigned automatically, editable.</summary>
    public string Letter
    {
        get => _letter;
        set => Set(ref _letter, (value ?? "").Trim().ToUpperInvariant());
    }
    public string Name { get => _name; set => Set(ref _name, value); }

    /// <summary>Length of a full new bar/board.</summary>
    public double StockLength { get => _stockLength; set => Set(ref _stockLength, value); }

    /// <summary>Blade width lost on every cut.</summary>
    public double Kerf { get => _kerf; set => Set(ref _kerf, value); }

    /// <summary>Length squared off the end of a bar before the first part.</summary>
    public double TrimCut { get => _trimCut; set => Set(ref _trimCut, value); }

    /// <summary>Remainders at least this long are kept as leftovers; shorter ones are scrap.</summary>
    public double MinLeftover { get => _minLeftover; set => Set(ref _minLeftover, value); }

    public ObservableCollection<CutItem> Cuts { get; set; } = new();
}
