namespace DoodleSharp.Commands
{
    /// <summary>
    /// Interface for undoable commands following the Command Pattern.
    /// All operations that modify the canvas or document state should implement this interface.
    /// </summary>
    public interface ICommand
    {
        /// <summary>
        /// Human-readable description of the command for UI display.
        /// Example: "Draw Circle", "Move 3 shapes", "Delete Line"
        /// </summary>
        string Description { get; }

        /// <summary>
        /// Executes the command. Called when the command is first performed or when redoing.
        /// </summary>
        void Execute();

        /// <summary>
        /// Reverses the command's effects. Called when undoing.
        /// </summary>
        void Undo();

        /// <summary>
        /// Whether this command can be merged with a subsequent command of the same type.
        /// Used for continuous operations like dragging where many small moves should be one undo step.
        /// </summary>
        bool CanMergeWith(ICommand other);

        /// <summary>
        /// Merges another command into this one. Only called if CanMergeWith returns true.
        /// </summary>
        void MergeWith(ICommand other);

        /// <summary>
        /// Whether this command is still meaningful after user code has been re-executed.
        ///
        /// <para>
        /// Running the code clears the registry and rebuilds every shape from source, so a command
        /// that holds <c>Shape</c> references — move, resize, style — is left pointing at objects
        /// that are no longer on the canvas; undoing one would do nothing visible while claiming to
        /// have worked. Those commands are dropped by
        /// <see cref="TransactionManager.PruneAfterCodeRun"/>, which is the default.
        /// </para>
        ///
        /// <para>
        /// A command whose undo is fundamentally a <b>source edit</b> is not stale, because the
        /// source is exactly what the next run reads. <c>DeleteShapesWithCodeCommand</c> is the one
        /// that matters: the canvas delete removes the shape's declaration from the file, which
        /// starts the debounced auto-run half a second later — so the blanket clear was wiping the
        /// undo entry for the delete the user had only just performed.
        /// </para>
        /// </summary>
        bool SurvivesCodeRun => false;
    }
}
