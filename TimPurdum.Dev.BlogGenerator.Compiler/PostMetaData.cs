public record PostMetaData(string Title, string SubTitle, string Url, DateTime PublishedDate,
    string Author, string Content, Dictionary<string, string> RazorComponents,
    List<string> ScriptTags, string Layout, string Description, string OutputPath, bool Update);

public record PageMetaData(string Title, string SubTitle, string Url, string Content,
    Dictionary<string, string> RazorComponents, List<string> ScriptTags, string Layout,
    string Description, int NavOrder,
    /// <summary>Frontmatter fields beyond the standard set (title/subtitle/description/layout/navorder),
    /// passed through to the layout via BaseRootTemplate's ExtraParameters reflection binding so custom
    /// layouts can declare typed [Parameter] props for arbitrary frontmatter keys.</summary>
    Dictionary<string, string>? ExtraFrontMatter = null);
