using System.Windows;
using System.Windows.Controls;

namespace StopCut;

public partial class ConnectionWindow : Window
{
    public ConnectionWindow()
    {
        InitializeComponent();
    }

    private void Log_TextChanged(object sender, TextChangedEventArgs e) => ((TextBox)sender).ScrollToEnd();
}
