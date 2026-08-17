using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using C2VGeometry;

namespace DoodleSharp;

/// <summary>
/// Sidebar listing every <see cref="GlobalParameters"/> entry with a type-appropriate editor.
///
/// <para>
/// Numeric rows use the two-tier update from the Properties panel (see CLAUDE.md note 30): dragging
/// the slider calls <see cref="GlobalParameters.Assign{T}"/> on every tick, which raises
/// <see cref="GlobalParameters.Changed"/> — the host debounces that into a resident re-execution so
/// the canvas tracks the drag live. The expensive half (rewriting the literal in the user's source
/// and doing a full recompile) happens once, on mouse-up, via <see cref="ParameterCommitted"/>.
/// Routing every tick through the commit path would rewrite the document on every pixel of drag.
/// </para>
/// </summary>
public partial class GlobalParametersPanel : UserControl
{
    /// <summary>Raised when an edit is finished and should be written back into the user's code.</summary>
    public event Action<Parameter>? ParameterCommitted;

    private bool _isUpdating;
    private bool _isDragging;
    private readonly Dictionary<string, Action> _valueRefreshers = new(StringComparer.OrdinalIgnoreCase);

    public GlobalParametersPanel()
    {
        InitializeComponent();

        GlobalParameters.Reloaded += OnRegistryReloaded;
        Unloaded += (_, _) => GlobalParameters.Reloaded -= OnRegistryReloaded;
    }

    private void OnRegistryReloaded()
    {
        // May arrive off the UI thread (MCP bridge) — and must never fire mid-drag, or the slider
        // the user is holding would be torn out from under the mouse.
        Dispatcher.BeginInvoke(new Action(() => { if (!_isDragging) Rebuild(); }));
    }

    /// <summary>Rebuilds every row from the registry. Called after a run changes the parameter set.</summary>
    public void Rebuild()
    {
        _valueRefreshers.Clear();
        RowsPanel.Children.Clear();

        var all = GlobalParameters.All;
        if (all.Count == 0)
        {
            RowsPanel.Children.Add(EmptyMessage);
            EmptyMessage.Visibility = Visibility.Visible;
            return;
        }

        string? currentGroup = null;
        bool firstRow = true;
        foreach (var p in all)
        {
            if (!string.Equals(p.Group, currentGroup, StringComparison.Ordinal))
            {
                currentGroup = p.Group;
                if (!string.IsNullOrWhiteSpace(currentGroup))
                {
                    RowsPanel.Children.Add(new TextBlock
                    {
                        Text = currentGroup!.ToUpperInvariant(),
                        Style = (Style)Resources["GroupHeader"]
                    });
                }
            }

            RowsPanel.Children.Add(BuildRow(p, firstRow));
            firstRow = false;
        }
    }

    /// <summary>Refreshes displayed values in place, without rebuilding the visual tree.</summary>
    public void RefreshValues()
    {
        _isUpdating = true;
        try
        {
            foreach (var refresh in _valueRefreshers.Values) refresh();
        }
        finally { _isUpdating = false; }
    }

    // ────────────────────────────────────────────────────────────────────────
    //  Row construction
    // ────────────────────────────────────────────────────────────────────────

    private UIElement BuildRow(Parameter p, bool isFirst)
    {
        var container = new StackPanel { Margin = new Thickness(0, isFirst ? 6 : 10, 0, 0) };

        var header = new Grid();
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var nameBlock = new TextBlock
        {
            Text = p.Name,
            Style = (Style)Resources["ParamName"],
            ToolTip = BuildTooltip(p)
        };
        Grid.SetColumn(nameBlock, 0);
        header.Children.Add(nameBlock);
        container.Children.Add(header);

        switch (p.Kind)
        {
            case ParamKind.Number: BuildNumberEditor(p, header, container); break;
            case ParamKind.Boolean: BuildBooleanEditor(p, header); break;
            case ParamKind.Text: BuildTextEditor(p, container); break;
            case ParamKind.Date: BuildDateEditor(p, container); break;
        }

        return container;
    }

    private static string BuildTooltip(Parameter p)
    {
        var lines = new List<string>();
        if (!string.IsNullOrWhiteSpace(p.Description)) lines.Add(p.Description!);
        lines.Add($"Type: {p.Kind}");
        lines.Add($"Declared: {p.DefaultValue}");
        if (p.SourceLine > 0)
            lines.Add($"Source: {System.IO.Path.GetFileName(p.SourceFile)}, line {p.SourceLine}");
        return string.Join(Environment.NewLine, lines);
    }

