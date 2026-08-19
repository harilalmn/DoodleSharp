using System;
using System.Collections.Generic;
using System.Linq;

namespace DoodleSharp.Project;

/// <summary>
/// The names a user's own declaration must not take, because a template imports the namespace that
/// declares them. C# searches the enclosing declaration space before any <c>using</c>, so a
/// namespace, type or variable called <c>Mouse</c> makes <c>DoodleSharp.Animation.Mouse</c>
/// unreachable by its short name for the whole of that scope.
/// </summary>
/// <remarks>
/// This is the single definition of "a DoodleSharp keyword", with two consumers that must not drift
/// apart: <see cref="Templates"/> renames a colliding <em>project</em> name at creation time, and
/// <see cref="DoodleSharp.Execution.ShadowedNameDiagnostics"/> reports a collision the user has
/// already written. The set is reflected rather than hard-coded so it cannot go stale as the API
/// grows.
/// </remarks>
public static class ReservedNames
{
    /// <summary>
    /// The namespaces every generated template imports.
    /// </summary>
    public static readonly IReadOnlyList<string> ImportedNamespaces = new[]
    {
        "System",
        "System.Linq",
        "System.Numerics",
        "System.Collections.Generic",
        "C2VGeometry",
        "DoodleSharp.Animation",
        "DoodleSharp.Console",
        "DoodleSharp.Sketching",
    };

    private static readonly Lazy<HashSet<string>> ApiNames = new(BuildApiNames);

    /// <summary>
    /// True when <paramref name="name"/> is the short name of a type in one of the
    /// <see cref="ImportedNamespaces"/>. Ordinal, because C# is case-sensitive: "mouse" shadows
    /// nothing and is left alone.
    /// </summary>
    public static bool IsApiName(string? name) =>
        !string.IsNullOrEmpty(name) && ApiNames.Value.Contains(name);

    /// <summary>All reserved names, for diagnostics and tests.</summary>
    public static IReadOnlyCollection<string> All => ApiNames.Value;

    private static HashSet<string> BuildApiNames()
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        var imported = new HashSet<string>(ImportedNamespaces, StringComparer.Ordinal);

        // The loaded set, plus the assemblies backing the imported namespaces explicitly: the lazy
        // set is built once, and whichever assemblies happened to be loaded at that moment must not
        // decide whether a name is caught.
        var assemblies = new HashSet<System.Reflection.Assembly>(AppDomain.CurrentDomain.GetAssemblies())
        {
            typeof(object).Assembly,                              // System, System.Collections.Generic
            typeof(Enumerable).Assembly,                          // System.Linq
            typeof(System.Console).Assembly,                      // System (System.Console.dll)
            typeof(System.Numerics.Vector2).Assembly,             // System.Numerics
            typeof(C2VGeometry.Shape).Assembly,                   // C2VGeometry
            typeof(DoodleSharp.Animation.Mouse).Assembly,         // DoodleSharp.Animation/.Console/.Sketching
        };

        foreach (var assembly in assemblies)
        {
            if (assembly.IsDynamic)
                continue;

            Type[] types;
            try
            {
                types = assembly.GetExportedTypes();
            }
            catch
            {
                continue;   // a partially loadable assembly must not break project creation
            }

            foreach (var type in types)
            {
                if (type.Namespace != null && imported.Contains(type.Namespace))
                    names.Add(StripArity(type.Name));
            }
        }

        // Names reachable unqualified through a `using static`, which the reflection above cannot
        // see: it collects type names, and these are members. The compiler injects
        // `global using static C2VGeometry.ViewportRoot;` into every compilation, so `Viewports` is
        // as shadowable as any type name and has to be reserved the same way.
        foreach (var member in typeof(C2VGeometry.ViewportRoot).GetMembers(
                     System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static |
                     System.Reflection.BindingFlags.DeclaredOnly))
        {
            names.Add(member.Name);
        }

        return names;
    }

    private static string StripArity(string typeName)
    {
        var tick = typeName.IndexOf('`');
        return tick < 0 ? typeName : typeName.Substring(0, tick);
    }
}
