using System.Windows;
using System.Windows.Media;

namespace DoodleSharp;

public partial class ExportOptionsWindow : Window
{
    public Brush SelectedBackground { get; private set; } = Brushes.Transparent;
    public bool IncludeGrid { get; private set; } = true;
    public bool UseCustomSize { get; private set; }
    public int CustomWidth { get; private set; }
    public int CustomHeight { get; private set; }

    public ExportOptionsWindow()
    {
        InitializeComponent();
    }
    
    public void SetDefault(string preference)
    {
        switch (preference)
        {
            case "Transparent": RadioTransparent.IsChecked = true; break;
            case "Canvas Background": RadioCanvas.IsChecked = true; break;
            case "White": RadioWhite.IsChecked = true; break;
            case "Black": RadioBlack.IsChecked = true; break;
            case "Light (White Smoke)": 
            case "Light": RadioLight.IsChecked = true; break;
        }
    }
    
    public void SetGridDefault(bool include)
    {
        CheckShowGrid.IsChecked = include;
    }

    private void RadioCustomSize_Changed(object sender, RoutedEventArgs e)
    {
        if (CustomSizePanel == null) return;
        bool isCustom = RadioCustomSize.IsChecked == true;
        CustomSizePanel.IsEnabled = isCustom;
        CustomSizePanel.Opacity = isCustom ? 1.0 : 0.5;
    }

    private void Export_Click(object sender, RoutedEventArgs e)
    {
        if (RadioTransparent.IsChecked == true)
            SelectedBackground = Brushes.Transparent;
        else if (RadioCanvas.IsChecked == true)
            SelectedBackground = null; // Signal to use current canvas brush
        else if (RadioWhite.IsChecked == true)
            SelectedBackground = Brushes.White;
        else if (RadioBlack.IsChecked == true)
            SelectedBackground = Brushes.Black;
        else if (RadioLight.IsChecked == true)
            SelectedBackground = Brushes.WhiteSmoke;

        IncludeGrid = CheckShowGrid.IsChecked == true;

        UseCustomSize = RadioCustomSize.IsChecked == true;
        if (UseCustomSize)
        {
            if (!int.TryParse(TextWidth.Text, out int w) || w <= 0 ||
                !int.TryParse(TextHeight.Text, out int h) || h <= 0)
            {
                MessageBox.Show("Please enter valid positive integers for width and height.",
                    "Invalid Size", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            CustomWidth = w;
            CustomHeight = h;
        }

        DialogResult = true;
        Close();
    }
}
