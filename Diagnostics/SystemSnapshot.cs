using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;

namespace DoodleSharp.Diagnostics;

/// <summary>
/// Collects the machine/process facts that a crash journal is useless without: what OS and .NET
/// build, which GPU and driver, how much memory, which assemblies were actually loaded.
///
/// <para>
/// Everything here is best-effort and exception-proof — a snapshot that fails must degrade to a
/// <c>&lt;unavailable&gt;</c> line, never to a startup failure. No WMI: it is slow and frequently
/// broken on the machines that report random crashes in the first place.
/// </para>
/// </summary>
public static class SystemSnapshot
{
    /// <summary>Static facts about the machine, OS, runtime and process. Written once, in the header.</summary>
    public static IEnumerable<string> DescribeEnvironment()
    {
        var lines = new List<string>();

        Add(lines, "app.version", () => typeof(SystemSnapshot).Assembly.GetName().Version?.ToString());
        Add(lines, "app.informational", () => typeof(SystemSnapshot).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion);
        Add(lines, "app.location", () => Environment.ProcessPath);
        Add(lines, "app.basedir", () => AppContext.BaseDirectory);
        Add(lines, "app.commandline", () => Environment.CommandLine);
        Add(lines, "app.workingdir", () => Environment.CurrentDirectory);

        Add(lines, "os.description", () => RuntimeInformation.OSDescription);
        Add(lines, "os.version", () => Environment.OSVersion.VersionString);
        Add(lines, "os.arch", () => RuntimeInformation.OSArchitecture.ToString());
        Add(lines, "os.64bit", () => Environment.Is64BitOperatingSystem.ToString());
        Add(lines, "os.uptime", () => TimeSpan.FromMilliseconds(Environment.TickCount64).ToString(@"d\.hh\:mm\:ss"));

        Add(lines, "clr.framework", () => RuntimeInformation.FrameworkDescription);
        Add(lines, "clr.runtimedir", () => RuntimeEnvironmentDirectory());
        Add(lines, "clr.processarch", () => RuntimeInformation.ProcessArchitecture.ToString());
        Add(lines, "clr.servergc", () => System.Runtime.GCSettings.IsServerGC.ToString());
        Add(lines, "clr.latencymode", () => System.Runtime.GCSettings.LatencyMode.ToString());

        Add(lines, "cpu.count", () => Environment.ProcessorCount.ToString(CultureInfo.InvariantCulture));
        Add(lines, "cpu.identifier", () => Environment.GetEnvironmentVariable("PROCESSOR_IDENTIFIER"));

        Add(lines, "mem.physical.total", () => Bytes(GC.GetGCMemoryInfo().TotalAvailableMemoryBytes));
        Add(lines, "mem.workingset", () => Bytes(Environment.WorkingSet));

        Add(lines, "proc.id", () => Environment.ProcessId.ToString(CultureInfo.InvariantCulture));
        Add(lines, "proc.starttime", () => SafeProcessStartTime());
        Add(lines, "proc.64bit", () => Environment.Is64BitProcess.ToString());
        Add(lines, "proc.elevated", () => IsElevated());
        Add(lines, "proc.debugger", () => Debugger.IsAttached.ToString());

        Add(lines, "user.name", () => Environment.UserName);
        Add(lines, "user.domain", () => Environment.UserDomainName);
        Add(lines, "user.interactive", () => Environment.UserInteractive.ToString());
        Add(lines, "machine.name", () => Environment.MachineName);

        Add(lines, "locale.culture", () => CultureInfo.CurrentCulture.Name);
        Add(lines, "locale.uiculture", () => CultureInfo.CurrentUICulture.Name);
        Add(lines, "locale.timezone", () => TimeZoneInfo.Local.DisplayName);

        Add(lines, "path.temp", () => Path.GetTempPath());
        Add(lines, "path.journal", () => Journal.Directory);
        Add(lines, "disk.temp.free", () => FreeSpaceOn(Path.GetTempPath()));

        foreach (var line in DescribeDisplayAdapters())
            lines.Add(line);

        foreach (var line in DescribeInterestingEnvironmentVariables())
            lines.Add(line);

        return lines;
    }

