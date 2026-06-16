namespace TimPurdum.Dev.BlogGenerator.Admin.ContentTypes;

/// <summary>
/// Describes a single content type the admin manages. Each descriptor owns the file-naming pattern,
/// the front-matter type + form component, and the parse/build callbacks that close over the
/// generic <c>TFront</c> type — so the Editor never needs a switch statement and the registry
/// is fully extensible at runtime.
/// </summary>
public interface IContentTypeDescriptor
{
    /// <summary>URL slug ("posts", "music"). Drives the <c>/admin/{slug}</c> routes.</summary>
    string Slug { get; }

    /// <summary>Plural display label shown in nav and on the list page header ("Music", "Posts").</summary>
    string DisplayName { get; }

    /// <summary>Singular noun for buttons and messages ("post", "music entry").</summary>
    string SingularNoun { get; }

    /// <summary>Short hint shown on the dashboard tile under the count.</summary>
    string DashboardHint { get; }

    /// <summary>Full repo-relative directory the content files live in.</summary>
    string ContentPath { get; }

    /// <summary>How filenames encode date + slug.</summary>
    ContentNamePattern NamePattern { get; }

    /// <summary>Public URL stem used when deriving the "view live" URL. Often differs from the admin
    /// <see cref="Slug"/> (e.g. admin slug <c>posts</c> → URL stem <c>post</c>). Defaults to <see cref="Slug"/>
    /// when the consumer doesn't override.</summary>
    string UrlStem { get; }

    /// <summary>Order hint for nav and dashboard tiles. Lower = earlier. Ties broken by slug.</summary>
    int Order { get; }

    /// <summary>The CLR type of the front-matter record. Used for type identity checks only.</summary>
    Type FrontMatterType { get; }

    /// <summary>The Razor component type rendering the front-matter form. Dispatched via DynamicComponent.</summary>
    Type EditorFormType { get; }

    /// <summary>
    /// Optional override that builds the public "view live" URL from a filename. When null, the
    /// editor falls back to a default derivation based on <see cref="NamePattern"/> and slug.
    /// </summary>
    Func<string, string>? BuildLiveUrl { get; }

    /// <summary>Allocate a fresh, default-initialized front-matter instance.</summary>
    object CreateFrontMatter();

    /// <summary>Parse raw markdown content into (front-matter, body).</summary>
    (object Frontmatter, string Body) ParseDocument(string text);

    /// <summary>Serialize front-matter + body back to markdown. Stamps lastmodified if applicable.</summary>
    string BuildDocument(object frontmatter, string body);
}
