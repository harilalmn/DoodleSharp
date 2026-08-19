using System;
using System.Collections.Generic;
using System.Linq;
using DoodleSharp.Canvas;
using C2VGeometry;

namespace DoodleSharp.Commands
{
    /// <summary>
    /// Command for adding a shape to the canvas.
    /// </summary>
    public class AddShapeCommand : ICommand
    {
        private readonly Shape _shape;
        // The host, not one canvas. A command uses only Refresh / AddShape / RemoveShape, which the
        // host names identically and routes to the cell that actually displays the shape. Capturing
        // a single canvas would capture whichever cell happened to be hovered when the command was
        // built, so undoing a delete made in one cell could re-add the shape to another — the
        // registry-versus-display desync these commands exist to keep closed.
        private readonly ViewportHost _canvas;

        public string Description { get; }

        public AddShapeCommand(Shape shape, ViewportHost canvas, string? description = null)
        {
            _shape = shape ?? throw new ArgumentNullException(nameof(shape));
            _canvas = canvas ?? throw new ArgumentNullException(nameof(canvas));
            Description = description ?? $"Add {shape.GetType().Name}";
        }

        public void Execute()
        {
            _shape.Draw();
            _canvas.Refresh();
        }

        public void Undo()
        {
            // Remove shape from renderer
            var shapes = CanvasRenderer.Instance.GetShapes().ToList();
            if (shapes.Contains(_shape))
            {
                // We need to clear and re-add all shapes except this one
                // This is inefficient but works with current architecture
                CanvasRenderer.Instance.Clear();
                foreach (var s in shapes)
                {
                    if (s != _shape && s is Shape shape)
                    {
                        shape.IsPlaced = false;
                        shape.Draw();
                    }
                }
            }
            _shape.IsPlaced = false;
            _canvas.Refresh();
        }

        public bool CanMergeWith(ICommand other) => false;
        public void MergeWith(ICommand other) { }
    }

    /// <summary>
    /// Command for deleting shapes from the canvas.
    /// </summary>
    public class DeleteShapesCommand : ICommand
    {
        private readonly List<Shape> _shapes;
        private readonly ViewportHost _canvas;

        public string Description { get; }

        public DeleteShapesCommand(IEnumerable<Shape> shapes, ViewportHost canvas)
        {
            _shapes = shapes.ToList();
            _canvas = canvas ?? throw new ArgumentNullException(nameof(canvas));

            if (_shapes.Count == 1)
                Description = $"Delete {_shapes[0].GetType().Name}";
            else
                Description = $"Delete {_shapes.Count} shapes";
        }

        public DeleteShapesCommand(Shape shape, ViewportHost canvas)
            : this(new[] { shape }, canvas)
        {
        }

        public void Execute()
        {
            // Remove shapes from renderer
            var allShapes = CanvasRenderer.Instance.GetShapes().ToList();
            CanvasRenderer.Instance.Clear();

            foreach (var s in allShapes)
            {
                if (!_shapes.Contains(s) && s is Shape shape)
                {
                    shape.IsPlaced = false;
                    shape.Draw();
                }
            }

            foreach (var shape in _shapes)
            {
                shape.IsPlaced = false;
            }

            _canvas.Refresh();
        }

        public void Undo()
        {
            // Re-add the deleted shapes
            foreach (var shape in _shapes)
            {
                shape.IsPlaced = false;
                shape.Draw();
            }
            _canvas.Refresh();
        }

        public bool CanMergeWith(ICommand other) => false;
        public void MergeWith(ICommand other) { }
    }

    /// <summary>
    /// Deletes shapes from the canvas <b>and</b> removes their declarations from the source.
    ///
    /// <para>
    /// Canvas delete is the one operation that edits the user's code, so undoing it has to put the
    /// code back as well as the shape — restoring only the shape would leave a drawing that the
    /// next run silently undoes. The source text is captured verbatim before and after, rather than
    /// re-derived, because the edit is a text removal and re-running the matcher backwards is not a
    /// thing that can be made reliable.
    /// </para>
    ///
    /// <para>
    /// A shape lives in two collections: <c>CanvasRenderer</c> (the registry, which is what
    /// <c>RayCaster</c> and the run-time machinery see) and <c>RenderCanvas</c> (what is displayed).
    /// Both are updated in both directions — the original delete only touched the display list, so
    /// the shape lingered in the registry.
    /// </para>
    /// </summary>
    public class DeleteShapesWithCodeCommand : ICommand
    {
        /// <summary>One file's text before and after the deletion.</summary>
        public sealed class CodeEdit
        {
            public CodeEdit(object file, string before, string after)
            {
                File = file;
                Before = before;
                After = after;
            }