    /// <summary>Assemblies loaded at header time — version drift and a stray old DLL are common causes.</summary>
    public static IEnumerable<string> DescribeLoadedAssemblies()
    {
        var lines = new List<string> { "assemblies.loaded:" };
        try
        {
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies()
                         .OrderBy(a => a.GetName().Name, StringComparer.OrdinalIgnoreCase))
            {
                try
                {
                    var name = assembly.GetName();
                    var location = assembly.IsDynamic ? "<dynamic>" : assembly.Location;
                    lines.Add($"  {name.Name,-45} {name.Version,-14} {location}");
                }
                catch { /* skip this assembly */ }
            }
        }
        catch (Exception ex)
        {
            lines.Add($"  <unavailable: {ex.GetType().Name}>");
        }
        return lines;
    }

    /// <summary>Volatile runtime state, sampled on every state dump (i.e. on every crash).</summary>
    public static IEnumerable<string> DescribeRuntimeState()
    {
        var lines = new List<string>();
        try
        {
            var info = GC.GetGCMemoryInfo();
            lines.Add($"gc.heap = {Bytes(GC.GetTotalMemory(forceFullCollection: false))}  committed = {Bytes(info.TotalCommittedBytes)}  fragmented = {Bytes(info.FragmentedBytes)}");
            lines.Add($"gc.collections = gen0:{GC.CollectionCount(0)} gen1:{GC.CollectionCount(1)} gen2:{GC.CollectionCount(2)}  pauseTimePct = {info.PauseTimePercentage:F2}");
            lines.Add($"gc.pinned = {info.PinnedObjectsCount}  lastGenerationSize = {Bytes(info.HeapSizeBytes)}");
            lines.Add($"mem.workingset = {Bytes(Environment.WorkingSet)}");
            lines.Add($"threads.pool = {ThreadPoolDescription()}");
        }
        catch (Exception ex)
        {
            lines.Add($"<runtime state unavailable: {ex.GetType().Name}: {ex.Message}>");
        }

        try
        {
            using var process = Process.GetCurrentProcess();
            lines.Add($"proc.counters = {DescribeProcessCounters(process)}");
        }
        catch { }

        try
        {
            var loaded = AppDomain.CurrentDomain.GetAssemblies().Length;
            lines.Add($"assemblies.count = {loaded}");
        }
        catch { }

        return lines;
    }

    /// <summary>
    /// One-line process counters for the heartbeat. Handle counts matter more than they look:
    /// a GDI/USER handle leak is a classic cause of a WPF app dying "randomly" once it hits the
    /// per-process 10,000 limit, and the only visible symptom beforehand is this number climbing.
    /// </summary>
    public static string DescribeProcessCounters(Process? cached)
    {
        try
        {
            var process = cached;
            if (process == null) return "<no process handle>";

            process.Refresh();
            var builder = new StringBuilder();
            builder.Append("ws=").Append(Bytes(process.WorkingSet64));
            builder.Append(" priv=").Append(Bytes(process.PrivateMemorySize64));
            builder.Append(" gcheap=").Append(Bytes(GC.GetTotalMemory(false)));
            builder.Append(" threads=").Append(process.Threads.Count.ToString(CultureInfo.InvariantCulture));
            builder.Append(" handles=").Append(process.HandleCount.ToString(CultureInfo.InvariantCulture));

            if (OperatingSystem.IsWindows())
            {
                var gdi = GuiResources(process, GuiResourcesGdiObjects);
                var user = GuiResources(process, GuiResourcesUserObjects);
                builder.Append(" gdi=").Append(gdi < 0 ? "?" : gdi.ToString(CultureInfo.InvariantCulture));
                builder.Append(" user=").Append(user < 0 ? "?" : user.ToString(CultureInfo.InvariantCulture));
            }

            builder.Append(" gc=").Append(GC.CollectionCount(0)).Append('/')
                   .Append(GC.CollectionCount(1)).Append('/').Append(GC.CollectionCount(2));
            builder.Append(" cpu=").Append(process.TotalProcessorTime.TotalSeconds.ToString("F1", CultureInfo.InvariantCulture)).Append('s');
            return builder.ToString();
        }
        catch (Exception ex)
        {
            return $"<counters unavailable: {ex.GetType().Name}>";
        }
    }

    /// <summary>
    /// Display adapter and driver, read straight from the registry. WPF renders through the GPU, so
    /// a stale or crashing display driver is one of the highest-probability explanations for a WPF app
    /// that dies with no managed exception. The driver version here is directly comparable against
    /// the vendor's known-bad lists.
    /// </summary>
    private static IEnumerable<string> DescribeDisplayAdapters()
    {
        var lines = new List<string>();
        if (!OperatingSystem.IsWindows())
            return lines;

        try
        {
            using var classKey = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(
                @"SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}");
            if (classKey == null)
            {
                lines.Add("gpu = <registry key not found>");
                return lines;
            }

            var index = 0;
            foreach (var subKeyName in classKey.GetSubKeyNames())
            {
                if (!subKeyName.All(char.IsDigit)) continue;   // skip "Properties", "Configuration"
                using var adapter = classKey.OpenSubKey(subKeyName);
                if (adapter == null) continue;

                var description = adapter.GetValue("DriverDesc") as string;
                if (string.IsNullOrEmpty(description)) continue;

                lines.Add($"gpu[{index}].name = {description}");
                lines.Add($"gpu[{index}].driver = {adapter.GetValue("DriverVersion")} ({adapter.GetValue("DriverDate")})");
                lines.Add($"gpu[{index}].provider = {adapter.GetValue("ProviderName")}");
                index++;
            }

            if (index == 0) lines.Add("gpu = <no adapters enumerated>");
        }
        catch (Exception ex)
        {
            lines.Add($"gpu = <unavailable: {ex.GetType().Name}: {ex.Message}>");
        }

        return lines;
    }

    private static IEnumerable<string> DescribeInterestingEnvironmentVariables()
    {
        var lines = new List<string>();
        try
        {
            // Deliberately a whitelist: the full environment is large and can carry secrets.
            var prefixes = new[] { "DOODLESHARP_", "DOTNET_", "COMPlus_" };
            foreach (System.Collections.DictionaryEntry entry in Environment.GetEnvironmentVariables())
            {
                var key = entry.Key?.ToString();
                if (string.IsNullOrEmpty(key)) continue;
                if (!prefixes.Any(p => key.StartsWith(p, StringComparison.OrdinalIgnoreCase))) continue;
                lines.Add($"env.{key} = {entry.Value}");
            }
        }
        catch { }
        return lines;
    }

    // ── small helpers ────────────────────────────────────────────────────────

    private static void Add(List<string> lines, string name, Func<string?> read)
    {
        try
        {
            lines.Add($"{name} = {read() ?? "<null>"}");
        }
        catch (Exception ex)
        {
            lines.Add($"{name} = <unavailable: {ex.GetType().Name}>");
        }
    }

    private static string RuntimeEnvironmentDirectory()
    {
        try { return Path.GetDirectoryName(typeof(object).Assembly.Location) ?? "<unknown>"; }
        catch { return "<unknown>"; }
    }

    private static string SafeProcessStartTime()
    {
        try
        {
            using var process = Process.GetCurrentProcess();
            return process.StartTime.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
        }
        catch { return "<unavailable>"; }
    }

    private static string IsElevated()
    {
        if (!OperatingSystem.IsWindows()) return "n/a";
        try
        {
            using var identity = System.Security.Principal.WindowsIdentity.GetCurrent();
            return new System.Security.Principal.WindowsPrincipal(identity)
                .IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator).ToString();
        }
        catch { return "<unavailable>"; }
    }

    private static string FreeSpaceOn(string path)
    {
        try
        {
            var root = Path.GetPathRoot(Path.GetFullPath(path));
            if (string.IsNullOrEmpty(root)) return "<unknown>";
            var drive = new DriveInfo(root);
            return $"{Bytes(drive.AvailableFreeSpace)} free of {Bytes(drive.TotalSize)}";
        }
        catch { return "<unavailable>"; }
    }

    private static string ThreadPoolDescription()
    {
        try
        {
            ThreadPool.GetAvailableThreads(out var worker, out var io);
            ThreadPool.GetMaxThreads(out var maxWorker, out var maxIo);
            return $"worker {maxWorker - worker}/{maxWorker} io {maxIo - io}/{maxIo} pending={ThreadPool.PendingWorkItemCount} count={ThreadPool.ThreadCount}";
        }
        catch { return "<unavailable>"; }
    }

    private static string Bytes(long value)
    {
        if (value < 0) return value.ToString(CultureInfo.InvariantCulture);
        string[] units = { "B", "KB", "MB", "GB", "TB" };
        double size = value;
        var unit = 0;
        while (size >= 1024 && unit < units.Length - 1) { size /= 1024; unit++; }
        return $"{size.ToString(unit == 0 ? "F0" : "F1", CultureInfo.InvariantCulture)}{units[unit]}";
    }

    private const uint GuiResourcesGdiObjects = 0;
    private const uint GuiResourcesUserObjects = 1;

    [SupportedOSPlatform("windows")]
    private static int GuiResources(Process process, uint flags)
    {
        try { return (int)GetGuiResources(process.Handle, flags); }
        catch { return -1; }
    }

    // DllImport rather than the source-generated LibraryImport: the latter requires
    // <AllowUnsafeBlocks>, which this project deliberately does not enable.
    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint GetGuiResources(IntPtr hProcess, uint uiFlags);
}
