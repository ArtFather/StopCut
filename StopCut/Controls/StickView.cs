using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace StopCut.Controls;

public enum PieceState { Pending, Current, Done, Skipped }

/// <summary>What to draw: one stick with its trim, planned pieces, kerfs and remainder.</summary>
public sealed record StickDrawing(
    string Title,
    double StartLength,
    double Trim,
    double Kerf,
    double MinLeftover,
    IReadOnlyList<(double Length, PieceState State)> Pieces,
    string UnitLabel);

/// <summary>Draws a stick to scale, split into its cuts, highlighting the piece being cut now.</summary>
public sealed class StickView : FrameworkElement
{
    public static readonly DependencyProperty StickProperty = DependencyProperty.Register(
        nameof(Stick), typeof(StickDrawing), typeof(StickView),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public StickDrawing? Stick
    {
        get => (StickDrawing?)GetValue(StickProperty);
        set => SetValue(StickProperty, value);
    }

    private static readonly Brush PendingFill = Freeze(new SolidColorBrush(Color.FromRgb(0xB8, 0xD4, 0xEE)));
    private static readonly Brush CurrentFill = Freeze(new SolidColorBrush(Color.FromRgb(0xFF, 0x9F, 0x1C)));
    private static readonly Brush DoneFill = Freeze(new SolidColorBrush(Color.FromRgb(0xD0, 0xD0, 0xD0)));
    private static readonly Brush SkippedFill = Brushes.White;
    private static readonly Brush KerfFill = Brushes.Black;
    private static readonly Brush TrimFill = Freeze(new SolidColorBrush(Color.FromRgb(0x80, 0x80, 0x80)));
    private static readonly Brush LeftoverFill = Freeze(new SolidColorBrush(Color.FromRgb(0x9C, 0xD9, 0x8F)));
    private static readonly Brush ScrapFill = Freeze(new SolidColorBrush(Color.FromRgb(0xEE, 0xEE, 0xEE)));
    private static readonly Pen Outline = Freeze(new Pen(Brushes.DimGray, 1));
    private static readonly Pen CurrentOutline = Freeze(new Pen(Brushes.Red, 3));

    private static T Freeze<T>(T f) where T : Freezable { f.Freeze(); return f; }

    protected override Size MeasureOverride(Size availableSize) =>
        new(double.IsInfinity(availableSize.Width) ? 400 : availableSize.Width, 70);

    protected override void OnRender(DrawingContext dc)
    {
        var s = Stick;
        var w = ActualWidth;
        var h = ActualHeight;
        if (s is null || s.StartLength <= 0 || w <= 0)
        {
            DrawText(dc, "No stick planned yet", new Point(4, h / 2 - 9), 14, Brushes.Gray, w - 8);
            return;
        }

        var scale = w / s.StartLength;
        double x = 0;
        Rect Segment(double length) { var r = new Rect(x, 0, Math.Max(0, length * scale), h); x += r.Width; return r; }

        if (s.Trim > 0) dc.DrawRectangle(TrimFill, null, Segment(s.Trim));

        Rect? current = null;
        foreach (var (length, state) in s.Pieces)
        {
            var r = Segment(length);
            var fill = state switch
            {
                PieceState.Current => CurrentFill,
                PieceState.Done => DoneFill,
                PieceState.Skipped => SkippedFill,
                _ => PendingFill,
            };
            dc.DrawRectangle(fill, Outline, r);
            var text = state == PieceState.Done ? $"✓ {length:0.###}" : $"{length:0.###}";
            DrawText(dc, text, new Point(r.X + 4, r.Y + h / 2 - 10), state == PieceState.Current ? 18 : 14,
                state == PieceState.Done ? Brushes.DimGray : Brushes.Black, r.Width - 8);
            if (state == PieceState.Current) current = r;
            if (s.Kerf > 0) dc.DrawRectangle(KerfFill, null, Segment(s.Kerf));
        }

        var remaining = s.StartLength - x / scale;
        if (remaining > 0.0005)
        {
            var r = new Rect(x, 0, Math.Max(0, w - x), h);
            var keep = remaining >= s.MinLeftover;
            dc.DrawRectangle(keep ? LeftoverFill : ScrapFill, Outline, r);
            DrawText(dc, $"{(keep ? "leftover" : "scrap")} {remaining:0.###}", new Point(r.X + 4, r.Y + h / 2 - 9), 13,
                Brushes.Black, r.Width - 8);
        }

        if (current is { } c) dc.DrawRectangle(null, CurrentOutline, new Rect(c.X + 1.5, 1.5, Math.Max(0, c.Width - 3), h - 3));
    }

    private void DrawText(DrawingContext dc, string text, Point at, double size, Brush brush, double maxWidth)
    {
        // FormattedText throws for very large (or infinite) widths, which crashed the app at startup.
        if (double.IsNaN(maxWidth) || maxWidth < 12) return;
        maxWidth = Math.Min(maxWidth, 10000);
        var ft = new FormattedText(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
            new Typeface("Segoe UI"), size, brush, VisualTreeHelper.GetDpi(this).PixelsPerDip)
        {
            MaxTextWidth = maxWidth,
            MaxLineCount = 1,
            Trimming = TextTrimming.CharacterEllipsis,
        };
        dc.DrawText(ft, at);
    }
}
