using System.IO;
using System.Reflection;
using System.Runtime.Loader;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using DoodleSharp.Canvas;
using DoodleSharp.Console;
using DoodleSharp.Diagnostics;
using DoodleSharp.Project;

namespace DoodleSharp.Execution;

public class CompilationResult
{
    public bool Success { get; set; }
    public string? Error { get; set; }
    public IEnumerable<Diagnostic>? Diagnostics { get; set; }
}

public class ModuleCompiler
{
    private static readonly List<MetadataReference> DefaultReferences;

    static ModuleCompiler()
    {
        DefaultReferences = new List<MetadataReference>();

        // Add core runtime references
        var trustedAssemblies = ((string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") ?? "")
            .Split(Path.PathSeparator);

        var neededAssemblies = new[]
        {
            // Core runtime
            "System.Runtime",
            "System.Private.CoreLib",
            "netstandard",
            
            // Collections
            "System.Collections",
            "System.Collections.Concurrent",
            "System.Collections.Immutable",
            
            // Linq
            "System.Linq",
            "System.Linq.Expressions",
            
            // Numerics
            "System.Numerics",
            "System.Numerics.Vectors",
            
            // I/O
            "System.Console",
            "System.IO",
            "System.IO.FileSystem",
            
            // Text
            "System.Text.RegularExpressions",
            "System.Text.Json",
            "System.Text.Encoding.Extensions",
            
            // Threading
            "System.Threading",
            "System.Threading.Tasks",
            
            // Other common
            "System.Memory",
            "System.ObjectModel",
            "System.ComponentModel",
            "System.ComponentModel.Primitives",
            "Microsoft.CSharp",
            
            // UI
            "System.Windows.Forms",
            
            // WPF
            "WindowsBase",
            "PresentationCore",
            "PresentationFramework",
            "System.Xaml",
        };

        foreach (var assembly in trustedAssemblies)
        {
            var name = Path.GetFileNameWithoutExtension(assembly);
            if (neededAssemblies.Any(n => name.Equals(n, StringComparison.OrdinalIgnoreCase)))
            {
                DefaultReferences.Add(MetadataReference.CreateFromFile(assembly));
            }
        }

        // Add C2VGeometry assembly so user code can `using C2VGeometry;`
        DefaultReferences.Add(MetadataReference.CreateFromFile(typeof(C2VGeometry.Shape).Assembly.Location));

        // Add the DoodleSharp host assembly so user code can `using DoodleSharp.Console;` (VizConsole),
        // `using DoodleSharp.Animation;`, and `using DoodleSharp.Sketching;`. Before the geometry
        // unification this came in via the host-resident DoodleSharp.Geometry assembly; geometry now
        // lives in the separate C2VGeometry.dll, so the host must be referenced explicitly or those
        // namespaces fail to resolve (CS0246) in both the editor and at runtime.
        DefaultReferences.Add(MetadataReference.CreateFromFile(typeof(ModuleCompiler).Assembly.Location));
    }

    /// <summary>
    /// Gets the default metadata references used for compilation.
    /// Useful for semantic analysis features like semantic highlighting.
    /// </summary>
    public IEnumerable<MetadataReference> GetReferences()
    {
        return DefaultReferences;
    }

    public async Task<CompilationResult> CompileAndExecuteAsync(VizCodeProject project)
    {
        // User code runs in-process, so this method is where an app-killing failure is most likely
        // to originate. The scope's ENTER-without-EXIT is the signal that the process died inside a
        // run; the records in between narrow it to compile, load, or execute.
        using var scope = Journal.Scope("EXEC.RUN", "Compile and execute",
            $"project={project.ProjectFile.Name} dir={project.ProjectDirectory}");
        try
        {
            // Tear down any currently-running sketch (unloads its assembly context) before
            // we compile a fresh user assembly; otherwise the prior context would leak.
            DoodleSharp.Sketching.SketchRuntime.Instance.Stop();

            // Frame callbacks hold delegates into the user assembly. Left queued, they pin the
            // collectible load context so it never unloads, and the previous run's callbacks keep
            // firing against shapes this run has already replaced.
            DoodleSharp.Animation.Frame.Clear();

            // Clear previous shapes and console
            CanvasRenderer.Instance.Clear();
            ConsoleOutput.Instance.Clear();

            ConsoleOutput.Instance.WriteLine("Compiler", 0,
                $"Run started. Project: '{project.ProjectFile.Name}', files: " +
                string.Join(", ", project.Files.Select(f => f.FileName)));

            // forExecution: true applies both source rewriters — the shape/animation name rewriter
            // (so HideUnnamedShapes can distinguish named from anonymous shapes) and the stack guard
            // (so runaway recursion surfaces as a catchable InsufficientExecutionStackException
            // rather than an uncatchable StackOverflowException that kills the whole app). Only the
            // execute path opts in: both shift in-line character offsets, which would misdirect the
            // offset-based editor features that share this method.
            var (compilation, allDlls) = await CreateCompilationAsync(project, forExecution: true);

            // Emit to memory stream with PDB for line numbers in stack traces
            using var ms = new MemoryStream();
            using var pdbStream = new MemoryStream();
            Journal.Debug("EXEC.EMIT.BEGIN", "Emitting user assembly", $"assembly={compilation.AssemblyName}");
            var emitResult = compilation.Emit(ms, pdbStream);

            if (!emitResult.Success)
            {
                var errors = emitResult.Diagnostics
                    .Where(d => d.Severity == DiagnosticSeverity.Error)
                    .Select(FormatDiagnostic)
                    .ToList();

                Journal.Warn("EXEC.EMIT.FAIL", $"Compilation failed with {errors.Count} error(s)",
                    string.Join(" ;; ", errors.Take(20)));

                return new CompilationResult
                {
                    Success = false,
                    Error = "Compilation Error:\n" + string.Join(Environment.NewLine, errors),
                    Diagnostics = emitResult.Diagnostics
                };
            }

            // Set working directory to project folder so relative paths resolve correctly
            var previousDirectory = Environment.CurrentDirectory;
            if (!string.IsNullOrEmpty(project.ProjectDirectory))
                Environment.CurrentDirectory = project.ProjectDirectory;

            // Execute
            ms.Seek(0, SeekOrigin.Begin);
            pdbStream.Seek(0, SeekOrigin.Begin);
            Journal.Info("EXEC.EMIT.OK", "User assembly emitted",
                $"assembly={compilation.AssemblyName} bytes={ms.Length} deps={allDlls.Count} cwd={Environment.CurrentDirectory}");
            try
            {
                return await ExecuteAssemblyAsync(ms, pdbStream, allDlls, project.ProjectFile.Name ?? "MyProject");
            }
            finally
            {
                Environment.CurrentDirectory = previousDirectory;
            }
        }
        catch (Exception ex)
        {
            Journal.Error("EXEC.RUN.FAIL", "Compile/execute threw", ex, $"project={project.ProjectFile.Name}");
            return new CompilationResult
            {
                Success = false,
                Error = $"Error: {ex.Message}"
            };
        }
    }

    /// <summary>
    /// Check for syntax and compilation errors without executing.
    /// Used for real-time error checking while typing.
    /// </summary>
    public async Task<CompilationResult> CheckSyntaxAsync(VizCodeProject project)
    {
        try
        {
            var (compilation, _) = await CreateCompilationAsync(project);

            // Get diagnostics without emitting
            var diagnostics = compilation.GetDiagnostics();
            var errors = diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).ToList();

            return new CompilationResult
            {
                Success = errors.Count == 0,
                Error = errors.Count > 0 ? $"{errors.Count} error(s)" : null,
                Diagnostics = diagnostics
            };
        }
        catch (Exception ex)
        {
            return new CompilationResult
            {
                Success = false,
                Error = $"Error: {ex.Message}"
            };
        }
    }

    // ── Resident assembly (Global Parameters fast path) ──
    // After a successful Main() run we keep the load context alive so a global-parameter edit can
    // re-invoke Main() without paying for a Roslyn compile. That is what makes dragging a slider in
    // the Global Parameters panel feel live (~ms) instead of a few hundred ms per tick. Any source
    // edit invalidates it and the next run compiles fresh.
    private static VizAssemblyLoadContext? _residentContext;
    private static MethodInfo? _residentMain;
    private static string? _residentWorkingDirectory;

    /// <summary>True when <see cref="ReExecuteResidentAsync"/> can re-run without recompiling.</summary>
    public static bool HasResidentAssembly => _residentMain != null;

    /// <summary>
    /// Drops the resident assembly and unloads its context. Call whenever the source changes, so the
    /// next run recompiles rather than re-running stale IL.
    /// </summary>
    public static void InvalidateResident()
    {
        _residentMain = null;
        _residentWorkingDirectory = null;
        var ctx = _residentContext;
        _residentContext = null;
        if (ctx != null)
        {
            // Unloading a collectible ALC while user code still holds a reference into it is a
            // classic source of delayed, hard-to-attribute failures — worth a record every time.
            Journal.Debug("EXEC.RESIDENT.UNLOAD", "Unloading resident user assembly context");
            ctx.Unload();
        }
    }

    /// <summary>
    /// Re-invokes <c>Main()</c> on the already-loaded user assembly. Used when only a global
    /// parameter changed — the source is untouched, so the compiled IL is still valid.
    /// <para>
    /// Caveat: static state in user code is <em>not</em> reset between resident re-runs (the same
    /// assembly instance is reused). Straight-line <c>Main()</c> code — the norm — is unaffected;
    /// code that accumulates into a static field will see it grow. A full Run always starts clean.
    /// </para>
    /// </summary>
    public static async Task<CompilationResult> ReExecuteResidentAsync()
    {
        var mainMethod = _residentMain;
        if (mainMethod == null)
            return new CompilationResult { Success = false, Error = "No resident assembly to re-execute." };

        using var scope = Journal.Scope("EXEC.RESIDENT.RERUN", "Re-invoking Main() on the resident assembly",
            $"cwd={_residentWorkingDirectory}");

        CanvasRenderer.Instance.Clear();
        ConsoleOutput.Instance.Clear();

        var previousDirectory = Environment.CurrentDirectory;
        if (!string.IsNullOrEmpty(_residentWorkingDirectory) && Directory.Exists(_residentWorkingDirectory))
            Environment.CurrentDirectory = _residentWorkingDirectory;

        try
        {
            return await InvokeMainAsync(mainMethod);
        }
        catch (Exception ex)
        {
            Journal.Error("EXEC.RESIDENT.FAIL", "Resident re-execution threw", ex);
            return new CompilationResult { Success = false, Error = $"Runtime Error: {FormatRuntimeError(ex)}" };
        }
        finally
        {
            Environment.CurrentDirectory = previousDirectory;
        }
    }

    /// <summary>
    /// Invokes a user <c>Main()</c> inside a global-parameter run scope. <see cref="GlobalParameters.BeginRun"/>
    /// suppresses change notifications for the duration — without it every <c>Set(...)</c> the user
    /// code executes would raise Changed, which re-runs Main(), which raises again. Stale parameters
    /// are pruned only when the run actually completed, so a runtime error does not blank the panel.
    /// </summary>
    private static async Task<CompilationResult> InvokeMainAsync(MethodInfo mainMethod)
    {
        C2VGeometry.GlobalParameters.BeginRun();
        bool completed = false;
        try
        {
            // The boundary between "our code" and "the user's code". A journal that stops between
            // this record and EXEC.MAIN.RETURN means the user's Main() took the process down —
            // typically a StackOverflow or an infinite allocation, neither of which is catchable.
            Journal.Info("EXEC.MAIN.INVOKE", "Entering user Main()",
                $"method={mainMethod.DeclaringType?.FullName}.{mainMethod.Name} params={mainMethod.GetParameters().Length}");

            // Catch TargetInvocationException inside Task.Run to prevent the VS debugger from
            // breaking on a "user-unhandled" exception.
            var invokeException = await Task.Run<Exception?>(() =>
            {
                try
                {
                    if (mainMethod.GetParameters().Length > 0)
                        mainMethod.Invoke(null, new object[] { Array.Empty<string>() });
                    else
                        mainMethod.Invoke(null, null);
                    return null;
                }
                catch (TargetInvocationException ex)
                {
                    return ex.InnerException ?? ex;
                }
            });

            if (invokeException != null)
            {
                Journal.Error("EXEC.MAIN.THREW", "User Main() threw", invokeException);
                return new CompilationResult
                {
                    Success = false,
                    Error = $"Runtime Error: {FormatRuntimeError(invokeException)}"
                };
            }

            completed = true;
            Journal.Info("EXEC.MAIN.RETURN", "User Main() returned normally",
                $"shapes={CanvasRenderer.Instance.GetShapes().Count}");

            // After successful execution, hide shapes without variable names
            // (shapes with names have Name set by AnimationNameRewriter)
            HideUnnamedShapes();

            return new CompilationResult { Success = true };
        }
        finally
        {
            C2VGeometry.GlobalParameters.EndRun(pruneStale: completed);
        }
    }

    public static async Task<CompilationResult> ExecuteAssemblyAsync(Stream assemblyStream, Stream? pdbStream, HashSet<string> dependencies, string projectName)
    {
        // A fresh compile supersedes whatever was resident; release it before loading the new one.
        InvalidateResident();

        // Use our custom AssemblyLoadContext that knows about restored packages
        var loadContext = new VizAssemblyLoadContext(dependencies);
        bool transferredOwnership = false;
        try
        {
            try
            {
                var assembly = pdbStream != null
                    ? loadContext.LoadFromStream(assemblyStream, pdbStream)
                    : loadContext.LoadFromStream(assemblyStream);

                // ── Sketch mode probe ──
                // If the user wrote a class deriving from DoodleSharp.Sketching.Sketch, hand off to
                // SketchRuntime which keeps the load context resident for the per-frame loop.
                var sketchBase = typeof(DoodleSharp.Sketching.Sketch);
                Type[] allUserTypes;
                try { allUserTypes = assembly.GetTypes(); }
                catch (System.Reflection.ReflectionTypeLoadException rtle)
                {
                    Journal.Error("EXEC.LOAD.TYPES_FAIL", "GetTypes() on the user assembly failed", rtle,
                        "loaderExceptions=" + string.Join(" ;; ",
                            (rtle.LoaderExceptions ?? Array.Empty<Exception?>()).Select(le => le?.Message ?? "(null)")));
                    ConsoleOutput.Instance.WriteLine("Compiler", 0,
                        $"GetTypes failed: {rtle.Message}. LoaderExceptions: " +
                        string.Join(" | ", (rtle.LoaderExceptions ?? Array.Empty<Exception?>()).Select(le => le?.Message ?? "(null)")));
                    allUserTypes = rtle.Types.Where(t => t != null).Cast<Type>().ToArray();
                }

                var typeNames = string.Join(", ", allUserTypes.Take(8).Select(t => t.FullName));
                ConsoleOutput.Instance.WriteLine("Compiler", 0,
                    $"Loaded {allUserTypes.Length} type(s) from user assembly: {typeNames}");

                var sketchType = allUserTypes
                    .FirstOrDefault(t => !t.IsAbstract && sketchBase.IsAssignableFrom(t));

                if (sketchType != null)
                {
                    ConsoleOutput.Instance.WriteLine("Compiler", 0,
                        $"Sketch detected: {sketchType.FullName}. Entering Sketch mode.");
                    Journal.Info("EXEC.SKETCH.START", "Entering sketch mode", $"type={sketchType.FullName}");
                    DoodleSharp.Sketching.SketchRuntime.Instance.Start(sketchType, loadContext);
                    transferredOwnership = true;   // SketchRuntime now owns Unload()
                    return new CompilationResult { Success = true };
                }

                ConsoleOutput.Instance.WriteLine("Compiler", 0,
                    "No Sketch subclass found. Falling back to Main() mode.");

                // ── Main() mode (default) ──
                // Get the entry point namespace from project name
                var projectNamespace = Templates.SanitizeIdentifier(projectName);
                var entryTypeName = $"{projectNamespace}.Viz";

                var entryType = assembly.GetType(entryTypeName);

                if (entryType == null)
                {
                    var allTypes = assembly.GetTypes().Select(t => t.FullName).ToList();
                    return new CompilationResult
                    {
                        Success = false,
                        Error = $"Entry point not found: class '{entryTypeName}' is missing.\n\nAvailable types:\n" +
                                string.Join("\n", allTypes.Take(10)) +
                                $"\n\nEnsure StartViz contains:\nnamespace {projectNamespace}\n...\n    class Viz"
                    };
                }

                var mainMethod = entryType.GetMethod("Main", BindingFlags.Public | BindingFlags.Static);
                if (mainMethod == null)
                {
                    // Maybe it's a script without a class? 
                    return new CompilationResult
                    {
                        Success = false,
                        Error = $"Entry point not found: static method 'Main()' is missing in {entryTypeName} class."
                    };
                }

                var result = await InvokeMainAsync(mainMethod);

                if (result.Success)
                {
                    // Keep the assembly loaded so a Global Parameters edit can re-invoke Main()
                    // without recompiling. The next compile (or InvalidateResident) unloads it.
                    _residentContext = loadContext;
                    _residentMain = mainMethod;
                    _residentWorkingDirectory = Environment.CurrentDirectory;
                    transferredOwnership = true;
                }

                return result;
            }
            finally
            {
                // Only unload the load context if SketchRuntime didn't take ownership.
                // Sketch mode keeps the context resident across frames.
                if (!transferredOwnership)
                    loadContext.Unload();
            }
        }
        catch (TargetInvocationException ex)
        {
            Journal.Error("EXEC.LOAD.INVOKE_FAIL", "Reflection invoke failed", ex.InnerException ?? ex);
            return new CompilationResult
            {
                Success = false,
                Error = $"Runtime Error: {FormatRuntimeError(ex.InnerException ?? ex)}"
            };
        }
        catch (Exception ex)
        {
            Journal.Error("EXEC.LOAD.FAIL", "Assembly load/execute failed", ex, $"project={projectName}");
            return new CompilationResult
            {
                Success = false,
                Error = $"Runtime Error: {FormatRuntimeError(ex)}"
            };
        }
    }

    /// <summary>
    /// Formats a runtime exception with file/line info extracted from the stack trace.
    /// </summary>
    private static string FormatRuntimeError(Exception ex)
    {
        var message = ex.Message;

        // Parse the stack trace to find frames in user source files
        if (ex.StackTrace != null)
        {
            var lines = ex.StackTrace.Split('\n');
            foreach (var line in lines)
            {
                // Look for stack frames with .cs file references
                // Format: "at Namespace.Class.Method() in C:\path\file.cs:line 42"
                var match = System.Text.RegularExpressions.Regex.Match(
                    line, @"in\s+(.+\.cs):line\s+(\d+)");
                if (match.Success)
                {
                    var filePath = match.Groups[1].Value;
                    var lineNumber = match.Groups[2].Value;
                    var fileName = Path.GetFileName(filePath);

                    // Also extract the method name for context
                    var methodMatch = System.Text.RegularExpressions.Regex.Match(
                        line, @"at\s+(.+?)\(");
                    var methodInfo = methodMatch.Success ? methodMatch.Groups[1].Value : null;

                    // Simplify method name - just show "Class.Method"
                    if (methodInfo != null)
                    {
                        var parts = methodInfo.Split('.');
                        if (parts.Length >= 2)
                            methodInfo = string.Join(".", parts.Skip(parts.Length - 2));
                    }

                    message += $"\n  at {(methodInfo != null ? methodInfo + " " : "")}({fileName}, line {lineNumber})";
                }
            }
        }

        return message;
    }

    /// <summary>
    /// Hides shapes that don't have a Name set (anonymous/inline shapes).
    /// Only shapes with explicit variable names are shown.
    /// </summary>
    private static void HideUnnamedShapes()
    {
        var shapes = CanvasRenderer.Instance.GetShapes();
        int hiddenCount = 0;
        var typeCounts = new Dictionary<string, int>();
        foreach (var drawable in shapes)
        {
            if (drawable is C2VGeometry.Shape shape && string.IsNullOrEmpty(shape.Name) && !shape.IsExplicitlyDrawn && shape.IsVisible)
            {
                shape.IsVisible = false;
                hiddenCount++;
                var typeName = shape.GetType().Name;
                typeCounts[typeName] = typeCounts.GetValueOrDefault(typeName) + 1;
            }
        }

        if (hiddenCount > 0)
        {
            var breakdown = string.Join(", ", typeCounts
                .OrderByDescending(kv => kv.Value)
                .Select(kv => $"{kv.Value} {kv.Key}"));
            ConsoleOutput.Instance.WriteLine("DoodleSharp", 0,
                $"Warning: {hiddenCount} unnamed shape(s) hidden ({breakdown}). " +
                "To keep them visible, assign to a var (e.g. var x = new VLine(...)) " +
                "or set Name explicitly in the initializer.");
        }
    }

    public async Task<(List<MetadataReference> References, HashSet<string> AllDlls)> GetProjectReferencesAndDllsAsync(VizCodeProject project)
    {
        var references = new List<MetadataReference>(DefaultReferences);
        var allDlls = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (project.ProjectFile != null && project.ProjectFile.Packages.Any())
        {
            var packagesDir = Path.Combine(project.ProjectDirectory, ".packages");
            using var nuget = new NuGetHelper(packagesDir);

            foreach (var pkg in project.ProjectFile.Packages)
            {
                try
                {
                    var dlls = await nuget.RestorePackageAsync(pkg.Id, pkg.Version);
                    foreach (var dll in dlls) allDlls.Add(dll);
                }
                catch (Exception ex)
                {
                    Journal.Warn("EXEC.NUGET.RESTORE_FAIL", "NuGet restore failed", $"package={pkg.Id}@{pkg.Version}", ex);
                    ConsoleOutput.Instance.WriteLine("Compiler", 0, $"Warning: Failed to restore {pkg.Id}: {ex.Message}");
                }
            }

            foreach (var dll in allDlls)
            {
                using var fs = File.OpenRead(dll);
                references.Add(MetadataReference.CreateFromStream(fs, filePath: dll));
            }
        }

        // Load project assembly references
        if (project.ProjectFile?.References?.Any() == true)
        {
            var trustedAssemblies = ((string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") ?? "")
                .Split(Path.PathSeparator);

            foreach (var asmRef in project.ProjectFile.References)
            {
                try
                {
                    if (asmRef.IsFramework)
                    {
                        // Find framework assembly by name
                        var match = trustedAssemblies.FirstOrDefault(a =>
                            Path.GetFileNameWithoutExtension(a).Equals(asmRef.Path, StringComparison.OrdinalIgnoreCase));
                        if (match != null)
                        {
                            references.Add(MetadataReference.CreateFromFile(match));
                        }
                        else
                        {
                            ConsoleOutput.Instance.WriteLine("Compiler", 0, $"Warning: Framework assembly '{asmRef.Path}' not found.");
                        }
                    }
                    else
                    {
                        // Load local DLL
                        var dllPath = asmRef.Path;
                        if (!Path.IsPathRooted(dllPath))
                        {
                            dllPath = Path.Combine(project.ProjectDirectory, dllPath);
                        }

                        if (File.Exists(dllPath))
                        {
                            using var fs = File.OpenRead(dllPath);
                            references.Add(MetadataReference.CreateFromStream(fs, filePath: dllPath));
                            allDlls.Add(dllPath); // Add for runtime loading context
                        }
                        else
                        {
                            ConsoleOutput.Instance.WriteLine("Compiler", 0, $"Warning: Assembly '{asmRef.Path}' not found.");
                        }
                    }
                }
                catch (Exception ex)
                {
                    ConsoleOutput.Instance.WriteLine("Compiler", 0, $"Warning: Failed to load reference '{asmRef.Path}': {ex.Message}");
                }
            }
        }

        return (references, allDlls);
    }

    /// <summary>
    /// Builds a compilation over every source file in the project.
    /// </summary>
    /// <param name="project">The project whose sources to compile.</param>
    /// <param name="forExecution">
    /// <para>
    /// <c>false</c> (the default) produces an <b>offset-faithful</b> compilation: the syntax trees
    /// correspond character-for-character to what the user has on screen. Every editor feature that
    /// maps an editor offset onto the compilation — go-to-definition, find-all-references, rename,
    /// quick actions, syntax checking — <b>must</b> use this mode.
    /// </para>
    /// <para>
    /// <c>true</c> applies the two source rewriters needed to actually run the code, both of which
    /// shift character offsets:
    /// <list type="bullet">
    ///   <item><see cref="AnimationNameRewriter"/> injects <c>{ Name = "x" }</c> into shape and
    ///   animation declarations (so <c>HideUnnamedShapes</c> can tell named from anonymous shapes).</item>
    ///   <item><see cref="StackGuardRewriter"/> injects a stack probe into every method body, turning
    ///   runaway recursion into a catchable exception instead of a process kill (see
    ///   <c>SKETCH_ISOLATION_PLAN.md</c>).</item>
    /// </list>
    /// </para>
    /// <para>
    /// The default is deliberately the safe one. Previously the name rewriter ran unconditionally,
    /// so F12 / Shift+F12 / rename resolved the wrong token in any file containing a named shape
    /// declaration ahead of the caret, and rename wrote its edits at shifted offsets into other
    /// files. A new caller that forgets this flag now gets correct offsets rather than silent
    /// corruption.
    /// </para>
    /// </param>
    public async Task<(CSharpCompilation Compilation, HashSet<string> AllDlls)> CreateCompilationAsync(
        VizCodeProject project, bool forExecution = false)
    {
        // Get ALL source files from project directory (not just open ones)
        var allSourceFiles = project.GetAllSourceFiles().ToList();

        // Parse all source files into syntax trees
        var rewriter = forExecution ? new AnimationNameRewriter() : null;
        var syntaxTrees = allSourceFiles.Select(file =>
        {
            var sourceText = Microsoft.CodeAnalysis.Text.SourceText.From(
                file.Content, System.Text.Encoding.UTF8);
            var tree = CSharpSyntaxTree.ParseText(
                sourceText,
                path: file.FilePath,
                options: new CSharpParseOptions(LanguageVersion.Latest));

            if (rewriter == null)
                return tree;   // offset-faithful: hand back exactly what the user typed

            // Transform animation variable declarations to include Name property
            var newRoot = rewriter.Visit(tree.GetRoot());

            // Guard against uncatchable StackOverflowException from runaway recursion. The guard
            // preserves line numbers (for stack traces) but shifts in-line offsets, which is why it
            // rides along only on the execute path.
            newRoot = StackGuardRewriter.Inject(newRoot);

            // IMPORTANT: Preserve the original file path when creating the new tree
            // Using newRoot.SyntaxTree loses the file path!
            return tree.WithRootAndOptions(newRoot, tree.Options);
        }).ToList();

        Journal.Debug("EXEC.PARSE.DONE", "Syntax trees built",
            $"files={syntaxTrees.Count} forExecution={forExecution} chars={allSourceFiles.Sum(f => f.Content.Length)}");

        // Resolve NuGet packages and references
        var (references, allDlls) = await GetProjectReferencesAndDllsAsync(project);

        // Create compilation
        var compilation = CSharpCompilation.Create(
            assemblyName: $"VizCodeAssembly_{Guid.NewGuid():N}",
            syntaxTrees: syntaxTrees,
            references: references,
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)
                .WithOptimizationLevel(OptimizationLevel.Debug)
                .WithPlatform(Platform.X64)
        );

        return (compilation, allDlls);
    }

    private static string FormatDiagnostic(Diagnostic d)
    {
        var location = d.Location.GetLineSpan();
        var fileName = Path.GetFileName(location.Path);
        var line = location.StartLinePosition.Line + 1;
        var col = location.StartLinePosition.Character + 1;

        // Include filename if available
        if (!string.IsNullOrEmpty(fileName))
            return $"{fileName}({line},{col}): error {d.Id}: {d.GetMessage()}";

        return $"({line},{col}): error {d.Id}: {d.GetMessage()}";
    }
}

public class VizAssemblyLoadContext : AssemblyLoadContext
{
    private readonly HashSet<string> _dependencyPaths;
    private static readonly Assembly _hostAssembly = typeof(VizAssemblyLoadContext).Assembly;
    private static readonly string _hostAssemblyName = typeof(VizAssemblyLoadContext).Assembly.GetName().Name!;

    public VizAssemblyLoadContext(HashSet<string> dependencyPaths) : base(isCollectible: true)
    {
        _dependencyPaths = dependencyPaths;
    }

    protected override Assembly? Load(AssemblyName assemblyName)
    {
        // IMPORTANT: Return the host DoodleSharp assembly to share singletons like CanvasRenderer.Instance
        if (string.Equals(assemblyName.Name, _hostAssemblyName, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(assemblyName.Name, "DoodleSharp", StringComparison.OrdinalIgnoreCase))
        {
            return _hostAssembly;
        }
        
        // Check if we have a path for this assembly
        // We look for any path that matches the assembly name (case insensitive)
        foreach (var path in _dependencyPaths)
        {
            var fileName = Path.GetFileNameWithoutExtension(path);
            if (string.Equals(fileName, assemblyName.Name, StringComparison.OrdinalIgnoreCase))
            {
                // Found it, load from file
                return LoadFromAssemblyPath(path);
            }
        }

        // Return null to allow default loading (from runtime, etc.)
        return null;
    }
}

/// <summary>
/// Rewrites animation and shape variable declarations to automatically set the Name property
/// to the variable name. Transforms:
///   Animation circleAnim = new MoveAnimation(...);
///   VCircle myCircle = new VCircle(0, 0, 10);
/// To:
///   Animation circleAnim = new MoveAnimation(...) { Name = "circleAnim" };
///   VCircle myCircle = new VCircle(0, 0, 10) { Name = "myCircle" };
/// </summary>
internal class AnimationNameRewriter : CSharpSyntaxRewriter
{
    private static readonly HashSet<string> AnimationTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "Animation", "DrawAnimation", "MoveAnimation", "RotateAnimation",
        "FlipAnimation", "FadeInAnimation", "FadeOutAnimation", "ValueAnimation",
        "ObjectPropertyAnimation"
    };

    // Every drawable type a user can declare. A type missing from this list is never auto-named,
    // and HideUnnamedShapes then hides it once Main() returns — so `var h = new VHatch(...)` simply
    // produced nothing, which is what the documented hatch examples were hitting.
    private static readonly HashSet<string> ShapeTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "VPoint", "VLine", "VCircle", "VArc", "VRectangle", "VEllipse",
        "VPolygon", "VPolyline", "VBezier", "VSpline", "VArrow", "VText",
        "VGrid", "VGroup", "VDimension", "Region", "VXLine", "VRay",
        "VHatch", "VRadialDimension", "VSpatialGrid"
    };

    public override SyntaxNode? VisitLocalDeclarationStatement(LocalDeclarationStatementSyntax node)
    {
        var declaration = node.Declaration;
        var variables = declaration.Variables;

        // Process each variable in the declaration
        var newVariables = new List<VariableDeclaratorSyntax>();
        bool anyChanged = false;

        foreach (var variable in variables)
        {
            var newVariable = TryRewriteNamedVariable(declaration.Type, variable);
            if (newVariable != variable)
                anyChanged = true;
            newVariables.Add(newVariable);
        }

        if (!anyChanged)
            return base.VisitLocalDeclarationStatement(node);

        var newDeclaration = declaration.WithVariables(
            SyntaxFactory.SeparatedList(newVariables));
        return node.WithDeclaration(newDeclaration);
    }

    public override SyntaxNode? VisitFieldDeclaration(FieldDeclarationSyntax node)
    {
        var declaration = node.Declaration;
        var variables = declaration.Variables;

        // Process each variable in the declaration
        var newVariables = new List<VariableDeclaratorSyntax>();
        bool anyChanged = false;

        foreach (var variable in variables)
        {
            var newVariable = TryRewriteNamedVariable(declaration.Type, variable);
            if (newVariable != variable)
                anyChanged = true;
            newVariables.Add(newVariable);
        }

        if (!anyChanged)
            return base.VisitFieldDeclaration(node);

        var newDeclaration = declaration.WithVariables(
            SyntaxFactory.SeparatedList(newVariables));
        return node.WithDeclaration(newDeclaration);
    }

    private VariableDeclaratorSyntax TryRewriteNamedVariable(TypeSyntax type, VariableDeclaratorSyntax variable)
    {
        // Check if this is an animation or shape type (explicit or var)
        // For generic types like ValueAnimation<VCircle>, extract the base name
        var typeName = type.ToString();
        var baseTypeName = type is GenericNameSyntax genericType
            ? genericType.Identifier.Text
            : typeName;
        bool isExplicitNamedType = AnimationTypes.Contains(typeName) || AnimationTypes.Contains(baseTypeName) || ShapeTypes.Contains(typeName);

        // Check if initializer is an object creation expression
        if (variable.Initializer?.Value is ObjectCreationExpressionSyntax objectCreation)
        {
            // Check if the created type is an animation or shape type
            // For generic types like ValueAnimation<VCircle>, extract the base name
            var createdTypeName = objectCreation.Type.ToString();
            var baseCreatedTypeName = objectCreation.Type is GenericNameSyntax genericName
                ? genericName.Identifier.Text
                : createdTypeName;
            bool isCreatedTypeNamed = AnimationTypes.Contains(createdTypeName) || AnimationTypes.Contains(baseCreatedTypeName) || ShapeTypes.Contains(createdTypeName);

            // Only add Name initializer if the created type is actually an animation/shape type
            // This prevents adding Name to List<VPoint> and other non-shape types when 'var' is used
            if (!isExplicitNamedType && !isCreatedTypeNamed)
                return variable;

            return TryAddNameInitializer(variable, objectCreation, objectCreation.Initializer);
        }

        // Handle target-typed new: VLine line = new(...)
        if (variable.Initializer?.Value is ImplicitObjectCreationExpressionSyntax implicitCreation)
        {
            // For implicit object creation, we need an explicit named type
            if (!isExplicitNamedType)
                return variable;

            return TryAddNameInitializerImplicit(variable, implicitCreation, implicitCreation.Initializer);
        }

        return variable;
    }

    private VariableDeclaratorSyntax TryAddNameInitializer(
        VariableDeclaratorSyntax variable,
        ObjectCreationExpressionSyntax objectCreation,
        InitializerExpressionSyntax? existingInitializer)
    {
        // Skip if has a collection initializer (cannot mix with object initializer)
        if (existingInitializer != null && existingInitializer.Kind() == SyntaxKind.CollectionInitializerExpression)
            return variable;

        // Skip if already has an initializer with Name set
        if (existingInitializer != null)
        {
            var hasNameProperty = existingInitializer.Expressions
                .OfType<AssignmentExpressionSyntax>()
                .Any(a => a.Left.ToString() == "Name");
            if (hasNameProperty)
                return variable;
        }

        // Create the Name = "variableName" assignment
        var variableName = variable.Identifier.Text;
        var nameAssignment = SyntaxFactory.AssignmentExpression(
            SyntaxKind.SimpleAssignmentExpression,
            SyntaxFactory.IdentifierName("Name"),
            SyntaxFactory.LiteralExpression(
                SyntaxKind.StringLiteralExpression,
                SyntaxFactory.Literal(variableName)));

        // Create or extend the object initializer
        InitializerExpressionSyntax newInitializer;
        if (existingInitializer != null)
        {
            // Add to existing initializer
            var newExpressions = existingInitializer.Expressions.Add(nameAssignment);
            newInitializer = existingInitializer.WithExpressions(newExpressions);
        }
        else
        {
            // Create new initializer
            newInitializer = SyntaxFactory.InitializerExpression(
                SyntaxKind.ObjectInitializerExpression,
                SyntaxFactory.SingletonSeparatedList<ExpressionSyntax>(nameAssignment));
        }

        var newObjectCreation = objectCreation.WithInitializer(newInitializer);
        var newInitializerClause = variable.Initializer!.WithValue(newObjectCreation);
        return variable.WithInitializer(newInitializerClause);
    }

    private VariableDeclaratorSyntax TryAddNameInitializerImplicit(
        VariableDeclaratorSyntax variable,
        ImplicitObjectCreationExpressionSyntax implicitCreation,
        InitializerExpressionSyntax? existingInitializer)
    {
        // Skip if has a collection initializer (cannot mix with object initializer)
        if (existingInitializer != null && existingInitializer.Kind() == SyntaxKind.CollectionInitializerExpression)
            return variable;

        // Skip if already has an initializer with Name set
        if (existingInitializer != null)
        {
            var hasNameProperty = existingInitializer.Expressions
                .OfType<AssignmentExpressionSyntax>()
                .Any(a => a.Left.ToString() == "Name");
            if (hasNameProperty)
                return variable;
        }

        // Create the Name = "variableName" assignment
        var variableName = variable.Identifier.Text;
        var nameAssignment = SyntaxFactory.AssignmentExpression(
            SyntaxKind.SimpleAssignmentExpression,
            SyntaxFactory.IdentifierName("Name"),
            SyntaxFactory.LiteralExpression(
                SyntaxKind.StringLiteralExpression,
                SyntaxFactory.Literal(variableName)));

        // Create or extend the object initializer
        InitializerExpressionSyntax newInitializer;
        if (existingInitializer != null)
        {
            // Add to existing initializer
            var newExpressions = existingInitializer.Expressions.Add(nameAssignment);
            newInitializer = existingInitializer.WithExpressions(newExpressions);
        }
        else
        {
            // Create new initializer
            newInitializer = SyntaxFactory.InitializerExpression(
                SyntaxKind.ObjectInitializerExpression,
                SyntaxFactory.SingletonSeparatedList<ExpressionSyntax>(nameAssignment));
        }

        var newImplicitCreation = implicitCreation.WithInitializer(newInitializer);
        var newInitializerClause = variable.Initializer!.WithValue(newImplicitCreation);
        return variable.WithInitializer(newInitializerClause);
    }
}
