using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using WpfCanvas = System.Windows.Controls.Canvas;

namespace DoodleSharp;

public partial class PdfExportOptionsWindow : Window
{
    private readonly double _contentWidthUnits;
    private readonly double _contentHeightUnits;

    // Public properties for export settings
    public bool IsAutoSize { get; private set; } = true;
    public double PageWidthMm { get; private set; }
    public double PageHeightMm { get; private set; }
    public double ScaleMmPerUnit { get; private set; } = 1.0;
    public double MarginMm { get; private set; } = 10.0;
    public bool IsLandscape { get; private set; }

    private static readonly (string Name, string Category, double WidthMm, double HeightMm)[] PaperSizes =
    [
        ("Auto (fit to content)", "Auto", 0, 0),
        // ISO A Series
        ("A0  (841 x 1189 mm)", "A", 841, 1189),
        ("A1  (594 x 841 mm)", "A", 594, 841),
        ("A2  (420 x 594 mm)", "A", 420, 594),
        ("A3  (297 x 420 mm)", "A", 297, 420),
        ("A4  (210 x 297 mm)", "A", 210, 297),
        ("A5  (148 x 210 mm)", "A", 148, 210),
        ("A6  (105 x 148 mm)", "A", 105, 148),
        ("A7  (74 x 105 mm)", "A", 74, 105),
        ("A8  (52 x 74 mm)", "A", 52, 74),
        // ISO B Series
        ("B0  (1000 x 1414 mm)", "B", 1000, 1414),
        ("B1  (707 x 1000 mm)", "B", 707, 1000),
        ("B2  (500 x 707 mm)", "B", 500, 707),
        ("B3  (353 x 500 mm)", "B", 353, 500),
        ("B4  (250 x 353 mm)", "B", 250, 353),
        ("B5  (176 x 250 mm)", "B", 176, 250),
        // ISO C Series
        ("C3  (324 x 458 mm)", "C", 324, 458),
        ("C4  (229 x 324 mm)", "C", 229, 324),
        ("C5  (162 x 229 mm)", "C", 162, 229),
        ("C6  (114 x 162 mm)", "C", 114, 162),
        // US Sizes
        ("Letter  (216 x 279 mm)", "US", 216, 279),
        ("Legal  (216 x 356 mm)", "US", 216, 356),
        ("Tabloid  (279 x 432 mm)", "US", 279, 432),
    ];

    public PdfExportOptionsWindow(double contentWidthUnits, double contentHeightUnits)
    {
        InitializeComponent();

        _contentWidthUnits = contentWidthUnits;
        _contentHeightUnits = contentHeightUnits;

        // Populate paper size combo
        foreach (var size in PaperSizes)
        {
            ComboPaperSize.Items.Add(size.Name);
        }
        ComboPaperSize.SelectedIndex = 0; // Auto
    }

