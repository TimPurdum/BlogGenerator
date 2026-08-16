# Draft and Publish Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Let an author save a post repeatedly without it appearing on the public site, and publish or unpublish it from the admin portal.

**Architecture:** A `draft: true` YAML front-matter key, absent by default, modeled as a nullable `bool?` behind an opt-in `IDraftable` interface on the admin side. The compiler partitions content into published and draft once in `Generator.GenerateSite`, renders only the published half, and sweeps orphaned `.html` from the post output root so unpublishing removes the live file. The admin gets Publish and Unpublish actions in the shared editor chrome, reusing the existing two-phase rename to re-date on publish.

**Tech Stack:** .NET 10, C# latest, Blazor WebAssembly, YamlDotNet 16.3.0, MSTest, GitHub Contents API.

Full design rationale: `docs/superpowers/specs/2026-08-15-draft-publish-design.md`.

## Global Constraints

- Target framework is `net10.0` everywhere. Nullable reference types enabled.
- No new third-party NuGet dependencies. MSTest is the only new package and it is Microsoft's own.
- An absent `draft` key means published. Never write `draft: false` into a file.
- `Draft` on admin front-matter models is `bool?`, never `bool`. The serializer's `OmitNull` is what keeps existing files byte-identical.
- Prose in ReadMe files and commit messages uses US English.
- Version bumps land in the final task, not incrementally: `TimPurdum.Dev.BlogGenerator` 1.1.6 to 1.2.0, `TimPurdum.Dev.BlogGenerator.Admin` 1.4.0-preview to 1.5.0-preview.
- Work happens in the worktree at `D:\worktrees\bg-draft-publish` on branch `feature/draft-publish`.

## File Structure

**Created**

- `TimPurdum.Dev.BlogGenerator.Tests/TimPurdum.Dev.BlogGenerator.Tests.csproj` — MSTest project referencing the Compiler.
- `TimPurdum.Dev.BlogGenerator.Tests/FrontMatterTests.cs` — YAML boolean parsing.
- `TimPurdum.Dev.BlogGenerator.Tests/OutputSweeperTests.cs` — orphan deletion against a temp directory.
- `TimPurdum.Dev.BlogGenerator.Tests/DraftPartitionTests.cs` — `MarkupParser` reads the flag off real files.
- `TimPurdum.Dev.BlogGenerator.Compiler/OutputSweeper.cs` — the one place that deletes generated output.
- `TimPurdum.Dev.BlogGenerator.Admin/ContentTypes/IDraftable.cs` — the opt-in interface.

**Modified**

- `TimPurdum.Dev.BlogGenerator.Compiler/FrontMatter.cs` — add `GetBool`.
- `TimPurdum.Dev.BlogGenerator.Compiler/PostMetaData.cs` — add `Draft` to both records.
- `TimPurdum.Dev.BlogGenerator.Shared/ContentMetaData.cs` — add `Draft` to the three typed records.
- `TimPurdum.Dev.BlogGenerator.Compiler/MarkupParser.cs` — read the flag; stop creating output directories during parsing.
- `TimPurdum.Dev.BlogGenerator.Compiler/Generator.cs` — partition, filter, sweep, create output directories at the write site.
- `TimPurdum.Dev.BlogGenerator.Admin/FrontMatter/PostFrontMatter.cs`, `PageFrontMatter.cs` — implement `IDraftable`.
- `TimPurdum.Dev.BlogGenerator.Admin/ContentTypes/ContentTypeDescriptor.cs` — new content starts as a draft.
- `TimPurdum.Dev.BlogGenerator.Admin/Services/GitHubApiService.cs` — `DeleteFileAsync` returns its commit result.
- `TimPurdum.Dev.BlogGenerator.Admin/Pages/Editor.razor` — badge, Publish/Unpublish actions, deploy-tracking fix.
- `TimPurdum.Dev.BlogGenerator.Admin/Pages/ContentListPage.razor` — status column and filter.
- `TimPurdum.Dev.BlogGenerator.Admin/ReadMe.md`, both `.csproj` version blocks.

---

### Task 1: Test project and YAML boolean parsing

**Files:**
- Create: `TimPurdum.Dev.BlogGenerator.Tests/TimPurdum.Dev.BlogGenerator.Tests.csproj`
- Create: `TimPurdum.Dev.BlogGenerator.Tests/FrontMatterTests.cs`
- Modify: `TimPurdum.Dev.BlogGenerator.Compiler/FrontMatter.cs`

**Interfaces:**
- Consumes: nothing.
- Produces: `bool FrontMatter.GetBool(string key, bool @default = false)`. Used by Task 3.

There is no solution file in this repo, so the test project is referenced and run by path. That is deliberate — the consuming sites each have their own solution and this submodule has never had one.

- [ ] **Step 1: Create the test project**

Scaffold it from the SDK template rather than hand-writing the csproj, so the MSTest package versions match the installed SDK instead of being guessed:

```bash
cd /d/worktrees/bg-draft-publish
dotnet new mstest -n TimPurdum.Dev.BlogGenerator.Tests -o TimPurdum.Dev.BlogGenerator.Tests
rm TimPurdum.Dev.BlogGenerator.Tests/UnitTest1.cs
dotnet add TimPurdum.Dev.BlogGenerator.Tests/TimPurdum.Dev.BlogGenerator.Tests.csproj \
  reference TimPurdum.Dev.BlogGenerator.Compiler/TimPurdum.Dev.BlogGenerator.Compiler.csproj
```

Then confirm the generated csproj has `<TargetFramework>net10.0</TargetFramework>` and `<Nullable>enable</Nullable>`; add or correct them if the template defaulted elsewhere.

Verify the empty project runs before writing any test:

Run: `dotnet test TimPurdum.Dev.BlogGenerator.Tests/TimPurdum.Dev.BlogGenerator.Tests.csproj`
Expected: build succeeds, 0 tests run. If this fails, fix it here — every later step depends on it.

- [ ] **Step 2: Write the failing test**

Create `TimPurdum.Dev.BlogGenerator.Tests/FrontMatterTests.cs`:

