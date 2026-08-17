using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using ICSharpCode.AvalonEdit.CodeCompletion;
using Microsoft.CodeAnalysis;

namespace DoodleSharp.Editor;

/// <summary>
/// A popup panel that displays documentation for the currently selected completion item.
/// Floats to the right of the completion window, showing signature, summary, parameters, and return info.
/// Dark-themed to match the existing completion window styling.
/// </summary>
public class DocumentationSidecar
{
    private readonly Popup _popup;
    private readonly Border _border;
    private readonly StackPanel _contentPanel;
    private readonly TextBlock _signatureBlock;
    private readonly TextBlock _summaryBlock;
    private readonly StackPanel _paramsPanel;
    private readonly TextBlock _returnsBlock;
    private CompletionWindow? _trackedWindow;

    private static readonly Brush BackgroundBrush = new SolidColorBrush(Color.FromRgb(30, 30, 30));
    private static readonly Brush BorderBrush = new SolidColorBrush(Color.FromRgb(60, 60, 60));
    private static readonly Brush SignatureColor = new SolidColorBrush(Color.FromRgb(220, 220, 220));
    private static readonly Brush SummaryColor = new SolidColorBrush(Color.FromRgb(180, 180, 180));
    private static readonly Brush ParamNameColor = new SolidColorBrush(Color.FromRgb(156, 220, 254));
    private static readonly Brush ParamDescColor = new SolidColorBrush(Color.FromRgb(160, 160, 160));
    private static readonly Brush LabelColor = new SolidColorBrush(Color.FromRgb(86, 156, 214));

    public DocumentationSidecar()
    {
        _signatureBlock = new TextBlock
        {
            Foreground = SignatureColor,
            FontFamily = new FontFamily("Cascadia Code, Consolas, Courier New"),
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 6)
        };

