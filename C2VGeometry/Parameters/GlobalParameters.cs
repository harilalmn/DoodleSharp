using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;

namespace C2VGeometry;

/// <summary>
/// A project-wide registry of named values that survives across code runs and can be edited live from
/// the Global Parameters panel.
///
/// <para>Declare in one place, read anywhere:</para>
/// <code>
/// GlobalParameters.Set&lt;double&gt;("String Length", 10, min: 0, max: 50);
/// GlobalParameters.Set&lt;bool&gt;("String Broken", true);
///
/// double half   = GlobalParameters.Get("String Length") * 0.5;
/// string status = GlobalParameters.Get("String Broken") ? " " : " not ";
/// </code>
///
/// <para>
/// Reactivity is "re-run everything", not a dependency graph: the registry lives in the host and
/// outlives the collectible user assembly, so changing a value simply re-executes <c>Main()</c> and
/// every derived value is recomputed. That is always correct and needs no invalidation logic.
/// </para>
///
/// <para>
/// Only <c>double</c>-family numbers, <c>bool</c>, <c>string</c> and <c>DateTime</c> may be stored.
/// User-defined types are rejected on purpose: holding an instance of a type from the user assembly
/// would pin its collectible <c>AssemblyLoadContext</c> and leak one assembly per run.
/// </para>
/// </summary>
public static class GlobalParameters
{
    private static readonly object Sync = new();
    private static readonly Dictionary<string, Parameter> Params = new(StringComparer.OrdinalIgnoreCase);

    private static int _generation;
    private static int _ordinal;
    private static bool _suppressNotifications;

    /// <summary>Raised when a parameter's <em>value</em> changes. Not raised during a code run.</summary>
    public static event Action<Parameter>? Changed;

    /// <summary>
    /// Raised when the <em>set</em> of parameters changes (declared, removed, or bulk-cleared), i.e.
    /// when the panel must rebuild its rows rather than just refresh them.
    /// </summary>
    public static event Action? Reloaded;

    /// <summary>All parameters in declaration order.</summary>
    public static IReadOnlyList<Parameter> All
    {
        get { lock (Sync) return Params.Values.OrderBy(p => p.Ordinal).ToList(); }
    }

    public static int Count { get { lock (Sync) return Params.Count; } }

    // ────────────────────────────────────────────────────────────────────────
    //  Declaration
    // ────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Declares a parameter and its default. Idempotent: re-running the code re-declares the same
    /// parameter without discarding a value the user has since dialled in from the panel. If the
    /// declared default itself changes (the user edited the literal), the code wins and any override
    /// is dropped — editing the number in the source must visibly do something.
    /// </summary>
    /// <param name="name">Display name and lookup key. Case-insensitive.</param>
    /// <param name="value">The default value.</param>
    /// <param name="min">Slider lower bound (numbers only). Derived from the default when omitted.</param>
    /// <param name="max">Slider upper bound (numbers only). Derived from the default when omitted.</param>
    /// <param name="step">Slider increment (numbers only).</param>
    /// <param name="group">Optional heading the panel groups this parameter under.</param>
    /// <param name="description">Optional tooltip text.</param>
    public static Parameter Set<T>(
        string name,
        T value,
        double? min = null,
        double? max = null,
        double? step = null,
        string? group = null,
        string? description = null,
        [CallerFilePath] string sourceFile = "",
        [CallerLineNumber] int sourceLine = 0)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Global parameter name must not be empty.", nameof(name));

        var (kind, boxed) = Normalize(name, value);