```csharp
using TimPurdum.Dev.BlogGenerator.Compiler;

namespace TimPurdum.Dev.BlogGenerator.Tests;

[TestClass]
public sealed class FrontMatterTests
{
    [TestMethod]
    public void GetBool_ReturnsDefault_WhenKeyAbsent()
    {
        FrontMatter front = FrontMatter.Parse("title: Hello");
        Assert.IsFalse(front.GetBool("draft"));
    }

    [TestMethod]
    [DataRow("draft: true")]
    [DataRow("draft: True")]
    [DataRow("draft: yes")]
    [DataRow("draft: y")]
    [DataRow("draft: on")]
    [DataRow("draft: 1")]
    public void GetBool_IsTrue_ForYamlTruthyScalars(string yaml)
    {
        FrontMatter front = FrontMatter.Parse(yaml);
        Assert.IsTrue(front.GetBool("draft"), $"Expected '{yaml}' to read as true.");
    }

    [TestMethod]
    [DataRow("draft: false")]
    [DataRow("draft: no")]
    [DataRow("draft: 0")]
    [DataRow("draft:")]
    public void GetBool_IsFalse_ForYamlFalsyScalars(string yaml)
    {
        FrontMatter front = FrontMatter.Parse(yaml);
        Assert.IsFalse(front.GetBool("draft"), $"Expected '{yaml}' to read as false.");
    }

    [TestMethod]
    public void GetBool_ReturnsDefault_ForUnrecognizedValue()
    {
        FrontMatter front = FrontMatter.Parse("draft: maybe");
        Assert.IsFalse(front.GetBool("draft"));
    }
}
```

The truthy list is wider than `bool.TryParse` on purpose. A hand-written `draft: yes` that silently read as false would publish a draft, and that is the one failure this feature exists to prevent.

- [ ] **Step 3: Run the tests to verify they fail**

Run: `dotnet test TimPurdum.Dev.BlogGenerator.Tests/TimPurdum.Dev.BlogGenerator.Tests.csproj`
Expected: compile error, `'FrontMatter' does not contain a definition for 'GetBool'`.

- [ ] **Step 4: Implement `GetBool`**

In `TimPurdum.Dev.BlogGenerator.Compiler/FrontMatter.cs`, add after `GetDateTime` (which ends at line 39):

```csharp
    /// <summary>
    /// Reads a YAML boolean. Accepts the YAML 1.1 truthy/falsy scalar set (<c>true/yes/y/on/1</c> and
    /// <c>false/no/n/off/0</c>), case-insensitively, because front matter is hand-written as often as
    /// it is generated. An unrecognized value returns <paramref name="default"/> rather than throwing.
    /// </summary>
    public bool GetBool(string key, bool @default = false)
    {
        if (!_data.TryGetValue(key, out object? v) || v is null) return @default;
        if (v is bool b) return b;

        return v.ToString()?.Trim().ToLowerInvariant() switch
        {
            "true" or "yes" or "y" or "on" or "1" => true,
            "false" or "no" or "n" or "off" or "0" => false,
            _ => @default
        };
    }
```

- [ ] **Step 5: Run the tests to verify they pass**

Run: `dotnet test TimPurdum.Dev.BlogGenerator.Tests/TimPurdum.Dev.BlogGenerator.Tests.csproj`
Expected: PASS, 12 tests.

- [ ] **Step 6: Commit**

```bash
git add TimPurdum.Dev.BlogGenerator.Tests TimPurdum.Dev.BlogGenerator.Compiler/FrontMatter.cs
git commit -m "feat(compiler): add FrontMatter.GetBool with an MSTest project"
```

---

### Task 2: Orphaned output sweeper

**Files:**
- Create: `TimPurdum.Dev.BlogGenerator.Compiler/OutputSweeper.cs`
- Create: `TimPurdum.Dev.BlogGenerator.Tests/OutputSweeperTests.cs`

**Interfaces:**
- Consumes: nothing.
- Produces: `IReadOnlyList<string> OutputSweeper.SweepOrphans(string outputRoot, IEnumerable<string> claimedPaths)`, returning the absolute paths it deleted. Used by Task 4.

This is the only code in the feature that deletes files, so it is written test-first and kept as a pure function of a directory and a set of paths.

- [ ] **Step 1: Write the failing tests**

Create `TimPurdum.Dev.BlogGenerator.Tests/OutputSweeperTests.cs`:

```csharp
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
    public void SweepOrphans_MatchesClaimsWithRedundantDotSegment()
    {
        string kept = WriteHtml("2026", "8", "15", "live-post.html");
        string claimWithDotSegment = Path.Combine(_root, ".", "2026", "8", "15", "live-post.html");

        OutputSweeper.SweepOrphans(_root, [claimWithDotSegment]);

        Assert.IsTrue(File.Exists(kept), "A claim that normalizes to the same file must not be swept.");
    }

    [TestMethod]
    public void SweepOrphans_ThrowsOnNonRootedClaim()
    {
        string kept = WriteHtml("2026", "8", "15", "live-post.html");
        string nonRootedClaim = Path.Combine("2026", "8", "15", "live-post.html");

        Assert.ThrowsExactly<ArgumentException>(() => OutputSweeper.SweepOrphans(_root, [nonRootedClaim]));
        Assert.IsTrue(File.Exists(kept), "A malformed claim must not cause the file it names to be deleted.");
    }

    [TestMethod]
    public void SweepOrphans_KeepsClaimedAndDeletesUnclaimed_InSameDirectory()
    {
        string kept = WriteHtml("2026", "8", "15", "live-post.html");
        string orphan = WriteHtml("2026", "8", "15", "unpublished-post.html");

        IReadOnlyList<string> deleted = OutputSweeper.SweepOrphans(_root, [kept]);

        Assert.IsTrue(File.Exists(kept));
        Assert.IsFalse(File.Exists(orphan));
        Assert.AreEqual(1, deleted.Count);
        Assert.IsTrue(
            Directory.Exists(Path.Combine(_root, "2026", "8", "15")),
            "A directory still holding a claimed file must not be pruned.");
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
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test TimPurdum.Dev.BlogGenerator.Tests/TimPurdum.Dev.BlogGenerator.Tests.csproj`
Expected: compile error, `The name 'OutputSweeper' does not exist`.

- [ ] **Step 3: Implement the sweeper**

Create `TimPurdum.Dev.BlogGenerator.Compiler/OutputSweeper.cs`:

```csharp
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
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test TimPurdum.Dev.BlogGenerator.Tests/TimPurdum.Dev.BlogGenerator.Tests.csproj`
Expected: PASS, 20 tests total.

- [ ] **Step 5: Commit**

```bash
git add TimPurdum.Dev.BlogGenerator.Compiler/OutputSweeper.cs TimPurdum.Dev.BlogGenerator.Tests/OutputSweeperTests.cs
git commit -m "feat(compiler): sweep orphaned generated HTML from an output root"
```

---

### Task 3: Carry the draft flag through parsing

**Files:**
- Modify: `TimPurdum.Dev.BlogGenerator.Compiler/PostMetaData.cs`
- Modify: `TimPurdum.Dev.BlogGenerator.Shared/ContentMetaData.cs`
- Modify: `TimPurdum.Dev.BlogGenerator.Compiler/MarkupParser.cs`
- Create: `TimPurdum.Dev.BlogGenerator.Tests/DraftPartitionTests.cs`

