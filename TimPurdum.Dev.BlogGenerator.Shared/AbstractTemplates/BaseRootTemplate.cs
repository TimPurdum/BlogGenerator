using System.Reflection;
using Microsoft.AspNetCore.Components;
using TimPurdum.Dev.BlogGenerator.Shared.DefaultImplementationTemplates;

namespace TimPurdum.Dev.BlogGenerator.Shared.AbstractTemplates;

public abstract class BaseRootTemplate: ComponentBase
{
    [Parameter]
    public required string Layout { get; set; }
    
    [Parameter]
    public required MarkupString Title { get; set; }
    
    [Parameter]
    public MarkupString? SubTitle { get; set; }
    
    [Parameter]
    public MarkupString? Description { get; set; }
    
    [Parameter]
    public required MarkupString Content { get; set; }
    
    [Parameter]
    public string? Author { get; set; }
    
    [Parameter]
    public DateTime? PublishedDate { get; set; }
    
    [Parameter]
    public string? Url { get; set; }
    
    [Parameter]
    public string? SiteName { get; set; }
    
    [Parameter]
    public MarkupString? HeaderLinks { get; set; }
    
    [Parameter]
    public List<LinkData>? NavLinks { get; set; }
    
    [Parameter]
    public List<MarkupString>? Scripts { get; set; }

    protected Type LayoutType
    {
        get
        {
            _layoutType ??= GetType().Assembly.GetTypes()
                                .Where(t => t.IsSubclassOf(typeof(ComponentBase)))
                                .FirstOrDefault(t =>
                                    string.Equals(t.Name, Layout, StringComparison.OrdinalIgnoreCase))
                            ?? typeof(BaseRootTemplate).Assembly.GetTypes()
                                .Where(t => t.IsSubclassOf(typeof(ComponentBase)))
                                .FirstOrDefault(t =>
                                    string.Equals(t.Name, Layout, StringComparison.OrdinalIgnoreCase))
                            ?? typeof(PostLayout);

            return _layoutType;
        }
    }

    private PropertyInfo[] LayoutProperties
    {
        get
        {
            _layoutProperties ??= LayoutType.GetProperties(BindingFlags.Public | BindingFlags.Instance);
            return _layoutProperties;
        }
    }

    private Type? _layoutType;
    private PropertyInfo[]? _layoutProperties;
    protected Dictionary<string, object?> Parameters => new Dictionary<string, object?>
    {
        { nameof(Title), Title },
        { nameof(SubTitle), SubTitle }, 
        { nameof(PublishedDate), PublishedDate },
        { nameof(Content), Content },
        { nameof(Author), Author },
        { nameof(Url), Url },
        { nameof(SiteName), SiteName },
        { nameof(Description), Description },
        { nameof(NavLinks), NavLinks }
    }.Where(kv => LayoutProperties.Any(p => p.Name.Equals(kv.Key, StringComparison.OrdinalIgnoreCase)))
        .ToDictionary(kv => kv.Key, kv => kv.Value);
}