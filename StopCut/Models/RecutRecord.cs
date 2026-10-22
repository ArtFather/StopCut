namespace StopCut.Models;

/// <summary>A part cut again (bad part) or added on the fly while cutting.</summary>
public sealed class RecutRecord
{
    public DateTime Time { get; set; } = DateTime.Now;
    public string Kind { get; set; } = "";
    public string Job { get; set; } = "";
    public string Material { get; set; } = "";
    public string Label { get; set; } = "";
    public double Length { get; set; }
}
