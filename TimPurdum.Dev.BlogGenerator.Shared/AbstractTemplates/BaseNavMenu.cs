using Microsoft.AspNetCore.Components;

namespace TimPurdum.Dev.BlogGenerator.Shared.AbstractTemplates;

public class BaseNavMenu: ComponentBase
{
    [EditorRequired]
    [Parameter]
    public required List<LinkData> NavLinks { get; set; }
}