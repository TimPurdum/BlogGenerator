using Microsoft.AspNetCore.Components;

namespace TimPurdum.Dev.BlogGenerator.Shared.AbstractTemplates;

public abstract class BaseGalleryLayout : LayoutComponentBase
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

    /// <summary>Approximate date (YYYY-MM) parsed from the file prefix.</summary>
    [Parameter]
    public DateTime? Date { get; set; }

    [Parameter]
    public List<GalleryImage>? Images { get; set; }

    [Parameter]
    public string? Description { get; set; }
}
