namespace TimPurdum.Dev.BlogGenerator.Compiler;

/// <summary>
/// Removes generated <c>.html</c> under a content type's output root that no live entry claims.
///
/// Generated output is committed to the consuming repo and served directly, so a file the compiler
/// stops producing stays live forever unless it is actively deleted. One rule covers all three ways
/// that happens: an entry is unpublished, renamed, or its source markdown is deleted.
///
/// Only call this with a root the compiler owns end to end. Page output shares a directory with
/// hand-maintained files such as <c>404.html</c> and must use a targeted delete instead.
/// </summary>
public static class OutputSweeper
{
    /// <summary>
    /// Deletes every <c>.html</c> beneath <paramref name="outputRoot"/> not present in
    /// <paramref name="claimedPaths"/>, then prunes directories the deletions emptied.
    /// Returns the absolute paths deleted, for logging.
    /// </summary>
    public static IReadOnlyList<string> SweepOrphans(string outputRoot, IEnumerable<string> claimedPaths)
    {
        if (!Directory.Exists(outputRoot)) return [];

        // Compare on normalized absolute paths: claims are built by string concatenation elsewhere in
        // the compiler and can carry "." segments or mixed separators.
        HashSet<string> claimed = new(
            claimedPaths.Select(Path.GetFullPath),
            StringComparer.OrdinalIgnoreCase);

        List<string> deleted = [];
        foreach (string file in Directory.EnumerateFiles(outputRoot, "*.html", SearchOption.AllDirectories))
        {
            string full = Path.GetFullPath(file);
            if (claimed.Contains(full)) continue;
            File.Delete(full);
            deleted.Add(full);
        }

        PruneEmptyDirectories(outputRoot);
        return deleted;
    }

    /// <summary>Removes directories left empty by the sweep, deepest first. The root itself is kept.</summary>
    private static void PruneEmptyDirectories(string outputRoot)
    {
        List<string> directories = Directory
            .EnumerateDirectories(outputRoot, "*", SearchOption.AllDirectories)
            .OrderByDescending(static d => d.Length)
            .ToList();

        foreach (string directory in directories)
        {
            if (!Directory.EnumerateFileSystemEntries(directory).Any())
            {
                Directory.Delete(directory);
            }
        }
    }
}
