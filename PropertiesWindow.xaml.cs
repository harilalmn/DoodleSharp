using System.ComponentModel;
using System.Windows;

namespace DoodleSharp;

public partial class PropertiesWindow : Window
{
    public PropertiesWindow()
    {
        InitializeComponent();
    }

    private void Window_Closing(object sender, CancelEventArgs e)
    {
        // Hide instead of closing so we can reuse the window
        e.Cancel = true;
        Hide();
    }
}