    private void ComboPaperSize_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ComboPaperSize.SelectedIndex < 0) return;

        bool isAuto = ComboPaperSize.SelectedIndex == 0;

        // Enable/disable orientation controls
        if (OrientationPanel != null)
        {
            OrientationPanel.IsEnabled = !isAuto;
            OrientationPanel.Opacity = isAuto ? 0.4 : 1.0;
            if (LabelOrientation != null)
            {
                LabelOrientation.Opacity = isAuto ? 0.4 : 1.0;
            }
        }

        UpdatePreview();
    }

    private void Orientation_Changed(object sender, RoutedEventArgs e)
    {
        UpdatePreview();
    }

    private void Setting_Changed(object sender, TextChangedEventArgs e)
    {
        UpdatePreview();
    }

    private void UpdatePreview()
    {
        if (PreviewCanvas == null || TextInfo == null || TextWarning == null) return;

        // Parse current settings
        if (!double.TryParse(TextScale.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out double scale) || scale <= 0)
            scale = 1.0;

        if (!double.TryParse(TextMargin.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out double margin) || margin < 0)
            margin = 10.0;

        bool isAuto = ComboPaperSize.SelectedIndex == 0;
        bool landscape = RadioLandscape.IsChecked == true;

        // Content size in mm
        double contentWMm = _contentWidthUnits * scale;
        double contentHMm = _contentHeightUnits * scale;

        // Page size in mm
        double pageWMm, pageHMm;

        if (isAuto)
        {
            // Auto: page fits content + margins
            pageWMm = contentWMm + 2 * margin;
            pageHMm = contentHMm + 2 * margin;
        }
        else
        {
            var paper = PaperSizes[ComboPaperSize.SelectedIndex];
            if (landscape)
            {
                pageWMm = paper.HeightMm;
                pageHMm = paper.WidthMm;
            }
            else
            {
                pageWMm = paper.WidthMm;
                pageHMm = paper.HeightMm;
            }
        }

        // Printable area
        double printableW = Math.Max(pageWMm - 2 * margin, 0);
        double printableH = Math.Max(pageHMm - 2 * margin, 0);

        // Check overflow
        bool overflows = !isAuto && (contentWMm > printableW || contentHMm > printableH);

        // Render preview
        PreviewCanvas.Children.Clear();

        double canvasW = PreviewCanvas.ActualWidth > 0 ? PreviewCanvas.ActualWidth : 180;
        double canvasH = PreviewCanvas.ActualHeight > 0 ? PreviewCanvas.ActualHeight : 200;

        // Scale to fit preview with padding
        double shadowPx = 3;
        // Paper+shadow must fit within canvas; reserve shadowPx on right/bottom
        double maxPaperW = canvasW - shadowPx;
        double maxPaperH = canvasH - shadowPx;

        if (pageWMm <= 0 || pageHMm <= 0) return;

        double fitScale = Math.Min(maxPaperW / pageWMm, maxPaperH / pageHMm);

        double paperW = pageWMm * fitScale;
        double paperH = pageHMm * fitScale;

        // Center the paper+shadow unit within the canvas
        double totalW = paperW + shadowPx;
        double totalH = paperH + shadowPx;
        double offsetX = (canvasW - totalW) / 2;
        double offsetY = (canvasH - totalH) / 2;

        // 1. Shadow
        var shadow = new Rectangle
        {
            Width = paperW,
            Height = paperH,
            Fill = new SolidColorBrush(Color.FromArgb(80, 0, 0, 0)),
        };
        WpfCanvas.SetLeft(shadow, offsetX + 3);
        WpfCanvas.SetTop(shadow, offsetY + 3);
        PreviewCanvas.Children.Add(shadow);

        // 2. Paper (white)
        var paper_rect = new Rectangle
        {
            Width = paperW,
            Height = paperH,
            Fill = Brushes.White,
            Stroke = new SolidColorBrush(Color.FromRgb(160, 160, 160)),
            StrokeThickness = 1,
        };
        WpfCanvas.SetLeft(paper_rect, offsetX);
        WpfCanvas.SetTop(paper_rect, offsetY);
        PreviewCanvas.Children.Add(paper_rect);

        // 3. Margin boundary (dashed)
        double marginPx = margin * fitScale;
        if (marginPx > 0 && marginPx * 2 < paperW && marginPx * 2 < paperH)
        {
            var marginRect = new Rectangle
            {
                Width = paperW - 2 * marginPx,
                Height = paperH - 2 * marginPx,
                Fill = Brushes.Transparent,
                Stroke = new SolidColorBrush(Color.FromRgb(140, 140, 140)),
                StrokeThickness = 0.8,
                StrokeDashArray = new DoubleCollection { 4, 3 },
            };
            WpfCanvas.SetLeft(marginRect, offsetX + marginPx);
            WpfCanvas.SetTop(marginRect, offsetY + marginPx);
            PreviewCanvas.Children.Add(marginRect);
        }

        // 4. Content rectangle (centered in printable area)
        if (contentWMm > 0 && contentHMm > 0)
        {
            double contentPxW = contentWMm * fitScale;
            double contentPxH = contentHMm * fitScale;

            // Center content in printable area
            double printablePxW = printableW * fitScale;
            double printablePxH = printableH * fitScale;

            double contentX = offsetX + marginPx + (printablePxW - contentPxW) / 2;
            double contentY = offsetY + marginPx + (printablePxH - contentPxH) / 2;

            var contentColor = overflows
                ? Color.FromRgb(220, 80, 80)
                : Color.FromRgb(80, 140, 220);

            var contentRect = new Rectangle
            {
                Width = Math.Max(contentPxW, 1),
                Height = Math.Max(contentPxH, 1),
                Fill = new SolidColorBrush(Color.FromArgb(40, contentColor.R, contentColor.G, contentColor.B)),
                Stroke = new SolidColorBrush(contentColor),
                StrokeThickness = 1.2,
            };
            WpfCanvas.SetLeft(contentRect, contentX);
            WpfCanvas.SetTop(contentRect, contentY);
            PreviewCanvas.Children.Add(contentRect);
        }

        // Info text
        TextInfo.Text = $"Paper: {pageWMm:F0} x {pageHMm:F0} mm\n"
                      + $"Content: {contentWMm:F1} x {contentHMm:F1} mm";

        if (overflows)
        {
            TextWarning.Text = "Content exceeds printable area";
            TextWarning.Visibility = Visibility.Visible;
        }
        else
        {
            TextWarning.Visibility = Visibility.Collapsed;
        }
    }

    private void Export_Click(object sender, RoutedEventArgs e)
    {
        // Parse and validate scale
        if (!double.TryParse(TextScale.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out double scale) || scale <= 0)
        {
            MessageBox.Show("Please enter a valid positive number for scale.",
                "Invalid Scale", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        // Parse and validate margin
        if (!double.TryParse(TextMargin.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out double margin) || margin < 0)
        {
            MessageBox.Show("Please enter a valid non-negative number for margin.",
                "Invalid Margin", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        ScaleMmPerUnit = scale;
        MarginMm = margin;
        IsAutoSize = ComboPaperSize.SelectedIndex == 0;
        IsLandscape = RadioLandscape.IsChecked == true;

        if (IsAutoSize)
        {
            PageWidthMm = 0;
            PageHeightMm = 0;
        }
        else
        {
            var paper = PaperSizes[ComboPaperSize.SelectedIndex];
            if (IsLandscape)
            {
                PageWidthMm = paper.HeightMm;
                PageHeightMm = paper.WidthMm;
            }
            else
            {
                PageWidthMm = paper.WidthMm;
                PageHeightMm = paper.HeightMm;
            }
        }

        DialogResult = true;
        Close();
    }
}
