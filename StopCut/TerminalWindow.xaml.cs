using System.Windows;
using System.Windows.Controls;

namespace StopCut;

public partial class TerminalWindow : Window
{
    public TerminalWindow()
    {
        InitializeComponent();
    }

    private void Log_TextChanged(object sender, TextChangedEventArgs e) => ((TextBox)sender).ScrollToEnd();
}
