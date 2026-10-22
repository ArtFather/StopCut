namespace StopCut.Models;

public enum LengthUnit { Inches, Millimeters }

public static class Units
{
    public const double MillimetersPerInch = 25.4;

    public static string Label(LengthUnit unit) => unit == LengthUnit.Millimeters ? "mm" : "in";

    /// <summary>Converts a length between units, rounded to a sensible precision for the target unit.</summary>
    public static double Convert(double value, LengthUnit from, LengthUnit to)
    {
        if (from == to) return value;
        return to == LengthUnit.Millimeters
            ? Math.Round(value * MillimetersPerInch, 2)
            : Math.Round(value / MillimetersPerInch, 4);
    }
}
