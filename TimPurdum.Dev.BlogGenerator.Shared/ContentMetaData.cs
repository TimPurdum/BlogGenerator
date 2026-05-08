namespace TimPurdum.Dev.BlogGenerator.Shared;

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
