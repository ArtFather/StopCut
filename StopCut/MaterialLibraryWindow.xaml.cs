using System.Windows;
using StopCut.ViewModels;

namespace StopCut;

public partial class MaterialLibraryWindow : Window
{
    public MaterialLibraryWindow()
    {
        InitializeComponent();
    }

    private void Window_Closed(object? sender, EventArgs e) => (DataContext as MainViewModel)?.Save();
}
