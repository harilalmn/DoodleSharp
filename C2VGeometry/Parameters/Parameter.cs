using System;
using System.Globalization;

namespace C2VGeometry;

/// <summary>The storage family of a global parameter. Everything numeric collapses to <see cref="Number"/>.</summary>
public enum ParamKind
{
    Number,
    Boolean,
    Text,
    Date
}

/// <summary>
/// One entry in the <see cref="GlobalParameters"/> registry: the current value, the default declared
/// in code, editor metadata for the Global Parameters panel, and the source location of the
/// <c>GlobalParameters.Set(...)</c> call so the panel can write an edited value back into the code.
/// </summary>
public sealed class Parameter
{
    public string Name { get; }
    public ParamKind Kind { get; internal set; }

    /// <summary>The live value. Stored as double / bool / string / DateTime.</summary>
    public object Value { get; internal set; }

    /// <summary>The value the last <c>Set(...)</c> call declared. Restored by <see cref="GlobalParameters.Reset"/>.</summary>
    public object DefaultValue { get; internal set; }

    /// <summary>
    /// True when <see cref="Value"/> was changed from outside the code (the panel, MCP, or
    /// <see cref="GlobalParameters.Assign{T}"/>). A re-declaring <c>Set(...)</c> with an unchanged
    /// default will not clobber an overridden value — otherwise a live slider drag would snap back
    /// to the literal every time the code re-runs.
    /// </summary>
    public bool IsOverridden { get; internal set; }

    // ── Editor metadata (Number only; UI-side, not persisted to code) ──
    public double? Min { get; internal set; }
    public double? Max { get; internal set; }
    public double? Step { get; internal set; }

    /// <summary>
    /// True once the slider range has been retargeted from the panel. A re-declaring <c>Set(...)</c>
    /// then leaves <see cref="Min"/>/<see cref="Max"/> alone, so widening the range to explore a
    /// value is not undone by the next run.
    /// </summary>
    public bool RangePinned { get; internal set; }

    public string? Group { get; internal set; }
    public string? Description { get; internal set; }

    // ── Source location of the declaring Set(...) call, for code write-back ──
    public string SourceFile { get; internal set; } = "";
    public int SourceLine { get; internal set; }

    /// <summary>Run epoch this parameter was last declared in; used to prune deleted declarations.</summary>
    internal int Generation { get; set; }

    /// <summary>Declaration order within a run, so the panel lists parameters as they appear in code.</summary>
    internal int Ordinal { get; set; }

    internal Parameter(string name, ParamKind kind, object value)
    {
        Name = name;
        Kind = kind;
        Value = value;
        DefaultValue = value;
    }

    public double AsDouble => Value is double d ? d : 0;
    public bool AsBool => Value is bool b && b;
    public string AsText => Value as string ?? Value?.ToString() ?? "";
    public DateTime AsDate => Value is DateTime dt ? dt : default;

    /// <summary>
    /// The slider's effective lower bound. Falls back to a range derived from the declared default so
    /// a bare <c>Set("X", 10)</c> still gets a usable slider.
    /// </summary>
    public double EffectiveMin => Min ?? DeriveRange().min;

    /// <summary>The slider's effective upper bound. See <see cref="EffectiveMin"/>.</summary>
    public double EffectiveMax => Max ?? DeriveRange().max;

    private (double min, double max) DeriveRange()
    {
        var v = Value is double d ? d : 0;
        var baseline = Math.Abs(v);
        if (baseline < 1e-9) return (-1, 1);
        return v > 0 ? (0, v * 2) : (v * 2, 0);
    }

    /// <summary>Renders the value the way it should appear as a C# literal in the user's source.</summary>
    public string ToLiteral() => Value switch
    {
        double d => d.ToString("0.############", CultureInfo.InvariantCulture),
        bool b => b ? "true" : "false",
        string s => "\"" + s.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"",
        DateTime dt => $"DateTime.Parse(\"{dt:o}\")",
        _ => Value?.ToString() ?? ""
    };

    public override string ToString() => $"{Name} = {ToLiteral()}";
}
