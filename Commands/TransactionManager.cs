using System;
using System.Collections.Generic;

namespace DoodleSharp.Commands
{
    /// <summary>
    /// Manages undo/redo operations using the Command Pattern.
    /// Provides transaction support for grouping multiple commands as a single undo step.
    /// Similar to Revit API's Transaction model.
    /// </summary>
    public class TransactionManager
    {
        private static TransactionManager? _instance;
        private static readonly object _lock = new();

        private readonly Stack<ICommand> _undoStack = new();
        private readonly Stack<ICommand> _redoStack = new();
        private readonly List<ICommand> _transactionCommands = new();
        private string? _transactionName;
        private bool _isInTransaction;

        /// <summary>
        /// Maximum number of commands to keep in the undo stack.
        /// </summary>
        public int MaxUndoLevels { get; set; } = 100;

        /// <summary>
        /// Whether an undo operation is available.
        /// </summary>
        public bool CanUndo => _undoStack.Count > 0;

        /// <summary>
        /// Whether a redo operation is available.
        /// </summary>
        public bool CanRedo => _redoStack.Count > 0;

        /// <summary>
        /// Description of the next command to undo, or null if none.
        /// </summary>
        public string? UndoDescription => _undoStack.Count > 0 ? _undoStack.Peek().Description : null;

        /// <summary>
        /// Description of the next command to redo, or null if none.
        /// </summary>
        public string? RedoDescription => _redoStack.Count > 0 ? _redoStack.Peek().Description : null;

        /// <summary>
        /// Number of commands in the undo stack.
        /// </summary>
        public int UndoCount => _undoStack.Count;

        /// <summary>
        /// Number of commands in the redo stack.
        /// </summary>
        public int RedoCount => _redoStack.Count;

        /// <summary>
        /// Whether a transaction is currently active.
        /// </summary>
        public bool IsInTransaction => _isInTransaction;

        /// <summary>
        /// Event raised when the undo/redo state changes.
        /// </summary>
        public event EventHandler? StateChanged;

        /// <summary>
        /// Singleton instance of the TransactionManager.
        /// </summary>
        public static TransactionManager Instance
        {
            get
            {
                if (_instance == null)
                {
                    lock (_lock)
                    {
                        _instance ??= new TransactionManager();
                    }
                }
                return _instance;
            }
        }

        private TransactionManager() { }

        /// <summary>
        /// Executes a command and adds it to the undo stack.
        /// If a transaction is active, the command is added to the transaction instead.
        /// </summary>
        public void Execute(ICommand command)
        {
            if (command == null) throw new ArgumentNullException(nameof(command));

            command.Execute();

            if (_isInTransaction)
            {
                _transactionCommands.Add(command);
            }
            else
            {
                AddToUndoStack(command);
            }
        }

        /// <summary>
        /// Adds a command to the undo stack without executing it.
        /// Use when the command has already been executed externally.
        /// </summary>
        public void RecordCommand(ICommand command)
        {
            if (command == null) throw new ArgumentNullException(nameof(command));

            if (_isInTransaction)
            {
                _transactionCommands.Add(command);
            }
            else
            {
                AddToUndoStack(command);
            }
        }

        private void AddToUndoStack(ICommand command)
        {
            // Try to merge with the last command if possible
            if (_undoStack.Count > 0)
            {
                var lastCommand = _undoStack.Peek();
                if (lastCommand.CanMergeWith(command))
                {
                    lastCommand.MergeWith(command);
                    _redoStack.Clear();
                    OnStateChanged();
                    return;
                }
            }

            _undoStack.Push(command);
            _redoStack.Clear();

            // Enforce max undo levels
            TrimUndoStack();

            OnStateChanged();
        }

        private void TrimUndoStack()
        {
            if (_undoStack.Count > MaxUndoLevels)
            {
                // Convert to array, keep only the newest MaxUndoLevels
                var commands = _undoStack.ToArray();
                _undoStack.Clear();
                for (int i = MaxUndoLevels - 1; i >= 0; i--)
                {
                    _undoStack.Push(commands[i]);
                }
            }
        }

        /// <summary>
        /// Undoes the last command.
        /// </summary>
        /// <returns>True if a command was undone, false if the undo stack was empty.</returns>
        public bool Undo()
        {
            if (_undoStack.Count == 0) return false;

            var command = _undoStack.Pop();
            command.Undo();
            _redoStack.Push(command);

            OnStateChanged();
            return true;
        }

        /// <summary>
        /// Redoes the last undone command.
        /// </summary>
        /// <returns>True if a command was redone, false if the redo stack was empty.</returns>
        public bool Redo()
        {
            if (_redoStack.Count == 0) return false;

            var command = _redoStack.Pop();
            command.Execute();
            _undoStack.Push(command);

            OnStateChanged();
            return true;
        }

