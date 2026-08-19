using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace DoodleSharp.Canvas;

/// <summary>
/// One leaf of the viewport grid: a <see cref="RenderCanvas"/> plus the small navigation overlay
/// that appears while the pointer is over it.
///
/// <para>
/// The overlay is <b>hover-revealed in every mode</b>. It used to appear only while user code owned
/// the mouse — the canvas stops zooming on the wheel then, so there had to be another way to
/// navigate — and that guarantee still holds, because hovering is a superset of that case. With
/// several cells on screen an always-visible panel per cell would be noise, and a revealed one also
/// answers "which cell am I pointing at".
/// </para>
/// </summary>
internal sealed class ViewportCell : Border
{
    private readonly Grid _stack = new();
    private readonly Border _nav;
    private readonly TextBlock _zoomText;

    internal ViewportCell(RenderCanvas canvas)
    {
        Canvas = canvas;

        _zoomText = new TextBlock
        {
            MinWidth = 46,
            Margin = new Thickness(4, 0, 8, 0),
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = Brushes.LightGray,
            Text = "100%",
            ToolTip = "Current zoom level",
        };

        var buttons = new StackPanel { Orientation = Orientation.Horizontal };
        buttons.Children.Add(_zoomText);
        buttons.Children.Add(NavButton("−", "Zoom out", () => Canvas.ZoomStep(false)));
        buttons.Children.Add(NavButton("+", "Zoom in", () => Canvas.ZoomStep(true)));
        buttons.Children.Add(NavButton("⤢", "Zoom to fit all shapes", ZoomExtentsRequested));

        _nav = new Border
        {
            Margin = new Thickness(0, 12, 12, 0),
            Padding = new Thickness(6, 4, 6, 4),
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Top,
            Background = new SolidColorBrush(Color.FromArgb(0xCC, 0x1E, 0x1E, 0x1E)),
            CornerRadius = new CornerRadius(6),
            Visibility = Visibility.Collapsed,
            Child = buttons,
        };

        _stack.Children.Add(canvas);
        _stack.Children.Add(_nav);
        Child = _stack;

        // The zoom readout has to follow the view however it changed — the buttons, a zoom-to-fit, a
        // wheel, or a middle-drag pan (which keeps working even when user code owns the mouse).
        canvas.Viewport.TransformChanged += (_, _) =>
        {
            if (_nav.Visibility == Visibility.Visible) UpdateZoomReadout();
        };

        MouseEnter += OnPointerEntered;
        MouseLeave += OnPointerLeft;
    }

    internal RenderCanvas Canvas { get; }

    /// <summary>Raised when this cell's zoom-to-fit is pressed; the host supplies the shapes.</summary>
    internal event Action<ViewportCell>? ZoomExtentsClicked;

    /// <summary>Raised when the pointer enters, so the host can make this the active cell.</summary>
    internal event Action<ViewportCell>? Activated;

    /// <summary>
    /// Hides the navigation overlay for the duration of a capture. It is a child of this cell, so
    /// unlike the old sibling panel it <b>would</b> be baked into an exported image.
    /// </summary>
    internal IDisposable HideChromeForCapture()
    {
        var previous = _nav.Visibility;
        _nav.Visibility = Visibility.Collapsed;
        return new Restore(() => _nav.Visibility = previous);
    }

    /// <summary>
    /// Marks this cell as the one keyboard shortcuts and tools act on. Drawn only when the layout is
    /// actually divided, so a single-cell canvas renders exactly the pixels it always has.
    /// </summary>
    internal void SetActive(bool active, bool layoutIsDivided)
    {
        BorderThickness = layoutIsDivided ? new Thickness(1) : new Thickness(0);
        BorderBrush = layoutIsDivided && active
            ? new SolidColorBrush(Color.FromRgb(0x3E, 0x6E, 0xA8))
            : new SolidColorBrush(Color.FromRgb(0x33, 0x33, 0x33));
    }

    internal void UpdateZoomReadout() => _zoomText.Text = $"{Canvas.Scale * 100:F0}%";

    private void OnPointerEntered(object sender, MouseEventArgs e)
    {
        _nav.Visibility = Visibility.Visible;
        UpdateZoomReadout();
        Activated?.Invoke(this);
    }

    // Deliberately not clearing the active cell here: moving the pointer to the Draw menu or the
    // outliner must leave the last cell active, or every keyboard shortcut would lose its target.
    private void OnPointerLeft(object sender, MouseEventArgs e) => _nav.Visibility = Visibility.Collapsed;

    private void ZoomExtentsRequested() => ZoomExtentsClicked?.Invoke(this);

    private Button NavButton(string glyph, string tip, Action onClick)
    {
        var button = new Button
        {
            Content = glyph,
            ToolTip = tip,
            Style = TryFindResource("MediaButtonStyle") as Style
                    ?? Application.Current?.TryFindResource("MediaButtonStyle") as Style,
        };
        button.Click += (_, _) => onClick();
        return button;
    }

    private sealed class Restore : IDisposable
    {
        private readonly Action _undo;
        internal Restore(Action undo) => _undo = undo;
        public void Dispose() => _undo();
    }
}
