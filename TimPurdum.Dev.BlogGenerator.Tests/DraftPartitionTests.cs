using TimPurdum.Dev.BlogGenerator.Compiler;
using TimPurdum.Dev.BlogGenerator.Shared;

namespace TimPurdum.Dev.BlogGenerator.Tests;

/// <summary>
/// Exercises the real parser against real files. <c>MarkupParser</c> reads its paths from the static
/// <c>Generator.BlogSettings</c>, so each test points that at a throwaway directory.
/// </summary>
/// <summary>
/// Not parallelized: every test here points the static <c>Generator.BlogSettings</c> at its own
/// temp directory, and the assembly runs tests at method-level parallelism (see MSTestSettings.cs).
/// <see cref="DoNotParallelizeAttribute"/> only serializes this class's own methods against each
/// other — it does not protect against another test class touching the same static concurrently.
/// That's fine today because no other class reads or writes <c>Generator.BlogSettings</c> (or the
/// <c>Generator.MusicEntries</c>/<c>ShowEntries</c>/<c>GalleryEntries</c> statics); if a future test
/// class does, it needs this same attribute or the race comes back.
/// </summary>
[TestClass]
[DoNotParallelize]
public sealed class DraftPartitionTests
{
    private string _root = "";

    [TestInitialize]
    public void CreateTempSite()
    {
        _root = Path.Combine(Path.GetTempPath(), "bloggen-draft-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(_root, "posts"));
        Directory.CreateDirectory(Path.Combine(_root, "wwwroot"));

        Generator.BlogSettings = new BlogSettings
        {
            PostsContentPath = Path.Combine(_root, "posts"),
            OutputWebRootPath = Path.Combine(_root, "wwwroot")
        };
    }

    [TestCleanup]
    public void DeleteTempSite()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    private void WritePost(string fileName, string extraFrontMatter = "")
    {
        string body = $"""
                       ---
                       layout: post
                       title: A Title
                       {extraFrontMatter}
                       ---

                       Some body text.
                       """;
        File.WriteAllText(Path.Combine(_root, "posts", fileName), body);
    }

    [TestMethod]
    public void GeneratePostMetaDatas_MarksPostWithDraftTrue()
    {
        WritePost("2026-08-15-work-in-progress.md", "draft: true");

        List<PostMetaData> posts = MarkupParser.GeneratePostMetaDatas();

        Assert.AreEqual(1, posts.Count);
        Assert.IsTrue(posts[0].Draft);
    }

    [TestMethod]
    public void GeneratePostMetaDatas_TreatsAbsentKeyAsPublished()
    {
        WritePost("2026-08-15-shipped.md");

        List<PostMetaData> posts = MarkupParser.GeneratePostMetaDatas();

        Assert.AreEqual(1, posts.Count);
        Assert.IsFalse(posts[0].Draft, "An absent draft key must mean published — every existing post relies on this.");
    }

    [TestMethod]
    public void GeneratePostMetaDatas_DoesNotCreateOutputDirectoriesForDrafts()
    {
        WritePost("2026-08-15-work-in-progress.md", "draft: true");

        MarkupParser.GeneratePostMetaDatas();

        Assert.IsFalse(Directory.Exists(Path.Combine(_root, "wwwroot", "post", "2026")),
            "Parsing must not create output directories; the write site does that.");
    }
}
