using System.Reflection;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Components;
using TimPurdum.Dev.BlogGenerator.Shared;
using TimPurdum.Dev.BlogGenerator.Shared.AbstractTemplates;
using TimPurdum.Dev.BlogGenerator.Shared.DefaultImplementationTemplates;
using HtmlRenderer = Microsoft.AspNetCore.Components.Web.HtmlRenderer;

namespace TimPurdum.Dev.BlogGenerator.Compiler;

public static class Generator
{
    private static IServiceProvider? _serviceProvider;
    private static ILoggerFactory? _loggerFactory;
    public static BlogSettings? BlogSettings;

    public static async Task GenerateSite(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
        _loggerFactory = serviceProvider.GetRequiredService<ILoggerFactory>();
        BlogSettings = serviceProvider.GetRequiredService<BlogSettings>();
        await using HtmlRenderer renderer = new(_serviceProvider, _loggerFactory);

        var posts = MarkupParser.GeneratePostMetaDatas();
        List<LinkData> navLinks = [];

        foreach (var post in posts)
            navLinks.Add(new LinkData(post.Title, post.SubTitle, post.Url,
                post.PublishedDate, post.Author));

        var pages = await MarkupParser.GeneratePageMetaDatas(navLinks);

        Type rootTemplateType = Assembly.LoadFile(BlogSettings.SourceAssemblyOutputPath!).GetTypes()
                   .FirstOrDefault(t => t.IsSubclassOf(typeof(BaseRootTemplate)))
               ?? typeof(RootTemplate);

        foreach (var page in pages)
        {
            var html = await RenderPage(page, renderer, navLinks, rootTemplateType);
            var fileName = Path.GetFileNameWithoutExtension(page.Url);
            if (string.IsNullOrWhiteSpace(fileName) || fileName == Path.DirectorySeparatorChar.ToString())
                fileName = "index";
            var filePath = Path.Combine(BlogSettings.OutputWebRootPath, $"{fileName}.html");
            await File.WriteAllTextAsync(filePath, html);

            await CreateComponents(page.RazorComponents);
        }

        foreach (var post in posts)
        {
            var html = await RenderPost(post, renderer, navLinks, rootTemplateType);
            var outputFolder = Path.Combine(
                BlogSettings.OutputWebRootPath,
                "post",
                post.PublishedDate.Year.ToString(),
                post.PublishedDate.Month.ToString(),
                post.PublishedDate.Day.ToString());
            Directory.CreateDirectory(outputFolder);
            var fileName = Path.GetFileNameWithoutExtension(post.Url);
            var filePath = Path.Combine(outputFolder, $"{fileName}.html");
            await File.WriteAllTextAsync(filePath, html);

            await CreateComponents(post.RazorComponents);
        }

        // Generate the RSS feed
        var rssXml = await RssFeedGenerator.GenerateRssFeed(posts);
        var rssFilePath = Path.Combine(BlogSettings.OutputWebRootPath, "feed.xml");
        await File.WriteAllTextAsync(rssFilePath, rssXml);
    }

