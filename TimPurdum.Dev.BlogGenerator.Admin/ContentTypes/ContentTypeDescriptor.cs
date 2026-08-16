using TimPurdum.Dev.BlogGenerator.Admin.Services;

namespace TimPurdum.Dev.BlogGenerator.Admin.ContentTypes;

/// <summary>
/// Strongly-typed descriptor that closes over <typeparamref name="TFront"/> so the parse/build
/// callbacks can invoke <see cref="MarkdownDocument.Parse{T}"/> and <see cref="MarkdownDocument.Build{T}"/>
/// without reflection. Auto-stamps <c>lastmodified</c> if the front-matter type implements
/// <see cref="IHasLastmodified"/>.
/// </summary>
public sealed class ContentTypeDescriptor<TFront> : IContentTypeDescriptor where TFront : class, new()
{
    public required string Slug { get; init; }
    public required string DisplayName { get; init; }
    public required string SingularNoun { get; init; }
    public required string DashboardHint { get; init; }
    public required string ContentPath { get; init; }
    public required ContentNamePattern NamePattern { get; init; }
    public required Type EditorFormType { get; init; }
    public int Order { get; init; }
    public Func<string, string>? BuildLiveUrl { get; init; }
    /// <summary>Public URL stem. Falls back to <see cref="Slug"/> when null.</summary>
    public string? UrlStemOverride { get; init; }

    public string UrlStem => UrlStemOverride ?? Slug;

    public Type FrontMatterType => typeof(TFront);

    /// <summary>Allocates a default-initialized front-matter instance.</summary>
    public object CreateFrontMatter() => new TFront();

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

    public (object Frontmatter, string Body) ParseDocument(string text)
    {
        (TFront front, string body) = MarkdownDocument.Parse<TFront>(text);
        return (front, body);
    }

    public string BuildDocument(object frontmatter, string body)
    {
        TFront front = (TFront)frontmatter;
        if (front is IHasLastmodified stamped)
        {
            stamped.Lastmodified = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss");
        }
        return MarkdownDocument.Build(front, body);
    }
}
