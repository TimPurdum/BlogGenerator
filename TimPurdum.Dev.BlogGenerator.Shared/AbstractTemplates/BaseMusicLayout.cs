using Microsoft.AspNetCore.Components;

namespace TimPurdum.Dev.BlogGenerator.Shared.AbstractTemplates;

public abstract class BaseMusicLayout : LayoutComponentBase
{
    [Parameter]
    public required MarkupString Title { get; set; }

    [Parameter]
    public MarkupString? SubTitle { get; set; }

    [Parameter]
    public required MarkupString Content { get; set; }

    [EditorRequired]
    [Parameter]
    public required List<LinkData> NavLinks { get; set; }

    /// <summary>Performance / recording date parsed from the file's YYYY-MM-DD prefix.</summary>
    [Parameter]
    public DateTime? Date { get; set; }

    /// <summary>"performance" | "recording" | "composition" — frontmatter <c>type</c>.</summary>
    [Parameter]
    public string? Type { get; set; }

    [Parameter]
    public string? Ensemble { get; set; }

    /// <summary>"soloist" | "principal" | "section" | "conductor".</summary>
    [Parameter]
    public string? Role { get; set; }

    [Parameter]
    public string? Venue { get; set; }

    [Parameter]
    public string? EmbedUrl { get; set; }

    [Parameter]
    public string? CoverImage { get; set; }

    [Parameter]
    public string? Description { get; set; }
}
