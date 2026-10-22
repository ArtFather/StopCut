namespace StopCut.Models;

/// <summary>Everything the app saves to disk.</summary>
public sealed class AppData
{
    public List<Material> Materials { get; set; } = new();
    public List<Leftover> Leftovers { get; set; } = new();
    public SerialSettings Serial { get; set; } = new();

    /// <summary>Units every length in the app is entered and stored in.</summary>
    public LengthUnit Units { get; set; } = LengthUnit.Inches;

    /// <summary>Folder scanned for job CSVs at startup; empty when not set.</summary>
    public string JobsFolder { get; set; } = "";

    /// <summary>Job CSVs already loaded from the jobs folder: file name to "size|last write ticks".</summary>
    public Dictionary<string, string> ImportedJobFiles { get; set; } = new();

}
