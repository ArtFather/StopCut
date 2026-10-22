namespace StopCut.Models;

/// <summary>Everything the app saves to disk.</summary>
public sealed class AppData
{
    public List<Material> Materials { get; set; } = new();
    public List<Leftover> Leftovers { get; set; } = new();

    /// <summary>Recuts and on-the-fly extra parts, newest last.</summary>
    public List<RecutRecord> Recuts { get; set; } = new();
    public SerialSettings Serial { get; set; } = new();

    /// <summary>Units every length in the app is entered and stored in.</summary>
    public LengthUnit Units { get; set; } = LengthUnit.Inches;

    /// <summary>Folder scanned for job CSVs at startup; empty when not set.</summary>
    public string JobsFolder { get; set; } = "";

    /// <summary>Parts shorter than this (mm) are cut with a spacer block between the stop and the material.</summary>
    public double SpacerMinPartMm { get; set; } = 100;

    /// <summary>Length of that spacer block (mm); the stop moves out by this much for those parts.</summary>
    public double SpacerLengthMm { get; set; } = 300;

    /// <summary>Job CSVs already loaded from the jobs folder: file name to "size|last write ticks".</summary>
    public Dictionary<string, string> ImportedJobFiles { get; set; } = new();

}
