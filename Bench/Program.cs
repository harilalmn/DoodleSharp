using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace DoodleSharp.Bench;

// These aliases must sit INSIDE the namespace, not above it. This project's namespace is nested
// under DoodleSharp, which contains a member namespace called Console — and members of an enclosing
// namespace beat using-aliases declared at compilation-unit level, so a top-of-file
// `using Console = System.Console;` is silently ignored and every Console.WriteLine fails to bind.
using Console = System.Console;
using Path = System.IO.Path;
using Directory = System.IO.Directory;
using File = System.IO.File;

/// <summary>
/// Entry point for the render benchmark.
///
/// <para>
/// Usage: <c>DoodleSharp.Bench [--scene NAME] [--path NAME] [--budget N] [--size WxH]
/// [--no-raster] [--out FILE]</c>. With no arguments it runs every scene against every camera path
/// at a reduced budget, which takes a couple of minutes; <c>--budget</c> raises it to the real
/// numbers when you want a headline figure rather than a regression check.
/// </para>
/// </summary>
internal static class Program
{
    // STA because RenderCanvas is a WPF FrameworkElement — WPF objects derive from DispatcherObject
    // and cannot be constructed on an MTA thread.
    [STAThread]
    private static int Main(string[] args)
    {
        try
        {
            return Run(args);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Benchmark failed: {ex}");
            return 1;
        }
    }

    private static int Run(string[] args)
    {
        var sceneFilter = Arg(args, "--scene");
        var pathFilter = Arg(args, "--path");
        var outPath = Arg(args, "--out");
        var rasterize = !args.Contains("--no-raster");

        // Which renderer to measure. The whole point of keeping the legacy path alive is being able
        // to run the same scene through both and compare, rather than remembering what it used to do.
        var backend = Arg(args, "--backend") ?? "Legacy";
        ApplicationSettings.Instance.RenderBackend = backend;

        var budget = int.TryParse(Arg(args, "--budget"), out var b) ? b : 50_000;
        var (width, height) = ParseSize(Arg(args, "--size") ?? "1920x1080");

        var scenes = SceneGenerator.All
            .Where(s => sceneFilter == null || s.Name == sceneFilter)
            .ToArray();
        var paths = CameraPath.All
            .Where(p => pathFilter == null || p.Name == pathFilter)
            .ToArray();

        if (scenes.Length == 0) { Console.Error.WriteLine($"No scene matches '{sceneFilter}'."); return 2; }
        if (paths.Length == 0) { Console.Error.WriteLine($"No path matches '{pathFilter}'."); return 2; }

        Console.WriteLine($"DoodleSharp render benchmark");
        Console.WriteLine($"  viewport   {width}x{height}");
        Console.WriteLine($"  budget     {budget:N0} shapes/scene");
        Console.WriteLine($"  rasterize  {(rasterize ? "yes (includes WPF tessellation + composition)" : "no (instruction build only)")}");
        Console.WriteLine($"  backend    {backend}");
        Console.WriteLine();

        var runner = new BenchRunner(width, height, rasterize);

        // --png renders a single frame to an image instead of timing anything, so visual changes
        // (level of detail, the dense-hatch substitution) can be checked rather than assumed.
        var png = Arg(args, "--png");
        if (png != null)
        {
            var zoom = double.TryParse(Arg(args, "--zoom"), out var z) ? z : 0.3;
            foreach (var scene in scenes)
            {
                var file = scenes.Length == 1 ? png
                    : Path.Combine(Path.GetDirectoryName(png) ?? ".",
                                   $"{Path.GetFileNameWithoutExtension(png)}-{scene.Name}.png");
                runner.RenderSnapshot(scene.Name, budget, zoom, file);
                Console.WriteLine($"  wrote {file}");
            }
            return 0;
        }

        var results = new List<BenchResult>();

        foreach (var scene in scenes)
        {
            foreach (var path in paths)
            {
                Console.Write($"  {scene.Name,-16} {path.Name,-14} … ");
                Console.Out.Flush();

                var result = runner.Run(scene, path, budget);
                results.Add(result);

                Console.WriteLine(
                    $"{result.ShapeCount,9:N0} shapes  " +
                    $"p50 {result.Frames.P50Ms,7:F2}ms  p95 {result.Frames.P95Ms,7:F2}ms " +
                    $"({result.Frames.P95Fps,5:F1} fps)");

                // Force a collection between runs so one scene's garbage cannot be attributed to
                // the next scene's allocation numbers.
                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();
            }
        }

        Console.WriteLine();
        PrintTable(results);

        if (outPath != null)
        {
            var json = JsonSerializer.Serialize(new
            {
                generatedUtc = DateTime.UtcNow.ToString("O"),
                machine = Environment.MachineName,
                processors = Environment.ProcessorCount,
                runtime = Environment.Version.ToString(),
                width,
                height,
                budget,
                rasterize,
                results,
            }, new JsonSerializerOptions
            {
                WriteIndented = true,
                DefaultIgnoreCondition = JsonIgnoreCondition.Never,
            });

            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outPath))!);
            File.WriteAllText(outPath, json, Encoding.UTF8);
            Console.WriteLine($"Wrote {outPath}");
        }

        return 0;
    }

    private static void PrintTable(List<BenchResult> results)
    {
        Console.WriteLine("scene            path           shapes      p50      p95      p99   cull  tess  rast  visible/considered   alloc/f  gen0   hit p99");
        Console.WriteLine("─────────────────────────────────────────────────────────────────────────────────────────────────────────────────────────────────────");

        foreach (var r in results)
        {
            var f = r.Frames;
            Console.WriteLine(
                $"{r.Scene,-16} {r.Path,-13} {r.ShapeCount,8:N0} " +
                $"{f.P50Ms,8:F2} {f.P95Ms,8:F2} {f.P99Ms,8:F2} " +
                $"{f.CullMs,6:F2}{f.TessellateMs,6:F2}{f.RasterMs,6:F2} " +
                $"{f.MeanVisibleShapes,8:N0}/{f.MeanConsideredShapes,-10:N0} " +
                $"{f.MeanAllocatedBytes / 1024.0,8:F1}KB {f.Gen0Collections,5} " +
                $"{r.HitTestP99Ms,8:F3}ms");
        }

        Console.WriteLine();
        Console.WriteLine("cull/tess/rast are mean ms per frame. 'considered' is shapes examined to find 'visible' —");
        Console.WriteLine("when it tracks the document size rather than the visible count, culling is doing nothing.");
    }

    private static string? Arg(string[] args, string name)
    {
        var i = Array.IndexOf(args, name);
        return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
    }

    private static (int, int) ParseSize(string spec)
    {
        var parts = spec.Split('x', 'X');
        if (parts.Length == 2 && int.TryParse(parts[0], out var w) && int.TryParse(parts[1], out var h))
            return (w, h);
        return (1920, 1080);
    }
}