    private void BuildNumberEditor(Parameter p, Grid header, StackPanel container)
    {
        // Value box sits on the header row, right-aligned next to the name.
        var valueBox = new TextBox
        {
            Style = (Style)Resources["ParamBox"],
            Width = 68,
            Text = FormatNumber(p.AsDouble)
        };
        Grid.SetColumn(valueBox, 1);
        header.Children.Add(valueBox);

        var minBox = new TextBox
        {
            Style = (Style)Resources["RangeBox"],
            Text = FormatNumber(p.EffectiveMin)
        };
        var maxBox = new TextBox
        {
            Style = (Style)Resources["RangeBox"],
            Text = FormatNumber(p.EffectiveMax)
        };

        var slider = new Slider
        {
            Minimum = p.EffectiveMin,
            Maximum = p.EffectiveMax,
            Value = Math.Clamp(p.AsDouble, p.EffectiveMin, p.EffectiveMax),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(6, 0, 6, 0),
            IsMoveToPointEnabled = true
        };
        if (p.Step is > 0)
        {
            slider.TickFrequency = p.Step.Value;
            slider.IsSnapToTickEnabled = true;
        }

        var sliderRow = new Grid { Margin = new Thickness(0, 4, 0, 0) };
        sliderRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        sliderRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        sliderRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(minBox, 0);
        Grid.SetColumn(slider, 1);
        Grid.SetColumn(maxBox, 2);
        sliderRow.Children.Add(minBox);
        sliderRow.Children.Add(slider);
        sliderRow.Children.Add(maxBox);
        container.Children.Add(sliderRow);

        bool dirty = false;

        // Backstop for value changes that have no end-of-gesture event of their own (mouse wheel,
        // repeat-button clicks on the track). It is suppressed while a thumb drag is in progress so
        // a slow drag cannot rewrite the source mid-gesture.
        var idleCommit = new System.Windows.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(450)
        };

        // ── Live tier: mutate + notify on every tick, no code rewrite, no recompile. ──
        slider.ValueChanged += (_, e) =>
        {
            if (_isUpdating) return;
            dirty = true;
            _isUpdating = true;
            try { valueBox.Text = FormatNumber(e.NewValue); }
            finally { _isUpdating = false; }
            GlobalParameters.Assign(p.Name, e.NewValue);

            idleCommit.Stop();
            idleCommit.Start();
        };

        // ── Commit tier: once, when the gesture ends. ──
        void Commit()
        {
            idleCommit.Stop();
            if (!dirty) return;
            dirty = false;
            _isDragging = false;
            ParameterCommitted?.Invoke(p);
        }

        idleCommit.Tick += (_, _) =>
        {
            idleCommit.Stop();
            if (!_isDragging) Commit();
        };

        // Thumb.DragStarted/DragCompleted are the reliable drag boundaries on a Slider. The
        // Preview mouse events are not: while the Thumb holds mouse capture the button-up never
        // reaches a handler attached to the Slider itself, so a drag would end with no commit and
        // the edited value would never reach the user's source.
        slider.AddHandler(Thumb.DragStartedEvent,
            new DragStartedEventHandler((_, _) => _isDragging = true), handledEventsToo: true);
        slider.AddHandler(Thumb.DragCompletedEvent,
            new DragCompletedEventHandler((_, _) => Commit()), handledEventsToo: true);

        slider.LostKeyboardFocus += (_, _) => Commit();
        slider.KeyUp += (_, e) =>
        {
            if (e.Key is Key.Left or Key.Right or Key.PageUp or Key.PageDown or Key.Home or Key.End)
                Commit();
        };

        // Typing an exact value bypasses the slider entirely.
        void ApplyValueBox()
        {
            if (_isUpdating) return;
            if (!TryParse(valueBox.Text, out var v))
            {
                _isUpdating = true;
                try { valueBox.Text = FormatNumber(p.AsDouble); }
                finally { _isUpdating = false; }
                return;
            }

            GlobalParameters.Assign(p.Name, v);
            _isUpdating = true;
            try
            {
                if (v < slider.Minimum) { slider.Minimum = v; minBox.Text = FormatNumber(v); }
                if (v > slider.Maximum) { slider.Maximum = v; maxBox.Text = FormatNumber(v); }
                slider.Value = v;
            }
            finally { _isUpdating = false; }

            dirty = true;
            Commit();
        }

        valueBox.LostFocus += (_, _) => ApplyValueBox();
        valueBox.KeyDown += (_, e) => { if (e.Key == Key.Enter) ApplyValueBox(); };