    public static async Task<string> RenderComponent(Dictionary<string, object?> parameters)
    {
        await using HtmlRenderer renderer = new(_serviceProvider!, _loggerFactory!);
        return await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var parameterView = ParameterView.FromDictionary(parameters);

            var root = await renderer
                .RenderComponentAsync<MarkupComponent>(parameterView);

            return root.ToHtmlString();
        });
    }

    private static async Task<string> RenderPage(PageMetaData page, HtmlRenderer renderer, List<LinkData> navLinks,
        Type rootTemplateType)
    {
        return await renderer.Dispatcher.InvokeAsync(async () =>
        {
            Dictionary<string, object?> parameters = new()
            {
                { nameof(BaseRootTemplate.Layout), page.Layout },
                { nameof(BaseRootTemplate.NavLinks), navLinks },
                { nameof(BaseRootTemplate.Title), (MarkupString)page.Title },
                { nameof(BaseRootTemplate.SubTitle), (MarkupString)page.SubTitle },
                { nameof(BaseRootTemplate.Description), (MarkupString)page.Description },
                { nameof(BaseRootTemplate.PublishedDate), null },
                { nameof(BaseRootTemplate.Author), null },
                { nameof(BaseRootTemplate.Url), page.Url },
                { nameof(BaseRootTemplate.Content), (MarkupString)page.Content },
                { nameof(BaseRootTemplate.SiteName), BlogSettings!.SiteName },
                { nameof(BaseRootTemplate.HeaderLinks), (MarkupString)string.Join(Environment.NewLine, BlogSettings.HeaderLinks) },
                { nameof(BaseRootTemplate.Scripts), page.ScriptTags.Select(s => (MarkupString)s).ToList() }
            };
            var parameterView = ParameterView.FromDictionary(parameters);
            var root = await renderer
                .RenderComponentAsync(rootTemplateType, parameterView);
            return root.ToHtmlString();
        });
    }

    private static async Task<string> RenderPost(PostMetaData post, HtmlRenderer renderer, List<LinkData> navLinks,
        Type rootTemplateType)
    {
        return await renderer.Dispatcher.InvokeAsync(async () =>
        {
            Dictionary<string, object?> parameters = new()
            {
                { nameof(BaseRootTemplate.Layout), post.Layout },
                { nameof(BaseRootTemplate.NavLinks), navLinks },
                { nameof(BaseRootTemplate.Title), (MarkupString)post.Title },
                { nameof(BaseRootTemplate.SubTitle), (MarkupString)post.SubTitle },
                { nameof(BaseRootTemplate.Description), (MarkupString)post.Description },
                { nameof(BaseRootTemplate.PublishedDate), post.PublishedDate },
                { nameof(BaseRootTemplate.Author), post.Author },
                { nameof(BaseRootTemplate.Url), post.Url },
                { nameof(BaseRootTemplate.Content), (MarkupString)post.Content },
                { nameof(BaseRootTemplate.SiteName), BlogSettings!.SiteName },
                { nameof(BaseRootTemplate.HeaderLinks), (MarkupString)string.Join(Environment.NewLine, BlogSettings.HeaderLinks) },
                { nameof(BaseRootTemplate.Scripts), post.ScriptTags.Select(s => (MarkupString)s).ToList() }
            };
            var parameterView = ParameterView.FromDictionary(parameters);
            var root = await renderer
                .RenderComponentAsync(rootTemplateType, parameterView);
            return root.ToHtmlString();
        });
    }

    private static async Task CreateComponents(Dictionary<string, string> components)
    {
        foreach (var kvp in components)
        {
            var componentName = kvp.Key.KebabCaseToPascalCase();
            // add Preserve method to prevent trimming of components
            var componentContent = kvp.Value;

            if (CodeBlockRegex.Match(componentContent) is { Success: true } match)
            {
                componentContent = componentContent.Replace(match.Value, $$"""
                                      @code
                                      {
                                          [System.Diagnostics.CodeAnalysis.DynamicDependency(
                                              System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.All, typeof({{componentName}}))]
                                          private static void Preserve() { }
                                          
                                          {{match.Groups["content"].Value}}
                                      }
                                      """);
            }
            else
            {
                componentContent += $$"""

                                      @code
                                      {
                                          [System.Diagnostics.CodeAnalysis.DynamicDependency(
                                              System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.All, typeof({{componentName}}))]
                                          private static void Preserve() { }
                                      }
                                      """;
            }

            // Write the component content to a file
            var componentFilePath = Path.Combine(BlogSettings!.OutputComponentsPath,
                $"{componentName}.razor");
            await File.WriteAllTextAsync(componentFilePath, componentContent);

            Console.WriteLine($"Created Razor component: {componentFilePath}");
        }
    }
    
    private static readonly Regex CodeBlockRegex = new(@"@code\s*\{(?<content>[^}]*)\}", RegexOptions.Compiled);
}