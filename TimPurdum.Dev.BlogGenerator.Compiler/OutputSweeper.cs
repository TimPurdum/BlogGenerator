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
        // the compiler and can carry redundant "." or ".." segments, which Path.GetFullPath collapses.
        // Every claim must already be rooted -- see the guard below -- because GetFullPath resolves a
        // relative path against the process's current directory, not outputRoot.
        List<string> claimedPathList = claimedPaths.ToList();
        foreach (string claim in claimedPathList)
        {
            if (!Path.IsPathRooted(claim))
            {
                throw new ArgumentException(
                    $"Claimed path '{claim}' is not rooted. All claims must be absolute paths under " +
                    $"'{outputRoot}', or a relative claim can resolve against the wrong directory and " +
                    "the live file it names would be swept as an orphan.",
                    nameof(claimedPaths));
            }
        }

        // Ordinal-ignore-case because on Windows two spellings of the same name (e.g. "Live-Post.html"
        // vs. "live-post.html") are the same file, and a case-sensitive comparison would treat a claimed
        // file as unclaimed and delete a live page. The tradeoff: on a case-sensitive filesystem, an
        // orphan differing from a claim only by case survives the sweep -- the harmless direction.
        HashSet<string> claimed = new(
            claimedPathList.Select(Path.GetFullPath),
            StringComparer.OrdinalIgnoreCase);

        List<string> deleted = [];
        List<string> files = Directory
            .EnumerateFiles(outputRoot, "*.html", SearchOption.AllDirectories)
            .ToList();
        foreach (string file in files)
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