        Parameter param;
        bool isNew;
        lock (Sync)
        {
            if (!Params.TryGetValue(name, out param!))
            {
                param = new Parameter(name, kind, boxed) { Ordinal = _ordinal++ };
                Params[name] = param;
                isNew = true;
            }
            else
            {
                isNew = false;
                bool defaultChanged = !ValuesEqual(param.DefaultValue, boxed) || param.Kind != kind;

                param.Kind = kind;
                param.DefaultValue = boxed;

                // The code's literal changed, or nothing has overridden it — adopt the declared value.
                if (defaultChanged || !param.IsOverridden)
                {
                    param.Value = boxed;
                    param.IsOverridden = false;
                }
            }

            // Metadata always tracks the latest declaration. Min/max are only overwritten when the
            // call actually supplied them, so a range dialled in from the panel survives a re-run.
            if (min.HasValue && !param.RangePinned) param.Min = min;
            if (max.HasValue && !param.RangePinned) param.Max = max;
            if (step.HasValue) param.Step = step;
            if (group != null) param.Group = group;
            if (description != null) param.Description = description;
            param.SourceFile = sourceFile;
            param.SourceLine = sourceLine;
            param.Generation = _generation;
        }

        if (isNew) RaiseReloaded();
        return param;
    }

    // ────────────────────────────────────────────────────────────────────────
    //  Reading
    // ────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Reads a parameter as a self-converting value. See <see cref="ParamValue"/> for the one
    /// operator (<c>+</c>) where the implicit conversions are ambiguous.
    /// </summary>
    public static ParamValue Get(string name)
    {
        lock (Sync)
            return new ParamValue(name, Params.TryGetValue(name, out var p) ? p.Value : null);
    }

    /// <summary>Reads a parameter as a specific type. Always unambiguous; throws if undeclared.</summary>
    public static T Get<T>(string name) => Get(name).As<T>();

    /// <summary>Reads a parameter, returning <paramref name="fallback"/> when it is undeclared.</summary>
    public static T Get<T>(string name, T fallback)
    {
        var v = Get(name);
        return v.Exists ? v.As<T>() : fallback;
    }

    public static bool Has(string name)
    {
        lock (Sync) return Params.ContainsKey(name);
    }

    public static Parameter? Find(string name)
    {
        lock (Sync) return Params.TryGetValue(name, out var p) ? p : null;
    }

    // ────────────────────────────────────────────────────────────────────────
    //  Mutation
    // ────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Imperatively writes a value, marking it as an override so the next <see cref="Set{T}"/> with an
    /// unchanged default leaves it alone. This is what the panel calls on every slider tick.
    /// </summary>
    public static void Assign<T>(string name, T value)
    {
        var (kind, boxed) = Normalize(name, value);

        Parameter param;
        bool created = false;
        lock (Sync)
        {
            if (!Params.TryGetValue(name, out param!))
            {
                param = new Parameter(name, kind, boxed) { Ordinal = _ordinal++, Generation = _generation };
                Params[name] = param;
                created = true;
            }
            else
            {
                if (ValuesEqual(param.Value, boxed) && param.Kind == kind) return;
                param.Kind = kind;
                param.Value = boxed;
                param.IsOverridden = !ValuesEqual(param.DefaultValue, boxed);
            }
        }

        if (created) RaiseReloaded();
        RaiseChanged(param);
    }

    /// <summary>Drops any override and restores the value declared in code.</summary>
    public static void Reset(string name)
    {
        Parameter? param;
        lock (Sync)
        {
            if (!Params.TryGetValue(name, out param)) return;
            if (!param.IsOverridden) return;
            param.Value = param.DefaultValue;
            param.IsOverridden = false;
        }
        RaiseChanged(param);
    }

    /// <summary>Drops every override, restoring all code-declared defaults.</summary>
    public static void ResetAll()
    {
        List<Parameter> touched;
        lock (Sync)
        {
            touched = Params.Values.Where(p => p.IsOverridden).ToList();
            foreach (var p in touched)
            {
                p.Value = p.DefaultValue;
                p.IsOverridden = false;
            }
        }
        foreach (var p in touched) RaiseChanged(p);
    }

    /// <summary>Adjusts a number parameter's slider range. UI metadata only — never written to code.</summary>
    public static void SetRange(string name, double? min, double? max)
    {
        lock (Sync)
        {
            if (!Params.TryGetValue(name, out var p)) return;
            p.Min = min;
            p.Max = max;
            p.RangePinned = true;
        }
    }

    /// <summary>Empties the registry. Called when a different project is opened.</summary>
    public static void ClearAll()
    {
        bool had;
        lock (Sync)
        {
            had = Params.Count > 0;
            Params.Clear();
            _ordinal = 0;
        }
        if (had) RaiseReloaded();
    }

    // ────────────────────────────────────────────────────────────────────────
    //  Run lifecycle
    // ────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Marks the start of a user-code run. Notifications are suppressed for its duration — without
    /// this the <c>Set(...)</c> calls inside <c>Main()</c> would raise <see cref="Changed"/>, which
    /// re-runs <c>Main()</c>, which raises again. Always pair with <see cref="EndRun"/> in a finally.
    /// </summary>
    public static void BeginRun()
    {
        lock (Sync)
        {
            _suppressNotifications = true;
            _generation++;
            _ordinal = 0;
        }
    }

    /// <summary>
    /// Marks the end of a user-code run and re-enables notifications. When
    /// <paramref name="pruneStale"/> is true, parameters that were not re-declared during this run
    /// are removed — that is what makes deleting or renaming a <c>Set(...)</c> line clear the panel
    /// row. Pass false when the run failed, so a compile error does not blank the panel.
    /// </summary>
    public static void EndRun(bool pruneStale)
    {
        lock (Sync)
        {
            _suppressNotifications = false;

            if (pruneStale)
            {
                var stale = Params.Values.Where(p => p.Generation != _generation).Select(p => p.Name).ToList();
                foreach (var name in stale) Params.Remove(name);
            }
        }

        // Announce unconditionally: declarations and removals both happened while suppressed, so the
        // panel has no other signal that the parameter set moved.
        Reloaded?.Invoke();
    }

    // ────────────────────────────────────────────────────────────────────────
    //  Internals
    // ────────────────────────────────────────────────────────────────────────

    private static (ParamKind Kind, object Boxed) Normalize<T>(string name, T value)
    {
        object? v = value;
        return v switch
        {
            double d => (ParamKind.Number, d),
            float f => (ParamKind.Number, (double)f),
            int i => (ParamKind.Number, (double)i),
            long l => (ParamKind.Number, (double)l),
            short s => (ParamKind.Number, (double)s),
            decimal m => (ParamKind.Number, (double)m),
            bool b => (ParamKind.Boolean, b),
            string str => (ParamKind.Text, str),
            DateTime dt => (ParamKind.Date, dt),
            null => throw new ArgumentNullException(nameof(value),
                $"Global parameter '{name}' cannot be null."),
            _ => throw new ArgumentException(
                $"Global parameter '{name}' cannot hold {v.GetType().Name}. Supported types are " +
                "double (and other numerics), bool, string and DateTime. Storing an instance of a " +
                "type declared in your own code would keep its assembly loaded forever.", nameof(value))
        };
    }

    private static bool ValuesEqual(object? a, object? b)
    {
        if (a is double x && b is double y)
            return Math.Abs(x - y) < 1e-12;
        return Equals(a, b);
    }

    private static void RaiseChanged(Parameter p)
    {
        bool suppressed;
        lock (Sync) suppressed = _suppressNotifications;
        if (!suppressed) Changed?.Invoke(p);
    }

    private static void RaiseReloaded()
    {
        bool suppressed;
        lock (Sync) suppressed = _suppressNotifications;
        if (!suppressed) Reloaded?.Invoke();
    }

    /// <summary>Used by <see cref="ParamValue"/> to make "undeclared parameter" errors actionable.</summary>
    internal static string DescribeKnownNames()
    {
        List<string> names;
        lock (Sync) names = Params.Keys.OrderBy(n => n).ToList();
        if (names.Count == 0) return "No global parameters are declared.";
        return "Declared parameters: " + string.Join(", ", names.Select(n => $"'{n}'")) + ".";
    }
}