        // Range boxes are panel-only metadata — they retarget the slider, never the source.
        void ApplyRange()
        {
            if (_isUpdating) return;
            if (!TryParse(minBox.Text, out var lo) || !TryParse(maxBox.Text, out var hi) || hi <= lo)
            {
                _isUpdating = true;
                try
                {
                    minBox.Text = FormatNumber(slider.Minimum);
                    maxBox.Text = FormatNumber(slider.Maximum);
                }
                finally { _isUpdating = false; }
                return;
            }

            _isUpdating = true;
            try
            {
                // Widen before narrowing so WPF never sees Minimum > Maximum.
                slider.Minimum = Math.Min(lo, slider.Minimum);
                slider.Maximum = Math.Max(hi, slider.Maximum);
                slider.Minimum = lo;
                slider.Maximum = hi;
                slider.Value = Math.Clamp(p.AsDouble, lo, hi);
            }
            finally { _isUpdating = false; }

            GlobalParameters.SetRange(p.Name, lo, hi);
        }

        minBox.LostFocus += (_, _) => ApplyRange();
        maxBox.LostFocus += (_, _) => ApplyRange();
        minBox.KeyDown += (_, e) => { if (e.Key == Key.Enter) ApplyRange(); };
        maxBox.KeyDown += (_, e) => { if (e.Key == Key.Enter) ApplyRange(); };

        _valueRefreshers[p.Name] = () =>
        {
            valueBox.Text = FormatNumber(p.AsDouble);
            slider.Minimum = Math.Min(p.EffectiveMin, p.AsDouble);
            slider.Maximum = Math.Max(p.EffectiveMax, p.AsDouble);
            minBox.Text = FormatNumber(slider.Minimum);
            maxBox.Text = FormatNumber(slider.Maximum);
            slider.Value = p.AsDouble;
        };
    }

    private void BuildBooleanEditor(Parameter p, Grid header)
    {
        var check = new CheckBox
        {
            IsChecked = p.AsBool,
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = (System.Windows.Media.Brush)FindResource("ForegroundBrush")
        };
        Grid.SetColumn(check, 1);
        header.Children.Add(check);

        check.Click += (_, _) =>
        {
            if (_isUpdating) return;
            GlobalParameters.Assign(p.Name, check.IsChecked == true);
            ParameterCommitted?.Invoke(p);
        };

        _valueRefreshers[p.Name] = () => check.IsChecked = p.AsBool;
    }

    private void BuildTextEditor(Parameter p, StackPanel container)
    {
        var box = new TextBox
        {
            Style = (Style)Resources["ParamBox"],
            Text = p.AsText,
            Margin = new Thickness(0, 4, 0, 0)
        };
        container.Children.Add(box);

        void Apply()
        {
            if (_isUpdating) return;
            if (box.Text == p.AsText) return;
            GlobalParameters.Assign(p.Name, box.Text);
            ParameterCommitted?.Invoke(p);
        }

        box.LostFocus += (_, _) => Apply();
        box.KeyDown += (_, e) => { if (e.Key == Key.Enter) Apply(); };

        _valueRefreshers[p.Name] = () => box.Text = p.AsText;
    }

    private void BuildDateEditor(Parameter p, StackPanel container)
    {
        // Editable at runtime but never written back to source: the declaring expression is usually
        // something like DateTime.Now, and replacing that with a frozen literal would be wrong.
        var box = new TextBox
        {
            Style = (Style)Resources["ParamBox"],
            Text = p.AsDate.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
            Margin = new Thickness(0, 4, 0, 0),
            ToolTip = "Edits apply to the current run only — date parameters are not written back to code."
        };
        container.Children.Add(box);

        void Apply()
        {
            if (_isUpdating) return;
            if (DateTime.TryParse(box.Text, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt))
                GlobalParameters.Assign(p.Name, dt);
            else
                box.Text = p.AsDate.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
        }

        box.LostFocus += (_, _) => Apply();
        box.KeyDown += (_, e) => { if (e.Key == Key.Enter) Apply(); };

        _valueRefreshers[p.Name] = () =>
            box.Text = p.AsDate.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
    }

    private void ResetAllButton_Click(object sender, RoutedEventArgs e)
    {
        GlobalParameters.ResetAll();
        RefreshValues();
    }

    private static string FormatNumber(double v) =>
        v.ToString("0.############", CultureInfo.InvariantCulture);

    private static bool TryParse(string text, out double value) =>
        double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value) ||
        double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out value);
}
