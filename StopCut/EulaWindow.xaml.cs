using System.IO;
using System.Windows;

namespace StopCut;

/// <summary>
/// Shows the license agreement (EULA.md, embedded at build time). On first start, and whenever
/// <see cref="Version"/> changes, it must be accepted before the app opens; from Help it is read-only.
/// </summary>
public partial class EulaWindow : Window
{
    /// <summary>Bump when EULA.md changes in a way users must accept again.</summary>
    public const string Version = "1";

    /// <summary>Records which EULA version was accepted and when.</summary>
    private static readonly string AcceptedPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "StopCut", "eula-accepted.txt");

    public EulaWindow(bool viewOnly)
    {
        InitializeComponent();
        TermsText.Text = LoadTerms();
        if (viewOnly)
        {
            AcceptPanel.Visibility = Visibility.Collapsed;
            CloseButton.Visibility = Visibility.Visible;
        }
    }

    public static bool IsAccepted()
    {
        try
        {
            return File.Exists(AcceptedPath) && File.ReadLines(AcceptedPath).FirstOrDefault() == Version;
        }
        catch
        {
            return false;
        }
    }

    private static string LoadTerms()
    {
        using var stream = typeof(EulaWindow).Assembly.GetManifestResourceStream("StopCut.EULA.md");
        return stream is null ? "License agreement text is missing from this build." : new StreamReader(stream).ReadToEnd();
    }

    private void Accept_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(AcceptedPath)!);
            File.WriteAllText(AcceptedPath, $"{Version}\n{DateTime.Now:yyyy-MM-dd HH:mm:ss}\n{Environment.UserName}\n");
        }
        catch (Exception ex)
        {
            // Still let them in this time; they will be asked again next start.
            MessageBox.Show(this, $"Your acceptance could not be saved ({ex.Message}).", "StopCut",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        DialogResult = true;
    }

    private void Decline_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