        /// <summary>
        /// Begins a transaction. All commands executed until CommitTransaction() are grouped
        /// as a single undo step.
        /// </summary>
        /// <param name="name">Description for the transaction (shown in undo menu).</param>
        public void BeginTransaction(string name)
        {
            if (_isInTransaction)
            {
                throw new InvalidOperationException("A transaction is already in progress. Commit or rollback first.");
            }

            _isInTransaction = true;
            _transactionName = name;
            _transactionCommands.Clear();
        }

        /// <summary>
        /// Commits the current transaction, grouping all executed commands as one undo step.
        /// </summary>
        public void CommitTransaction()
        {
            if (!_isInTransaction)
            {
                throw new InvalidOperationException("No transaction is in progress.");
            }

            _isInTransaction = false;

            if (_transactionCommands.Count > 0)
            {
                if (_transactionCommands.Count == 1)
                {
                    // Single command - add directly
                    AddToUndoStack(_transactionCommands[0]);
                }
                else
                {
                    // Multiple commands - wrap in composite
                    var composite = new CompositeCommand(_transactionName ?? "Transaction", _transactionCommands.ToArray());
                    AddToUndoStack(composite);
                }
            }

            _transactionCommands.Clear();
            _transactionName = null;
        }

        /// <summary>
        /// Rolls back the current transaction, undoing all commands executed within it.
        /// </summary>
        public void RollbackTransaction()
        {
            if (!_isInTransaction)
            {
                throw new InvalidOperationException("No transaction is in progress.");
            }

            _isInTransaction = false;

            // Undo commands in reverse order
            for (int i = _transactionCommands.Count - 1; i >= 0; i--)
            {
                _transactionCommands[i].Undo();
            }

            _transactionCommands.Clear();
            _transactionName = null;

            OnStateChanged();
        }

        /// <summary>
        /// Clears both undo and redo stacks.
        /// Call when loading a new document or clearing the canvas.
        /// </summary>
        public void Clear()
        {
            _undoStack.Clear();
            _redoStack.Clear();
            _transactionCommands.Clear();
            _isInTransaction = false;
            _transactionName = null;

            OnStateChanged();
        }

        /// <summary>
        /// Drops the commands that a code run has invalidated, keeping those whose undo is a source
        /// edit (<see cref="ICommand.SurvivesCodeRun"/>). Call this after user code has been
        /// executed, in place of <see cref="Clear"/>.
        ///
        /// <para>
        /// Every run site used to call <c>Clear()</c> outright, on the reasoning that all shapes are
        /// regenerated from code so any command holding a <c>Shape</c> reference is stale. True for
        /// move/resize/style — but the canvas delete <b>edits the source</b>, and that edit starts
        /// the debounced auto-run (on by default, 500 ms). So the run triggered by the delete
        /// cleared the undo entry the delete had just pushed, and Ctrl+Z half a second later
        /// reported "Nothing to undo". Pressing Run manually after a delete lost it the same way.
        /// </para>
        ///
        /// <para>
        /// Order is preserved in both stacks: the survivors are pushed back oldest-first.
        /// </para>
        /// </summary>
        public void PruneAfterCodeRun()
        {
            var undoRemoved = PruneStack(_undoStack);
            var redoRemoved = PruneStack(_redoStack);

            if (undoRemoved || redoRemoved)
                OnStateChanged();
        }

        /// <summary>Rebuilds a stack from its survivors, oldest-first. True if anything was dropped.</summary>
        private static bool PruneStack(Stack<ICommand> stack)
        {
            if (stack.Count == 0) return false;

            // Stack.ToArray() is newest-first; reverse it so the survivors go back in original order.
            var commands = stack.ToArray();
            var survivors = new List<ICommand>(commands.Length);
            for (int i = commands.Length - 1; i >= 0; i--)
            {
                if (commands[i].SurvivesCodeRun)
                    survivors.Add(commands[i]);
            }

            if (survivors.Count == commands.Length) return false;

            stack.Clear();
            foreach (var command in survivors)
                stack.Push(command);

            return true;
        }

        /// <summary>
        /// Gets descriptions of all commands in the undo stack (most recent first).
        /// </summary>
        public IEnumerable<string> GetUndoHistory()
        {
            foreach (var command in _undoStack)
            {
                yield return command.Description;
            }
        }

        /// <summary>
        /// Gets descriptions of all commands in the redo stack (most recent first).
        /// </summary>
        public IEnumerable<string> GetRedoHistory()
        {
            foreach (var command in _redoStack)
            {
                yield return command.Description;
            }
        }

        protected virtual void OnStateChanged()
        {
            StateChanged?.Invoke(this, EventArgs.Empty);
        }
    }
}
