using StopCut.Models;
using StopCut.ViewModels;

namespace StopCut.Services;

public sealed class PlanResult
{
    public List<PlannedBar> Bars { get; } = new();

    /// <summary>Parts longer than a full stock bar; they can't be planned.</summary>
    public List<CutItem> Unplaced { get; } = new();
}

/// <summary>
/// Assigns the remaining parts of a cut list to bars. Leftovers of the material are used first,
/// then new stock, placing longest parts first into the bar with the least room that still fits.
/// </summary>
public static class CutPlanner
{
    public static PlanResult Plan(Material material, IEnumerable<Leftover> leftovers, Func<CutItem, bool>? include = null)
    {
        var result = new PlanResult();
        var pieces = material.Cuts
            .Where(c => c.Length > 0 && (include?.Invoke(c) ?? true))
            .SelectMany(c => Enumerable.Repeat(c, Math.Max(0, c.Quantity - c.Done)))
            .OrderByDescending(c => c.Length)
            .ToList();

        var bars = leftovers
            .OrderBy(l => l.Length)
            .Select(l => new PlannedBar
            {
                Source = $"Use leftover {l.Tag} ({l.Length:0.###})",
                LeftoverId = l.Id,
                LeftoverTag = l.Tag,
                Material = material,
                StartLength = l.Length,
                Remaining = l.Length - material.TrimCut,
            })
            .ToList();

        var usableStock = material.StockLength - material.TrimCut;
        foreach (var piece in pieces)
        {
            var bar = bars.Where(b => b.Remaining >= piece.Length).MinBy(b => b.Remaining);
            if (bar is null)
            {
                if (piece.Length > usableStock)
                {
                    result.Unplaced.Add(piece);
                    continue;
                }
                bar = new PlannedBar { Material = material, Source = "New stock", StartLength = material.StockLength, Remaining = usableStock };
                bars.Add(bar);
            }
            bar.Cuts.Add(new PlannedCut(bar, piece));
            bar.Remaining = Math.Max(0, bar.Remaining - piece.Length - material.Kerf);
        }

        var number = 1;
        foreach (var bar in bars.Where(b => b.Cuts.Count > 0))
        {
            bar.Number = number++;
            result.Bars.Add(bar);
        }
        return result;
    }
}