            /// <summary>The <c>VizCodeFile</c>, passed back to the apply callback untouched.</summary>
            public object File { get; }
            public string Before { get; }
            public string After { get; }
        }

        private readonly List<Shape> _shapes;
        private readonly ViewportHost _canvas;
        private readonly List<CodeEdit> _edits;
        private readonly Action<object, string> _applyContent;

        public string Description { get; }

        /// <param name="applyContent">
        /// Writes a file's content back into the host: updates the model, the open editor if it is
        /// the active file, and the completion workspace. Supplied by the host so this class stays
        /// free of WPF and of the project model's internals.
        /// </param>
        public DeleteShapesWithCodeCommand(
            IEnumerable<Shape> shapes,
            ViewportHost canvas,
            IEnumerable<CodeEdit> edits,
            Action<object, string> applyContent)
        {
            _shapes = shapes.ToList();
            _canvas = canvas ?? throw new ArgumentNullException(nameof(canvas));
            _edits = edits.ToList();
            _applyContent = applyContent ?? throw new ArgumentNullException(nameof(applyContent));

            Description = _shapes.Count == 1
                ? $"Delete {_shapes[0].GetType().Name}"
                : $"Delete {_shapes.Count} shapes";
        }

        public void Execute()
        {
            foreach (var shape in _shapes)
            {
                CanvasRenderer.Instance.RemoveShape(shape);
                _canvas.RemoveShape(shape);
            }

            foreach (var edit in _edits)
                _applyContent(edit.File, edit.After);
        }

        public void Undo()
        {
            foreach (var shape in _shapes)
            {
                // AddShape early-returns when IsPlaced is still set; RemoveShape cleared it.
                CanvasRenderer.Instance.AddShape(shape);
                _canvas.AddShape(shape);
            }

            foreach (var edit in _edits)
                _applyContent(edit.File, edit.Before);
        }

        public bool CanMergeWith(ICommand other) => false;
        public void MergeWith(ICommand other) { }

        /// <summary>
        /// This one outlives a run. Undoing it restores the source text, which is what the next run
        /// reads, so the entry stays useful even though the <c>Shape</c> objects it holds have been
        /// replaced. The shapes are re-added anyway: the run cannot have recreated them, since their
        /// declarations were the thing removed.
        ///
        /// <para>
        /// Known limitation: <b>redo</b> after an intervening run removes objects that are no longer
        /// on the canvas, so the shape stays visible until the code edit is picked up — immediately
        /// with auto-update on, at the next run without it. Undo, the direction that matters here,
        /// is correct either way.
        /// </para>
        /// </summary>
        public bool SurvivesCodeRun => true;
    }

    /// <summary>
    /// Command for moving shapes by a displacement vector.
    /// Supports merging for continuous drag operations.
    /// </summary>
    public class MoveShapesCommand : ICommand
    {
        private readonly List<Shape> _shapes;
        private readonly ViewportHost _canvas;
        private VXYZ _totalDisplacement;
        private readonly DateTime _createdAt;

        public string Description { get; private set; } = string.Empty;

        /// <summary>
        /// Time window for merging consecutive move commands (milliseconds).
        /// </summary>
        public static int MergeWindowMs { get; set; } = 500;

        public MoveShapesCommand(IEnumerable<Shape> shapes, VXYZ displacement, ViewportHost canvas)
        {
            _shapes = shapes.ToList();
            _totalDisplacement = displacement;
            _canvas = canvas ?? throw new ArgumentNullException(nameof(canvas));
            _createdAt = DateTime.UtcNow;

            UpdateDescription();
        }

        public MoveShapesCommand(Shape shape, VXYZ displacement, ViewportHost canvas)
            : this(new[] { shape }, displacement, canvas)
        {
        }

        private void UpdateDescription()
        {
            if (_shapes.Count == 1)
                Description = $"Move {_shapes[0].GetType().Name}";
            else
                Description = $"Move {_shapes.Count} shapes";
        }

        public void Execute()
        {
            foreach (var shape in _shapes)
            {
                shape.Move(_totalDisplacement);
            }
            _canvas.Refresh();
        }

        public void Undo()
        {
            // Move by negative displacement
            var reverseDisplacement = new VXYZ(-_totalDisplacement.X, -_totalDisplacement.Y, -_totalDisplacement.Z);
            foreach (var shape in _shapes)
            {
                shape.Move(reverseDisplacement);
            }
            _canvas.Refresh();
        }

