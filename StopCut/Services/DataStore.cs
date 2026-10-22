using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using StopCut.Models;

namespace StopCut.Services;

/// <summary>Loads and saves materials, cut lists, leftovers and settings as JSON in %AppData%\StopCut.</summary>
public sealed class DataStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public string FilePath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "StopCut", "data.json");

    /// <summary>Where versions before the rename to StopCut saved their data.</summary>
    private static readonly string OldFilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "TigerStop", "data.json");

    /// <summary>Set when the saved file couldn't be read and was moved aside; shown to the user once.</summary>
    public string? LoadProblem { get; private set; }

    public AppData Load()
    {
        if (!File.Exists(FilePath) && File.Exists(OldFilePath))
        {
            // First start after the rename: bring the old data over (the old file is left in place).
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
                File.Copy(OldFilePath, FilePath);
            }
            catch { /* best effort; falls through to a fresh start */ }
        }
        if (!File.Exists(FilePath)) return new AppData();
        AppData? data;
        try
        {
            data = JsonSerializer.Deserialize<AppData>(File.ReadAllText(FilePath), Options);
        }
        catch (Exception ex)
        {
            // Keep the unreadable file so nothing is lost, and start fresh.
            var backup = FilePath + $".unreadable-{DateTime.Now:yyyyMMdd-HHmmss}";
            try { File.Copy(FilePath, backup, overwrite: true); } catch { /* best effort */ }
            LoadProblem = $"Saved data could not be read ({ex.Message}).\nA copy was kept at:\n{backup}";
            return new AppData();
        }
        return Normalize(data ?? new AppData());
    }

    /// <summary>Fills gaps left by files saved by older versions (missing lists, null text).</summary>
    private static AppData Normalize(AppData data)
    {
        data.Materials ??= new();
        data.Leftovers ??= new();
        data.Recuts ??= new();
        data.Recuts.RemoveAll(r => r is null);
        data.Serial ??= new();
        data.JobsFolder ??= "";
        data.ImportedJobFiles = data.ImportedJobFiles is null
            ? new(StringComparer.OrdinalIgnoreCase)
            : new(data.ImportedJobFiles, StringComparer.OrdinalIgnoreCase);
        data.Materials.RemoveAll(m => m is null);
        data.Leftovers.RemoveAll(l => l is null);
        foreach (var m in data.Materials)
        {
            m.Name ??= "";
            m.Code ??= "";
            m.Letter ??= "";
            m.Cuts ??= new();
            foreach (var c in m.Cuts.Where(c => c is null).ToList()) m.Cuts.Remove(c);
            foreach (var c in m.Cuts)
            {
                c.Job ??= "";
                c.Label ??= "";
            }
        }
        foreach (var l in data.Leftovers)
        {
            l.MaterialName ??= "";
            l.Note ??= "";
            l.Tag ??= "";
        }
        data.Serial.PortName ??= "COM1";
        data.Serial.MoveCommandTemplate ??= SerialSettings.DefaultMoveCommand;
        data.Serial.Terminator ??= SerialSettings.DefaultTerminator;
        return data;
    }

    public void Save(AppData data)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        var tmp = FilePath + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(data, Options));
        File.Move(tmp, FilePath, overwrite: true);
    }
}
