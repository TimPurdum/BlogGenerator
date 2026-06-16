using TimPurdum.Dev.BlogGenerator.Admin.ContentTypes;
using YamlDotNet.Serialization;

namespace TimPurdum.Dev.BlogGenerator.Admin.FrontMatter;

/// <summary>
/// Default front-matter for a blog post. Property order in YAML output follows the order of
/// properties here, so keep human-friendly fields (Title, Layout) first and machine-managed
/// fields (Lastmodified) last.
/// </summary>
public sealed class PostFrontMatter : IHasLastmodified
{
    [YamlMember(Alias = "layout")] public string Layout { get; set; } = "post";
    [YamlMember(Alias = "title")] public string Title { get; set; } = "";
    [YamlMember(Alias = "subtitle")] public string? Subtitle { get; set; }
    [YamlMember(Alias = "description")] public string? Description { get; set; }
    [YamlMember(Alias = "author")] public string? Author { get; set; }
    [YamlMember(Alias = "lastmodified")] public string? Lastmodified { get; set; }
}
