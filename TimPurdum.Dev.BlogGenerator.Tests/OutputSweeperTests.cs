using TimPurdum.Dev.BlogGenerator.Compiler;

namespace TimPurdum.Dev.BlogGenerator.Tests;

[TestClass]
public sealed class OutputSweeperTests
{
    private string _root = "";

    [TestInitialize]
    public void CreateTempRoot()
    {
        _root = Path.Combine(Path.GetTempPath(), "bloggen-sweep-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    [TestCleanup]
    public void DeleteTempRoot()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    private string WriteHtml(params string[] segments)
    {
        string path = Path.Combine([_root, .. segments]);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "<html></html>");
        return path;
    }

    [TestMethod]
    public void SweepOrphans_DeletesUnclaimedHtml()
    {
        string orphan = WriteHtml("2026", "8", "15", "old-post.html");

        IReadOnlyList<string> deleted = OutputSweeper.SweepOrphans(_root, []);

        Assert.IsFalse(File.Exists(orphan));
        Assert.AreEqual(1, deleted.Count);
    }

    [TestMethod]
    public void SweepOrphans_KeepsClaimedHtml()
    {
        string kept = WriteHtml("2026", "8", "15", "live-post.html");

        IReadOnlyList<string> deleted = OutputSweeper.SweepOrphans(_root, [kept]);

        Assert.IsTrue(File.Exists(kept));
        Assert.AreEqual(0, deleted.Count);
    }

    [TestMethod]
    public void SweepOrphans_MatchesClaimsGivenAsRelativePaths()
    {
        string kept = WriteHtml("2026", "8", "15", "live-post.html");
        string relative = Path.Combine(_root, ".", "2026", "8", "15", "live-post.html");

        OutputSweeper.SweepOrphans(_root, [relative]);

        Assert.IsTrue(File.Exists(kept), "A claim that normalizes to the same file must not be swept.");
    }

    [TestMethod]
    public void SweepOrphans_LeavesNonHtmlAlone()
    {
        string sidecar = Path.Combine(_root, "notes.txt");
        File.WriteAllText(sidecar, "keep me");

        OutputSweeper.SweepOrphans(_root, []);

        Assert.IsTrue(File.Exists(sidecar));
    }

    [TestMethod]
    public void SweepOrphans_PrunesDirectoriesLeftEmpty()
    {
        WriteHtml("2026", "8", "15", "old-post.html");

        OutputSweeper.SweepOrphans(_root, []);

        Assert.IsFalse(Directory.Exists(Path.Combine(_root, "2026")));
        Assert.IsTrue(Directory.Exists(_root), "The root itself is never removed.");
    }

    [TestMethod]
    public void SweepOrphans_ReturnsEmpty_WhenRootDoesNotExist()
    {
        IReadOnlyList<string> deleted =
            OutputSweeper.SweepOrphans(Path.Combine(_root, "no-such-dir"), []);

        Assert.AreEqual(0, deleted.Count);
    }
}
