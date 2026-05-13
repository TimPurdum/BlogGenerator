using System.Reflection;
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

    /// <summary>Collections exposed to user-authored landing pages via <see cref="MarkupComponent"/>.
    /// Replaced (not mutated) inside <see cref="GenerateSite"/> before pages are rendered.</summary>
    // TODO: collapse static state into an injected context (BlogSettings + collections) — broader refactor across MarkupParser + Generator.
    public static List<MusicMetaData> MusicEntries = [];
    public static List<ShowMetaData> ShowEntries = [];
    public static List<GalleryMetaData> GalleryEntries = [];

    public static async Task GenerateSite(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
        _loggerFactory = serviceProvider.GetRequiredService<ILoggerFactory>();
        BlogSettings = serviceProvider.GetRequiredService<BlogSettings>();
        await using HtmlRenderer renderer = new(_serviceProvider, _loggerFactory);

        List<PostMetaData> posts = MarkupParser.GeneratePostMetaDatas();
        MusicEntries = MarkupParser.GenerateMusicMetaDatas();
        ShowEntries = MarkupParser.GenerateShowMetaDatas();
        GalleryEntries = MarkupParser.GenerateGalleryMetaDatas();

        List<LinkData> navLinks = [];
        foreach (PostMetaData post in posts)
        {
            navLinks.Add(new LinkData(post.Title, post.SubTitle, post.Url,
                post.PublishedDate, post.Author));
        }

        List<PageMetaData> pages = await MarkupParser.GeneratePageMetaDatas(navLinks);

        Type rootTemplateType = Assembly.LoadFile(BlogSettings.SourceAssemblyOutputPath!).GetTypes()
                   .FirstOrDefault(t => t.IsSubclassOf(typeof(BaseRootTemplate)))
               ?? typeof(RootTemplate);

        foreach (PageMetaData page in pages)
        {
            string html = await RenderPage(page, renderer, navLinks, rootTemplateType);
            string fileName = Path.GetFileNameWithoutExtension(page.Url);
            if (string.IsNullOrWhiteSpace(fileName) || fileName == Path.DirectorySeparatorChar.ToString())
                fileName = "index";
            string filePath = Path.Combine(BlogSettings.OutputWebRootPath, $"{fileName}.html");
            await File.WriteAllTextAsync(filePath, html);

            await CreateRazorComponents(page.RazorComponents);
        }

        foreach (PostMetaData post in posts)
        {
            if (!post.Update) continue;
            string html = await RenderPost(post, renderer, navLinks, rootTemplateType);
            await File.WriteAllTextAsync(post.OutputPath, html);
            await CreateRazorComponents(post.RazorComponents);
        }

        foreach (MusicMetaData music in MusicEntries)
        {
            if (!music.Update) continue;
            string html = await RenderMusic(music, renderer, navLinks, rootTemplateType);
            await File.WriteAllTextAsync(music.OutputPath, html);
            await CreateRazorComponents(music.RazorComponents);
        }

        foreach (ShowMetaData show in ShowEntries)
        {
            if (!show.Update) continue;
            string html = await RenderShow(show, renderer, navLinks, rootTemplateType);
            await File.WriteAllTextAsync(show.OutputPath, html);
            await CreateRazorComponents(show.RazorComponents);
        }

        foreach (GalleryMetaData gallery in GalleryEntries)
        {
            if (!gallery.Update) continue;
            string html = await RenderGallery(gallery, renderer, navLinks, rootTemplateType);
            await File.WriteAllTextAsync(gallery.OutputPath, html);
            await CreateRazorComponents(gallery.RazorComponents);
        }

        // RSS feed (posts only for now; extending to music/shows is a Phase 4 polish item)
        string rssXml = await RssFeedGenerator.GenerateRssFeed(posts);
        string rssFilePath = Path.Combine(BlogSettings.OutputWebRootPath, "feed.xml");
        await File.WriteAllTextAsync(rssFilePath, rssXml);
    }

    public static async Task<string> RenderComponent(Dictionary<string, object?> parameters)
    {
        await using HtmlRenderer renderer = new(_serviceProvider!, _loggerFactory!);
        return await renderer.Dispatcher.InvokeAsync(async () =>
        {
            ParameterView parameterView = ParameterView.FromDictionary(parameters);
            var root = await renderer.RenderComponentAsync<MarkupComponent>(parameterView);
            return root.ToHtmlString();
        });
    }

    private static Task<string> RenderPage(PageMetaData page, HtmlRenderer renderer, List<LinkData> navLinks,
        Type rootTemplateType)
    {
        // Expose the typed collections so custom page layouts (e.g. a HomeLayout that needs upcoming
        // shows) can declare them as [Parameter] props, plus any non-standard frontmatter the page
        // author included (heroImage, custom flags, etc). BaseRootTemplate's reflection filter drops
        // keys the layout doesn't declare.
        Dictionary<string, object?> extras = new()
        {
            [nameof(MusicEntries)]   = MusicEntries,
            [nameof(ShowEntries)]    = ShowEntries,
            [nameof(GalleryEntries)] = GalleryEntries,
        };
        if (page.ExtraFrontMatter is not null)
        {
            foreach (KeyValuePair<string, string> kv in page.ExtraFrontMatter)
            {
                // PascalCase the key so it matches the conventional C# property name on the layout
                // (frontmatter is lowercase, properties are PascalCase).
                string camel = char.ToUpperInvariant(kv.Key[0]) + kv.Key[1..];
                extras[camel] = kv.Value;
            }
        }
        return RenderRoot(
            layout: page.Layout,
            title: (MarkupString)page.Title,
            subTitle: (MarkupString)page.SubTitle,
            description: (MarkupString)page.Description,
            publishedDate: null,
            author: null,
            url: page.Url,
            content: (MarkupString)page.Content,
            scriptTags: page.ScriptTags,
            extraParameters: extras,
            renderer, navLinks, rootTemplateType);
    }

    private static Task<string> RenderPost(PostMetaData post, HtmlRenderer renderer, List<LinkData> navLinks,
        Type rootTemplateType)
        => RenderRoot(
            layout: post.Layout,
            title: (MarkupString)post.Title,
            subTitle: (MarkupString)post.SubTitle,
            description: (MarkupString)post.Description,
            publishedDate: post.PublishedDate,
            author: post.Author,
            url: post.Url,
            content: (MarkupString)post.Content,
            scriptTags: post.ScriptTags,
            extraParameters: null,
            renderer, navLinks, rootTemplateType);

    private static Task<string> RenderMusic(MusicMetaData music, HtmlRenderer renderer, List<LinkData> navLinks,
        Type rootTemplateType)
        => RenderRoot(
            layout: music.Layout,
            title: (MarkupString)music.Title,
            subTitle: (MarkupString)music.SubTitle,
            description: (MarkupString)music.Description,
            publishedDate: null,
            author: null,
            url: music.Url,
            content: (MarkupString)music.Content,
            scriptTags: music.ScriptTags,
            extraParameters: music.ExtraParameters,
            renderer, navLinks, rootTemplateType);

    private static Task<string> RenderShow(ShowMetaData show, HtmlRenderer renderer, List<LinkData> navLinks,
        Type rootTemplateType)
        => RenderRoot(
            layout: show.Layout,
            title: (MarkupString)show.Title,
            subTitle: (MarkupString)show.SubTitle,
            description: (MarkupString)show.Description,
            publishedDate: null,
            author: null,
            url: show.Url,
            content: (MarkupString)show.Content,
            scriptTags: show.ScriptTags,
            extraParameters: show.ExtraParameters,
            renderer, navLinks, rootTemplateType);

    private static Task<string> RenderGallery(GalleryMetaData gallery, HtmlRenderer renderer, List<LinkData> navLinks,
        Type rootTemplateType)
        => RenderRoot(
            layout: gallery.Layout,
            title: (MarkupString)gallery.Title,
            subTitle: (MarkupString)gallery.SubTitle,
            description: (MarkupString)gallery.Description,
            publishedDate: null,
            author: null,
            url: gallery.Url,
            content: (MarkupString)gallery.Content,
            scriptTags: gallery.ScriptTags,
            extraParameters: gallery.ExtraParameters,
            renderer, navLinks, rootTemplateType);

    private static async Task<string> RenderRoot(
        string layout,
        MarkupString title,
        MarkupString? subTitle,
        MarkupString? description,
        DateTime? publishedDate,
        string? author,
        string? url,
        MarkupString content,
        List<string> scriptTags,
        Dictionary<string, object?>? extraParameters,
        HtmlRenderer renderer,
        List<LinkData> navLinks,
        Type rootTemplateType)
    {
        return await renderer.Dispatcher.InvokeAsync(async () =>
        {
            Dictionary<string, object?> parameters = new()
            {
                { nameof(BaseRootTemplate.Layout), layout },
                { nameof(BaseRootTemplate.NavLinks), navLinks },
                { nameof(BaseRootTemplate.Title), title },
                { nameof(BaseRootTemplate.SubTitle), subTitle },
                { nameof(BaseRootTemplate.Description), description },
                { nameof(BaseRootTemplate.PublishedDate), publishedDate },
                { nameof(BaseRootTemplate.Author), author },
                { nameof(BaseRootTemplate.Url), url },
                { nameof(BaseRootTemplate.Content), content },
                { nameof(BaseRootTemplate.SiteName), BlogSettings!.SiteName },
                { nameof(BaseRootTemplate.HeaderLinks), (MarkupString)string.Join(Environment.NewLine, BlogSettings.HeaderLinks) },
                { nameof(BaseRootTemplate.Scripts), scriptTags.Select(s => (MarkupString)s).ToList() },
                { nameof(BaseRootTemplate.ExtraParameters), extraParameters }
            };
            ParameterView parameterView = ParameterView.FromDictionary(parameters);
            var root = await renderer.RenderComponentAsync(rootTemplateType, parameterView);
            return root.ToHtmlString();
        });
    }

    private static async Task CreateRazorComponents(Dictionary<string, string> components)
    {
        foreach (KeyValuePair<string, string> kvp in components)
        {
            string componentName = kvp.Key.KebabCaseToPascalCase();
            string componentContent = kvp.Value;
            componentContent += $$"""

                                  @code
                                  {
                                      [System.Diagnostics.CodeAnalysis.DynamicDependency(
                                          System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.All, typeof({{componentName}}))]
                                      private static void Preserve() { }
                                  }
                                  """;

            string componentFilePath = Path.Combine(BlogSettings!.OutputComponentsPath,
                $"{componentName}.razor");
            await File.WriteAllTextAsync(componentFilePath, componentContent);

            Console.WriteLine($"Created Razor component: {componentFilePath}");
        }
    }
}
