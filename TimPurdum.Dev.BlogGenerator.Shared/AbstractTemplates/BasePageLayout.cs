using Microsoft.AspNetCore.Components;

namespace TimPurdum.Dev.BlogGenerator.Shared.AbstractTemplates;

public abstract class BasePageLayout: LayoutComponentBase
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
}