        public bool CanMergeWith(ICommand other)
        {
            if (other is not MoveShapesCommand moveCmd)
                return false;

            // Can merge if same shapes and within time window
            if (moveCmd._shapes.Count != _shapes.Count)
                return false;

            if (!_shapes.All(s => moveCmd._shapes.Contains(s)))
                return false;

            var timeDiff = (DateTime.UtcNow - _createdAt).TotalMilliseconds;
            return timeDiff < MergeWindowMs;
        }

        public void MergeWith(ICommand other)
        {
            if (other is MoveShapesCommand moveCmd)
            {
                // Add the new displacement to our total
                // Note: We don't re-execute because the move was already applied
                _totalDisplacement = new VXYZ(
                    _totalDisplacement.X + moveCmd._totalDisplacement.X,
                    _totalDisplacement.Y + moveCmd._totalDisplacement.Y,
                    _totalDisplacement.Z + moveCmd._totalDisplacement.Z
                );
            }
        }
    }

    /// <summary>
    /// Command for modifying a shape's property.
    /// </summary>
    public class ModifyPropertyCommand<T> : ICommand
    {
        private readonly Shape _shape;
        private readonly string _propertyName;
        private readonly T _oldValue;
        private readonly T _newValue;
        private readonly Action<T> _setter;
        private readonly ViewportHost _canvas;

        public string Description { get; }

        public ModifyPropertyCommand(
            Shape shape,
            string propertyName,
            T oldValue,
            T newValue,
            Action<T> setter,
            ViewportHost canvas)
        {
            _shape = shape ?? throw new ArgumentNullException(nameof(shape));
            _propertyName = propertyName;
            _oldValue = oldValue;
            _newValue = newValue;
            _setter = setter ?? throw new ArgumentNullException(nameof(setter));
            _canvas = canvas ?? throw new ArgumentNullException(nameof(canvas));
            Description = $"Modify {shape.GetType().Name}.{propertyName}";
        }

        public void Execute()
        {
            _setter(_newValue);
            _canvas.Refresh();
        }

        public void Undo()
        {
            _setter(_oldValue);
            _canvas.Refresh();
        }

        public bool CanMergeWith(ICommand other) => false;
        public void MergeWith(ICommand other) { }
    }

    /// <summary>
    /// Command for rotating shapes around a pivot point.
    /// </summary>
    public class RotateShapesCommand : ICommand
    {
        private readonly List<Shape> _shapes;
        private readonly VXYZ _pivot;
        private readonly double _angleDegrees;
        private readonly ViewportHost _canvas;

        public string Description { get; }

        public RotateShapesCommand(IEnumerable<Shape> shapes, VXYZ pivot, double angleDegrees, ViewportHost canvas)
        {
            _shapes = shapes.ToList();
            _pivot = pivot;
            _angleDegrees = angleDegrees;
            _canvas = canvas ?? throw new ArgumentNullException(nameof(canvas));

            if (_shapes.Count == 1)
                Description = $"Rotate {_shapes[0].GetType().Name}";
            else
                Description = $"Rotate {_shapes.Count} shapes";
        }

        public void Execute()
        {
            foreach (var shape in _shapes)
            {
                shape.Rotate(_pivot, _angleDegrees);
            }
            _canvas.Refresh();
        }

        public void Undo()
        {
            foreach (var shape in _shapes)
            {
                shape.Rotate(_pivot, -_angleDegrees);
            }
            _canvas.Refresh();
        }

        public bool CanMergeWith(ICommand other) => false;
        public void MergeWith(ICommand other) { }
    }

    /// <summary>
    /// Command for scaling shapes from a center point.
    /// </summary>
    public class ScaleShapesCommand : ICommand
    {
        private readonly List<Shape> _shapes;
        private readonly VXYZ _center;
        private readonly double _factor;
        private readonly ViewportHost _canvas;

        public string Description { get; }

        public ScaleShapesCommand(IEnumerable<Shape> shapes, VXYZ center, double factor, ViewportHost canvas)
        {
            _shapes = shapes.ToList();
            _center = center;
            _factor = factor;
            _canvas = canvas ?? throw new ArgumentNullException(nameof(canvas));

            if (_shapes.Count == 1)
                Description = $"Scale {_shapes[0].GetType().Name}";
            else
                Description = $"Scale {_shapes.Count} shapes";
        }

        public void Execute()
        {
            foreach (var shape in _shapes)
            {
                shape.Scale(_center, _factor);
            }
            _canvas.Refresh();
        }

        public void Undo()
        {
            // Scale by inverse factor
            foreach (var shape in _shapes)
            {
                shape.Scale(_center, 1.0 / _factor);
            }
            _canvas.Refresh();
        }

        public bool CanMergeWith(ICommand other) => false;
        public void MergeWith(ICommand other) { }
    }
}
