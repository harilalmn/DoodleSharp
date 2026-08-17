using System;
using System.Linq;
using Microsoft.CodeAnalysis;
using Xunit;
using DoodleSharp.Execution;

namespace DoodleSharp.Tests;

/// <summary>
/// Regression guard: user code compiled by ModuleCompiler must be able to `using C2VGeometry;`
/// AND `using DoodleSharp.Console;` / `DoodleSharp.Animation;` / `DoodleSharp.Sketching;`. The geometry
/// types live in C2VGeometry.dll; Console/Animation/Sketching live in the DoodleSharp host assembly.
/// Both must be in the default reference set or those usings fail (CS0246) in the editor and at run.
/// </summary>
public class CompilerReferenceTests
{
    private static bool ReferencesAssemblyOf(Type t)
    {
        var path = t.Assembly.Location;
        return new ModuleCompiler().GetReferences()
            .OfType<PortableExecutableReference>()
            .Any(r => string.Equals(r.FilePath, path, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void DefaultReferences_IncludeC2VGeometryAssembly()
        => Assert.True(ReferencesAssemblyOf(typeof(C2VGeometry.Shape)));

    [Fact]
    public void DefaultReferences_IncludeHostAssembly_ForVizConsole()
        => Assert.True(ReferencesAssemblyOf(typeof(DoodleSharp.Console.VizConsole)));

    [Fact]
    public void DefaultReferences_IncludeHostAssembly_ForMouse()
        => Assert.True(ReferencesAssemblyOf(typeof(DoodleSharp.Animation.Mouse)));

    /// <summary>
    /// The references being present is necessary but not sufficient — this compiles a realistic script
    /// against the real reference set, so a signature the documentation promises but the compiler
    /// rejects fails here rather than in front of a user. Every member touched below appears in the
    /// README and in F1 Help.
    /// </summary>
    [Fact]
    public void UserCodeCanRegisterMouseHandlersAndReadThePayload()
    {
        const string source = """
            using System;
            using C2VGeometry;
            using DoodleSharp.Animation;
            using DoodleSharp.Console;

            namespace Probe
            {
                public class Viz
                {
                    public static void Main()
                    {
                        var cursor = new VCircle(new VXYZ(0, 0), 8) { Name = "cursor" };

                        Mouse.OnMove(e =>
                        {
                            cursor.Center = e.Position;
                            VizConsole.Log($"{e.X},{e.Y} raw={e.RawPosition} screen={e.ScreenX},{e.ScreenY}");
                            VizConsole.Log($"{e.Kind} {e.Button} scale={e.Scale}");
                        });

                        Mouse.OnDown(e =>
                        {
                            if (e.Shift || e.Ctrl || e.Alt) return;
                            if (e.LeftDown || e.RightDown || e.MiddleDown)
                                new VCircle(e.Position, 10) { FillColor = "Cyan" };

                            Shape? hit = e.Target;
                            if (hit != null) hit.Remove();
                        });

                        Mouse.OnUp(e => VizConsole.Log(e.ClickCount));
                        Mouse.OnClick(e => VizConsole.Log("click"));
                        Mouse.OnDoubleClick(e => VizConsole.Log("double"));
                        Mouse.OnDrag(e => cursor.Center = e.RawPosition);
                        Mouse.OnWheel(e => VizConsole.Log(e.WheelNotches + e.WheelDelta));
                        Mouse.OnEnter(e => VizConsole.Log("enter"));
                        Mouse.OnLeave(e => VizConsole.Log("leave"));

                        // Detaching, and the polled state usable with no handler registered.
                        Mouse.OnEnter(null);
                        VizConsole.Log($"{Mouse.X},{Mouse.Y} down={Mouse.IsDown} any={Mouse.HasHandlers}");

                        Frame.Request(t => cursor.Center = new VXYZ(Mouse.X, Mouse.Y));
                    }
                }
            }
            """;

        var tree = Microsoft.CodeAnalysis.CSharp.SyntaxFactory.ParseSyntaxTree(source);
        var compilation = Microsoft.CodeAnalysis.CSharp.CSharpCompilation.Create(
            "MouseApiProbe",
            new[] { tree },
            new ModuleCompiler().GetReferences(),
            new Microsoft.CodeAnalysis.CSharp.CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));

        var errors = compilation.GetDiagnostics()
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .Select(d => $"{d.Id} {d.GetMessage()} @ {d.Location.GetLineSpan().StartLinePosition}")
            .ToArray();

        Assert.Empty(errors);
    }
}
