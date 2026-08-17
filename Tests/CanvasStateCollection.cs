using Xunit;

namespace DoodleSharp.Tests;

// Shared xUnit collection for every test that touches process-wide static state:
// the CanvasRenderer.Instance singleton and/or the C2VGeometry.Shape.DefaultRegistry
// and Shape.AutoRegister statics. DisableParallelization serializes these tests so
// one class nulling/rebinding the registry can't race with another asserting canvas
// membership. Any new test class that mutates those statics must join this collection.
[CollectionDefinition("CanvasState", DisableParallelization = true)]
public class CanvasStateCollection { }
