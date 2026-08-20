using System.IO;
using System.Text.RegularExpressions;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Xml;
using ICSharpCode.AvalonEdit;
using ICSharpCode.AvalonEdit.CodeCompletion;
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Folding;
using ICSharpCode.AvalonEdit.Highlighting;
using ICSharpCode.AvalonEdit.Highlighting.Xshd;
using ICSharpCode.AvalonEdit.Search;
using Microsoft.Win32;
using System.Collections.ObjectModel;
using System.Windows.Threading;
using DoodleSharp.Animation;
using DoodleSharp.Canvas;
using DoodleSharp.Commands;
using DoodleSharp.Console;
using DoodleSharp.Diagnostics;
using DoodleSharp.Editor;
using DoodleSharp.Editor.Minimap;
using DoodleSharp.Execution;
using DoodleSharp.Export;
using DoodleSharp.Project;
using DoodleSharp.Search;
using DoodleSharp.Services;
using ICSharpCode.AvalonEdit.Rendering;
using Microsoft.CodeAnalysis;

// Resolve ambiguities between WPF and WinForms/Drawing
using Color = System.Windows.Media.Color;
using Point = System.Windows.Point;
using Size = System.Windows.Size;
using Pen = System.Windows.Media.Pen;
using Brush = System.Windows.Media.Brush;
using FontFamily = System.Windows.Media.FontFamily;
using FontStyle = System.Windows.FontStyle;
using FontWeight = System.Windows.FontWeight;
using ToolTip = System.Windows.Controls.ToolTip;
using ContextMenu = System.Windows.Controls.ContextMenu;
using MenuItem = System.Windows.Controls.MenuItem;
using Control = System.Windows.Controls.Control;
using Application = System.Windows.Application;
using MessageBox = System.Windows.MessageBox;
using Cursors = System.Windows.Input.Cursors;
using Cursor = System.Windows.Input.Cursor;
using Button = System.Windows.Controls.Button;
using TextBox = System.Windows.Controls.TextBox;
using ComboBox = System.Windows.Controls.ComboBox;
using CheckBox = System.Windows.Controls.CheckBox;
using Label = System.Windows.Controls.Label;
using Image = System.Windows.Controls.Image;
using OpenFileDialog = Microsoft.Win32.OpenFileDialog;
using SaveFileDialog = Microsoft.Win32.SaveFileDialog;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;

namespace DoodleSharp;

public partial class MainWindow : Window
{
    private readonly ModuleCompiler _compiler;
    private VizCodeProject? _currentProject;
    private VizCodeFile? _activeFile;
    private CompletionWindow? _completionWindow;
    private bool _completionRunning;   // a completion query is in flight; see TriggerManualCompletion
    private OverloadInsightWindow? _insightWindow;
    
    // Folding
    private FoldingManager? _foldingManager;
    private BraceFoldingStrategy? _foldingStrategy;
    private DispatcherTimer? _foldingTimer;

    // File system watcher for external changes
    private FileSystemWatcher? _projectWatcher;
    private DispatcherTimer? _fileWatcherDebounceTimer;

    // Snippet session for Tab navigation
    private SnippetSession? _snippetSession;
    private VizTextMarkerService? _textMarkerService;
    private RefactoringProvider? _refactoringProvider;
    private BracketHighlightRenderer? _bracketRenderer;
    private MultiSelectionRenderer? _multiSelectionRenderer;
    private SelectionHighlightRenderer? _selectionHighlightRenderer;

    // Drag-and-drop state for project tree
    private System.Windows.Point _dragStartPoint;
    private bool _isDragging;

    // Real-time error checking
    private DispatcherTimer? _syntaxCheckTimer;
    private bool _textChangedSinceLastCheck;

    // Animation
    private System.Diagnostics.Stopwatch _animationStopwatch = new();

    /// <summary>
    /// Free-running clock for frame callbacks. Separate from <see cref="_animationStopwatch"/>,
    /// which starts and stops with timeline playback — a self-rescheduling callback has no notion
    /// of being paused, so it needs a clock that never stops.
    /// </summary>
    private readonly System.Diagnostics.Stopwatch _frameLoopClock = new();
    private double _lastAnimationFrameTime = -1;

    /// <summary>
    /// The <see cref="CanvasRenderer.RegistryVersion"/> the canvas snapshot was last taken at, so
    /// <see cref="RepaintAfterUserCode"/> can tell "shapes moved" from "shapes were added or removed".
    /// </summary>
    private int _lastRepaintedRegistryVersion = -1;

    // Peek Definition popup
    private System.Windows.Controls.Primitives.Popup? _peekPopup;

    // Inlay Hints
    private Editor.InlayHintGenerator? _inlayHintGenerator;

    // Semantic Highlighting
    private Editor.SemanticHighlighter? _semanticHighlighter;
    private DispatcherTimer? _semanticUpdateTimer;

    // Auto Save (periodic write-to-disk of unsaved project files)
    private DispatcherTimer? _autoSaveTimer;
    private bool _autoSavePromptActive;      // an auto-save prompt is on screen - don't stack another
    private bool _autoSavePromptSuppressed;  // user answered "No" - stay quiet until the project has a home
    private const int MinAutoSaveSeconds = 5;
    private const int MaxAutoSaveSeconds = 3600;

    // Auto-Run (periodic re-execution of the project's code, per-project setting)
    private DispatcherTimer? _autoRunTimer;
    private bool _autoRunInFlight;           // a tick's run is still going - don't stack another
    private string? _lastAutoRunSignature;   // source as of the last full compile; unchanged -> resident re-run
    private const int AutoRunIntervalMs = 500;

    // Console panel: bound once in InitializeConsole and updated in place by RefreshConsole.
    private readonly ObservableCollection<Console.ConsoleEntry> _consoleEntries = new();

    // Code Lens
    private Editor.CodeLensGenerator? _codeLensGenerator;

    // Hierarchy Provider
    private Editor.HierarchyProvider? _hierarchyProvider;

    // IntelliSense: Cached compilation workspace and documentation sidecar
    private Editor.CachedCompilationWorkspace? _completionWorkspace;
    private Editor.DocumentationSidecar? _docSidecar;

    // Find and Replace
    private FindReplaceService _findReplaceService = new();
    private FindReplaceDialog? _findReplaceDialog;

    // Properties Panel
    private PropertiesPanel? _propertiesPanel;

    public static RoutedCommand RenameCommand = new RoutedCommand();
    public static RoutedCommand GoToDefinitionCommand = new RoutedCommand();
    public static RoutedCommand FindAllReferencesCommand = new RoutedCommand();
    public static RoutedCommand PeekDefinitionCommand = new RoutedCommand();
    public static RoutedCommand DocumentSymbolsCommand = new RoutedCommand();
    public static RoutedCommand WorkspaceSymbolsCommand = new RoutedCommand();
    public static RoutedCommand CallHierarchyCommand = new RoutedCommand();
    public static RoutedCommand TypeHierarchyCommand = new RoutedCommand();
    public static RoutedCommand DirectRenameCommand = new RoutedCommand();

    public MainWindow(VizCodeProject? project = null)
    {
        using var bootScope = Journal.Scope("MW.CTOR", "Main window construction",
            $"project={project?.ProjectFilePath ?? "<none>"}");

        // Registered before any subsystem initialises, so even a crash during start-up dumps
        // whatever state exists by then.
        Journal.RegisterStateProvider("MainWindow", DescribeStateForJournal);

        InitializeComponent();

        // Capture the arrangement declared in the XAML before anything touches it. This is what
        // Reset Layout restores, and taking it from the live tree rather than a second copy on disk
        // is what stops the default and the markup drifting apart. The ordering is load-bearing:
        // one Hide() or a restored layout before this line and the "default" is no longer default.
        _defaultLayoutXml = SerializeLayout();
        InitializeDockPanels();

        VersionText.Text = $"v{UpdateChecker.CurrentVersion}";

        _compiler = new ModuleCompiler();
        _refactoringProvider = new RefactoringProvider(_compiler);
        _hierarchyProvider = new Editor.HierarchyProvider();

        // Initialize snippet session
        _snippetSession = new SnippetSession(CodeEditor);
        SnippetCompletionData.ActiveSession = _snippetSession;

        // Each subsystem gets its own scope: an ENTER with no EXIT names the one that hung or died.
        using (Journal.Scope("MW.INIT.EDITOR")) InitializeEditor();
        using (Journal.Scope("MW.INIT.COMMANDS")) InitializeCommands();
        using (Journal.Scope("MW.INIT.CANVAS")) InitializeCanvas();
        using (Journal.Scope("MW.INIT.CONSOLE")) InitializeConsole();
        using (Journal.Scope("MW.INIT.CONTEXTMENU")) InitializeContextMenu();

        if (project != null)
        {
            _currentProject = project;
            LoadProjectTree();
            RefreshFileTabs();

            var entry = _currentProject.EntryPointFile;
            if (entry != null) SelectFile(entry);

            // Start watching for external changes
            StartProjectWatcher(_currentProject.ProjectDirectory);

            // Initialize cached compilation workspace for IntelliSense
            InitializeCompletionWorkspace();
        }

        // Unconditional: application settings are global, so the Settings tab must show the saved
        // values even when no project is open. It used to run only inside the branch above.
        LoadSettingsToUI();

        // Everything the markup does to a control has happened by now, so from here a settings
        // handler firing really is the user.
        _settingsUiReady = true;

        Loaded += MainWindow_Loaded;
    }

    /// <summary>
    /// Describes what the window is holding, for the crash-time state dump. Must be cheap and must
    /// never throw — <see cref="Journal.CaptureState"/> calls it while the process is dying.
    /// </summary>
    private string DescribeStateForJournal()
    {
        var text = new System.Text.StringBuilder();
        try
        {
            text.AppendLine($"project = {_currentProject?.ProjectFilePath ?? "<none>"}");
            text.AppendLine($"projectDir = {_currentProject?.ProjectDirectory ?? "<none>"}");
            text.AppendLine($"unsaved = {_currentProject?.HasUnsavedChanges}");

            if (_currentProject != null)
            {
                foreach (var file in _currentProject.Files)
                {
                    text.AppendLine($"file: {Journal.DescribeFile(file.FilePath, file.Content)} " +
                                    $"open={file.IsOpen} new={file.IsNew} dirty={file.HasUnsavedChanges} entry={file.IsEntryPoint}");
                }
            }

            text.AppendLine($"activeFile = {_activeFile?.FilePath ?? "<none>"}");
            text.AppendLine($"editorChars = {CodeEditor?.Document?.TextLength} caret = {CodeEditor?.CaretOffset}");
            text.AppendLine($"canvasShapes = {CanvasRenderer.Instance.GetShapes().Count}");
            text.AppendLine($"timelinePlaying = {CanvasRenderer.Instance.ActiveTimeline?.IsPlaying}");
            text.AppendLine($"sketchRunning = {DoodleSharp.Sketching.SketchRuntime.Instance.IsRunning}");
            text.AppendLine($"residentAssembly = {ModuleCompiler.HasResidentAssembly}");
            text.AppendLine($"globalParameters = {string.Join(", ", C2VGeometry.GlobalParameters.All.Select(p => $"{p.Name}={p.Value}"))}");
        }
        catch (Exception ex)
        {
            text.AppendLine($"<state capture failed: {ex.GetType().Name}: {ex.Message}>");
        }

        // The live editor text last: it is the single most useful artefact for reproducing a crash,
        // and putting it at the end keeps the summary readable.
        try
        {
            if (CodeEditor?.Document != null)
                Journal.WriteBlock("ACTIVE EDITOR BUFFER", CodeEditor.Document.Text);
        }
        catch { }

        return text.ToString();
    }

    private void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        Journal.Info("MW.LOADED", "Main window loaded");
        ViewportHost.CenterOrigin();

        // Apply application settings
        var settings = ApplicationSettings.Instance;
        ViewportHost.ShowGrid = settings.ShowGrid;
        GridMenuItem.IsChecked = settings.ShowGrid;

        // Restore the docking arrangement. Falls back to the XAML default plus the saved visibility
        // booleans when there is no layout file yet, or when the one on disk cannot be trusted.
        RestoreLayout();

        _ = CheckForUpdatesAsync();
    }

    private string? _updateUrl;

    private async System.Threading.Tasks.Task CheckForUpdatesAsync()
    {
        var info = await UpdateChecker.CheckAsync();
        if (info is null) return;

        _updateUrl = info.ReleaseUrl;
        UpdateAvailableText.Text = $"v{info.Latest} available — Update!";
        UpdateAvailableButton.ToolTip = $"You're on v{info.Current}. Click to open the release page for {info.TagName}.";
        UpdateAvailableButton.Visibility = Visibility.Visible;
    }

    private void UpdateAvailableButton_Click(object sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrEmpty(_updateUrl))
        {
            UpdateChecker.OpenInBrowser(_updateUrl);
        }
    }

    /// <summary>
    /// The canvas of the viewport the user is working in — the cell the pointer last entered or
    /// clicked.
    ///
    /// <para>
    /// A property, never a field: a snapshot taken once would pin the first cell forever, and every
    /// tool, selection and keyboard shortcut would keep acting on it after the pointer moved on.
    /// Anything that belongs to the <i>drawing</i> rather than to where the user is working — the
    /// background, the grid, snapping, a repaint — goes through <see cref="ViewportHost"/> instead.
    /// </para>
    /// </summary>
    private RenderCanvas RenderCanvas => ViewportHost.ActiveCanvas;

    private void InitializeCanvas()
    {
        // Wired per canvas, because a resize can create more of them at any time. Raised for the
        // first cell during the host's own construction, so this must be attached before then —
        // it is: the host raises it again for every cell it builds afterwards.
        ViewportHost.CanvasCreated += (s, canvas) => WireCanvas(canvas);
        foreach (var canvas in ViewportHost.Canvases) WireCanvas(canvas);

        // Timeline panel events
        TimelinePanel.TimeChanged += (s, time) =>
        {
            var timeline = CanvasRenderer.Instance.ActiveTimeline;
            if (timeline != null)
            {
                // Pause if scrubbing
                if (timeline.IsPlaying)
                {
                    _isPaused = true;
                    timeline.IsPlaying = false;
                    _animationStopwatch.Stop();
                    PlayPauseBtn.Content = "\u25B6";
                }
                ViewportHost.Refresh();
            }
        };

        // Animation Loop — uses CompositionTarget.Rendering for vsync-aligned frames
        // Frame callbacks get their timestamp from here rather than from the timeline's stopwatch,
        // which starts and stops with playback.
        _frameLoopClock.Restart();
        DoodleSharp.Animation.Frame.CallbackFailed += ex =>
        {
            Console.ConsoleOutput.Instance.WriteError("Animation", 0,
                $"Frame callback threw; the loop has been stopped. {ex.GetType().Name}: {ex.Message}");
            Console.ConsoleOutput.Instance.Flush();
        };

        DoodleSharp.Animation.Mouse.CallbackFailed += ex =>
        {
            Console.ConsoleOutput.Instance.WriteError("Mouse", 0,
                $"Mouse handler threw; all handlers have been detached. {ex.GetType().Name}: {ex.Message}");
            Console.ConsoleOutput.Instance.Flush();
        };

        // Registering or dropping a handler flips the canvas in and out of interactive mode, which
        // changes visible chrome. Main() runs on a thread-pool thread, so this has to be marshalled.
        DoodleSharp.Animation.Mouse.HandlersChanged += () =>
            Dispatcher.BeginInvoke(new Action(SyncInteractiveModeChrome));

        bool _needsInitialZoom = true;
        TimeSpan _lastRenderTime = TimeSpan.Zero;
        CompositionTarget.Rendering += (s, e) =>
        {
            // Deduplicate: WPF can fire Rendering multiple times per frame
            var args = (RenderingEventArgs)e;
            if (args.RenderingTime == _lastRenderTime) return;
            _lastRenderTime = args.RenderingTime;

            // ── Sketch mode ──
            // A running Sketch (p5.js-style Setup/Draw) drives the frame loop here.
            // Mutually exclusive with the Timeline path in v1.
            if (DoodleSharp.Sketching.SketchRuntime.Instance.IsRunning)
            {
                // Apply a Background(color) call from Setup before the tick so the new
                // brush is visible on the first frame paint.
                var bgRequest = DoodleSharp.Sketching.SketchRuntime.Instance.TryConsumeBackground();
                if (bgRequest != null)
                {
                    try
                    {
                        var c = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(bgRequest);
                        ViewportHost.CanvasBackground = new System.Windows.Media.SolidColorBrush(c);
                    }
                    catch
                    {
                        Console.ConsoleOutput.Instance.WriteLine("Sketch", 0,
                            $"Background: '{bgRequest}' is not a recognised color name.");
                    }
                }

                DoodleSharp.Sketching.SketchRuntime.Instance.Tick();

                // Tick() calls CanvasRenderer.Clear() and re-runs the user's Draw(), so the shape
                // objects are new every frame. Refresh() alone would keep repainting the snapshot
                // that Render() took at Run time — which is why a sketch creating its shapes in
                // Draw() used to sit on frame 0. Hand the canvas this frame's shapes first.
                ViewportHost.ForEach(c =>
                    c.SetFrameShapes(CanvasRenderer.Instance.GetShapes(c.OwningViewport!)));
                ViewportHost.Refresh();

                if (DoodleSharp.Sketching.SketchRuntime.Instance.TryConsumeZoomRequest())
                    ViewportHost.ForEach(c =>
                        c.ZoomExtents(CanvasRenderer.Instance.GetShapes(c.OwningViewport!)));

                UpdateAnimationControlsVisibility();
                return;
            }

            // ── Frame callbacks (the requestAnimationFrame model) ──
            // Independent of the timeline: a Main()-mode script can drive motion by rescheduling a
            // callback, without composing an Animator.
            if (DoodleSharp.Animation.Frame.HasPending)
            {
                if (DoodleSharp.Animation.Frame.Pump(_frameLoopClock.Elapsed.TotalSeconds))
                {
                    RepaintAfterUserCode();
                }
            }

            // ── Mouse handlers ──
            // Those are dispatched synchronously from the canvas's own input handlers, so by the time
            // we get here they have already run; all that is left is one repaint for the frame. Doing
            // it here rather than per event coalesces a burst of moves into a single redraw.
            if (DoodleSharp.Animation.Mouse.ConsumeSceneDirty())
            {
                RepaintAfterUserCode();
            }

            var timeline = CanvasRenderer.Instance.ActiveTimeline;

            if (timeline != null && timeline.IsPlaying)
            {
                // Update animation state (sets DrawFactor, positions, etc.)
                var elapsedSeconds = _animationStopwatch.Elapsed.TotalSeconds;
                var scaledTime = elapsedSeconds * timeline.Speed;

                // Throttle rendering to the user's desired FPS (using real time, not scaled)
                var frameInterval = 1.0 / timeline.Fps;
                if (elapsedSeconds - _lastAnimationFrameTime >= frameInterval - 0.002)
                {
                    _lastAnimationFrameTime = elapsedSeconds;

                    timeline.Update(scaledTime);

                    // Update() mutates OffsetX/OffsetY/DrawFactor in place, so the shape objects
                    // are unchanged but the bounds the cull index holds are now stale. Re-read them
                    // before repainting — culling is no longer disabled during playback, and a
                    // stale box means a moving shape either vanishes or fails to appear.
                    ViewportHost.ForEach(c => c.ReindexForAnimationFrame());

                    // Redraw canvas first (critical path — before UI updates trigger layout)
                    ViewportHost.Refresh();

                    // Zoom to fit on first frame that has visible shapes (if setting enabled)
                    if (_needsInitialZoom && timeline.Shapes.Count > 0)
                    {
                        if (ApplicationSettings.Instance.ZoomToFitOnRun)
                        {
                            ViewportHost.ForEach(c =>
                                c.ZoomExtents(CanvasRenderer.Instance.GetShapes(c.OwningViewport!)));
                        }
                        _needsInitialZoom = false;
                    }
                }
            }
            else
            {
                _needsInitialZoom = true; // Reset for next timeline
            }

            // Update animation controls visibility and time display (after canvas draw)
            UpdateAnimationControlsVisibility();
        };
    }

    /// <summary>
    /// Repaints the canvas after per-frame user code ran — a <c>Frame</c> callback or a <c>Mouse</c>
    /// handler.
    ///
    /// <para>
    /// <b>A shape the callback *created* needs <c>SetFrameShapes</c>, not <c>Refresh()</c>.</b>
    /// <c>CanvasRenderer.AddShape</c> appends only to the registry's own list;
    /// <c>RenderCanvas._currentShapes</c> is a separate snapshot assigned by <c>Render()</c> at the
    /// end of a run. So <c>Refresh()</c> alone repaints the snapshot as it was when the run finished
    /// and the new shape never appears — while a shape *mutated* in place appears fine, which is what
    /// made this asymmetry easy to miss. <c>SetFrameShapes</c> retakes the snapshot and rebuilds the
    /// cull index in one call.
    /// </para>
    ///
    /// <para>
    /// The snapshot is only retaken when <see cref="CanvasRenderer.RegistryVersion"/> moved, because
    /// it costs a <c>ToList()</c> plus a full index rebuild. In the common case — a callback nudging
    /// existing shapes — the shape objects are identical and only their cached bounds are stale, so
    /// the cheaper re-index is enough.
    /// </para>
    /// </summary>
    private void RepaintAfterUserCode()
    {
        var version = CanvasRenderer.Instance.RegistryVersion;
        if (version != _lastRepaintedRegistryVersion)
        {
            _lastRepaintedRegistryVersion = version;
            ViewportHost.ForEach(c =>
                c.SetFrameShapes(CanvasRenderer.Instance.GetShapes(c.OwningViewport!)));
        }
        else
        {
            // Shapes moved in place, so only the cull index's cached boxes went stale.
            ViewportHost.ForEach(c => c.ReindexForAnimationFrame());
        }

        ViewportHost.Refresh();
    }

    /// <summary>
    /// Everything the window needs from one canvas. Called for every cell, including ones a layout
    /// change creates later — a cell whose events were never attached looks alive but reports no
    /// coordinates and no selection, which is invisible until someone tries to work in it.
    /// </summary>
    private void WireCanvas(RenderCanvas canvas)
    {
        canvas.MouseWorldPositionChanged += (s, pos) =>
        {
            CoordinatesText.Text = $"X: {pos.X:F2}  Y: {pos.Y:F2}";
        };

        canvas.SelectionTool.SelectionChanged += OnSelectionChanged;
        canvas.SelectionTool.ControlPointDragEnded += OnControlPointDragEnded;
    }

    private void InitializeConsole()
    {
        // Bound once and then updated in place. Reassigning ItemsSource regenerates every row, which
        // under Auto-Run meant tearing down and rebuilding the whole panel twice a second.
        ConsoleListBox.ItemsSource = _consoleEntries;

        Console.ConsoleOutput.Instance.OutputChanged += (s, e) =>
        {
            Dispatcher.Invoke(RefreshConsole);
        };

        // Initialize Find Results Panel
        FindResultsPanel.ResultActivated += (s, result) =>
        {
            if (result != null)
            {
                NavigateToSearchResult(result);
            }
        };
    }

    /// <summary>
    /// Builds the editor's right-click menu.
    ///
    /// <para>
    /// It used to offer only Cut/Copy/Paste and "Move type to new file", so navigation and the
    /// quick-action list were reachable only by shortcut (F12, Shift+F12, Ctrl+.) — invisible unless
    /// you already knew they existed. The menu now mirrors what <c>SharedEditorController</c>
    /// builds, plus a Quick Actions entry that opens the same analyser-driven list as Ctrl+.
    /// </para>
    /// </summary>
    private void InitializeContextMenu()
    {
        var contextMenu = new ContextMenu();

        // Standard Edit Commands with proper binding
        contextMenu.Items.Add(new MenuItem { Header = "Cut", Command = ApplicationCommands.Cut, InputGestureText = "Ctrl+X" });
        contextMenu.Items.Add(new MenuItem { Header = "Copy", Command = ApplicationCommands.Copy, InputGestureText = "Ctrl+C" });
        contextMenu.Items.Add(new MenuItem { Header = "Paste", Command = ApplicationCommands.Paste, InputGestureText = "Ctrl+V" });

        contextMenu.Items.Add(new Separator());

        // Navigation
        contextMenu.Items.Add(new MenuItem { Header = "Go to Definition", Command = GoToDefinitionCommand, InputGestureText = "F12" });
        contextMenu.Items.Add(new MenuItem { Header = "Peek Definition", Command = PeekDefinitionCommand, InputGestureText = "Alt+F12" });
        contextMenu.Items.Add(new MenuItem { Header = "Find All References", Command = FindAllReferencesCommand, InputGestureText = "Shift+F12" });

        contextMenu.Items.Add(new Separator());

        // Refactoring
        var quickActions = new MenuItem { Header = "Quick Actions...", InputGestureText = "Ctrl+." };
        quickActions.Click += (s, e) => ShowQuickActionsMenu();
        contextMenu.Items.Add(quickActions);

        contextMenu.Items.Add(new MenuItem { Header = "Rename Symbol", Command = DirectRenameCommand, InputGestureText = "F2" });

        var moveItem = new MenuItem
        {
            Header = "Move type to new file...",
            Name = "MoveTypeMenuItem",
            Tag = "" // Initialize Tag
        };
        moveItem.Click += MoveTypeMenuItem_Click;
        contextMenu.Items.Add(moveItem);

        CodeEditor.ContextMenu = contextMenu;
        CodeEditor.ContextMenuOpening += CodeEditor_ContextMenuOpening;
    }

    /// <summary>
    /// Brings the console panel level with <see cref="Console.ConsoleOutput"/>, touching only the
    /// rows that actually changed.
    /// </summary>
    /// <remarks>
    /// Console output is append-only within a run, so the new list almost always shares a prefix
    /// with the displayed one; entries are the same objects, so the shared part is found by
    /// reference. Keeping those rows leaves their containers, selection and scroll position alone,
    /// and a refresh that changes nothing does nothing at all — which is the common case when
    /// Auto-Run re-runs a program the user is not editing.
    /// </remarks>
    private void RefreshConsole()
    {
        // Not GetEntries(): that answers for the running program, which mid-run is a half-built
        // list nobody should see on screen.
        var entries = Console.ConsoleOutput.Instance.GetDisplayedEntries();

        int shared = 0;
        while (shared < _consoleEntries.Count && shared < entries.Count &&
               ReferenceEquals(_consoleEntries[shared], entries[shared]))
        {
            shared++;
        }

        if (shared == _consoleEntries.Count && shared == entries.Count) return;

        while (_consoleEntries.Count > shared)
            _consoleEntries.RemoveAt(_consoleEntries.Count - 1);

        for (int i = shared; i < entries.Count; i++)
            _consoleEntries.Add(entries[i]);

        // Defer scroll to after WPF finishes rendering the new items.
        if (_consoleEntries.Count > 0)
        {
            var lastItem = _consoleEntries[_consoleEntries.Count - 1];
            Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded, () =>
            {
                ConsoleListBox.ScrollIntoView(lastItem);
            });
        }
    }

    private void ClearConsoleButton_Click(object sender, RoutedEventArgs e)
    {
        Console.ConsoleOutput.Instance.Clear();
    }

    private void ConsoleCopy_Click(object sender, RoutedEventArgs e)
    {
        CopySelectedConsoleItems();
    }

    private void ConsoleSelectAll_Click(object sender, RoutedEventArgs e)
    {
        ConsoleListBox.SelectAll();
    }

    private void ConsoleListBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.C && (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
        {
            CopySelectedConsoleItems();
            e.Handled = true;
        }
        else if (e.Key == Key.A && (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
        {
            ConsoleListBox.SelectAll();
            e.Handled = true;
        }
    }

    private void ConsoleListBox_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (ConsoleListBox.SelectedItem is Console.ConsoleEntry entry && entry.IsClickable)
        {
            NavigateToError(entry.FilePath!, entry.LineNumber, entry.Column);
            e.Handled = true;
        }
    }

    private void NavigateToError(string filePath, int line, int column)
    {
        if (_currentProject == null) return;

        // Find and open the file in the project
        var file = _currentProject.Files.FirstOrDefault(f =>
            string.Equals(f.FilePath, filePath, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(Path.GetFileName(f.FilePath), Path.GetFileName(filePath), StringComparison.OrdinalIgnoreCase));

        if (file != null)
        {
            // Switch to the file's tab
            SelectFile(file);

            // Navigate to the line and column
            try
            {
                // Ensure line is within bounds
                if (line > 0 && line <= CodeEditor.Document.LineCount)
                {
                    var lineObj = CodeEditor.Document.GetLineByNumber(line);
                    var col = Math.Max(1, Math.Min(column, lineObj.Length + 1));
                    var offset = CodeEditor.Document.GetOffset(line, col);

                    CodeEditor.CaretOffset = offset;
                    CodeEditor.ScrollToLine(line);
                    CodeEditor.Focus();

                    // Highlight the line briefly
                    CodeEditor.Select(lineObj.Offset, lineObj.Length);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"NavigateToError: {ex.Message}");
            }
        }
    }

    private void CopySelectedConsoleItems()
    {
        if (ConsoleListBox.SelectedItems.Count == 0) return;
        
        var lines = ConsoleListBox.SelectedItems
            .Cast<Console.ConsoleEntry>()
            .Select(m => m.Message);
        var text = string.Join(Environment.NewLine, lines);
        System.Windows.Clipboard.SetText(text);
    }




    private void ResetLayout_Click(object sender, RoutedEventArgs e)
    {
        ResetLayoutToDefault();
    }

    /// <summary>
    /// Puts every panel back where it ships — the arrangement captured from the XAML at start-up.
    ///
    /// <para>
    /// Also restores the Ribbon and Minimap, which are not in the DockingManager and so are not
    /// described by the captured layout. "Reset Layout" means the whole window; leaving the ribbon
    /// hidden afterwards is the kind of half-measure that reads as a bug.
    /// </para>
    /// </summary>
    private void ResetLayoutToDefault()
    {
        ApplyLayoutXml(_defaultLayoutXml);

        ApplicationSettings.Instance.ShowRibbon = true;
        ApplicationSettings.Instance.ShowMinimap = false;
        SetRibbonVisibility(true);
        SetMinimapVisibility(false);
        ApplicationSettings.Save();

        SetStatus("Layout reset", isError: false);
    }

    private void HelpNav_Click(object sender, RoutedEventArgs e)
    {
        string? targetName = null;

        if (sender is System.Windows.Documents.Hyperlink link)
            targetName = link.Tag as string;
        else if (sender is Button btn)
            targetName = btn.Tag as string;

        if (targetName != null)
        {
            var target = FindName(targetName) as FrameworkElement;
            target?.BringIntoView();
        }
    }


    private void Caret_PositionChanged(object? sender, EventArgs e)
    {
        // Signature help belongs to one argument list. Closing it here — rather than only on the
        // characters that usually end a call — means it cannot survive the caret being moved out by
        // any route: a typed ')' or ';', an arrow key, or a click somewhere else entirely.
        if (_insightWindow != null && !IsCaretInsideArgumentList())
            _insightWindow.Close();

        if (_bracketRenderer == null) return;

        var result = BracketSearcher.SearchBracket(CodeEditor.Document, CodeEditor.CaretOffset);
        _bracketRenderer.Result = result;
        CodeEditor.TextArea.TextView.InvalidateLayer(KnownLayer.Selection);

        // Update breadcrumb navigation
        UpdateBreadcrumb();
    }

    private void UpdateBreadcrumb()
    {
        try
        {
            var text = CodeEditor.Text;
            var offset = CodeEditor.CaretOffset;
            var line = CodeEditor.TextArea.Caret.Line;

            // Find current namespace, class, and method
            var breadcrumbParts = new List<(string Text, string Kind)>();

            // Parse backwards to find enclosing constructs
            var currentNamespace = FindEnclosingConstruct(text, offset, "namespace");
            var currentClass = FindEnclosingConstruct(text, offset, "class");
            var currentMethod = FindEnclosingMethod(text, offset);

            if (!string.IsNullOrEmpty(currentNamespace))
                breadcrumbParts.Add((currentNamespace, "namespace"));

            if (!string.IsNullOrEmpty(currentClass))
                breadcrumbParts.Add((currentClass, "class"));

            if (!string.IsNullOrEmpty(currentMethod))
                breadcrumbParts.Add((currentMethod, "method"));

            // Update UI
            BreadcrumbPanel.Children.Clear();

            if (breadcrumbParts.Count == 0)
            {
                BreadcrumbText.Text = _activeFile?.FileName ?? "Ready";
                BreadcrumbPanel.Children.Add(BreadcrumbText);
            }
            else
            {
                for (int i = 0; i < breadcrumbParts.Count; i++)
                {
                    var (partText, kind) = breadcrumbParts[i];

                    // Add separator
                    if (i > 0)
                    {
                        BreadcrumbPanel.Children.Add(new TextBlock
                        {
                            Text = " > ",
                            Foreground = new SolidColorBrush(Color.FromRgb(128, 128, 128)),
                            VerticalAlignment = VerticalAlignment.Center
                        });
                    }

                    // Color based on kind
                    var color = kind switch
                    {
                        "namespace" => Color.FromRgb(86, 156, 214),   // Blue
                        "class" => Color.FromRgb(78, 201, 176),       // Teal
                        "method" => Color.FromRgb(220, 220, 170),     // Yellow
                        _ => Color.FromRgb(156, 220, 254)             // Light blue
                    };

                    var textBlock = new TextBlock
                    {
                        Text = partText,
                        Foreground = new SolidColorBrush(color),
                        VerticalAlignment = VerticalAlignment.Center,
                        Cursor = System.Windows.Input.Cursors.Hand
                    };

                    BreadcrumbPanel.Children.Add(textBlock);
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"UpdateBreadcrumb error: {ex.Message}");
        }
    }

    private string? FindEnclosingConstruct(string text, int offset, string keyword)
    {
        // Simple regex-based search for enclosing namespace or class
        var pattern = keyword == "namespace"
            ? @"namespace\s+([a-zA-Z_][a-zA-Z0-9_\.]*)"
            : @"(?:class|struct|interface|record)\s+([a-zA-Z_][a-zA-Z0-9_<>,\s]*)";

        var matches = System.Text.RegularExpressions.Regex.Matches(text.Substring(0, Math.Min(offset, text.Length)), pattern);

        // Find the last match that starts before the offset
        string? result = null;
        foreach (System.Text.RegularExpressions.Match match in matches)
        {
            // Check if we're still inside this construct by looking for matching braces
            var constructStart = match.Index;
            var braceDepth = 0;
            var inConstruct = false;

            for (int i = constructStart; i < Math.Min(offset, text.Length); i++)
            {
                if (text[i] == '{')
                {
                    braceDepth++;
                    inConstruct = true;
                }
                else if (text[i] == '}')
                {
                    braceDepth--;
                    if (braceDepth <= 0 && inConstruct)
                    {
                        inConstruct = false;
                        break;
                    }
                }
            }

            if (braceDepth > 0 || !inConstruct)
            {
                var name = match.Groups[1].Value.Trim();
                // Clean up generics
                var angleIndex = name.IndexOf('<');
                if (angleIndex > 0 && keyword != "namespace")
                {
                    name = name.Substring(0, angleIndex);
                }
                result = name;
            }
        }

        return result;
    }

    private string? FindEnclosingMethod(string text, int offset)
    {
        // Find method declarations before the offset
        var pattern = @"(?:public|private|protected|internal|static|async|override|virtual|abstract|\s)+\s+\S+\s+([a-zA-Z_][a-zA-Z0-9_]*)\s*\([^)]*\)\s*(?:where[^{]*)?{";
        var matches = System.Text.RegularExpressions.Regex.Matches(text.Substring(0, Math.Min(offset, text.Length)), pattern);

        string? result = null;
        foreach (System.Text.RegularExpressions.Match match in matches)
        {
            var methodStart = match.Index;
            var braceStart = match.Index + match.Length - 1;
            var braceDepth = 1;

            // Check if we're inside this method's body
            for (int i = braceStart + 1; i < Math.Min(offset, text.Length); i++)
            {
                if (text[i] == '{') braceDepth++;
                else if (text[i] == '}')
                {
                    braceDepth--;
                    if (braceDepth <= 0) break;
                }
            }

            if (braceDepth > 0)
            {
                result = match.Groups[1].Value;
            }
        }

        return result;
    }

    // Flag to prevent clearing multi-selections when AddNextOccurrence changes the selection
    private bool _isAddingNextOccurrence;

    // Flag to suppress marking file as unsaved during programmatic text changes
    private bool _suppressUnsavedMarking;

    private void TextArea_SelectionChanged_ClearMultiSelect(object? sender, EventArgs e)
    {
        // Don't clear if we're in the middle of AddNextOccurrence or multi-cursor editing
        if (_isAddingNextOccurrence || _isMultiCursorEditing) return;

        // Clear multi-selections when user manually changes selection
        _multiSelectionRenderer?.ClearSelections();
    }

    private void OnCodeEditorSelectionChanged(object? sender, EventArgs e)
    {
        if (_selectionHighlightRenderer != null)
        {
            _selectionHighlightRenderer.UpdateSelection(CodeEditor.SelectedText);
        }
    }

    private void TextArea_PreviewMouseDown_ClearMultiSelect(object? sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        // Ctrl+Alt+Click adds a new cursor at the click position
        if (e.LeftButton == System.Windows.Input.MouseButtonState.Pressed &&
            Keyboard.Modifiers == (ModifierKeys.Control | ModifierKeys.Alt))
        {
            // Get the position from mouse click
            var position = CodeEditor.TextArea.TextView.GetPositionFloor(e.GetPosition(CodeEditor.TextArea.TextView));
            if (position.HasValue)
            {
                var offset = CodeEditor.Document.GetOffset(position.Value.Location);

                // Initialize multi-selection renderer if needed, starting from current caret
                if (_multiSelectionRenderer != null)
                {
                    if (!_multiSelectionRenderer.HasSelections)
                    {
                        // Add the current caret position as first cursor
                        _multiSelectionRenderer.AddSelection(CodeEditor.CaretOffset, 0);
                    }
                    // Add new cursor at click position
                    _multiSelectionRenderer.AddSelection(offset, 0);
                }

                e.Handled = true;
                return;
            }
        }

        // Clear multi-selections when user clicks in the text area (without Ctrl+Alt)
        // This handles the case where SelectionChanged doesn't fire (e.g., clicking to place caret)
        if (_multiSelectionRenderer != null && _multiSelectionRenderer.HasSelections)
        {
            _multiSelectionRenderer.ClearSelections();
        }
    }

    // Flag to prevent clearing multi-selections during multi-cursor editing
    private bool _isMultiCursorEditing;

    private void TextArea_TextEntering_MultiCursor(object? sender, System.Windows.Input.TextCompositionEventArgs e)
    {
        // If we have multi-selections and user types, apply to all cursors
        if (_multiSelectionRenderer != null && _multiSelectionRenderer.HasSelections && !string.IsNullOrEmpty(e.Text))
        {
            _isMultiCursorEditing = true;
            _isAddingNextOccurrence = true;
            try
            {
                _multiSelectionRenderer.InsertTextAtAllCursors(e.Text);
                e.Handled = true; // Prevent default handling
            }
            finally
            {
                _isAddingNextOccurrence = false;
                _isMultiCursorEditing = false;
            }
        }
    }

    private void ExportConsoleButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog
        {
            Filter = "Text Files (*.txt)|*.txt",
            DefaultExt = ".txt",
            FileName = "console_output"
        };

        if (dialog.ShowDialog() == true)
        {
            try
            {
                File.WriteAllText(dialog.FileName, Console.ConsoleOutput.Instance.GetDisplayedOutput());
                SetStatus($"Console exported: {Path.GetFileName(dialog.FileName)}", isError: false);
            }
            catch (Exception ex)
            {
                SetStatus($"Export error: {ex.Message}", isError: true);
            }
        }
    }

    private void InitializeEditor()
    {
        // Enable built-in Find/Replace (Ctrl+F / Ctrl+H)
        SearchPanel.Install(CodeEditor);
        
        // Load syntax highlighting
        try
        {
            var assembly = typeof(MainWindow).Assembly;
            var resourceName = "DoodleSharp.Editor.CSharpHighlighting.xshd";
            using var stream = assembly.GetManifestResourceStream(resourceName);

            if (stream != null)
            {
                using var reader = new XmlTextReader(stream);
                CodeEditor.SyntaxHighlighting = HighlightingLoader.Load(reader, HighlightingManager.Instance);
            }
            else
            {
                MessageBox.Show($"Could not find embedded resource '{resourceName}'.\nAvailable resources:\n{string.Join("\n", assembly.GetManifestResourceNames())}", "Resource Error", MessageBoxButton.OK, MessageBoxImage.Error);
                CodeEditor.SyntaxHighlighting = HighlightingManager.Instance.GetDefinition("C#");
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Error loading highlighting: {ex.Message}\n{ex.StackTrace}", "Highlighting Error", MessageBoxButton.OK, MessageBoxImage.Error);
            CodeEditor.SyntaxHighlighting = HighlightingManager.Instance.GetDefinition("C#");
        }

        // Track changes in active file
        CodeEditor.TextChanged += (s, e) =>
        {
            if (_activeFile != null && !_suppressUnsavedMarking)
            {
                _activeFile.HasUnsavedChanges = true;
                RefreshFileTabs();
            }
        };

        // Initialize TextMarkerService
        _textMarkerService = new VizTextMarkerService(CodeEditor.Document);
        CodeEditor.TextArea.TextView.BackgroundRenderers.Add(_textMarkerService);

        CodeEditor.TextArea.TextView.Services.AddService(typeof(VizTextMarkerService), _textMarkerService);
        
        // Initial options
        CodeEditor.Options.ConvertTabsToSpaces = true;
        CodeEditor.Options.IndentationSize = 4;
        
        // Handle KeyDown for shortcuts
        CodeEditor.TextArea.KeyDown += CodeEditor_KeyDown;
        
        // Marker events
        CodeEditor.MouseHover += TextEditor_MouseHover;
        CodeEditor.MouseHoverStopped += TextEditor_MouseHoverStopped;
        
        // Refactoring key binding (Ctrl+.)
        CodeEditor.InputBindings.Add(new KeyBinding(RenameCommand, new KeyGesture(Key.OemPeriod, ModifierKeys.Control)));
        CommandBindings.Add(new CommandBinding(RenameCommand, Rename_Executed));

        // Go to Definition (F12)
        CodeEditor.InputBindings.Add(new KeyBinding(GoToDefinitionCommand, new KeyGesture(Key.F12)));
        CommandBindings.Add(new CommandBinding(GoToDefinitionCommand, GoToDefinition_Executed));

        // Find All References (Shift+F12)
        CodeEditor.InputBindings.Add(new KeyBinding(FindAllReferencesCommand, new KeyGesture(Key.F12, ModifierKeys.Shift)));
        CommandBindings.Add(new CommandBinding(FindAllReferencesCommand, FindAllReferences_Executed));

        // Peek Definition (Alt+F12)
        CodeEditor.InputBindings.Add(new KeyBinding(PeekDefinitionCommand, new KeyGesture(Key.F12, ModifierKeys.Alt)));
        CommandBindings.Add(new CommandBinding(PeekDefinitionCommand, PeekDefinition_Executed));

        // Document Symbols (Ctrl+Shift+O)
        CodeEditor.InputBindings.Add(new KeyBinding(DocumentSymbolsCommand, new KeyGesture(Key.O, ModifierKeys.Control | ModifierKeys.Shift)));
        CommandBindings.Add(new CommandBinding(DocumentSymbolsCommand, DocumentSymbols_Executed));

        // Workspace Symbols (Ctrl+T)
        CodeEditor.InputBindings.Add(new KeyBinding(WorkspaceSymbolsCommand, new KeyGesture(Key.T, ModifierKeys.Control)));
        CommandBindings.Add(new CommandBinding(WorkspaceSymbolsCommand, WorkspaceSymbols_Executed));

        // Call Hierarchy (Ctrl+Shift+H)
        CodeEditor.InputBindings.Add(new KeyBinding(CallHierarchyCommand, new KeyGesture(Key.H, ModifierKeys.Control | ModifierKeys.Shift)));
        CommandBindings.Add(new CommandBinding(CallHierarchyCommand, CallHierarchy_Executed));

        // Type Hierarchy (Ctrl+Shift+T)
        CodeEditor.InputBindings.Add(new KeyBinding(TypeHierarchyCommand, new KeyGesture(Key.T, ModifierKeys.Control | ModifierKeys.Shift)));
        CommandBindings.Add(new CommandBinding(TypeHierarchyCommand, TypeHierarchy_Executed));

        // Direct Rename (F2)
        CodeEditor.InputBindings.Add(new KeyBinding(DirectRenameCommand, new KeyGesture(Key.F2)));
        CommandBindings.Add(new CommandBinding(DirectRenameCommand, DirectRename_Executed));

        // Setup autocomplete
        CodeEditor.TextArea.TextEntered += TextArea_TextEntered;
        CodeEditor.TextArea.TextEntering += TextArea_TextEntering;

        // Subscribe to method completion callback for signature help
        CompletionData.OnMethodCompleted = ShowSignatureHelp;

        // Setup auto-indentation on Enter key
        CodeEditor.TextArea.PreviewKeyDown += TextArea_PreviewKeyDown;

        // Intercept Paste command to support multi-cursor paste
        CodeEditor.TextArea.CommandBindings.Insert(0, new System.Windows.Input.CommandBinding(
            ApplicationCommands.Paste,
            (s, e) =>
            {
                if (_multiSelectionRenderer != null && _multiSelectionRenderer.HasSelections)
                {
                    _isMultiCursorEditing = true;
                    _isAddingNextOccurrence = true;
                    try
                    {
                        _multiSelectionRenderer.PasteAtAllCursors();
                        e.Handled = true;
                    }
                    finally
                    {
                        _isAddingNextOccurrence = false;
                        _isMultiCursorEditing = false;
                    }
                }
                // else: let AvalonEdit's default paste handle it
            },
            (s, e) =>
            {
                if (_multiSelectionRenderer != null && _multiSelectionRenderer.HasSelections)
                    e.CanExecute = true;
                // else: don't handle, let AvalonEdit's default paste take over
            }));

        // Intercept Copy so Ctrl+C gathers the text from EVERY multi-cursor selection
        // (newline-joined, document order) rather than just the main selection.
        CodeEditor.TextArea.CommandBindings.Insert(0, new System.Windows.Input.CommandBinding(
            ApplicationCommands.Copy,
            (s, e) =>
            {
                if (_multiSelectionRenderer != null && _multiSelectionRenderer.HasSelections
                    && _multiSelectionRenderer.CopyAllSelections())
                {
                    e.Handled = true;
                }
                // else: let AvalonEdit's default copy handle it
            },
            (s, e) =>
            {
                if (_multiSelectionRenderer != null && _multiSelectionRenderer.HasSelections)
                    e.CanExecute = true;
            }));

        // Intercept Cut likewise: copy all selections, then delete them at every cursor.
        CodeEditor.TextArea.CommandBindings.Insert(0, new System.Windows.Input.CommandBinding(
            ApplicationCommands.Cut,
            (s, e) =>
            {
                if (_multiSelectionRenderer != null && _multiSelectionRenderer.HasSelections)
                {
                    _isMultiCursorEditing = true;
                    _isAddingNextOccurrence = true;
                    try
                    {
                        if (_multiSelectionRenderer.CutAllSelections())
                            e.Handled = true;
                    }
                    finally
                    {
                        _isAddingNextOccurrence = false;
                        _isMultiCursorEditing = false;
                    }
                }
            },
            (s, e) =>
            {
                if (_multiSelectionRenderer != null && _multiSelectionRenderer.HasSelections)
                    e.CanExecute = true;
            }));

        // Initialize Bracket Highlighting
        _bracketRenderer = new BracketHighlightRenderer(CodeEditor.TextArea.TextView);
        CodeEditor.TextArea.TextView.BackgroundRenderers.Add(_bracketRenderer);
        CodeEditor.TextArea.Caret.PositionChanged += Caret_PositionChanged;

        // Initialize Multi-Selection Highlighting (for Ctrl+D)
        _multiSelectionRenderer = new MultiSelectionRenderer(CodeEditor.TextArea.TextView);
        CodeEditor.TextArea.TextView.BackgroundRenderers.Add(_multiSelectionRenderer);
        CodeEditor.TextArea.SelectionChanged += TextArea_SelectionChanged_ClearMultiSelect;

        // Initialize Selection Highlight Renderer (Draws occurrences of selected text)
        _selectionHighlightRenderer = new SelectionHighlightRenderer(CodeEditor.TextArea.TextView);
        CodeEditor.TextArea.TextView.BackgroundRenderers.Add(_selectionHighlightRenderer);
        CodeEditor.TextArea.SelectionChanged += OnCodeEditorSelectionChanged;
        CodeEditor.TextArea.TextEntering += TextArea_TextEntering_MultiCursor;
        CodeEditor.TextArea.PreviewMouseDown += TextArea_PreviewMouseDown_ClearMultiSelect;

        // Initialize Inlay Hints
        _inlayHintGenerator = new Editor.InlayHintGenerator(CodeEditor.Document);
        CodeEditor.TextArea.TextView.ElementGenerators.Add(_inlayHintGenerator);
        _inlayHintGenerator.Enabled = false; // Disabled by default, can be enabled via menu

        // Initialize Semantic Highlighting
        _semanticHighlighter = new Editor.SemanticHighlighter(CodeEditor.Document);
        CodeEditor.TextArea.TextView.LineTransformers.Add(_semanticHighlighter);
        _semanticHighlighter.Enabled = true; // Enabled by default

        // Timer for debounced semantic highlighting updates
        _semanticUpdateTimer = new DispatcherTimer();
        _semanticUpdateTimer.Interval = TimeSpan.FromMilliseconds(500);
        _semanticUpdateTimer.Tick += async (s, e) =>
        {
            _semanticUpdateTimer.Stop();
            await UpdateSemanticHighlightingAsync();
        };

        // Initialize Code Lens
        _codeLensGenerator = new Editor.CodeLensGenerator(CodeEditor.Document);
        CodeEditor.TextArea.TextView.ElementGenerators.Add(_codeLensGenerator);
        _codeLensGenerator.Enabled = false; // Disabled by default (can be slow)

        // Initialize Folding
        if (_foldingManager == null)
        {
            _foldingManager = FoldingManager.Install(CodeEditor.TextArea);
        }
        _foldingStrategy = new BraceFoldingStrategy();
        
        // Timer for folding updates
        _foldingTimer = new DispatcherTimer();
        _foldingTimer.Interval = TimeSpan.FromSeconds(2);
        _foldingTimer.Tick += (s, e) =>
        {
            try
            {
                UpdateFoldings();
            }
            catch (Exception ex)
            {
                SetStatus($"Folding Error: {ex.Message}", true);
            }
        };
        _foldingTimer.Start();

        // Timer for real-time syntax checking (continuous interval)
        _syntaxCheckTimer = new DispatcherTimer();
        _syntaxCheckTimer.Interval = TimeSpan.FromMilliseconds(800);
        _syntaxCheckTimer.Tick += async (s, e) =>
        {
            if (_textChangedSinceLastCheck && _currentProject != null)
            {
                _textChangedSinceLastCheck = false;
                await PerformSyntaxCheckAsync();
            }
        };
        _syntaxCheckTimer.Start();

        // Track text changes for syntax checking
        CodeEditor.TextChanged += (s, e) => _textChangedSinceLastCheck = true;

        // Auto Save timer (periodic save of unsaved project files)
        _autoSaveTimer = new DispatcherTimer();
        _autoSaveTimer.Tick += AutoSaveTimer_Tick;
        ApplyAutoSaveSettings();

        // Auto-Run timer (periodic re-execution of the code; armed per project)
        _autoRunTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(AutoRunIntervalMs) };
        _autoRunTimer.Tick += AutoRunTimer_Tick;
        ApplyAutoRunSetting();

        // Ctrl+MouseWheel to change font size
        CodeEditor.PreviewMouseWheel += CodeEditor_PreviewMouseWheel;

        // Initialize Minimap
        InitializeMinimap();

        // Clear canvas selection when user clicks into the code editor
        CodeEditor.PreviewMouseDown += (s, e) =>
        {
            if (ViewportHost.SelectedShapes.Count > 0)
            {
                ViewportHost.ClearSelection();
                ViewportHost.Refresh();
                _propertiesPanel?.UpdateSelection(new List<C2VGeometry.Shape>());
            }
        };
    }

    private void CodeEditor_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (Keyboard.Modifiers == ModifierKeys.Control)
        {
            // Ctrl+Wheel: change font size
            var currentSize = CodeEditor.FontSize;
            if (e.Delta > 0)
            {
                // Scroll up: increase font size
                CodeEditor.FontSize = Math.Min(currentSize + 1, 48);
            }
            else
            {
                // Scroll down: decrease font size
                CodeEditor.FontSize = Math.Max(currentSize - 1, 8);
            }
            e.Handled = true;
        }
    }

    #region Autocomplete

    private void TextArea_TextEntering(object sender, TextCompositionEventArgs e)
    {
        // Handle wrap selection with brackets
        if (e.Text.Length == 1 && !CodeEditor.TextArea.Selection.IsEmpty)
        {
            var closingChar = e.Text[0] switch
            {
                '(' => ')',
                '{' => '}',
                '[' => ']',
                '<' => '>',
                '"' => '"',
                '\'' => '\'',
                _ => '\0'
            };

            if (closingChar != '\0')
            {
                WrapSelectionWith(e.Text[0], closingChar);
                e.Handled = true;
                return;
            }
        }

        if (e.Text.Length == 0) return;

        if (_completionWindow != null)
        {
            var ch = e.Text[0];

            // A dot has to restart completion as a member list. The old window is still open and
            // still non-null, so TriggerManualCompletion would early-return; the window then filters
            // itself down to nothing and closes, and no member list ever appears. (This is the
            // "typing a dot after an identifier shows no suggestions" bug.) Close it here, then let
            // TextEntered re-trigger once the dot is actually in the document.
            if (ch == '.')
            {
                _completionWindow.Close();
                return;
            }

            // Space closes the list; it never commits. Committing on space is actively destructive
            // while typing a keyword the list cannot contain — typing `new ` replaced the keyword
            // with the selected type, and `new VXYZ(10, ` replaced the argument with `Viz`. If the
            // space follows a priming keyword, reopen the list for what comes next.
            if (ch == ' ')
            {
                _completionWindow.Close();
                if (IsAfterCompletionKeyword())
                    Dispatcher.BeginInvoke(DispatcherPriority.Input, TriggerManualCompletion);
                return;
            }

            // Commit the highlighted item on a character that ends an identifier *and* implies the
            // user has finished choosing — an opening bracket, a separator, a terminator. Anything
            // else that cannot continue an identifier merely dismisses. See CompletionInteraction
            // for why space is in neither set.
            // A snippet expands into a multi-line construct with placeholders, so a bracket or a
            // separator must not accept it. That only became reachable once snippets sorted first
            // and won the selection: typing `for(` would otherwise expand the whole loop around the
            // parenthesis the user was in the middle of writing. Tab and Enter — handled by
            // AvalonEdit as explicit accept keys — stay the only way to take one.
            var snippetSelected = _completionWindow.CompletionList.SelectedItem is Editor.SnippetCompletionData;

            if (Editor.CompletionInteraction.Commits(ch, snippetSelected))
            {
                _completionWindow.CompletionList.RequestInsertion(e);
            }
            else if (Editor.CompletionInteraction.Dismisses(ch) ||
                     (snippetSelected && Editor.CompletionInteraction.Commits(ch)))
            {
                _completionWindow.Close();
            }

            return;
        }

        // Note: '.' is deliberately NOT handled here. TextEntered fires it once the character is in
        // the document; triggering from both handlers used to start two overlapping completion
        // queries and leave one of the two windows orphaned.
        if (char.IsLetter(e.Text[0]) || e.Text[0] == '_')
        {
            // Auto-popup while typing an identifier, the way Visual Studio does. Fires from the
            // second character so a single stray letter does not open a list, and from the first
            // character after a priming keyword (`new`, `is`, `as`) so `new V` lists types at once.
            // Deferred to Input priority so the character is in the document before we read it.
            Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() =>
            {
                if (_completionWindow != null) return;

                var caret = CodeEditor.CaretOffset;
                bool trigger = caret >= 2 &&
                    (char.IsLetterOrDigit(CodeEditor.Document.GetCharAt(caret - 2)) ||
                     CodeEditor.Document.GetCharAt(caret - 2) == '_');

                if (!trigger)
                    trigger = IsAfterCompletionKeyword(skipBack: 1);

                if (trigger)
                    TriggerManualCompletion();
            }));
        }
        else if (e.Text == " " && IsAfterCompletionKeyword())
        {
            // "new " / "is " / "as " — list candidate types up front rather than waiting for a letter.
            Dispatcher.BeginInvoke(DispatcherPriority.Input, TriggerManualCompletion);
        }
    }

    /// <summary>
    /// True when the caret sits immediately after a completion-priming keyword (<c>new</c>,
    /// <c>is</c>, <c>as</c>), allowing for whitespace.
    /// </summary>
    /// <param name="skipBack">Characters to ignore at the caret tail — 1 when probing from
    /// TextEntering, where the character just typed is already accounted for.</param>
    private bool IsAfterCompletionKeyword(int skipBack = 0)
    {
        var offset = CodeEditor.CaretOffset - skipBack;
        if (offset < 0 || offset > CodeEditor.Document.TextLength) return false;

        // Only the tail matters, so avoid materialising the whole document.
        var start = Math.Max(0, offset - 64);
        var window = CodeEditor.Document.GetText(start, offset - start);

        return Editor.CompletionInteraction.IsPrimingKeyword(
            Editor.CompletionInteraction.WordBefore(window, window.Length));
    }

    private void WrapSelectionWith(char open, char close)
    {
        var selection = CodeEditor.TextArea.Selection;
        var selectedText = selection.GetText();
        var document = CodeEditor.Document;

        var startOffset = selection.SurroundingSegment.Offset;
        var length = selection.SurroundingSegment.Length;

        var wrappedText = $"{open}{selectedText}{close}";
        document.Replace(startOffset, length, wrappedText);

        // Clear selection and position caret after the closing bracket
        CodeEditor.TextArea.ClearSelection();
        CodeEditor.CaretOffset = startOffset + wrappedText.Length;
    }

    private void TextArea_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        // HIGHEST PRIORITY: Tab key for drawing input mode cycling
        // Must intercept here as well since TextArea may handle Tab before MainWindow
        if (e.Key == Key.Tab && RenderCanvas.DrawingTool.Mode != Canvas.DrawingMode.None && RenderCanvas.DrawingTool.Points.Count > 0)
        {
            e.Handled = true;
            if (RenderCanvas.DrawingTool.CycleInputMode())
            {
                ViewportHost.Refresh();
                UpdateDrawingInputStatus();
            }
            return;
        }

        // Handle Backspace/Delete for multi-cursor editing
        if (_multiSelectionRenderer != null && _multiSelectionRenderer.HasSelections)
        {
            if (e.Key == Key.Back)
            {
                _isMultiCursorEditing = true;
                _isAddingNextOccurrence = true;
                try
                {
                    _multiSelectionRenderer.BackspaceAtAllCursors();
                    e.Handled = true;
                }
                finally
                {
                    _isAddingNextOccurrence = false;
                    _isMultiCursorEditing = false;
                }
                return;
            }
            else if (e.Key == Key.Delete)
            {
                _isMultiCursorEditing = true;
                _isAddingNextOccurrence = true;
                try
                {
                    _multiSelectionRenderer.DeleteAtAllCursors();
                    e.Handled = true;
                }
                finally
                {
                    _isAddingNextOccurrence = false;
                    _isMultiCursorEditing = false;
                }
                return;
            }
            else if (e.Key == Key.Escape)
            {
                // Escape clears multi-cursor mode
                _multiSelectionRenderer.ClearSelections();
                e.Handled = true;
                return;
            }
            else if (e.Key == Key.Left)
            {
                _isMultiCursorEditing = true;
                _isAddingNextOccurrence = true;
                try
                {
                    if (Keyboard.Modifiers == (ModifierKeys.Control | ModifierKeys.Shift))
                        _multiSelectionRenderer.ExtendAllSelectionsWordLeft();
                    else if (Keyboard.Modifiers == ModifierKeys.Control)
                        _multiSelectionRenderer.MoveAllCursorsWordLeft();
                    else if (Keyboard.Modifiers == ModifierKeys.Shift)
                        _multiSelectionRenderer.ExtendAllSelectionsLeft();
                    else
                        _multiSelectionRenderer.MoveAllCursorsLeft();
                    e.Handled = true;
                }
                finally
                {
                    _isAddingNextOccurrence = false;
                    _isMultiCursorEditing = false;
                }
                return;
            }
            else if (e.Key == Key.Right)
            {
                _isMultiCursorEditing = true;
                _isAddingNextOccurrence = true;
                try
                {
                    if (Keyboard.Modifiers == (ModifierKeys.Control | ModifierKeys.Shift))
                        _multiSelectionRenderer.ExtendAllSelectionsWordRight();
                    else if (Keyboard.Modifiers == ModifierKeys.Control)
                        _multiSelectionRenderer.MoveAllCursorsWordRight();
                    else if (Keyboard.Modifiers == ModifierKeys.Shift)
                        _multiSelectionRenderer.ExtendAllSelectionsRight();
                    else
                        _multiSelectionRenderer.MoveAllCursorsRight();
                    e.Handled = true;
                }
                finally
                {
                    _isAddingNextOccurrence = false;
                    _isMultiCursorEditing = false;
                }
                return;
            }
            else if (e.Key == Key.Up)
            {
                _isMultiCursorEditing = true;
                _isAddingNextOccurrence = true;
                try
                {
                    _multiSelectionRenderer.MoveAllCursorsUp();
                    e.Handled = true;
                }
                finally
                {
                    _isAddingNextOccurrence = false;
                    _isMultiCursorEditing = false;
                }
                return;
            }
            else if (e.Key == Key.Down)
            {
                _isMultiCursorEditing = true;
                _isAddingNextOccurrence = true;
                try
                {
                    _multiSelectionRenderer.MoveAllCursorsDown();
                    e.Handled = true;
                }
                finally
                {
                    _isAddingNextOccurrence = false;
                    _isMultiCursorEditing = false;
                }
                return;
            }
            else if (e.Key == Key.Home)
            {
                _isMultiCursorEditing = true;
                _isAddingNextOccurrence = true;
                try
                {
                    if (Keyboard.Modifiers == ModifierKeys.Shift)
                        _multiSelectionRenderer.ExtendAllSelectionsHome();
                    else
                        _multiSelectionRenderer.MoveAllCursorsHome();
                    e.Handled = true;
                }
                finally
                {
                    _isAddingNextOccurrence = false;
                    _isMultiCursorEditing = false;
                }
                return;
            }
            else if (e.Key == Key.End)
            {
                _isMultiCursorEditing = true;
                _isAddingNextOccurrence = true;
                try
                {
                    if (Keyboard.Modifiers == ModifierKeys.Shift)
                        _multiSelectionRenderer.ExtendAllSelectionsEnd();
                    else
                        _multiSelectionRenderer.MoveAllCursorsEnd();
                    e.Handled = true;
                }
                finally
                {
                    _isAddingNextOccurrence = false;
                    _isMultiCursorEditing = false;
                }
                return;
            }
            else if (e.Key == Key.Enter)
            {
                _isMultiCursorEditing = true;
                _isAddingNextOccurrence = true;
                try
                {
                    _multiSelectionRenderer.EnterAtAllCursors(AutoIndentMenuItem.IsChecked);
                    e.Handled = true;
                }
                finally
                {
                    _isAddingNextOccurrence = false;
                    _isMultiCursorEditing = false;
                }
                return;
            }
        }

        // Handle Tab for snippet placeholder navigation
        if (e.Key == Key.Tab && _snippetSession != null && _snippetSession.IsActive)
        {
            if (Keyboard.Modifiers == ModifierKeys.Shift)
            {
                // Shift+Tab: previous placeholder
                if (_snippetSession.MoveToPreviousPlaceholder())
                {
                    e.Handled = true;
                    return;
                }
            }
            else if (Keyboard.Modifiers == ModifierKeys.None)
            {
                // Tab: next placeholder
                if (_snippetSession.MoveToNextPlaceholder())
                {
                    e.Handled = true;
                    return;
                }
                // If MoveToNextPlaceholder returns false, session ended - let Tab work normally
            }
        }

        // Handle Escape to cancel snippet session
        if (e.Key == Key.Escape && _snippetSession != null && _snippetSession.IsActive)
        {
            _snippetSession.EndSession();
            e.Handled = true;
            return;
        }

        // Handle Ctrl+Space for manual completion
        if (e.Key == Key.Space && Keyboard.Modifiers == ModifierKeys.Control)
        {
            TriggerManualCompletion();
            e.Handled = true;
            return;
        }

        // Handle Ctrl+Alt+Up/Down for adding cursors (catch before AvalonEdit)
        if (Keyboard.Modifiers == (ModifierKeys.Control | ModifierKeys.Alt))
        {
            // Check both e.Key and e.SystemKey - behavior varies
            var actualKey = e.Key == Key.System ? e.SystemKey : e.Key;
            if (actualKey == Key.Up)
            {
                _multiSelectionRenderer?.AddCursorAbove();
                e.Handled = true;
                return;
            }
            else if (actualKey == Key.Down)
            {
                _multiSelectionRenderer?.AddCursorBelow();
                e.Handled = true;
                return;
            }
        }

        if (e.Key == Key.Enter && !AutoIndentMenuItem.IsChecked)
            return;

        if (e.Key == Key.Enter && Keyboard.Modifiers == ModifierKeys.None)
        {
            e.Handled = HandleAutoIndentEnter();
        }
    }

    /// <summary>
    /// Triggers completion based on context (Ctrl+Space or typing).
    /// Uses the CachedCompilationWorkspace when available for O(1) incremental updates.
    /// </summary>
    private void TriggerManualCompletion() => TriggerCompletion(autoTrigger: true);

    private async void TriggerCompletion(bool autoTrigger)
    {
        try
        {
            // _completionRunning covers the await below: without it, two triggers arriving before the
            // first Roslyn query returns (e.g. the auto-popup racing a Ctrl+Space) would each build a
            // window, and the loser would be orphaned on screen with no way to close it.
            if (_completionWindow != null || _completionRunning)
                return;

            _completionRunning = true;

            var offset = CodeEditor.CaretOffset;
            var code = CodeEditor.Text;

            try
            {
                 List<ICompletionData> completions;
                 bool isAfterNew;
                 string prefix;
                 string? expectedType;

                 if (_completionWorkspace != null && _activeFile != null)
                 {
                     // Use cached workspace for incremental compilation (Phase 1)
                     var fileId = _activeFile.FileName;
                     var service = new Editor.RoslynCompletionService(_completionWorkspace);
                     // The fourth value is the expected type at the caret. The list stays alphabetical —
                     // nothing ranks by it (note 115) — but it decides which row opens highlighted, so
                     // `VXYZ p = new ` puts Tab on VXYZ instead of on whatever the alphabet put first.
                     (completions, isAfterNew, prefix, expectedType) = await service.GetCompletionsAsync(code, offset, _completionWorkspace, fileId);
                 }
                 else
                 {
                     // Fallback: create fresh compilation
                     var service = new Editor.RoslynCompletionService(_compiler.GetReferences());
                     var otherFiles = GetOtherProjectFiles();
                     (completions, isAfterNew, prefix, expectedType) = await service.GetCompletionsAsync(code, offset, otherFiles);
                 }

                 // The Roslyn query is awaited, and the user keeps typing during it. If the caret has
                 // moved on, this result describes a position that no longer exists — showing it pops a
                 // list anchored to stale text, which is how a namespace list appeared after a closing
                 // parenthesis. An explicit Ctrl+Space is never stale, so only auto-triggers bail.
                 if (autoTrigger && CodeEditor.CaretOffset != offset)
                     return;

                 // Nothing to offer. Notably this is how "no completion while naming a new variable"
                 // stays quiet: the service deliberately returns an empty list there, and adding
                 // snippets regardless would put a snippet list over the name being invented.
                 if (completions.Count == 0)
                     return;

                 // Fuzzy-filter (and score, for the match highlighting), then order alphabetically.
                 var sortedCompletions = SortCompletions(completions, prefix);

                 // Snippets are not symbols, so they survive a position where Roslyn resolves nothing —
                 // which is exactly the half-typed state the user is in when they want `for`. Building
                 // the item list before deciding whether to open the window is what makes that work;
                 // gating on the symbol count alone meant a broken statement offered nothing at all.
                 var isMemberAccess = offset > prefix.Length &&
                     code.Length > offset - prefix.Length - 1 &&
                     code[offset - prefix.Length - 1] == '.';

                 var snippets = new List<ICompletionData>();
                 if (!isAfterNew && !isMemberAccess)
                 {
                     foreach (var (trigger, description) in Editor.CodeSnippets.GetAll())
                     {
                         if (!string.IsNullOrEmpty(prefix) &&
                             !trigger.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                             continue;

                         snippets.Add(new Editor.SnippetCompletionData(trigger, description, Editor.CodeSnippets.GetSnippet(trigger)!));
                     }
                 }

                 // A keyword whose spelling a snippet already occupies is pure duplication now that the
                 // snippet sits above it: `for` listed the loop snippet and then the bare `for` keyword
                 // two rows apart, and the keyword row can no longer be reached by ranking or by a commit
                 // character. Only the ~19 keywords that have a snippet are dropped — the rest (`int`,
                 // `var`, `return`, `float`, …) are why keywords are injected at all, since Roslyn's
                 // LookupSymbols returns declared symbols only and without them `for (int` ranked
                 // IntersectionResult first.
                 if (snippets.Count > 0)
                 {
                     var triggers = new HashSet<string>(snippets.Select(s => s.Text), StringComparer.Ordinal);
                     sortedCompletions = sortedCompletions
                         .Where(c => c is not Editor.CompletionData { Kind: Editor.CompletionKind.Keyword } kw
                                     || !triggers.Contains(kw.Text))
                         .ToList();
                 }

                 if (sortedCompletions.Count > 0 || snippets.Count > 0)
                 {
                     // Build the window in a local first and only publish it to the field once it is
                     // actually on screen. The field is the "completion is busy" gate for every other
                     // entry point, so assigning it before Show() means any exception in between (a
                     // sort, a style lookup, a data item) leaves a non-null field for a window that will
                     // never open and never close — killing IntelliSense for the rest of the session.
                     var window = new CompletionWindow(CodeEditor.TextArea);

                     // Explicitly set StartOffset based on the prefix length to fix off-by-one replacement bugs
                     window.StartOffset = offset - prefix.Length;

                     var data = window.CompletionList.CompletionData;

                     // Snippets go first, and the initial selection is CompletionData[0], so a matching
                     // snippet is what Tab inserts. AvalonEdit renders items in insertion order and never
                     // consults Priority for it — appending them left `for`/`foreach` below every
                     // FormatException-shaped type in the list, several scrolls down, which is not a
                     // place a snippet can be discovered, let alone accepted with one key.
                     foreach (var snippet in snippets)
                     {
                         data.Add(snippet);
                     }

                     foreach (var item in sortedCompletions)
                     {
                         data.Add(item);
                     }

                     window.Closed += (s, args) =>
                     {
                         _completionWindow = null;
                         _docSidecar?.Close();
                     };

                     _completionWindow = window;
                     ShowCompletionWindowWithSelection(expectedType);
                 }
            }
            catch (Exception ex)
            {
                 // Leave no half-open window behind; the finally clears the busy gate either way.
                 try { _completionWindow?.Close(); } catch { }
                 _completionWindow = null;
                 Journal.Warn("MW.COMPLETION.FAILED", "Completion query failed", null, ex);
                 System.Diagnostics.Debug.WriteLine($"Completion Error: {ex.Message}");
            }
            finally
            {
                _completionRunning = false;
            }
        }
        catch (Exception ex)
        {
            DoodleSharp.Diagnostics.Journal.Error("MW.EDITOR.TRIGGERCOMPLETION_FAIL", "TriggerCompletion threw", ex);
            SetStatus($"TriggerCompletion failed: {ex.Message}", isError: true);
        }
    }

    /// <summary>
    /// Filters completions to what the typed prefix fuzzy-matches (tagging each with its score and
    /// match positions, which is what the list renders in bold), then orders them alphabetically.
    /// </summary>
    private List<ICompletionData> SortCompletions(List<ICompletionData> completions, string prefix)
    {
        // Score all items with fuzzy matcher and tag match positions
        foreach (var c in completions)
        {
            if (c is Editor.CompletionData cd && !string.IsNullOrEmpty(prefix))
            {
                cd.MatchScore = Editor.FuzzyMatcher.Score(prefix, c.Text);
                cd.MatchPositions = Editor.FuzzyMatcher.GetMatchPositions(prefix, c.Text);
            }
        }

        // Filter: if prefix is non-empty, remove items that don't fuzzy-match
        IEnumerable<ICompletionData> filtered = completions;
        if (!string.IsNullOrEmpty(prefix))
        {
            filtered = completions.Where(c =>
            {
                if (c is Editor.CompletionData cd)
                    return cd.MatchScore != null;
                // SnippetCompletionData: use simple prefix check
                return c.Text.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) ||
                       c.Text.Contains(prefix, StringComparison.OrdinalIgnoreCase);
            });
        }

        // Alphabetical, and nothing else. The list used to be ranked — expected type, fuzzy score
        // band, type-vs-member, scope, then *name length* — which produced an order with no visible
        // rule at all: a member list on a VLine opened End, Flip, Move, Clone, Scale, Start, Divide,
        // Offset, so finding a known member meant reading every row instead of jumping to where the
        // alphabet says it is. A predictable order beats a clever one for a list you scan by eye,
        // and it costs nothing in speed, because the fuzzy prefix filter above has already thrown
        // out everything that does not match what was typed.
        //
        // Snippets are not in this list at all: the caller adds them ahead of these items, which is
        // what keeps them at the top and selected (note 101).
        return filtered
            .OrderBy(c => c.Text, StringComparer.OrdinalIgnoreCase)
            .ThenBy(c => c.Text, StringComparer.Ordinal) // deterministic tie-break for A/a pairs
            .ToList();
    }

    // ---- IntelliSense Infrastructure (Phases 1, 3, 5) ----

    /// <summary>
    /// Initializes (or re-initializes) the CachedCompilationWorkspace with all project files.
    /// Called when a project is loaded or created.
    /// </summary>
    private void InitializeCompletionWorkspace()
    {
        try
        {
            _completionWorkspace = new Editor.CachedCompilationWorkspace(_compiler.GetReferences());
            if (_currentProject != null)
            {
                foreach (var file in _currentProject.Files)
                {
                    _completionWorkspace.UpdateFile(file.FileName, file.Content);
                }

                // Fetch dynamic project references asynchronously (NuGet, Assembly refs)
                _ = UpdateWorkspaceReferencesAsync();
            }

            // Let refactoring/navigation reuse the same warm compilation instead of rebuilding it
            // (and re-running a NuGet restore) on every right-click, F12 or Ctrl+.
            if (_refactoringProvider != null)
                _refactoringProvider.Workspace = _completionWorkspace;
        }
        catch (Exception ex)
        {
            Journal.Warn("MW.WORKSPACE.INIT_FAIL", "IntelliSense workspace failed to initialise", null, ex);
            System.Diagnostics.Debug.WriteLine($"Workspace init error: {ex.Message}");
            _completionWorkspace = null;
            if (_refactoringProvider != null)
                _refactoringProvider.Workspace = null;
        }
    }

    /// <summary>
    /// Reconciles the IntelliSense workspace with the project's current file set: adds files it has
    /// never seen, refreshes their content, and drops files that are gone.
    ///
    /// <para>
    /// The workspace used to be populated only when a project was loaded, so any file created,
    /// deleted or pulled in by the file watcher mid-session was invisible to completion and to
    /// go-to-definition, while deleted files lingered and kept resolving. Call this after any change
    /// to the project's file list.
    /// </para>
    /// </summary>
    private void SyncCompletionWorkspaceFiles()
    {
        if (_completionWorkspace == null || _currentProject == null) return;

        try
        {
            var live = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var file in _currentProject.Files)
            {
                if (string.IsNullOrEmpty(file.FileName)) continue;
                live.Add(file.FileName);

                // The active file's buffer is the truth for it; the rest use their stored content.
                var content = file == _activeFile ? CodeEditor.Text : file.Content;
                _completionWorkspace.UpdateFile(file.FileName, content);
            }

            foreach (var stale in _completionWorkspace.GetFileIds().Where(id => !live.Contains(id)).ToList())
                _completionWorkspace.RemoveFile(stale);
        }
        catch (Exception ex)
        {
            Journal.Warn("MW.WORKSPACE.SYNC_FAIL", "IntelliSense workspace sync failed", null, ex);
        }
    }

    /// <summary>
    /// Asynchronously fetches project-specific references (NuGet packages and assembly references)
    /// and replaces the default references in the completion workspace.
    /// </summary>
    private async Task UpdateWorkspaceReferencesAsync()
    {
        if (_currentProject == null || _completionWorkspace == null) return;
        
        try
        {
            var (references, _) = await _compiler.GetProjectReferencesAndDllsAsync(_currentProject);
            _completionWorkspace.ReplaceReferences(references);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to update workspace references: {ex.Message}");
        }
    }

    /// <summary>
    /// Triggers completion for object initializer properties (Phase 3).
    /// Called after typing '{' when in a 'new Type { }' context.
    /// </summary>
    private async void TriggerObjectInitializerCompletion()
    {
        try
        {
            var code = CodeEditor.Text;
            var position = CodeEditor.CaretOffset;

            // Quick text check: look back for pattern 'new TypeName {'
            var textBefore = position > 30 ? code.Substring(position - 30, 30) : code.Substring(0, position);
            if (!textBefore.Contains("new ")) return;

            // Parse and check if we're in an initializer context
            var syntaxTree = Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree.ParseText(code);
            var root = await syntaxTree.GetRootAsync();

            if (Editor.RoslynCompletionService.IsInObjectInitializer(root, position))
            {
                TriggerManualCompletion();
            }
        }
        catch { /* ignore - best effort */ }
    }

    /// <summary>
    /// Triggers completion for attribute names (Phase 3).
    /// Called after typing '[' at the start of a line or after whitespace.
    /// </summary>
    private async void TriggerAttributeCompletion()
    {
        try
        {
            var code = CodeEditor.Text;
            var position = CodeEditor.CaretOffset;

            var syntaxTree = Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree.ParseText(code);
            var root = await syntaxTree.GetRootAsync();

            if (Editor.RoslynCompletionService.IsInAttributeContext(root, position))
            {
                TriggerManualCompletion();
            }
        }
        catch { /* ignore */ }
    }

    /// <summary>
    /// Triggers completion for generic type arguments (Phase 3).
    /// Called after typing '&lt;' in a generic context like List&lt;|&gt;.
    /// </summary>
    private async void TriggerGenericTypeCompletion()
    {
        try
        {
            var code = CodeEditor.Text;
            var position = CodeEditor.CaretOffset;

            // Quick text check: the character before '<' should be a letter/digit (type name end)
            if (position < 2) return;
            var charBeforeAngle = code[position - 2]; // position-1 is '<', position-2 is char before
            if (!char.IsLetterOrDigit(charBeforeAngle) && charBeforeAngle != '_') return;

            var syntaxTree = Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree.ParseText(code);
            var root = await syntaxTree.GetRootAsync();

            if (Editor.RoslynCompletionService.IsInGenericTypeArgument(root, position))
            {
                TriggerManualCompletion();
            }
        }
        catch { /* ignore */ }
    }

    private bool HandleAutoIndentEnter()
    {
        var document = CodeEditor.Document;
        var offset = CodeEditor.CaretOffset;
        var line = document.GetLineByOffset(offset);
        var lineText = document.GetText(line.Offset, line.Length);

        // Get current indentation
        var currentIndent = GetLineIndentation(lineText);
        var trimmedLine = lineText.Trim();

        // Calculate new indentation
        var newIndent = currentIndent;

        // Increase indent after opening brace
        if (trimmedLine.EndsWith("{"))
        {
            newIndent += "    ";
        }

        // Check if we're between { and } - need to add extra line
        var afterCursor = document.GetText(offset, line.EndOffset - offset).Trim();
        if (trimmedLine.EndsWith("{") && afterCursor.StartsWith("}"))
        {
            // Insert newline + indent + newline + decreased indent + position cursor
            var closingIndent = currentIndent;
            document.Insert(offset, "\n" + newIndent + "\n" + closingIndent);
            CodeEditor.CaretOffset = offset + 1 + newIndent.Length;
            return true;
        }

        // Insert newline with proper indentation
        document.Insert(offset, "\n" + newIndent);
        CodeEditor.CaretOffset = offset + 1 + newIndent.Length;
        return true;
    }

    private static string GetLineIndentation(string line)
    {
        var indent = new System.Text.StringBuilder();
        foreach (var c in line)
        {
            if (c == ' ' || c == '\t')
                indent.Append(c);
            else
                break;
        }
        return indent.ToString();
    }

    private void HandleClosingBraceIndent()
    {
        var document = CodeEditor.Document;
        var offset = CodeEditor.CaretOffset;
        var line = document.GetLineByOffset(offset);
        var lineText = document.GetText(line.Offset, line.Length);

        // Only auto-dedent if the line only contains whitespace before the }
        var textBeforeBrace = lineText.Substring(0, offset - line.Offset - 1);
        if (!string.IsNullOrWhiteSpace(textBeforeBrace))
            return;

        // Find matching opening brace to determine proper indentation
        var matchingIndent = FindMatchingBraceIndent(document.Text, offset - 1);
        if (matchingIndent == null)
            return;

        // Replace the current line's indentation
        var newLineText = matchingIndent + lineText.TrimStart();
        document.Replace(line.Offset, line.Length, newLineText);

        // Position caret after the }
        CodeEditor.CaretOffset = line.Offset + matchingIndent.Length + 1;
    }

    private static string? FindMatchingBraceIndent(string text, int closingBracePos)
    {
        var depth = 1;
        for (int i = closingBracePos - 1; i >= 0; i--)
        {
            var c = text[i];
            if (c == '}')
                depth++;
            else if (c == '{')
            {
                depth--;
                if (depth == 0)
                {
                    // Found matching brace, get its line's indentation
                    var lineStart = text.LastIndexOf('\n', i) + 1;
                    var lineText = text.Substring(lineStart, i - lineStart + 1);
                    return GetLineIndentation(lineText);
                }
            }
        }
        return null;
    }

    private void TextArea_TextEntered(object sender, TextCompositionEventArgs e)
    {
        if (e.Text == ".")
        {
            // Dot completion - show members
            TriggerManualCompletion();
        }
        else if (e.Text == "(")
        {
            // Auto-close parenthesis and show signature help
            AutoInsertClosingBracket(')');
            ShowSignatureHelp();
        }
        else if (e.Text == "{")
        {
            // Auto-close curly brace
            AutoInsertClosingBracket('}');

            // Trigger object initializer completion (Phase 3): new Type { | }
            TriggerObjectInitializerCompletion();
        }
        else if (e.Text == "[")
        {
            // Auto-close square bracket
            AutoInsertClosingBracket(']');

            // Trigger attribute completion (Phase 3): [|]
            TriggerAttributeCompletion();
        }
        else if (e.Text == "<")
        {
            // Auto-close angle bracket only in generic context
            if (ShouldAutoCloseAngleBracket())
            {
                AutoInsertClosingBracket('>');
            }

            // Trigger generic type argument completion (Phase 3): List<|>
            TriggerGenericTypeCompletion();
        }
        else if (e.Text == "\"")
        {
            // Auto-close double quote (if not already closing one)
            AutoInsertClosingQuote('"');
        }
        else if (e.Text == "'")
        {
            // Auto-close single quote (if not already closing one)
            AutoInsertClosingQuote('\'');
        }
        else if (e.Text == ")" || e.Text == "}" || e.Text == "]" || e.Text == ">")
        {
            // Skip over closing bracket if it matches
            SkipOverClosingBracket(e.Text[0]);

            if (e.Text == ")")
            {
                // Close signature help
                _insightWindow?.Close();
            }
            else if (e.Text == "}" && AutoIndentMenuItem.IsChecked)
            {
                // Auto-dedent closing brace
                HandleClosingBraceIndent();
            }
        }
        else if (e.Text == ",")
        {
            // Close existing signature help window if open
            if (_insightWindow != null)
            {
                _insightWindow.Close();
            }

            // Always show signature help when comma is typed inside parentheses
            // Use Dispatcher to ensure the window is fully closed before reopening
            Dispatcher.BeginInvoke(new Action(() =>
            {
                ShowSignatureHelp();
                TriggerManualCompletion();
            }), System.Windows.Threading.DispatcherPriority.Input);
        }
        else if (e.Text == " ")
        {
            // Close completion window when space is typed after keywords
            var offset = CodeEditor.CaretOffset;
            if (offset >= 4)
            {
                var textBefore = CodeEditor.Document.GetText(offset - 4, 3);
                if (textBefore == "new")
                {
                    TriggerManualCompletion();
                }
                else if (textBefore == "var")
                {
                    // Close completion window after 'var '
                    _completionWindow?.Close();
                }
            }
        }
        else if (e.Text == ";")
        {
            // Format on type - format the current line when semicolon is typed
            FormatCurrentLineOnType();
        }
        else if (char.IsLetter(e.Text[0]))
        {
            var offset = CodeEditor.CaretOffset;
            var wordStart = offset - 1;
            while (wordStart > 0 && char.IsLetterOrDigit(CodeEditor.Document.GetCharAt(wordStart - 1)))
            {
                wordStart--;
            }
            var currentWord = CodeEditor.Document.GetText(wordStart, offset - wordStart);
            
            // Type keywords that shouldn't trigger completion for themselves OR for the variable name after them
            var typeKeywords = new[] { "var", "int", "string", "bool", "double", "float", "char", "byte", 
                "short", "long", "decimal", "object", "void" };
            
            // Control flow keywords - no completion for themselves
            var controlKeywords = new[] { "using", "namespace", "class", "struct", 
                "interface", "enum", "return", "if", "else", "while", "for", "foreach", "switch", "case",
                "break", "continue", "try", "catch", "finally", "throw", "public", "private", "protected",
                "internal", "static", "const", "readonly", "virtual", "override", "abstract", "sealed" };
            
            if (typeKeywords.Contains(currentWord) || controlKeywords.Contains(currentWord))
            {
                // Close any existing completion window when a keyword is fully typed
                _completionWindow?.Close();
            }
            else
            {
                // Check if the PREVIOUS word (before current word) is a type keyword
                // This detects "var arc|" or "int count|" patterns
                var prevWordEnd = wordStart;
                // Skip whitespace before current word
                while (prevWordEnd > 0 && char.IsWhiteSpace(CodeEditor.Document.GetCharAt(prevWordEnd - 1)))
                {
                    prevWordEnd--;
                }
                // Find start of previous word
                var prevWordStart = prevWordEnd;
                while (prevWordStart > 0 && char.IsLetterOrDigit(CodeEditor.Document.GetCharAt(prevWordStart - 1)))
                {
                    prevWordStart--;
                }
                
                if (prevWordStart < prevWordEnd)
                {
                    var prevWord = CodeEditor.Document.GetText(prevWordStart, prevWordEnd - prevWordStart);
                    if (typeKeywords.Contains(prevWord))
                    {
                        // User is typing a variable name after a type - don't show completion
                        _completionWindow?.Close();
                        return;
                    }
                }
                
                // Show general completions after typing a letter
                TriggerManualCompletion();
            }
        }
    }

    private void FormatCurrentLineOnType()
    {
        try
        {
            var document = CodeEditor.Document;
            var line = document.GetLineByOffset(CodeEditor.CaretOffset);
            var lineText = document.GetText(line.Offset, line.Length);

            // Only format if the line has actual code (not just whitespace or comments)
            var trimmed = lineText.Trim();
            if (string.IsNullOrEmpty(trimmed) || trimmed.StartsWith("//"))
                return;

            // Format the line
            var formatted = FormatLineForOnType(lineText);

            // Only replace if different
            if (formatted != lineText)
            {
                var caretInLine = CodeEditor.CaretOffset - line.Offset;
                document.Replace(line.Offset, line.Length, formatted);

                // Try to maintain caret position relative to end of line
                var newOffset = line.Offset + Math.Min(caretInLine + (formatted.Length - lineText.Length), formatted.Length);
                CodeEditor.CaretOffset = Math.Max(line.Offset, Math.Min(newOffset, line.Offset + formatted.Length));
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"FormatCurrentLineOnType error: {ex.Message}");
        }
    }

    private string FormatLineForOnType(string line)
    {
        if (string.IsNullOrWhiteSpace(line))
            return line;

        // Preserve leading whitespace (indentation)
        var leadingWhitespace = "";
        var i = 0;
        while (i < line.Length && char.IsWhiteSpace(line[i]))
        {
            leadingWhitespace += line[i];
            i++;
        }

        var content = line.Substring(i).TrimEnd();
        if (string.IsNullOrEmpty(content))
            return line;

        // Basic formatting rules (minimal to avoid breaking code)
        // Add space after keywords
        content = System.Text.RegularExpressions.Regex.Replace(content, @"\b(if|else|for|foreach|while|switch|using|return|throw|new|var|catch|finally)\(", "$1 (");

        // Add space around = but not ==, !=, <=, >=, +=, -=, =>, etc.
        content = System.Text.RegularExpressions.Regex.Replace(content, @"([^=!<>+\-*/%&|^])=(?!>)([^=])", "$1 = $2");

        // Add space after comma (but not inside strings)
        content = System.Text.RegularExpressions.Regex.Replace(content, @",([^\s])", ", $1");

        // Remove space before semicolon
        content = System.Text.RegularExpressions.Regex.Replace(content, @"\s+;", ";");

        // Remove multiple spaces
        content = System.Text.RegularExpressions.Regex.Replace(content, @"  +", " ");

        return leadingWhitespace + content;
    }

    /// <summary>
    /// Turns a diagnostic's line/column span into a document range worth underlining.
    ///
    /// <para>
    /// Roslyn reports "missing token" errors — a dropped <c>;</c>, <c>)</c> or <c>(</c>, an
    /// incomplete expression — as <b>zero-width</b> spans at the point where the token should have
    /// been. There is nothing to underline, so the previous code skipped them entirely, which meant
    /// the most common class of mistake-while-typing produced no squiggle and did not even count
    /// towards the error total: a bare <c>for</c> yields seven diagnostics, every one of them
    /// zero-width, and the file looked clean.
    /// </para>
    /// <para>
    /// An empty span is widened to something visible, in order of preference: the word starting at
    /// the position, then the token immediately before it (the usual case, since the missing token
    /// belongs after what was just typed), then a single character.
    /// </para>
    /// </summary>
    private bool TryGetDiagnosticRange(Microsoft.CodeAnalysis.FileLinePositionSpan lineSpan,
                                       out int offset, out int length)
    {
        try
        {
            return Editor.DiagnosticRange.TryResolve(
                CodeEditor.Document,
                lineSpan.StartLinePosition.Line + 1, lineSpan.StartLinePosition.Character,
                lineSpan.EndLinePosition.Line + 1, lineSpan.EndLinePosition.Character,
                out offset, out length);
        }
        catch
        {
            offset = 0;
            length = 0;
            return false;
        }
    }

    /// <summary>
    /// Performs syntax check and updates error markers.
    /// </summary>
    private async Task PerformSyntaxCheckAsync()
    {
        if (_currentProject == null) return;

        try
        {
            // Sync current editor content
            if (_activeFile != null)
            {
                _activeFile.Content = CodeEditor.Text;
            }

            var result = await _compiler.CheckSyntaxAsync(_currentProject);

            // Clear previous markers
            _textMarkerService?.Clear();

            var totalErrorCount = 0;
            var markerMessages = new Dictionary<(int Offset, int Length), List<string>>();
            var markerColors = new Dictionary<(int Offset, int Length), Color>();

            // Handle C# diagnostics
            if (result.Diagnostics != null)
            {
                foreach (var diagnostic in result.Diagnostics)
                {
                    if (diagnostic.Severity != Microsoft.CodeAnalysis.DiagnosticSeverity.Error &&
                        diagnostic.Severity != Microsoft.CodeAnalysis.DiagnosticSeverity.Warning)
                        continue;

                    var lineSpan = diagnostic.Location.GetLineSpan();

                    // Check if diagnostic belongs to the currently active file
                    var activePath = _activeFile?.FilePath;
                    bool isMatch = false;

                    if (activePath != null)
                    {
                        if (string.IsNullOrEmpty(lineSpan.Path))
                            isMatch = true;
                        else if (string.Equals(lineSpan.Path, activePath, StringComparison.OrdinalIgnoreCase))
                            isMatch = true;
                        else if (string.Equals(Path.GetFileName(lineSpan.Path), Path.GetFileName(activePath), StringComparison.OrdinalIgnoreCase))
                            isMatch = true;
                    }

                    if (isMatch)
                    {
                        // Count first, and unconditionally: whether a squiggle can be drawn is a
                        // rendering question, not a "was there an error" question.
                        if (diagnostic.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error)
                            totalErrorCount++;

                        if (TryGetDiagnosticRange(lineSpan, out var offset, out var length))
                        {
                            var color = diagnostic.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error ? Colors.Red : Colors.Orange;

                            // A single mistake produces a burst of diagnostics at one spot — a bare
                            // `for` yields seven — so merge them into one marker instead of stacking
                            // seven overlapping squiggles with seven separate tooltips.
                            var key = (offset, length);
                            if (markerMessages.TryGetValue(key, out var existing))
                            {
                                existing.Add(diagnostic.GetMessage());
                            }
                            else
                            {
                                markerMessages[key] = new List<string> { diagnostic.GetMessage() };
                                markerColors[key] = color;
                            }

                            // Errors win over warnings when both land on the same range.
                            if (diagnostic.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error)
                                markerColors[key] = color;
                        }
                    }
                }

                foreach (var ((offset, length), messages) in markerMessages)
                {
                    _textMarkerService?.Create(offset, length,
                        string.Join(Environment.NewLine, messages.Distinct()), markerColors[(offset, length)]);
                }
            }

            // Update inlay hints
            if (_inlayHintGenerator != null && _inlayHintGenerator.Enabled)
            {
                _inlayHintGenerator.UpdateHints(CodeEditor.Text);
                CodeEditor.TextArea.TextView.Redraw();
            }

            // Trigger semantic highlighting update (debounced)
            TriggerSemanticHighlightingUpdate();

            // Update Code Lens (debounced - done via semantic timer)
            UpdateCodeLens();

            // Update status bar with error count or clear it
            if (totalErrorCount > 0)
            {
                SetStatus($"{totalErrorCount} error{(totalErrorCount != 1 ? "s" : "")}", isError: true);
            }
            else
            {
                SetStatus("Ready", isError: false);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Syntax check error: {ex.Message}");
        }
    }

    /// <summary>
    /// Inserts a closing bracket at the current cursor position without moving the cursor.
    /// </summary>
    private void AutoInsertClosingBracket(char closingBracket)
    {
        var offset = CodeEditor.CaretOffset;

        // Don't auto-close if the next character is already the closing bracket
        if (offset < CodeEditor.Document.TextLength)
        {
            var nextChar = CodeEditor.Document.GetCharAt(offset);
            if (nextChar == closingBracket)
                return;
        }

        // Don't auto-close if next char is a letter/digit (likely not wanting auto-close)
        if (offset < CodeEditor.Document.TextLength)
        {
            var nextChar = CodeEditor.Document.GetCharAt(offset);
            if (char.IsLetterOrDigit(nextChar))
                return;
        }

        CodeEditor.Document.Insert(offset, closingBracket.ToString());
        CodeEditor.CaretOffset = offset; // Keep cursor before the closing bracket
    }

    /// <summary>
    /// Inserts a closing quote, handling the case where we might be closing an existing quote.
    /// </summary>
    private void AutoInsertClosingQuote(char quote)
    {
        var offset = CodeEditor.CaretOffset;

        // Check if we just closed a quote (typed quote after existing content)
        // Count quotes before cursor to determine if we're in a string
        var textBefore = CodeEditor.Document.GetText(0, offset);
        var quoteCount = 0;
        var escaped = false;

        for (int i = 0; i < textBefore.Length - 1; i++) // -1 because we just typed the quote
        {
            if (textBefore[i] == '\\' && !escaped)
            {
                escaped = true;
                continue;
            }
            if (textBefore[i] == quote && !escaped)
            {
                quoteCount++;
            }
            escaped = false;
        }

        // If odd number of quotes, we just closed a string - don't auto-insert
        if (quoteCount % 2 == 1)
            return;

        // Don't auto-close if next character is already the same quote
        if (offset < CodeEditor.Document.TextLength)
        {
            var nextChar = CodeEditor.Document.GetCharAt(offset);
            if (nextChar == quote)
                return;
        }

        // Don't auto-close if next char is a letter/digit
        if (offset < CodeEditor.Document.TextLength)
        {
            var nextChar = CodeEditor.Document.GetCharAt(offset);
            if (char.IsLetterOrDigit(nextChar))
                return;
        }

        CodeEditor.Document.Insert(offset, quote.ToString());
        CodeEditor.CaretOffset = offset;
    }

    /// <summary>
    /// Checks if we should auto-close angle bracket (for generics, not comparisons).
    /// </summary>
    private bool ShouldAutoCloseAngleBracket()
    {
        var offset = CodeEditor.CaretOffset;
        if (offset < 2)
            return false;

        // Look at what's before the '<' to determine if it's likely a generic
        var charBefore = CodeEditor.Document.GetCharAt(offset - 2);

        // If preceded by a letter (likely a type name), it's probably a generic
        if (char.IsLetter(charBefore) || charBefore == '_')
        {
            // Additional check: find the identifier before '<'
            var start = offset - 2;
            while (start > 0 && (char.IsLetterOrDigit(CodeEditor.Document.GetCharAt(start - 1)) || CodeEditor.Document.GetCharAt(start - 1) == '_'))
            {
                start--;
            }

            var identifier = CodeEditor.Document.GetText(start, offset - 1 - start);

            // Common generic type names
            var genericTypes = new[] { "List", "Dictionary", "HashSet", "Queue", "Stack",
                "IEnumerable", "IList", "ICollection", "IDictionary", "ISet",
                "Action", "Func", "Task", "Nullable", "Lazy", "Tuple", "ValueTuple",
                "KeyValuePair", "Span", "Memory", "ReadOnlySpan", "ReadOnlyMemory" };

            if (genericTypes.Any(t => identifier.EndsWith(t)))
                return true;

            // If it looks like a type name (starts with uppercase), probably generic
            if (identifier.Length > 0 && char.IsUpper(identifier[0]))
                return true;
        }

        return false;
    }

    /// <summary>
    /// If the character after cursor matches the typed closing bracket, skip over it instead of duplicating.
    /// </summary>
    private void SkipOverClosingBracket(char closingBracket)
    {
        var offset = CodeEditor.CaretOffset;

        // Check if we just typed a closing bracket and there's another one right after
        if (offset < CodeEditor.Document.TextLength)
        {
            var nextChar = CodeEditor.Document.GetCharAt(offset);
            if (nextChar == closingBracket)
            {
                // Delete the duplicate we just typed and move past the existing one
                CodeEditor.Document.Remove(offset - 1, 1);
                CodeEditor.CaretOffset = offset;
            }
        }
    }

    private void ShowCompletionWindow()
    {
        // Triggered by Ctrl+Space. An explicit request always re-queries: closing any list that is
        // already up clears the guard in TriggerManualCompletion, which would otherwise make
        // Ctrl+Space appear to do nothing whenever a stale or filtered-down list was still open.
        if (_completionWindow != null)
        {
            _completionWindow.Close();
            _completionWindow = null;
        }

        TriggerCompletion(autoTrigger: false);
    }

    private string GetAllProjectCode()
    {
        if (_currentProject == null)
            return CodeEditor.Text;

        // Make sure current editor content is synced
        SaveCurrentEditorContent();

        return string.Join("\n\n", _currentProject.Files.Select(f => f.Content));
    }

    // Legacy methods removed


    // Legacy inference methods removed

    /// <summary>
    /// Gets the content of all project files except the current active file.
    /// Used for multi-file Roslyn analysis (important for 'var' type inference).
    /// </summary>
    private IEnumerable<string> GetOtherProjectFiles()
    {
        if (_currentProject == null || _activeFile == null)
            return Enumerable.Empty<string>();

        SaveCurrentEditorContent();
        
        return _currentProject.Files
            .Where(f => f != _activeFile)
            .Select(f => f.Content)
            .Where(c => !string.IsNullOrWhiteSpace(c));
    }

    private static int FindDottedIdentifierStart(TextDocument document, int offset)
    {
        var start = offset;
        while (start > 0)
        {
            var c = document.GetCharAt(start - 1);
            if (!char.IsLetterOrDigit(c) && c != '_' && c != '.')
                break;
            start--;
        }
        return start;
    }

    /// <summary>
    /// True when the caret sits inside an unclosed argument list on the current statement — the only
    /// place signature help means anything.
    /// </summary>
    /// <remarks>
    /// Deliberately a cheap bracket scan rather than a parse: it runs after every awaited signature
    /// query and on every caret move, and the text around the caret is usually mid-edit anyway.
    /// Scanning stops at a statement terminator so a previous line's parentheses cannot keep the
    /// tooltip alive.
    /// </remarks>
    private bool IsCaretInsideArgumentList()
    {
        var document = CodeEditor.Document;
        int depth = 0;

        for (int i = CodeEditor.CaretOffset - 1; i >= 0; i--)
        {
            char c = document.GetCharAt(i);

            if (c == ')') depth++;
            else if (c == '(')
            {
                if (depth == 0) return true;   // an unclosed '(' to our left
                depth--;
            }
            else if (c == ';' || c == '{' || c == '}' || c == '\n')
            {
                return false;                  // left the statement
            }
        }

        return false;
    }

    private async void ShowSignatureHelp()
    {
        try
        {
            if (_insightWindow != null)
                return;

            var offset = CodeEditor.CaretOffset;
            var code = CodeEditor.Text;

            try
            {
                 // Use the live workspace when available so methods declared in other project files
                 // resolve; the single-file path only ever sees the tab being typed in.
                 List<string> signatures;
                 int currentParamIndex;

                 if (_completionWorkspace != null && _activeFile != null)
                 {
                     var service = new Editor.RoslynCompletionService(_completionWorkspace);
                     (signatures, currentParamIndex) = await service.GetSignatureHelpAsync(
                         code, offset, _completionWorkspace, _activeFile.FileName);
                 }
                 else
                 {
                     var service = new Editor.RoslynCompletionService(_compiler.GetReferences());
                     (signatures, currentParamIndex) = await service.GetSignatureHelpAsync(code, offset);
                 }

                 if (signatures.Count == 0)
                     return;

                 // The Roslyn query is awaited, and typing continues during it. Without this guard the
                 // window opens for a call the caret has already left — which is how signature help
                 // stayed on screen after the closing parenthesis and the semicolon. The comma handler
                 // makes it worse by reopening on a Dispatcher callback, which can land after the ')'.
                 if (CodeEditor.CaretOffset != offset || !IsCaretInsideArgumentList())
                     return;

                 _insightWindow = new OverloadInsightWindow(CodeEditor.TextArea);
                 _insightWindow.Provider = new SignatureHelpProvider(signatures, currentParamIndex);
             
                 // Try to find reasonable start/end offsets for the window logic (optional)
                 // Simple approach: Current cursor
                 _insightWindow.StartOffset = offset;
                 _insightWindow.EndOffset = CodeEditor.Document.TextLength;

                 StyleInsightWindow(_insightWindow);
                 _insightWindow.Show();
                 _insightWindow.Closed += (s, e) => _insightWindow = null;
            }
            catch (Exception ex)
            {
                 System.Diagnostics.Debug.WriteLine($"ShowSignatureHelp error: {ex}");
            }
        }
        catch (Exception ex)
        {
            DoodleSharp.Diagnostics.Journal.Error("MW.EDITOR.SHOWSIGNATUREHELP_FAIL", "ShowSignatureHelp threw", ex);
            SetStatus($"ShowSignatureHelp failed: {ex.Message}", isError: true);
        }
    }

    private int FindClosingParenthesis(int fromOffset)
    {
        var document = CodeEditor.Document;
        var depth = 1;

        for (int i = fromOffset; i < document.TextLength; i++)
        {
            var c = document.GetCharAt(i);
            if (c == '(')
            {
                depth++;
            }
            else if (c == ')')
            {
                depth--;
                if (depth == 0)
                    return i;
            }
        }
        return -1;
    }

    /// <summary>
    /// Finds the position of the opening parenthesis for the current method call.
    /// Handles nested parentheses correctly.
    /// </summary>
    private int FindOpeningParenthesis(int fromOffset)
    {
        var document = CodeEditor.Document;
        var depth = 0;

        for (int i = fromOffset - 1; i >= 0; i--)
        {
            var c = document.GetCharAt(i);
            if (c == ')')
            {
                depth++;
            }
            else if (c == '(')
            {
                if (depth == 0)
                    return i;
                depth--;
            }
            else if (c == ';' || c == '{' || c == '}')
            {
                // Stop searching at statement boundaries
                return -1;
            }
        }

        return -1;
    }

    /// <summary>
    /// Counts the number of commas between two positions to determine current parameter index.
    /// Handles nested parentheses and strings.
    /// </summary>
    private int CountCommasBeforeCursor(int startOffset, int endOffset)
    {
        var document = CodeEditor.Document;
        var count = 0;
        var parenDepth = 0;
        var inString = false;
        var inChar = false;

        for (int i = startOffset; i < endOffset && i < document.TextLength; i++)
        {
            var c = document.GetCharAt(i);
            var prev = i > 0 ? document.GetCharAt(i - 1) : '\0';

            // Handle escape sequences
            if ((inString || inChar) && prev == '\\')
                continue;

            // Toggle string state
            if (c == '"' && !inChar)
            {
                inString = !inString;
                continue;
            }

            // Toggle char state
            if (c == '\'' && !inString)
            {
                inChar = !inChar;
                continue;
            }

            if (inString || inChar)
                continue;

            // Track parenthesis depth
            if (c == '(') parenDepth++;
            else if (c == ')') parenDepth--;
            else if (c == ',' && parenDepth == 0)
                count++;
        }

        return count;
    }

    private List<string> GetMethodSignatures(string fullName)
    {
        // Split into type and method name
        var lastDot = fullName.LastIndexOf('.');
        if (lastDot < 0)
        {
            // Could be a local method or a type (for static methods)
            return TypeInspector.GetMethodSignatures(fullName, fullName);
        }

        var typePart = fullName.Substring(0, lastDot);
        var methodName = fullName.Substring(lastDot + 1);

        // Try to resolve the type
        var allCode = GetAllProjectCode();
        var textBefore = CodeEditor.Document.GetText(0, CodeEditor.CaretOffset);

        // Check if typePart is a variable
        var actualType = null as string; // Legacy logic disabled
        if (actualType != null)
        {
            return TypeInspector.GetMethodSignatures(actualType, methodName);
        }

        // typePart could be a type name or namespace.type
        return TypeInspector.GetMethodSignatures(typePart, methodName);
    }

    private void StyleCompletionWindow(CompletionWindow window)
    {
        try
        {
            // VS-like dark background
            var darkBg = new SolidColorBrush(Color.FromRgb(30, 30, 30));
            var borderColor = new SolidColorBrush(Color.FromRgb(60, 60, 60));
            
            // Use application theme resources with fallback
            if (FindResource("SecondaryBackgroundBrush") is Brush bg)
            {
                window.Background = bg;
                window.CompletionList.Background = bg;
            }
            else
            {
                window.Background = darkBg;
                window.CompletionList.Background = darkBg;
            }
            
            if (FindResource("BorderBrush") is Brush border)
            {
                window.BorderBrush = border;
            }
            else
            {
                window.BorderBrush = borderColor;
            }
            
            if (FindResource("ForegroundBrush") is Brush fg)
            {
                window.Foreground = fg;
                window.CompletionList.Foreground = fg;
            }
            else
            {
                window.CompletionList.Foreground = Brushes.White;
            }

            // Style the ListBox for VS-like selection highlighting
            var listBox = window.CompletionList.ListBox;
            if (listBox != null)
            {
                // VS uses a subtle blue highlight for selection
                var selectionBrush = new SolidColorBrush(Color.FromRgb(51, 51, 52));
                var hoverBrush = new SolidColorBrush(Color.FromRgb(45, 45, 48));
                
                listBox.Background = window.CompletionList.Background;
                listBox.BorderThickness = new Thickness(0);
                
                // Apply item container style for better selection visuals
                var itemStyle = new Style(typeof(ListBoxItem));
                itemStyle.Setters.Add(new Setter(ListBoxItem.PaddingProperty, new Thickness(4, 2, 4, 2)));
                itemStyle.Setters.Add(new Setter(ListBoxItem.MarginProperty, new Thickness(0)));
                itemStyle.Setters.Add(new Setter(ListBoxItem.BorderThicknessProperty, new Thickness(0)));
                
                // Selection trigger
                var selectedTrigger = new Trigger { Property = ListBoxItem.IsSelectedProperty, Value = true };
                selectedTrigger.Setters.Add(new Setter(ListBoxItem.BackgroundProperty, new SolidColorBrush(Color.FromRgb(0, 122, 204))));
                selectedTrigger.Setters.Add(new Setter(ListBoxItem.ForegroundProperty, Brushes.White));
                itemStyle.Triggers.Add(selectedTrigger);
                
                // Hover trigger (not selected)
                var hoverTrigger = new MultiTrigger();
                hoverTrigger.Conditions.Add(new Condition(ListBoxItem.IsMouseOverProperty, true));
                hoverTrigger.Conditions.Add(new Condition(ListBoxItem.IsSelectedProperty, false));
                hoverTrigger.Setters.Add(new Setter(ListBoxItem.BackgroundProperty, hoverBrush));
                itemStyle.Triggers.Add(hoverTrigger);
                
                listBox.ItemContainerStyle = itemStyle;
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"StyleCompletionWindow error: {ex.Message}");
            // Fallback
            var darkBg = new SolidColorBrush(Color.FromRgb(37, 37, 38));
            window.Background = darkBg;
            window.CompletionList.Background = darkBg;
            window.CompletionList.Foreground = Brushes.White;
        }

        window.BorderThickness = new Thickness(1);

        // Better sizing for VS-like appearance - auto-size to content
        window.Width = double.NaN;
        window.Height = double.NaN;
        window.MinWidth = 350;
        window.MaxWidth = 700;
        window.MaxHeight = 400;
        window.SizeToContent = SizeToContent.WidthAndHeight;
    }

    /// <summary>
    /// Shows the completion window with a row selected, and attaches the documentation sidecar.
    ///
    /// <para>
    /// The row is the first one unless <paramref name="expectedType"/> names an item in the list —
    /// see <see cref="Editor.CompletionPreselect"/> for why the selection is context-aware while the
    /// order stays alphabetical.
    /// </para>
    /// </summary>
    private void ShowCompletionWindowWithSelection(string? expectedType = null)
    {
        if (_completionWindow == null)
            return;

        if (_completionWindow.CompletionList.CompletionData.Count == 0)
        {
            // Nothing to show. Clearing the field matters: it is the gate every other trigger
            // checks, and a window that was never shown will never raise Closed to clear it.
            _completionWindow = null;
            return;
        }

        StyleCompletionWindow(_completionWindow);

        // Select the row the caret is actually about: the expected type where there is one, the
        // first row otherwise (note 122).
        var preselect = Editor.CompletionPreselect.IndexOf(_completionWindow.CompletionList.CompletionData, expectedType);
        var preselectedItem = preselect >= 0 ? _completionWindow.CompletionList.CompletionData[preselect] : null;
        if (preselectedItem != null)
            _completionWindow.CompletionList.SelectedItem = preselectedItem;

        // If signature help is visible, offset the completion window below it
        if (_insightWindow != null)
        {
            _completionWindow.Loaded += (s, e) =>
            {
                // Get the insight window's actual height and add offset
                var insightHeight = _insightWindow?.ActualHeight ?? 30;
                _completionWindow.Top += insightHeight + 2;
            };
        }

        // Close window if it becomes empty after filtering
        _completionWindow.CompletionList.ListBox.Items.CurrentChanged += (s, e) =>
        {
            if (_completionWindow != null &&
                _completionWindow.CompletionList.ListBox.Items.Count == 0)
            {
                _completionWindow.Close();
            }
        };

        // Documentation sidecar (Phase 4)
        _docSidecar = new Editor.DocumentationSidecar();
        _docSidecar.TrackCompletionWindow(_completionWindow);

        // Show docs when selection changes
        _completionWindow.CompletionList.ListBox.SelectionChanged += (s, e) =>
        {
            var selectedItem = _completionWindow?.CompletionList.SelectedItem as Editor.CompletionData;
            if (selectedItem?.Symbol != null)
            {
                _docSidecar?.ShowForItem(selectedItem);
            }
            else
            {
                _docSidecar?.Hide();
            }
        };

        // Show initial selection's docs after the window renders, and bring the selected row into
        // view. Setting SelectedItem highlights a row but never scrolls to it, so a preselected type
        // several hundred rows down was selected off-screen and the list still looked like it had
        // opened on AccessViolationException.
        _completionWindow.Loaded += (s, e) =>
        {
            _docSidecar?.UpdatePosition();
            if (preselectedItem != null)
                _completionWindow?.CompletionList.ListBox?.ScrollIntoView(preselectedItem);
            var initialItem = _completionWindow?.CompletionList.SelectedItem as Editor.CompletionData;
            if (initialItem?.Symbol != null)
                _docSidecar?.ShowForItem(initialItem);
        };

        // The Closed handler that clears _completionWindow is attached by the caller, before the
        // field is published — attaching a second one here would double-close the doc sidecar.
        _completionWindow.Show();
    }

    private void StyleInsightWindow(OverloadInsightWindow window)
    {
        try
        {
            if (FindResource("SecondaryBackgroundBrush") is Brush bg)
                window.Background = bg;
            
            if (FindResource("BorderBrush") is Brush border)
                window.BorderBrush = border;
            
            if (FindResource("ForegroundBrush") is Brush fg)
                window.Foreground = fg;
        }
        catch
        {
             // Fallback
             window.Background = new SolidColorBrush(Color.FromRgb(37, 37, 38));
             window.BorderBrush = new SolidColorBrush(Color.FromRgb(63, 63, 70));
             window.Foreground = Brushes.White;
        }

        window.BorderThickness = new Thickness(1);
        
        // Ensure good sizing for signature help
        window.Width = double.NaN;
        window.MinWidth = 500;
        window.MaxWidth = 800; // Allow sufficient width for long signatures
    }

    #endregion

    private void InitializeCommands()
    {
        PreviewKeyDown += MainWindow_PreviewKeyDown;
    }

    #region Project Management



    private void LoadProject(string projectFilePath)
    {
        using var scope = Journal.Scope("MW.PROJECT.LOAD", "Loading project into the window",
            $"path={projectFilePath}");
        try
        {
            // Stop existing watcher
            StopProjectWatcher();

            // Global parameters belong to the project that declared them; the incoming project
            // re-declares its own on first run.
            C2VGeometry.GlobalParameters.ClearAll();
            ModuleCompiler.InvalidateResident();

            _currentProject = VizCodeProject.Load(projectFilePath);
            LoadProjectTree();
            RefreshFileTabs();

            var fileToSelect = _currentProject.EntryPointFile ?? _currentProject.Files.FirstOrDefault();
            if (fileToSelect != null)
            {
                SelectFile(fileToSelect);
            }

            // Start watching for external changes
            StartProjectWatcher(_currentProject.ProjectDirectory);

            // Add to recent projects
            Project.RecentProjectsManager.AddProject(projectFilePath, _currentProject.ProjectFile.Name);

            SetStatus($"Loaded project: {_currentProject.Files.Count} file(s)", isError: false);
            LoadSettingsToUI();

            // Initialize cached compilation workspace for IntelliSense
            InitializeCompletionWorkspace();
        }
        catch (Exception ex)
        {
            Journal.Error("MW.PROJECT.LOAD_FAIL", "Project load failed", ex, $"path={projectFilePath}");
            SetStatus($"Error loading project: {ex.Message}", isError: true);
        }
    }

    private void RefreshFileTabs()
    {
        var selectedFile = _activeFile;
        FileTabs.ItemsSource = null;
        FileTabs.ItemsSource = _currentProject?.Files.Where(f => f.IsOpen).ToList();

        if (selectedFile != null && selectedFile.IsOpen)
        {
            FileTabs.SelectedItem = selectedFile;
        }
    }

    private void SelectFile(VizCodeFile file)
    {
        // A file becoming active in the editor is the "file opened" event the journal is expected to
        // record — with the content hash, so the crash can be tied to an exact revision of the source.
        Journal.Info("MW.FILE.SELECT", "File opened in the editor",
            Journal.DescribeFile(file.FilePath, file.Content) + $" kind={file.Kind} dirty={file.HasUnsavedChanges}");

        // Save current editor content before switching
        var previous = _activeFile;
        SaveCurrentEditorContent();

        // Push the outgoing file's final text into the workspace now. Semantic updates are debounced
        // by 500 ms, so switching tabs quickly used to leave the previous file's last edits invisible
        // to completion and go-to-definition until it was reopened and touched again.
        if (previous != null && previous != file && !string.IsNullOrEmpty(previous.FileName))
            _completionWorkspace?.UpdateFile(previous.FileName, previous.Content);

        _activeFile = file;

        // Suppress unsaved marking when loading file content
        _suppressUnsavedMarking = true;
        CodeEditor.Text = file.Content;
        _suppressUnsavedMarking = false;

        UpdateSyntaxHighlighting(file.FileName);

        // Select the tab without triggering SelectionChanged recursively
        if (FileTabs.SelectedItem != file)
        {
            FileTabs.SelectedItem = file;
        }
    }

    private void UpdateSyntaxHighlighting(string fileName)
    {
        try
        {
            var assembly = typeof(MainWindow).Assembly;
            using var stream = assembly.GetManifestResourceStream("DoodleSharp.Editor.CSharpHighlighting.xshd");

            if (stream != null)
            {
                using var reader = new XmlTextReader(stream);
                CodeEditor.SyntaxHighlighting = HighlightingLoader.Load(reader, HighlightingManager.Instance);
            }
            else
            {
                CodeEditor.SyntaxHighlighting = HighlightingManager.Instance.GetDefinition("C#");
            }
        }
        catch
        {
            CodeEditor.SyntaxHighlighting = HighlightingManager.Instance.GetDefinition("C#");
        }
    }

    private void SaveCurrentEditorContent()
    {
        if (_activeFile != null && CodeEditor.Text != _activeFile.Content)
        {
            _activeFile.Content = CodeEditor.Text;
        }
    }

    private bool PromptSaveChanges()
    {
        if (_currentProject == null)
            return true;

        SaveCurrentEditorContent();

        var unsavedFiles = _currentProject.Files.Where(f => f.HasUnsavedChanges).ToList();
        if (unsavedFiles.Count == 0)
            return true;

        var result = MessageBox.Show(
            $"You have {unsavedFiles.Count} unsaved file(s). Save changes?",
            "Unsaved Changes",
            MessageBoxButton.YesNoCancel,
            MessageBoxImage.Question);

        if (result == MessageBoxResult.Cancel)
            return false;

        if (result == MessageBoxResult.Yes)
        {
            // If project is in temp directory, prompt for save location
            if (_currentProject.ProjectDirectory.StartsWith(Path.GetTempPath()))
            {
                if (!SaveProjectToNewLocation())
                    return false;
            }
            else
            {
                _currentProject.SaveAllFiles();
            }
        }

        return true;
    }

    /// <summary>
    /// Applies the current Auto Save settings to the timer: restarts it on the configured
    /// interval when enabled, stops it when disabled. Safe to call before the timer exists.
    /// </summary>
    private void ApplyAutoSaveSettings()
    {
        if (_autoSaveTimer == null)
            return;

        _autoSaveTimer.Stop();

        var settings = ApplicationSettings.Instance;
        if (!settings.AutoSaveEnabled)
            return;

        var seconds = Math.Clamp(settings.AutoSaveIntervalSeconds, MinAutoSaveSeconds, MaxAutoSaveSeconds);
        _autoSaveTimer.Interval = TimeSpan.FromSeconds(seconds);
        _autoSaveTimer.Start();
    }

    /// <summary>
    /// Brings the Auto-Run checkbox and its timer in line with the current project. Called after every
    /// settings load, which is also every project open, so it needs no hook into the several places
    /// <c>_currentProject</c> is assigned.
    ///
    /// <para>
    /// The checkbox is written here rather than by the handler, so it is assigned only while
    /// <see cref="_loadingSettings"/> is up and the handler is suppressed — otherwise loading a project
    /// would write the loaded value straight back and save the project file for nothing.
    /// </para>
    /// </summary>
    private void ApplyAutoRunSetting()
    {
        if (_autoRunTimer == null || AutoRunCheck == null) return;

        var enabled = _currentProject?.ProjectFile.Settings.AutoRun == true;

        // Auto-Run is a project setting, so with no project there is nothing to arm.
        AutoRunCheck.IsEnabled = _currentProject != null;
        if (AutoRunCheck.IsChecked != enabled)
        {
            var wasLoading = _loadingSettings;
            _loadingSettings = true;
            try { AutoRunCheck.IsChecked = enabled; }
            finally { _loadingSettings = wasLoading; }
        }

        // Dropped because this runs on every settings load, i.e. every project open: the signature is
        // process-global, so a project opened whose source happens to match the last one's would
        // otherwise take the resident path against the PREVIOUS project's assembly. Realistic for two
        // projects from the same template, and silent when it happens. The cost of clearing it is one
        // full compile on the first tick, which is what a newly opened project needs anyway.
        _lastAutoRunSignature = null;

        _autoRunTimer.Stop();
        if (enabled) _autoRunTimer.Start();
    }

    /// <summary>
    /// A cheap stand-in for "has the code changed since the last compile" — every file's name and
    /// content, concatenated. Auto-Run compares it to decide between a full Roslyn run and a resident
    /// re-invoke, which is not merely a cost question: a full run blanks the canvas for the whole
    /// compile (<c>CompileAndExecuteAsync</c> clears before it compiles), and at 500 ms intervals that
    /// is most of the time, so re-compiling unchanged source would make the drawing flicker.
    ///
    /// <para>
    /// Deliberately the text and not a timestamp or a dirty flag: an external edit, an undo back to
    /// the original text, and a file added or removed all have to be caught, and only the text sees
    /// all three. Projects here are a handful of small files, so the concatenation is cheap.
    /// </para>
    /// </summary>
    private string CurrentSourceSignature()
    {
        if (_currentProject == null) return string.Empty;

        var sb = new System.Text.StringBuilder();
        foreach (var file in _currentProject.Files)
        {
            sb.Append(file.FileName).Append('\u0000').Append(file.Content).Append('\u0001');
        }
        return sb.ToString();
    }

    /// <summary>
    /// Persists the Auto-Run toggle onto the project and arms or disarms the timer. Guarded by
    /// <see cref="SettingsUiBusy"/> like every other settings handler: the loader drives this control
    /// too, and an unguarded handler would write the markup's or the loader's value back over the
    /// user's (CLAUDE.md note 103).
    /// </summary>
    private void AutoRunCheck_Changed(object sender, RoutedEventArgs e)
    {
        if (SettingsUiBusy || _currentProject == null) return;

        var enabled = AutoRunCheck.IsChecked == true;
        _currentProject.ProjectFile.Settings.AutoRun = enabled ? true : null;

        // Deliberately before the save: the toggle has to take effect even if remembering it does
        // not, and stopping the timer is the half the user is watching for.
        _autoRunTimer?.Stop();
        if (enabled) _autoRunTimer?.Start();

        Journal.Info("MW.AUTORUN.TOGGLE", "Auto-Run toggled", $"enabled={enabled}");

        if (TrySaveProjectFile("Auto-Run setting"))
        {
            SetStatus(enabled ? $"Auto-Run on - re-running every {AutoRunIntervalMs} ms" : "Auto-Run off", isError: false);
        }
    }

    /// <summary>
    /// Writes the <c>.vizproj</c>, reporting a failure instead of letting it escape. Returns whether
    /// the file was written.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Every caller is a UI event handler, and an exception out of one of those reaches the WPF
    /// dispatcher and ends the process — note 134's rule, which until now only <c>async void</c>
    /// handlers were held to. It is not a hypothetical: unticking Auto-Run closed the app, because
    /// OneDrive had the project file open for the moment the atomic rename needed it and the
    /// <see cref="IOException"/> had nowhere to go. <c>DurableFile</c> now retries that rename, so
    /// this should be rare — but "rare" is not a reason to keep a path from a settings checkbox to a
    /// dead process.
    /// </para>
    ///
    /// <para>
    /// Only for the project's own settings, which are a preference: the setting is already applied
    /// in memory and the worst case is that it is forgotten by the next session. Saving the user's
    /// source is a different matter and keeps its loud failure.
    /// </para>
    /// </remarks>
    private bool TrySaveProjectFile(string what)
    {
        if (_currentProject == null) return false;

        try
        {
            _currentProject.SaveProjectFile();
            return true;
        }
        catch (Exception ex)
        {
            Journal.Error("MW.PROJ.SAVE_FAIL", "Could not save the project file", ex, $"what={what}");
            SetStatus($"{what} applied, but could not be saved to the project file: {ex.Message}", isError: true);
            return false;
        }
    }

    /// <summary>
    /// One Auto-Run tick: exactly what pressing Run does, minus the dialogs.
    ///
    /// <para>
    /// A run can easily outlast the interval — a Roslyn compile is tens to hundreds of milliseconds —
    /// so a tick that arrives while the last one is still going is dropped rather than queued. Without
    /// that, a slow project would stack runs faster than it could finish them.
    /// </para>
    /// </summary>
    private async void AutoRunTimer_Tick(object? sender, EventArgs e)
    {
        try
        {
            if (_autoRunInFlight) return;
            if (_currentProject?.ProjectFile.Settings.AutoRun != true)
            {
                _autoRunTimer?.Stop();
                return;
            }

            _autoRunInFlight = true;
            try
            {
                // Flush the editor first: the signature below has to describe the text on screen, and
                // both run paths would do this anyway.
                SaveCurrentEditorContent();

                var signature = CurrentSourceSignature();
                if (signature != _lastAutoRunSignature || !ModuleCompiler.HasResidentAssembly)
                {
                    _lastAutoRunSignature = signature;
                    await RunSilentlyAsync("Auto-Run");
                }
                else
                {
                    await ReExecuteResidentSilentlyAsync("Auto-Run");
                }
            }
            catch (Exception ex)
            {
                // A throw out of an async void tick would reach the dispatcher and take the app down.
                Journal.Error("MW.AUTORUN.TICK_FAIL", "Auto-Run tick failed", ex);
            }
            finally
            {
                _autoRunInFlight = false;
            }
        }
        catch (Exception ex)
        {
            DoodleSharp.Diagnostics.Journal.Error("MW.AUTORUN.TICK_UNHANDLED", "AutoRunTimer_Tick threw", ex);
            SetStatus($"AutoRunTimer_Tick failed: {ex.Message}", isError: true);
        }
    }

    private void AutoSaveTimer_Tick(object? sender, EventArgs e)
    {
        if (!ApplicationSettings.Instance.AutoSaveEnabled)
        {
            _autoSaveTimer?.Stop();
            return;
        }

        // A prompt from an earlier tick is still on screen.
        if (_autoSavePromptActive || _currentProject == null)
            return;

        // Flush the editor into the active file first - the file is already flagged dirty
        // by the CodeEditor.TextChanged handler, this just copies the text across.
        SaveCurrentEditorContent();

        if (!_currentProject.HasUnsavedChanges)
            return;

        // The project has no real location on disk, so there is nothing to auto-save to.
        // Ask the user to save it rather than silently dropping the changes.
        if (ProjectNeedsSaveLocation())
        {
            PromptForAutoSaveLocation();
            return;
        }

        _autoSavePromptSuppressed = false; // project has a home again - reminders back on

        try
        {
            _currentProject.SaveAllFiles();
            RefreshFileTabs();
            SetStatus($"Auto-saved at {DateTime.Now:HH:mm:ss}", isError: false);
        }
        catch (Exception ex)
        {
            Journal.Error("MW.AUTOSAVE.FAIL", "Auto-save failed", ex);
            SetStatus($"Auto-save failed: {ex.Message}", isError: true);
        }
    }

    /// <summary>
    /// True when the project has no real location on disk yet - either it still lives in the
    /// temp folder (the "unsaved new project" state) or it holds files that have never been
    /// given a path through the Save dialog.
    /// </summary>
    private bool ProjectNeedsSaveLocation()
    {
        if (_currentProject == null)
            return false;

        if (_currentProject.ProjectDirectory.StartsWith(Path.GetTempPath(), StringComparison.OrdinalIgnoreCase))
            return true;

        return _currentProject.Files.Any(f => f.IsNew || string.IsNullOrEmpty(f.FilePath));
    }

    /// <summary>
    /// Tells the user that auto-save has nowhere to write and offers to run a normal Save.
    /// Answering "No" silences the reminder until the project actually gets saved.
    /// </summary>
    private void PromptForAutoSaveLocation()
    {
        if (_autoSavePromptSuppressed)
        {
            SetStatus("Auto-save skipped - this project has not been saved to disk yet (Ctrl+S)", isError: true);
            return;
        }

        _autoSavePromptActive = true;
        try
        {
            var result = MessageBox.Show(
                this,
                "Auto Save cannot run because this project has not been saved to disk yet.\n\n" +
                "Save it now?\n\n" +
                "Choosing No keeps your changes in memory only, and stops this reminder " +
                "until the project has been saved.",
                "Auto Save",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (result == MessageBoxResult.Yes)
                SaveButton_Click(this, new RoutedEventArgs());
            else
                _autoSavePromptSuppressed = true;
        }
        finally
        {
            _autoSavePromptActive = false;
        }
    }

    private bool SaveProjectToNewLocation()
    {
        using var dialog = new System.Windows.Forms.FolderBrowserDialog
        {
            Description = "Select folder to save VizCode project",
            UseDescriptionForTitle = true
        };

        if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
        {
            _currentProject?.MoveToDirectory(dialog.SelectedPath);
            RefreshFileTabs();
            return true;
        }

        return false;
    }

    private async void ManagePackagesMenuItem_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (_currentProject == null)
            {
                SetStatus("No project open", isError: true);
                return;
            }
            var win = new NuGetPackageManagerWindow(_currentProject);
            win.Owner = this;
            win.ShowDialog();
        
            // Refresh project tree to show .packages folder if created
            LoadProjectTree();
            RefreshFileTabs(); // In case any files were modified/added externally (unlikely but good practice)
        
            // Refresh completion references after adding/removing packages
            await UpdateWorkspaceReferencesAsync();
        }
        catch (Exception ex)
        {
            DoodleSharp.Diagnostics.Journal.Error("MW.PACKAGES.MANAGE_UNHANDLED", "ManagePackagesMenuItem_Click threw", ex);
            SetStatus($"ManagePackagesMenuItem failed: {ex.Message}", isError: true);
        }
    }

    #region File System Watcher

    private void StartProjectWatcher(string projectDirectory)
    {
        if (string.IsNullOrEmpty(projectDirectory) || !Directory.Exists(projectDirectory))
            return;

        try
        {
            _projectWatcher = new FileSystemWatcher(projectDirectory)
            {
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite,
                IncludeSubdirectories = true,
                EnableRaisingEvents = true
            };

            // Watch for .cs files
            _projectWatcher.Created += OnProjectFileChanged;
            _projectWatcher.Deleted += OnProjectFileChanged;
            _projectWatcher.Renamed += OnProjectFileRenamed;
            _projectWatcher.Changed += OnProjectFileChanged;

            // Initialize debounce timer for batching rapid changes
            _fileWatcherDebounceTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(500)
            };
            _fileWatcherDebounceTimer.Tick += (s, e) =>
            {
                _fileWatcherDebounceTimer.Stop();
                RefreshProjectFromDisk();
            };
        }
        catch (Exception ex)
        {
            SetStatus($"Warning: Could not start file watcher: {ex.Message}", isError: false);
        }
    }

    private void StopProjectWatcher()
    {
        if (_projectWatcher != null)
        {
            _projectWatcher.EnableRaisingEvents = false;
            _projectWatcher.Created -= OnProjectFileChanged;
            _projectWatcher.Deleted -= OnProjectFileChanged;
            _projectWatcher.Renamed -= OnProjectFileRenamed;
            _projectWatcher.Changed -= OnProjectFileChanged;
            _projectWatcher.Dispose();
            _projectWatcher = null;
        }

        _fileWatcherDebounceTimer?.Stop();
    }

    private void OnProjectFileChanged(object sender, FileSystemEventArgs e)
    {
        // React to source code files or directories
        if (!ShouldRefreshForPath(e.FullPath)) return;

        // Debounce rapid changes
        Dispatcher.Invoke(() =>
        {
            _fileWatcherDebounceTimer?.Stop();
            _fileWatcherDebounceTimer?.Start();
        });
    }

    private void OnProjectFileRenamed(object sender, RenamedEventArgs e)
    {
        // React if either old or new path should trigger refresh
        if (!ShouldRefreshForPath(e.FullPath) && !ShouldRefreshForPath(e.OldFullPath)) return;

        Dispatcher.Invoke(() =>
        {
            _fileWatcherDebounceTimer?.Stop();
            _fileWatcherDebounceTimer?.Start();
        });
    }

    private bool ShouldRefreshForPath(string path)
    {
        // Always refresh for directories (folder created/deleted)
        if (Directory.Exists(path) || !Path.HasExtension(path))
            return true;

        // Refresh for source code files
        var ext = Path.GetExtension(path).ToLowerInvariant();
        return ext == ".cs";
    }

    private void RefreshProjectFromDisk()
    {
        if (_currentProject == null) return;

        try
        {
            // Remember current active file
            var currentActiveFilePath = _activeFile?.FilePath;

            // Refresh files from disk
            var refresh = _currentProject.RefreshFilesFromDisk();

            // Files may have appeared or vanished — keep IntelliSense in step with the new set.
            SyncCompletionWorkspaceFiles();

            // A file whose content was replaced needs its new text pushed into the workspace, and —
            // if it is the file on screen — into the editor.
            foreach (var reloaded in refresh.Reloaded)
            {
                _completionWorkspace?.UpdateFile(reloaded.FileName, reloaded.Content);
                if (reloaded == _activeFile)
                    ReplaceEditorTextPreservingPosition(reloaded.Content);
            }

            // Refresh UI
            LoadProjectTree();
            RefreshFileTabs();

            // Restore active file if still exists
            if (!string.IsNullOrEmpty(currentActiveFilePath))
            {
                var restoredFile = _currentProject.Files.FirstOrDefault(f =>
                    f.FilePath.Equals(currentActiveFilePath, StringComparison.OrdinalIgnoreCase));
                if (restoredFile != null && restoredFile != _activeFile)
                {
                    SelectFile(restoredFile);
                }
                else if (_activeFile != null && !_currentProject.Files.Contains(_activeFile))
                {
                    // Active file was deleted, select entry point or first file
                    var fallback = _currentProject.EntryPointFile ?? _currentProject.Files.FirstOrDefault();
                    if (fallback != null) SelectFile(fallback);
                }
            }

            ReportDiskRefresh(refresh);
        }
        catch (Exception ex)
        {
            SetStatus($"Error refreshing project: {ex.Message}", isError: true);
        }
    }

    /// <summary>
    /// Replaces the editor's text while keeping the caret and scroll position where the user left
    /// them. Assigning <c>CodeEditor.Text</c> outright sends the caret to offset 0 and scrolls to
    /// the top, which — on a background watcher tick — moves the view out from under someone who
    /// was only reading.
    /// </summary>
    private void ReplaceEditorTextPreservingPosition(string newText)
    {
        var caret = CodeEditor.CaretOffset;
        var scroll = CodeEditor.TextArea.TextView.ScrollOffset;

        _suppressUnsavedMarking = true;
        CodeEditor.Text = newText;
        _suppressUnsavedMarking = false;

        CodeEditor.CaretOffset = Math.Min(caret, CodeEditor.Document.TextLength);
        CodeEditor.ScrollToVerticalOffset(scroll.Y);
        CodeEditor.ScrollToHorizontalOffset(scroll.X);
    }

    /// <summary>
    /// States what the refresh actually did. This used to say "Project refreshed from disk"
    /// unconditionally — including when nothing had been re-read, which is what let the missing
    /// reload go unnoticed for so long.
    /// </summary>
    private void ReportDiskRefresh(VizCodeProject.DiskRefreshResult refresh)
    {
        // Conflicts first: they are the only outcome that needs the user to do something.
        if (refresh.Conflicted.Count > 0)
        {
            var names = string.Join(", ", refresh.Conflicted.Select(f => f.FileName));
            SetStatus($"Changed on disk but kept your unsaved edits: {names}. " +
                      $"Save to overwrite the disk copy, or close the file without saving to take it.",
                      isError: true);
            return;
        }

        if (!refresh.AnythingChanged)
        {
            // Silent. The watcher fires on our own saves, so this is the common case and does not
            // deserve a status message that overwrites whatever was there.
            return;
        }

        var parts = new List<string>();
        if (refresh.Reloaded.Count > 0) parts.Add($"reloaded {Describe(refresh.Reloaded.Select(f => f.FileName))}");
        if (refresh.Added.Count > 0) parts.Add($"added {Describe(refresh.Added.Select(f => f.FileName))}");
        if (refresh.Removed.Count > 0) parts.Add($"removed {Describe(refresh.Removed.Select(f => f.FileName))}");

        SetStatus(char.ToUpperInvariant(parts[0][0]) + string.Join(", ", parts).Substring(1), isError: false);

        static string Describe(IEnumerable<string> names)
        {
            var list = names.ToList();
            return list.Count <= 3
                ? string.Join(", ", list)
                : $"{list.Count} files";
        }
    }

    #endregion

    #endregion

    #region Tab Events

    private void FileTabs_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (FileTabs.SelectedItem is VizCodeFile selectedFile && selectedFile != _activeFile)
        {
            SelectFile(selectedFile);
        }
    }

    private void CloseTab_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is VizCodeFile file)
        {
            // Don't allow closing the entry point file
            if (file.IsEntryPoint)
            {
                MessageBox.Show(
                    "Cannot close the entry point file (StartViz.cs).",
                    "Cannot Close",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            if (file.HasUnsavedChanges)
            {
                var result = MessageBox.Show(
                    $"Save changes to {file.FileName}?",
                    "Unsaved Changes",
                    MessageBoxButton.YesNoCancel,
                    MessageBoxImage.Question);

                if (result == MessageBoxResult.Cancel)
                    return;

                if (result == MessageBoxResult.Yes)
                    _currentProject?.SaveFile(file);
            }

            // Close the tab (don't remove from project, just mark as not open)
            file.IsOpen = false;
            RefreshFileTabs();

            // Select another open file
            var openFiles = _currentProject?.Files.Where(f => f.IsOpen).ToList();
            if (openFiles?.Count > 0)
            {
                SelectFile(openFiles[0]);
            }
            else
            {
                _activeFile = null;
                CodeEditor.Text = "";
            }
        }
    }

    #endregion

    #region Button Handlers

    private void NewProjectButton_Click(object sender, RoutedEventArgs e)
    {
        if (!PromptSaveChanges())
            return;

        var dialog = new NewProjectDialog();
        dialog.Owner = this;
        if (dialog.ShowDialog() == true)
        {
            try
            {
                // Stop existing watcher
                StopProjectWatcher();

                // Global parameters belong to the project that declared them.
                C2VGeometry.GlobalParameters.ClearAll();
                ModuleCompiler.InvalidateResident();

                if (dialog.OpenExistingProject)
                {
                    // Open existing project instead of creating new
                    _currentProject = VizCodeProject.Load(dialog.FullPath);
                    SetStatus($"Opened existing project: {dialog.ProjectName}", false);
                }
                else
                {
                    _currentProject = VizCodeProject.CreateNew(dialog.FullPath, dialog.ProjectName);
                    SetStatus($"Project created: {dialog.ProjectName}", false);
                }

                LoadProjectTree();
                RefreshFileTabs();

                // Start watching for external changes
                StartProjectWatcher(_currentProject.ProjectDirectory);

                if (_currentProject.EntryPointFile != null)
                {
                    SelectFile(_currentProject.EntryPointFile);
                }

                // The other project-open path already does this. Without it the Settings tab — and
                // the Auto-Run checkbox, which arms a timer — kept showing the *previous* project's
                // values after switching projects through this dialog.
                LoadSettingsToUI();

                // Initialize cached compilation workspace for IntelliSense
                InitializeCompletionWorkspace();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error: {ex.Message}");
            }
        }
    }

    private void NewFileButton_Click(object sender, RoutedEventArgs e)
    {
        if (_currentProject == null)
        {
            SetStatus("No project open", isError: true);
            return;
        }

        var projectName = _currentProject.ProjectFile.Name;

        var result = MessageBox.Show(
            "Create a Sketch file?\n\nYes — p5.js-style animation file with Setup()/Draw() blocks (uses C2VGeometry).\nNo — regular module file.",
            "New File",
            MessageBoxButton.YesNoCancel,
            MessageBoxImage.Question);
        if (result == MessageBoxResult.Cancel) return;
        bool createSketch = result == MessageBoxResult.Yes;

        // Generate unique name. Sketches use the StartSketch convention; the auto-infer
        // in VizCodeFile.Kind picks up that name pattern even after reload.
        const string ext = ".cs";
        var baseStem = createSketch ? "StartSketch" : "Untitled";
        int i = 1;
        string fileName = createSketch ? $"{baseStem}{ext}" : $"{baseStem}-1{ext}";
        while (_currentProject.Files.Any(f => f.FileName.Equals(fileName, StringComparison.OrdinalIgnoreCase)))
        {
            i++;
            fileName = createSketch ? $"{baseStem}-{i}{ext}" : $"{baseStem}-{i}{ext}";
        }

        var className = Path.GetFileNameWithoutExtension(fileName);
        string content = createSketch
            ? Templates.GetStartSketchTemplate(projectName)
            : Templates.GetEmptyModuleTemplate(projectName, className);

        var newFile = new VizCodeFile
        {
            FilePath = string.Empty, // No path yet
            Content = content,
            HasUnsavedChanges = true,
            IsNew = true,
            Kind = createSketch ? VizFileKind.Sketch : VizFileKind.Module
        };

        // Hack: We need a temporary 'FilePath' for the tab binding to display name correctly
        // VizCodeFile.FileName is derived from FilePath.
        // Let's set a fake path for now.
        newFile.FilePath = Path.Combine(_currentProject.ProjectDirectory, fileName);

        _currentProject.Files.Add(newFile);

        // Register it with IntelliSense straight away. Without this the new file's types stay
        // invisible to completion, go-to-definition and quick actions until the project is
        // reloaded — which is what made "MyNewClass." complete nothing right after creating it.
        SyncCompletionWorkspaceFiles();

        RefreshFileTabs();
        SelectFile(newFile);

        SetStatus($"Created: {fileName}", isError: false);
    }

    private string? PromptForFileName()
    {
        // Using a simple approach with InputBox-style dialog
        var dialog = new Window
        {
            Title = "New File",
            Width = 350,
            SizeToContent = SizeToContent.Height,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Owner = this,
            ResizeMode = ResizeMode.NoResize,
            Background = (SolidColorBrush)FindResource("SecondaryBackgroundBrush")
        };

        var grid = new Grid { Margin = new Thickness(16) };
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var label = new TextBlock
        {
            Text = "Enter file name (without extension):",
            Foreground = (SolidColorBrush)FindResource("ForegroundBrush"),
            Margin = new Thickness(0, 0, 0, 8)
        };
        Grid.SetRow(label, 0);

        var textBox = new TextBox
        {
            Text = "Module1",
            Margin = new Thickness(0, 0, 0, 16),
            Padding = new Thickness(8, 4, 8, 4)
        };
        textBox.SelectAll();
        Grid.SetRow(textBox, 1);

        var buttonPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right
        };
        Grid.SetRow(buttonPanel, 2);

        var okButton = new Button
        {
            Content = "OK",
            Width = 80,
            Margin = new Thickness(0, 0, 8, 0),
            Style = (Style)FindResource("RunButtonStyle"), // Use Accent Color
            Foreground = Brushes.White // Force white text
        };
        okButton.Click += (s, e) => { dialog.DialogResult = true; dialog.Close(); };

        var cancelButton = new Button
        {
            Content = "Cancel",
            Width = 80,
            Style = (Style)FindResource("RibbonButtonStyle"),
            Foreground = (SolidColorBrush)FindResource("ForegroundBrush")
        };
        cancelButton.Click += (s, e) => { dialog.DialogResult = false; dialog.Close(); };

        buttonPanel.Children.Add(okButton);
        buttonPanel.Children.Add(cancelButton);

        grid.Children.Add(label);
        grid.Children.Add(textBox);
        grid.Children.Add(buttonPanel);

        dialog.Content = grid;
        textBox.Focus();

        if (dialog.ShowDialog() == true)
        {
            return textBox.Text;
        }

        return null;
    }

    private void OpenButton_Click(object sender, RoutedEventArgs e)
    {
        if (!PromptSaveChanges())
            return;

        var dialog = new OpenFileDialog
        {
            Filter = "DoodleSharp Project (*.vizproj)|*.vizproj",
            DefaultExt = ".vizproj",
            Title = "Open Project"
        };

        if (dialog.ShowDialog() == true)
        {
            LoadProject(dialog.FileName);
        }
    }

    private bool _loadingSettings;

    /// <summary>
    /// False until the constructor has finished, so a control's XAML-declared initial value cannot
    /// be mistaken for the user changing a setting.
    ///
    /// <para>
    /// <c>InitializeComponent</c> raises Checked / SelectionChanged for any control that declares a
    /// starting value, long before <see cref="LoadSettingsToUI"/> runs — and a settings handler that
    /// fires there writes the markup's value into <c>ApplicationSettings.Instance</c> and saves it,
    /// destroying what was on disk before it is ever read. The retired <i>Auto Draw Shapes</i>
    /// checkbox carried <c>IsChecked="True"</c> and did exactly that: the setting came back on at
    /// every launch and could not be turned off permanently. Every settings handler checks
    /// <see cref="SettingsUiBusy"/>, which covers both this window and the load itself.
    /// </para>
    /// </summary>
    private bool _settingsUiReady;

    /// <summary>True while a settings handler must not write back — during construction, or a load.</summary>
    private bool SettingsUiBusy => !_settingsUiReady || _loadingSettings;
    private void LoadSettingsToUI()
    {
        _loadingSettings = true;
        try
        {
        // Project settings are per-project and simply absent when no project is open; the
        // application settings further down are global and have to load either way. The two used to
        // share an early return, so with no project the Settings tab showed the XAML defaults for
        // every application setting — and because Save writes all of them back from the UI, pressing
        // it then overwrote the user's saved snap, highlight, export and default-shape values.
        var settings = _currentProject?.ProjectFile.Settings;
        if (settings == null)
        {
            SettingsColorBox.Text = "";
            SettingsFillColorBox.Text = "";
            SettingsCanvasColorBox.Text = "";
            SettingsThicknessBox.Text = "";
            SettingsLineTypeScaleBox.Text = "";
        }
        else
        {
            SettingsColorBox.Text = settings.DefaultColor ?? "";
            SettingsFillColorBox.Text = settings.DefaultFillColor ?? "";
            SettingsCanvasColorBox.Text = settings.DefaultCanvasBackgroundColor ?? "";
            SettingsThicknessBox.Text = settings.DefaultLineWeight.HasValue
                ? settings.DefaultLineWeight.Value.ToString()
                : "";
            SettingsLineTypeScaleBox.Text = settings.DefaultLineTypeScale.HasValue
                ? settings.DefaultLineTypeScale.Value.ToString()
                : "";

            // Dimension Style
            DimStyleOffsetBox.Text = settings.DimOffset.HasValue ? settings.DimOffset.Value.ToString() : "";
            DimStyleArrowSizeBox.Text = settings.DimArrowSize.HasValue ? settings.DimArrowSize.Value.ToString() : "";
            DimStyleTextHeightBox.Text = settings.DimTextHeight.HasValue ? settings.DimTextHeight.Value.ToString() : "";
            DimStyleDecimalPlacesBox.Text = settings.DimDecimalPlaces.HasValue ? settings.DimDecimalPlaces.Value.ToString() : "";
            DimStyleExtendBeyondBox.Text = settings.DimExtendBeyondDimLines.HasValue ? settings.DimExtendBeyondDimLines.Value.ToString() : "";
            DimStyleOffsetFromOriginBox.Text = settings.DimOffsetFromOrigin.HasValue ? settings.DimOffsetFromOrigin.Value.ToString() : "";
            DimStylePrefixBox.Text = settings.DimPrefix ?? "";
            DimStyleSuffixBox.Text = settings.DimSuffix ?? "";
            DimStyleTextBgOpaqueCheck.IsChecked = settings.DimTextBgOpaque == true;
            DimStyleExtLineColorBox.Text = settings.DimExtensionLineColor ?? "";
            DimStyleDimLineColorBox.Text = settings.DimDimensionLineColor ?? "";
            DimStyleTextColorBox.Text = settings.DimTextColor ?? "";
            DimStyleSuppressDimLineCheck.IsChecked = settings.DimSuppressDimensionLine == true;

            // Apply Canvas Background immediately on load (Fix for Issue 1)
            if (!string.IsNullOrEmpty(settings.DefaultCanvasBackgroundColor))
            {
                try {
                    var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(settings.DefaultCanvasBackgroundColor));
                    ViewportHost.CanvasBackground = brush;
                } catch {}
            }
        }

        // ---------------------------------------------------------
        // Load Application Settings
        // ---------------------------------------------------------
        var appSettings = ApplicationSettings.Instance;
        
        // Export Background
        string exportBg = appSettings.DefaultExportBackground ?? "Transparent";
        foreach (ComboBoxItem item in SettingsExportBackgroundCombo.Items)
        {
            var content = item.Content?.ToString();
            if (content == exportBg || 
               (exportBg == "Light" && content != null && content.Contains("Light")))
            {
                SettingsExportBackgroundCombo.SelectedItem = item;
                break;
            }
        }
        if (SettingsExportBackgroundCombo.SelectedItem == null) 
            SettingsExportBackgroundCombo.SelectedIndex = 0;
            
        // Include Grid
        SettingsIncludeGridCheck.IsChecked = appSettings.IncludeGridInExport;

        // Application-level Default Shape Settings
        AppSettingsColorBox.Text = appSettings.AppDefaultColor ?? "";
        AppSettingsFillColorBox.Text = appSettings.AppDefaultFillColor ?? "";
        AppSettingsCanvasColorBox.Text = appSettings.AppDefaultCanvasBackground ?? "";
        AppSettingsThicknessBox.Text = appSettings.AppDefaultLineWeight.HasValue
            ? appSettings.AppDefaultLineWeight.Value.ToString()
            : "";
        AppSettingsLineTypeScaleBox.Text = appSettings.AppDefaultLineTypeScale.HasValue
            ? appSettings.AppDefaultLineTypeScale.Value.ToString()
            : "";

        // Snap Settings
        SnapEndpointCheck.IsChecked = appSettings.SnapEndpointEnabled;
        SnapMidpointCheck.IsChecked = appSettings.SnapMidpointEnabled;
        SnapCenterCheck.IsChecked = appSettings.SnapCenterEnabled;
        SnapIntersectionCheck.IsChecked = appSettings.SnapIntersectionEnabled;
        SnapNearestCheck.IsChecked = appSettings.SnapNearestEnabled;
        SnapPerpendicularCheck.IsChecked = appSettings.SnapPerpendicularEnabled;
        SnapExtensionCheck.IsChecked = appSettings.SnapExtensionEnabled;
        SnapTangentCheck.IsChecked = appSettings.SnapTangentEnabled;
        SnapToGridCheck.IsChecked = appSettings.SnapToGridEnabled;
        ViewportHost.SnapToGrid = appSettings.SnapToGridEnabled;

        // Highlight Settings
        HighlightColorBox.Text = appSettings.HighlightColor ?? "Yellow";
        HighlightOpacitySlider.Value = appSettings.HighlightOpacity;
        HighlightOpacityText.Text = $"{appSettings.HighlightOpacity}%";
        UpdateColorButton(HighlightColorBtn, HighlightColorBox.Text);

        // Canvas Settings
        SettingsZoomToFitCheck.IsChecked = appSettings.ZoomToFitOnRun;
        SettingsDrawPointAsPatchCheck.IsChecked = appSettings.DrawPointAsPatch;

        // Auto Save Settings
        SettingsAutoSaveCheck.IsChecked = appSettings.AutoSaveEnabled;
        SettingsAutoSaveIntervalBox.Text = appSettings.AutoSaveIntervalSeconds.ToString();

        // Line Style Rendering Settings
        SettingsDisplayLineWeightCheck.IsChecked = appSettings.DisplayLineWeight;

        // Render backend. An unrecognised value in the settings file behaves as Auto, matching
        // ShouldUseRasterBackend, rather than leaving the combo blank.
        SettingsRenderBackendCombo.SelectedIndex = appSettings.RenderBackend?.Trim().ToLowerInvariant() switch
        {
            "legacy" => 1,
            "managed" => 2,
            "gpu" => 3,
            _ => 0,
        };

        // Update Button colors for Project Settings
        UpdateColorButton(SettingsColorBtn, SettingsColorBox.Text);
        UpdateColorButton(SettingsFillColorBtn, SettingsFillColorBox.Text);
        UpdateColorButton(SettingsCanvasColorBtn, SettingsCanvasColorBox.Text);

        // Update Button colors for Application Settings
        UpdateColorButton(AppSettingsColorBtn, AppSettingsColorBox.Text);
        UpdateColorButton(AppSettingsFillColorBtn, AppSettingsFillColorBox.Text);
        UpdateColorButton(AppSettingsCanvasColorBtn, AppSettingsCanvasColorBox.Text);
        }
        finally { _loadingSettings = false; }

        ApplyAutoSaveSettings();
        ApplyAutoRunSetting();
    }

    private void ColorBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        // Project Settings color boxes
        if (sender == SettingsColorBox)
            UpdateColorButton(SettingsColorBtn, SettingsColorBox.Text);
        else if (sender == SettingsFillColorBox)
            UpdateColorButton(SettingsFillColorBtn, SettingsFillColorBox.Text);
        else if (sender == SettingsCanvasColorBox)
            UpdateColorButton(SettingsCanvasColorBtn, SettingsCanvasColorBox.Text);
        // Application Settings color boxes
        else if (sender == AppSettingsColorBox)
            UpdateColorButton(AppSettingsColorBtn, AppSettingsColorBox.Text);
        else if (sender == AppSettingsFillColorBox)
            UpdateColorButton(AppSettingsFillColorBtn, AppSettingsFillColorBox.Text);
        else if (sender == AppSettingsCanvasColorBox)
            UpdateColorButton(AppSettingsCanvasColorBtn, AppSettingsCanvasColorBox.Text);

        // Apply live so new shapes pick up the default immediately (no "Save Settings" click needed).
        if (!SettingsUiBusy)
            PersistDefaultColor(sender as TextBox);
    }

    /// <summary>
    /// Live-applies a default stroke/fill/canvas color box: validates it, writes it to the right
    /// setting (per-project, or app-level fallback), and re-applies ShapeDefaults so newly created
    /// shapes use it at once. Mirrors how the Highlight color already applies on change.
    /// </summary>
    private void PersistDefaultColor(TextBox? box)
    {
        if (box == null) return;
        string? val = string.IsNullOrWhiteSpace(box.Text) ? null : box.Text.Trim();
        if (val != null) { try { _ = ColorConverter.ConvertFromString(val); } catch { return; } } // ignore mid-typing

        var proj = _currentProject?.ProjectFile.Settings;
        if (box == SettingsColorBox && proj != null) proj.DefaultColor = val;
        else if (box == SettingsFillColorBox && proj != null) proj.DefaultFillColor = val;
        else if (box == SettingsCanvasColorBox && proj != null) proj.DefaultCanvasBackgroundColor = val;
        else if (box == AppSettingsColorBox) { ApplicationSettings.Instance.AppDefaultColor = val; ApplicationSettings.Save(); }
        else if (box == AppSettingsFillColorBox) { ApplicationSettings.Instance.AppDefaultFillColor = val; ApplicationSettings.Save(); }
        else if (box == AppSettingsCanvasColorBox) { ApplicationSettings.Instance.AppDefaultCanvasBackground = val; ApplicationSettings.Save(); }
        else return;

        ApplyShapeDefaultsLive();

        // Live canvas background for the canvas-color boxes.
        if ((box == SettingsCanvasColorBox || box == AppSettingsCanvasColorBox) && !string.IsNullOrWhiteSpace(val))
        {
            try { ViewportHost.CanvasBackground = new SolidColorBrush((Color)ColorConverter.ConvertFromString(val)); } catch { }
        }
    }

    private void DefaultNumericBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (SettingsUiBusy || sender is not TextBox box) return;
        bool empty = string.IsNullOrWhiteSpace(box.Text);
        if (!empty && !double.TryParse(box.Text.Trim(), out _)) return; // ignore mid-typing
        double? val = empty ? null : double.Parse(box.Text.Trim());

        var proj = _currentProject?.ProjectFile.Settings;
        if (box == SettingsThicknessBox && proj != null) proj.DefaultLineWeight = val;
        else if (box == SettingsLineTypeScaleBox && proj != null) proj.DefaultLineTypeScale = val;
        else if (box == AppSettingsThicknessBox) { ApplicationSettings.Instance.AppDefaultLineWeight = val; ApplicationSettings.Save(); }
        else if (box == AppSettingsLineTypeScaleBox) { ApplicationSettings.Instance.AppDefaultLineTypeScale = val; ApplicationSettings.Save(); }
        else return;

        ApplyShapeDefaultsLive();
    }

    /// <summary>Re-applies shape default styles. Project settings win; app-level defaults are the fallback.</summary>
    private void ApplyShapeDefaultsLive()
    {
        if (_currentProject != null)
        {
            _currentProject.ApplySettings();
        }
        else
        {
            var app = ApplicationSettings.Instance;
            C2VGeometry.ShapeDefaults.GlobalColor = string.IsNullOrWhiteSpace(app.AppDefaultColor) ? null : app.AppDefaultColor;
            C2VGeometry.ShapeDefaults.GlobalFillColor = string.IsNullOrWhiteSpace(app.AppDefaultFillColor) ? null : app.AppDefaultFillColor;
            C2VGeometry.ShapeDefaults.GlobalLineWeight = app.AppDefaultLineWeight;
            C2VGeometry.ShapeDefaults.GlobalLineTypeScale = app.AppDefaultLineTypeScale;
        }
    }
    
    private void UpdateColorButton(Button btn, string colorText)
    {
        if (btn == null) return;

        try
        {
            if (string.IsNullOrWhiteSpace(colorText))
                btn.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#444444"));
            else
            {
                 var color = (Color)ColorConverter.ConvertFromString(colorText);
                 btn.Background = new SolidColorBrush(color);
            }
        }
        catch
        {
            // Keep previous or set to default on error
            btn.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#444444"));
        }
    }

    private void PickColorButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string tag)
        {
            TextBox? targetBox = tag switch
            {
                "Stroke" => SettingsColorBox,
                "Fill" => SettingsFillColorBox,
                "Canvas" => SettingsCanvasColorBox,
                "AppStroke" => AppSettingsColorBox,
                "AppFill" => AppSettingsFillColorBox,
                "AppCanvas" => AppSettingsCanvasColorBox,
                _ => null
            };

            if (targetBox == null) return;

            var dialog = new ColorPickerDialog(targetBox.Text);
            dialog.Owner = this;
            if (dialog.ShowDialog() == true)
            {
                targetBox.Text = dialog.SelectedColor;
            }
        }
    }

    private void HighlightColorBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (HighlightColorBtn != null && HighlightColorBox != null)
            UpdateColorButton(HighlightColorBtn, HighlightColorBox.Text);
    }

    private void PickHighlightColorButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new ColorPickerDialog(HighlightColorBox.Text);
        dialog.Owner = this;
        if (dialog.ShowDialog() == true)
        {
            HighlightColorBox.Text = dialog.SelectedColor;
        }
    }

    private void HighlightOpacitySlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (HighlightOpacityText != null)
        {
            HighlightOpacityText.Text = $"{(int)e.NewValue}%";
        }
    }

    private void SettingsZoomToFitCheck_Changed(object sender, RoutedEventArgs e)
    {
        if (SettingsUiBusy) return;
        ApplicationSettings.Instance.ZoomToFitOnRun = SettingsZoomToFitCheck.IsChecked == true;
        ApplicationSettings.Save();
    }

    private void SettingsDrawPointAsPatchCheck_Changed(object sender, RoutedEventArgs e)
    {
        if (SettingsUiBusy) return;
        ApplicationSettings.Instance.DrawPointAsPatch = SettingsDrawPointAsPatchCheck.IsChecked == true;
        ApplicationSettings.Save();
        ViewportHost.Refresh();
    }

    private void SettingsAutoSaveCheck_Changed(object sender, RoutedEventArgs e)
    {
        if (SettingsUiBusy) return;
        ApplicationSettings.Instance.AutoSaveEnabled = SettingsAutoSaveCheck.IsChecked == true;
        ApplicationSettings.Save();
        _autoSavePromptSuppressed = false; // re-enabling auto-save re-arms the reminder
        ApplyAutoSaveSettings();
    }

    private void SettingsAutoSaveIntervalBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (SettingsUiBusy) return;

        var text = SettingsAutoSaveIntervalBox.Text.Trim();
        if (!int.TryParse(text, out var seconds)) return; // ignore mid-typing
        if (seconds < MinAutoSaveSeconds || seconds > MaxAutoSaveSeconds) return;

        ApplicationSettings.Instance.AutoSaveIntervalSeconds = seconds;
        ApplicationSettings.Save();
        ApplyAutoSaveSettings();
    }

    /// <summary>
    /// Off (the default): a shape's LineWeight is device pixels and a stroke looks the same at any
    /// zoom. On: it is world units, so strokes grow and shrink with the drawing. Replaced a pair of
    /// Absolute/Relative dropdowns — line weight and line type scale — that offered four
    /// combinations where two were wanted; line type scale is now always absolute.
    /// </summary>
    private void SettingsDisplayLineWeightCheck_Changed(object sender, RoutedEventArgs e)
    {
        if (SettingsUiBusy) return;

        ApplicationSettings.Instance.DisplayLineWeight = SettingsDisplayLineWeightCheck.IsChecked == true;
        ApplicationSettings.Save();
        ViewportHost.Refresh();
    }

    /// <summary>
    /// The render backend had no UI at all — it was reachable only by hand-editing
    /// <c>appsettings.json</c>, which is not a setting a user can be expected to discover.
    /// </summary>
    private void SettingsRenderBackendCombo_Changed(object sender, SelectionChangedEventArgs e)
    {
        // Fires during InitializeComponent, before the control field is assigned.
        if (SettingsUiBusy || SettingsRenderBackendCombo == null) return;

        ApplicationSettings.Instance.RenderBackend = SettingsRenderBackendCombo.SelectedIndex switch
        {
            1 => "Legacy",
            2 => "Managed",
            3 => "GPU",
            _ => "Auto",
        };
        ApplicationSettings.Save();

        // A backend switch changes layer ordering, so the whole scene has to be rebuilt.
        ViewportHost.Refresh();
    }

    private void SaveSettingsButton_Click(object sender, RoutedEventArgs e)
    {
        // 1. Save Project Settings
        if (_currentProject != null)
        {
            string? Color = SettingsColorBox.Text.Trim();
            if (string.IsNullOrEmpty(Color)) Color = null;

            string? fillColor = SettingsFillColorBox.Text.Trim();
            if (string.IsNullOrEmpty(fillColor)) fillColor = null;
            
            string? canvasColor = SettingsCanvasColorBox.Text.Trim();
            if (string.IsNullOrEmpty(canvasColor)) canvasColor = null;

            double? thickness = null;
            if (double.TryParse(SettingsThicknessBox.Text.Trim(), out double t))
            {
                thickness = t;
            }

            double? lineTypeScale = null;
            if (double.TryParse(SettingsLineTypeScaleBox.Text.Trim(), out double lts))
            {
                lineTypeScale = lts;
            }

            // Parse Dimension Style values
            double? dimOffset = null;
            if (double.TryParse(DimStyleOffsetBox.Text.Trim(), out double doff)) dimOffset = doff;
            double? dimArrowSize = null;
            if (double.TryParse(DimStyleArrowSizeBox.Text.Trim(), out double das)) dimArrowSize = das;
            double? dimTextHeight = null;
            if (double.TryParse(DimStyleTextHeightBox.Text.Trim(), out double dth)) dimTextHeight = dth;
            int? dimDecimalPlaces = null;
            if (int.TryParse(DimStyleDecimalPlacesBox.Text.Trim(), out int ddp)) dimDecimalPlaces = ddp;
            double? dimExtendBeyond = null;
            if (double.TryParse(DimStyleExtendBeyondBox.Text.Trim(), out double deb)) dimExtendBeyond = deb;
            double? dimOffsetFromOrigin = null;
            if (double.TryParse(DimStyleOffsetFromOriginBox.Text.Trim(), out double dofo)) dimOffsetFromOrigin = dofo;
            string? dimPrefix = DimStylePrefixBox.Text;
            if (string.IsNullOrEmpty(dimPrefix)) dimPrefix = null;
            string? dimSuffix = DimStyleSuffixBox.Text;
            if (string.IsNullOrEmpty(dimSuffix)) dimSuffix = null;
            bool? dimTextBgOpaque = DimStyleTextBgOpaqueCheck.IsChecked == true ? true : null;
            string? dimExtLineColor = DimStyleExtLineColorBox.Text.Trim();
            if (string.IsNullOrEmpty(dimExtLineColor)) dimExtLineColor = null;
            string? dimDimLineColor = DimStyleDimLineColorBox.Text.Trim();
            if (string.IsNullOrEmpty(dimDimLineColor)) dimDimLineColor = null;
            string? dimTextColor = DimStyleTextColorBox.Text.Trim();
            if (string.IsNullOrEmpty(dimTextColor)) dimTextColor = null;
            bool? dimSuppressDimLine = DimStyleSuppressDimLineCheck.IsChecked == true ? true : null;

            var settings = _currentProject.ProjectFile.Settings;
            settings.DefaultColor = Color;
            settings.DefaultFillColor = fillColor;
            settings.DefaultCanvasBackgroundColor = canvasColor;
            settings.DefaultLineWeight = thickness;
            settings.DefaultLineTypeScale = lineTypeScale;
            settings.DimOffset = dimOffset;
            settings.DimArrowSize = dimArrowSize;
            settings.DimTextHeight = dimTextHeight;
            settings.DimDecimalPlaces = dimDecimalPlaces;
            settings.DimExtendBeyondDimLines = dimExtendBeyond;
            settings.DimOffsetFromOrigin = dimOffsetFromOrigin;
            settings.DimPrefix = dimPrefix;
            settings.DimSuffix = dimSuffix;
            settings.DimTextBgOpaque = dimTextBgOpaque;
            settings.DimExtensionLineColor = dimExtLineColor;
            settings.DimDimensionLineColor = dimDimLineColor;
            settings.DimTextColor = dimTextColor;
            settings.DimSuppressDimensionLine = dimSuppressDimLine;

            _currentProject.ApplySettings();
            
            if (!string.IsNullOrEmpty(canvasColor))
            {
                try {
                    var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(canvasColor));
                    ViewportHost.CanvasBackground = brush;
                } catch { }
            }

            TrySaveProjectFile("Settings");
        }

        // 2. Save Application Settings
        string exportBg = "Transparent";
        if (SettingsExportBackgroundCombo.SelectedItem is ComboBoxItem item)
        {
            exportBg = item.Content?.ToString() ?? "Transparent";
        }

        ApplicationSettings.Instance.DefaultExportBackground = exportBg;
        ApplicationSettings.Instance.IncludeGridInExport = SettingsIncludeGridCheck.IsChecked == true;

        // Save Application-level default shape settings
        string? appColor = AppSettingsColorBox.Text.Trim();
        ApplicationSettings.Instance.AppDefaultColor = string.IsNullOrEmpty(appColor) ? null : appColor;

        string? appFillColor = AppSettingsFillColorBox.Text.Trim();
        ApplicationSettings.Instance.AppDefaultFillColor = string.IsNullOrEmpty(appFillColor) ? null : appFillColor;

        string? appCanvasColor = AppSettingsCanvasColorBox.Text.Trim();
        ApplicationSettings.Instance.AppDefaultCanvasBackground = string.IsNullOrEmpty(appCanvasColor) ? null : appCanvasColor;

        if (double.TryParse(AppSettingsThicknessBox.Text.Trim(), out double appThickness))
            ApplicationSettings.Instance.AppDefaultLineWeight = appThickness;
        else
            ApplicationSettings.Instance.AppDefaultLineWeight = null;

        if (double.TryParse(AppSettingsLineTypeScaleBox.Text.Trim(), out double appLineTypeScale))
            ApplicationSettings.Instance.AppDefaultLineTypeScale = appLineTypeScale;
        else
            ApplicationSettings.Instance.AppDefaultLineTypeScale = null;

        // Save Snap Settings
        ApplicationSettings.Instance.SnapEndpointEnabled = SnapEndpointCheck.IsChecked == true;
        ApplicationSettings.Instance.SnapMidpointEnabled = SnapMidpointCheck.IsChecked == true;
        ApplicationSettings.Instance.SnapCenterEnabled = SnapCenterCheck.IsChecked == true;
        ApplicationSettings.Instance.SnapIntersectionEnabled = SnapIntersectionCheck.IsChecked == true;
        ApplicationSettings.Instance.SnapNearestEnabled = SnapNearestCheck.IsChecked == true;
        ApplicationSettings.Instance.SnapPerpendicularEnabled = SnapPerpendicularCheck.IsChecked == true;
        ApplicationSettings.Instance.SnapExtensionEnabled = SnapExtensionCheck.IsChecked == true;
        ApplicationSettings.Instance.SnapTangentEnabled = SnapTangentCheck.IsChecked == true;
        ApplicationSettings.Instance.SnapToGridEnabled = SnapToGridCheck.IsChecked == true;
        ViewportHost.SnapToGrid = ApplicationSettings.Instance.SnapToGridEnabled;

        // Save Highlight Settings
        ApplicationSettings.Instance.HighlightColor = HighlightColorBox.Text.Trim();
        ApplicationSettings.Instance.HighlightOpacity = (int)HighlightOpacitySlider.Value;

        ApplicationSettings.Save();

        // Refresh snap settings for all tools
        ViewportHost.RefreshSnapSettings();

        SetStatus("Settings saved (Project and Application).", isError: false);
    }

    private void CloseProjectMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (!PromptSaveChanges())
            return;

        // Stop any running animations before closing
        StopAllAnimations();

        var welcome = new WelcomeWindow();
        welcome.Show();
        Close();
    }

    private void StopAllAnimations()
    {
        var timeline = CanvasRenderer.Instance.ActiveTimeline;
        if (timeline != null)
        {
            timeline.IsPlaying = false;
            timeline.Stop();
            _animationStopwatch.Reset();
            _lastAnimationFrameTime = -1;
        }

        // Clear active timeline reference
        CanvasRenderer.Instance.ActiveTimeline = null;

        // Drop any queued frame callbacks and registered mouse handlers. This must be unconditional:
        // SketchRuntime.Stop() returns immediately when no sketch is active, so before this line Stop
        // did not stop a Frame loop at all — a Main()-mode script driving motion by rescheduling a
        // callback kept animating after the user pressed Stop, with no way to halt it short of another
        // Run. Dropping the mouse handlers here is also what takes the canvas back out of interactive
        // mode, so Stop restores selection and wheel zoom.
        DoodleSharp.Animation.Frame.Clear();
        DoodleSharp.Animation.Mouse.Clear();

        // Stop any running sketch and unload its assembly context. Also reset the canvas
        // background in case the sketch called Background(color) — otherwise the user-set
        // color would leak into the next Run.
        var wasSketchRunning = DoodleSharp.Sketching.SketchRuntime.Instance.IsRunning;
        DoodleSharp.Sketching.SketchRuntime.Instance.Stop();
        if (wasSketchRunning)
        {
            ViewportHost.CanvasBackground = new System.Windows.Media.SolidColorBrush(
                System.Windows.Media.Color.FromRgb(30, 30, 30));
        }

        // Hide animation controls
        AnimationControlsPanel.Visibility = Visibility.Collapsed;
        _isPaused = false;

        // Handlers were just dropped, so interactive mode is over: restore selection, the properties
        // panel and wheel zoom, and take the navigation overlay away.
        SyncInteractiveModeChrome();
    }

    private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
    {
        Journal.Info("MW.CLOSING", "Main window closing");

        if (!PromptSaveChanges())
        {
            e.Cancel = true;
            Journal.Info("MW.CLOSE.CANCELLED", "Close cancelled by the unsaved-changes prompt");
            return;
        }

        // Stop any running animations
        StopAllAnimations();

        // Clean up file watcher
        StopProjectWatcher();

        // Stop auto-save so it can't fire a prompt while the window is closing
        _autoSaveTimer?.Stop();

        // Same for Auto-Run: a tick during teardown would compile and execute into a dying window.
        _autoRunTimer?.Stop();

        // After the prompt, so a cancelled close does not persist a layout the user keeps editing.
        SaveLayout();

        Journal.Info("MW.CLOSED", "Main window teardown complete");
    }

    private void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        if (_currentProject == null)
            return;

        SaveCurrentEditorContent();

        // Check for new files that need a save location
        foreach (var file in _currentProject.Files.Where(f => f.IsNew).ToList())
        {
            SelectFile(file); // Show the file being saved
            var dialog = new SaveFileDialog
            {
                FileName = file.FileName,
                Filter = "C# Files (*.cs)|*.cs|Text Files (*.txt)|*.txt|JSON Files (*.json)|*.json",
                DefaultExt = ".cs",
                InitialDirectory = _currentProject.ProjectDirectory
            };

            if (dialog.ShowDialog() == true)
            {
                file.FilePath = dialog.FileName;
                file.IsNew = false;
                // Update class name if file name changed? 
                // That's complex refactoring, skipping for now unless needed.
            }
            else
            {
                // User cancelled save for this file. 
                // We should probably stop saving others? Or just skip? 
                // Proceeding to save others.
            }
        }

        // If project is in temp directory, prompt for real location
        if (_currentProject.ProjectDirectory.StartsWith(Path.GetTempPath()))
        {
            if (!SaveProjectToNewLocation())
                return;
        }
        else
        {
            _currentProject.SaveAllFiles();
        }

        RefreshFileTabs();
        LoadProjectTree();
        SetStatus("All files saved", isError: false);
    }

    private async void RunButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            using var runScope = Journal.Scope("MW.RUN", "Run requested",
                $"project={_currentProject?.ProjectFile.Name ?? "<none>"} files={_currentProject?.Files.Count ?? 0}");

            if (_currentProject == null || _currentProject.Files.Count == 0)
            {
                SetStatus("No files to compile", isError: false);
                return;
            }

            // Save current editor content
            SaveCurrentEditorContent();

            // Verify entry point exists
            if (_currentProject.EntryPointFile == null)
            {
                SetStatus("Error: StartViz.cs not found", isError: true);
                return;
            }

            SetStatus("Compiling...", isError: false);
            RunButton.IsEnabled = false;

            // Clear selection before running (shapes will be recreated from code)
            ViewportHost.ClearSelection();
            _propertiesPanel?.UpdateSelection(new List<C2VGeometry.Shape>());

            // Show console tab when running code, unless the user has hidden it via Windows > Console
            if (ShowConsoleMenuItem.IsChecked)
            {
                SetPaneVisible("ds.tool.console", true);
            }

            try
            {
                _textMarkerService?.Clear();
                var result = await _compiler.CompileAndExecuteAsync(_currentProject);

                // Apply project settings (including background)
                _currentProject.ApplySettings();
                if (_currentProject.ProjectFile.Settings.DefaultCanvasBackgroundColor is string bgCode)
                {
                     try { ViewportHost.CanvasBackground = new SolidColorBrush((Color)ColorConverter.ConvertFromString(bgCode)); } catch {}
                }

                if (result.Success)
                {
                    // Reset animation time
                    _animationStopwatch.Restart();
                    _lastAnimationFrameTime = -1;

                    // Drop the commands the run invalidated — every shape is regenerated from code, so
                    // anything holding a Shape reference now points at an object that has left the
                    // canvas. Code-backed commands (the canvas delete) are kept: see PruneAfterCodeRun.
                    TransactionManager.Instance.PruneAfterCodeRun();

                    var shapes = CanvasRenderer.Instance.GetShapes();
                    var count = shapes.Count;

                    CanvasRenderer.Instance.RenderTo(ViewportHost);
                    SetStatus($"Success: {count} shape{(count != 1 ? "s" : "")} drawn", isError: false);
                    PopulateOutliner(shapes);
                    Journal.Info("MW.RUN.OK", "Run succeeded", $"shapes={count}");
                }
                else
                {
                    Journal.Warn("MW.RUN.FAILED", "Run did not succeed", $"error={result.Error}");
                    var errorCount = result.Diagnostics?.Count(d => d.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error) ?? 0;

                    if (errorCount > 0)
                    {
                        SetStatus($"Compilation failed: {errorCount} error{(errorCount != 1 ? "s" : "")}", isError: true);
                    }
                    else if (!string.IsNullOrEmpty(result.Error))
                    {
                        // Show the error message if no diagnostics but compilation failed
                        SetStatus("Compilation Error", isError: true);
                        // Also write full error to console
                        Console.ConsoleOutput.Instance.WriteError("Compiler", 0, result.Error);
                    }
                    else
                    {
                        SetStatus("Compilation failed", isError: true);
                    }
                }

                // Show diagnostics (errors/warnings) in console and editor
                if (result.Diagnostics != null)
                {
                    foreach (var diagnostic in result.Diagnostics)
                    {
                        // Only show errors and warnings
                        if (diagnostic.Severity != Microsoft.CodeAnalysis.DiagnosticSeverity.Error &&
                            diagnostic.Severity != Microsoft.CodeAnalysis.DiagnosticSeverity.Warning)
                            continue;

                        var lineSpan = diagnostic.Location.GetLineSpan();
                        var startLine = lineSpan.StartLinePosition.Line + 1;
                        var startCol = lineSpan.StartLinePosition.Character + 1;

                        // Determine file path - use lineSpan.Path or try to find matching project file
                        var filePath = lineSpan.Path;
                        if (string.IsNullOrEmpty(filePath) && _currentProject != null)
                        {
                            // Try to find the file by matching filename in the error
                            filePath = _activeFile?.FilePath ?? "";
                        }

                        // Add to console as clickable error entry
                        var errorCode = diagnostic.Id;
                        var message = $"{errorCode}: {diagnostic.GetMessage()}";
                        Console.ConsoleOutput.Instance.WriteCompilationError(filePath, startLine, startCol, message);

                        // Also highlight in editor if it matches the active file
                        var activePath = _activeFile?.FilePath;
                        bool isMatch = false;

                        if (activePath != null)
                        {
                            if (string.IsNullOrEmpty(lineSpan.Path))
                            {
                                isMatch = true;
                            }
                            else
                            {
                                if (string.Equals(lineSpan.Path, activePath, StringComparison.OrdinalIgnoreCase))
                                    isMatch = true;
                                else if (string.Equals(Path.GetFileName(lineSpan.Path), Path.GetFileName(activePath), StringComparison.OrdinalIgnoreCase))
                                    isMatch = true;
                            }
                        }

                        if (isMatch && TryGetDiagnosticRange(lineSpan, out var markerOffset, out var markerLength))
                        {
                            // Widened range: missing-token diagnostics are zero-width and would otherwise
                            // never be underlined. See TryGetDiagnosticRange.
                            var color = diagnostic.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error ? Colors.Red : Colors.Orange;
                            _textMarkerService?.Create(markerOffset, markerLength, diagnostic.GetMessage(), color);
                        }
                    }
                }

            }
            catch (Exception ex)
            {
                Journal.Error("MW.RUN.THREW", "Run handler threw", ex);
                SetStatus($"Error: {ex.Message}", isError: true);
            }
            finally
            {
                // Flush any pending console output
                Console.ConsoleOutput.Instance.Flush();
                RunButton.IsEnabled = true;

                // The run registered (or stopped registering) mouse handlers, so bring the canvas chrome
                // into line. Done once here rather than per registration, so a Main() that assigns several
                // handlers does not flicker the panel on and off.
                SyncInteractiveModeChrome();
            }
        }
        catch (Exception ex)
        {
            DoodleSharp.Diagnostics.Journal.Error("MW.RUN.CLICK_UNHANDLED", "RunButton_Click threw", ex);
            SetStatus($"RunButton failed: {ex.Message}", isError: true);
        }
    }

    /// <summary>
    /// Compiles and runs the project without the Run button's ceremony — no error dialogs, minimal
    /// status updates. Used by the Global Parameters paths, which re-run the program in response to
    /// a value change rather than to a user pressing Run.
    /// </summary>
    /// <param name="label">
    /// Status/console tag naming the mechanism that asked for this run. It is a parameter rather
    /// than a constant because three unrelated things re-run the code silently, and reporting an
    /// Auto-Run tick as "Parameters:" names a panel the user may not even have open.
    /// </param>
    private async Task RunSilentlyAsync(string label)
    {
        if (_currentProject == null || _currentProject.Files.Count == 0)
            return;

        // Save current editor content
        SaveCurrentEditorContent();

        // Verify entry point exists
        if (_currentProject.EntryPointFile == null)
            return;

        try
        {
            _textMarkerService?.Clear();
            var result = await _compiler.CompileAndExecuteAsync(_currentProject);

            // Apply project settings
            _currentProject.ApplySettings();
            if (_currentProject.ProjectFile.Settings.DefaultCanvasBackgroundColor is string bgCode)
            {
                try { ViewportHost.CanvasBackground = new SolidColorBrush((Color)ColorConverter.ConvertFromString(bgCode)); } catch { }
            }

            if (result.Success)
            {
                _animationStopwatch.Restart();
                _lastAnimationFrameTime = -1;

                // Not Clear(): the canvas delete's own code edit is what starts this auto-run, so
                // clearing here wiped the undo entry for the delete the user had just performed.
                TransactionManager.Instance.PruneAfterCodeRun();

                var shapes = CanvasRenderer.Instance.GetShapes();
                CanvasRenderer.Instance.RenderTo(ViewportHost);

                // Zoom to fit if enabled in settings
                if (ApplicationSettings.Instance.ZoomToFitOnRun && shapes.Count > 0)
                {
                    ViewportHost.ForEach(c =>
                        c.ZoomExtents(CanvasRenderer.Instance.GetShapes(c.OwningViewport!)));
                }

                SetStatus($"{label}: {shapes.Count} shape{(shapes.Count != 1 ? "s" : "")}", isError: false);
                PopulateOutliner(shapes);
            }
            else
            {
                // Show error count in status bar only (no dialogs)
                var errorCount = result.Diagnostics?.Count(d => d.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error) ?? 0;
                if (errorCount > 0)
                {
                    SetStatus($"{label}: {errorCount} error{(errorCount != 1 ? "s" : "")}", isError: true);
                }

                // Add error markers to editor silently
                if (result.Diagnostics != null)
                {
                    foreach (var diagnostic in result.Diagnostics.Where(d =>
                        d.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error ||
                        d.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Warning))
                    {
                        var lineSpan = diagnostic.Location.GetLineSpan();
                        var activePath = _activeFile?.FilePath;
                        if (activePath == null) continue;

                        bool isMatch = string.IsNullOrEmpty(lineSpan.Path) ||
                            string.Equals(lineSpan.Path, activePath, StringComparison.OrdinalIgnoreCase) ||
                            string.Equals(Path.GetFileName(lineSpan.Path), Path.GetFileName(activePath), StringComparison.OrdinalIgnoreCase);

                        if (isMatch && TryGetDiagnosticRange(lineSpan, out var markerOffset, out var markerLength))
                        {
                            var color = diagnostic.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error ? Colors.Red : Colors.Orange;
                            _textMarkerService?.Create(markerOffset, markerLength, diagnostic.GetMessage(), color);
                        }
                    }
                }
            }
        }
        catch
        {
            // Silently ignore errors during auto-update
        }
        finally
        {
            Console.ConsoleOutput.Instance.Flush();
            SyncInteractiveModeChrome();
        }
    }

    private void ClearButton_Click(object sender, RoutedEventArgs e)
    {
        CanvasRenderer.Instance.Clear();
        ViewportHost.ClearShapes();
        TransactionManager.Instance.Clear(); // Clear undo stack
        SetStatus("Canvas cleared", isError: false);
    }

    private void FormatButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var caretOffset = CodeEditor.CaretOffset;
            CodeEditor.Text = CodeFormatter.Format(CodeEditor.Text);

            if (caretOffset <= CodeEditor.Text.Length)
                CodeEditor.CaretOffset = caretOffset;

            SetStatus("Code formatted", isError: false);
        }
        catch (Exception ex)
        {
            SetStatus($"Format error: {ex.Message}", isError: true);
        }
    }

    private void ExportButton_Click(object sender, RoutedEventArgs e)
    {
        var optionsDialog = new ExportOptionsWindow();
        optionsDialog.Owner = this;
        
        // Set default from Application Settings
        if (ApplicationSettings.Instance.DefaultExportBackground != null)
        {
            optionsDialog.SetDefault(ApplicationSettings.Instance.DefaultExportBackground);
        }
        
        optionsDialog.SetGridDefault(ApplicationSettings.Instance.IncludeGridInExport);
        
        if (optionsDialog.ShowDialog() != true) return;

        var dialog = new SaveFileDialog
        {
            Filter = "PNG Image (*.png)|*.png",
            DefaultExt = ".png",
            FileName = "canvas_export"
        };

        if (dialog.ShowDialog() == true)
        {
            try
            {
                int customWidth = optionsDialog.UseCustomSize ? optionsDialog.CustomWidth : 0;
                int customHeight = optionsDialog.UseCustomSize ? optionsDialog.CustomHeight : 0;
                ExportCanvasToPng(dialog.FileName, optionsDialog.SelectedBackground, optionsDialog.IncludeGrid, customWidth, customHeight);
                SetStatus($"Exported: {Path.GetFileName(dialog.FileName)}", isError: false);
            }
            catch (Exception ex)
            {
                SetStatus($"Export error: {ex.Message}", isError: true);
            }
        }
    }

    /// <summary>
    /// Makes sure the canvas is on screen and laid out before a capture reads its size.
    ///
    /// <para>
    /// Every export renders <c>RenderCanvas</c> at its own <c>ActualWidth</c>/<c>ActualHeight</c>,
    /// and a pane that is hidden — or merely sitting on a non-selected tab, which an AvalonDock tab
    /// group unloads — reports zero. Before the panels were dockable that took deliberate effort to
    /// reach; now it is one click away, and the symptom is an "Invalid Canvas Dimensions: 0x0"
    /// exception rather than anything a user could act on.
    /// </para>
    /// </summary>
    private void EnsureCanvasReadyForCapture()
    {
        SetPaneVisible("ds.tool.canvas", true);
        UpdateLayout();
        ViewportHost.UpdateLayout();
    }

    private void ExportCanvasToPng(string filePath, Brush? overrideBackground = null, bool includeGrid = true,
        int customWidth = 0, int customHeight = 0)
    {
        EnsureCanvasReadyForCapture();

        // Save current state
        bool wasGridShown = ViewportHost.ShowGrid;
        var originalBackground = ViewportHost.CanvasBackground;

        // The overlay layer is a visual child of the canvas and every capture below renders the
        // canvas, so without this the F10 readout and any selection handles land in the PNG.
        using var overlayOff = ViewportHost.SuppressOverlayForCapture();

        try
        {
            // Apply export settings
            ViewportHost.ShowGrid = includeGrid;

            // Set the export background (null means use current canvas background)
            if (overrideBackground != null)
            {
                ViewportHost.CanvasBackground = overrideBackground;
            }

            // Allow visual to update
            ViewportHost.UpdateLayout();

            var canvasWidth = (int)ViewportHost.ActualWidth;
            var canvasHeight = (int)ViewportHost.ActualHeight;

            if (canvasWidth <= 0 || canvasHeight <= 0)
                throw new InvalidOperationException($"Invalid Canvas Dimensions: {canvasWidth}x{canvasHeight}");

            bool useCustom = customWidth > 0 && customHeight > 0;
            int outputWidth = useCustom ? customWidth : canvasWidth;
            int outputHeight = useCustom ? customHeight : canvasHeight;

            RenderTargetBitmap rtb;

            if (useCustom)
            {
                // Render canvas at its actual size first
                var canvasRtb = new RenderTargetBitmap(canvasWidth, canvasHeight, 96, 96, PixelFormats.Pbgra32);
                canvasRtb.Render(ViewportHost);

                // Scale uniformly to fit within custom size, preserving aspect ratio
                double scaleX = (double)outputWidth / canvasWidth;
                double scaleY = (double)outputHeight / canvasHeight;
                double uniformScale = Math.Min(scaleX, scaleY);

                double scaledW = canvasWidth * uniformScale;
                double scaledH = canvasHeight * uniformScale;
                double offsetX = (outputWidth - scaledW) / 2;
                double offsetY = (outputHeight - scaledH) / 2;

                var dv = new DrawingVisual();
                using (var dc = dv.RenderOpen())
                {
                    // Fill background for letterbox bars
                    if (overrideBackground != null)
                        dc.DrawRectangle(overrideBackground, null, new Rect(0, 0, outputWidth, outputHeight));
                    else
                        dc.DrawRectangle(originalBackground, null, new Rect(0, 0, outputWidth, outputHeight));

                    dc.DrawImage(canvasRtb, new Rect(offsetX, offsetY, scaledW, scaledH));
                }
                rtb = new RenderTargetBitmap(outputWidth, outputHeight, 96, 96, PixelFormats.Pbgra32);
                rtb.Render(dv);
            }
            else
            {
                rtb = new RenderTargetBitmap(outputWidth, outputHeight, 96, 96, PixelFormats.Pbgra32);
                rtb.Render(ViewportHost);
            }

            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(rtb));

            using var fs = new FileStream(filePath, FileMode.Create);
            encoder.Save(fs);
        }
        finally
        {
            // Restore original state
            ViewportHost.CanvasBackground = originalBackground;
            ViewportHost.ShowGrid = wasGridShown;
            ViewportHost.UpdateLayout();
        }
    }

    private void ExportDxfButton_Click(object sender, RoutedEventArgs e)
    {
        var shapes = CanvasRenderer.Instance.GetShapes();
        if (shapes.Count == 0)
        {
            MessageBox.Show(
                "No shapes to export.\n\nPlease run code that creates shapes before exporting to DXF.",
                "No Shapes",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        var dialog = new SaveFileDialog
        {
            Filter = "AutoCAD DXF (*.dxf)|*.dxf",
            DefaultExt = ".dxf",
            FileName = "shapes_export"
        };

        if (dialog.ShowDialog() == true)
        {
            try
            {
                var exporter = new DxfExporter();
                if (ViewportHost.IsDivided)
                {
                    // R12 DXF has no viewport concept, so a divided drawing is flattened into model
                    // space laid out like the screen. Said out loud, because the coordinates in the
                    // file are then screen distances rather than the ones the code produced.
                    EnsureCanvasReadyForCapture();
                    var flattened = ViewportHost.FlattenForModelSpace();
                    exporter.Export(flattened, dialog.FileName);

                    Console.ConsoleOutput.Instance.WriteLine("Export", 0,
                        $"Exported {ViewportHost.Canvases.Count} viewports tiled into DXF model space. " +
                        "DXF has no viewport concept, so each cell was scaled by its own zoom and " +
                        "moved into place — coordinates in the file are screen distances, not the " +
                        "drawing's own. Export a single undivided viewport for true coordinates.");
                    SetStatus($"Exported {ViewportHost.Canvases.Count} viewports tiled (coordinates rescaled)", isError: false);
                }
                else
                {
                    exporter.Export(shapes, dialog.FileName);
                    SetStatus($"Exported: {Path.GetFileName(dialog.FileName)}", isError: false);
                }
            }
            catch (Exception ex)
            {
                SetStatus($"DXF export error: {ex.Message}", isError: true);
            }
        }
    }

    private void ExportPdfButton_Click(object sender, RoutedEventArgs e)
    {
        var shapes = CanvasRenderer.Instance.GetShapes();
        if (shapes.Count == 0)
        {
            MessageBox.Show(
                "No shapes to export.\n\nPlease run code that creates shapes before exporting to PDF.",
                "No Shapes",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        // Calculate content bounding box
        double minX = double.MaxValue, minY = double.MaxValue;
        double maxX = double.MinValue, maxY = double.MinValue;
        foreach (var drawable in shapes)
        {
            if (drawable is C2VGeometry.Shape shape)
            {
                var bounds = shape.GetBounds();
                minX = Math.Min(minX, bounds.Min.X);
                minY = Math.Min(minY, bounds.Min.Y);
                maxX = Math.Max(maxX, bounds.Max.X);
                maxY = Math.Max(maxY, bounds.Max.Y);
            }
        }
        double contentW = minX == double.MaxValue ? 100 : maxX - minX;
        double contentH = minY == double.MaxValue ? 100 : maxY - minY;

        // Show page setup dialog
        var options = new PdfExportOptionsWindow(contentW, contentH) { Owner = this };
        if (options.ShowDialog() != true) return;

        var dialog = new SaveFileDialog
        {
            Filter = "PDF Document (*.pdf)|*.pdf",
            DefaultExt = ".pdf",
            FileName = "shapes_export"
        };

        if (dialog.ShowDialog() == true)
        {
            try
            {
                var exporter = new PdfExporter();
                if (ViewportHost.IsDivided)
                {
                    // Tiled as it appears on screen. The page-setup scale is not carried across:
                    // "1 unit = N mm" has no single answer once the cells are at different zooms.
                    EnsureCanvasReadyForCapture();
                    var tiles = ViewportHost.GetTiles()
                        .Select(t => new PdfExporter.PdfTile(
                            t.DeviceRect, t.Scale, t.Canvas.Viewport.PanX, t.Canvas.Viewport.PanY, t.Shapes))
                        .ToList();

                    exporter.ExportTiled(tiles, dialog.FileName,
                        ViewportHost.ActualWidth, ViewportHost.ActualHeight, options.MarginMm);
                }
                else
                {
                    exporter.Export(shapes, dialog.FileName,
                        options.PageWidthMm, options.PageHeightMm,
                        options.ScaleMmPerUnit, options.MarginMm);
                }
                SetStatus($"Exported: {Path.GetFileName(dialog.FileName)}", isError: false);
            }
            catch (Exception ex)
            {
                SetStatus($"PDF export error: {ex.Message}", isError: true);
            }
        }
    }

    private void ExportSvgButton_Click(object sender, RoutedEventArgs e)
    {
        var shapes = CanvasRenderer.Instance.GetShapes();
        if (shapes.Count == 0)
        {
            MessageBox.Show(
                "No shapes to export.\n\nPlease run code that creates shapes before exporting to SVG.",
                "No Shapes",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        var dialog = new SaveFileDialog
        {
            Filter = "SVG Image (*.svg)|*.svg",
            DefaultExt = ".svg",
            FileName = "shapes_export"
        };

        if (dialog.ShowDialog() == true)
        {
            try
            {
                if (ViewportHost.IsDivided)
                {
                    // A divided drawing exports tiled, as it appears on screen. An undivided one
                    // keeps the historical path untouched: that one fits the *shapes* with padding
                    // and ignores the view entirely, which is a different picture and the one every
                    // existing export has produced.
                    EnsureCanvasReadyForCapture();
                    var tiles = ViewportHost.GetTiles()
                        .Select(t => new Canvas.SvgExporter.SvgTile(
                            t.DeviceRect, t.Scale, t.Canvas.Viewport.PanX, t.Canvas.Viewport.PanY, t.Shapes))
                        .ToList();

                    Canvas.SvgExporter.SaveTiledToFile(
                        dialog.FileName, tiles, ViewportHost.ActualWidth, ViewportHost.ActualHeight);
                }
                else
                {
                    Canvas.SvgExporter.SaveToFile(dialog.FileName, shapes);
                }
                SetStatus($"Exported: {Path.GetFileName(dialog.FileName)}", isError: false);
            }
            catch (Exception ex)
            {
                SetStatus($"SVG export error: {ex.Message}", isError: true);
            }
        }
    }

    private void ExportGifButton_Click(object sender, RoutedEventArgs e)
    {
        var timeline = CanvasRenderer.Instance.ActiveTimeline;
        if (timeline == null)
        {
            MessageBox.Show(
                "No active animation timeline found.\n\nPlease run code that creates and plays a Timeline before exporting a GIF.",
                "No Animation",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        var optionsDialog = new GifExportOptionsWindow();
        optionsDialog.Owner = this;
        optionsDialog.SetDuration(timeline.Duration);

        if (optionsDialog.ShowDialog() != true) return;

        var dialog = new SaveFileDialog
        {
            Filter = "GIF Animation (*.gif)|*.gif",
            DefaultExt = ".gif",
            FileName = "animation_export"
        };

        if (dialog.ShowDialog() == true)
        {
            // Show progress dialog
            var progressDialog = new ProgressDialog("Exporting GIF animation...");
            progressDialog.Owner = this;
            progressDialog.Show();

            // Set hourglass cursor on main window too
            var originalCursor = Cursor;
            Cursor = System.Windows.Input.Cursors.Wait;

            try
            {
                ExportCanvasToGif(dialog.FileName, timeline, optionsDialog.Duration, optionsDialog.Fps,
                    optionsDialog.SelectedBackground, optionsDialog.IncludeGrid, progressDialog);
                SetStatus($"Exported: {Path.GetFileName(dialog.FileName)}", isError: false);
            }
            catch (Exception ex)
            {
                SetStatus($"Export error: {ex.Message}", isError: true);
            }
            finally
            {
                progressDialog.Close();
                Cursor = originalCursor;
            }
        }
    }

    private void ExportCanvasToGif(string filePath, Timeline timeline, double duration, int fps,
        Brush? overrideBackground, bool includeGrid, ProgressDialog? progressDialog = null)
    {
        EnsureCanvasReadyForCapture();

        // Save current state
        bool wasGridShown = ViewportHost.ShowGrid;
        var originalBackground = ViewportHost.CanvasBackground;
        bool wasPlaying = timeline.IsPlaying;

        // Keep the F10 readout and selection handles out of every captured frame.
        using var overlayOff = ViewportHost.SuppressOverlayForCapture();

        try
        {
            // Apply export settings
            ViewportHost.ShowGrid = includeGrid;
            if (overrideBackground != null)
            {
                ViewportHost.CanvasBackground = overrideBackground;
            }

            var width = (int)ViewportHost.ActualWidth;
            var height = (int)ViewportHost.ActualHeight;

            if (width <= 0 || height <= 0)
                throw new InvalidOperationException($"Invalid Canvas Dimensions: {width}x{height}");

            int totalFrames = (int)(duration * fps);
            int frameDelayMs = 1000 / fps;
            double timeStep = duration / totalFrames;

            using var fs = new FileStream(filePath, FileMode.Create);
            using var encoder = new GifEncoder(fs, width, height, frameDelayMs, repeat: true);

            for (int i = 0; i < totalFrames; i++)
            {
                // Update progress dialog
                progressDialog?.SetProgress(i + 1, totalFrames);

                // Update timeline to this frame's time
                double time = i * timeStep;
                timeline.Update(time);

                // Force canvas to redraw with updated animation state
                ViewportHost.Refresh();

                // Force the dispatcher to process rendering and UI updates
                Dispatcher.Invoke(() => { }, DispatcherPriority.Render);

                // Capture frame
                var rtb = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
                rtb.Render(ViewportHost);

                encoder.AddFrame(rtb);
            }
        }
        finally
        {
            // Restore original state
            ViewportHost.CanvasBackground = originalBackground;
            ViewportHost.ShowGrid = wasGridShown;

            // Restore timeline to end if it was playing
            if (wasPlaying)
            {
                timeline.Update(timeline.Duration);
            }

            ViewportHost.Refresh();
        }
    }

    private void ExportVideoButton_Click(object sender, RoutedEventArgs e)
    {
        var timeline = CanvasRenderer.Instance.ActiveTimeline;
        if (timeline == null)
        {
            MessageBox.Show(
                "No active animation timeline found.\n\nPlease run code that creates and plays a Timeline before exporting a video.",
                "No Animation",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        var optionsDialog = new VideoExportOptionsWindow();
        optionsDialog.Owner = this;
        optionsDialog.SetDuration(timeline.Duration);
        optionsDialog.SetCanvasSize((int)ViewportHost.ActualWidth, (int)ViewportHost.ActualHeight);

        if (optionsDialog.ShowDialog() != true) return;

        var dialog = new SaveFileDialog
        {
            Filter = "MP4 Video (*.mp4)|*.mp4",
            DefaultExt = ".mp4",
            FileName = "animation_export"
        };

        if (dialog.ShowDialog() == true)
        {
            var progressDialog = new ProgressDialog("Exporting MP4 video...");
            progressDialog.Owner = this;
            progressDialog.Show();

            var originalCursor = Cursor;
            Cursor = System.Windows.Input.Cursors.Wait;

            try
            {
                ExportCanvasToVideo(dialog.FileName, timeline, optionsDialog.Duration, optionsDialog.Fps,
                    optionsDialog.Bitrate, optionsDialog.OutputWidth, optionsDialog.OutputHeight,
                    optionsDialog.SelectedBackground, optionsDialog.IncludeGrid, progressDialog);
                SetStatus($"Exported: {Path.GetFileName(dialog.FileName)}", isError: false);
            }
            catch (Exception ex)
            {
                SetStatus($"Export error: {ex.Message}", isError: true);
                MessageBox.Show($"Failed to export video:\n\n{ex.Message}", "Export Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                progressDialog.Close();
                Cursor = originalCursor;
            }
        }
    }

    private void ExportCanvasToVideo(string filePath, Timeline timeline, double duration, int fps,
        uint bitrateMbps, int outputWidth, int outputHeight, Brush? overrideBackground, bool includeGrid,
        ProgressDialog? progressDialog = null)
    {
        EnsureCanvasReadyForCapture();

        bool wasGridShown = ViewportHost.ShowGrid;
        var originalBackground = ViewportHost.CanvasBackground;
        bool wasPlaying = timeline.IsPlaying;

        // Keep the F10 readout and selection handles out of every captured frame.
        using var overlayOff = ViewportHost.SuppressOverlayForCapture();

        try
        {
            ViewportHost.ShowGrid = includeGrid;
            if (overrideBackground != null)
            {
                ViewportHost.CanvasBackground = overrideBackground;
            }

            var canvasWidth = (int)ViewportHost.ActualWidth;
            var canvasHeight = (int)ViewportHost.ActualHeight;

            if (canvasWidth <= 0 || canvasHeight <= 0)
                throw new InvalidOperationException($"Invalid Canvas Dimensions: {canvasWidth}x{canvasHeight}");

            // Ensure output dimensions are even (required for H.264)
            int width = outputWidth - (outputWidth % 2);
            int height = outputHeight - (outputHeight % 2);

            int totalFrames = (int)(duration * fps);
            double timeStep = duration / totalFrames;

            // Check if we need to scale
            bool needsScaling = (width != canvasWidth || height != canvasHeight);

            using var encoder = new Export.VideoExporter(filePath, width, height, fps, bitrateMbps);

            for (int i = 0; i < totalFrames; i++)
            {
                progressDialog?.SetProgress(i + 1, totalFrames);

                double time = i * timeStep;
                timeline.Update(time);

                ViewportHost.Refresh();
                Dispatcher.Invoke(() => { }, DispatcherPriority.Render);

                RenderTargetBitmap rtb;

                if (needsScaling)
                {
                    // Calculate scale factor preserving aspect ratio
                    double scaleX = (double)width / canvasWidth;
                    double scaleY = (double)height / canvasHeight;
                    double scale = Math.Min(scaleX, scaleY);

                    // Calculate dimensions at scaled resolution
                    int scaledPixelWidth = (int)(canvasWidth * scale);
                    int scaledPixelHeight = (int)(canvasHeight * scale);

                    // Render canvas at HIGH DPI to get sharp vector graphics
                    // Higher DPI = WPF renders more pixels for the same logical size
                    double targetDpi = 96 * scale;
                    var canvasRtb = new RenderTargetBitmap(scaledPixelWidth, scaledPixelHeight, targetDpi, targetDpi, PixelFormats.Pbgra32);
                    canvasRtb.Render(ViewportHost);

                    // Calculate centering offset for letterbox/pillarbox
                    double offsetX = (width - scaledPixelWidth) / 2.0;
                    double offsetY = (height - scaledPixelHeight) / 2.0;

                    // Compose final frame with background and centered sharp render
                    var drawingVisual = new DrawingVisual();
                    using (var dc = drawingVisual.RenderOpen())
                    {
                        // Fill background first (for letterbox/pillarbox areas)
                        var bgBrush = overrideBackground ?? ViewportHost.CanvasBackground ?? Brushes.Black;
                        dc.DrawRectangle(bgBrush, null, new Rect(0, 0, width, height));

                        // Draw the high-res render centered (no scaling, 1:1 pixels)
                        dc.DrawImage(canvasRtb, new Rect(offsetX, offsetY, scaledPixelWidth, scaledPixelHeight));
                    }

                    rtb = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
                    rtb.Render(drawingVisual);
                }
                else
                {
                    rtb = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
                    rtb.Render(ViewportHost);
                }

                encoder.AddFrame(rtb);
            }
        }
        finally
        {
            ViewportHost.CanvasBackground = originalBackground;
            ViewportHost.ShowGrid = wasGridShown;

            if (wasPlaying)
            {
                timeline.Update(timeline.Duration);
            }

            ViewportHost.Refresh();
        }
    }

    private void GridMenuItem_Click(object sender, RoutedEventArgs e)
    {
        {
            ViewportHost.ShowGrid = GridMenuItem.IsChecked;

            // Save to application settings
            ApplicationSettings.Instance.ShowGrid = GridMenuItem.IsChecked;
            ApplicationSettings.Save();
        }
    }

    private void InlayHintsMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (_inlayHintGenerator != null)
        {
            _inlayHintGenerator.Enabled = InlayHintsMenuItem.IsChecked;

            // Update hints immediately if enabling
            if (_inlayHintGenerator.Enabled)
            {
                _inlayHintGenerator.UpdateHints(CodeEditor.Text);
            }

            CodeEditor.TextArea.TextView.Redraw();
        }
    }

    private void SemanticHighlightingMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (_semanticHighlighter != null)
        {
            _semanticHighlighter.Enabled = SemanticHighlightingMenuItem.IsChecked;

            // Update highlighting immediately if enabling
            if (_semanticHighlighter.Enabled)
            {
                _ = UpdateSemanticHighlightingAsync();
            }
            else
            {
                _semanticHighlighter.Clear();
            }

            CodeEditor.TextArea.TextView.Redraw();
        }
    }

    private async Task UpdateSemanticHighlightingAsync()
    {
        if (_semanticHighlighter == null || !_semanticHighlighter.Enabled) return;

        try
        {
            if (_completionWorkspace != null && _activeFile != null)
            {
                // Use shared workspace — avoids duplicate compilation
                var fileId = _activeFile.FileName;
                _completionWorkspace.UpdateFile(fileId, CodeEditor.Text);
                await _semanticHighlighter.UpdateTokensAsync(_completionWorkspace, fileId);
            }
            else
            {
                // Fallback: standalone compilation
                var code = CodeEditor.Text;
                var references = _compiler.GetReferences();
                await _semanticHighlighter.UpdateTokensAsync(code, references);
            }

            // Redraw on UI thread
            await Dispatcher.InvokeAsync(() =>
            {
                CodeEditor.TextArea.TextView.Redraw();
            });
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Semantic highlighting error: {ex.Message}");
        }
    }

    private void TriggerSemanticHighlightingUpdate()
    {
        if (_semanticHighlighter == null || !_semanticHighlighter.Enabled) return;

        // Restart the debounce timer
        _semanticUpdateTimer?.Stop();
        _semanticUpdateTimer?.Start();
    }

    private void UpdateCodeLens()
    {
        if (_codeLensGenerator == null || !_codeLensGenerator.Enabled) return;

        try
        {
            _codeLensGenerator.UpdateCodeLens(CodeEditor.Text);
            CodeEditor.TextArea.TextView.Redraw();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Code lens error: {ex.Message}");
        }
    }

    private void CodeLensMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (_codeLensGenerator != null)
        {
            _codeLensGenerator.Enabled = CodeLensMenuItem.IsChecked;

            // Update code lens immediately if enabling
            if (_codeLensGenerator.Enabled)
            {
                UpdateCodeLens();
                SetStatus($"Code Lens enabled ({_codeLensGenerator.ItemCount} item{(_codeLensGenerator.ItemCount != 1 ? "s" : "")})", isError: false);
            }
            else
            {
                SetStatus("Code Lens disabled", isError: false);
            }

            CodeEditor.TextArea.TextView.Redraw();
        }
    }

    #region Windows Menu - Visibility Controls

    /// <summary>
    /// The dockable panels, keyed by the ContentId their pane carries in the XAML — which is also the
    /// identity a saved layout refers to.
    ///
    /// <para>
    /// Each entry pairs a pane with its Windows-menu item and any side effect that must follow the
    /// panel appearing or disappearing. The registry exists so that show/hide has exactly one
    /// implementation: before docking, nine near-identical <c>Set*Visibility</c> methods each poked a
    /// Visibility, a splitter, a GridLength and a MinWidth by hand, and the console's checkmark had to
    /// be re-derived from its tab because the two had drifted apart.
    /// </para>
    /// </summary>
    private readonly Dictionary<string, DockPanelEntry> _dockPanels = new();

    /// <summary>
    /// ContentId to the panel's actual content, which is what a restored layout has to be re-attached
    /// to — the saved file records the arrangement and the ids, never the controls themselves.
    /// </summary>
    private readonly Dictionary<string, object> _dockContent = new();

    private sealed record DockPanelEntry(
        AvalonDock.Layout.LayoutAnchorable Pane,
        MenuItem? MenuItem,
        Action<bool>? OnVisibilityChanged);

    /// <summary>
    /// Guards the visibility handler while the layout itself is being swapped. Deserializing a layout
    /// raises IsVisibleChanged for every pane it touches, and reacting to those would overwrite the
    /// user's saved settings with intermediate states of the restore.
    /// </summary>
    private bool _isApplyingLayout;

    /// <summary>
    /// Wires every dockable panel to its menu item. Called once, from the constructor.
    ///
    /// <para>
    /// The rule that keeps menu and panels in step: a menu click only ever asks the pane to change,
    /// and the pane's own <c>IsVisibleChanged</c> is the single writer of the checkmark and of the
    /// persisted setting. Closing a panel with its X button, dragging the last one out of a pane, and
    /// toggling the menu then all converge on the same code instead of each maintaining the others.
    /// </para>
    /// </summary>
    private void InitializeDockPanels()
    {
        Register("ds.tool.canvas", CanvasPane, ShowCanvasMenuItem, visible =>
        {
            // Running and drawing both need somewhere to draw.
            RunButton.IsEnabled = visible;
            RunMenuItem.IsEnabled = visible;
            DrawMenu.IsEnabled = visible;
        });

        Register("ds.tool.console", ConsolePane, ShowConsoleMenuItem);
        Register("ds.tool.findresults", FindResultsPane, null);
        Register("ds.tool.timeline", TimelinePane, ShowTimelineMenuItem);
        Register("ds.tool.projectbrowser", ProjectBrowserPane, ShowProjectBrowserMenuItem);
        Register("ds.tool.outliner", OutlinerPane, ShowOutlinerMenuItem);

        Register("ds.tool.properties", PropertiesPane, ShowPropertiesMenuItem, visible =>
        {
            if (!visible) return;
            InitializePropertiesPanel();
            DockedPropertiesContainer.Child = _propertiesPanel;
            _propertiesPanel?.UpdateSelection(ViewportHost.SelectedShapes.ToList());
        });

        Register("ds.tool.globalparameters", GlobalParametersPane, ShowGlobalParametersMenuItem, visible =>
        {
            if (!visible) return;
            InitializeGlobalParametersPanel();
            GlobalParamsContainer.Child = _globalParametersPanel;
            _globalParametersPanel?.Rebuild();
        });

        // The two documents are not panels in the registry sense — they have no menu entry, no
        // persisted visibility and CanClose="False" — but their content still has to be recoverable
        // by ContentId, because a restored layout brings *every* pane back empty and ReattachPanelContent
        // can only refill the ones it has content for. Registration is what makes a pane restorable,
        // not its type: with the documents absent from _dockContent, Code and Settings came back as
        // blank tabs on every launch after the first while every tool panel restored correctly.
        CaptureContent("ds.document.code", CodeDocument);
        CaptureContent("ds.document.settings", SettingsDocument);

        void Register(string contentId, AvalonDock.Layout.LayoutAnchorable pane, MenuItem? menuItem,
                      Action<bool>? onChanged = null)
        {
            _dockPanels[contentId] = new DockPanelEntry(pane, menuItem, onChanged);
            CaptureContent(contentId, pane);
            pane.IsVisibleChanged += (_, __) => OnPaneVisibilityChanged(contentId);
        }

        void CaptureContent(string contentId, AvalonDock.Layout.LayoutContent pane)
        {
            if (pane.Content != null) _dockContent[contentId] = pane.Content;
        }
    }

    /// <summary>
    /// The single writer of a panel's menu checkmark and persisted visibility. Fires for a menu
    /// toggle, the pane's own close button, a drag that empties a pane, and a layout restore.
    /// </summary>
    private void OnPaneVisibilityChanged(string contentId)
    {
        if (_isApplyingLayout) return;
        if (!_dockPanels.TryGetValue(contentId, out var entry)) return;

        var visible = entry.Pane.IsVisible;

        if (entry.MenuItem != null) entry.MenuItem.IsChecked = visible;
        entry.OnVisibilityChanged?.Invoke(visible);

        StoreVisibility(contentId, visible);
        ApplicationSettings.Save();
    }

    /// <summary>Shows or hides a panel by ContentId. Hide, never Close — Close is not reversible.</summary>
    private void SetPaneVisible(string contentId, bool visible)
    {
        if (!_dockPanels.TryGetValue(contentId, out var entry)) return;

        if (visible)
        {
            entry.Pane.Show();
            entry.Pane.IsActive = true;
        }
        else
        {
            // Hide moves the pane to LayoutRoot.Hidden, where it remembers the container it came from
            // so a later Show() puts it back where it was. Close would remove it from the tree for
            // good, and its menu item would become a one-way trip.
            entry.Pane.Hide();
        }
    }

    private static void StoreVisibility(string contentId, bool visible)
    {
        var settings = ApplicationSettings.Instance;
        switch (contentId)
        {
            case "ds.tool.canvas": settings.ShowCanvas = visible; break;
            case "ds.tool.console": settings.ShowConsole = visible; break;
            case "ds.tool.timeline": settings.ShowTimeline = visible; break;
            case "ds.tool.projectbrowser": settings.ShowProjectBrowser = visible; break;
            case "ds.tool.outliner": settings.ShowOutliner = visible; break;
            case "ds.tool.properties": settings.ShowProperties = visible; break;
            case "ds.tool.globalparameters": settings.ShowGlobalParameters = visible; break;
        }
    }

    // ── Menu handlers. Each only asks the pane to change; the pane reports back. ─────────────────

    private void ShowCanvasMenuItem_Click(object sender, RoutedEventArgs e)
        => SetPaneVisible("ds.tool.canvas", ShowCanvasMenuItem.IsChecked);

    private void ShowConsoleMenuItem_Click(object sender, RoutedEventArgs e)
        => SetPaneVisible("ds.tool.console", ShowConsoleMenuItem.IsChecked);

    private void ShowTimelineMenuItem_Click(object sender, RoutedEventArgs e)
        => SetPaneVisible("ds.tool.timeline", ShowTimelineMenuItem.IsChecked);

    private void ShowProjectBrowserMenuItem_Click(object sender, RoutedEventArgs e)
        => SetPaneVisible("ds.tool.projectbrowser", ShowProjectBrowserMenuItem.IsChecked);

    private void ShowOutlinerMenuItem_Click(object sender, RoutedEventArgs e)
        => SetPaneVisible("ds.tool.outliner", ShowOutlinerMenuItem.IsChecked);

    private void ShowPropertiesMenuItem_Click(object sender, RoutedEventArgs e)
        => SetPaneVisible("ds.tool.properties", ShowPropertiesMenuItem.IsChecked);

    private void ShowGlobalParametersMenuItem_Click(object sender, RoutedEventArgs e)
        => SetPaneVisible("ds.tool.globalparameters", ShowGlobalParametersMenuItem.IsChecked);

    /// <summary>Brings the Find Results panel forward. Called when a search produces results.</summary>
    private void ShowFindResultsTab() => SetPaneVisible("ds.tool.findresults", true);

    // ── The two panels that are not in the DockingManager ────────────────────────────────────────
    // The Ribbon is a horizontal command strip and the Minimap is bound to the editor's scroll
    // position; floating either would be meaningless. Both keep a plain visibility toggle and their
    // own settings key, which is also why Reset Layout has to restore them separately.

    private void ShowRibbonMenuItem_Click(object sender, RoutedEventArgs e)
    {
        var isVisible = ShowRibbonMenuItem.IsChecked;
        SetRibbonVisibility(isVisible);

        ApplicationSettings.Instance.ShowRibbon = isVisible;
        ApplicationSettings.Save();
    }

    private void SetRibbonVisibility(bool isVisible)
    {
        RibbonPanel.Visibility = isVisible ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>
    /// Applies the saved panel visibility at start-up, for the case where there is no layout to
    /// restore from — a first run, or a layout file that was rejected.
    /// </summary>
    private void ApplyWindowVisibilitySettings()
    {
        var settings = ApplicationSettings.Instance;

        SetPaneVisible("ds.tool.canvas", settings.ShowCanvas);
        SetPaneVisible("ds.tool.console", settings.ShowConsole);
        SetPaneVisible("ds.tool.findresults", false);
        SetPaneVisible("ds.tool.timeline", settings.ShowTimeline);
        SetPaneVisible("ds.tool.projectbrowser", settings.ShowProjectBrowser);
        SetPaneVisible("ds.tool.outliner", settings.ShowOutliner);
        SetPaneVisible("ds.tool.properties", settings.ShowProperties);
        SetPaneVisible("ds.tool.globalparameters", settings.ShowGlobalParameters);

        ShowRibbonMenuItem.IsChecked = settings.ShowRibbon;
        SetRibbonVisibility(settings.ShowRibbon);

        ShowMinimapMenuItem.IsChecked = settings.ShowMinimap;
        SetMinimapVisibility(settings.ShowMinimap);
    }

    #endregion

    #region Docking layout persistence

    /// <summary>
    /// The arrangement declared in the XAML, captured in the constructor before anything mutates it.
    /// Reset Layout restores this.
    /// </summary>
    private string _defaultLayoutXml = string.Empty;

    private static string LayoutFilePath => System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "DoodleSharp", "layout.xml");

    /// <summary>
    /// Serializes the current arrangement.
    ///
    /// <para>
    /// AvalonDock 5 dropped the old <c>XmlLayoutSerializer</c> in favour of a DTO: the layout maps to
    /// a plain object graph, and how that graph reaches disk is the host's business. The DTO carries
    /// <c>[XmlRoot]</c>, so <see cref="System.Xml.Serialization.XmlSerializer"/> is the intended
    /// pairing — and the result stays human-readable, which matters for a file users may need to
    /// delete or send in with a bug report.
    /// </para>
    /// </summary>
    private string SerializeLayout()
    {
        var dto = new AvalonDock.Serialization.LayoutDtoMapper().ToDto(Dock.Layout);

        var serializer = new System.Xml.Serialization.XmlSerializer(dto.GetType());
        using var writer = new System.IO.StringWriter();
        serializer.Serialize(writer, dto);
        return writer.ToString();
    }

    /// <summary>
    /// Replaces the current arrangement. Used by both the start-up restore and Reset Layout, so the
    /// two cannot drift apart.
    /// </summary>
    private void ApplyLayoutXml(string layoutXml)
    {
        if (string.IsNullOrWhiteSpace(layoutXml)) return;

        _isApplyingLayout = true;
        try
        {
            var serializer = new System.Xml.Serialization.XmlSerializer(
                typeof(AvalonDock.Core.Serialization.Dto.LayoutRootDto));

            using var reader = new System.IO.StringReader(layoutXml);
            var dto = (AvalonDock.Core.Serialization.Dto.LayoutRootDto)serializer.Deserialize(reader)!;

            var restored = (AvalonDock.Layout.LayoutRoot)new AvalonDock.Serialization.LayoutDtoMapper().FromDto(dto);

            Dock.Layout = restored;

            ReattachPanelContent();
            RestoreMissingPanels();
            Dock.Layout.CollectGarbage();
            RecoverOffScreenFloatingPanels();
        }
        finally
        {
            _isApplyingLayout = false;
        }

        // The panes settled while the guard was up, so bring the menu back into step in one pass.
        foreach (var contentId in _dockPanels.Keys.ToList())
            OnPaneVisibilityChanged(contentId);
    }

    /// <summary>
    /// Puts the real controls back into the restored panes.
    ///
    /// <para>
    /// A serialized layout records the arrangement and each pane's ContentId — never the controls,
    /// which cannot be serialized and in any case must remain the same instances the code-behind
    /// holds by name. So every restored pane comes back empty and is matched to its content here.
    /// A pane whose id this version no longer knows keeps no content and is dropped by the caller's
    /// <c>CollectGarbage</c>, rather than being restored as an empty panel.
    /// </para>
    /// </summary>
    private void ReattachPanelContent()
    {
        foreach (var content in AvalonDock.Layout.Extensions.Descendents(Dock.Layout).OfType<AvalonDock.Layout.LayoutContent>())
        {
            if (content.ContentId is not string id) continue;

            if (_dockContent.TryGetValue(id, out var control))
            {
                content.Content = control;
            }
            else
            {
                // The pane survives the restore but stays blank, which reads as "the editor is gone"
                // rather than as a wiring mistake. Naming it is the difference between a bug report
                // and a journal line that points at the missing registration.
                Journal.Warn("MW.LAYOUT.NOCONTENT",
                    "A restored pane has no registered content and will render empty",
                    $"contentId={id}");
            }
        }

        // Hidden anchorables live outside the visual tree walk above, but must still be re-attached
        // or showing one from the Windows menu would produce an empty panel.
        foreach (var hidden in Dock.Layout.Hidden)
        {
            if (hidden.ContentId is string id && _dockContent.TryGetValue(id, out var control))
                hidden.Content = control;
        }

        // The registry's pane references now point at the previous layout's objects, so rebuild the
        // map against the restored tree; otherwise every menu toggle would act on a detached pane.
        RebindPanesAfterRestore();
    }

    /// <summary>Re-points the panel registry at the panes belonging to the layout just restored.</summary>
    private void RebindPanesAfterRestore()
    {
        var byId = AvalonDock.Layout.Extensions.Descendents(Dock.Layout)
            .OfType<AvalonDock.Layout.LayoutAnchorable>()
            .Concat(Dock.Layout.Hidden)
            .Where(a => a.ContentId != null)
            .GroupBy(a => a.ContentId!)
            .ToDictionary(g => g.Key, g => g.First());

        foreach (var (contentId, entry) in _dockPanels.ToList())
        {
            if (!byId.TryGetValue(contentId, out var pane) || ReferenceEquals(pane, entry.Pane)) continue;

            _dockPanels[contentId] = entry with { Pane = pane };
            pane.IsVisibleChanged += (_, __) => OnPaneVisibilityChanged(contentId);
        }
    }

    /// <summary>
    /// Re-inserts any registered panel the restored layout does not mention, hidden. Without this a
    /// panel added after a layout was saved simply would not exist, and its menu entry would be inert.
    /// </summary>
    private void RestoreMissingPanels()
    {
        var present = AvalonDock.Layout.Extensions.Descendents(Dock.Layout)
            .OfType<AvalonDock.Layout.LayoutAnchorable>()
            .Concat(Dock.Layout.Hidden)
            .Select(a => a.ContentId)
            .Where(id => id != null)
            .Select(id => id!);

        foreach (var id in Docking.LayoutFile.FindMissingIds(_dockPanels.Keys, present))
        {
            if (!_dockPanels.TryGetValue(id, out var entry)) continue;

            try
            {
                Dock.Layout.Hidden.Add(entry.Pane);
            }
            catch (Exception ex)
            {
                Journal.Warn("MW.LAYOUT.PANELADD",
                    "Could not re-insert a panel missing from the saved layout",
                    $"contentId={id} error={ex.Message}");
            }
        }
    }

    /// <summary>
    /// Brings back any floating panel whose saved position is off the current desktop — the case where
    /// a layout was saved across two monitors and reopened on one.
    /// </summary>
    private void RecoverOffScreenFloatingPanels()
    {
        var desktop = Docking.ScreenBounds.VirtualScreen;

        foreach (var window in Dock.Layout.FloatingWindows.ToList())
        {
            // Position lives on the positionable group that roots the floating window.
            if (window is not AvalonDock.Layout.LayoutAnchorableFloatingWindow { RootPanel: { } panel }) continue;

            var current = new Rect(panel.FloatingLeft, panel.FloatingTop,
                                   panel.FloatingWidth, panel.FloatingHeight);

            var corrected = Docking.ScreenBounds.ClampToVirtualScreen(current, desktop);
            if (corrected == current) continue;

            panel.FloatingLeft = corrected.Left;
            panel.FloatingTop = corrected.Top;
            panel.FloatingWidth = corrected.Width;
            panel.FloatingHeight = corrected.Height;

            Journal.Info("MW.LAYOUT.RECOVER",
                "Moved an off-screen floating panel back onto the desktop",
                $"from={current} to={corrected} desktop={desktop}");
        }
    }

    /// <summary>
    /// Restores the saved arrangement, falling back to the XAML default whenever the file cannot be
    /// trusted. Falling back costs nothing: the default is already loaded, so it just means not
    /// deserializing.
    /// </summary>
    private void RestoreLayout()
    {
        string? saved = null;
        try
        {
            if (System.IO.File.Exists(LayoutFilePath))
                saved = Docking.LayoutFile.Unwrap(System.IO.File.ReadAllText(LayoutFilePath));
        }
        catch (Exception ex)
        {
            Journal.Warn("MW.LAYOUT.READ", "Could not read the saved layout", ex.Message);
        }

        if (saved == null)
        {
            // No layout, or one this version will not accept. The visibility booleans are still
            // meaningful, so honour those on top of the default arrangement.
            ApplyWindowVisibilitySettings();
            return;
        }

        try
        {
            ApplyLayoutXml(saved);
        }
        catch (Exception ex)
        {
            Journal.Warn("MW.LAYOUT.RESTORE", "Saved layout could not be applied; using the default",
                ex.Message);
            TryQuarantineLayoutFile();
            ApplyLayoutXml(_defaultLayoutXml);
            ApplyWindowVisibilitySettings();
        }
    }

    /// <summary>Persists the arrangement. Called once, from the close path.</summary>
    private void SaveLayout()
    {
        try
        {
            var dir = System.IO.Path.GetDirectoryName(LayoutFilePath);
            if (dir != null) System.IO.Directory.CreateDirectory(dir);

            System.IO.File.WriteAllText(LayoutFilePath,
                Docking.LayoutFile.Wrap(SerializeLayout(), UpdateChecker.CurrentVersion?.ToString() ?? ""));
        }
        catch (Exception ex)
        {
            // A layout is a convenience; failing to save one must never obstruct shutdown.
            Journal.Warn("MW.LAYOUT.SAVE", "Could not save the layout", ex.Message);
        }
    }

    /// <summary>Keeps a rejected layout file around once, so a bug report can show what went wrong.</summary>
    private static void TryQuarantineLayoutFile()
    {
        try
        {
            System.IO.File.Copy(LayoutFilePath,
                System.IO.Path.ChangeExtension(LayoutFilePath, ".bad.xml"), overwrite: true);
        }
        catch
        {
            // Best effort only.
        }
    }

    #endregion

    #region Minimap

    private void InitializeMinimap()
    {
        EditorMinimap.AttachToEditor(CodeEditor);

        // Subscribe to marker changes to show errors in minimap
        if (_textMarkerService != null)
        {
            _textMarkerService.MarkersChanged += (s, e) => UpdateMinimapMarkers();
        }
    }

    private void UpdateMinimapMarkers()
    {
        if (_textMarkerService == null || CodeEditor.Document == null) return;

        try
        {
            var markers = _textMarkerService.GetMarkers()
                .Select(m =>
                {
                    var line = CodeEditor.Document.GetLineByOffset(m.StartOffset);
                    return new MinimapMarker
                    {
                        Line = line.LineNumber,
                        Color = m.MarkerColor ?? Colors.Red,
                        Message = m.Message
                    };
                })
                .GroupBy(m => m.Line)
                .Select(g => g.First()) // One marker per line
                .ToList();

            EditorMinimap.UpdateMarkers(markers);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"UpdateMinimapMarkers error: {ex.Message}");
        }
    }

    private void ShowMinimapMenuItem_Click(object sender, RoutedEventArgs e)
    {
        var isVisible = ShowMinimapMenuItem.IsChecked;
        SetMinimapVisibility(isVisible);

        ApplicationSettings.Instance.ShowMinimap = isVisible;
        ApplicationSettings.Save();
    }

    private void SetMinimapVisibility(bool isVisible)
    {
        EditorMinimap.Visibility = isVisible ? Visibility.Visible : Visibility.Collapsed;
        if (isVisible)
        {
            EditorMinimap.ForceRender();
        }
    }

    #endregion

    #region Global Parameters Panel

    private GlobalParametersPanel? _globalParametersPanel;
    private DispatcherTimer? _paramLiveTimer;
    private bool _paramReExecuteInFlight;

    private void InitializeGlobalParametersPanel()
    {
        if (_globalParametersPanel != null) return;

        _globalParametersPanel = new GlobalParametersPanel();
        _globalParametersPanel.ParameterCommitted += OnGlobalParameterCommitted;

        // Live tier. Every slider tick raises Changed; coalesce them onto one short timer so a drag
        // produces a steady stream of re-executions instead of one per pixel.
        _paramLiveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(40) };
        _paramLiveTimer.Tick += async (_, _) =>
        {
            _paramLiveTimer!.Stop();
            await ReExecuteForParametersAsync();
        };

        C2VGeometry.GlobalParameters.Changed += OnGlobalParameterChanged;
        _globalParametersPanel.Rebuild();
    }

    private void OnGlobalParameterChanged(C2VGeometry.Parameter parameter)
    {
        // May arrive off the UI thread (MCP bridge).
        Dispatcher.BeginInvoke(new Action(() =>
        {
            _paramLiveTimer?.Stop();
            _paramLiveTimer?.Start();
        }));
    }

    /// <summary>
    /// Re-runs the user's code for the new parameter values. Uses the resident assembly when one is
    /// available — a parameter change does not touch the source, so the compiled IL is still valid
    /// and skipping Roslyn is what keeps a slider drag interactive.
    /// </summary>
    private Task ReExecuteForParametersAsync() => ReExecuteResidentSilentlyAsync("Parameters");

    /// <summary>
    /// Re-invokes <c>Main()</c> on the already-loaded assembly, falling back to a full run when there
    /// is none. Shared by the Global Parameters tiers and by Auto-Run, which want the same thing for
    /// different reasons: neither has touched the source, so Roslyn has nothing to do.
    ///
    /// <para>
    /// The reason this is not merely an optimisation for Auto-Run is that
    /// <c>CompileAndExecuteAsync</c> clears the canvas <b>before</b> it compiles — so a full run
    /// leaves the drawing blank for the whole compile, which at one run every 500 ms is most of the
    /// time. Here the clear and the re-execute are microseconds apart and nothing is visible.
    /// </para>
    ///
    /// <para>
    /// <paramref name="label"/> is the console/status tag, so the user can tell which mechanism is
    /// re-running their code.
    /// </para>
    /// </summary>
    private async Task ReExecuteResidentSilentlyAsync(string label)
    {
        if (_currentProject == null || _paramReExecuteInFlight) return;

        _paramReExecuteInFlight = true;
        try
        {
            if (!ModuleCompiler.HasResidentAssembly)
            {
                await RunSilentlyAsync(label);
                return;
            }

            var result = await ModuleCompiler.ReExecuteResidentAsync();

            if (result.Success)
            {
                var shapes = CanvasRenderer.Instance.GetShapes();
                CanvasRenderer.Instance.RenderTo(ViewportHost);
                PopulateOutliner(shapes);
                SetStatus($"{label}: {shapes.Count} shape{(shapes.Count != 1 ? "s" : "")}", isError: false);
            }
            else if (!string.IsNullOrEmpty(result.Error))
            {
                SetStatus($"{label} update failed", isError: true);
                Console.ConsoleOutput.Instance.WriteError(label, 0, result.Error!);
            }
        }
        finally
        {
            _paramReExecuteInFlight = false;
        }
    }

    /// <summary>
    /// Commit tier: the user finished an edit, so write the new value back into the
    /// <c>GlobalParameters.Set(...)</c> literal that declared it, then do a full recompile so the
    /// source and the canvas agree. Runs once per edit, never per slider tick.
    /// </summary>
    private async void OnGlobalParameterCommitted(C2VGeometry.Parameter parameter)
    {
        try
        {
            if (_currentProject == null) return;

            // Date parameters are declared from expressions like DateTime.Now; freezing that into a
            // literal would change the program's meaning, so those stay runtime-only.
            if (parameter.Kind == C2VGeometry.ParamKind.Date)
            {
                await ReExecuteForParametersAsync();
                return;
            }

            var reason = TryWriteParameterToSource(parameter);

            if (reason == null)
            {
                // The source changed, so the resident IL is stale — force a real recompile.
                ModuleCompiler.InvalidateResident();
                await RunSilentlyAsync("Parameters");
                _globalParametersPanel?.RefreshValues();
            }
            else
            {
                // Say why, rather than silently leaving code and canvas disagreeing.
                Console.ConsoleOutput.Instance.WriteLine("Parameters", 0,
                    $"'{parameter.Name}' = {parameter.ToLiteral()} applied to this run only — {reason}");
                SetStatus($"'{parameter.Name}' updated for this run only ({reason})", isError: false);
                await ReExecuteForParametersAsync();
            }
        }
        catch (Exception ex)
        {
            DoodleSharp.Diagnostics.Journal.Error("MW.PARAMS.COMMIT_UNHANDLED", "OnGlobalParameterCommitted threw", ex);
            SetStatus($"OnGlobalParameterCommitted failed: {ex.Message}", isError: true);
        }
    }

    /// <summary>
    /// Replaces the declared value in the source file that called <c>GlobalParameters.Set(...)</c>.
    /// When the declaring file is the one on screen the edit goes through the AvalonEdit document so
    /// undo history and caret position survive; otherwise the file's content is updated directly.
    /// </summary>
    /// <returns>null on success, otherwise a short human-readable reason the write-back was skipped.</returns>
    private string? TryWriteParameterToSource(C2VGeometry.Parameter parameter)
    {
        if (string.IsNullOrEmpty(parameter.SourceFile))
            return "no declaration site was recorded";

        var file = _currentProject?.Files.FirstOrDefault(f =>
            string.Equals(f.FilePath, parameter.SourceFile, StringComparison.OrdinalIgnoreCase));

        // The declaration may live in a project file that is not open in a tab.
        if (file == null)
        {
            if (!System.IO.File.Exists(parameter.SourceFile))
                return $"{System.IO.Path.GetFileName(parameter.SourceFile)} is not part of this project";

            try
            {
                var diskContent = System.IO.File.ReadAllText(parameter.SourceFile);
                var rewritten = ParameterCodeWriter.TryRewrite(diskContent, parameter);
                if (rewritten == null)
                    return $"the Set(...) call was not found at {System.IO.Path.GetFileName(parameter.SourceFile)}:{parameter.SourceLine}";
                DoodleSharp.Project.DurableFile.WriteAllText(parameter.SourceFile, rewritten);
                return null;
            }
            catch (Exception ex) { return $"could not write {System.IO.Path.GetFileName(parameter.SourceFile)}: {ex.Message}"; }
        }

        bool isActiveTab = ReferenceEquals(file, _activeFile);
        var source = isActiveTab ? CodeEditor.Text : file.Content;

        if (!ParameterCodeWriter.TryFindValueSpan(source, parameter, out var span))
            return $"the Set(...) call was not found at {file.FileName}:{parameter.SourceLine}";

        var literal = parameter.ToLiteral();
        if (literal == span.CurrentText) return null;   // already in sync

        if (isActiveTab)
        {
            // Surgical replace keeps undo/redo and the caret intact — resetting CodeEditor.Text
            // would scroll the view and blow away the undo stack on every commit.
            CodeEditor.Document.Replace(span.Offset, span.Length, literal);
            SaveCurrentEditorContent();
        }
        else
        {
            file.Content = source.Remove(span.Offset, span.Length).Insert(span.Offset, literal);
            file.HasUnsavedChanges = true;
        }

        return null;
    }



    #endregion

    #region Properties Panel

    private void InitializePropertiesPanel()
    {
        if (_propertiesPanel != null) return;

        _propertiesPanel = new PropertiesPanel();
        _propertiesPanel.ShapePropertyChanged += OnPropertiesPanelPropertyChanged;
        // Flex-slider drag: redraw the canvas only (no source-code sync) so dragging stays smooth.
        _propertiesPanel.ShapeLivePreview += (_, __) => ViewportHost.Refresh();
    }

    #region Interactive mode (user Mouse handlers)

    /// <summary>
    /// True while user code has a mouse handler registered, which puts the canvas into interactive
    /// mode: it stops competing for the mouse, so selection, wheel zoom and double-click zoom-to-fit
    /// are suppressed and the floating navigation controls take their place.
    /// </summary>
    private static bool IsCanvasInteractive => DoodleSharp.Animation.Mouse.HasHandlers;

    /// <summary>
    /// Brings the window chrome into line with interactive mode. Called whenever the handler set
    /// becomes empty or non-empty, and after every run.
    ///
    /// <para>
    /// Idempotent, and deliberately driven off <c>Mouse.HasHandlers</c> rather than tracked separately:
    /// handlers are dropped and re-registered on every run, so a flag maintained alongside them would
    /// be one more thing to get out of step.
    /// </para>
    /// </summary>
    private void SyncInteractiveModeChrome()
    {
        var interactive = IsCanvasInteractive;

        // The navigation overlay is no longer switched here: every cell reveals its own while the
        // pointer is over it, in either mode. Interactive mode's guarantee — that there is still a
        // way to zoom once user code owns the wheel — is unchanged, because hovering is a superset
        // of it.

        // The status-bar hint describes gestures that no longer do what it says once user code owns
        // the mouse. Scroll is the one that is NOT handed over wholesale — the canvas keeps zooming
        // until a wheel handler is registered — so it has to be reported separately, or the hint is
        // wrong in the common case of a sketch that only watches clicks. Read at the end of every run
        // path, which is where the handler set has settled.
        var wheelIsUserCode = DoodleSharp.Animation.Mouse.HasWheelHandler;
        CanvasHintText.Text = interactive
            ? (wheelIsUserCode ? "Mouse: your code | Middle-click: Pan"
                               : "Mouse: your code | Scroll: Zoom | Middle-click: Pan")
            : "Scroll: Zoom | Middle-click: Pan";

        // Selection is suppressed in interactive mode, so the properties panel has nothing to show and
        // no way to be given anything — it edits the selected shape. Hide it rather than leaving a
        // permanently empty panel taking up room, and disable the menu item and F4 so it cannot be
        // brought back into that state.
        ShowPropertiesMenuItem.IsEnabled = !interactive;

        if (interactive)
        {
            // Drop any selection made before the run so no stale handles are left on screen and the
            // outliner does not keep reporting a selection the canvas will not honour.
            ViewportHost.ClearSelection();
            SetPaneVisible("ds.tool.properties", false);

            // Deliberately not persisted: this is a temporary consequence of the running project, not
            // a preference. Leaving ShowProperties alone means the panel comes back by itself when the
            // user runs something that does not register handlers.
            ShowPropertiesMenuItem.IsChecked = false;
        }
        else if (ApplicationSettings.Instance.ShowProperties)
        {
            SetPaneVisible("ds.tool.properties", true);
        }

        ViewportHost.Refresh();
    }

    #endregion






    private void OnPropertiesPanelPropertyChanged(object? sender, ShapePropertyChangedEventArgs e)
    {
        // Refresh canvas
        ViewportHost.Refresh();

        // Update code (suppress auto-update to avoid recompiling and losing in-memory changes)
        var shape = e.Shape;
        if (shape == null || _currentProject == null) return;

        var entryFile = _currentProject.EntryPointFile;
        if (entryFile == null) return;

        var content = entryFile.Content;
        bool codeChanged = false;

        if (e.PropertyName == "Name" && !string.IsNullOrEmpty(e.OldValue))
        {
            // Rename variable throughout code
            var (renamed, found) = Canvas.CodeSyncManager.RenameShapeVariable(content, e.OldValue, shape.Name);
            if (found) { content = renamed; codeChanged = true; }
        }
        else if (e.PropertyName == "Color")
        {
            var (updated, found) = Canvas.CodeSyncManager.UpdateShapeStyleProperty(content, shape, "Color", $"\"{shape.Color}\"");
            if (found) { content = updated; codeChanged = true; }
        }
        else if (e.PropertyName == "FillColor")
        {
            var (updated, found) = Canvas.CodeSyncManager.UpdateShapeStyleProperty(content, shape, "FillColor", $"\"{shape.FillColor}\"");
            if (found) { content = updated; codeChanged = true; }
        }
        else if (e.PropertyName == "LineWeight")
        {
            var (updated, found) = Canvas.CodeSyncManager.UpdateShapeStyleProperty(content, shape, "LineWeight",
                shape.LineWeight.ToString("F1", System.Globalization.CultureInfo.InvariantCulture));
            if (found) { content = updated; codeChanged = true; }
        }
        else if (e.PropertyName == "Opacity")
        {
            var (updated, found) = Canvas.CodeSyncManager.UpdateShapeStyleProperty(content, shape, "Opacity",
                shape.Opacity.ToString("F2", System.Globalization.CultureInfo.InvariantCulture));
            if (found) { content = updated; codeChanged = true; }
        }
        else if (e.PropertyName == "IsVisible")
        {
            var (updated, found) = Canvas.CodeSyncManager.UpdateShapeStyleProperty(content, shape, "IsVisible",
                shape.IsVisible ? "true" : "false");
            if (found) { content = updated; codeChanged = true; }
        }
        else
        {
            // Geometry change - update constructor parameters
            var (newContent, found) = Canvas.CodeSyncManager.UpdateShapeCode(content, shape);
            if (found && newContent != content) { content = newContent; codeChanged = true; }
        }

        if (codeChanged)
        {
            entryFile.Content = content;

            if (_activeFile == entryFile)
            {
                var caretOffset = CodeEditor.CaretOffset;
                CodeEditor.Text = content;
                CodeEditor.CaretOffset = Math.Min(caretOffset, content.Length);
            }

            RefreshFileTabs();
        }
    }



    #endregion

    private void ZoomToShapeMenuItem_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new ZoomToShapeDialog { Owner = this };
        if (dialog.ShowDialog() == true && dialog.ShapeId.HasValue)
        {
            if (ViewportHost.ZoomToShape(dialog.ShapeId.Value))
            {
                SetStatus($"Zoomed to shape ID: {dialog.ShapeId.Value}", isError: false);
            }
            else
            {
                SetStatus($"Shape with ID {dialog.ShapeId.Value} not found", isError: true);
            }
        }
    }

    private void ExitMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (PromptSaveChanges())
        {
            Application.Current.Shutdown();
        }
    }

    private void DuplicateLineMenuItem_Click(object sender, RoutedEventArgs e)
    {
        DuplicateLine();
    }

    private void DeleteLineMenuItem_Click(object sender, RoutedEventArgs e)
    {
        DeleteLine();
    }

    private void MoveLineUpMenuItem_Click(object sender, RoutedEventArgs e)
    {
        MoveLineUp();
    }

    private void MoveLineDownMenuItem_Click(object sender, RoutedEventArgs e)
    {
        MoveLineDown();
    }

    private void RemoveBlankLinesMenuItem_Click(object sender, RoutedEventArgs e)
    {
        var document = CodeEditor.Document;
        if (document.LineCount == 0) return;

        document.BeginUpdate();
        try
        {
            for (int i = document.LineCount; i >= 1; i--)
            {
                var line = document.GetLineByNumber(i);
                var text = document.GetText(line.Offset, line.Length);
                if (string.IsNullOrWhiteSpace(text))
                {
                    document.Remove(line.Offset, line.TotalLength);
                }
            }
        }
        finally
        {
            document.EndUpdate();
        }
    }

    private void AddCursorAboveMenuItem_Click(object sender, RoutedEventArgs e)
    {
        CodeEditor.Focus();
        AddCursorAbove();
    }

    private void AddCursorBelowMenuItem_Click(object sender, RoutedEventArgs e)
    {
        CodeEditor.Focus();
        AddCursorBelow();
    }

    private void ToggleCommentMenuItem_Click(object sender, RoutedEventArgs e)
    {
        ToggleComment();
    }

    private void RefactorMenuItem_Click(object sender, RoutedEventArgs e)
    {
        // Trigger the same logic as the keyboard shortcut
        Rename_Executed(sender, null);
    }

    private void HelpMenuItem_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var helpWindow = new HelpWindow();
            helpWindow.Show();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to open Help window:\n\n{ex}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    /// <summary>
    /// Opens the folder holding the diagnostic journals. Also forces a state dump first, so the file
    /// the user is about to pick up already contains a snapshot of the session as it stands.
    /// </summary>
    private void OpenDiagnosticsFolderMenuItem_Click(object sender, RoutedEventArgs e)
    {
        Journal.Info("MW.DIAG.OPEN_FOLDER", "User opened the diagnostics folder");
        Journal.CaptureState("user requested diagnostics");
        Journal.Flush();
        Journal.OpenFolder();
    }

    private void CopyJournalPathMenuItem_Click(object sender, RoutedEventArgs e)
    {
        var path = Journal.FilePath;
        if (string.IsNullOrEmpty(path))
        {
            SetStatus("Journaling is disabled for this session", isError: true);
            return;
        }

        try
        {
            System.Windows.Clipboard.SetText(path);
            SetStatus($"Journal path copied: {path}", isError: false);
        }
        catch (Exception ex)
        {
            Journal.Warn("MW.DIAG.COPY_FAIL", "Could not copy the journal path", null, ex);
            SetStatus($"Could not copy path: {ex.Message}", isError: true);
        }
    }

    private void AboutMenuItem_Click(object sender, RoutedEventArgs e)
    {
        var version = UpdateChecker.CurrentVersion;
        MessageBox.Show(
            "DoodleSharp - 2D Geometry Visualizer\n\n" +
            "A tool for visualizing 2D geometry using C# code.\n" +
            "Create points, lines, circles, rectangles, and more!\n\n" +
            $"Version v{version}\n" +
            "Licensed under the MIT License.\n\n" +
            "Developed by\n" +
            "Harilal M N\n" +
            "harilalmn@gmail.com",
            "About DoodleSharp",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }

    #endregion

    private void EditMenu_SubmenuOpened(object sender, RoutedEventArgs e)
    {
        // "Active" defined as having an open file.
        InsertColorMenuItem.IsEnabled = _activeFile != null;
    }

    private void InsertColorMenuItem_Click(object sender, RoutedEventArgs e)
    {
        PerformInsertColor();
    }

    private void PerformInsertColor()
    {
        if (_activeFile == null) return;
        
        // Ensure editor accepts input
        if (!CodeEditor.IsKeyboardFocusWithin)
        {
             // If called from menu, we might need to focus. 
             // But if called from shortcut, we want to ensure we don't insert when focus is in e.g. Console.
             // If called from Menu, CodeEditor usually isn't focused momentarilly?
             // Let's try to focus it.
             CodeEditor.Focus();
        }

        var dialog = new ColorPickerDialog();
        dialog.Owner = this;
        if (dialog.ShowDialog() == true)
        {
            CodeEditor.Document.Insert(CodeEditor.CaretOffset, dialog.SelectedColor);
        }
    }

    #region Keyboard Shortcuts

    private void CodeEditor_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Space && Keyboard.Modifiers == ModifierKeys.Control)
        {
            TriggerManualCompletion();
            e.Handled = true;
        }
    }

    private static bool IsTextInputFocused()
    {
        var focused = Keyboard.FocusedElement as DependencyObject;
        while (focused != null)
        {
            if (focused is TextBox || focused is System.Windows.Controls.Primitives.TextBoxBase)
                return true;
            focused = VisualTreeHelper.GetParent(focused);
        }
        return false;
    }

    private void MainWindow_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        // HIGHEST PRIORITY: Drawing input when mouse is over canvas and waiting for next point
        // This intercepts digit keys to start distance input mode for precise drawing
        if (ViewportHost.IsMouseOver &&
            RenderCanvas.DrawingTool.Mode != Canvas.DrawingMode.None &&
            RenderCanvas.DrawingTool.Points.Count > 0)
        {
            var isInInputMode = RenderCanvas.DrawingTool.InputMode != Canvas.DrawingInputMode.None;

            // Tab cycles input modes (None -> Distance -> Angle -> None)
            if (e.Key == Key.Tab)
            {
                e.Handled = true;
                if (RenderCanvas.DrawingTool.CycleInputMode())
                {
                    ViewportHost.Refresh();
                    UpdateDrawingInputStatus();
                }
                return;
            }

            // Escape cancels input mode
            if (e.Key == Key.Escape && isInInputMode)
            {
                RenderCanvas.DrawingTool.HandleEscapeInput();
                ViewportHost.Refresh();
                UpdateDrawingInputStatus();
                e.Handled = true;
                return;
            }

            // Backspace removes last character
            if (e.Key == Key.Back && isInInputMode)
            {
                if (RenderCanvas.DrawingTool.HandleBackspace())
                {
                    ViewportHost.Refresh();
                    UpdateDrawingInputStatus();
                    e.Handled = true;
                }
                return;
            }

            // Number keys - start distance input when drawing
            char? inputChar = null;
            if (e.Key >= Key.D0 && e.Key <= Key.D9)
                inputChar = (char)('0' + (e.Key - Key.D0));
            else if (e.Key >= Key.NumPad0 && e.Key <= Key.NumPad9)
                inputChar = (char)('0' + (e.Key - Key.NumPad0));
            else if (e.Key == Key.OemPeriod || e.Key == Key.Decimal)
                inputChar = '.';
            else if (e.Key == Key.OemMinus || e.Key == Key.Subtract)
                inputChar = '-';

            if (inputChar.HasValue)
            {
                // Start Distance mode if not already in input mode
                if (!isInInputMode)
                {
                    RenderCanvas.DrawingTool.StartDistanceInput();
                }

                if (RenderCanvas.DrawingTool.HandleCharInput(inputChar.Value))
                {
                    ViewportHost.Refresh();
                    UpdateDrawingInputStatus();
                    e.Handled = true;
                }
                return;
            }

            // Enter confirms input and places point
            if (e.Key == Key.Enter && isInInputMode)
            {
                if (RenderCanvas.DrawingTool.HandleEnterInput())
                {
                    var effectivePoint = RenderCanvas.DrawingTool.GetEffectiveEndPoint();
                    if (effectivePoint != null)
                    {
                        // Simulate a click at the effective position
                        RenderCanvas.DrawingTool.OnLeftClick(effectivePoint);
                        ViewportHost.Refresh();
                        UpdateDrawingInputStatus();
                    }
                }
                e.Handled = true;
                return;
            }
        }

        if (Keyboard.Modifiers == ModifierKeys.Control)
        {
            switch (e.Key)
            {
                case Key.Z:
                    if (IsCanvasUndoContext())
                    {
                        PerformUndo();
                        e.Handled = true;
                    }
                    break;
                case Key.Y:
                    if (IsCanvasUndoContext())
                    {
                        PerformRedo();
                        e.Handled = true;
                    }
                    break;
                case Key.N:
                    NewFileButton_Click(sender, e);
                    e.Handled = true;
                    break;
                case Key.O:
                    OpenButton_Click(sender, e);
                    e.Handled = true;
                    break;
                case Key.S:
                    SaveButton_Click(sender, e);
                    e.Handled = true;
                    break;
                case Key.OemQuestion:
                    ToggleComment();
                    e.Handled = true;
                    break;
                case Key.Space:
                    TriggerManualCompletion();
                    e.Handled = true;
                    break;
                case Key.Enter:
                    RunButton_Click(sender, e);
                    e.Handled = true;
                    break;
                case Key.R:
                    ResetLayoutToDefault();
                    e.Handled = true;
                    break;
                case Key.D:
                    AddNextOccurrence();
                    e.Handled = true;
                    break;
                case Key.G:
                    ZoomToShapeMenuItem_Click(sender, e);
                    e.Handled = true;
                    break;
                case Key.M:
                    ToggleMeasuringTool();
                    e.Handled = true;
                    break;
                case Key.F:
                    FindMenuItem_Click(sender, e);
                    e.Handled = true;
                    break;
                case Key.H:
                    FindReplaceMenuItem_Click(sender, e);
                    e.Handled = true;
                    break;
            }
        }
        else if (Keyboard.Modifiers == (ModifierKeys.Control | ModifierKeys.Shift))
        {
            switch (e.Key)
            {
                case Key.F:
                    // Find in Files. The Search menu has advertised this gesture all along, but
                    // nothing ever handled it: Format occupied Ctrl+Shift+F here, so the menu's
                    // InputGestureText was aspirational. Moving Format to Alt+Shift+F freed the
                    // keys and left this arm empty — which made the gesture do nothing at all,
                    // a worse outcome than doing the wrong thing. This is the other half.
                    FindInFilesMenuItem_Click(sender, e);
                    e.Handled = true;
                    break;
                case Key.D:
                    DeleteLine();
                    e.Handled = true;
                    break;
                case Key.N:
                    NewProjectButton_Click(sender, e);
                    e.Handled = true;
                    break;
                case Key.K:
                    PerformInsertColor();
                    e.Handled = true;
                    break;
                case Key.L:
                    SelectAllOccurrences();
                    e.Handled = true;
                    break;
                case Key.M:
                    ShowMinimapMenuItem.IsChecked = !ShowMinimapMenuItem.IsChecked;
                    ShowMinimapMenuItem_Click(sender, e);
                    e.Handled = true;
                    break;
            }
        }
        else if (Keyboard.Modifiers == ModifierKeys.Alt)
        {
            // When Alt is pressed, actual key is in e.SystemKey, not e.Key
            switch (e.SystemKey)
            {
                case Key.Up:
                    MoveLineUp();
                    e.Handled = true;
                    break;
                case Key.Down:
                    MoveLineDown();
                    e.Handled = true;
                    break;
            }
        }
        else if (Keyboard.Modifiers == (ModifierKeys.Shift | ModifierKeys.Alt))
        {
            // When Alt is pressed, actual key is in e.SystemKey, not e.Key
            switch (e.SystemKey)
            {
                case Key.F:
                    FormatButton_Click(sender, e);
                    e.Handled = true;
                    break;
                case Key.Down:
                    CopyLineDown();
                    e.Handled = true;
                    break;
                case Key.Up:
                    CopyLineUp();
                    e.Handled = true;
                    break;
                case Key.Right:
                    ExpandSelection();
                    e.Handled = true;
                    break;
                case Key.Left:
                    ShrinkSelection();
                    e.Handled = true;
                    break;
            }
        }
        else if (Keyboard.Modifiers == (ModifierKeys.Control | ModifierKeys.Alt))
        {
            // Ctrl+Alt+Up/Down: Add cursor above/below (like VSCode)
            var actualKey = e.Key == Key.System ? e.SystemKey : e.Key;
            switch (actualKey)
            {
                case Key.Up:
                    AddCursorAbove();
                    e.Handled = true;
                    break;
                case Key.Down:
                    AddCursorBelow();
                    e.Handled = true;
                    break;
            }
        }
        else if (e.Key == Key.F5)
        {
            RunButton_Click(sender, e);
            e.Handled = true;
        }
        else if (e.Key == Key.F1)
        {
            HelpMenuItem_Click(sender, e);
            e.Handled = true;
        }
        else if (e.Key == Key.F4 && Keyboard.Modifiers == ModifierKeys.None)
        {
            // Inert in interactive mode: the properties panel edits the selected shape, and selection
            // is suppressed while user code owns the mouse. Swallow the key rather than letting it
            // open a panel that could never be given anything.
            if (!IsCanvasInteractive)
            {
                ShowPropertiesMenuItem.IsChecked = !ShowPropertiesMenuItem.IsChecked;
                ShowPropertiesMenuItem_Click(sender, e);
            }
            e.Handled = true;
        }
        else if (e.Key == Key.F6 && Keyboard.Modifiers == ModifierKeys.None)
        {
            ShowGlobalParametersMenuItem.IsChecked = !ShowGlobalParametersMenuItem.IsChecked;
            ShowGlobalParametersMenuItem_Click(sender, e);
            e.Handled = true;
        }
        else if (e.Key == Key.F10 && Keyboard.Modifiers == ModifierKeys.None)
        {
            // Frame-timing readout. A diagnostic, so it is off unless asked for -- FrameMetrics
            // costs nothing while disabled.
            ViewportHost.ShowPerformanceHud = !ViewportHost.ShowPerformanceHud;
            SetStatus($"Performance HUD: {(ViewportHost.ShowPerformanceHud ? "ON" : "OFF")}", isError: false);
            e.Handled = true;
        }
        else if (e.Key == Key.F9 && Keyboard.Modifiers == ModifierKeys.None)
        {
            // Toggle Snap to Grid
            var newValue = !ApplicationSettings.Instance.SnapToGridEnabled;
            ApplicationSettings.Instance.SnapToGridEnabled = newValue;
            SnapToGridCheck.IsChecked = newValue;
            ViewportHost.SnapToGrid = newValue;
            ApplicationSettings.Save();
            SetStatus($"Snap to Grid: {(newValue ? "ON" : "OFF")}", isError: false);
            e.Handled = true;
        }
        // Handle numeric input for drawing tool distance/angle
        else if (!CodeEditor.IsKeyboardFocusWithin && !IsTextInputFocused() && RenderCanvas.DrawingTool.InputMode != Canvas.DrawingInputMode.None)
        {
            // Number keys
            if (e.Key >= Key.D0 && e.Key <= Key.D9)
            {
                var digit = (char)('0' + (e.Key - Key.D0));
                if (RenderCanvas.DrawingTool.HandleCharInput(digit))
                {
                    ViewportHost.Refresh();
                    UpdateDrawingInputStatus();
                    e.Handled = true;
                }
            }
            else if (e.Key >= Key.NumPad0 && e.Key <= Key.NumPad9)
            {
                var digit = (char)('0' + (e.Key - Key.NumPad0));
                if (RenderCanvas.DrawingTool.HandleCharInput(digit))
                {
                    ViewportHost.Refresh();
                    UpdateDrawingInputStatus();
                    e.Handled = true;
                }
            }
            // Decimal point
            else if (e.Key == Key.OemPeriod || e.Key == Key.Decimal)
            {
                if (RenderCanvas.DrawingTool.HandleCharInput('.'))
                {
                    ViewportHost.Refresh();
                    UpdateDrawingInputStatus();
                    e.Handled = true;
                }
            }
            // Minus sign
            else if (e.Key == Key.OemMinus || e.Key == Key.Subtract)
            {
                if (RenderCanvas.DrawingTool.HandleCharInput('-'))
                {
                    ViewportHost.Refresh();
                    UpdateDrawingInputStatus();
                    e.Handled = true;
                }
            }
            // Backspace
            else if (e.Key == Key.Back)
            {
                if (RenderCanvas.DrawingTool.HandleBackspace())
                {
                    ViewportHost.Refresh();
                    UpdateDrawingInputStatus();
                    e.Handled = true;
                }
            }
            // Enter to confirm input and place point
            else if (e.Key == Key.Enter)
            {
                if (RenderCanvas.DrawingTool.HandleEnterInput())
                {
                    // Simulate a click at the effective position
                    var effectivePoint = RenderCanvas.DrawingTool.GetEffectiveEndPoint();
                    if (effectivePoint != null)
                    {
                        RenderCanvas.DrawingTool.OnLeftClick(effectivePoint);
                        ViewportHost.Refresh();
                        UpdateDrawingStatus();
                    }
                    e.Handled = true;
                }
            }
        }
        else if (e.Key == Key.Escape)
        {
            // First check if we need to cancel input mode
            if (RenderCanvas.DrawingTool.InputMode != Canvas.DrawingInputMode.None)
            {
                RenderCanvas.DrawingTool.HandleEscapeInput();
                ViewportHost.Refresh();
                UpdateDrawingStatus();
                e.Handled = true;
            }
            // Cancel drawing tool if active
            else if (RenderCanvas.DrawingTool.Mode != Canvas.DrawingMode.None)
            {
                CancelDrawingTool();
                EnableSelectionMode();
                e.Handled = true;
            }
            // Cancel measuring tool if active
            else if (RenderCanvas.MeasuringTool.Mode == Canvas.ToolMode.Measuring)
            {
                RenderCanvas.MeasuringTool.CancelMeasuring();
                ViewportHost.Refresh();
                SetStatus("Measuring cancelled", isError: false);
                e.Handled = true;
            }
            // Clear selection if in selection mode
            else if (RenderCanvas.IsSelectionMode && ViewportHost.SelectedShapes.Count > 0)
            {
                ViewportHost.ClearSelection();
                ViewportHost.Refresh();
                SetStatus("Selection cleared", isError: false);
                e.Handled = true;
            }
        }
        // Delete key - delete selected shapes (only when no text input is focused)
        else if (e.Key == Key.Delete && !CodeEditor.IsKeyboardFocusWithin && !IsTextInputFocused())
        {
            if (RenderCanvas.IsSelectionMode && ViewportHost.SelectedShapes.Count > 0)
            {
                DeleteSelectedShapes();
                e.Handled = true;
            }
        }
        // Drawing tool shortcuts (only when no text input is focused)
        else if (!CodeEditor.IsKeyboardFocusWithin && !IsTextInputFocused() && Keyboard.Modifiers == ModifierKeys.None)
        {
            switch (e.Key)
            {
                case Key.P:
                    SetDrawingMode(Canvas.DrawingMode.Point);
                    e.Handled = true;
                    break;
                case Key.L:
                    SetDrawingMode(Canvas.DrawingMode.Line);
                    e.Handled = true;
                    break;
                case Key.C:
                    SetDrawingMode(Canvas.DrawingMode.Circle);
                    e.Handled = true;
                    break;
                case Key.R:
                    SetDrawingMode(Canvas.DrawingMode.Rectangle);
                    e.Handled = true;
                    break;
                case Key.A:
                    // Select all shapes (when not in editor)
                    if (RenderCanvas.IsSelectionMode)
                    {
                        // "Select all" means the whole drawing, so every cell selects its own —
                        // a selection cannot span canvases, but the command should not stop at one.
                        ViewportHost.ForEach(c => c.SelectionTool.SelectAll(c.GetCurrentShapes()));
                        ViewportHost.Refresh();
                        var count = RenderCanvas.SelectionTool.SelectedShapes.Count;
                        SetStatus($"Selected {count} shape{(count != 1 ? "s" : "")}", isError: false);
                        e.Handled = true;
                    }
                    break;
            }
        }
    }

    private void DeleteSelectedShapes()
    {
        var selectedShapes = RenderCanvas.SelectionTool.SelectedShapes.ToList();
        if (selectedShapes.Count == 0) return;

        if (_currentProject == null) return;

        // Flush the editor first: the shape's declaration may have been edited since the last run,
        // and matching against the stored copy would either miss it or cut the wrong text.
        SaveCurrentEditorContent();

        // Work out the edits first, then hand them to a command — the deletion has to be undoable
        // as one step covering both the canvas and the code. PlanDeletion searches every file, not
        // just the entry point: a shape can be constructed in any module.
        var (planned, stillToRemove) = Canvas.CodeSyncManager.PlanDeletion(
            _currentProject.Files.Select(f => (File: f, f.Content)), selectedShapes);

        var edits = planned
            .Select(e => new DeleteShapesWithCodeCommand.CodeEdit(e.File, e.Before, e.After))
            .ToList();

        // Clear selection first — the shapes are about to leave the canvas.
        var count = selectedShapes.Count;
        ViewportHost.ClearSelection();

        TransactionManager.Instance.Execute(new DeleteShapesWithCodeCommand(
            selectedShapes, ViewportHost, edits, ApplyFileContentFromCommand));

        // Say plainly when the canvas and the code have diverged. Reporting "Deleted 1 shape" while
        // the declaration is still sitting in the file is how this went unnoticed: the next run just
        // brings the shape back.
        if (stillToRemove.Count > 0)
        {
            var names = string.Join(", ", stillToRemove.Select(s => s.Name ?? s.GetType().Name));
            SetStatus($"Removed {count} shape{(count != 1 ? "s" : "")} from the canvas, but could not " +
                      $"find code for: {names}. Re-running will restore {(stillToRemove.Count == 1 ? "it" : "them")}.",
                      isError: true);
            Journal.Warn("MW.DELETE.CODE_NOT_FOUND", "Shape deleted from canvas but its code was not found",
                $"shapes={names}");
        }
        else
        {
            SetStatus($"Deleted {count} shape{(count != 1 ? "s" : "")} — Ctrl+Z to undo", isError: false);
        }
    }

    /// <summary>
    /// Writes a file's content back into the model, the open editor and the completion workspace.
    /// Handed to <see cref="DeleteShapesWithCodeCommand"/> so that undo and redo go through exactly
    /// the same path as the original edit — the alternative, duplicating this in the command, is how
    /// undo ends up subtly different from the thing it is reversing.
    /// </summary>
    private void ApplyFileContentFromCommand(object fileObj, string content)
    {
        if (fileObj is not VizCodeFile file) return;

        file.Content = content;
        file.HasUnsavedChanges = true;

        if (_activeFile == file)
            ReplaceEditorTextPreservingPosition(content);

        _completionWorkspace?.UpdateFile(file.FileName, content);

        // The source changed, so the resident assembly is stale. Without this a Global Parameters
        // change would re-execute the old IL and put the deleted shape straight back (note 37).
        ModuleCompiler.InvalidateResident();

        RefreshFileTabs();
    }

    #endregion

    #region Measuring Tool

    private void ToggleMeasuringTool()
    {
        var tool = RenderCanvas.MeasuringTool;
        tool.Toggle();

        if (tool.Mode == Canvas.ToolMode.Measuring)
        {
            SetStatus("Measuring: Click first point", isError: false);
            tool.MeasurementCompleted += OnMeasurementCompleted;
            tool.ModeChanged += OnMeasuringModeChanged;
            tool.RefreshSnapSettings();
        }
        else
        {
            tool.MeasurementCompleted -= OnMeasurementCompleted;
            tool.ModeChanged -= OnMeasuringModeChanged;
            SetStatus("Ready", isError: false);
        }

        ViewportHost.Refresh();
    }

    private void OnMeasurementCompleted(object? sender, double distance)
    {
        SetStatus($"Distance: {distance:F2}", isError: false);
    }

    private void OnMeasuringModeChanged(object? sender, Canvas.ToolMode mode)
    {
        if (mode == Canvas.ToolMode.Measuring)
        {
            if (RenderCanvas.MeasuringTool.FirstPoint == null)
            {
                SetStatus("Measuring: Click first point", isError: false);
            }
        }
        else
        {
            SetStatus("Ready", isError: false);
        }
    }

    #endregion

    #region Drawing Tools

    private void SelectTool_Click(object sender, RoutedEventArgs e)
    {
        CancelDrawingTool();
        EnableSelectionMode();
    }

    private void EnableSelectionMode()
    {
        ViewportHost.IsSelectionMode = true;
        RenderCanvas.Cursor = Cursors.Arrow;
        ViewportHost.RefreshSnapSettings();
        SetStatus("Selection mode: Click to select, Shift+Click to add, Ctrl+Click to toggle", isError: false);
        ViewportHost.Refresh();
    }

    private void OnSelectionChanged(object? sender, EventArgs e)
    {
        var selectedShapes = RenderCanvas.SelectionTool.SelectedShapes;

        // Update status bar
        var count = selectedShapes.Count;
        if (count == 0)
        {
            SetStatus("Selection mode: Click to select, Shift+Click to add, Ctrl+Click to toggle", isError: false);
        }
        else if (count == 1)
        {
            var shape = selectedShapes[0];
            var nameInfo = !string.IsNullOrEmpty(shape.Name) ? $" \"{shape.Name}\"" : "";
            SetStatus($"Selected: {shape.GetType().Name}{nameInfo} (ID: {shape.Id})", isError: false);
        }
        else
        {
            SetStatus($"Selected {count} shapes", isError: false);
        }

        // Update properties panel
        _propertiesPanel?.UpdateSelection(selectedShapes.ToList());
    }

    private void OnControlPointDragEnded(object? sender, Canvas.ControlPointDragEndedEventArgs e)
    {
        // Update the code for the dragged shape
        var shape = e.Shape;
        if (shape == null || _currentProject == null) return;

        var entryFile = _currentProject.EntryPointFile;
        if (entryFile == null) return;

        var content = entryFile.Content;

        // Try to update the shape's constructor in code
        var (newContent, found) = Canvas.CodeSyncManager.UpdateShapeCode(content, shape);

        if (found && newContent != content)
        {
            // Update the file content
            entryFile.Content = newContent;

            // Update the editor if this file is currently displayed
            if (_activeFile == entryFile)
            {
                // Save and restore the caret: assigning Text sends it to 0.
                var caretOffset = CodeEditor.CaretOffset;
                CodeEditor.Text = newContent;
                CodeEditor.CaretOffset = Math.Min(caretOffset, newContent.Length);
            }

            // Mark as modified
            RefreshFileTabs();
            SetStatus($"Updated {shape.GetType().Name} in code", isError: false);
        }
    }

    private void DrawPoint_Click(object sender, RoutedEventArgs e)
    {
        SetDrawingMode(Canvas.DrawingMode.Point);
    }

    private void DrawLine_Click(object sender, RoutedEventArgs e)
    {
        SetDrawingMode(Canvas.DrawingMode.Line);
    }

    private void DrawCircle_CenterRadius_Click(object sender, RoutedEventArgs e)
    {
        SetDrawingMode(Canvas.DrawingMode.Circle);
    }

    private void DrawCircle_CenterDiameter_Click(object sender, RoutedEventArgs e)
    {
        SetDrawingMode(Canvas.DrawingMode.CircleDiameter);
    }

    private void DrawCircle_TwoPoints_Click(object sender, RoutedEventArgs e)
    {
        SetDrawingMode(Canvas.DrawingMode.CircleTwoPoints);
    }

    private void DrawCircle_ThreePoints_Click(object sender, RoutedEventArgs e)
    {
        SetDrawingMode(Canvas.DrawingMode.CircleThreePoints);
    }

    private void DrawCircle_TanTanRadius_Click(object sender, RoutedEventArgs e)
    {
        SetStatus("Circle (Tan, Tan, Radius) - Not yet implemented", true);
    }

    private void DrawCircle_TanTanTan_Click(object sender, RoutedEventArgs e)
    {
        SetStatus("Circle (Tan, Tan, Tan) - Not yet implemented", true);
    }

    private void DrawRect_Click(object sender, RoutedEventArgs e)
    {
        SetDrawingMode(Canvas.DrawingMode.Rectangle);
    }

    private void DrawEllipse_Click(object sender, RoutedEventArgs e)
    {
        SetDrawingMode(Canvas.DrawingMode.Ellipse);
    }

    private void DrawArc_ThreePoints_Click(object sender, RoutedEventArgs e)
    {
        SetDrawingMode(Canvas.DrawingMode.Arc);
    }

    private void DrawArc_StartCenterEnd_Click(object sender, RoutedEventArgs e)
    {
        SetDrawingMode(Canvas.DrawingMode.Arc);
        SetStatus("Arc (Start, Center, End) - Click start, center, then end point", false);
    }

    private void DrawArc_StartCenterAngle_Click(object sender, RoutedEventArgs e)
    {
        SetDrawingMode(Canvas.DrawingMode.Arc);
        SetStatus("Arc (Start, Center, Angle) - Click start, center, then sweep angle", false);
    }

    private void DrawArc_StartCenterLength_Click(object sender, RoutedEventArgs e)
    {
        SetDrawingMode(Canvas.DrawingMode.Arc);
        SetStatus("Arc (Start, Center, Length) - Click start, center, then arc length", false);
    }

    private void DrawArc_StartEndAngle_Click(object sender, RoutedEventArgs e)
    {
        SetDrawingMode(Canvas.DrawingMode.Arc);
        SetStatus("Arc (Start, End, Angle) - Click start, end, then sweep angle", false);
    }

    private void DrawArc_StartEndRadius_Click(object sender, RoutedEventArgs e)
    {
        SetDrawingMode(Canvas.DrawingMode.Arc);
        SetStatus("Arc (Start, End, Radius) - Click start, end, then radius point", false);
    }

    private void DrawArc_CenterStartEnd_Click(object sender, RoutedEventArgs e)
    {
        SetDrawingMode(Canvas.DrawingMode.Arc);
        SetStatus("Arc (Center, Start, End) - Click center, start, then end point", false);
    }

    private void DrawArc_CenterStartAngle_Click(object sender, RoutedEventArgs e)
    {
        SetDrawingMode(Canvas.DrawingMode.Arc);
        SetStatus("Arc (Center, Start, Angle) - Click center, start, then sweep angle", false);
    }

    private void DrawArc_CenterStartLength_Click(object sender, RoutedEventArgs e)
    {
        SetDrawingMode(Canvas.DrawingMode.Arc);
        SetStatus("Arc (Center, Start, Length) - Click center, start, then arc length", false);
    }

    private void DrawArc_Continue_Click(object sender, RoutedEventArgs e)
    {
        SetStatus("Arc (Continue) - Not yet implemented", true);
    }

    private void DrawPolygon_Click(object sender, RoutedEventArgs e)
    {
        SetDrawingMode(Canvas.DrawingMode.Polygon);
    }

    private void DrawPolyline_Click(object sender, RoutedEventArgs e)
    {
        SetDrawingMode(Canvas.DrawingMode.Polyline);
    }

    private void DrawBezier_Click(object sender, RoutedEventArgs e)
    {
        SetDrawingMode(Canvas.DrawingMode.Bezier);
    }

    private void DrawSpline_Click(object sender, RoutedEventArgs e)
    {
        SetDrawingMode(Canvas.DrawingMode.Spline);
    }

    private void DrawArrow_Click(object sender, RoutedEventArgs e)
    {
        SetDrawingMode(Canvas.DrawingMode.Arrow);
    }

    private void DrawText_Click(object sender, RoutedEventArgs e)
    {
        SetDrawingMode(Canvas.DrawingMode.Text);
    }

    private void SetDrawingMode(Canvas.DrawingMode mode)
    {
        // Cancel measuring tool if active
        if (RenderCanvas.MeasuringTool.Mode == Canvas.ToolMode.Measuring)
        {
            RenderCanvas.MeasuringTool.CancelMeasuring();
        }

        // Disable selection mode when drawing
        ViewportHost.IsSelectionMode = false;
        ViewportHost.ClearSelection();

        var tool = RenderCanvas.DrawingTool;
        tool.SetMode(mode);

        // Update UI
        UpdateDrawingToolbarButtons();
        SetStatus(tool.StatusMessage, isError: false);

        // Set crosshair cursor on canvas
        RenderCanvas.Cursor = Cursors.Cross;

        // Subscribe to events
        tool.ShapeCompleted -= OnShapeCompleted;
        tool.ModeChanged -= OnDrawingModeChanged;
        tool.TextPlacementRequested -= OnTextPlacementRequested;
        tool.ShapeCompleted += OnShapeCompleted;
        tool.ModeChanged += OnDrawingModeChanged;
        tool.TextPlacementRequested += OnTextPlacementRequested;
        tool.RefreshSnapSettings();

        ViewportHost.Refresh();
    }

    private void CancelDrawingTool()
    {
        var tool = RenderCanvas.DrawingTool;
        tool.Cancel();
        UpdateDrawingToolbarButtons();

        // Reset cursor to normal
        RenderCanvas.Cursor = Cursors.Arrow;

        ViewportHost.Refresh();
    }

    private void UpdateDrawingStatus()
    {
        var tool = RenderCanvas.DrawingTool;
        SetStatus(tool.StatusMessage, isError: false);
    }

    private void UpdateDrawingInputStatus()
    {
        var tool = RenderCanvas.DrawingTool;
        if (tool.InputMode != Canvas.DrawingInputMode.None)
        {
            var inputText = tool.GetInputDisplayText();
            var hint = "(Tab: cycle, Enter: confirm, Esc: cancel)";
            SetStatus($"{inputText}  {hint}", isError: false);
        }
        else
        {
            SetStatus(tool.StatusMessage, isError: false);
        }
    }

    private void OnShapeCompleted(object? sender, C2VGeometry.Shape shape)
    {
        // Sync counters from existing code to avoid duplicate variable names
        var existingCode = _currentProject?.EntryPointFile?.Content ?? "";
        Canvas.CodeGenerator.SyncCountersFromCode(existingCode);

        var code = Canvas.CodeGenerator.GenerateCode(shape);
        InsertShapeCode(code);

        // Add the shape directly to the canvas (no need to run code)
        RenderCanvas.AddShape(shape);

        // Update status
        var tool = RenderCanvas.DrawingTool;
        SetStatus(tool.StatusMessage, isError: false);
    }

    private void OnTextPlacementRequested(object? sender, C2VGeometry.VXYZ location)
    {
        // Show dialog to get text content
        var dialog = new System.Windows.Window
        {
            Title = "Enter Text",
            Width = 350,
            Height = 180,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Owner = this,
            ResizeMode = ResizeMode.NoResize,
            Background = (Brush)FindResource("BackgroundBrush"),
            WindowStyle = WindowStyle.ToolWindow
        };

        var panel = new StackPanel { Margin = new Thickness(15) };
        var label = new TextBlock
        {
            Text = "Enter text content:",
            Foreground = (Brush)FindResource("ForegroundBrush"),
            Margin = new Thickness(0, 0, 0, 8)
        };
        var textBox = new System.Windows.Controls.TextBox
        {
            Background = (Brush)FindResource("SecondaryBackgroundBrush"),
            Foreground = (Brush)FindResource("ForegroundBrush"),
            BorderBrush = (Brush)FindResource("BorderBrush"),
            Padding = new Thickness(8, 6, 8, 6),
            FontSize = 14
        };
        var buttonPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 12, 0, 0)
        };
        var okButton = new Button
        {
            Content = "OK",
            Width = 80,
            Margin = new Thickness(0, 0, 8, 0),
            Style = (Style)FindResource("RibbonButtonStyle"),
            IsDefault = true
        };
        var cancelButton = new Button
        {
            Content = "Cancel",
            Width = 80,
            Style = (Style)FindResource("RibbonButtonStyle"),
            IsCancel = true
        };

        okButton.Click += (s, e) => { dialog.DialogResult = true; dialog.Close(); };
        cancelButton.Click += (s, e) => { dialog.DialogResult = false; dialog.Close(); };

        buttonPanel.Children.Add(okButton);
        buttonPanel.Children.Add(cancelButton);
        panel.Children.Add(label);
        panel.Children.Add(textBox);
        panel.Children.Add(buttonPanel);
        dialog.Content = panel;

        dialog.Loaded += (s, e) => textBox.Focus();

        if (dialog.ShowDialog() == true && !string.IsNullOrWhiteSpace(textBox.Text))
        {
            // Complete the text shape
            var tool = RenderCanvas.DrawingTool;
            tool.CompleteText(location, textBox.Text);
        }
    }

    private void OnDrawingModeChanged(object? sender, Canvas.DrawingMode mode)
    {
        UpdateDrawingToolbarButtons();
        if (mode == Canvas.DrawingMode.None)
        {
            // Return to selection mode
            EnableSelectionMode();
        }
        else
        {
            var tool = RenderCanvas.DrawingTool;
            SetStatus(tool.StatusMessage, isError: false);
        }
    }

    private void UpdateDrawingToolbarButtons()
    {
        // Toolbar has been removed - this method is now a no-op
    }

    private void InsertShapeCode(string code)
    {
        var entryFile = _currentProject?.EntryPointFile;
        if (entryFile == null) return;

        var content = entryFile.Content;
        var insertPos = FindMainMethodInsertPosition(content);
        if (insertPos < 0) return;

        // Insert the code with 12-space indent (inside Main body)
        var indentedCode = "            " + code + Environment.NewLine;
        var newContent = content.Insert(insertPos, indentedCode);

        // Update the file content
        entryFile.Content = newContent;
        entryFile.HasUnsavedChanges = true;

        // Update the editor if this is the active file
        if (_activeFile == entryFile)
        {
            var caretPos = CodeEditor.CaretOffset;
            CodeEditor.Text = newContent;
            // Try to restore caret position
            if (caretPos <= insertPos)
            {
                CodeEditor.CaretOffset = caretPos;
            }
            else
            {
                CodeEditor.CaretOffset = caretPos + indentedCode.Length;
            }
        }
    }

    private int FindMainMethodInsertPosition(string content)
    {
        // C# syntax: "public static void Main()"
        var mainIndex = content.IndexOf("public static void Main");
        if (mainIndex < 0) return -1;

        // Find the opening brace of Main()
        var braceStart = content.IndexOf('{', mainIndex);
        if (braceStart < 0) return -1;

        // Find matching closing brace
        int braceCount = 1;
        int pos = braceStart + 1;
        int lastNewline = pos;

        while (pos < content.Length && braceCount > 0)
        {
            if (content[pos] == '{') braceCount++;
            else if (content[pos] == '}') braceCount--;
            if (content[pos] == '\n') lastNewline = pos + 1;
            pos++;
        }

        // Insert before the closing brace line
        return lastNewline;
    }

    #endregion

    #region Editor Line Operations

    private void DuplicateLine() => CopyLineDown();

    private void CopyLineDown()
    {
        if (!CodeEditor.IsKeyboardFocusWithin) return;

        var document = CodeEditor.Document;
        var textArea = CodeEditor.TextArea;
        var selection = textArea.Selection;

        // Determine the range of lines to duplicate
        int startLine, endLine;
        if (selection.IsEmpty)
        {
            // No selection - duplicate current line
            startLine = endLine = textArea.Caret.Line;
        }
        else
        {
            // Has selection - get all lines in selection
            var selStart = selection.SurroundingSegment.Offset;
            var selEnd = selection.SurroundingSegment.EndOffset;
            startLine = document.GetLineByOffset(selStart).LineNumber;
            endLine = document.GetLineByOffset(selEnd).LineNumber;

            // If selection ends at the very start of a line, don't include that line
            var endLineObj = document.GetLineByNumber(endLine);
            if (selEnd == endLineObj.Offset && endLine > startLine)
            {
                endLine--;
            }
        }

        // Get the text of all lines to duplicate
        var firstLine = document.GetLineByNumber(startLine);
        var lastLine = document.GetLineByNumber(endLine);
        var textToDuplicate = document.GetText(firstLine.Offset, lastLine.EndOffset - firstLine.Offset);

        // Insert the duplicated text after the last line
        var insertOffset = lastLine.EndOffset;
        document.Insert(insertOffset, Environment.NewLine + textToDuplicate);

        // Move caret down by the number of lines duplicated
        var lineCount = endLine - startLine + 1;
        textArea.Caret.Line = textArea.Caret.Line + lineCount;
    }

    private void CopyLineUp()
    {
        if (!CodeEditor.IsKeyboardFocusWithin) return;

        var document = CodeEditor.Document;
        var textArea = CodeEditor.TextArea;
        var selection = textArea.Selection;

        // Determine the range of lines to duplicate
        int startLine, endLine;
        if (selection.IsEmpty)
        {
            startLine = endLine = textArea.Caret.Line;
        }
        else
        {
            var selStart = selection.SurroundingSegment.Offset;
            var selEnd = selection.SurroundingSegment.EndOffset;
            startLine = document.GetLineByOffset(selStart).LineNumber;
            endLine = document.GetLineByOffset(selEnd).LineNumber;

            var endLineObj = document.GetLineByNumber(endLine);
            if (selEnd == endLineObj.Offset && endLine > startLine)
            {
                endLine--;
            }
        }

        // Get the text of all lines to duplicate
        var firstLine = document.GetLineByNumber(startLine);
        var lastLine = document.GetLineByNumber(endLine);
        var textToDuplicate = document.GetText(firstLine.Offset, lastLine.EndOffset - firstLine.Offset);

        // Insert the duplicated text before the first line
        var insertOffset = firstLine.Offset;
        document.Insert(insertOffset, textToDuplicate + Environment.NewLine);

        // Caret stays at same position (which is now in the duplicated text)
    }

    private void DeleteLine()
    {
        if (!CodeEditor.IsKeyboardFocusWithin) return;

        var document = CodeEditor.Document;
        var caret = CodeEditor.TextArea.Caret;
        var line = document.GetLineByNumber(caret.Line);

        var deleteLength = line.TotalLength;
        if (line.LineNumber == document.LineCount && line.LineNumber > 1)
        {
            var prevLine = document.GetLineByNumber(line.LineNumber - 1);
            document.Remove(prevLine.EndOffset, line.EndOffset - prevLine.EndOffset);
        }
        else
        {
            document.Remove(line.Offset, deleteLength);
        }
    }

    private void MoveLineUp()
    {
        if (!CodeEditor.IsKeyboardFocusWithin) return;

        var document = CodeEditor.Document;
        var textArea = CodeEditor.TextArea;
        var selection = textArea.Selection;

        // Get the range of lines to move
        int startLine, endLine;
        bool hadSelection = !selection.IsEmpty;
        if (selection.IsEmpty)
        {
            startLine = endLine = textArea.Caret.Line;
        }
        else
        {
            startLine = document.GetLineByOffset(selection.SurroundingSegment.Offset).LineNumber;
            endLine = document.GetLineByOffset(selection.SurroundingSegment.EndOffset).LineNumber;
            // If selection ends at start of a line, don't include that line
            var endLineObj = document.GetLineByNumber(endLine);
            if (selection.SurroundingSegment.EndOffset == endLineObj.Offset && endLine > startLine)
                endLine--;
        }

        if (startLine <= 1) return;

        var firstLine = document.GetLineByNumber(startLine);
        var lastLine = document.GetLineByNumber(endLine);
        var lineAbove = document.GetLineByNumber(startLine - 1);

        var selectedText = document.GetText(firstLine.Offset, lastLine.EndOffset - firstLine.Offset);
        var aboveText = document.GetText(lineAbove.Offset, lineAbove.Length);

        document.BeginUpdate();
        try
        {
            // Remove the selected lines and the line above, then insert in swapped order
            int blockStart = lineAbove.Offset;
            int blockLength = lastLine.EndOffset - lineAbove.Offset;

            // Build the new text: selected lines + newline + line that was above
            string newText = selectedText + Environment.NewLine + aboveText;

            document.Replace(blockStart, blockLength, newText);
        }
        finally
        {
            document.EndUpdate();
        }

        // Always restore selection at new position to allow continuous moving
        var newFirstLine = document.GetLineByNumber(startLine - 1);
        var newLastLine = document.GetLineByNumber(endLine - 1);

        // Select from start of first line to end of last line
        textArea.Caret.Position = new ICSharpCode.AvalonEdit.TextViewPosition(startLine - 1, 1);
        textArea.Selection = ICSharpCode.AvalonEdit.Editing.Selection.Create(textArea, newFirstLine.Offset, newLastLine.EndOffset);
    }

    private void MoveLineDown()
    {
        if (!CodeEditor.IsKeyboardFocusWithin) return;

        var document = CodeEditor.Document;
        var textArea = CodeEditor.TextArea;
        var selection = textArea.Selection;

        // Get the range of lines to move
        int startLine, endLine;
        bool hadSelection = !selection.IsEmpty;
        if (selection.IsEmpty)
        {
            startLine = endLine = textArea.Caret.Line;
        }
        else
        {
            startLine = document.GetLineByOffset(selection.SurroundingSegment.Offset).LineNumber;
            endLine = document.GetLineByOffset(selection.SurroundingSegment.EndOffset).LineNumber;
            // If selection ends at start of a line, don't include that line
            var endLineObj = document.GetLineByNumber(endLine);
            if (selection.SurroundingSegment.EndOffset == endLineObj.Offset && endLine > startLine)
                endLine--;
        }

        if (endLine >= document.LineCount) return;

        var firstLine = document.GetLineByNumber(startLine);
        var lastLine = document.GetLineByNumber(endLine);
        var lineBelow = document.GetLineByNumber(endLine + 1);

        var selectedText = document.GetText(firstLine.Offset, lastLine.EndOffset - firstLine.Offset);
        var belowText = document.GetText(lineBelow.Offset, lineBelow.Length);

        document.BeginUpdate();
        try
        {
            // Remove the selected lines and the line below, then insert in swapped order
            int blockStart = firstLine.Offset;
            int blockLength = lineBelow.EndOffset - firstLine.Offset;

            // Build the new text: line that was below + newline + selected lines
            string newText = belowText + Environment.NewLine + selectedText;

            document.Replace(blockStart, blockLength, newText);
        }
        finally
        {
            document.EndUpdate();
        }

        // Always restore selection at new position to allow continuous moving
        var newFirstLine = document.GetLineByNumber(startLine + 1);
        var newLastLine = document.GetLineByNumber(endLine + 1);

        // Select from start of first line to end of last line
        textArea.Caret.Position = new ICSharpCode.AvalonEdit.TextViewPosition(startLine + 1, 1);
        textArea.Selection = ICSharpCode.AvalonEdit.Editing.Selection.Create(textArea, newFirstLine.Offset, newLastLine.EndOffset);
    }

    private void AddCursorAbove()
    {
        if (!CodeEditor.IsKeyboardFocusWithin) return;
        _multiSelectionRenderer?.AddCursorAbove();
    }

    private void AddCursorBelow()
    {
        if (!CodeEditor.IsKeyboardFocusWithin) return;
        _multiSelectionRenderer?.AddCursorBelow();
    }

    private void ToggleComment()
    {
        if (!CodeEditor.IsKeyboardFocusWithin) return;

        var document = CodeEditor.Document;
        var selection = CodeEditor.TextArea.Selection;

        if (selection.IsEmpty)
        {
            var caret = CodeEditor.TextArea.Caret;
            var line = document.GetLineByNumber(caret.Line);
            var lineText = document.GetText(line.Offset, line.Length);
            var trimmedText = lineText.TrimStart();

            if (trimmedText.StartsWith("//"))
            {
                var commentIndex = lineText.IndexOf("//", StringComparison.Ordinal);
                var removeLength = lineText.Length > commentIndex + 2 && lineText[commentIndex + 2] == ' ' ? 3 : 2;
                document.Remove(line.Offset + commentIndex, removeLength);
            }
            else
            {
                var insertIndex = lineText.Length - trimmedText.Length;
                document.Insert(line.Offset + insertIndex, "// ");
            }
        }
        else
        {
            var startLine = selection.StartPosition.Line;
            var endLine = selection.EndPosition.Line;

            var allCommented = true;
            for (var i = startLine; i <= endLine; i++)
            {
                var line = document.GetLineByNumber(i);
                var lineText = document.GetText(line.Offset, line.Length).TrimStart();
                if (!string.IsNullOrEmpty(lineText) && !lineText.StartsWith("//"))
                {
                    allCommented = false;
                    break;
                }
            }

            document.BeginUpdate();
            try
            {
                for (var i = endLine; i >= startLine; i--)
                {
                    var line = document.GetLineByNumber(i);
                    var lineText = document.GetText(line.Offset, line.Length);
                    var trimmedText = lineText.TrimStart();

                    if (allCommented)
                    {
                        if (trimmedText.StartsWith("//"))
                        {
                            var commentIndex = lineText.IndexOf("//", StringComparison.Ordinal);
                            var removeLength = lineText.Length > commentIndex + 2 && lineText[commentIndex + 2] == ' ' ? 3 : 2;
                            document.Remove(line.Offset + commentIndex, removeLength);
                        }
                    }
                    else
                    {
                        var insertIndex = lineText.Length - trimmedText.Length;
                        document.Insert(line.Offset + insertIndex, "// ");
                    }
                }
            }
            finally
            {
                document.EndUpdate();
            }
        }
    }

    #endregion

    #region Selection Operations

    // Stack to track selection expansion history for shrinking
    private readonly Stack<(int Start, int Length)> _selectionHistory = new();

    private void ExpandSelection()
    {
        if (!CodeEditor.IsKeyboardFocusWithin) return;

        var document = CodeEditor.Document;
        var textArea = CodeEditor.TextArea;
        var selection = textArea.Selection;

        int currentStart, currentLength;
        if (selection.IsEmpty)
        {
            currentStart = textArea.Caret.Offset;
            currentLength = 0;
        }
        else
        {
            var segment = selection.SurroundingSegment;
            currentStart = segment.Offset;
            currentLength = segment.Length;
        }

        // Save current selection for shrinking
        _selectionHistory.Push((currentStart, currentLength));

        // Determine what to expand to
        var text = document.Text;
        int newStart = currentStart;
        int newEnd = currentStart + currentLength;

        if (currentLength == 0)
        {
            // No selection - select current word
            (newStart, newEnd) = GetWordBounds(text, currentStart);
        }
        else
        {
            // Try expanding to larger constructs
            var currentText = text.Substring(currentStart, currentLength);

            // If word selected, try to expand to quoted string or parentheses
            var (wordStart, wordEnd) = GetWordBounds(text, currentStart);
            if (newStart == wordStart && newEnd == wordEnd)
            {
                // Try to expand to enclosing brackets/quotes
                var (bracketStart, bracketEnd) = GetEnclosingBrackets(text, currentStart, currentLength);
                if (bracketStart < newStart || bracketEnd > newEnd)
                {
                    newStart = bracketStart;
                    newEnd = bracketEnd;
                }
                else
                {
                    // Expand to line
                    var line = document.GetLineByOffset(currentStart);
                    newStart = line.Offset;
                    newEnd = line.EndOffset;
                }
            }
            else if (IsEntireLine(document, currentStart, currentLength))
            {
                // Expand to include more lines or block
                var (blockStart, blockEnd) = GetEnclosingBlock(text, currentStart, currentLength);
                newStart = blockStart;
                newEnd = blockEnd;
            }
            else
            {
                // Expand to line
                var startLine = document.GetLineByOffset(currentStart);
                var endLine = document.GetLineByOffset(currentStart + currentLength);
                newStart = startLine.Offset;
                newEnd = endLine.EndOffset;
            }
        }

        // Apply new selection
        if (newStart != currentStart || newEnd != currentStart + currentLength)
        {
            textArea.Selection = ICSharpCode.AvalonEdit.Editing.Selection.Create(textArea, newStart, newEnd);
            textArea.Caret.Offset = newEnd;
        }
    }

    private void ShrinkSelection()
    {
        if (!CodeEditor.IsKeyboardFocusWithin) return;
        if (_selectionHistory.Count == 0) return;

        var textArea = CodeEditor.TextArea;
        var (start, length) = _selectionHistory.Pop();

        if (length == 0)
        {
            textArea.ClearSelection();
            textArea.Caret.Offset = start;
        }
        else
        {
            textArea.Selection = ICSharpCode.AvalonEdit.Editing.Selection.Create(textArea, start, start + length);
            textArea.Caret.Offset = start + length;
        }
    }

    private (int Start, int End) GetWordBounds(string text, int offset)
    {
        if (offset >= text.Length) return (offset, offset);

        int start = offset;
        int end = offset;

        // Expand backwards
        while (start > 0 && (char.IsLetterOrDigit(text[start - 1]) || text[start - 1] == '_'))
            start--;

        // Expand forwards
        while (end < text.Length && (char.IsLetterOrDigit(text[end]) || text[end] == '_'))
            end++;

        return (start, end);
    }

    private (int Start, int End) GetEnclosingBrackets(string text, int start, int length)
    {
        var brackets = new Dictionary<char, char>
        {
            { ')', '(' }, { ']', '[' }, { '}', '{' }, { '>', '<' }, { '"', '"' }, { '\'', '\'' }
        };

        int searchStart = start;
        int searchEnd = start + length;

        // Search outward for enclosing brackets
        for (int i = start - 1; i >= 0; i--)
        {
            char c = text[i];
            if (c == '(' || c == '[' || c == '{')
            {
                // Find matching closing bracket
                int depth = 1;
                for (int j = searchEnd; j < text.Length; j++)
                {
                    if (text[j] == c) depth++;
                    else if (text[j] == brackets.FirstOrDefault(x => x.Value == c).Key)
                    {
                        depth--;
                        if (depth == 0)
                        {
                            return (i, j + 1);
                        }
                    }
                }
            }
            else if (c == '"' || c == '\'')
            {
                // Find matching quote
                for (int j = searchEnd; j < text.Length; j++)
                {
                    if (text[j] == c && (j == 0 || text[j - 1] != '\\'))
                    {
                        return (i, j + 1);
                    }
                }
            }
        }

        return (start, start + length);
    }

    private (int Start, int End) GetEnclosingBlock(string text, int start, int length)
    {
        // Find enclosing braces
        int braceDepth = 0;
        int blockStart = start;

        for (int i = start - 1; i >= 0; i--)
        {
            if (text[i] == '}') braceDepth++;
            else if (text[i] == '{')
            {
                if (braceDepth == 0)
                {
                    blockStart = i;
                    break;
                }
                braceDepth--;
            }
        }

        braceDepth = 0;
        int blockEnd = start + length;

        for (int i = start + length; i < text.Length; i++)
        {
            if (text[i] == '{') braceDepth++;
            else if (text[i] == '}')
            {
                if (braceDepth == 0)
                {
                    blockEnd = i + 1;
                    break;
                }
                braceDepth--;
            }
        }

        return (blockStart, blockEnd);
    }

    private bool IsEntireLine(ICSharpCode.AvalonEdit.Document.TextDocument document, int start, int length)
    {
        var startLine = document.GetLineByOffset(start);
        var endLine = document.GetLineByOffset(start + length);
        return start == startLine.Offset && start + length == endLine.EndOffset;
    }

    private string? _lastSearchText;

    private void AddNextOccurrence()
    {
        if (!CodeEditor.IsKeyboardFocusWithin) return;
        if (_multiSelectionRenderer == null) return;

        var document = CodeEditor.Document;
        var textArea = CodeEditor.TextArea;
        var selection = textArea.Selection;

        string searchText;
        int searchFrom;

        if (selection.IsEmpty && !_multiSelectionRenderer.HasSelections)
        {
            // No selection - select current word first
            var (wordStart, wordEnd) = GetWordBounds(document.Text, textArea.Caret.Offset);
            if (wordStart == wordEnd) return;

            searchText = document.GetText(wordStart, wordEnd - wordStart);
            _isAddingNextOccurrence = true;
            textArea.Selection = ICSharpCode.AvalonEdit.Editing.Selection.Create(textArea, wordStart, wordEnd);
            textArea.Caret.Offset = wordEnd;
            _isAddingNextOccurrence = false;
            _lastSearchText = searchText;
            return;
        }

        // Get selected text (from current selection or last search)
        var segment = selection.SurroundingSegment;
        searchText = document.GetText(segment.Offset, segment.Length);
        _lastSearchText = searchText;

        // Search for next occurrence after current selection
        searchFrom = segment.EndOffset;
        var text = document.Text;
        var nextIndex = text.IndexOf(searchText, searchFrom, StringComparison.Ordinal);

        // Wrap around if not found
        if (nextIndex < 0)
        {
            nextIndex = text.IndexOf(searchText, 0, StringComparison.Ordinal);
        }

        // Check if this occurrence is already selected (in main selection or multi-selections)
        if (nextIndex >= 0)
        {
            // Check if already in multi-selections
            bool alreadySelected = false;
            foreach (var sel in _multiSelectionRenderer.Selections)
            {
                if (sel.StartOffset == nextIndex && sel.Length == searchText.Length)
                {
                    alreadySelected = true;
                    break;
                }
            }
            // Also check if it's the current main selection
            if (segment.Offset == nextIndex && segment.Length == searchText.Length)
            {
                alreadySelected = true;
            }

            if (alreadySelected)
            {
                // All occurrences already selected
                return;
            }

            // Add current selection to the multi-selection renderer before moving
            _multiSelectionRenderer.AddSelection(segment.Offset, segment.Length);

            // Move caret selection to the new occurrence
            _isAddingNextOccurrence = true;
            textArea.Selection = ICSharpCode.AvalonEdit.Editing.Selection.Create(textArea, nextIndex, nextIndex + searchText.Length);
            textArea.Caret.Offset = nextIndex + searchText.Length;
            _isAddingNextOccurrence = false;

            // Scroll to make visible
            textArea.Caret.BringCaretToView();
        }
    }

    private void SelectAllOccurrences()
    {
        if (!CodeEditor.IsKeyboardFocusWithin) return;
        if (_multiSelectionRenderer == null) return;

        var document = CodeEditor.Document;
        var textArea = CodeEditor.TextArea;
        var selection = textArea.Selection;

        string searchText;

        if (selection.IsEmpty)
        {
            // No selection - use word at caret
            var (wordStart, wordEnd) = GetWordBounds(document.Text, textArea.Caret.Offset);
            if (wordStart == wordEnd) return;
            searchText = document.GetText(wordStart, wordEnd - wordStart);
        }
        else
        {
            var segment = selection.SurroundingSegment;
            searchText = document.GetText(segment.Offset, segment.Length);
        }

        // Find all occurrences
        var text = document.Text;
        var occurrences = new List<(int Start, int End)>();
        int index = 0;

        while ((index = text.IndexOf(searchText, index, StringComparison.Ordinal)) >= 0)
        {
            occurrences.Add((index, index + searchText.Length));
            index += searchText.Length;
        }

        if (occurrences.Count <= 1) return;

        _isAddingNextOccurrence = true;
        try
        {
            // Clear existing multi-selections
            _multiSelectionRenderer.ClearSelections();

            // Add all occurrences except the last one to multi-selection renderer
            for (int i = 0; i < occurrences.Count - 1; i++)
            {
                var occ = occurrences[i];
                _multiSelectionRenderer.AddSelection(occ.Start, occ.End - occ.Start);
            }

            // Set main selection to the last occurrence
            var last = occurrences[^1];
            textArea.Selection = ICSharpCode.AvalonEdit.Editing.Selection.Create(textArea, last.Start, last.End);
            textArea.Caret.Offset = last.End;
            textArea.Caret.BringCaretToView();
        }
        finally
        {
            _isAddingNextOccurrence = false;
        }

        SetStatus($"Selected {occurrences.Count} occurrences of \"{searchText}\"", false);
    }

    #endregion

    private void SetStatus(string message, bool isError)
    {
        StatusText.Text = message;
        if (isError)
        {
            StatusText.Foreground = new SolidColorBrush(Colors.OrangeRed);
        }
        else
        {
            StatusText.Foreground = (SolidColorBrush)FindResource("ForegroundBrush");
        }
    }

    #region Folding

    private void UpdateFoldings()
    {
        if (_foldingStrategy != null && _foldingManager != null)
        {
            _foldingStrategy.UpdateFoldings(_foldingManager, CodeEditor.Document);
        }
    }

    #endregion

    #region Project Browser

    private void LoadProjectTree()
    {
        if (_currentProject == null) return;

        var root = new ProjectTreeItem
        {
            Name = Path.GetFileName(_currentProject.ProjectDirectory) ?? "Project",
            FullPath = _currentProject.ProjectDirectory,
            IsDirectory = true
        };

        // Add References virtual node
        var referencesNode = new ProjectTreeItem
        {
            Name = "References",
            FullPath = string.Empty,
            IsDirectory = false,
            IsReferencesNode = true
        };

        // Populate references from project file
        if (_currentProject.ProjectFile?.References != null)
        {
            foreach (var asmRef in _currentProject.ProjectFile.References)
            {
                referencesNode.Children.Add(new ProjectTreeItem
                {
                    Name = asmRef.ToString(),
                    FullPath = asmRef.Path,
                    IsDirectory = false,
                    IsReferenceItem = true
                });
            }
        }

        root.Children.Add(referencesNode);

        BuildProjectTree(root);
        
        ProjectTreeView.ItemsSource = new ObservableCollection<ProjectTreeItem> { root };
    }

    private void BuildProjectTree(ProjectTreeItem item)
    {
        if (!item.IsDirectory) return;

        try
        {
            // Directories
            foreach (var dir in Directory.GetDirectories(item.FullPath))
            {
                var dirItem = new ProjectTreeItem
                {
                    Name = Path.GetFileName(dir),
                    FullPath = dir,
                    IsDirectory = true
                };
                BuildProjectTree(dirItem);
                item.Children.Add(dirItem);
            }

            // Files
            foreach (var file in Directory.GetFiles(item.FullPath))
            {
                if (file.EndsWith(".vizproj", StringComparison.OrdinalIgnoreCase))
                    continue;

                var fileItem = new ProjectTreeItem
                {
                    Name = Path.GetFileName(file),
                    FullPath = file,
                    IsDirectory = false
                };
                item.Children.Add(fileItem);
            }
        }
        catch (Exception ex)
        {
            SetStatus($"Error building tree: {ex.Message}", true);
        }
    }

    private void ProjectTreeView_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (ProjectTreeView.SelectedItem is ProjectTreeItem item)
        {
            // Handle References node - keep double-click for this dialog
            if (item.IsReferencesNode)
            {
                if (_currentProject != null)
                {
                    var dialog = new AddReferenceWindow(_currentProject);
                    dialog.Owner = this;
                    if (dialog.ShowDialog() == true)
                    {
                        LoadProjectTree(); // Refresh to show updated references
                        SetStatus("References updated", isError: false);
                    }
                }
                return;
            }
        }
    }

    private void ProjectTreeView_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (e.NewValue is ProjectTreeItem item)
        {
            OpenFileFromProjectTree(item);
        }
    }

    private void OpenFileFromProjectTree(ProjectTreeItem item)
    {
        // Ignore reference items
        if (item.IsReferencesNode || item.IsReferenceItem)
        {
            return;
        }

        // Handle regular files (ignore directories for opening)
        if (!item.IsDirectory)
        {
            // Check if file is already loaded
            var existingFile = _currentProject?.Files.FirstOrDefault(f => f.FilePath.Equals(item.FullPath, StringComparison.OrdinalIgnoreCase));

            if (existingFile != null)
            {
                // Reopen the tab if it was closed
                if (!existingFile.IsOpen)
                {
                    existingFile.IsOpen = true;
                    RefreshFileTabs();
                }
                SelectFile(existingFile);
            }
            else if (File.Exists(item.FullPath) && _currentProject != null)
            {
                try
                {
                    // Open generic file
                    var newFile = new VizCodeFile
                    {
                        FilePath = item.FullPath,
                        Content = File.ReadAllText(item.FullPath),
                        HasUnsavedChanges = false
                    };
                    
                    _currentProject.Files.Add(newFile);
                    RefreshFileTabs();
                    SelectFile(newFile);
                }
                catch (Exception ex)
                {
                        SetStatus($"Error opening file: {ex.Message}", true);
                }
            }
        }
    }

    #region Project Tree Context Menu

    private void ContextMenu_NewFile_Click(object sender, RoutedEventArgs e)
    {
        if (_currentProject == null) return;

        var item = GetContextMenuTargetItem(sender);
        if (item == null) return;

        // Determine target directory
        var targetDir = item.IsDirectory ? item.FullPath : Path.GetDirectoryName(item.FullPath);
        if (string.IsNullOrEmpty(targetDir)) return;

        // Prompt for file name
        var fileName = PromptForInput("New File", "Enter file name:", GetDefaultNewFileName());
        if (string.IsNullOrEmpty(fileName)) return;

        // Ensure correct extension
        var ext = Path.GetExtension(fileName);
        if (string.IsNullOrEmpty(ext))
        {
            fileName += ".cs";
        }

        var fullPath = Path.Combine(targetDir, fileName);

        if (File.Exists(fullPath))
        {
            MessageBox.Show($"File '{fileName}' already exists.", "File Exists", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        try
        {
            // Create file with template
            var projectName = _currentProject.ProjectFile.Name;
            var className = Path.GetFileNameWithoutExtension(fileName);
            var content = Templates.GetEmptyModuleTemplate(projectName, className);

            DoodleSharp.Project.DurableFile.WriteAllText(fullPath, content);

            // Add to project and open
            var newFile = new VizCodeFile
            {
                FilePath = fullPath,
                Content = content,
                HasUnsavedChanges = false
            };
            _currentProject.Files.Add(newFile);

            LoadProjectTree();
            RefreshFileTabs();
            SelectFile(newFile);
            SetStatus($"Created: {fileName}", isError: false);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Error creating file: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void ContextMenu_NewFolder_Click(object sender, RoutedEventArgs e)
    {
        if (_currentProject == null) return;

        var item = GetContextMenuTargetItem(sender);
        if (item == null) return;

        // Determine target directory
        var targetDir = item.IsDirectory ? item.FullPath : Path.GetDirectoryName(item.FullPath);
        if (string.IsNullOrEmpty(targetDir)) return;

        // Prompt for folder name
        var folderName = PromptForInput("New Folder", "Enter folder name:", "NewFolder");
        if (string.IsNullOrEmpty(folderName)) return;

        var fullPath = Path.Combine(targetDir, folderName);

        if (Directory.Exists(fullPath))
        {
            MessageBox.Show($"Folder '{folderName}' already exists.", "Folder Exists", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        try
        {
            Directory.CreateDirectory(fullPath);
            LoadProjectTree();
            SetStatus($"Created folder: {folderName}", isError: false);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Error creating folder: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void ContextMenu_Rename_Click(object sender, RoutedEventArgs e)
    {
        if (_currentProject == null) return;

        var item = GetContextMenuTargetItem(sender);
        if (item == null || item.IsReferencesNode || item.IsReferenceItem) return;

        // Don't allow renaming entry point
        if (!item.IsDirectory && IsEntryPointFile(item.FullPath))
        {
            MessageBox.Show("Cannot rename the entry point file.", "Cannot Rename", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var currentName = item.Name;
        var newName = PromptForInput("Rename", $"Enter new name for '{currentName}':", currentName);
        if (string.IsNullOrEmpty(newName) || newName == currentName) return;

        var parentDir = Path.GetDirectoryName(item.FullPath);
        if (string.IsNullOrEmpty(parentDir)) return;

        var newPath = Path.Combine(parentDir, newName);

        try
        {
            if (item.IsDirectory)
            {
                if (Directory.Exists(newPath))
                {
                    MessageBox.Show($"Folder '{newName}' already exists.", "Folder Exists", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
                Directory.Move(item.FullPath, newPath);

                // Update any open files that were in this directory
                foreach (var file in _currentProject.Files)
                {
                    if (file.FilePath.StartsWith(item.FullPath, StringComparison.OrdinalIgnoreCase))
                    {
                        file.FilePath = file.FilePath.Replace(item.FullPath, newPath);
                    }
                }
            }
            else
            {
                if (File.Exists(newPath))
                {
                    MessageBox.Show($"File '{newName}' already exists.", "File Exists", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
                File.Move(item.FullPath, newPath);

                // Update open file reference
                var openFile = _currentProject.Files.FirstOrDefault(f => f.FilePath.Equals(item.FullPath, StringComparison.OrdinalIgnoreCase));
                if (openFile != null)
                {
                    openFile.FilePath = newPath;
                }
            }

            LoadProjectTree();
            RefreshFileTabs();
            SetStatus($"Renamed to: {newName}", isError: false);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Error renaming: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void ContextMenu_Delete_Click(object sender, RoutedEventArgs e)
    {
        if (_currentProject == null) return;

        var item = GetContextMenuTargetItem(sender);
        if (item == null || item.IsReferencesNode || item.IsReferenceItem) return;

        // Don't allow deleting entry point
        if (!item.IsDirectory && IsEntryPointFile(item.FullPath))
        {
            MessageBox.Show("Cannot delete the entry point file.", "Cannot Delete", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var itemType = item.IsDirectory ? "folder" : "file";
        var result = MessageBox.Show(
            $"Are you sure you want to delete the {itemType} '{item.Name}'?\n\nThis action cannot be undone.",
            "Confirm Delete",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (result != MessageBoxResult.Yes) return;

        try
        {
            if (item.IsDirectory)
            {
                // Close any open files from this directory
                var filesToClose = _currentProject.Files
                    .Where(f => f.FilePath.StartsWith(item.FullPath, StringComparison.OrdinalIgnoreCase))
                    .ToList();
                foreach (var file in filesToClose)
                {
                    _currentProject.Files.Remove(file);
                }

                Directory.Delete(item.FullPath, true);
            }
            else
            {
                // Close file if open
                var openFile = _currentProject.Files.FirstOrDefault(f => f.FilePath.Equals(item.FullPath, StringComparison.OrdinalIgnoreCase));
                if (openFile != null)
                {
                    _currentProject.Files.Remove(openFile);
                }

                File.Delete(item.FullPath);
            }

            LoadProjectTree();
            RefreshFileTabs();

            // Select first available file if current was deleted
            if (_activeFile == null || !_currentProject.Files.Contains(_activeFile))
            {
                var firstFile = _currentProject.Files.FirstOrDefault();
                if (firstFile != null) SelectFile(firstFile);
            }

            SetStatus($"Deleted: {item.Name}", isError: false);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Error deleting: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void ContextMenu_GoToLocation_Click(object sender, RoutedEventArgs e)
    {
        var item = GetContextMenuTargetItem(sender);
        if (item == null) return;

        try
        {
            if (item.IsReferencesNode) return;

            if (item.IsDirectory)
            {
                // Open the folder itself in Explorer
                System.Diagnostics.Process.Start("explorer.exe", item.FullPath);
            }
            else if (!string.IsNullOrEmpty(item.FullPath) && (File.Exists(item.FullPath) || item.IsReferenceItem))
            {
                // Open Explorer and select the file
                System.Diagnostics.Process.Start("explorer.exe", $"/select,\"{item.FullPath}\"");
            }
        }
        catch (Exception ex)
        {
            SetStatus($"Error opening location: {ex.Message}", true);
        }
    }

    private void ProjectTreeView_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        // Select the TreeViewItem under the mouse
        var treeViewItem = FindAncestor<TreeViewItem>((DependencyObject)e.OriginalSource);
        if (treeViewItem != null)
        {
            treeViewItem.IsSelected = true;
            treeViewItem.Focus();

            // Show context menu
            var contextMenu = CreateProjectTreeContextMenu();
            contextMenu.PlacementTarget = treeViewItem;
            contextMenu.IsOpen = true;
            e.Handled = true;
        }
    }

    private void ProjectTreeView_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _dragStartPoint = e.GetPosition(ProjectTreeView);
        _isDragging = false;
    }

    private void ProjectTreeView_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed) return;

        var currentPos = e.GetPosition(ProjectTreeView);
        var diff = currentPos - _dragStartPoint;

        if (Math.Abs(diff.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(diff.Y) < SystemParameters.MinimumVerticalDragDistance)
            return;

        if (_isDragging) return;

        var treeViewItem = FindAncestor<TreeViewItem>((DependencyObject)e.OriginalSource);
        if (treeViewItem == null) return;

        var item = treeViewItem.DataContext as ProjectTreeItem;
        if (item == null || item.IsReferencesNode || item.IsReferenceItem) return;

        // Don't allow dragging the root project node
        if (_currentProject != null && item.FullPath == _currentProject.ProjectDirectory) return;

        // Don't allow dragging entry point file
        if (!item.IsDirectory && IsEntryPointFile(item.FullPath)) return;

        _isDragging = true;
        var data = new DataObject("ProjectTreeItem", item);
        DragDrop.DoDragDrop(treeViewItem, data, DragDropEffects.Move);
        _isDragging = false;
    }

    private void ProjectTreeView_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = DragDropEffects.None;

        if (!e.Data.GetDataPresent("ProjectTreeItem")) return;

        var targetTreeViewItem = FindAncestor<TreeViewItem>((DependencyObject)e.OriginalSource);
        if (targetTreeViewItem == null) return;

        var targetItem = targetTreeViewItem.DataContext as ProjectTreeItem;
        var draggedItem = e.Data.GetData("ProjectTreeItem") as ProjectTreeItem;
        if (targetItem == null || draggedItem == null) return;

        // Determine target directory
        string targetDir;
        if (targetItem.IsDirectory)
            targetDir = targetItem.FullPath;
        else if (!string.IsNullOrEmpty(targetItem.FullPath))
            targetDir = Path.GetDirectoryName(targetItem.FullPath) ?? "";
        else
            return;

        // Don't allow dropping onto itself or its own parent directory
        var sourceDir = Path.GetDirectoryName(draggedItem.FullPath) ?? "";
        if (string.Equals(targetDir, sourceDir, StringComparison.OrdinalIgnoreCase)) return;

        // Don't allow dropping a folder into itself or its own subtree
        if (draggedItem.IsDirectory && targetDir.StartsWith(draggedItem.FullPath, StringComparison.OrdinalIgnoreCase)) return;

        // Don't allow dropping onto references
        if (targetItem.IsReferencesNode || targetItem.IsReferenceItem) return;

        e.Effects = DragDropEffects.Move;
        e.Handled = true;
    }

    private void ProjectTreeView_Drop(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent("ProjectTreeItem")) return;
        if (_currentProject == null) return;

        var targetTreeViewItem = FindAncestor<TreeViewItem>((DependencyObject)e.OriginalSource);
        if (targetTreeViewItem == null) return;

        var targetItem = targetTreeViewItem.DataContext as ProjectTreeItem;
        var draggedItem = e.Data.GetData("ProjectTreeItem") as ProjectTreeItem;
        if (targetItem == null || draggedItem == null) return;

        // Determine target directory
        string targetDir;
        if (targetItem.IsDirectory)
            targetDir = targetItem.FullPath;
        else if (!string.IsNullOrEmpty(targetItem.FullPath))
            targetDir = Path.GetDirectoryName(targetItem.FullPath) ?? "";
        else
            return;

        var sourceDir = Path.GetDirectoryName(draggedItem.FullPath) ?? "";
        if (string.Equals(targetDir, sourceDir, StringComparison.OrdinalIgnoreCase)) return;
        if (draggedItem.IsDirectory && targetDir.StartsWith(draggedItem.FullPath, StringComparison.OrdinalIgnoreCase)) return;

        var newPath = Path.Combine(targetDir, draggedItem.Name);

        try
        {
            if (draggedItem.IsDirectory)
            {
                if (Directory.Exists(newPath))
                {
                    MessageBox.Show($"Folder '{draggedItem.Name}' already exists in the target location.", "Cannot Move", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
                Directory.Move(draggedItem.FullPath, newPath);

                // Update any open files that were in this directory
                foreach (var file in _currentProject.Files)
                {
                    if (file.FilePath.StartsWith(draggedItem.FullPath, StringComparison.OrdinalIgnoreCase))
                    {
                        file.FilePath = file.FilePath.Replace(draggedItem.FullPath, newPath);
                    }
                }
            }
            else
            {
                if (File.Exists(newPath))
                {
                    MessageBox.Show($"File '{draggedItem.Name}' already exists in the target location.", "Cannot Move", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
                File.Move(draggedItem.FullPath, newPath);

                // Update open file reference
                var openFile = _currentProject.Files.FirstOrDefault(f => f.FilePath.Equals(draggedItem.FullPath, StringComparison.OrdinalIgnoreCase));
                if (openFile != null)
                {
                    openFile.FilePath = newPath;
                }
            }

            LoadProjectTree();
            RefreshFileTabs();
            SetStatus($"Moved '{draggedItem.Name}' to {Path.GetFileName(targetDir)}", isError: false);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Error moving: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private ContextMenu CreateProjectTreeContextMenu()
    {
        var menu = new ContextMenu
        {
            Background = (SolidColorBrush)FindResource("SecondaryBackgroundBrush"),
            BorderBrush = (SolidColorBrush)FindResource("BorderBrush"),
            Foreground = (SolidColorBrush)FindResource("ForegroundBrush")
        };

        var newFileItem = new MenuItem { Header = "New File" };
        newFileItem.Icon = new Image { Source = new BitmapImage(new Uri("/img/file.png", UriKind.Relative)), Width = 16, Height = 16 };
        newFileItem.Click += ContextMenu_NewFile_Click;
        menu.Items.Add(newFileItem);

        var newFolderItem = new MenuItem { Header = "New Folder" };
        newFolderItem.Icon = new Image { Source = new BitmapImage(new Uri("/img/folder.png", UriKind.Relative)), Width = 16, Height = 16 };
        newFolderItem.Click += ContextMenu_NewFolder_Click;
        menu.Items.Add(newFolderItem);

        menu.Items.Add(new Separator { Background = (SolidColorBrush)FindResource("BorderBrush") });

        var renameItem = new MenuItem { Header = "Rename" };
        renameItem.Click += ContextMenu_Rename_Click;
        menu.Items.Add(renameItem);

        var deleteItem = new MenuItem { Header = "Delete", Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#E74C3C")) };
        deleteItem.Click += ContextMenu_Delete_Click;
        menu.Items.Add(deleteItem);

        menu.Items.Add(new Separator { Background = (SolidColorBrush)FindResource("BorderBrush") });

        var goToLocationItem = new MenuItem { Header = "Go to Location" };
        goToLocationItem.Click += ContextMenu_GoToLocation_Click;
        menu.Items.Add(goToLocationItem);

        return menu;
    }

    private static T? FindAncestor<T>(DependencyObject current) where T : DependencyObject
    {
        while (current != null)
        {
            if (current is T found)
                return found;
            current = VisualTreeHelper.GetParent(current);
        }
        return null;
    }

    private ProjectTreeItem? GetContextMenuTargetItem(object sender)
    {
        // Use selected item (set by PreviewMouseRightButtonDown)
        if (ProjectTreeView.SelectedItem is ProjectTreeItem selectedItem)
            return selectedItem;
        return null;
    }

    private bool IsEntryPointFile(string filePath)
    {
        var fileName = Path.GetFileName(filePath);
        return fileName.Equals("StartViz.cs", StringComparison.OrdinalIgnoreCase);
    }

    private string GetDefaultNewFileName() => "NewFile.cs";

    private string? PromptForInput(string title, string prompt, string defaultValue)
    {
        var dialog = new Window
        {
            Title = title,
            Width = 350,
            SizeToContent = SizeToContent.Height,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Owner = this,
            ResizeMode = ResizeMode.NoResize,
            Background = (SolidColorBrush)FindResource("SecondaryBackgroundBrush")
        };

        var grid = new Grid { Margin = new Thickness(16) };
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var label = new TextBlock
        {
            Text = prompt,
            Foreground = (SolidColorBrush)FindResource("ForegroundBrush"),
            Margin = new Thickness(0, 0, 0, 8)
        };
        Grid.SetRow(label, 0);
        grid.Children.Add(label);

        var textBox = new TextBox
        {
            Text = defaultValue,
            Background = (SolidColorBrush)FindResource("BackgroundBrush"),
            Foreground = (SolidColorBrush)FindResource("ForegroundBrush"),
            BorderBrush = (SolidColorBrush)FindResource("BorderBrush"),
            Padding = new Thickness(8, 6, 8, 6),
            Margin = new Thickness(0, 0, 0, 16)
        };
        textBox.SelectAll();
        Grid.SetRow(textBox, 1);
        grid.Children.Add(textBox);

        var buttonPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right
        };
        Grid.SetRow(buttonPanel, 2);

        string? result = null;

        var okButton = new Button
        {
            Content = "OK",
            Width = 80,
            Padding = new Thickness(0, 6, 0, 6),
            Margin = new Thickness(0, 0, 8, 0),
            Background = (SolidColorBrush)FindResource("AccentBrush"),
            Foreground = Brushes.White,
            BorderThickness = new Thickness(0)
        };
        okButton.Click += (s, e) =>
        {
            result = textBox.Text.Trim();
            dialog.DialogResult = true;
        };

        var cancelButton = new Button
        {
            Content = "Cancel",
            Width = 80,
            Padding = new Thickness(0, 6, 0, 6),
            Background = (SolidColorBrush)FindResource("SecondaryBackgroundBrush"),
            Foreground = (SolidColorBrush)FindResource("ForegroundBrush"),
            BorderBrush = (SolidColorBrush)FindResource("BorderBrush")
        };
        cancelButton.Click += (s, e) => dialog.DialogResult = false;

        buttonPanel.Children.Add(okButton);
        buttonPanel.Children.Add(cancelButton);
        grid.Children.Add(buttonPanel);

        dialog.Content = grid;
        dialog.Loaded += (s, e) => textBox.Focus();

        return dialog.ShowDialog() == true ? result : null;
    }

    #endregion

    #endregion

    private ToolTip? _currentToolTip;

    private async void TextEditor_MouseHover(object sender, MouseEventArgs e)
    {
        try
        {
            var pos = CodeEditor.GetPositionFromPoint(e.GetPosition(CodeEditor));
            if (pos == null || CodeEditor.Document == null) return;

            var offset = CodeEditor.Document.GetOffset(pos.Value.Line, pos.Value.Column);

            // First check for error markers
            if (_textMarkerService != null)
            {
                var marker = _textMarkerService.GetMarkerAtOffset(offset);
                if (marker != null && marker.Message != null)
                {
                    ShowTooltip(marker.Message, isError: true);
                    e.Handled = true;
                    return;
                }
            }

            // Check if hovering over a method call - show signature
            var methodInfo = GetMethodSignatureAtOffset(offset);
            if (methodInfo != null)
            {
                ShowMethodSignatureTooltip(methodInfo.Value.typeName, methodInfo.Value.methodName, methodInfo.Value.signatures);
                e.Handled = true;
                return;
            }

            // Roslyn-based Quick Info
            // We use a fire-and-forget approach here because MouseHover is synchronous
            // and we don't want to block the UI thread.
            var code = CodeEditor.Text;
            var service = new Editor.RoslynCompletionService(_compiler.GetReferences());
            var quickInfo = await service.GetQuickInfoAsync(code, offset);

            if (quickInfo != null)
            {
                 ShowStyledTypeTooltip(quickInfo.Value.Kind, quickInfo.Value.TypeName, quickInfo.Value.Name, quickInfo.Value.Documentation);
                 e.Handled = true;
                 return;
            }

            // Fallback: No method call - try to show type information for identifier under cursor using partial reflection if Roslyn fails (or during typing)
            // Ideally Roslyn should handle everything.
            var typeInfo = GetTypeInfoAtOffset(offset);
            if (typeInfo != null)
            {
                ShowStyledTypeTooltip(typeInfo.Value.category, typeInfo.Value.typeName, typeInfo.Value.identifier);
                e.Handled = true;
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Hover error: {ex.Message}");
        }
    }

    private (string category, string typeName, string identifier)? GetTypeInfoAtOffset(int offset)
    {
        var document = CodeEditor.Document;
        if (document == null) return null;

        // Find the identifier at this position
        var wordStart = offset;
        var wordEnd = offset;

        // Expand backwards to find word start
        while (wordStart > 0)
        {
            var c = document.GetCharAt(wordStart - 1);
            if (!char.IsLetterOrDigit(c) && c != '_')
                break;
            wordStart--;
        }

        // Expand forwards to find word end
        while (wordEnd < document.TextLength)
        {
            var c = document.GetCharAt(wordEnd);
            if (!char.IsLetterOrDigit(c) && c != '_')
                break;
            wordEnd++;
        }

        if (wordStart >= wordEnd)
            return null;

        var identifier = document.GetText(wordStart, wordEnd - wordStart);
        if (string.IsNullOrEmpty(identifier))
            return null;

        var textBeforeCursor = document.GetText(0, wordStart);
        var allCode = GetAllProjectCode();

        // Check if it's a type name
        var resolvedType = Editor.TypeInspector.ResolveType(identifier);
        if (resolvedType != null)
        {
            var typeDesc = resolvedType.IsClass ? "class" : (resolvedType.IsValueType ? "struct" : "type");
            return (typeDesc, resolvedType.FullName ?? identifier, identifier);
        }

        // Check common types
        var commonType = Editor.TypeInspector.GetCommonTypes().FirstOrDefault(t => t.Name == identifier);
        if (commonType.Name != null)
        {
            return ("type", commonType.Description, identifier);
        }

        // Hover logic temporarily disabled during Roslyn refactor
        // Check if it's a method parameter
        // var parameters = Editor.CompletionProvider.FindCurrentMethodParametersPublic(textBeforeCursor);
        // var param = parameters.FirstOrDefault(p => p.Name == identifier);
        // if (param.Name != null)
        // {
        //     return ("parameter", param.Type, identifier);
        // }

        // Check if it's a local variable
        // var locals = Editor.CompletionProvider.FindLocalVariablesPublic(textBeforeCursor);
        // var local = locals.FirstOrDefault(v => v.Name == identifier);
        // if (local.Name != null)
        // {
        //     return ("local", local.Type, identifier);
        // }

        // Try to find variable type using existing infrastructure
        // var varType = Editor.CompletionProvider.FindVariableType(textBeforeCursor, identifier, allCode);
        // if (varType != null)
        // {
        //     return ("variable", varType, identifier);
        // }

        return null;
    }

    /// <summary>
    /// Gets method signature information at the given offset if the identifier is a method call.
    /// </summary>
    private (string typeName, string methodName, List<string> signatures)? GetMethodSignatureAtOffset(int offset)
    {
        return null; // Legacy logic disabled during Roslyn refactor
    }

    private List<string> GetExtensionMethodSignatures(string typeName, string methodName)
    {
        var signatures = new List<string>();

        // Check LINQ extension methods
        var linqMethods = typeof(System.Linq.Enumerable).GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)
            .Where(m => m.Name.Equals(methodName, StringComparison.OrdinalIgnoreCase) &&
                        m.IsDefined(typeof(System.Runtime.CompilerServices.ExtensionAttribute), false))
            .ToList();

        foreach (var method in linqMethods.Take(3)) // Limit to 3 overloads
        {
            var parameters = method.GetParameters().Skip(1); // Skip 'this' parameter
            var paramStr = string.Join(", ", parameters.Select(p => $"{Editor.TypeInspector.GetTypeName(p.ParameterType)} {p.Name}"));
            signatures.Add($"{Editor.TypeInspector.GetTypeName(method.ReturnType)} {method.Name}({paramStr})");
        }

        return signatures;
    }

    private void ShowMethodSignatureTooltip(string typeName, string methodName, List<string> signatures)
    {
        if (_currentToolTip != null)
        {
            _currentToolTip.IsOpen = false;
        }

        _currentToolTip = new ToolTip();
        _currentToolTip.PlacementTarget = CodeEditor;
        _currentToolTip.Background = new SolidColorBrush(Color.FromRgb(30, 30, 30));
        _currentToolTip.BorderBrush = new SolidColorBrush(Color.FromRgb(60, 60, 60));

        var mainPanel = new StackPanel();

        // Show overload count if multiple signatures
        if (signatures.Count > 1)
        {
            mainPanel.Children.Add(new TextBlock
            {
                Text = $"({signatures.Count} overloads)",
                Foreground = new SolidColorBrush(Color.FromRgb(128, 128, 128)),
                FontSize = 11,
                Margin = new Thickness(0, 0, 0, 4)
            });
        }

        // Display each signature
        foreach (var signature in signatures.Take(5)) // Limit display to 5 overloads
        {
            var sigPanel = new WrapPanel();

            // Parse signature: "returnType methodName(params)"
            var parenIndex = signature.IndexOf('(');
            if (parenIndex > 0)
            {
                var beforeParen = signature.Substring(0, parenIndex).Trim();
                var paramsAndClose = signature.Substring(parenIndex);

                // Split return type and method name
                var lastSpace = beforeParen.LastIndexOf(' ');
                if (lastSpace > 0)
                {
                    var returnType = beforeParen.Substring(0, lastSpace).Trim();
                    var mName = beforeParen.Substring(lastSpace + 1).Trim();

                    // Return type in teal
                    sigPanel.Children.Add(new TextBlock
                    {
                        Text = returnType + " ",
                        Foreground = new SolidColorBrush(Color.FromRgb(78, 201, 176)),
                        FontSize = 12
                    });

                    // Method name in yellow
                    sigPanel.Children.Add(new TextBlock
                    {
                        Text = mName,
                        Foreground = new SolidColorBrush(Color.FromRgb(220, 220, 170)),
                        FontSize = 12
                    });
                }
                else
                {
                    // No return type (constructor)
                    sigPanel.Children.Add(new TextBlock
                    {
                        Text = beforeParen,
                        Foreground = new SolidColorBrush(Color.FromRgb(78, 201, 176)),
                        FontSize = 12
                    });
                }

                // Parameters with syntax coloring
                var paramText = paramsAndClose.Trim('(', ')');
                sigPanel.Children.Add(new TextBlock
                {
                    Text = "(",
                    Foreground = Brushes.White,
                    FontSize = 12
                });

                if (!string.IsNullOrWhiteSpace(paramText))
                {
                    var paramParts = SplitParameters(paramText);
                    for (int i = 0; i < paramParts.Count; i++)
                    {
                        var param = paramParts[i].Trim();
                        var paramLastSpace = param.LastIndexOf(' ');
                        if (paramLastSpace > 0)
                        {
                            var paramType = param.Substring(0, paramLastSpace);
                            var paramName = param.Substring(paramLastSpace + 1);

                            // Parameter type in teal
                            sigPanel.Children.Add(new TextBlock
                            {
                                Text = paramType + " ",
                                Foreground = new SolidColorBrush(Color.FromRgb(78, 201, 176)),
                                FontSize = 12
                            });

                            // Parameter name in light blue
                            sigPanel.Children.Add(new TextBlock
                            {
                                Text = paramName,
                                Foreground = new SolidColorBrush(Color.FromRgb(156, 220, 254)),
                                FontSize = 12
                            });
                        }
                        else
                        {
                            sigPanel.Children.Add(new TextBlock
                            {
                                Text = param,
                                Foreground = Brushes.White,
                                FontSize = 12
                            });
                        }

                        if (i < paramParts.Count - 1)
                        {
                            sigPanel.Children.Add(new TextBlock
                            {
                                Text = ", ",
                                Foreground = Brushes.White,
                                FontSize = 12
                            });
                        }
                    }
                }

                sigPanel.Children.Add(new TextBlock
                {
                    Text = ")",
                    Foreground = Brushes.White,
                    FontSize = 12
                });
            }
            else
            {
                // Fallback: display as plain text
                sigPanel.Children.Add(new TextBlock
                {
                    Text = signature,
                    Foreground = Brushes.White,
                    FontSize = 12
                });
            }

            mainPanel.Children.Add(sigPanel);
        }

        if (signatures.Count > 5)
        {
            mainPanel.Children.Add(new TextBlock
            {
                Text = $"... and {signatures.Count - 5} more",
                Foreground = new SolidColorBrush(Color.FromRgb(128, 128, 128)),
                FontSize = 11,
                Margin = new Thickness(0, 4, 0, 0)
            });
        }

        _currentToolTip.Content = mainPanel;
        _currentToolTip.IsOpen = true;
    }

    private List<string> SplitParameters(string paramText)
    {
        var result = new List<string>();
        int depth = 0;
        int start = 0;

        for (int i = 0; i < paramText.Length; i++)
        {
            char c = paramText[i];
            if (c == '<' || c == '(' || c == '[') depth++;
            else if (c == '>' || c == ')' || c == ']') depth--;
            else if (c == ',' && depth == 0)
            {
                result.Add(paramText.Substring(start, i - start));
                start = i + 1;
            }
        }

        if (start < paramText.Length)
            result.Add(paramText.Substring(start));

        return result;
    }

    private void ShowTooltip(string message, bool isError = false)
    {
        if (_currentToolTip != null)
        {
            _currentToolTip.IsOpen = false;
        }

        _currentToolTip = new ToolTip();
        _currentToolTip.PlacementTarget = CodeEditor;
        _currentToolTip.Background = new SolidColorBrush(Color.FromRgb(30, 30, 30));
        _currentToolTip.BorderBrush = new SolidColorBrush(isError ? Color.FromRgb(200, 80, 80) : Color.FromRgb(60, 60, 60));
        _currentToolTip.Foreground = Brushes.White;

        var textBlock = new TextBlock
        {
            Text = message,
            TextWrapping = TextWrapping.Wrap,
            MaxWidth = 400
        };
        _currentToolTip.Content = textBlock;
        _currentToolTip.IsOpen = true;
    }

    private void ShowStyledTypeTooltip(string category, string typeName, string identifier, string? documentation = null)
    {
        if (_currentToolTip != null)
        {
            _currentToolTip.IsOpen = false;
        }

        _currentToolTip = new ToolTip();
        _currentToolTip.PlacementTarget = CodeEditor;
        _currentToolTip.Background = new SolidColorBrush(Color.FromRgb(30, 30, 30));
        _currentToolTip.BorderBrush = new SolidColorBrush(Color.FromRgb(60, 60, 60));

        var mainPanel = new StackPanel();

        var headerPanel = new StackPanel { Orientation = Orientation.Horizontal };

        // Category in gray: (local), (parameter), (type), etc.
        headerPanel.Children.Add(new TextBlock
        {
            Text = $"({category}) ",
            Foreground = new SolidColorBrush(Color.FromRgb(128, 128, 128)),
            FontSize = 12
        });

        // Type name in teal
        headerPanel.Children.Add(new TextBlock
        {
            Text = typeName,
            Foreground = new SolidColorBrush(Color.FromRgb(78, 201, 176)),
            FontSize = 12
        });

        // Identifier name in light blue (only if different from type)
        if (identifier != typeName && category != "type" && category != "class" && category != "struct")
        {
            headerPanel.Children.Add(new TextBlock
            {
                Text = $" {identifier}",
                Foreground = new SolidColorBrush(Color.FromRgb(156, 220, 254)),
                FontSize = 12
            });
        }

        mainPanel.Children.Add(headerPanel);

        // Try to get documentation
        // string? documentation is now passed in as argument
        
        if (documentation == null)
        {
            // First try built-in documentation
            if (category == "type" || category == "class" || category == "struct")
            {
                documentation = Editor.XmlDocumentationProvider.GetBuiltInDocumentation(identifier);
            }
            else
            {
                // Try to get documentation from the type's member
                documentation = Editor.XmlDocumentationProvider.GetBuiltInDocumentation(typeName, identifier);
            }

            // If no built-in doc, try reflection-based XML docs
            if (documentation == null)
            {
                var resolvedType = Editor.TypeInspector.ResolveType(typeName);
                if (resolvedType != null)
                {
                    if (category == "type" || category == "class" || category == "struct")
                    {
                        documentation = Editor.XmlDocumentationProvider.GetTypeSummary(resolvedType);
                    }
                }
            }
        }

        if (!string.IsNullOrEmpty(documentation))
        {
            mainPanel.Children.Add(new Separator
            {
                Margin = new Thickness(0, 4, 0, 4),
                Background = new SolidColorBrush(Color.FromRgb(60, 60, 60))
            });

            mainPanel.Children.Add(new TextBlock
            {
                Text = documentation,
                Foreground = new SolidColorBrush(Color.FromRgb(200, 200, 200)),
                FontSize = 11,
                TextWrapping = TextWrapping.Wrap,
                MaxWidth = 350
            });
        }

        _currentToolTip.Content = mainPanel;
        _currentToolTip.IsOpen = true;
    }

    private void TextEditor_MouseHoverStopped(object sender, MouseEventArgs e)
    {
        if (_currentToolTip != null)
        {
            _currentToolTip.IsOpen = false;
            _currentToolTip = null;
        }
    }

    private void Rename_Executed(object sender, ExecutedRoutedEventArgs e) => ShowQuickActionsMenu();

    /// <summary>
    /// Opens the quick-action list for the caret position (Ctrl+. and the right-click menu).
    /// </summary>
    private async void ShowQuickActionsMenu()
    {
        try
        {
            // Report missing prerequisites through the status bar rather than a modal: this now runs
            // from a menu click as well as a shortcut, and a dialog on right-click would be jarring.
            if (_currentProject == null)
            {
                SetStatus("Quick actions need an open project", isError: true);
                return;
            }
            if (_activeFile == null)
            {
                SetStatus("Quick actions need an open file", isError: true);
                return;
            }
            if (_refactoringProvider == null)
            {
                SetStatus("Refactoring provider is not initialised", isError: true);
                return;
            }

            // Sync current content
            _activeFile.Content = CodeEditor.Text;
            var currentContent = CodeEditor.Text;
            var offset = CodeEditor.CaretOffset;
            var selectionLength = CodeEditor.SelectionLength;

            // 1. Get Quick Actions from RefactoringProvider (pass current content directly)
            SetStatus("Analyzing...", false);
            List<DoodleSharp.Editor.RefactoringProvider.QuickActionItem> quickActions;
            try
            {
                quickActions = await _refactoringProvider.GetQuickActionsAsync(_currentProject, _activeFile.FilePath, currentContent, offset, selectionLength);
            }
            catch (Exception ex)
            {
                // An empty menu with no explanation is indistinguishable from "nothing applies here".
                Journal.Error("MW.QUICKACTION.FAILED", "Quick action analysis threw", ex);
                SetStatus($"Quick actions failed: {ex.Message}", isError: true);
                return;
            }
            SetStatus("Ready", false);

            var contextMenu = new ContextMenu();
            bool hasItems = false;

            // Add Refactoring Items
            foreach (var action in quickActions)
            {
                var item = new MenuItem { Header = action.Title };
            
                // Add shortcut hint if applicable
                if (action.ActionId == "Rename") item.InputGestureText = "Ctrl+R, R";
            
                item.Click += (s, args) => PerformQuickAction(action);
                contextMenu.Items.Add(item);
                hasItems = true;
            }

            // 2. Check for missing namespaces (types and extension methods)
            // Keep existing logic for now as it's robust
            var word = GetWordAtOffset(CodeEditor.Document, offset);
            if (!string.IsNullOrEmpty(word))
            {
                var currentCode = CodeEditor.Text;

                // First check for types
                var namespaces = TypeInspector.FindNamespacesForType(word);

                // Also check for extension methods (like LINQ's Select, Where, etc.)
                var extensionNamespaces = TypeInspector.FindNamespacesForExtensionMethod(word);
                foreach (var ns in extensionNamespaces)
                {
                    namespaces.Add(ns);
                }

                // Filter out namespaces that are already in the file
                var newNamespaces = namespaces.Distinct()
                    .Where(ns => !currentCode.Contains($"using {ns};"))
                    .OrderByDescending(n => n.StartsWith("DoodleSharp"))
                    .ThenBy(n => n)
                    .ToList();

                if (newNamespaces.Count > 0)
                {
                    if (hasItems) contextMenu.Items.Add(new Separator());

                    foreach (var ns in newNamespaces)
                    {
                        var item = new MenuItem { Header = $"using {ns};" };
                        item.Click += (s, args) => AddUsingStatement(ns);
                        contextMenu.Items.Add(item);
                    }
                    hasItems = true;
                }
            }

            // 4. Show menu or feedback
            if (hasItems)
            {
                // Get visual position below the caret
                var textView = CodeEditor.TextArea.TextView;
                var pos = textView.GetVisualPosition(
                    new TextViewPosition(CodeEditor.TextArea.Caret.Line, CodeEditor.TextArea.Caret.Column),
                    ICSharpCode.AvalonEdit.Rendering.VisualYPosition.LineBottom);

                // Adjust for scrolling
                pos = new System.Windows.Point(pos.X - textView.ScrollOffset.X, pos.Y - textView.ScrollOffset.Y);

                // Position relative to TextView at caret position
                contextMenu.PlacementTarget = textView;
                contextMenu.Placement = System.Windows.Controls.Primitives.PlacementMode.RelativePoint;
                contextMenu.HorizontalOffset = pos.X;
                contextMenu.VerticalOffset = pos.Y;
                contextMenu.IsOpen = true;
                SetStatus("Quick actions available", false);
            }
            else
            {
                SetStatus($"No quick actions. File: {System.IO.Path.GetFileName(_activeFile.FilePath)}, Offset: {offset}", true);
            }
        }
        catch (Exception ex)
        {
            DoodleSharp.Diagnostics.Journal.Error("MW.EDITOR.SHOWQUICKACTIONSMENU_FAIL", "ShowQuickActionsMenu threw", ex);
            SetStatus($"ShowQuickActionsMenu failed: {ex.Message}", isError: true);
        }
    }

    /// <summary>
    /// Writes a generated method stub at the location the analyser resolved.
    ///
    /// <para>
    /// The target comes from the semantic model — the owning type's real declaration site — so
    /// <c>VectorManager.DrawVector(...)</c> generates into VectorManager.cs even though StartViz.cs
    /// is the open tab. The previous implementation counted closing braces backwards from the end of
    /// the active document, which could only ever write into the file on screen, and only landed in
    /// the right class when the file held exactly one type inside one namespace.
    /// </para>
    /// </summary>
    private void GenerateMethodFromQuickAction(DoodleSharp.Editor.RefactoringProvider.QuickActionItem action)
    {
        var built = Editor.MethodStubBuilder.Build(action.Data);
        if (!built.IsValid)
        {
            SetStatus("Could not determine where to generate the method", isError: true);
            Journal.Warn("MW.QUICKACTION.NO_TARGET", "Generate method had no resolved insertion point",
                $"method={action.Data.GetValueOrDefault("MethodName")}");
            return;
        }

        var methodName = action.Data["MethodName"];
        var stub = built.Text;
        var insertOffset = built.Offset;
        var targetFile = ResolveProjectFile(built.TargetFilePath);

        // Same file as the open tab: go through the document so undo history and the caret survive.
        bool targetIsActive = targetFile == null || targetFile == _activeFile;

        if (targetIsActive)
        {
            if (insertOffset < 0 || insertOffset > CodeEditor.Document.TextLength)
            {
                SetStatus("Generate method: the file changed, please retry", isError: true);
                return;
            }

            CodeEditor.Document.Insert(insertOffset, stub);
            CodeEditor.CaretOffset = insertOffset + stub.Length;
            SetStatus($"Generated '{methodName}'", isError: false);
            Journal.Info("MW.QUICKACTION.GENERATED", "Method generated in the active file",
                $"method={methodName} offset={insertOffset}");
            return;
        }

        // Different file: patch its in-memory content, then open it so the result is visible.
        var content = targetFile!.Content;
        if (insertOffset < 0 || insertOffset > content.Length)
        {
            SetStatus($"Generate method: {targetFile.FileName} changed, please retry", isError: true);
            return;
        }

        targetFile.Content = content.Insert(insertOffset, stub);
        targetFile.HasUnsavedChanges = true;
        targetFile.IsOpen = true;

        // Keep IntelliSense in step: the new member should be completable immediately.
        _completionWorkspace?.UpdateFile(targetFile.FileName, targetFile.Content);

        RefreshFileTabs();
        SelectFile(targetFile);
        CodeEditor.CaretOffset = Math.Min(insertOffset + stub.Length, CodeEditor.Document.TextLength);
        CodeEditor.ScrollToLine(CodeEditor.Document.GetLineByOffset(CodeEditor.CaretOffset).LineNumber);

        SetStatus($"Generated '{methodName}' in {(string.IsNullOrEmpty(built.TargetType) ? targetFile.FileName : built.TargetType)}", isError: false);
        Journal.Info("MW.QUICKACTION.GENERATED_CROSSFILE", "Method generated in another file",
            $"method={methodName} file={targetFile.FileName} offset={insertOffset}");
    }

    /// <summary>
    /// Finds the project file a resolved path refers to. Falls back to a file-name match because a
    /// syntax tree's path can differ in casing or separators from the project's copy.
    /// </summary>
    private VizCodeFile? ResolveProjectFile(string? path)
    {
        if (_currentProject == null || string.IsNullOrWhiteSpace(path)) return null;

        return _currentProject.Files.FirstOrDefault(f =>
                   string.Equals(f.FilePath, path, StringComparison.OrdinalIgnoreCase))
               ?? _currentProject.Files.FirstOrDefault(f =>
                   string.Equals(f.FileName, Path.GetFileName(path), StringComparison.OrdinalIgnoreCase));
    }

    private async void PerformQuickAction(DoodleSharp.Editor.RefactoringProvider.QuickActionItem action)
    {
        try
        {
            if (action.ActionId == "Rename")
            {
                 if (action.Data.TryGetValue("Name", out var name))
                 {
                     PerformRename(name);
                 }
            }
            else if (action.ActionId == "MoveTypeToFile")
            {
                if (action.Data.TryGetValue("TypeName", out var typeName))
                {
                    MoveTypeToNewFile(typeName);
                }
            }
            else if (action.ActionId == "ExtractInterface")
            {
                 MessageBox.Show("Extract Interface: Coming soon!", "Refactoring", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            else if (action.ActionId == "ImplementInterface")
            {
                if (action.Data.TryGetValue("InterfaceName", out var interfaceName) &&
                    action.Data.TryGetValue("ClassName", out var className))
                {
                    await ImplementInterfaceAsync(className, interfaceName);
                }
            }
            else if (action.ActionId == "FixFormatting")
            {
                 try 
                 {
                     var newText = DoodleSharp.Editor.CodeFormatter.Format(CodeEditor.Text);
                     CodeEditor.Document.Replace(0, CodeEditor.Document.TextLength, newText);
                 }
                 catch (Exception ex)
                 {
                     SetStatus($"Formatting failed: {ex.Message}", true);
                 }
            }
            else if (action.ActionId == "GenerateMethod")
            {
                GenerateMethodFromQuickAction(action);
            }
            else if (action.ActionId == "GenerateType")
            {
                if (action.Data.TryGetValue("TypeName", out var typeName))
                {
                    action.Data.TryGetValue("ConstructorParams", out var ctorParams);
                    ctorParams ??= "";
                
                    // Generate class stub with constructor if parameters are available
                    var constructorCode = "";
                    if (!string.IsNullOrEmpty(ctorParams))
                    {
                        // Generate fields and constructor body from parameters
                        var paramList = ctorParams.Split(new[] { ", " }, StringSplitOptions.RemoveEmptyEntries);
                        var fields = new List<string>();
                        var assignments = new List<string>();
                    
                        foreach (var param in paramList)
                        {
                            var parts = param.Split(' ');
                            if (parts.Length >= 2)
                            {
                                var paramType = parts[0];
                                var paramName = parts[1];
                                var fieldName = "_" + paramName;
                                fields.Add($"        private {paramType} {fieldName};");
                                assignments.Add($"            {fieldName} = {paramName};");
                            }
                        }
                    
                        var fieldsStr = string.Join("\r\n", fields);
                        var assignmentsStr = string.Join("\r\n", assignments);
                        constructorCode = $@"
    {fieldsStr}

            public {typeName}({ctorParams})
            {{
    {assignmentsStr}
            }}";
                    }
                    else
                    {
                        constructorCode = $@"
            public {typeName}()
            {{
            }}";
                    }
                
                    var classStub = $@"

    public class {typeName}
    {{{constructorCode}
    }}
    ";
                
                    // Insert at end of file (before last closing brace if there's a namespace)
                    var text = CodeEditor.Text;
                    var lastBrace = text.LastIndexOf('}');
                    if (lastBrace > 0)
                    {
                        CodeEditor.Document.Insert(lastBrace, classStub);
                    }
                    else
                    {
                        CodeEditor.Document.Insert(text.Length, classStub);
                    }
                }
            }
            else if (action.ActionId == "GenerateTypeInNewFile")
            {
                if (action.Data.TryGetValue("TypeName", out var typeName) && _currentProject != null)
                {
                    action.Data.TryGetValue("ConstructorParams", out var ctorParams);
                    ctorParams ??= "";
                
                    // Get namespace from current file
                    var currentNamespace = "";
                    var nsMatch = System.Text.RegularExpressions.Regex.Match(CodeEditor.Text, @"namespace\s+([\w.]+)");
                    if (nsMatch.Success)
                    {
                        currentNamespace = nsMatch.Groups[1].Value;
                    }
                
                    // Generate class stub with constructor if parameters are available
                    var constructorCode = "";
                    if (!string.IsNullOrEmpty(ctorParams))
                    {
                        // Generate fields and constructor body from parameters
                        var paramList = ctorParams.Split(new[] { ", " }, StringSplitOptions.RemoveEmptyEntries);
                        var fields = new List<string>();
                        var assignments = new List<string>();
                    
                        foreach (var param in paramList)
                        {
                            var parts = param.Split(' ');
                            if (parts.Length >= 2)
                            {
                                var paramType = parts[0];
                                var paramName = parts[1];
                                var fieldName = "_" + paramName;
                                fields.Add($"        private {paramType} {fieldName};");
                                assignments.Add($"            {fieldName} = {paramName};");
                            }
                        }
                    
                        var fieldsStr = string.Join("\r\n", fields);
                        var assignmentsStr = string.Join("\r\n", assignments);
                        constructorCode = $@"
    {fieldsStr}

            public {typeName}({ctorParams})
            {{
    {assignmentsStr}
            }}";
                    }
                    else
                    {
                        constructorCode = $@"
            public {typeName}()
            {{
            }}";
                    }
                
                    // Build full file content
                    var fileContent = "";
                    if (!string.IsNullOrEmpty(currentNamespace))
                    {
                        fileContent = $@"namespace {currentNamespace}
    {{
        public class {typeName}
        {{{constructorCode.Replace("\r\n", "\r\n    ")}
        }}
    }}
    ";
                    }
                    else
                    {
                        fileContent = $@"public class {typeName}
    {{{constructorCode}
    }}
    ";
                    }
                
                    // Create new file in project
                    var newFileName = typeName + ".cs";
                    var projectDir = _currentProject.ProjectDirectory ?? "";
                    var newFilePath = System.IO.Path.Combine(projectDir, newFileName);
                
                    // Add file to project
                    var newFile = new VizCodeFile
                    {
                        FilePath = newFilePath,
                        Content = fileContent,
                        HasUnsavedChanges = true,
                        IsNew = true
                    };
                
                    _currentProject.Files.Add(newFile);
                    RefreshFileTabs();
                
                    // Open the new file
                    SelectFile(newFile);
                
                    SetStatus($"Created new file: {newFileName}", false);
                }
            }
            else if (action.ActionId == "GenerateConstructor")
            {
                if (action.Data.TryGetValue("TypeName", out var typeName))
                {
                    // Generate a constructor stub
                    var stub = $"\r\n\r\n        public {typeName}()\r\n        {{\r\n            // TODO: Initialize fields\r\n        }}";
                
                    // Find the class opening brace and insert after the first line inside
                    var text = CodeEditor.Text;
                    var classPattern = $"class\\s+{System.Text.RegularExpressions.Regex.Escape(typeName)}";
                    var match = System.Text.RegularExpressions.Regex.Match(text, classPattern);
                
                    if (match.Success)
                    {
                        // Find the opening brace after the class declaration
                        var bracePos = text.IndexOf('{', match.Index);
                        if (bracePos > 0)
                        {
                            // Insert after the opening brace
                            CodeEditor.Document.Insert(bracePos + 1, stub);
                        }
                    }
                }
            }
            else if (action.ActionId == "AddParameter")
            {
                if (action.Data.TryGetValue("MethodName", out var methodName))
                {
                    // Prompt for parameter details
                    var paramType = PromptForInput("Add Parameter", "Enter parameter type:", "string");
                    if (string.IsNullOrEmpty(paramType)) return;
                
                    var paramName = PromptForInput("Add Parameter", "Enter parameter name:", "value");
                    if (string.IsNullOrEmpty(paramName)) return;
                
                    var newParam = $"{paramType} {paramName}";
                
                    // Find the method DECLARATION (not call site)
                    // Method declarations have a return type before the method name
                    var text = CodeEditor.Text;
                    var escapedName = System.Text.RegularExpressions.Regex.Escape(methodName);
                    // Pattern: return_type methodName( - the return type includes modifiers
                    var methodDeclPattern = $@"(?:void|int|string|bool|double|float|object|var|\w+)\s+{escapedName}\s*\(";
                    var match = System.Text.RegularExpressions.Regex.Match(text, methodDeclPattern);
                
                    if (match.Success)
                    {
                        var openParen = match.Index + match.Length - 1;
                        var closeParen = text.IndexOf(')', openParen);
                    
                        if (closeParen > openParen)
                        {
                            var existingParams = text.Substring(openParen + 1, closeParen - openParen - 1).Trim();
                        
                            if (string.IsNullOrEmpty(existingParams))
                            {
                                // No existing params, just insert
                                CodeEditor.Document.Insert(openParen + 1, newParam);
                            }
                            else
                            {
                                // Add comma and new param
                                CodeEditor.Document.Insert(closeParen, $", {newParam}");
                            }
                        }
                    }
                }
            }
            else if (action.ActionId == "RemoveUnusedUsings")
            {
                try
                {
                    // Use Roslyn to properly detect unused usings
                    var text = CodeEditor.Text;
                
                    // Parse the code and get compilation with diagnostics
                    var tree = Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree.ParseText(text);
                    var root = tree.GetRoot();
                
                    // Get all using directives
                    var usingDirectives = root.DescendantNodes()
                        .OfType<Microsoft.CodeAnalysis.CSharp.Syntax.UsingDirectiveSyntax>()
                        .ToList();
                
                    if (usingDirectives.Count == 0)
                    {
                        SetStatus("No using statements found", false);
                        return;
                    }
                
                    // Get compilation with the current project to check for unused usings
                    var (compilation, _) = await _compiler.CreateCompilationAsync(_currentProject!);
                
                    // Replace the tree in compilation for accurate analysis
                    var oldTree = compilation.SyntaxTrees.FirstOrDefault(t => 
                        string.Equals(System.IO.Path.GetFileName(t.FilePath), 
                                      System.IO.Path.GetFileName(_activeFile!.FilePath), 
                                      StringComparison.OrdinalIgnoreCase));
                
                    var newTree = Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree.ParseText(
                        text, 
                        options: new Microsoft.CodeAnalysis.CSharp.CSharpParseOptions(Microsoft.CodeAnalysis.CSharp.LanguageVersion.Latest),
                        path: _activeFile.FilePath ?? "");
                
                    if (oldTree != null)
                    {
                        compilation = compilation.ReplaceSyntaxTree(oldTree, newTree);
                    }
                    else
                    {
                        compilation = compilation.AddSyntaxTrees(newTree);
                    }
                
                    // Get diagnostics - CS8019 is "Unnecessary using directive"
                    var diagnostics = compilation.GetDiagnostics()
                        .Where(d => d.Id == "CS8019" || d.Id == "IDE0005")
                        .ToList();
                
                    if (diagnostics.Count == 0)
                    {
                        // Fallback: Check for CS0246 "type or namespace not found" after removing each using
                        // If removing a using causes CS0246, it's needed
                        var usedUsings = new HashSet<int>();
                        var model = compilation.GetSemanticModel(newTree);
                        var newRoot = await newTree.GetRootAsync();
                        var newUsingDirectives = newRoot.DescendantNodes()
                            .OfType<Microsoft.CodeAnalysis.CSharp.Syntax.UsingDirectiveSyntax>()
                            .ToList();
                    
                        // Get all type references in the code
                        var typeRefs = newRoot.DescendantNodes()
                            .OfType<Microsoft.CodeAnalysis.CSharp.Syntax.IdentifierNameSyntax>()
                            .ToList();
                    
                        foreach (var typeRef in typeRefs)
                        {
                            var symbolInfo = model.GetSymbolInfo(typeRef);
                            if (symbolInfo.Symbol != null)
                            {
                                var containingNs = symbolInfo.Symbol.ContainingNamespace?.ToDisplayString();
                                if (containingNs != null)
                                {
                                    for (int i = 0; i < newUsingDirectives.Count; i++)
                                    {
                                        var usingNs = newUsingDirectives[i].Name?.ToString();
                                        if (usingNs != null && containingNs.StartsWith(usingNs))
                                        {
                                            usedUsings.Add(i);
                                        }
                                    }
                                }
                            }
                        }
                    
                        // Remove unused usings (those not in usedUsings set)
                        var lines = text.Split('\n').ToList();
                        var removedCount = 0;
                    
                        for (int i = newUsingDirectives.Count - 1; i >= 0; i--)
                        {
                            if (!usedUsings.Contains(i))
                            {
                                var usingLine = newUsingDirectives[i].GetLocation().GetLineSpan().StartLinePosition.Line;
                                if (usingLine < lines.Count)
                                {
                                    lines.RemoveAt(usingLine);
                                    removedCount++;
                                }
                            }
                        }
                    
                        if (removedCount > 0)
                        {
                            CodeEditor.Document.Replace(0, CodeEditor.Document.TextLength, string.Join("\n", lines));
                            SetStatus($"Removed {removedCount} unused using(s)", false);
                        }
                        else
                        {
                            SetStatus("No unused usings found", false);
                        }
                    }
                    else
                    {
                        // Use the diagnostics to find unused usings
                        var lines = text.Split('\n').ToList();
                        var linesToRemove = diagnostics
                            .Select(d => d.Location.GetLineSpan().StartLinePosition.Line)
                            .Distinct()
                            .OrderByDescending(x => x)
                            .ToList();
                    
                        foreach (var lineNum in linesToRemove)
                        {
                            if (lineNum < lines.Count)
                            {
                                lines.RemoveAt(lineNum);
                            }
                        }
                    
                        CodeEditor.Document.Replace(0, CodeEditor.Document.TextLength, string.Join("\n", lines));
                        SetStatus($"Removed {linesToRemove.Count} unused using(s)", false);
                    }
                }
                catch (Exception ex)
                {
                    SetStatus($"Failed to remove unused usings: {ex.Message}", true);
                }
            }
            else if (action.ActionId == "ChangeSignature")
            {
                if (action.Data.TryGetValue("MethodName", out var methodName))
                {
                    // Find the method declaration
                    // Method declarations have a return type before the method name
                    var text = CodeEditor.Text;
                    var escapedName = System.Text.RegularExpressions.Regex.Escape(methodName);
                
                    // Pattern: return_type methodName(parameters)
                    // Use a non-greedy match for return type: (?:...)\s+
                    var methodDeclPattern = $@"(?:void|int|string|bool|double|float|object|var|\w+)\s+{escapedName}\s*\((.*?)\)";
                    var match = System.Text.RegularExpressions.Regex.Match(text, methodDeclPattern, System.Text.RegularExpressions.RegexOptions.Singleline);
                
                    if (match.Success)
                    {
                        var currentParams = match.Groups[1].Value.Trim();
                    
                        // Prompt user for new parameters
                        var newParams = PromptForInput("Change Signature", $"Edit parameters for '{methodName}':", currentParams);
                    
                        if (newParams != null && newParams != currentParams)
                        {
                            var methodIndex = match.Index;
                            var paramStartIndex = match.Groups[1].Index;
                            var paramLength = match.Groups[1].Length;
                        
                            CodeEditor.Document.Replace(paramStartIndex, paramLength, newParams);
                            SetStatus($"Signature changed for '{methodName}'", false);
                        }
                    }
                    else
                    {
                        MessageBox.Show($"Could not find method declaration for '{methodName}'.", "Change Signature", MessageBoxButton.OK, MessageBoxImage.Warning);
                    }
                }
            }
            else
            {
                 MessageBox.Show($"Action '{action.Title}' ({action.ActionId}) initiated.\nContext: {string.Join(", ", action.Data.Keys)}", "Quick Action", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }
        catch (Exception ex)
        {
            DoodleSharp.Diagnostics.Journal.Error("MW.EDITOR.PERFORMQUICKACTION_FAIL", "PerformQuickAction threw", ex);
            SetStatus($"PerformQuickAction failed: {ex.Message}", isError: true);
        }
    }

    private void PerformRename(string originalName, int offset = -1)
    {
        // Capture offset before dialog if not provided
        if (offset < 0)
            offset = CodeEditor.CaretOffset;

        var dialog = new RenameDialog(originalName);
        dialog.Owner = this;
        if (dialog.ShowDialog() == true && dialog.NewName != originalName)
        {
             ExecuteRename(dialog.NewName, offset);
        }
    }

    private async void ExecuteRename(string newName, int offset)
    {
        try
        {
            if (_currentProject == null || _activeFile == null || _refactoringProvider == null) return;

            string currentContent = CodeEditor.Text; // Should be main thread, safe to access
            var result = await _refactoringProvider.GetRenameEditsAsync(_currentProject, _activeFile.FilePath, offset, newName, currentContent);

            if (result.Success && result.Changes != null)
            {
                ApplyRefactoring(result.Changes);
                SetStatus("Rename applied", false);
            }
            else
            {
                SetStatus(result.Error ?? "Rename failed", true);
            }
        }
        catch (Exception ex)
        {
            DoodleSharp.Diagnostics.Journal.Error("MW.EDITOR.EXECUTERENAME_FAIL", "ExecuteRename threw", ex);
            SetStatus($"ExecuteRename failed: {ex.Message}", isError: true);
        }
    }

    private void DirectRename_Executed(object sender, ExecutedRoutedEventArgs e)
    {
        if (_currentProject == null || _activeFile == null) return;

        // Get the word at the current caret position
        var offset = CodeEditor.CaretOffset;
        var document = CodeEditor.Document;
        var text = document.Text;

        if (offset < 0 || offset > text.Length) return;

        // Find word boundaries
        int start = offset;
        int end = offset;

        // Move start backward to find word start
        while (start > 0 && IsIdentifierChar(text[start - 1]))
            start--;

        // Move end forward to find word end
        while (end < text.Length && IsIdentifierChar(text[end]))
            end++;

        if (start == end)
        {
            SetStatus("Place cursor on an identifier to rename", true);
            return;
        }

        var wordToRename = text.Substring(start, end - start);
        PerformRename(wordToRename, offset);
    }

    private static bool IsIdentifierChar(char c)
    {
        return char.IsLetterOrDigit(c) || c == '_';
    }

    private void AddUsingStatement(string namespaceName)
    {
        var document = CodeEditor.Document;
        var text = document.Text;
        
        // Simple insertion logic: find the last using or insert at top
        int insertOffset = 0;
        var lines = text.Split('\n');
        int lastUsingLine = -1;
        
        for (int i = 0; i < lines.Length; i++)
        {
            var line = lines[i].Trim();
            if (line.StartsWith("using ") && line.EndsWith(";"))
            {
                lastUsingLine = i;
            }
            else if (!string.IsNullOrWhiteSpace(line) && !line.StartsWith("//") && lastUsingLine != -1)
            {
                // Found code after usings
                break;
            }
        }

        string textToInsert = $"using {namespaceName};\n";

        if (lastUsingLine >= 0)
        {
            // Insert after the last using
             var line = document.GetLineByNumber(lastUsingLine + 1); // 1-indexed
             insertOffset = line.EndOffset;
             textToInsert = Environment.NewLine + $"using {namespaceName};";
        }
        else
        {
            // Insert at top
            insertOffset = 0;
        }

        document.Insert(insertOffset, textToInsert);
        SetStatus($"Added using {namespaceName};", false);
    }

    private async void GoToDefinition_Executed(object sender, ExecutedRoutedEventArgs e)
    {
        try
        {
            if (_currentProject == null || _activeFile == null || _refactoringProvider == null) return;

            // Sync current content
            _activeFile.Content = CodeEditor.Text;
            var offset = CodeEditor.CaretOffset;

            SetStatus("Finding definition...", false);

            var result = await _refactoringProvider.GetDefinitionAsync(_currentProject, _activeFile.FilePath, offset);

            if (result.Success && result.FilePath != null)
            {
                // Navigate to definition
                NavigateToLocation(result.FilePath, result.Line, result.Column);
                SetStatus($"Definition: {result.SymbolKind} {result.SymbolName}", false);
            }
            else
            {
                SetStatus(result.Error ?? "Definition not found", true);
            }
        }
        catch (Exception ex)
        {
            DoodleSharp.Diagnostics.Journal.Error("MW.EDITOR.GOTODEFINITION_FAIL", "GoToDefinition_Executed threw", ex);
            SetStatus($"GoToDefinition failed: {ex.Message}", isError: true);
        }
    }

    private async void FindAllReferences_Executed(object sender, ExecutedRoutedEventArgs e)
    {
        try
        {
            if (_currentProject == null || _activeFile == null || _refactoringProvider == null) return;

            // Sync current content
            _activeFile.Content = CodeEditor.Text;
            var offset = CodeEditor.CaretOffset;

            SetStatus("Finding references...", false);

            var result = await _refactoringProvider.FindAllReferencesAsync(_currentProject, _activeFile.FilePath, offset);

            if (result.Success)
            {
                if (result.References.Count == 0)
                {
                    SetStatus("No references found", true);
                    return;
                }

                // If only one reference (the definition itself), just navigate to it
                if (result.References.Count == 1)
                {
                    var singleRef = result.References[0];
                    NavigateToLocation(singleRef.FilePath, singleRef.Line, singleRef.Column);
                    SetStatus($"Found 1 reference to '{result.SymbolName}'", false);
                    return;
                }

                // Show references in console panel
                ShowReferencesInConsole(result.SymbolName ?? "Symbol", result.References);
                SetStatus($"Found {result.References.Count} references to '{result.SymbolName}'", false);
            }
            else
            {
                SetStatus(result.Error ?? "Find references failed", true);
            }
        }
        catch (Exception ex)
        {
            DoodleSharp.Diagnostics.Journal.Error("MW.EDITOR.FINDALLREFERENCES_FAIL", "FindAllReferences_Executed threw", ex);
            SetStatus($"FindAllReferences failed: {ex.Message}", isError: true);
        }
    }

    private void ShowReferencesInConsole(string symbolName, List<Editor.RefactoringProvider.ReferenceLocation> references)
    {
        // Clear console and show references
        Console.ConsoleOutput.Instance.Clear();
        Console.ConsoleOutput.Instance.AddEntry($"References to '{symbolName}' ({references.Count} found):");
        Console.ConsoleOutput.Instance.AddEntry(new string('-', 50));

        foreach (var reference in references)
        {
            var prefix = reference.IsDefinition ? "[Definition] " : "";
            var message = $"{prefix}{reference.LineText}";

            Console.ConsoleOutput.Instance.AddEntry(
                message,
                reference.FilePath,
                reference.Line,
                reference.Column
            );
        }

        Console.ConsoleOutput.Instance.AddEntry(new string('-', 50));
        Console.ConsoleOutput.Instance.AddEntry("Double-click to navigate to reference");

        // Make sure the results are actually on screen.
        SetPaneVisible("ds.tool.console", true);
    }

    private void NavigateToLocation(string filePath, int line, int column)
    {
        if (_currentProject == null) return;

        // Find and open the file in the project
        var file = _currentProject.Files.FirstOrDefault(f =>
            string.Equals(f.FilePath, filePath, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(Path.GetFileName(f.FilePath), Path.GetFileName(filePath), StringComparison.OrdinalIgnoreCase));

        if (file != null)
        {
            // Ensure the file's tab is open before switching
            if (!file.IsOpen)
            {
                file.IsOpen = true;
                RefreshFileTabs();
            }

            // Switch to the file's tab
            SelectFile(file);

            // Navigate to the line and column
            try
            {
                if (line > 0 && line <= CodeEditor.Document.LineCount)
                {
                    var lineObj = CodeEditor.Document.GetLineByNumber(line);
                    var col = Math.Max(1, Math.Min(column, lineObj.Length + 1));
                    var offset = CodeEditor.Document.GetOffset(line, col);

                    CodeEditor.CaretOffset = offset;
                    CodeEditor.ScrollToLine(line);
                    CodeEditor.Focus();

                    // Select the identifier at this location
                    var wordEnd = offset;
                    while (wordEnd < CodeEditor.Document.TextLength)
                    {
                        var c = CodeEditor.Document.GetCharAt(wordEnd);
                        if (!char.IsLetterOrDigit(c) && c != '_') break;
                        wordEnd++;
                    }
                    if (wordEnd > offset)
                    {
                        CodeEditor.Select(offset, wordEnd - offset);
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"NavigateToLocation: {ex.Message}");
            }
        }
        else
        {
            SetStatus($"File not found in project: {Path.GetFileName(filePath)}", true);
        }
    }

    private async void PeekDefinition_Executed(object sender, ExecutedRoutedEventArgs e)
    {
        try
        {
            if (_currentProject == null || _activeFile == null || _refactoringProvider == null) return;

            // Close any existing peek popup
            ClosePeekPopup();

            // Sync current content
            _activeFile.Content = CodeEditor.Text;
            var offset = CodeEditor.CaretOffset;

            SetStatus("Finding definition...", false);

            var result = await _refactoringProvider.GetDefinitionAsync(_currentProject, _activeFile.FilePath, offset);

            if (result.Success && result.FilePath != null)
            {
                ShowPeekDefinition(result);
            }
            else
            {
                SetStatus(result.Error ?? "Definition not found", true);
            }
        }
        catch (Exception ex)
        {
            DoodleSharp.Diagnostics.Journal.Error("MW.EDITOR.PEEKDEFINITION_FAIL", "PeekDefinition_Executed threw", ex);
            SetStatus($"PeekDefinition failed: {ex.Message}", isError: true);
        }
    }

    private void ShowPeekDefinition(Editor.RefactoringProvider.DefinitionResult result)
    {
        if (_currentProject == null || result.FilePath == null) return;

        // Find the file content
        var file = _currentProject.Files.FirstOrDefault(f =>
            string.Equals(f.FilePath, result.FilePath, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(Path.GetFileName(f.FilePath), Path.GetFileName(result.FilePath), StringComparison.OrdinalIgnoreCase));

        if (file == null)
        {
            SetStatus($"File not found: {Path.GetFileName(result.FilePath)}", true);
            return;
        }

        // Get context around the definition (5 lines before and 15 lines after)
        var lines = file.Content.Split('\n');
        var startLine = Math.Max(0, result.Line - 6);
        var endLine = Math.Min(lines.Length, result.Line + 15);
        var contextLines = lines.Skip(startLine).Take(endLine - startLine).ToList();
        var contextText = string.Join("\n", contextLines);

        // Create peek popup
        _peekPopup = new System.Windows.Controls.Primitives.Popup
        {
            PlacementTarget = CodeEditor,
            Placement = System.Windows.Controls.Primitives.PlacementMode.Relative,
            StaysOpen = false,
            AllowsTransparency = true
        };

        // Calculate position based on caret
        var caretPos = CodeEditor.TextArea.Caret.CalculateCaretRectangle();
        var visualPos = CodeEditor.TextArea.TextView.GetVisualPosition(
            new ICSharpCode.AvalonEdit.TextViewPosition(CodeEditor.TextArea.Caret.Line, CodeEditor.TextArea.Caret.Column),
            ICSharpCode.AvalonEdit.Rendering.VisualYPosition.LineBottom);

        _peekPopup.HorizontalOffset = 50;
        _peekPopup.VerticalOffset = visualPos.Y + 5;

        // Create content
        var border = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(30, 30, 30)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(0, 122, 204)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(3),
            Width = 600,
            MaxHeight = 350
        };

        var grid = new Grid();
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        // Header
        var header = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(45, 45, 45)),
            Padding = new Thickness(10, 5, 10, 5)
        };
        var headerPanel = new StackPanel { Orientation = Orientation.Horizontal };
        headerPanel.Children.Add(new TextBlock
        {
            Text = Path.GetFileName(result.FilePath),
            Foreground = new SolidColorBrush(Color.FromRgb(78, 201, 176)),
            FontWeight = FontWeights.SemiBold
        });
        headerPanel.Children.Add(new TextBlock
        {
            Text = $" : {result.Line}",
            Foreground = new SolidColorBrush(Color.FromRgb(156, 220, 254))
        });
        headerPanel.Children.Add(new TextBlock
        {
            Text = $"  ({result.SymbolKind} {result.SymbolName})",
            Foreground = new SolidColorBrush(Color.FromRgb(128, 128, 128)),
            Margin = new Thickness(10, 0, 0, 0)
        });
        header.Child = headerPanel;
        Grid.SetRow(header, 0);
        grid.Children.Add(header);

        // Code preview with AvalonEdit
        var previewEditor = new TextEditor
        {
            Text = contextText,
            IsReadOnly = true,
            ShowLineNumbers = true,
            Background = new SolidColorBrush(Color.FromRgb(30, 30, 30)),
            Foreground = Brushes.White,
            FontFamily = CodeEditor.FontFamily,
            FontSize = CodeEditor.FontSize - 1,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Padding = new Thickness(5)
        };

        // Apply syntax highlighting
        previewEditor.SyntaxHighlighting = CodeEditor.SyntaxHighlighting;

        // Set line number starting offset
        previewEditor.TextArea.TextView.LineTransformers.Clear();

        // Scroll to show the definition line in context
        var defLineInContext = result.Line - startLine - 1;
        if (defLineInContext > 0 && defLineInContext <= previewEditor.Document.LineCount)
        {
            previewEditor.ScrollToLine(defLineInContext);
            // Highlight the definition line
            var defLineObj = previewEditor.Document.GetLineByNumber(Math.Min(defLineInContext + 1, previewEditor.Document.LineCount));
            previewEditor.Select(defLineObj.Offset, defLineObj.Length);
        }

        Grid.SetRow(previewEditor, 1);
        grid.Children.Add(previewEditor);

        // Footer with actions
        var footer = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(45, 45, 45)),
            Padding = new Thickness(10, 5, 10, 5)
        };
        var footerPanel = new StackPanel { Orientation = Orientation.Horizontal };

        var goToButton = new Button
        {
            Content = "Go to Definition (Enter)",
            Padding = new Thickness(10, 3, 10, 3),
            Margin = new Thickness(0, 0, 10, 0),
            Background = new SolidColorBrush(Color.FromRgb(60, 60, 60)),
            Foreground = Brushes.White,
            BorderBrush = new SolidColorBrush(Color.FromRgb(80, 80, 80))
        };
        goToButton.Click += (s, args) =>
        {
            ClosePeekPopup();
            NavigateToLocation(result.FilePath, result.Line, result.Column);
        };
        footerPanel.Children.Add(goToButton);

        var closeText = new TextBlock
        {
            Text = "Press Escape to close",
            Foreground = new SolidColorBrush(Color.FromRgb(128, 128, 128)),
            VerticalAlignment = VerticalAlignment.Center
        };
        footerPanel.Children.Add(closeText);

        footer.Child = footerPanel;
        Grid.SetRow(footer, 2);
        grid.Children.Add(footer);

        border.Child = grid;
        _peekPopup.Child = border;

        // Handle keyboard events
        _peekPopup.KeyDown += (s, args) =>
        {
            if (args.Key == Key.Escape)
            {
                ClosePeekPopup();
                CodeEditor.Focus();
                args.Handled = true;
            }
            else if (args.Key == Key.Enter)
            {
                ClosePeekPopup();
                NavigateToLocation(result.FilePath, result.Line, result.Column);
                args.Handled = true;
            }
        };

        _peekPopup.Closed += (s, args) => _peekPopup = null;

        _peekPopup.IsOpen = true;
        previewEditor.Focus();

        SetStatus($"Peek: {result.SymbolKind} {result.SymbolName} in {Path.GetFileName(result.FilePath)}:{result.Line}", false);
    }

    private void ClosePeekPopup()
    {
        if (_peekPopup != null)
        {
            _peekPopup.IsOpen = false;
            _peekPopup = null;
        }
    }

    // Symbol picker popup
    private System.Windows.Controls.Primitives.Popup? _symbolsPopup;

    private async void DocumentSymbols_Executed(object sender, ExecutedRoutedEventArgs e)
    {
        try
        {
            if (_currentProject == null || _activeFile == null || _refactoringProvider == null) return;

            // Sync current content
            _activeFile.Content = CodeEditor.Text;

            SetStatus("Loading document symbols...", false);

            var result = await _refactoringProvider.GetDocumentSymbolsAsync(_currentProject, _activeFile.FilePath);

            if (result.Success)
            {
                ShowSymbolPicker(result.Symbols, "Go to Symbol in Editor", false);
            }
            else
            {
                SetStatus(result.Error ?? "Failed to load symbols", true);
            }
        }
        catch (Exception ex)
        {
            DoodleSharp.Diagnostics.Journal.Error("MW.EDITOR.DOCUMENTSYMBOLS_FAIL", "DocumentSymbols_Executed threw", ex);
            SetStatus($"DocumentSymbols failed: {ex.Message}", isError: true);
        }
    }

    private async void WorkspaceSymbols_Executed(object sender, ExecutedRoutedEventArgs e)
    {
        try
        {
            if (_currentProject == null || _refactoringProvider == null) return;

            // Sync current content if we have an active file
            if (_activeFile != null)
            {
                _activeFile.Content = CodeEditor.Text;
            }

            SetStatus("Loading workspace symbols...", false);

            var result = await _refactoringProvider.GetWorkspaceSymbolsAsync(_currentProject);

            if (result.Success)
            {
                ShowSymbolPicker(result.Symbols, "Go to Symbol in Workspace", true);
            }
            else
            {
                SetStatus(result.Error ?? "Failed to load symbols", true);
            }
        }
        catch (Exception ex)
        {
            DoodleSharp.Diagnostics.Journal.Error("MW.EDITOR.WORKSPACESYMBOLS_FAIL", "WorkspaceSymbols_Executed threw", ex);
            SetStatus($"WorkspaceSymbols failed: {ex.Message}", isError: true);
        }
    }

    private void ShowSymbolPicker(List<Editor.RefactoringProvider.DocumentSymbol> symbols, string title, bool showFilePath)
    {
        // Close existing popup
        CloseSymbolsPopup();

        _symbolsPopup = new System.Windows.Controls.Primitives.Popup
        {
            PlacementTarget = CodeEditor,
            Placement = System.Windows.Controls.Primitives.PlacementMode.Center,
            StaysOpen = false,
            AllowsTransparency = true
        };

        // Create content
        var border = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(30, 30, 30)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(0, 122, 204)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(5),
            Width = 500,
            MaxHeight = 400
        };

        var grid = new Grid();
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        // Title
        var titleBlock = new TextBlock
        {
            Text = title,
            Foreground = Brushes.White,
            FontWeight = FontWeights.SemiBold,
            Padding = new Thickness(10, 8, 10, 5)
        };
        Grid.SetRow(titleBlock, 0);
        grid.Children.Add(titleBlock);

        // Search box
        var searchBox = new TextBox
        {
            Margin = new Thickness(10, 5, 10, 5),
            Padding = new Thickness(5),
            Background = new SolidColorBrush(Color.FromRgb(45, 45, 45)),
            Foreground = Brushes.White,
            BorderBrush = new SolidColorBrush(Color.FromRgb(80, 80, 80)),
            CaretBrush = Brushes.White
        };
        Grid.SetRow(searchBox, 1);
        grid.Children.Add(searchBox);

        // Symbols list
        var listBox = new ListBox
        {
            Background = new SolidColorBrush(Color.FromRgb(30, 30, 30)),
            Foreground = Brushes.White,
            BorderThickness = new Thickness(0),
            MaxHeight = 300
        };

        // Flatten symbols including children
        var flatSymbols = new List<Editor.RefactoringProvider.DocumentSymbol>();
        void AddSymbolsRecursive(List<Editor.RefactoringProvider.DocumentSymbol> list, int indent = 0)
        {
            foreach (var symbol in list)
            {
                symbol.Detail = (indent > 0 ? new string(' ', indent * 2) : "") + symbol.Detail;
                flatSymbols.Add(symbol);
                if (symbol.Children.Count > 0)
                {
                    AddSymbolsRecursive(symbol.Children, indent + 1);
                }
            }
        }
        AddSymbolsRecursive(symbols);

        void PopulateList(string filter)
        {
            listBox.Items.Clear();
            var filtered = string.IsNullOrEmpty(filter)
                ? flatSymbols
                : flatSymbols.Where(s => s.Name.Contains(filter, StringComparison.OrdinalIgnoreCase)).ToList();

            foreach (var symbol in filtered.Take(100)) // Limit to 100 items
            {
                var itemPanel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(5, 3, 5, 3) };

                // Symbol kind icon/color
                var kindBrush = symbol.Kind switch
                {
                    "Class" => new SolidColorBrush(Color.FromRgb(78, 201, 176)),
                    "Interface" => new SolidColorBrush(Color.FromRgb(184, 215, 163)),
                    "Method" => new SolidColorBrush(Color.FromRgb(220, 220, 170)),
                    "Property" => new SolidColorBrush(Color.FromRgb(156, 220, 254)),
                    "Field" => new SolidColorBrush(Color.FromRgb(86, 156, 214)),
                    "Constructor" => new SolidColorBrush(Color.FromRgb(220, 220, 170)),
                    "Enum" => new SolidColorBrush(Color.FromRgb(184, 215, 163)),
                    "Event" => new SolidColorBrush(Color.FromRgb(255, 198, 109)),
                    _ => Brushes.White
                };

                var kindIcon = symbol.Kind switch
                {
                    "Class" => "C",
                    "Interface" => "I",
                    "Method" => "M",
                    "Property" => "P",
                    "Field" => "F",
                    "Constructor" => "C",
                    "Enum" => "E",
                    "Event" => "V",
                    "Struct" => "S",
                    "Record" => "R",
                    _ => "?"
                };

                itemPanel.Children.Add(new Border
                {
                    Width = 18,
                    Height = 18,
                    Background = kindBrush,
                    CornerRadius = new CornerRadius(2),
                    Margin = new Thickness(0, 0, 8, 0),
                    Child = new TextBlock
                    {
                        Text = kindIcon,
                        Foreground = Brushes.Black,
                        FontWeight = FontWeights.Bold,
                        FontSize = 11,
                        HorizontalAlignment = HorizontalAlignment.Center,
                        VerticalAlignment = VerticalAlignment.Center
                    }
                });

                itemPanel.Children.Add(new TextBlock
                {
                    Text = symbol.Name,
                    Foreground = Brushes.White,
                    VerticalAlignment = VerticalAlignment.Center
                });

                if (!string.IsNullOrEmpty(symbol.Detail))
                {
                    itemPanel.Children.Add(new TextBlock
                    {
                        Text = "  " + symbol.Detail,
                        Foreground = new SolidColorBrush(Color.FromRgb(128, 128, 128)),
                        VerticalAlignment = VerticalAlignment.Center
                    });
                }

                if (showFilePath)
                {
                    itemPanel.Children.Add(new TextBlock
                    {
                        Text = $"  : {symbol.Line}",
                        Foreground = new SolidColorBrush(Color.FromRgb(100, 100, 100)),
                        VerticalAlignment = VerticalAlignment.Center
                    });
                }

                var item = new ListBoxItem
                {
                    Content = itemPanel,
                    Tag = symbol,
                    Background = Brushes.Transparent
                };
                item.MouseDoubleClick += (s, args) => NavigateToSymbol(symbol);
                listBox.Items.Add(item);
            }

            if (listBox.Items.Count > 0)
            {
                listBox.SelectedIndex = 0;
            }
        }

        PopulateList("");

        searchBox.TextChanged += (s, args) => PopulateList(searchBox.Text);
        searchBox.PreviewKeyDown += (s, args) =>
        {
            if (args.Key == Key.Down && listBox.Items.Count > 0)
            {
                listBox.SelectedIndex = Math.Min(listBox.SelectedIndex + 1, listBox.Items.Count - 1);
                listBox.ScrollIntoView(listBox.SelectedItem);
                args.Handled = true;
            }
            else if (args.Key == Key.Up && listBox.Items.Count > 0)
            {
                listBox.SelectedIndex = Math.Max(listBox.SelectedIndex - 1, 0);
                listBox.ScrollIntoView(listBox.SelectedItem);
                args.Handled = true;
            }
            else if (args.Key == Key.Enter && listBox.SelectedItem is ListBoxItem selectedItem && selectedItem.Tag is Editor.RefactoringProvider.DocumentSymbol symbol)
            {
                NavigateToSymbol(symbol);
                args.Handled = true;
            }
            else if (args.Key == Key.Escape)
            {
                CloseSymbolsPopup();
                CodeEditor.Focus();
                args.Handled = true;
            }
        };

        Grid.SetRow(listBox, 2);
        grid.Children.Add(listBox);

        border.Child = grid;
        _symbolsPopup.Child = border;

        _symbolsPopup.Closed += (s, args) => _symbolsPopup = null;
        _symbolsPopup.IsOpen = true;
        searchBox.Focus();

        SetStatus($"Found {flatSymbols.Count} symbols", false);
    }

    private void NavigateToSymbol(Editor.RefactoringProvider.DocumentSymbol symbol)
    {
        CloseSymbolsPopup();
        NavigateToLocation(symbol.FilePath, symbol.Line, symbol.Column);
    }

    private void CloseSymbolsPopup()
    {
        if (_symbolsPopup != null)
        {
            _symbolsPopup.IsOpen = false;
            _symbolsPopup = null;
        }
    }

    private void CallHierarchy_Executed(object sender, ExecutedRoutedEventArgs e)
    {
        if (_hierarchyProvider == null) return;

        var offset = CodeEditor.CaretOffset;
        var result = _hierarchyProvider.GetCallHierarchy(CodeEditor.Text, offset);

        if (result == null)
        {
            SetStatus("No method found at cursor position", true);
            return;
        }

        // Display results in console
        ConsoleOutput.Instance.Clear();
        ConsoleOutput.Instance.AddEntry($"=== Call Hierarchy for '{result.MethodName}' ===");

        if (result.IncomingCalls.Count > 0)
        {
            ConsoleOutput.Instance.AddEntry("");
            ConsoleOutput.Instance.AddEntry($"Incoming Calls ({result.IncomingCalls.Count}):");
            foreach (var call in result.IncomingCalls)
            {
                ConsoleOutput.Instance.AddEntry(
                    $"  {call.MethodName}() calls {result.MethodName}()",
                    _activeFile?.FilePath,
                    call.Line,
                    0,
                    false);
            }
        }
        else
        {
            ConsoleOutput.Instance.AddEntry("No incoming calls found.");
        }

        if (result.OutgoingCalls.Count > 0)
        {
            ConsoleOutput.Instance.AddEntry("");
            ConsoleOutput.Instance.AddEntry($"Outgoing Calls ({result.OutgoingCalls.Count}):");
            foreach (var call in result.OutgoingCalls)
            {
                ConsoleOutput.Instance.AddEntry(
                    $"  {result.MethodName}() calls {call.MethodName}()",
                    _activeFile?.FilePath,
                    call.Line,
                    0,
                    false);
            }
        }
        else
        {
            ConsoleOutput.Instance.AddEntry("No outgoing calls found.");
        }

        SetStatus($"Call hierarchy for {result.MethodName}: {result.IncomingCalls.Count} callers, {result.OutgoingCalls.Count} callees", false);
    }

    private void TypeHierarchy_Executed(object sender, ExecutedRoutedEventArgs e)
    {
        if (_hierarchyProvider == null) return;

        var offset = CodeEditor.CaretOffset;
        var result = _hierarchyProvider.GetTypeHierarchy(CodeEditor.Text, offset);

        if (result == null)
        {
            SetStatus("No type found at cursor position", true);
            return;
        }

        // Display results in console
        ConsoleOutput.Instance.Clear();
        ConsoleOutput.Instance.AddEntry($"=== Type Hierarchy for '{result.TypeName}' ({result.TypeKind}) ===");

        if (result.BaseTypes.Count > 0)
        {
            ConsoleOutput.Instance.AddEntry("");
            ConsoleOutput.Instance.AddEntry("Base Types:");
            foreach (var baseType in result.BaseTypes)
            {
                ConsoleOutput.Instance.AddEntry($"  : {baseType.TypeName}");
            }
        }
        else
        {
            ConsoleOutput.Instance.AddEntry("No base types (other than object).");
        }

        if (result.DerivedTypes.Count > 0)
        {
            ConsoleOutput.Instance.AddEntry("");
            ConsoleOutput.Instance.AddEntry($"Derived Types ({result.DerivedTypes.Count}):");
            foreach (var derived in result.DerivedTypes)
            {
                ConsoleOutput.Instance.AddEntry(
                    $"  {derived.TypeName} : {result.TypeName}",
                    _activeFile?.FilePath,
                    derived.Line,
                    0,
                    false);
            }
        }
        else
        {
            ConsoleOutput.Instance.AddEntry("No derived types found.");
        }

        SetStatus($"Type hierarchy for {result.TypeName}: {result.BaseTypes.Count} base, {result.DerivedTypes.Count} derived", false);
    }

    private string GetWordAtOffset(TextDocument document, int offset)
    {
        if (offset < 0 || offset >= document.TextLength) return "";

        var start = offset;
        var end = offset;

        // Scan backwards
        while (start > 0)
        {
            char c = document.GetCharAt(start - 1);
            if (!char.IsLetterOrDigit(c) && c != '_') break;
            start--;
        }

        // Scan forwards
        while (end < document.TextLength)
        {
            char c = document.GetCharAt(end);
            if (!char.IsLetterOrDigit(c) && c != '_') break;
            end++;
        }

        if (end > start)
        {
            return document.GetText(start, end - start);
        }
        
        return "";
    }

    private void CodeEditor_ContextMenuOpening(object sender, ContextMenuEventArgs e)
    {
        if (CodeEditor.ContextMenu == null) return;
        
        var moveItem = CodeEditor.ContextMenu.Items.OfType<MenuItem>().FirstOrDefault(i => i.Name == "MoveTypeMenuItem");
        if (moveItem == null) return;

        moveItem.Visibility = Visibility.Collapsed;

        if (_activeFile == null) return;

        // Determine class under cursor
        var pos = CodeEditor.CaretOffset;
        // Safety check
        if (CodeEditor.Text == null || pos > CodeEditor.Text.Length) return;

        // Ask the parser which type the caret is in, rather than regex-matching `class\s+(\w+)` and
        // brace-counting the body. The old scan matched the word "class" inside comments and string
        // literals, and its brace counting was confused by braces in those same places.
        string? className = null;
        try
        {
            var tree = Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree.ParseText(CodeEditor.Text);
            var declaration = tree.GetRoot()
                .FindToken(Math.Min(pos, Math.Max(0, CodeEditor.Text.Length - 1)))
                .Parent?
                .AncestorsAndSelf()
                .OfType<Microsoft.CodeAnalysis.CSharp.Syntax.TypeDeclarationSyntax>()
                .FirstOrDefault();

            // The entry-point class has to stay in StartViz.cs.
            if (declaration != null && declaration.Identifier.Text != "Viz")
                className = declaration.Identifier.Text;
        }
        catch (Exception ex)
        {
            Journal.Debug("MW.CONTEXTMENU.PARSE_FAIL", "Could not determine the type under the caret",
                ex.Message);
            return;
        }

        if (!string.IsNullOrEmpty(className))
        {
            var fileName = _activeFile.FileName ?? "";
            var ext = Path.GetExtension(fileName);
            if (string.IsNullOrEmpty(ext))
                ext = ".cs";

            moveItem.Header = $"Move type '{className}' to {className}{ext}";
            moveItem.Tag = className;
            moveItem.Visibility = Visibility.Visible;
        }
    }

    private void MoveTypeMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (_activeFile == null || _currentProject == null) return;

        if (sender is MenuItem item && item.Tag is string className)
        {
            MoveTypeToNewFile(className);
        }
    }

    private void MoveTypeToNewFile(string typeName)
    {
        if (_activeFile == null || _currentProject == null) return;

        var code = CodeEditor.Text;
        if (string.IsNullOrEmpty(code)) return;

        // Find type definition (class, interface, enum, struct, record)
        var match = Regex.Match(code, $@"\b(?:public\s+|private\s+|internal\s+|protected\s+)?(?:partial\s+)?(?:static\s+)?(?:abstract\s+|sealed\s+)?(?:class|interface|enum|struct|record)\s+{Regex.Escape(typeName)}\b");
        if (!match.Success) return;

        var typeStart = match.Index;
        var openBrace = code.IndexOf('{', typeStart);
        if (openBrace == -1) return;

        // Find end of type by counting braces
        int braceCount = 1;
        int endPos = -1;
        for (int i = openBrace + 1; i < code.Length; i++)
        {
            if (code[i] == '{') braceCount++;
            else if (code[i] == '}')
            {
                braceCount--;
                if (braceCount == 0)
                {
                    endPos = i + 1;
                    break;
                }
            }
        }

        if (endPos == -1) return;

        // Extract type code
        var typeCode = code.Substring(typeStart, endPos - typeStart);

        // Remove from current file (and potentially trailing newline)
        var newCode = code.Remove(typeStart, endPos - typeStart).TrimEnd();
        CodeEditor.Text = newCode;
        _activeFile.Content = newCode;

        // Save the modified original file to disk
        if (!_activeFile.IsNew)
            DoodleSharp.Project.DurableFile.WriteAllText(_activeFile.FilePath, newCode);

        // Create new file
        var fileName = _activeFile.FileName ?? "";
        var ext = Path.GetExtension(fileName);
        if (string.IsNullOrEmpty(ext))
            ext = ".cs";
        var newFileName = $"{typeName}{ext}";

        // Basic template for new file (preserving usings if possible, or just the type)
        // Ideally we copy using statements from original file
        var usings = Regex.Matches(code, @"^using\s+[\w\.]+;", RegexOptions.Multiline)
                          .Select(m => m.Value)
                          .Distinct();
        var header = string.Join(Environment.NewLine, usings);

        // Check for namespace
        var nsMatch = Regex.Match(code, @"\bnamespace\s+([\w\.]+)");
        string newFileContent;

        if (nsMatch.Success)
        {
            var ns = nsMatch.Groups[1].Value;
            newFileContent = $"{header}\n\nnamespace {ns}\n{{\n    {typeCode}\n}}";
        }
        else
        {
            newFileContent = $"{header}\n\n{typeCode}";
        }

        // Create the file in the same directory as the source file and save immediately
        var sourceDir = Path.GetDirectoryName(_activeFile.FilePath) ?? _currentProject.ProjectDirectory;
        var newFilePath = Path.Combine(sourceDir, newFileName);
        var newFile = new VizCodeFile
        {
            FilePath = newFilePath,
            Content = newFileContent,
            HasUnsavedChanges = false,
            IsNew = false
        };

        // Save to disk immediately
        DoodleSharp.Project.DurableFile.WriteAllText(newFilePath, newFileContent);

        _currentProject.Files.Add(newFile);
        RefreshFileTabs();
        LoadProjectTree();

        // Open the new file in the editor
        SelectFile(newFile);

        SetStatus($"Moved '{typeName}' to {newFileName}", false);
    }

    private async Task ImplementInterfaceAsync(string className, string interfaceName)
    {
        if (_activeFile == null || _currentProject == null || _refactoringProvider == null) return;

        try
        {
            SetStatus($"Implementing {interfaceName}...", false);

            var implementation = await _refactoringProvider.GenerateInterfaceImplementationAsync(
                _currentProject, _activeFile.FilePath, className, interfaceName);

            if (string.IsNullOrEmpty(implementation))
            {
                SetStatus("No members to implement.", false);
                return;
            }

            // Find the class and insert before its closing brace
            var code = CodeEditor.Text;
            var classPattern = $@"class\s+{Regex.Escape(className)}\b";
            var classMatch = Regex.Match(code, classPattern);

            if (!classMatch.Success)
            {
                SetStatus($"Could not find class '{className}'", true);
                return;
            }

            // Find the opening brace of the class
            var openBrace = code.IndexOf('{', classMatch.Index);
            if (openBrace == -1) return;

            // Find the closing brace by counting braces
            int braceCount = 1;
            int closeBrace = -1;
            for (int i = openBrace + 1; i < code.Length; i++)
            {
                if (code[i] == '{') braceCount++;
                else if (code[i] == '}')
                {
                    braceCount--;
                    if (braceCount == 0)
                    {
                        closeBrace = i;
                        break;
                    }
                }
            }

            if (closeBrace == -1) return;

            // Insert implementation before the closing brace
            CodeEditor.Document.Insert(closeBrace, implementation);
            _activeFile.Content = CodeEditor.Text;
            _activeFile.HasUnsavedChanges = true;

            SetStatus($"Implemented interface '{interfaceName}'", false);
        }
        catch (Exception ex)
        {
            SetStatus($"Failed to implement interface: {ex.Message}", true);
        }
    }


    // Helper class for simpler menu item creation (to avoid casting ambiguity if any)
    private class MenuItemHeader : MenuItem { }

    private void ApplyRefactoring(Dictionary<string, List<(int Offset, int Length, string NewText)>> changes)
    {
        int totalChanges = 0;
        
        foreach (var kvp in changes)
        {
            var filePath = kvp.Key;
            var fileChanges = kvp.Value;
            
            var file = _currentProject?.Files.FirstOrDefault(f => f.FilePath.Equals(filePath, StringComparison.OrdinalIgnoreCase) || Path.GetFileName(f.FilePath).Equals(Path.GetFileName(filePath), StringComparison.OrdinalIgnoreCase));
            
            if (file != null)
            {
                // Check if this is the active file
                if (_activeFile == file)
                {
                    CodeEditor.Document.BeginUpdate();
                    foreach (var change in fileChanges) // assume sorted descending
                    {
                        CodeEditor.Document.Replace(change.Offset, change.Length, change.NewText);
                    }
                    CodeEditor.Document.EndUpdate();
                }
                else
                {
                    // Apply to string content
                    // Need to apply from end to start to avoid offset shifting
                    var content = file.Content;
                    foreach (var change in fileChanges)
                    {
                        if (change.Offset + change.Length <= content.Length)
                        {
                            content = content.Remove(change.Offset, change.Length).Insert(change.Offset, change.NewText);
                        }
                    }
                    file.Content = content;
                }
                
                file.HasUnsavedChanges = true;
                totalChanges += fileChanges.Count;
            }
        }
        
        RefreshFileTabs();
        SetStatus($"Renamed {totalChanges} occurrences.", false);
    }

    #region Outliner

    private const int OutlinerMaxShapes = 1000;

    private void PopulateOutliner(IReadOnlyList<C2VGeometry.IDrawable> shapes)
    {
        var items = new System.Collections.ObjectModel.ObservableCollection<Project.OutlinerItem>();

        // Group shapes by type
        var groupedShapes = shapes
            .OfType<C2VGeometry.Shape>()
            .GroupBy(s => s.GetType().Name)
            .OrderBy(g => g.Key);

        var totalShapeCount = shapes.Count;

        foreach (var group in groupedShapes)
        {
            var groupItem = new Project.OutlinerItem(group.Key + $" ({group.Count()})");

            // Skip individual shape items if too many shapes (performance optimization)
            if (totalShapeCount <= OutlinerMaxShapes)
            {
                foreach (var shape in group.OrderBy(s => s.Id))
                {
                    var shapeName = !string.IsNullOrEmpty(shape.Name) ? shape.Name : group.Key;
                    var shapeItem = new Project.OutlinerItem(shapeName, isShape: true, id: shape.Id);
                    groupItem.Children.Add(shapeItem);
                }
            }

            items.Add(groupItem);
        }

        OutlinerTreeView.ItemsSource = items;
    }

    private void OutlinerExpandAll_Click(object sender, RoutedEventArgs e)
    {
        SetOutlinerItemsExpanded(OutlinerTreeView, true);
    }

    private void OutlinerCollapseAll_Click(object sender, RoutedEventArgs e)
    {
        SetOutlinerItemsExpanded(OutlinerTreeView, false);
    }

    private void SetOutlinerItemsExpanded(ItemsControl itemsControl, bool isExpanded)
    {
        foreach (var item in itemsControl.Items)
        {
            var container = itemsControl.ItemContainerGenerator.ContainerFromItem(item) as TreeViewItem;
            if (container != null)
            {
                container.IsExpanded = isExpanded;
                SetOutlinerItemsExpanded(container, isExpanded);
            }
        }
    }

    private void OutlinerIdLink_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (sender is System.Windows.Controls.TextBlock textBlock && textBlock.DataContext is Project.OutlinerItem item && item.IsShape)
        {
            if (RenderCanvas.ZoomToShape(item.Id))
            {
                SetStatus($"Zoomed to shape ID: {item.Id}", isError: false);
            }
            else
            {
                SetStatus($"Shape with ID {item.Id} not found", isError: true);
            }
        }
    }

    private void OutlinerItem_MouseEnter(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (sender is FrameworkElement element && element.DataContext is Project.OutlinerItem item && item.IsShape)
        {
            RenderCanvas.HighlightedShapeId = item.Id;
        }
    }

    private void OutlinerItem_MouseLeave(object sender, System.Windows.Input.MouseEventArgs e)
    {
        RenderCanvas.HighlightedShapeId = null;
    }

    #endregion

    #region Undo/Redo

    /// <summary>
    /// Whether Ctrl+Z / Ctrl+Y should drive the canvas transaction stack rather than the editor's
    /// own undo.
    ///
    /// <para>
    /// This mirrors the Delete key's gate exactly — anywhere you can delete a shape, you can undo
    /// it. The previous test (canvas focused, or moused over, or something selected) failed in the
    /// state that matters most: right after a delete, which clears the selection, so a user who had
    /// moved the pointer off the canvas had no way to undo at all. Text inputs keep their own undo,
    /// which is why the editor and the properties-panel boxes are excluded rather than the canvas
    /// being required.
    /// </para>
    /// </summary>
    private bool IsCanvasUndoContext() =>
        !CodeEditor.IsKeyboardFocusWithin && !IsTextInputFocused();

    private void PerformUndo()
    {
        if (TransactionManager.Instance.CanUndo)
        {
            var description = TransactionManager.Instance.UndoDescription;
            TransactionManager.Instance.Undo();
            SetStatus($"Undo: {description}", isError: false);
        }
        else
        {
            SetStatus("Nothing to undo", isError: false);
        }
    }

    private void PerformRedo()
    {
        if (TransactionManager.Instance.CanRedo)
        {
            var description = TransactionManager.Instance.RedoDescription;
            TransactionManager.Instance.Redo();
            SetStatus($"Redo: {description}", isError: false);
        }
        else
        {
            SetStatus("Nothing to redo", isError: false);
        }
    }

    private void UndoMenuItem_Click(object sender, RoutedEventArgs e)
    {
        PerformUndo();
    }

    private void RedoMenuItem_Click(object sender, RoutedEventArgs e)
    {
        PerformRedo();
    }

    #endregion

    #region Animation Controls

    private bool _isPaused = false;

    private void PlayPause_Click(object sender, RoutedEventArgs e)
    {
        var timeline = CanvasRenderer.Instance.ActiveTimeline;
        if (timeline == null) return;

        if (_isPaused)
        {
            // Resume from paused state
            _isPaused = false;
            timeline.IsPlaying = true;
            _animationStopwatch.Start();
            PlayPauseBtn.Content = "\u23F8"; // Pause symbol
        }
        else if (timeline.IsPlaying)
        {
            // Pause
            _isPaused = true;
            timeline.IsPlaying = false;
            _animationStopwatch.Stop();
            PlayPauseBtn.Content = "\u25B6"; // Play symbol
        }
        else
        {
            // Start playing
            timeline.IsPlaying = true;
            _animationStopwatch.Restart();
            _lastAnimationFrameTime = -1;
            PlayPauseBtn.Content = "\u23F8"; // Pause symbol
        }
    }

    private void Stop_Click(object sender, RoutedEventArgs e)
    {
        var timeline = CanvasRenderer.Instance.ActiveTimeline;
        if (timeline == null) return;

        // Stop and reset
        timeline.IsPlaying = false;
        _isPaused = false;
        _animationStopwatch.Reset();
        _lastAnimationFrameTime = -1;
        timeline.Update(0);
        ViewportHost.Refresh();

        PlayPauseBtn.Content = "\u25B6"; // Play symbol
        TimeDisplay.Text = $"0.00s / {timeline.Duration:F2}s";
    }

    private void SpeedSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        var timeline = CanvasRenderer.Instance.ActiveTimeline;
        if (timeline != null)
        {
            timeline.Speed = SpeedSlider.Value;
        }

        // Update speed display
        if (SpeedText != null)
        {
            SpeedText.Text = $"{SpeedSlider.Value:F2}x";
        }
    }

    private Timeline? _lastTimeline = null;

    private void UpdateAnimationControlsVisibility()
    {
        // Sketch mode: show the controls (Stop button) but hide the timeline panel
        // because a sketch has no finite duration.
        if (DoodleSharp.Sketching.SketchRuntime.Instance.IsRunning)
        {
            AnimationControlsPanel.Visibility = Visibility.Visible;
            SetPaneVisible("ds.tool.timeline", false);
            TimeDisplay.Text = $"frame {DoodleSharp.Sketching.SketchRuntime.Instance.Active?.FrameCount ?? 0}";
            PlayPauseBtn.Content = "⏸"; // pause symbol — clicking it triggers the existing Stop path
            return;
        }

        var timeline = CanvasRenderer.Instance.ActiveTimeline;
        if (timeline != null)
        {
            // Always show animation controls (play/pause buttons)
            AnimationControlsPanel.Visibility = Visibility.Visible;

            // Only show timeline panel if user hasn't disabled it in Window menu
            bool userWantsTimeline = ShowTimelineMenuItem.IsChecked;
            if (userWantsTimeline)
            {
                SetPaneVisible("ds.tool.timeline", true);
            }

            // Update time display
            var currentTime = timeline.CurrentTime;
            var duration = timeline.Duration;
            TimeDisplay.Text = $"{currentTime:F2}s / {duration:F2}s";

            // Update play/pause button
            if (timeline.IsPlaying && !_isPaused)
            {
                PlayPauseBtn.Content = "\u23F8"; // Pause symbol
            }
            else
            {
                PlayPauseBtn.Content = "\u25B6"; // Play symbol
            }

            // Update timeline panel if timeline changed
            if (timeline != _lastTimeline)
            {
                _lastTimeline = timeline;
                TimelinePanel.SetTimeline(timeline);
            }
            else
            {
                // Just update playhead
                TimelinePanel.UpdatePlayhead();
            }
        }
        else
        {
            AnimationControlsPanel.Visibility = Visibility.Collapsed;
            SetPaneVisible("ds.tool.timeline", false);
            _isPaused = false;

            if (_lastTimeline != null)
            {
                _lastTimeline = null;
                TimelinePanel.SetTimeline(null);
            }
        }
    }

    #endregion

    #region Find and Replace

    private void FindMenuItem_Click(object sender, RoutedEventArgs e)
    {
        ShowFindReplaceDialog(showReplace: false);
    }

    private void FindReplaceMenuItem_Click(object sender, RoutedEventArgs e)
    {
        ShowFindReplaceDialog(showReplace: true);
    }

    private void FindInFilesMenuItem_Click(object sender, RoutedEventArgs e)
    {
        ShowFindReplaceDialog(showReplace: false, projectScope: true);
    }

    private void ShowFindReplaceDialog(bool showReplace, bool projectScope = false)
    {
        if (_findReplaceDialog == null)
        {
            _findReplaceDialog = new FindReplaceDialog { Owner = this };

            _findReplaceDialog.FindNextRequested += (s, options) => PerformFindNext(options);
            _findReplaceDialog.FindAllRequested += (s, options) => PerformFindAll(options);
            _findReplaceDialog.ReplaceRequested += (s, options) => PerformReplace(options);
            _findReplaceDialog.ReplaceAllRequested += (s, options) => PerformReplaceAll(options);
        }

        _findReplaceDialog.ShowReplace = showReplace;

        // Set initial search text from selection
        if (CodeEditor.SelectionLength > 0 && CodeEditor.SelectionLength < 100)
        {
            var selectedText = CodeEditor.SelectedText;
            if (!selectedText.Contains('\n') && !selectedText.Contains('\r'))
            {
                _findReplaceDialog.SearchText = selectedText;
            }
        }

        if (projectScope)
        {
            _findReplaceDialog.SetProjectScope();
        }

        _findReplaceDialog.Show();
        _findReplaceDialog.Activate();
    }

    private void PerformFindNext(SearchOptions options)
    {
        if (_activeFile == null) return;

        var content = CodeEditor.Text;
        var startIndex = CodeEditor.CaretOffset;

        var result = _findReplaceService.FindNext(content, options, startIndex);

        if (result.HasValue)
        {
            CodeEditor.Select(result.Value.Start, result.Value.Length);
            CodeEditor.ScrollTo(CodeEditor.Document.GetLineByOffset(result.Value.Start).LineNumber, 0);
            _findReplaceDialog?.SetStatus($"Match found at offset {result.Value.Start}");
        }
        else
        {
            _findReplaceDialog?.SetStatus("No matches found");
        }
    }

    private void PerformFindAll(SearchOptions options)
    {
        var results = new List<SearchResult>();

        if (options.Scope == SearchScope.EntireProject && _currentProject != null)
        {
            // Search all files in project
            var files = new Dictionary<string, string>();
            foreach (var file in _currentProject.Files)
            {
                files[file.FilePath] = file.Content;
            }
            results = _findReplaceService.FindInProject(files, options);
        }
        else if (_activeFile != null)
        {
            // Search current file only
            results = _findReplaceService.FindAll(CodeEditor.Text, _activeFile.FilePath, options);
        }

        ShowFindResults(results, options.SearchText);
    }

    private void ShowFindResults(List<SearchResult> results, string searchTerm)
    {
        FindResultsPanel.Results = results;
        FindResultsPanel.SetSearchTerm(searchTerm);
        ShowFindResultsTab();

        if (results.Count > 0)
        {
            _findReplaceDialog?.SetStatus($"Found {results.Count} match{(results.Count == 1 ? "" : "es")}");
        }
        else
        {
            _findReplaceDialog?.SetStatus("No matches found");
        }
    }

    private void PerformReplace(SearchOptions options)
    {
        if (_activeFile == null) return;

        var content = CodeEditor.Text;
        var startIndex = CodeEditor.CaretOffset;

        var result = _findReplaceService.ReplaceNext(content, options, startIndex);

        if (result.HasValue)
        {
            CodeEditor.Document.Text = result.Value.NewContent;
            CodeEditor.Select(result.Value.MatchStart, result.Value.MatchLength);
            CodeEditor.ScrollTo(CodeEditor.Document.GetLineByOffset(result.Value.MatchStart).LineNumber, 0);
            _findReplaceDialog?.SetStatus("Replaced 1 occurrence");
        }
        else
        {
            _findReplaceDialog?.SetStatus("No matches found");
        }
    }

    private void PerformReplaceAll(SearchOptions options)
    {
        if (options.Scope == SearchScope.EntireProject && _currentProject != null)
        {
            // Replace in all files
            int totalReplacements = 0;
            int filesModified = 0;

            foreach (var file in _currentProject.Files)
            {
                var (newContent, count) = _findReplaceService.ReplaceAll(file.Content, options);
                if (count > 0)
                {
                    file.Content = newContent;
                    totalReplacements += count;
                    filesModified++;

                    // Update editor if this is the active file
                    if (file == _activeFile)
                    {
                        CodeEditor.Document.Text = newContent;
                    }
                }
            }

            _findReplaceDialog?.SetStatus($"Replaced {totalReplacements} occurrence{(totalReplacements == 1 ? "" : "s")} in {filesModified} file{(filesModified == 1 ? "" : "s")}");
        }
        else if (_activeFile != null)
        {
            // Replace in current file only
            var (newContent, count) = _findReplaceService.ReplaceAll(CodeEditor.Text, options);

            if (count > 0)
            {
                CodeEditor.Document.Text = newContent;
                _findReplaceDialog?.SetStatus($"Replaced {count} occurrence{(count == 1 ? "" : "s")}");
            }
            else
            {
                _findReplaceDialog?.SetStatus("No matches found");
            }
        }
    }

    private void NavigateToSearchResult(SearchResult result)
    {
        // Find and open the file if it's different from current
        if (_currentProject != null)
        {
            var file = _currentProject.Files.FirstOrDefault(f => f.FilePath == result.FilePath);
            if (file != null && file != _activeFile)
            {
                SelectFile(file);
            }
        }

        // Navigate to the location
        if (_activeFile != null && _activeFile.FilePath == result.FilePath)
        {
            var line = Math.Min(result.LineNumber, CodeEditor.Document.LineCount);
            var lineObj = CodeEditor.Document.GetLineByNumber(line);
            var offset = lineObj.Offset + Math.Max(0, result.Column - 1);

            CodeEditor.CaretOffset = offset;
            CodeEditor.Select(offset, result.MatchLength);
            CodeEditor.ScrollTo(line, result.Column);
            CodeEditor.Focus();
        }
    }

    #endregion
}
