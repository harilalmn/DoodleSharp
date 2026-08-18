using System;
using System.IO;
using Xunit;

namespace DoodleSharp.Tests;

/// <summary>
/// Keyboard gestures that are declared in more than one place and must not drift apart.
///
/// <para>
/// A shortcut lives in up to five files: the window's InputBindings, the menu item's
/// InputGestureText, <c>MainWindow</c>'s own PreviewKeyDown switch, the parallel switch in
/// <see cref="DoodleSharp.Editor.SharedEditorController"/> (see note 43 — the main window does not
/// use the controller, so both are live code), and the F1 Help table. Changing one and not the rest
/// produces a gesture that works but is documented wrong, or a menu that advertises a key which does
/// nothing. Source scans because these need a real window to exercise.
/// </para>
/// </summary>
public class KeyBindingTests
{
    private static string Read(params string[] parts)
        => File.ReadAllText(Path.Combine(ArrowheadConsistencyTests.RepoRoot(), Path.Combine(parts)));

    [Fact]
    public void FormatCodeAdvertisesAltShiftF()
    {
        // Alt+Shift+F is the VS Code / Visual Studio spelling. It was Ctrl+Shift+F, which this same
        // window uses for Find in Files — the two clashed and Format won, so Find in Files could not
        // be reached by its advertised key at all.
        //
        // The menu's InputGestureText is what the user reads, so it must match the live handler;
        // BothKeyHandlersRouteFormatUnderAltShift asserts the handler itself.
        Assert.Contains("InputGestureText=\"Alt+Shift+F\"", Read("MainWindow.xaml"));
    }

    [Fact]
    public void TheInertWindowInputBindingsStayGone()
    {
        // They bound to {Binding FormatCodeCommand} and four siblings; MainWindow declares no such
        // property and never assigns a DataContext, so every one resolved to null and did nothing
        // while looking authoritative. A documentation pass read that block as the source of truth
        // for the Format shortcut, and a test asserting on it passed while proving nothing. Every
        // gesture lives in MainWindow_PreviewKeyDown instead.
        var xaml = Read("MainWindow.xaml");

        Assert.DoesNotContain("<Window.InputBindings>", xaml);
        foreach (var command in new[] { "FormatCodeCommand", "SaveCommand", "OpenCommand", "NewCommand", "RunCommand" })
            Assert.DoesNotContain($"{{Binding {command}}}", xaml);
    }

    [Fact]
    public void CtrlShiftFBelongsToFindInFilesAlone()
    {
        var xaml = Read("MainWindow.xaml");

        var occurrences = xaml.Split("InputGestureText=\"Ctrl+Shift+F\"").Length - 1;
        Assert.Equal(1, occurrences);

        // ...and it is the Find in Files item that keeps it.
        var at = xaml.IndexOf("InputGestureText=\"Ctrl+Shift+F\"", StringComparison.Ordinal);
        Assert.Contains("FindInFiles", xaml[Math.Max(0, at - 300)..at]);
    }

    [Fact]
    public void BothKeyHandlersRouteFormatUnderAltShift()
    {
        // The two switches are separate live implementations (note 43). Alt makes WPF report
        // Key.System with the real key in SystemKey, so the Alt branches read SystemKey — putting
        // Key.F in a branch that reads e.Key would silently never fire.
        foreach (var (file, marker) in new[]
        {
            ("MainWindow.xaml.cs", "FormatButton_Click(sender, e);"),
            (Path.Combine("Editor", "SharedEditorController.cs"), "FormatAll();"),
        })
        {
            var code = Read(file);

            var altShift = code.IndexOf("ModifierKeys.Shift | ModifierKeys.Alt", StringComparison.Ordinal);
            Assert.True(altShift > 0, $"{file} must have an Alt+Shift branch");

            var format = code.IndexOf(marker, altShift, StringComparison.Ordinal);
            Assert.True(format > 0, $"{file} must format from the Alt+Shift branch");

            // Nothing between the branch and the call may open a different modifier branch.
            var between = code[altShift..format];
            Assert.DoesNotContain("ModifierKeys.Control | ModifierKeys.Alt", between);
        }
    }

    [Fact]
    public void HelpDocumentsTheGestureThatIsActuallyBound()
    {
        var docs = Read("Documentation", "DocGenerator.cs");

        Assert.Contains("\"Alt+Shift+F\", \"Format code\"", docs);
        Assert.DoesNotContain("\"Ctrl+Shift+F\", \"Format code\"", docs);
    }
}
