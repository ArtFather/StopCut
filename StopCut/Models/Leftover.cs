using StopCut.Mvvm;

namespace StopCut.Models;

/// <summary>A usable offcut kept in stock for a material.</summary>
public sealed class Leftover : ObservableObject
{
    private double _length;
    private string _note = "";

    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Short ID written on the physical piece, e.g. "A12".</summary>
    public string Tag { get; set; } = "";
    public Guid MaterialId { get; set; }
    public string MaterialName { get; set; } = "";
    public double Length { get => _length; set => Set(ref _length, value); }
    public DateTime Created { get; set; } = DateTime.Now;
    public string Note { get => _note; set => Set(ref _note, value); }
}
