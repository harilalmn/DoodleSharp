using System.Linq;
using DoodleSharp.Commands;

namespace DoodleSharp.Tests;

/// <summary>
/// The undo entry for a canvas delete has to survive the run that the delete itself triggers.
///
/// <para>
/// Every run site cleared the undo stack outright, on the reasoning that all shapes are regenerated
/// from code so any command holding a <c>Shape</c> reference is stale. That is true of move, resize
/// and style — but the canvas delete <b>edits the source</b>, and a run cleared the undo entry the
/// delete had just pushed, so Ctrl+Z afterwards reported "Nothing to undo". The delete removed the
/// code correctly the whole time; only undo was lost.
/// </para>
///
/// <para>
/// The original trigger was the debounced auto-run started by the delete's own source edit; that
/// feature is gone and code now runs on F5 / Run only. The mechanism stays because two triggers
/// remain: pressing F5 straight after a delete, and the Global Parameters paths, which re-run the
/// program in response to a value change without anyone pressing Run.
/// </para>
/// </summary>
public class UndoSurvivesRunTests
{
    /// <summary>A command that holds no shape references, like the canvas delete.</summary>
    private sealed class CodeBackedCommand : ICommand
    {
        public CodeBackedCommand(string description) => Description = description;

        public string Description { get; }
        public int Executes { get; private set; }
        public int Undos { get; private set; }

        public void Execute() => Executes++;
        public void Undo() => Undos++;
        public bool CanMergeWith(ICommand other) => false;
        public void MergeWith(ICommand other) { }
        public bool SurvivesCodeRun => true;
    }

    /// <summary>A command whose undo needs the live shape objects, like a move.</summary>
    private sealed class ShapeBackedCommand : ICommand
    {
        public ShapeBackedCommand(string description) => Description = description;

        public string Description { get; }

        public void Execute() { }
        public void Undo() { }
        public bool CanMergeWith(ICommand other) => false;
        public void MergeWith(ICommand other) { }
    }

    /// <summary>
    /// The manager is a singleton shared with the rest of the suite, so every test starts from a
    /// known state rather than assuming one.
    /// </summary>
    private static TransactionManager FreshManager()
    {
        var manager = TransactionManager.Instance;
        manager.Clear();
        return manager;
    }

    [Fact]
    public void DeleteRemainsUndoableAfterTheRunItTriggers()
    {
        var manager = FreshManager();
        var delete = new CodeBackedCommand("Delete VCircle");

        manager.Execute(delete);
        manager.PruneAfterCodeRun();

        Assert.True(manager.CanUndo);
        Assert.Equal("Delete VCircle", manager.UndoDescription);

        Assert.True(manager.Undo());
        Assert.Equal(1, delete.Undos);
    }

    [Fact]
    public void ShapeBackedCommandsAreStillDroppedByARun()
    {
        var manager = FreshManager();
        manager.Execute(new ShapeBackedCommand("Move 3 shapes"));

        manager.PruneAfterCodeRun();

        Assert.False(manager.CanUndo);
    }

    [Fact]
    public void SurvivorsKeepTheirOrder()
    {
        var manager = FreshManager();
        manager.Execute(new CodeBackedCommand("first"));
        manager.Execute(new ShapeBackedCommand("stale"));
        manager.Execute(new CodeBackedCommand("second"));

        manager.PruneAfterCodeRun();

        Assert.Equal(new[] { "second", "first" }, manager.GetUndoHistory().ToArray());
    }

    [Fact]
    public void RedoStackIsPrunedOnTheSameRule()
    {
        var manager = FreshManager();
        manager.Execute(new CodeBackedCommand("delete"));
        manager.Execute(new ShapeBackedCommand("move"));
        manager.Undo();   // move -> redo stack
        manager.Undo();   // delete -> redo stack

        manager.PruneAfterCodeRun();

        Assert.Equal(new[] { "delete" }, manager.GetRedoHistory().ToArray());
        Assert.True(manager.Redo());
    }

    [Fact]
    public void CompositeSurvivesOnlyWhenEveryPartDoes()
    {
        var allCodeBacked = new CompositeCommand("both",
            new CodeBackedCommand("a"), new CodeBackedCommand("b"));
        var mixed = new CompositeCommand("mixed",
            new CodeBackedCommand("a"), new ShapeBackedCommand("b"));

        Assert.True(allCodeBacked.SurvivesCodeRun);
        Assert.False(mixed.SurvivesCodeRun);
        Assert.False(new CompositeCommand("empty").SurvivesCodeRun);
    }

    [Fact]
    public void TheRealDeleteCommandDeclaresItSurvives()
    {
        // The stubs above prove the manager's rule; this proves the one command that needs the rule
        // still opts in. It cannot be constructed here — it takes a RenderCanvas, which needs an STA
        // UI thread — so the declaration is checked directly. Losing this line is exactly the silent
        // regression that would bring the bug back.
        var declared = typeof(DeleteShapesWithCodeCommand)
            .GetProperty(nameof(ICommand.SurvivesCodeRun));

        Assert.NotNull(declared);
        Assert.True((bool)declared!.GetValue(
            System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(
                typeof(DeleteShapesWithCodeCommand)))!);
    }

    [Fact]
    public void CommandsDefaultToNotSurviving()
    {
        // The default is the safe one: a new command that forgets to think about this is dropped
        // by a run rather than left pointing at objects that are no longer on the canvas. It is a
        // default interface member, so it is reached through ICommand — which is how the manager
        // reaches it too.
        ICommand command = new ShapeBackedCommand("anything");
        Assert.False(command.SurvivesCodeRun);
    }
}
