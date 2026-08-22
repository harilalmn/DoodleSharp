using System;
using System.IO;
using System.Text;

namespace DoodleSharp.Project;

/// <summary>
/// Writes a file so that an interrupted write cannot destroy what was already there.
/// </summary>
/// <remarks>
/// <para>
/// <c>File.WriteAllText</c> truncates the target and then streams into it, so the window between
/// those two steps is a window in which the file on disk is neither the old content nor the new
/// one. Anything that ends the process inside that window — a crash in another thread, a power cut,
/// a full disk, the user killing a hung app — leaves a truncated file, and a truncated file is
/// indistinguishable from a short one.
/// </para>
///
/// <para>
/// That risk is not theoretical here. <b>Auto-save rewrites every one of the user's source files on
/// a timer</b>, so the app spends a fraction of every minute inside that window on files it did not
/// create and cannot reconstruct; the settings and recent-projects files are rewritten on almost
/// every UI interaction. A truncated <c>.cs</c> file is lost work. A truncated
/// <c>appsettings.json</c> is worse than it sounds: the loader catches the parse failure and
/// silently falls back to defaults, and the next save then writes those defaults over the file, so
/// the user's whole configuration disappears without a message.
/// </para>
///
/// <para>
/// Writing to a sibling temporary file and then replacing the target closes the window: the rename
/// is atomic, so a reader sees either the whole old file or the whole new one. Callers keep their
/// existing error handling — this throws exactly what <c>File.WriteAllText</c> would.
/// </para>
/// </remarks>
public static class DurableFile
{
    /// <summary>
    /// Writes <paramref name="contents"/> to <paramref name="path"/> atomically: fully, or not at
    /// all. Creates the file if it does not exist, and creates its directory if it is missing.
    /// </summary>
    /// <param name="encoding">
    /// Defaults to UTF-8 with no byte-order mark, matching <c>File.WriteAllText</c>.
    /// </param>
    public static void WriteAllText(string path, string contents, Encoding? encoding = null)
    {
        if (string.IsNullOrEmpty(path)) throw new ArgumentException("Path is required.", nameof(path));

        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            Directory.CreateDirectory(directory);

        // A sibling, so the replace below stays on one volume — File.Replace and File.Move are only
        // atomic within a volume, and the system temp folder is routinely on a different one.
        var temporary = path + ".tmp-" + Guid.NewGuid().ToString("N").Substring(0, 8);

        try
        {
            if (encoding == null)
                File.WriteAllText(temporary, contents);
            else
                File.WriteAllText(temporary, contents, encoding);

            if (File.Exists(path))
            {
                // Replace rather than Delete-then-Move: it is the atomic form, and it carries the
                // original file's attributes and ACLs across, which a fresh file would not inherit.
                // ignoreMetadataErrors keeps a file on a share, or one whose ACLs cannot be copied,
                // from failing a save that would otherwise have succeeded.
                Retrying(() => File.Replace(temporary, path, destinationBackupFileName: null, ignoreMetadataErrors: true));
            }
            else
            {
                Retrying(() => File.Move(temporary, path));
            }
        }
        catch
        {
            // The original is untouched either way; all that is left is not to litter beside it.
            TryDelete(temporary);
            throw;
        }
    }

    /// <summary>
    /// Runs the final rename, retrying briefly while the destination is held by something else.
    /// </summary>
    /// <remarks>
    /// <para>
    /// On Windows the last step of an atomic write is the one most likely to lose a race it did not
    /// enter. A sync client (OneDrive is the common one — the default projects folder lives under
    /// it), an indexer, or a virus scanner opens the destination for a few hundred milliseconds
    /// after it changes, and <c>File.Replace</c> then fails with ERROR_UNABLE_TO_REMOVE_REPLACED
    /// (0x80070497) or a sharing violation. Nothing is wrong with the write; the file is simply
    /// busy, and it will not be busy shortly.
    /// </para>
    ///
    /// <para>
    /// This cost a crash rather than a failed save: the exception escaped
    /// <c>MainWindow.AutoRunCheck_Changed</c>, which had no handler, reached the WPF dispatcher and
    /// took the process down while the user was only unticking Auto-Run.
    /// </para>
    ///
    /// <para>
    /// Only <see cref="IOException"/> is retried, and only for a fraction of a second. A read-only
    /// or ACL-denied destination raises <see cref="UnauthorizedAccessException"/> and still fails
    /// immediately, which is what the caller wants: no amount of waiting fixes it.
    /// </para>
    /// </remarks>
    /// <summary>
    /// Raised with the attempt number each time a rename fails and is about to be retried. Null in
    /// production.
    /// </summary>
    /// <remarks>
    /// A test cannot prove "the retry is what carried the write" by waiting a fixed time and looking:
    /// the retry budget is finite (~620 ms), a <c>Task.Delay</c> on a loaded machine overshoots it,
    /// and the test then sees a write that has already given up and reports the opposite of what
    /// happened. That is not hypothetical — it failed the 2026.8.15 release build having passed
    /// every local run. With this seam the test releases the file it is holding from inside the
    /// callback, so the first attempt has provably failed before the second one can succeed, and no
    /// wall-clock reading enters the assertion at all.
    /// </remarks>
    internal static Action<int>? RenameRetrying;

    private static void Retrying(Action rename)
    {
        const int attempts = 6;
        for (int attempt = 1; ; attempt++)
        {
            try
            {
                rename();
                return;
            }
            catch (IOException) when (attempt < attempts)
            {
                RenameRetrying?.Invoke(attempt);
                // 20, 40, 80, 160, 320 ms — about half a second in total, well inside the window a
                // sync client holds a file for, and short enough not to stall the UI thread visibly.
                System.Threading.Thread.Sleep(10 * (1 << attempt));
            }
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        catch
        {
            // Best effort: a leftover temporary file is untidy, but reporting it would mask the
            // real failure this is unwinding from.
        }
    }
}
