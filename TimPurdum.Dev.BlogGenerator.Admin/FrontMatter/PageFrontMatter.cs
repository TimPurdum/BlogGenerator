using TimPurdum.Dev.BlogGenerator.Admin.ContentTypes;
using YamlDotNet.Serialization;

namespace TimPurdum.Dev.BlogGenerator.Admin.FrontMatter;

/// <summary>
/// Default front-matter for a static page (about, index, etc.) — markdown files under
/// <c>Content/Pages/</c> with no date prefix. <see cref="Layout"/> chooses the rendering template
/// on the static-site side (e.g. <c>page</c> for vanilla content, <c>home</c> for a custom layout).
///
/// Includes optional <c>heroImage</c> / <c>heroImageAlt</c> fields that home-layout templates can
/// pick up via BlogGenerator's <c>ExtraFrontMatter</c> forwarding. Sites that don't have a home
/// layout will simply ignore them.
/// </summary>
public sealed class PageFrontMatter : IHasLastmodified
{
    [YamlMember(Alias = "layout")] public string Layout { get; set; } = "page";
    [YamlMember(Alias = "title")] public string Title { get; set; } = "";
    [YamlMember(Alias = "subtitle")] public string? Subtitle { get; set; }
    [YamlMember(Alias = "description")] public string? Description { get; set; }
    /// <summary>Optional — path to a hero figure image (home layouts pick this up).</summary>
    [YamlMember(Alias = "heroImage")] public string? HeroImage { get; set; }
    /// <summary>Optional — alt text for the hero image.</summary>
    [YamlMember(Alias = "heroImageAlt")] public string? HeroImageAlt { get; set; }
    [YamlMember(Alias = "lastmodified")] public string? Lastmodified { get; set; }
}
