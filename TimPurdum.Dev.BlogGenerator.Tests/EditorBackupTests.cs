using System.Text.Json;
using TimPurdum.Dev.BlogGenerator.Admin.FrontMatter;
using TimPurdum.Dev.BlogGenerator.Admin.Services;

namespace TimPurdum.Dev.BlogGenerator.Tests;

/// <summary>
/// Round-trip coverage for the editor's local-storage auto-backup payload and the non-generic
/// front-matter serializer the dirty-check snapshot uses. The backup crosses a JSON boundary into
/// localStorage and back on every store and restore, so the payload contract — fields intact,
/// times round-trip, and the snapshot serializer staying aligned with what Save would write —
/// is what the feature's correctness rests on.
/// </summary>
[TestClass]
public sealed class EditorBackupTests
{
    [TestMethod]
    public void EditorBackup_RoundTrips_ThroughJson()
    {
        EditorBackup backup = new(
            FileSha: "abc123",
            Markdown: "---\nlayout: post\ntitle: Test\n---\n\nHello, body.",
            Slug: "my-entry",
            DateFull: "2026-09-24",
            DateMonth: "2026-09",
            SavedAt: new DateTime(2026, 9, 24, 14, 32, 7, DateTimeKind.Local));

        string json = JsonSerializer.Serialize(backup);
        EditorBackup restored = JsonSerializer.Deserialize<EditorBackup>(json) ?? new EditorBackup(
            null, "", "", "", "", DateTime.MinValue);

        Assert.AreEqual(backup.FileSha, restored.FileSha);
        Assert.AreEqual(backup.Markdown, restored.Markdown);
        Assert.AreEqual(backup.Slug, restored.Slug);
        Assert.AreEqual(backup.DateFull, restored.DateFull);
        Assert.AreEqual(backup.DateMonth, restored.DateMonth);
        Assert.AreEqual(backup.SavedAt, restored.SavedAt);
    }

    [TestMethod]
    public void EditorBackup_RoundTrips_WithNullFileSha()
    {
        // New entries store a null FileSha; a null must survive the JSON boundary so the
        // "saved version changed" notice never fires against a missing SHA.
        EditorBackup backup = new(null, "# body", "draft-slug", "2026-09-24", "2026-09", DateTime.Now);

        string json = JsonSerializer.Serialize(backup);
        EditorBackup restored = JsonSerializer.Deserialize<EditorBackup>(json) ?? backup;

        Assert.IsNull(restored.FileSha);
    }

    [TestMethod]
    public void SerializeFrontMatter_MatchesWhatBuildWrites()
    {
        // The dirty-check snapshot must compare against the same YAML the save path emits, or
        // "dirty" stops meaning "Save would change the file".
        PostFrontMatter front = new()
        {
            Title = "Snapshot test",
            Subtitle = "sub",
            Draft = true,
            Lastmodified = "2026-09-24 10:00:00"
        };

        string built = MarkdownDocument.Build(front, "");
        string serialized = MarkdownDocument.SerializeFrontMatter(front);

        Assert.IsTrue(built.Contains(serialized, StringComparison.Ordinal),
            "SerializeFrontMatter output must match the front-matter block Build writes.");
    }

    [TestMethod]
    public void SerializeFrontMatter_OmitsNulls()
    {
        PostFrontMatter front = new() { Title = "Only title" };

        string yaml = MarkdownDocument.SerializeFrontMatter(front);

        StringAssert.Contains(yaml, "title: Only title");
        Assert.IsFalse(yaml.Contains("draft", StringComparison.Ordinal),
            "A null Draft must serialize to no draft key, matching a published entry's file.");
    }
}