**Interfaces:**
- Consumes: `FrontMatter.GetBool` from Task 1.
- Produces: `bool Draft` as the final positional parameter on `PostMetaData`, and `bool Draft = false` as a trailing optional parameter on `PageMetaData`, `MusicMetaData`, `ShowMetaData`, and `GalleryMetaData`. Used by Task 4.

`PostMetaData` has no optional parameters, so `Draft` is appended as required and every construction site must pass it. The other four records already end in optional parameters, so `Draft` is appended with a `false` default and existing call sites keep compiling.

- [ ] **Step 1: Write the failing test**

Create `TimPurdum.Dev.BlogGenerator.Tests/DraftPartitionTests.cs`:

```csharp
using TimPurdum.Dev.BlogGenerator.Compiler;
using TimPurdum.Dev.BlogGenerator.Shared;

namespace TimPurdum.Dev.BlogGenerator.Tests;

/// <summary>
/// Exercises the real parser against real files. <c>MarkupParser</c> reads its paths from the static
/// <c>Generator.BlogSettings</c>, so each test points that at a throwaway directory.
/// </summary>
[TestClass]
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
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test TimPurdum.Dev.BlogGenerator.Tests/TimPurdum.Dev.BlogGenerator.Tests.csproj`
Expected: compile error, `'PostMetaData' does not contain a definition for 'Draft'`.

- [ ] **Step 3: Add `Draft` to the metadata records**

In `TimPurdum.Dev.BlogGenerator.Compiler/PostMetaData.cs`, change the `PostMetaData` record (line 1) to append `Draft`, and add a defaulted `Draft` to `PageMetaData`:

```csharp
public record PostMetaData(string Title, string SubTitle, string Url, DateTime PublishedDate,
    string Author, string Content, Dictionary<string, string> RazorComponents,
    List<string> ScriptTags, string Layout, string Description, string OutputPath, bool Update,
    /// <summary>True when frontmatter carries <c>draft: true</c>. Draft entries render nowhere:
    /// no HTML file, no nav link, no feed item, no sitemap entry.</summary>
    bool Draft);
```

For `PageMetaData`, append after the existing `LastModified` parameter:

```csharp
    DateTime? LastModified = null,
    /// <summary>True when frontmatter carries <c>draft: true</c>. See <see cref="PostMetaData.Draft"/>.</summary>
    bool Draft = false);
```

In `TimPurdum.Dev.BlogGenerator.Shared/ContentMetaData.cs`, append the same defaulted parameter to `MusicMetaData`, `ShowMetaData`, and `GalleryMetaData`:

```csharp
    /// <summary>True when frontmatter carries <c>draft: true</c>. Draft entries render nowhere.</summary>
    bool Draft = false);
```

- [ ] **Step 4: Read the flag in `MarkupParser`**

Three edits in `TimPurdum.Dev.BlogGenerator.Compiler/MarkupParser.cs`.

First, in `GeneratePostMetaData`, alongside the other front-matter reads (near the `lastmodified` read at line 54), add:

```csharp
                bool draft = frontMatter.GetBool("draft");
```

and pass `draft` as the final argument to the `PostMetaData` construction at line 106.

Second, in the `ParsedEntry` record (line 287) append `bool Draft` as the final positional parameter, read it in `ParseEntryFile` next to the `lastmodified` merge (line 343):

```csharp
            bool draft = frontMatter.GetBool("draft");
```

and pass it as the final argument to the `ParsedEntry` construction at line 362. Then forward `parsed.Draft` into the `MusicMetaData`, `ShowMetaData`, and `GalleryMetaData` constructions (lines 167, 220, 278) by adding `Draft: parsed.Draft` as a named argument.

Third, in `GeneratePageMetaDataFromMarkdown`, read `frontMatter.GetBool("draft")` the same way and pass `Draft:` to the `PageMetaData` construction. Add `"draft"` to the `StandardPageFrontMatterKeys` set (line 479) so it is not also forwarded to layouts as an extra parameter.

- [ ] **Step 5: Stop creating output directories during parsing**

Delete the `Directory.CreateDirectory(outputFolder);` call at `MarkupParser.cs:66` and the `Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);` call at line 350. Both run before anything knows whether the entry will be written, so a draft would leave empty dated directories behind. Task 4 creates the directory at each write site instead.

- [ ] **Step 6: Run the tests to verify they pass**

Run: `dotnet test TimPurdum.Dev.BlogGenerator.Tests/TimPurdum.Dev.BlogGenerator.Tests.csproj`
Expected: PASS, 21 tests total.

- [ ] **Step 7: Commit**

```bash
git add TimPurdum.Dev.BlogGenerator.Compiler TimPurdum.Dev.BlogGenerator.Shared TimPurdum.Dev.BlogGenerator.Tests
git commit -m "feat(compiler): read draft frontmatter into every content metadata record"
```

---

### Task 4: Filter drafts out of the generated site

**Files:**
- Modify: `TimPurdum.Dev.BlogGenerator.Compiler/Generator.cs:23-102`

**Interfaces:**
- Consumes: `PostMetaData.Draft` and friends from Task 3, `OutputSweeper.SweepOrphans` from Task 2.
- Produces: nothing consumed by later tasks.

The single filter point. Everything downstream of the partition already treats its input list as the whole world, so nav links, the index page, `feed.xml`, and `sitemap.xml` all follow from it.

- [ ] **Step 1: Partition posts and pages**

Replace `Generator.cs:30`:

```csharp
        List<PostMetaData> posts = MarkupParser.GeneratePostMetaDatas();
```

with:

```csharp
        // One partition, applied before anything reads the list. navLinks, the render loops, the RSS
        // feed and the sitemap all consume `posts`, so filtering here covers every surface at once.
        List<PostMetaData> allPosts = MarkupParser.GeneratePostMetaDatas();
        List<PostMetaData> posts = allPosts.Where(static p => !p.Draft).ToList();
```

Replace line 42:

```csharp
        List<PageMetaData> pages = await MarkupParser.GeneratePageMetaDatas(navLinks);
```

with:

```csharp
        List<PageMetaData> allPages = await MarkupParser.GeneratePageMetaDatas(navLinks);
        List<PageMetaData> pages = allPages.Where(static p => !p.Draft).ToList();
```

Also filter the three typed collections at lines 31 to 33, so a site that adopts drafts for them behaves consistently:

```csharp
        MusicEntries = MarkupParser.GenerateMusicMetaDatas().Where(static m => !m.Draft).ToList();
        ShowEntries = MarkupParser.GenerateShowMetaDatas().Where(static s => !s.Draft).ToList();
        GalleryEntries = MarkupParser.GenerateGalleryMetaDatas().Where(static g => !g.Draft).ToList();
```