        _summaryBlock = new TextBlock
        {
            Foreground = SummaryColor,
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 6)
        };

        _paramsPanel = new StackPanel
        {
            Margin = new Thickness(0, 0, 0, 4)
        };

        _returnsBlock = new TextBlock
        {
            Foreground = ParamDescColor,
            FontSize = 11.5,
            TextWrapping = TextWrapping.Wrap
        };

        _contentPanel = new StackPanel
        {
            Margin = new Thickness(8, 6, 8, 6),
            MaxWidth = 350
        };
        _contentPanel.Children.Add(_signatureBlock);
        _contentPanel.Children.Add(_summaryBlock);
        _contentPanel.Children.Add(_paramsPanel);
        _contentPanel.Children.Add(_returnsBlock);

        _border = new Border
        {
            Background = BackgroundBrush,
            BorderBrush = BorderBrush,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(3),
            Child = _contentPanel
        };

        _popup = new Popup
        {
            Child = _border,
            Placement = PlacementMode.Absolute,
            AllowsTransparency = true,
            StaysOpen = false
        };
    }

    /// <summary>
    /// Shows documentation for a completion item. Loads docs from the ISymbol if available.
    /// </summary>
    public void ShowForItem(CompletionData item)
    {
        _paramsPanel.Children.Clear();
        _returnsBlock.Text = "";
        _returnsBlock.Visibility = Visibility.Collapsed;

        var signature = item.Symbol?.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat);
        var hasContent = false;

        // Signature
        if (!string.IsNullOrEmpty(signature))
        {
            _signatureBlock.Text = signature;
            _signatureBlock.Visibility = Visibility.Visible;
            hasContent = true;
        }
        else
        {
            _signatureBlock.Visibility = Visibility.Collapsed;
        }

        // Try to get documentation
        string? summary = null;
        var parameters = new Dictionary<string, string>();
        string? returns = null;

        if (item.Symbol != null)
        {
            // Try XML documentation from the symbol
            var xmlDoc = item.Symbol.GetDocumentationCommentXml();
            if (!string.IsNullOrEmpty(xmlDoc))
            {
                try
                {
                    var doc = new System.Xml.XmlDocument();
                    doc.LoadXml(xmlDoc);
                    summary = CleanXmlText(doc.SelectSingleNode("//summary")?.InnerText);
                    returns = CleanXmlText(doc.SelectSingleNode("//returns")?.InnerText);

                    var paramNodes = doc.SelectNodes("//param");
                    if (paramNodes != null)
                    {
                        foreach (System.Xml.XmlNode param in paramNodes)
                        {
                            var name = param.Attributes?["name"]?.Value;
                            if (!string.IsNullOrEmpty(name))
                                parameters[name] = CleanXmlText(param.InnerText) ?? "";
                        }
                    }
                }
                catch { /* ignore XML parse errors */ }
            }

            // Fallback to built-in documentation
            if (string.IsNullOrEmpty(summary))
            {
                var typeName = item.Symbol.ContainingType?.Name ?? item.Symbol.Name;
                var memberName = item.Symbol.ContainingType != null ? item.Symbol.Name : null;
                summary = XmlDocumentationProvider.GetBuiltInDocumentation(typeName, memberName);

                // If symbol itself is a type, try type-level documentation
                if (string.IsNullOrEmpty(summary) && item.Symbol is INamedTypeSymbol)
                {
                    summary = XmlDocumentationProvider.GetBuiltInDocumentation(item.Symbol.Name);
                }
            }
        }

        // Summary
        if (!string.IsNullOrEmpty(summary))
        {
            _summaryBlock.Text = summary;
            _summaryBlock.Visibility = Visibility.Visible;
            hasContent = true;
        }
        else
        {
            _summaryBlock.Visibility = Visibility.Collapsed;
        }

        // Parameters
        if (parameters.Count > 0)
        {
            var label = new TextBlock
            {
                Text = "Parameters:",
                Foreground = LabelColor,
                FontSize = 11,
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 2, 0, 2)
            };
            _paramsPanel.Children.Add(label);

            foreach (var (name, desc) in parameters)
            {
                var paramPanel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(8, 1, 0, 1) };
                paramPanel.Children.Add(new TextBlock
                {
                    Text = name,
                    Foreground = ParamNameColor,
                    FontSize = 11.5,
                    FontFamily = new FontFamily("Cascadia Code, Consolas, Courier New"),
                    Margin = new Thickness(0, 0, 6, 0)
                });
                if (!string.IsNullOrEmpty(desc))
                {
                    paramPanel.Children.Add(new TextBlock
                    {
                        Text = "— " + desc,
                        Foreground = ParamDescColor,
                        FontSize = 11.5,
                        TextWrapping = TextWrapping.Wrap
                    });
                }
                _paramsPanel.Children.Add(paramPanel);
            }
            hasContent = true;
        }

        // Returns
        if (!string.IsNullOrEmpty(returns))
        {
            _returnsBlock.Text = "Returns: " + returns;
            _returnsBlock.Visibility = Visibility.Visible;
            hasContent = true;
        }

        if (!hasContent)
        {
            Hide();
            return;
        }

        _popup.IsOpen = true;
        UpdatePosition();
    }

    /// <summary>
    /// Tracks a completion window so the sidecar repositions when it moves.
    /// </summary>
    public void TrackCompletionWindow(CompletionWindow window)
    {
        _trackedWindow = window;
        window.LocationChanged += (s, e) => UpdatePosition();
        window.SizeChanged += (s, e) => UpdatePosition();
    }

    /// <summary>
    /// Repositions the popup to the right of the tracked completion window.
    /// </summary>
    public void UpdatePosition()
    {
        if (_trackedWindow == null || !_popup.IsOpen) return;

        try
        {
            var windowLeft = _trackedWindow.Left;
            var windowTop = _trackedWindow.Top;
            var windowWidth = _trackedWindow.ActualWidth;

            // Position to the right of the completion window
            _popup.HorizontalOffset = windowLeft + windowWidth + 2;
            _popup.VerticalOffset = windowTop;

            // Check if it would go off screen to the right
            var screen = SystemParameters.WorkArea;
            _border.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            var desiredWidth = _border.DesiredSize.Width;

            if (_popup.HorizontalOffset + desiredWidth > screen.Right)
            {
                // Place to the left of the completion window instead
                _popup.HorizontalOffset = windowLeft - desiredWidth - 2;
            }
        }
        catch
        {
            // Ignore positioning errors
        }
    }

    public void Hide()
    {
        _popup.IsOpen = false;
    }

    public void Close()
    {
        _popup.IsOpen = false;
        _trackedWindow = null;
    }

    private static string? CleanXmlText(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        // Collapse whitespace
        text = System.Text.RegularExpressions.Regex.Replace(text, @"\s+", " ").Trim();
        // Strip remaining XML tags
        text = System.Text.RegularExpressions.Regex.Replace(text, @"<[^>]+>", "");
        return string.IsNullOrWhiteSpace(text) ? null : text;
    }
}
