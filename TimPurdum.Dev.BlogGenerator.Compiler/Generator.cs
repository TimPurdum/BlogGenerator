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

        // One partition, applied before anything reads the list. navLinks, the render loops, the RSS
        // feed and the sitemap all consume `posts`, so filtering here covers every surface at once.
        List<PostMetaData> allPosts = MarkupParser.GeneratePostMetaDatas();
        List<PostMetaData> posts = allPosts.Where(static p => !p.Draft).ToList();
        // Kept unfiltered (not just the assigned Entries fields) so the draft warning below can tell
        // which entries were excluded because they're drafts.
        List<MusicMetaData> allMusic = MarkupParser.GenerateMusicMetaDatas();
        List<ShowMetaData> allShows = MarkupParser.GenerateShowMetaDatas();
        List<GalleryMetaData> allGalleries = MarkupParser.GenerateGalleryMetaDatas();
        MusicEntries = allMusic.Where(static m => !m.Draft).ToList();
        ShowEntries = allShows.Where(static s => !s.Draft).ToList();
        GalleryEntries = allGalleries.Where(static g => !g.Draft).ToList();

        List<LinkData> navLinks = [];
        foreach (PostMetaData post in posts)
        {
            navLinks.Add(new LinkData(post.Title, post.SubTitle, post.Url,
                post.PublishedDate, post.Author));
        }

        List<PageMetaData> allPages = await MarkupParser.GeneratePageMetaDatas(navLinks);
        List<PageMetaData> pages = allPages.Where(static p => !p.Draft).ToList();

        Type rootTemplateType = Assembly.LoadFile(BlogSettings.SourceAssemblyOutputPath!).GetTypes()
                   .FirstOrDefault(t => t.IsSubclassOf(typeof(BaseRootTemplate)))
               ?? typeof(RootTemplate);

        foreach (PageMetaData page in pages)
        {
            string html = await RenderPage(page, renderer, navLinks, rootTemplateType);
            string filePath = PageOutputPath(page);
            await File.WriteAllTextAsync(filePath, html);

            await CreateRazorComponents(page.RazorComponents);
        }

        foreach (PostMetaData post in posts)
        {
            if (!post.Update) continue;
            string html = await RenderPost(post, renderer, navLinks, rootTemplateType);
            Directory.CreateDirectory(Path.GetDirectoryName(post.OutputPath)!);
            await File.WriteAllTextAsync(post.OutputPath, html);
            await CreateRazorComponents(post.RazorComponents);
        }

        foreach (MusicMetaData music in MusicEntries)
        {
            if (!music.Update) continue;
            string html = await RenderMusic(music, renderer, navLinks, rootTemplateType);
            Directory.CreateDirectory(Path.GetDirectoryName(music.OutputPath)!);
            await File.WriteAllTextAsync(music.OutputPath, html);
            await CreateRazorComponents(music.RazorComponents);
        }

        foreach (ShowMetaData show in ShowEntries)
        {
            if (!show.Update) continue;
            string html = await RenderShow(show, renderer, navLinks, rootTemplateType);
            Directory.CreateDirectory(Path.GetDirectoryName(show.OutputPath)!);
            await File.WriteAllTextAsync(show.OutputPath, html);
            await CreateRazorComponents(show.RazorComponents);
        }

        foreach (GalleryMetaData gallery in GalleryEntries)
        {
            if (!gallery.Update) continue;
            string html = await RenderGallery(gallery, renderer, navLinks, rootTemplateType);
            Directory.CreateDirectory(Path.GetDirectoryName(gallery.OutputPath)!);
            await File.WriteAllTextAsync(gallery.OutputPath, html);
            await CreateRazorComponents(gallery.RazorComponents);
        }

        // Unpublishing has to DELETE generated output, not merely skip it — the .html files are
        // committed to the consuming repo and served directly, so a skipped file stays live.
        //
        // This runs unconditionally, outside the `if (!entry.Update) continue` gates above. Update is
        // computed from source mtime against output mtime, and a fresh CI checkout stamps every file
        // with checkout time — deletion inside that gate would work locally and silently no-op in CI.
        //
        // A swept path is a draft's own output (an unpublish) or a path no live entry claims at all (an
        // orphan left by a rename or a deleted source file). Distinguish them so the log names the real
        // cause -- with parse failures now aborting the build (see MarkupParser.GeneratePostMetaData),
        // this is the main diagnostic for anything unexpected disappearing.
        HashSet<string> draftPostOutputPaths = new(
            allPosts.Where(static p => p.Draft).Select(static p => Path.GetFullPath(p.OutputPath)),
            StringComparer.OrdinalIgnoreCase);
        foreach (string removed in OutputSweeper.SweepOrphans(
                     Path.Combine(BlogSettings.OutputWebRootPath, "post"),
                     posts.Select(static p => p.OutputPath)))
        {
            string reason = draftPostOutputPaths.Contains(Path.GetFullPath(removed))
                ? "unpublished"
                : "orphaned (no matching entry -- renamed or source deleted)";
            Console.WriteLine($"Removed {reason} output: {removed}");
        }

        // Pages share OutputWebRootPath with hand-maintained files such as 404.html, so they get a
        // targeted delete by expected path rather than a sweep.
        foreach (PageMetaData draftPage in allPages.Where(static p => p.Draft))
        {
            string draftPagePath = PageOutputPath(draftPage);
            if (File.Exists(draftPagePath))
            {
                File.Delete(draftPagePath);
                Console.WriteLine($"Removed unpublished page output: {draftPagePath}");
            }
        }

        // Music, show, and gallery output roots are deliberately NOT swept -- unlike posts and pages,
        // those content paths can be unconfigured, and pointing a delete sweep at a root nothing
        // populates would treat everything under it as an orphan. So a draft of one of these types
        // leaves its previously published output reachable at its old URL; this is documented (core
        // ReadMe, "Drafts") as the consuming site's responsibility to clean up. Diagnostic only: warn,
        // never delete.
        foreach (MusicMetaData draftMusic in allMusic.Where(static m => m.Draft))
        {
            if (File.Exists(draftMusic.OutputPath))
            {
                Console.WriteLine(
                    $"Warning: music entry '{draftMusic.Title}' is marked draft, but its previously " +
                    $"published output still exists at '{draftMusic.OutputPath}'. It is not swept " +
                    "automatically -- remove it by hand.");
            }
        }
        foreach (ShowMetaData draftShow in allShows.Where(static s => s.Draft))
        {
            if (File.Exists(draftShow.OutputPath))
            {
                Console.WriteLine(
                    $"Warning: show entry '{draftShow.Title}' is marked draft, but its previously " +
                    $"published output still exists at '{draftShow.OutputPath}'. It is not swept " +
                    "automatically -- remove it by hand.");
            }
        }
        foreach (GalleryMetaData draftGallery in allGalleries.Where(static g => g.Draft))
        {
            if (File.Exists(draftGallery.OutputPath))
            {
                Console.WriteLine(
                    $"Warning: gallery entry '{draftGallery.Title}' is marked draft, but its previously " +
                    $"published output still exists at '{draftGallery.OutputPath}'. It is not swept " +
                    "automatically -- remove it by hand.");
            }
        }

        // RSS feed — posts merged with music + shows, sorted newest-first.
        string rssXml = await RssFeedGenerator.GenerateRssFeed(posts, MusicEntries, ShowEntries);
        string rssFilePath = Path.Combine(BlogSettings.OutputWebRootPath, "feed.xml");
        await File.WriteAllTextAsync(rssFilePath, rssXml);

        // sitemap.xml — covers pages + posts + all typed collections, regenerated on every build so the
        // sitemap stays in lockstep with the deployed wwwroot regardless of which content changed.
        string sitemapXml = SitemapGenerator.GenerateSitemap(pages, posts, MusicEntries, ShowEntries, GalleryEntries);
        string sitemapFilePath = Path.Combine(BlogSettings.OutputWebRootPath, "sitemap.xml");
        await File.WriteAllTextAsync(sitemapFilePath, sitemapXml);
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

    /// <summary>Maps a page's URL to its output filename. An empty or root URL is the site index.</summary>
    private static string PageOutputPath(PageMetaData page)
    {
        string fileName = Path.GetFileNameWithoutExtension(page.Url);
        if (string.IsNullOrWhiteSpace(fileName) || fileName == Path.DirectorySeparatorChar.ToString())
        {
            fileName = "index";
        }
        return Path.Combine(BlogSettings!.OutputWebRootPath, $"{fileName}.html");
    }
}
