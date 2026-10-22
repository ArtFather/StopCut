using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace StopCut.Services;

/// <summary>One row of an imported parts list. Lengths are in millimeters.</summary>
public sealed record ImportedPart(
    int LineNumber,
    string PartNumber,
    string Description,
    double LengthMm,
    int Quantity,
    string MaterialCode,
    string MaterialDescription)
{
    public string Label => $"{PartNumber} {Description}".Trim();
    public string MaterialName => $"{MaterialCode} {MaterialDescription}".Trim();

    /// <summary>Stock length read from a description like "SPR LBR 19x38x2438mm", if present.</summary>
    public double? StockLengthMm
    {
        get
        {
            var m = Regex.Match(MaterialDescription, @"(\d+(?:\.\d+)?)\s*x\s*(\d+(?:\.\d+)?)\s*x\s*(\d+(?:\.\d+)?)\s*mm", RegexOptions.IgnoreCase);
            return m.Success ? double.Parse(m.Groups[3].Value, CultureInfo.InvariantCulture) : null;
        }
    }
}

public sealed class PartsListImport
{
    public List<ImportedPart> Parts { get; } = new();
    public List<string> Problems { get; } = new();
}

/// <summary>
/// Reads the ERP parts list CSV (no header):
/// line no, part no, rev, description, "L X W X T" (mm), material code, rev, material description,
/// "Stock", work order, (blank), quantity.
/// </summary>
public static class PartsListImporter
{
    public static PartsListImport Read(string path)
    {
        var result = new PartsListImport();
        var lineNumber = 0;
        foreach (var line in File.ReadLines(path))
        {
            lineNumber++;
            if (string.IsNullOrWhiteSpace(line)) continue;
            var f = SplitCsv(line);
            if (f.Count < 12)
            {
                result.Problems.Add($"Line {lineNumber}: expected 12 columns, found {f.Count}");
                continue;
            }

            var dims = Regex.Split(f[4].Trim(), @"\s*[xX]\s*");
            if (!double.TryParse(dims[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var length) || length <= 0)
            {
                result.Problems.Add($"Line {lineNumber}: can't read a length from \"{f[4]}\"");
                continue;
            }
            if (!double.TryParse(f[11].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var qty) || qty <= 0)
            {
                result.Problems.Add($"Line {lineNumber}: can't read a quantity from \"{f[11]}\"");
                continue;
            }

            result.Parts.Add(new ImportedPart(
                lineNumber,
                f[1].Trim(),
                f[3].Trim(),
                length,
                (int)Math.Round(qty, MidpointRounding.AwayFromZero),
                f[5].Trim(),
                f[7].Trim()));
        }
        return result;
    }

    /// <summary>Splits one CSV line, honoring quoted fields and "" escapes.</summary>
    public static List<string> SplitCsv(string line)
    {
        var fields = new List<string>();
        var sb = new StringBuilder();
        var quoted = false;
        for (var i = 0; i < line.Length; i++)
        {
            var c = line[i];
            if (quoted)
            {
                if (c == '"' && i + 1 < line.Length && line[i + 1] == '"') { sb.Append('"'); i++; }
                else if (c == '"') quoted = false;
                else sb.Append(c);
            }
            else if (c == '"') quoted = true;
            else if (c == ',') { fields.Add(sb.ToString()); sb.Clear(); }
            else sb.Append(c);
        }
        fields.Add(sb.ToString());
        return fields;
    }
}
