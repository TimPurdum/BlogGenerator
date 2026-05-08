namespace TimPurdum.Dev.BlogGenerator.Shared;

// URL convention (matches PostMetaData): the Url field is extensionless — e.g. "/music/dvorak-cello-concerto",
// "/show/2026/9/12/spring-concert", "/gallery/winter-series". Consumers append ".html" at href-render time
// (see NavMenu.razor / Index.razor on timpurdum.dev for the established pattern).
// Generated files on disk are at OutputPath, which DOES include the .html extension.

/// <summary>
/// A music portfolio entry. Surfaced to user-authored landing pages via <c>List&lt;MusicMetaData&gt;</c>
/// parameter on <see cref="MarkupComponent"/>.
/// </summary>
public record MusicMetaData(
    string Title, string SubTitle, string Url, DateTime Date,
    string Content, Dictionary<string, string> RazorComponents,
    List<string> ScriptTags, string Layout, string Description,
    string OutputPath, bool Update,
    Dictionary<string, object?> ExtraParameters);

/// <summary>
/// A show / event entry. <see cref="PerformanceDate"/> is the actual show date (also embedded in the URL),
/// not a publish date.
/// </summary>
public record ShowMetaData(
    string Title, string SubTitle, string Url, DateTime PerformanceDate,
    string Content, Dictionary<string, string> RazorComponents,
    List<string> ScriptTags, string Layout, string Description,
    string OutputPath, bool Update,
    Dictionary<string, object?> ExtraParameters);

/// <summary>
/// A photo gallery entry. <see cref="Images"/> is exposed both as a typed list here and inside
/// <see cref="ExtraParameters"/> for layout binding.
/// </summary>
public record GalleryMetaData(
    string Title, string SubTitle, string Url, DateTime Date,
    string Content, Dictionary<string, string> RazorComponents,
    List<string> ScriptTags, string Layout, string Description,
    string OutputPath, bool Update,
    List<GalleryImage> Images,
    Dictionary<string, object?> ExtraParameters);
