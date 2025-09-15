using Microsoft.AspNetCore.Components;

namespace TimPurdum.Dev.BlogGenerator.Shared.AbstractTemplates;

public abstract class BaseHeader: ComponentBase
{
    [EditorRequired]
    [Parameter]
    public required List<LinkData> NavLinks { get; set; }
}