- [ ] **Step 2: Create output directories at the write sites**

Task 3 removed directory creation from the parser. Add it immediately before each of the five `File.WriteAllTextAsync(...OutputPath, html)` calls (the post loop at line 64, and the music, show, and gallery loops at lines 72, 80, 88):

```csharp
            Directory.CreateDirectory(Path.GetDirectoryName(post.OutputPath)!);
            await File.WriteAllTextAsync(post.OutputPath, html);
```

Use the matching loop variable in each of the other three. The page loop already builds its path under `OutputWebRootPath`, which always exists, so it needs no change.

- [ ] **Step 3: Extract the page output filename**

The page write loop computes its filename inline at lines 51 to 54. Both that loop and the draft-page delete below need it, so lift it to a private static method at the bottom of the class:

```csharp
    /// <summary>Maps a page's URL to its output filename. An empty or root URL is the site index.</summary>
    private static string PageOutputPath(PageMetaData page)
    {
        string fileName = Path.GetFileNameWithoutExtension(page.Url);
        if (string.IsNullOrWhiteSpace(fileName) || fileName == Path.DirectorySeparatorChar.ToString())
        {
            fileName = "index";
        }
        return Path.Combine(BlogSettings!.OutputWebRootPath, $"{fileName}.html");
    }
```

Replace lines 51 to 54 in the page loop with `string filePath = PageOutputPath(page);`.

- [ ] **Step 4: Delete output for unpublished content**

Add after the gallery render loop (after line 90), before the RSS feed is generated:

```csharp
        // Unpublishing has to DELETE generated output, not merely skip it — the .html files are
        // committed to the consuming repo and served directly, so a skipped file stays live.
        //
        // This runs unconditionally, outside the `if (!entry.Update) continue` gates above. Update is
        // computed from source mtime against output mtime, and a fresh CI checkout stamps every file
        // with checkout time — deletion inside that gate would work locally and silently no-op in CI.
        foreach (string removed in OutputSweeper.SweepOrphans(
                     Path.Combine(BlogSettings.OutputWebRootPath, "post"),
                     posts.Select(static p => p.OutputPath)))
        {
            Console.WriteLine($"Removed unpublished or orphaned output: {removed}");
        }

        // Pages share OutputWebRootPath with hand-maintained files such as 404.html, so they get a
        // targeted delete by expected path rather than a sweep.
        foreach (PageMetaData draftPage in allPages.Where(static p => p.Draft))
        {
            string draftPagePath = PageOutputPath(draftPage);
            if (File.Exists(draftPagePath))
            {
                File.Delete(draftPagePath);
                Console.WriteLine($"Removed unpublished page output: {draftPagePath}");
            }
        }
```

Do not sweep the `music`, `show`, or `gallery` roots yet. No shipped front-matter model marks those as drafts, and pointing a delete sweep at a root nothing populates is a risk with no payoff. Add it in the same shape when a site adopts drafts there.

- [ ] **Step 5: Verify the build and full test run**

Run: `dotnet build TimPurdum.Dev.BlogGenerator.Compiler/TimPurdum.Dev.BlogGenerator.Compiler.csproj`
Expected: build succeeded, 0 errors.

Run: `dotnet test TimPurdum.Dev.BlogGenerator.Tests/TimPurdum.Dev.BlogGenerator.Tests.csproj`
Expected: PASS, 21 tests.

- [ ] **Step 6: Commit**

```bash
git add TimPurdum.Dev.BlogGenerator.Compiler/Generator.cs
git commit -m "feat(compiler): render only published content and delete unpublished output"
```

---

### Task 5: The `IDraftable` opt-in and draft-by-default

**Files:**
- Create: `TimPurdum.Dev.BlogGenerator.Admin/ContentTypes/IDraftable.cs`
- Modify: `TimPurdum.Dev.BlogGenerator.Admin/FrontMatter/PostFrontMatter.cs`
- Modify: `TimPurdum.Dev.BlogGenerator.Admin/FrontMatter/PageFrontMatter.cs`
- Modify: `TimPurdum.Dev.BlogGenerator.Admin/ContentTypes/ContentTypeDescriptor.cs`
- Modify: `TimPurdum.Dev.BlogGenerator.Admin/ContentTypes/IContentTypeDescriptor.cs`
- Modify: `TimPurdum.Dev.BlogGenerator.Admin/Pages/Editor.razor`

**Interfaces:**
- Consumes: nothing.
- Produces: `interface IDraftable { bool? Draft { get; set; } }` in namespace `TimPurdum.Dev.BlogGenerator.Admin.ContentTypes`. Used by Tasks 8 and 9. Also produces `IContentTypeDescriptor.CreateFrontMatterForNewEntry()`, a breaking addition to the public interface.

- [ ] **Step 1: Create the interface**

Create `TimPurdum.Dev.BlogGenerator.Admin/ContentTypes/IDraftable.cs`:

```csharp
namespace TimPurdum.Dev.BlogGenerator.Admin.ContentTypes;

/// <summary>
/// Implemented by front-matter types that support draft and publish. New entries of an implementing
/// type start as drafts and render nowhere on the public site until published.
///
/// The property is nullable on purpose. <see cref="Services.MarkdownDocument"/> serializes with
/// <c>OmitNull</c>, so a published entry carries no <c>draft</c> key at all and existing files keep
/// their exact on-disk bytes. A plain <c>bool</c> would write <c>draft: false</c> into every file the
/// editor touched.
///
/// The interface exists rather than a loose YAML convention because <c>MarkdownDocument.Build</c>
/// serializes only the typed model — a key absent from a content type's class is erased on save. A
/// type that carried <c>draft</c> without modeling it would publish itself the first time it was saved.
/// </summary>
public interface IDraftable
{
    /// <summary>True when this entry is a draft. Null (the serialized absence of the key) means published.</summary>
    bool? Draft { get; set; }
}
```

- [ ] **Step 2: Implement it on the two shipped models**

