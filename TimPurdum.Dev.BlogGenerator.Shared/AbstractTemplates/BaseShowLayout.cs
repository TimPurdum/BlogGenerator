using Microsoft.AspNetCore.Components;

namespace TimPurdum.Dev.BlogGenerator.Shared.AbstractTemplates;

public abstract class BaseShowLayout : LayoutComponentBase
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

    /// <summary>Performance date parsed from the file's YYYY-MM-DD prefix. Same date used in the URL.</summary>
    [Parameter]
    public DateTime? PerformanceDate { get; set; }

    /// <summary>Free-form time string from frontmatter (e.g. "7:30 PM"). Not parsed.</summary>
    [Parameter]
    public string? Time { get; set; }

    [Parameter]
    public string? Venue { get; set; }

    [Parameter]
    public string? City { get; set; }

    [Parameter]
    public string? TicketUrl { get; set; }

    [Parameter]
    public string? Program { get; set; }

    [Parameter]
    public string? Role { get; set; }

    [Parameter]
    public string? Description { get; set; }
}
