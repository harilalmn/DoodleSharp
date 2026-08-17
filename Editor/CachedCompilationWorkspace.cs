using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace DoodleSharp.Editor;

/// <summary>
/// Maintains a cached CSharpCompilation that supports incremental updates.
/// Instead of creating a fresh compilation on every keystroke, only the changed
/// file's syntax tree is replaced (O(1) via ReplaceSyntaxTree).
/// Thread-safe via lock.
/// </summary>
public class CachedCompilationWorkspace
{
    private readonly object _lock = new();
    private CSharpCompilation _compilation;
    private readonly Dictionary<string, SyntaxTree> _trees = new();

    public CachedCompilationWorkspace(IEnumerable<MetadataReference> references)
    {
        _compilation = CSharpCompilation.Create(
            "CompletionAnalysis",
            Array.Empty<SyntaxTree>(),
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
    }

    /// <summary>
    /// Updates (or adds) a file in the workspace. Only the changed file is reparsed;
    /// the compilation is patched via AddSyntaxTrees/ReplaceSyntaxTree.
    /// </summary>
    public void UpdateFile(string fileId, string content)
    {
        var newTree = CSharpSyntaxTree.ParseText(content, path: fileId);

        lock (_lock)
        {
            if (_trees.TryGetValue(fileId, out var oldTree))
            {
                _compilation = _compilation.ReplaceSyntaxTree(oldTree, newTree);
            }
            else
            {
                _compilation = _compilation.AddSyntaxTrees(newTree);
            }
            _trees[fileId] = newTree;
        }
    }

    /// <summary>
    /// Removes a file from the workspace (e.g., when a file is deleted from the project).
    /// </summary>
    public void RemoveFile(string fileId)
    {
        lock (_lock)
        {
            if (_trees.TryGetValue(fileId, out var tree))
            {
                _compilation = _compilation.RemoveSyntaxTrees(tree);
                _trees.Remove(fileId);
            }
        }
    }

    /// <summary>
    /// File ids currently in the workspace. Used to reconcile against the project's live file list
    /// so files deleted mid-session stop resolving.
    /// </summary>
    public IReadOnlyCollection<string> GetFileIds()
    {
        lock (_lock)
        {
            return _trees.Keys.ToList();
        }
    }

    /// <summary>
    /// Returns the current cached compilation.
    /// </summary>
    public CSharpCompilation GetCompilation()
    {
        lock (_lock)
        {
            return _compilation;
        }
    }

    /// <summary>
    /// Returns the semantic model for a specific file.
    /// </summary>
    public SemanticModel? GetSemanticModel(string fileId)
    {
        lock (_lock)
        {
            if (_trees.TryGetValue(fileId, out var tree))
            {
                return _compilation.GetSemanticModel(tree);
            }
            return null;
        }
    }

    /// <summary>
    /// Returns the syntax tree for a specific file.
    /// </summary>
    public SyntaxTree? GetSyntaxTree(string fileId)
    {
        lock (_lock)
        {
            return _trees.GetValueOrDefault(fileId);
        }
    }

    /// <summary>
    /// Replaces all references (e.g., when NuGet packages change).
    /// Rebuilds the compilation from scratch with existing trees.
    /// </summary>
    public void ReplaceReferences(IEnumerable<MetadataReference> references)
    {
        lock (_lock)
        {
            _compilation = CSharpCompilation.Create(
                "CompletionAnalysis",
                _trees.Values,
                references,
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        }
    }
}