In `PostFrontMatter.cs`, change the class declaration and add the property before `Lastmodified` (machine-managed fields stay last, per the type's own doc comment):

```csharp
public sealed class PostFrontMatter : IHasLastmodified, IDraftable
{
    [YamlMember(Alias = "layout")] public string Layout { get; set; } = "post";
    [YamlMember(Alias = "title")] public string Title { get; set; } = "";
    [YamlMember(Alias = "subtitle")] public string? Subtitle { get; set; }
    [YamlMember(Alias = "description")] public string? Description { get; set; }
    [YamlMember(Alias = "author")] public string? Author { get; set; }
    [YamlMember(Alias = "draft")] public bool? Draft { get; set; }
    [YamlMember(Alias = "lastmodified")] public string? Lastmodified { get; set; }
}
```

Make the same two changes in `PageFrontMatter.cs`: add `IDraftable` to the base list and add the `draft` property immediately before `Lastmodified`.

- [ ] **Step 3: Default new content to draft via a separate allocator**

`ContentTypeDescriptor<TFront>.CreateFrontMatter()` stays exactly as it is — `public object CreateFrontMatter() => new TFront();` — because `Editor.razor`'s `OnParametersSetAsync` calls it unconditionally near the top of the method, on both the new-entry and edit-existing-entry routes, as the reset that guarantees a valid typed instance even if a load fails. Stamping `Draft = true` inside `CreateFrontMatter()` would make that shared call flag published entries as drafts for the moment between allocation and the edit route's `ParseDocument` replacing it — no observable bug today, but a hazard the next reader could easily reintroduce.

Instead, add a second allocator to `ContentTypeDescriptor.cs`:

```csharp
    /// <summary>
    /// Allocate front matter for a NEW entry. Identical to <see cref="CreateFrontMatter"/> except that
    /// types implementing <see cref="IDraftable"/> start as drafts, so nothing reaches the public site
    /// before its author publishes it.
    ///
    /// Kept separate from <see cref="CreateFrontMatter"/> because the editor allocates front matter on
    /// every load, including when opening an existing entry — defaulting to draft in the shared
    /// allocator would flag published entries as drafts while their file loads.
    /// </summary>
    public object CreateFrontMatterForNewEntry()
    {
        TFront front = new();
        if (front is IDraftable draftable)
        {
            draftable.Draft = true;
        }
        return front;
    }
```

In `Editor.razor`'s `OnParametersSetAsync`, inside the new-entry branch (`if (string.IsNullOrEmpty(File))`), call the new allocator before `_loading = false;`:

```csharp
        if (string.IsNullOrEmpty(File))
        {
            _frontmatter = _descriptor.CreateFrontMatterForNewEntry();
            _loading = false;
            return;
        }
```

- [ ] **Step 4: Add the new allocator to the interface**

Add the matching member to `TimPurdum.Dev.BlogGenerator.Admin/ContentTypes/IContentTypeDescriptor.cs`, alongside the unchanged `CreateFrontMatter()`:

```csharp
    /// <summary>Allocates a default-initialized front-matter instance.</summary>
    object CreateFrontMatter();

    /// <summary>
    /// Allocate front matter for a NEW entry. Identical to <see cref="CreateFrontMatter"/> except that
    /// types implementing <see cref="IDraftable"/> start as drafts, so nothing reaches the public site
    /// before its author publishes it.
    ///
    /// Kept separate from <see cref="CreateFrontMatter"/> because the editor allocates front matter on
    /// every load, including when opening an existing entry — defaulting to draft in the shared
    /// allocator would flag published entries as drafts while their file loads.
    /// </summary>
    object CreateFrontMatterForNewEntry();
```

Note: this adds a public member to `IContentTypeDescriptor`, which is a breaking change for any consuming site that implements the interface directly rather than going through `AddContentType<TFront, TForm>`. Record this in the package's upgrade notes.

- [ ] **Step 5: Verify the build**

Run: `dotnet build TimPurdum.Dev.BlogGenerator.Admin/TimPurdum.Dev.BlogGenerator.Admin.csproj`
Expected: build succeeded, 0 errors.

- [ ] **Step 6: Commit**

```bash
git add TimPurdum.Dev.BlogGenerator.Admin/ContentTypes TimPurdum.Dev.BlogGenerator.Admin/FrontMatter
git commit -m "feat(admin): add IDraftable and start new content as a draft"
```

---

### Task 6: Track the correct commit across a two-commit rename

**Files:**
- Modify: `TimPurdum.Dev.BlogGenerator.Admin/Services/GitHubApiService.cs:99-110`
- Modify: `TimPurdum.Dev.BlogGenerator.Admin/Pages/Editor.razor:344-392`

**Interfaces:**
- Consumes: nothing.
- Produces: `Task<RepoCommitResult> DeleteFileAsync(string path, string sha, string commitMessage, CancellationToken ct = default)` — was `Task`. Used by Task 8.

A rename is two commits, and the deploy banner currently tracks the first one, whose workflow run the second supersedes and cancels. Rename is rare today; publish makes it routine, so this is fixed before the publish action is built on top of it.

- [ ] **Step 1: Return the delete's commit result**

Replace `DeleteFileAsync` in `GitHubApiService.cs` (lines 99 to 110) with:

```csharp
    /// <summary>
    /// Delete a file at <paramref name="path"/>. <paramref name="sha"/> is required. Returns the
    /// resulting commit so callers doing a two-phase rename can track the LAST commit — the workflow
    /// run for an earlier commit in the same rename gets superseded and canceled.
    /// </summary>
    public async Task<RepoCommitResult> DeleteFileAsync(string path, string sha, string commitMessage,
        CancellationToken ct = default)
    {
        DeleteBody body = new(commitMessage, sha);
        using HttpRequestMessage req = new(HttpMethod.Delete, ContentsUrl(path))
        {
            Content = JsonContent.Create(body, options: JsonOptions)
        };
        ApplyAuth(req);
        HttpResponseMessage res = await http.SendAsync(req, ct);
        res.EnsureSuccessStatusCode();
        // A delete response carries a null `content` and a populated `commit`; PutResponse models both.
        PutResponse? wrapped = await res.Content.ReadFromJsonAsync<PutResponse>(JsonOptions, ct);
        return new RepoCommitResult(path, string.Empty, wrapped?.Commit?.Sha ?? string.Empty);
    }
```

The other caller, `ContentListPage.ConfirmDeleteAsync`, awaits without using the result and keeps compiling unchanged.

- [ ] **Step 2: Track the delete commit on the rename path**

In `Editor.razor`, inside `SaveAsync`, the rename branch currently assigns `result` from the PUT and then awaits the DELETE, discarding it. Change the branch (lines 367 to 381) so its final two lines read:

```csharp
                RepoCommitResult deleteResult = await Api.DeleteFileAsync(oldPath, _existingSha!, deleteMessage);
                // Track the delete, not the PUT: it is the later commit, and its workflow run is the
                // one that actually finishes. Fall back to the PUT if GitHub returned no commit sha.
                if (!string.IsNullOrEmpty(deleteResult.CommitSha))
                {
                    result = result with { CommitSha = deleteResult.CommitSha };
                }
```

- [ ] **Step 3: Verify the build**

Run: `dotnet build TimPurdum.Dev.BlogGenerator.Admin/TimPurdum.Dev.BlogGenerator.Admin.csproj`
Expected: build succeeded, 0 errors.

- [ ] **Step 4: Commit**

```bash
git add TimPurdum.Dev.BlogGenerator.Admin/Services/GitHubApiService.cs TimPurdum.Dev.BlogGenerator.Admin/Pages/Editor.razor
git commit -m "fix(admin): track the final commit of a two-phase rename in the deploy banner"
```

---

### Task 7: Publish and unpublish in the editor

**Files:**
- Modify: `TimPurdum.Dev.BlogGenerator.Admin/Pages/Editor.razor`

**Interfaces:**
- Consumes: `IDraftable` from Task 5, `DeleteFileAsync` returning `RepoCommitResult` from Task 6.
- Produces: nothing consumed by later tasks.

Publish needs no new file-writing path. Assigning today's date to the bound date field makes `BuildNewFileName()` produce a different name, which flips `WillRename` on its own, and the existing two-phase rename does the work.

- [ ] **Step 1: Add the state helpers**

In the `@code` block, after the `WillRename` property (which ends at line 152), add:

```csharp
    /// <summary>True when this content type opted into drafts by implementing IDraftable.</summary>
    private bool SupportsDrafts => _frontmatter is IDraftable;

    private bool IsDraft => _frontmatter is IDraftable { Draft: true };

    /// <summary>Why a save is happening. Only affects commit messages — the write path is identical.</summary>
    private enum SaveIntent { Save, Publish, Unpublish }
```

- [ ] **Step 2: Add the publish and unpublish actions**

After `SaveAsync`, add:

```csharp
    private async Task PublishAsync()
    {
        if (_frontmatter is not IDraftable draftable || _descriptor is null) return;

        // Null, not false: the serializer omits null, so a published entry carries no draft key.
        draftable.Draft = null;

        // Re-date dated types to today. A draft's filename date is a scratch value; the permanent date
        // and URL are fixed at publish time, so a post that sat for three weeks sorts to the top of the
        // archive instead of being buried. A Plain type (a page) has no date in its filename and its
        // filename IS its URL, so renaming one would break its route.
        switch (_descriptor.NamePattern)
        {
            case ContentNamePattern.Dated:
                _dateFull = DateTime.Today.ToString("yyyy-MM-dd");
                break;
            case ContentNamePattern.YearMonth:
                _dateMonth = DateTime.Today.ToString("yyyy-MM");
                break;
        }

        await SaveAsync(SaveIntent.Publish);
    }

    private async Task UnpublishAsync()
    {
        if (_frontmatter is not IDraftable draftable) return;
        // Leave the filename alone. A later re-publish picks up that day's date.
        draftable.Draft = true;
        await SaveAsync(SaveIntent.Unpublish);
    }
```

- [ ] **Step 3: Give `SaveAsync` an intent**

Change the signature at line 344 and add a no-argument overload above it so the form's `@onsubmit="SaveAsync"` binding keeps working unchanged:

```csharp
    private Task SaveAsync() => SaveAsync(SaveIntent.Save);

    private async Task SaveAsync(SaveIntent intent)
```

Inside, replace the `viewLive` assignment at line 360 with a draft guard:

```csharp
            // A draft has no live URL. BuildViewLiveUrl is static and cannot see front-matter state,
            // so the guard lives here rather than inside it.
            string? viewLive = IsDraft ? null : BuildViewLiveUrl(_descriptor, newFileName);
```

Then replace the three commit-message strings so publish and unpublish read correctly in the repo history. In the `isNew` branch (line 364):

```csharp
                string createVerb = intent == SaveIntent.Publish ? "publish" : "create";
                string commitMessage = $"admin: {createVerb} {_descriptor.Slug} entry {StripExtension(newFileName)}";
```

In the rename branch (lines 376 and 379):

```csharp
                string putVerb = intent == SaveIntent.Publish ? "publish" : "rename";
                string putMessage = $"admin: {putVerb} {_descriptor.Slug} entry to {slugForCommit}";
```

```csharp
                string deleteReason = intent == SaveIntent.Publish ? "published as" : "renamed to";
                string deleteMessage = $"admin: delete {_descriptor.Slug} entry {StripExtension(OriginalFileName)} ({deleteReason} {slugForCommit})";
```

In the final `else` branch (line 384):

```csharp
                string updateVerb = intent switch
                {
                    SaveIntent.Publish => "publish",
                    SaveIntent.Unpublish => "unpublish",
                    _ => "update"
                };
                string commitMessage = $"admin: {updateVerb} {_descriptor.Slug} entry {StripExtension(newFileName)}";
```

- [ ] **Step 4: Add the badge and the buttons**

In the header block, after the `Editing <code>` paragraph (line 23), add:

```razor
    @if (SupportsDrafts && !_loading && _descriptor is not null)
    {
        <p class="page-heading__lede">
            <span class="status-pill @(IsDraft ? "status-pill--draft" : "status-pill--published")">
                @(IsDraft ? "Draft" : "Published")
            </span>
            @if (IsDraft)
            {
                <span> — not on the public site until you publish.</span>
            }
        </p>
    }
```

In the `.editor-actions` div (lines 105 to 108), add the action button between Save and Cancel:

```razor
        <div class="editor-actions">
            <button class="admin-button" type="submit" disabled="@_saving">@(_saving ? "Saving…" : "Save")</button>
            @if (SupportsDrafts)
            {
                @if (IsDraft)
                {
                    <button class="admin-button" type="button" disabled="@_saving" @onclick="PublishAsync">Publish</button>
                }
                else
                {
                    <button class="admin-button admin-button--ghost" type="button" disabled="@_saving" @onclick="UnpublishAsync">Unpublish</button>
                }
            }
            <a class="admin-button admin-button--ghost" href="/admin/@_descriptor.Slug">Cancel</a>
        </div>
```

Publish on a dated type renames the file, so tell the user before they click. Update the rename hint at lines 78 to 81, whose current wording about old output is now wrong — the compiler prunes it:

```razor
                <p class="form-row__hint" style="margin-top: 0.5rem;">
                    Saving will rename: <code class="path">@OriginalFileName</code> &rarr; <code class="path">@BuildNewFileName()</code>.
                    There is no automatic redirect from the old URL. Output generated under the old name is
                    removed on the next build.
                </p>
```

- [ ] **Step 5: Style the badge**

Append to `TimPurdum.Dev.BlogGenerator.Admin/wwwroot/css/admin.css`, using the existing custom properties so a consuming site's palette override applies:

```css
.status-pill {
    display: inline-block;
    padding: 0.15rem 0.55rem;
    border-radius: 999px;
    font-size: 0.8rem;
    font-weight: 600;
    border: 1px solid currentColor;
}

.status-pill--draft {
    color: var(--admin-accent);
}

.status-pill--published {
    color: var(--admin-text);
    opacity: 0.75;
}
```

- [ ] **Step 6: Verify the build**

Run: `dotnet build TimPurdum.Dev.BlogGenerator.Admin/TimPurdum.Dev.BlogGenerator.Admin.csproj`
Expected: build succeeded, 0 errors.

- [ ] **Step 7: Commit**

```bash
git add TimPurdum.Dev.BlogGenerator.Admin/Pages/Editor.razor TimPurdum.Dev.BlogGenerator.Admin/wwwroot/css/admin.css
git commit -m "feat(admin): publish and unpublish actions in the editor"
```

---

### Task 8: Draft status on the content list

**Files:**
- Modify: `TimPurdum.Dev.BlogGenerator.Admin/Pages/ContentListPage.razor`

**Interfaces:**
- Consumes: `IDraftable` from Task 5.
- Produces: nothing consumed by later tasks.

GitHub's directory listing carries no front matter, so status requires reading each file. For a 20 to 50 entry archive against a 5,000 request hourly limit this is fine; the GraphQL batching escape hatch is documented in the spec and deliberately not built.

- [ ] **Step 1: Add the status state**

In the `@code` block, after `_busy` (line 104), add:

```csharp
    /// <summary>Draft status per filename. A missing entry means "not fetched yet or unreadable".</summary>
    private readonly Dictionary<string, bool> _draftByFile = new(StringComparer.OrdinalIgnoreCase);

    private StatusFilter _filter = StatusFilter.All;

    private enum StatusFilter { All, Drafts, Published }

    private bool SupportsDrafts =>
        _descriptor is not null && typeof(IDraftable).IsAssignableFrom(_descriptor.FrontMatterType);

    private List<Row> VisibleRows => _filter switch
    {
        StatusFilter.Drafts    => _rows.Where(r => _draftByFile.TryGetValue(r.FileName, out bool d) && d).ToList(),
        StatusFilter.Published => _rows.Where(r => _draftByFile.TryGetValue(r.FileName, out bool d) && !d).ToList(),
        _                      => _rows
    };
```

- [ ] **Step 2: Fetch statuses after the listing loads**

At the end of the `try` block in `LoadAsync`, after `_rows` is assigned (line 141), add:

```csharp
            if (SupportsDrafts)
            {
                await LoadDraftStatusesAsync();
            }
```

Then add the method after `LoadAsync`:

```csharp
    /// <summary>
    /// The directory listing carries no front matter, so each entry is fetched to learn its status.
    /// Concurrent, and a row that fails to load or parse is simply left out of the map — it renders as
    /// unknown rather than failing the whole page.
    /// </summary>
    private async Task LoadDraftStatusesAsync()
    {
        if (_descriptor is null) return;

        IEnumerable<Task<(string FileName, bool? Draft)>> fetches = _rows.Select(async row =>
        {
            try
            {
                RepoFile? file = await Api.GetFileAsync(row.Path);
                if (file is null) return (row.FileName, (bool?)null);
                (object front, _) = _descriptor.ParseDocument(file.Text);
                // Cast explicitly: without it the two return statements infer different tuple types
                // (bool? on the null path, bool here) and the lambda fails to compile.
                return (row.FileName, (bool?)((front as IDraftable)?.Draft ?? false));
            }
            catch
            {
                return (row.FileName, (bool?)null);
            }
        });

        (string FileName, bool? Draft)[] results = await Task.WhenAll(fetches);

        _draftByFile.Clear();
        foreach ((string fileName, bool? draft) in results)
        {
            if (draft.HasValue)
            {
                _draftByFile[fileName] = draft.Value;
            }
        }
    }
```

- [ ] **Step 3: Render the filter and the column**

Above the `<table>` (line 48), add the filter control:

```razor
        @if (SupportsDrafts)
        {
            <div class="content-filter" role="group" aria-label="Filter by status">
                <button class="admin-button admin-button--small @(_filter == StatusFilter.All ? "" : "admin-button--ghost")"
                        @onclick="() => _filter = StatusFilter.All">All</button>
                <button class="admin-button admin-button--small @(_filter == StatusFilter.Drafts ? "" : "admin-button--ghost")"
                        @onclick="() => _filter = StatusFilter.Drafts">Drafts</button>
                <button class="admin-button admin-button--small @(_filter == StatusFilter.Published ? "" : "admin-button--ghost")"
                        @onclick="() => _filter = StatusFilter.Published">Published</button>
            </div>
        }
```

Add a header cell after the Slug header (line 55):

```razor
                    @if (SupportsDrafts)
                    {
                        <th class="content-table__status">Status</th>
                    }
```

Change the row loop at line 60 from `_rows` to `VisibleRows`, and add the matching cell after the slug cell (line 71):

```razor
                        @if (SupportsDrafts)
                        {
                            <td class="content-table__status">
                                @if (_draftByFile.TryGetValue(row.FileName, out bool isDraft))
                                {
                                    <span class="status-pill @(isDraft ? "status-pill--draft" : "status-pill--published")">
                                        @(isDraft ? "Draft" : "Published")
                                    </span>
                                }
                                else
                                {
                                    <span class="status-pill">Unknown</span>
                                }
                            </td>
                        }
```

Change the count line at line 92 to reflect the filter:

```razor
        <p class="content-table__count">@VisibleRows.Count of @_rows.Count entries</p>
```

- [ ] **Step 4: Keep the map in sync on delete**

In `ConfirmDeleteAsync`, after `_rows.Remove(row);` (line 225), add:

```csharp
            _draftByFile.Remove(row.FileName);
```

- [ ] **Step 5: Verify the build**

Run: `dotnet build TimPurdum.Dev.BlogGenerator.Admin/TimPurdum.Dev.BlogGenerator.Admin.csproj`
Expected: build succeeded, 0 errors.

- [ ] **Step 6: Commit**

```bash
git add TimPurdum.Dev.BlogGenerator.Admin/Pages/ContentListPage.razor
git commit -m "feat(admin): show and filter draft status on the content list"
```

---

### Task 9: Documentation and version bumps

**Files:**
- Modify: `TimPurdum.Dev.BlogGenerator.Admin/ReadMe.md`
- Modify: `TimPurdum.Dev.BlogGenerator.Admin/TimPurdum.Dev.BlogGenerator.Admin.csproj:14-15`
- Modify: `TimPurdum.Dev.BlogGenerator/TimPurdum.Dev.BlogGenerator.csproj` (the `PackageVersion` and `Version` lines)

**Interfaces:**
- Consumes: everything above.
- Produces: nothing.

- [ ] **Step 1: Document the feature**

Add a section to `TimPurdum.Dev.BlogGenerator.Admin/ReadMe.md`, after "The editor is markdown-only":

````markdown
## Drafts

Content whose front matter carries `draft: true` renders nowhere: no HTML file, no index or nav
entry, no `feed.xml` item, no `sitemap.xml` entry. An absent `draft` key means published, so
existing content needs no migration.

New entries created in the admin start as drafts. The editor shows a Draft or Published badge and a
Publish or Unpublish button beside Save.

Publishing a dated type re-dates the file to the day you publish — a draft's filename date is a
scratch value, and this keeps a post that sat for three weeks from publishing buried mid-archive.
That is a rename, so it is two commits. Pages and other `Plain`-named types keep their filename,
because for them the filename is the URL.

Unpublishing keeps the filename and removes the generated HTML on the next build. The compiler
prunes any generated `.html` under the post output root that no published entry claims, which also
cleans up after renames and deletions.

### Opting a custom content type in

```csharp
public sealed record MusicFrontMatter : IHasLastmodified, IDraftable
{
    [YamlMember(Alias = "draft")] public bool? Draft { get; set; }
    // ...
}
```

`bool?`, not `bool`. The serializer omits nulls, so a published entry writes no `draft` key and its
file stays byte-identical. A plain `bool` writes `draft: false` into every file the editor touches.

A type that does not implement `IDraftable` behaves exactly as before and shows no publish controls.
Note that this is not optional if the content's markdown already carries a `draft` key by hand:
front-matter keys absent from the model are dropped on save, so an unmodeled `draft: true` would be
erased and the entry published.
````

Also add a `### Upgrading from 1.4.x` note recording that `GitHubApiService.DeleteFileAsync` now returns `Task<RepoCommitResult>` instead of `Task`. Callers that ignore the result are unaffected.

- [ ] **Step 2: Bump both package versions**

In `TimPurdum.Dev.BlogGenerator.Admin.csproj`, set both `<PackageVersion>` and `<Version>` to `1.5.0-preview`.

In `TimPurdum.Dev.BlogGenerator.csproj`, set both `<PackageVersion>` and `<Version>` to `1.2.0`.

- [ ] **Step 3: Commit**

```bash
git add TimPurdum.Dev.BlogGenerator.Admin/ReadMe.md TimPurdum.Dev.BlogGenerator.Admin/TimPurdum.Dev.BlogGenerator.Admin.csproj TimPurdum.Dev.BlogGenerator/TimPurdum.Dev.BlogGenerator.csproj
git commit -m "docs: document drafts and bump both packages"
```

---

### Task 10: End-to-end verification against the real site

**Files:**
- No source changes. Runs against the consuming site at `D:\timpurdum.github.io`.

**Interfaces:**
- Consumes: everything above.
- Produces: a verified branch ready for a pull request.

Unit tests cover the parsing and deletion logic. This task covers what they cannot: that 20 real posts round-trip unchanged, and that the admin's publish flow does what it claims against a real repository.

- [ ] **Step 1: Point the consuming site at the feature branch**

```bash
cd /d/timpurdum.github.io/BlogGenerator
git fetch origin
git checkout feature/draft-publish
cd /d/timpurdum.github.io
```

- [ ] **Step 2: Confirm the no-op case**

Build the site and confirm generated output is unchanged.

```bash
cd /d/timpurdum.github.io/TimPurdum.Dev.Source && dotnet build -c Release
cd /d/timpurdum.github.io/BlogGenerator/TimPurdum.Dev.BlogGenerator.Compiler && dotnet run -c Release
cd /d/timpurdum.github.io && git status --short TimPurdum.Dev/wwwroot
```

Expected: no output — `wwwroot` is byte-identical to what is committed. Scope the check to `wwwroot`: the compiler stamps `lastmodified` back into source markdown when it regenerates a post, so `TimPurdum.Dev.Source/Content` is expected to move.

This is the regression that matters most. If any generated file changed, stop and diagnose before continuing.

- [ ] **Step 3: Confirm unpublishing removes the live page**

Add `draft: true` to the front matter of `TimPurdum.Dev.Source/Content/Posts/2022-11-19-mastodon-feed-in-jekyll.md`, then re-run the compiler.

```bash
cd /d/timpurdum.github.io/BlogGenerator/TimPurdum.Dev.BlogGenerator.Compiler && dotnet run -c Release
cd /d/timpurdum.github.io && git status --short TimPurdum.Dev/wwwroot
```

Expected: `TimPurdum.Dev/wwwroot/post/2022/11/19/mastodon-feed-in-jekyll.html` shows as deleted. Confirm by search that the slug no longer appears in `TimPurdum.Dev/wwwroot/index.html`, `feed.xml`, or `sitemap.xml`.

- [ ] **Step 4: Confirm republishing restores it**

Remove the `draft: true` line, re-run the compiler, and confirm `git status --short TimPurdum.Dev/wwwroot` is clean again — the file returns identical to the committed version.

- [ ] **Step 5: Confirm the rename cleanup**

Rename `TimPurdum.Dev.Source/Content/Posts/2022-11-19-mastodon-feed-in-jekyll.md` to `2022-11-20-mastodon-feed-in-jekyll.md`, re-run the compiler, and confirm the old `post/2022/11/19/` output is gone and `post/2022/11/20/` exists. Then revert the rename and re-run so the tree is clean.

- [ ] **Step 6: Drive the admin against a scratch branch**

Do not run this against `main` — every admin save is a real commit that triggers a deploy. Create a scratch branch on the site repo first, and point `GitHubRepoConfig` at it if the admin does not already target the default branch.

```bash
cd /d/timpurdum.github.io/TimPurdum.Dev.Admin && dotnet run
```

Walk through and confirm each: a new post saves with `draft: true` in its front matter and offers no "view live" link; the content list shows it as a draft and the Drafts filter finds it; Publish renames the file to today's date, removes the `draft` key, and leaves exactly two commits; the deploy banner tracks a run that completes rather than one that is canceled; Unpublish restores `draft: true` and leaves the filename alone.

- [ ] **Step 7: Confirm an existing post's diff stays clean**

Open an already-published post in the admin, change one word of the body, and save. Inspect the commit diff: only the body line and `lastmodified` should change. No `draft: false` line may appear anywhere.

- [ ] **Step 8: Restore the submodule pointer and open the pull request**

```bash
cd /d/timpurdum.github.io/BlogGenerator && git checkout main
cd /d/worktrees/bg-draft-publish && git push -u origin feature/draft-publish
gh pr create --repo TimPurdum/BlogGenerator --base main --head feature/draft-publish --title "feat: draft and publish for content types"
```

The submodule bump in the consuming site is a separate change, made after this pull request merges.
