using System.ComponentModel;
using System.Windows;
using StopCut.Models;
using StopCut.ViewModels;

namespace StopCut;

public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel = new();
    private ConnectionWindow? _connectionWindow;
    private TerminalWindow? _terminalWindow;
    private MaterialLibraryWindow? _libraryWindow;

    public MainWindow()
    {
        InitializeComponent();
        DataContext = _viewModel;
        Loaded += (_, _) => _viewModel.ScanJobsFolder(false);
    }

    private void JobsFolder_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog { Title = "Folder with job CSV files" };
        if (System.IO.Directory.Exists(_viewModel.JobsFolder)) dialog.InitialDirectory = _viewModel.JobsFolder;
        if (dialog.ShowDialog(this) == true) _viewModel.JobsFolder = dialog.FolderName;
    }

    private void Window_Closing(object? sender, CancelEventArgs e) => _viewModel.Dispose();

    private void Settings_SubmenuOpened(object sender, RoutedEventArgs e)
    {
        InchesItem.IsChecked = _viewModel.Units == LengthUnit.Inches;
        MillimetersItem.IsChecked = _viewModel.Units == LengthUnit.Millimeters;
    }

    private void Inches_Click(object sender, RoutedEventArgs e) => _viewModel.Units = LengthUnit.Inches;

    private void Millimeters_Click(object sender, RoutedEventArgs e) => _viewModel.Units = LengthUnit.Millimeters;

    private void Connection_Click(object sender, RoutedEventArgs e) =>
        ShowTool(ref _connectionWindow, () => new ConnectionWindow());

    private void Library_Click(object sender, RoutedEventArgs e) =>
        ShowTool(ref _libraryWindow, () => new MaterialLibraryWindow());

    private void Terminal_Click(object sender, RoutedEventArgs e) =>
        ShowTool(ref _terminalWindow, () => new TerminalWindow());

    private void Eula_Click(object sender, RoutedEventArgs e) =>
        new EulaWindow(viewOnly: true) { Owner = this, WindowStartupLocation = WindowStartupLocation.CenterOwner }.ShowDialog();

    private void About_Click(object sender, RoutedEventArgs e) =>
        MessageBox.Show(this,
            "StopCut - cut manager for TigerStop saw stops\n\n" +
            "StopCut is an independent product and is not affiliated with, sponsored, or endorsed by TigerStop, LLC.\n\n" +
            "Provided \"as is\", without warranty of any kind (MIT License; see Help > License agreement). This app moves machinery: keep the " +
            "saw's guards, interlocks and hardwired emergency stops in place and never rely on it for safety.",
            "About StopCut", MessageBoxButton.OK, MessageBoxImage.Information);

    /// <summary>Opens a settings window sharing this window's data, or brings it forward if already open.</summary>
    private void ShowTool<T>(ref T? window, Func<T> create) where T : Window
    {
        if (window is { IsLoaded: true })
        {
            window.Activate();
            return;
        }
        window = create();
        window.Owner = this;
        window.DataContext = _viewModel;
        window.Show();
    }
}
