using System.Text;
using System.Text.RegularExpressions;
using Markdig.Parsers;
using Markdig.Syntax;
using Microsoft.AspNetCore.Components;
using TimPurdum.Dev.BlogGenerator.Shared;
using TimPurdum.Dev.BlogGenerator.Shared.AbstractTemplates;

namespace TimPurdum.Dev.BlogGenerator.Compiler;

public static class MarkupParser
{
    public static List<PostMetaData> GeneratePostMetaDatas()
    {
        string[] posts = Directory.GetFiles(Generator.BlogSettings!.PostsContentPath, "*.md", SearchOption.AllDirectories);
        List<PostMetaData> postMetaDatas = [];
        foreach (string post in posts)
        {
            PostMetaData? postMetaData = GeneratePostMetaData(post);
            if (postMetaData is not null)
            {
                postMetaDatas.Add(postMetaData);
            }
        }

        return postMetaDatas;
    }

    private static PostMetaData? GeneratePostMetaData(string post)
    {
        string fileName = Path.GetFileNameWithoutExtension(post);
            if (Resources.PostNameRegex.Match(fileName) is not { Success: true } match)
            {
                Console.WriteLine($"Skipping post {fileName} due to invalid name format.");
                return null; // Skip posts with invalid names
            }
            fileName = match.Groups["fileName"].Value;
            DateTime publishedDate = new DateTime(
                int.Parse(match.Groups[1].Value),
                int.Parse(match.Groups[2].Value),
                int.Parse(match.Groups[3].Value));
            string content = File.ReadAllText(post);

            try
            {
                DateTime fileLastModified = File.GetLastWriteTimeUtc(post);
                // Extract YAML front-matter using regex
                Match yamlMatch = Resources.FileRegex.Match(content);
                if (!yamlMatch.Success) throw new ArgumentException("Content does not contain valid YAML front-matter.");

                string yaml = yamlMatch.Groups[1].Value;
                string markdownContent = yamlMatch.Groups[2].Value;
                FrontMatter frontMatter = FrontMatter.Parse(yaml);
                DateTime? lastModified = frontMatter.GetDateTime("lastmodified");
                if (lastModified.HasValue && lastModified.Value > fileLastModified)
                {
                    fileLastModified = lastModified.Value;
                }
                
                string outputFolder = Path.Combine(
                    Generator.BlogSettings!.OutputWebRootPath,
                    "post",
                    publishedDate.Year.ToString(),
                    publishedDate.Month.ToString(),
                    publishedDate.Day.ToString());
                Directory.CreateDirectory(outputFolder);
                string outFilePath = Path.Combine(outputFolder, $"{fileName}.html");
                DateTime outFileLastModified = File.Exists(outFilePath)
                    ? File.GetLastWriteTimeUtc(outFilePath)
                    : DateTime.MinValue;

                // if the output file is newer than both the content file and the last modified date, skip processing
                bool update = !(outFileLastModified >= fileLastModified && outFileLastModified >= publishedDate);
                
                List<string> markdownLines = markdownContent.Split(Environment.NewLine).ToList();
                List<string> resultLines = [];
                Dictionary<string, string> razorComponentSections = [];
                List<string> scripts = [];
        
                string postContent = ParseMarkdownLines(markdownLines, ref resultLines, 
                    ref razorComponentSections, ref scripts);

                string urlPath = $"/post/{publishedDate.Year}/{publishedDate.Month}/{publishedDate.Day}/{fileName}";

                string title = frontMatter.GetString("title", "Untitled");
                string subTitle = frontMatter.GetString("subtitle");
                string authorName = frontMatter.GetString("author");
                string layout = frontMatter.GetString("layout", "post");
                layout = $"{layout.ToUpperFirstChar()}Layout";
                string description = frontMatter.GetString("description");

                if (update)
                {
                    // add or update the lastmodified in the yaml data
                    frontMatter.Set("lastmodified", DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss"));

                    // rewrite the yaml front-matter with the updated lastmodified date
                    StringBuilder newYamlBuilder = new StringBuilder();
                    newYamlBuilder.AppendLine("---");
                    newYamlBuilder.Append(frontMatter.Serialize());
                    newYamlBuilder.AppendLine("---");
                    newYamlBuilder.AppendLine(string.Join(Environment.NewLine, markdownLines));
                    File.WriteAllText(post, newYamlBuilder.ToString());
                }

                return new PostMetaData(title, subTitle, urlPath, publishedDate, authorName, postContent, 
                    razorComponentSections, scripts, layout, description, outFilePath, update);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error generating Razor content for post {fileName}: {ex.Message}");
            }

            return null;
    }

    public static List<MusicMetaData> GenerateMusicMetaDatas()
    {
        BlogSettings settings = Generator.BlogSettings!;
        if (string.IsNullOrWhiteSpace(settings.MusicContentPath) || !Directory.Exists(settings.MusicContentPath))
        {
            return [];
        }

        string[] files = Directory.GetFiles(settings.MusicContentPath, "*.md", SearchOption.AllDirectories);
        List<MusicMetaData> result = [];
        Dictionary<string, string> seenSlugs = new(StringComparer.OrdinalIgnoreCase);

        foreach (string file in files)
        {
            ParsedEntry? parsed = ParseEntryFile(
                file,
                Resources.PostNameRegex,
                m => new DateTime(int.Parse(m.Groups[1].Value), int.Parse(m.Groups[2].Value), int.Parse(m.Groups[3].Value)),
                (slug, _) => Path.Combine(settings.OutputWebRootPath, "music", $"{slug}.html"),
                "music");
            if (parsed is null) continue;

            CheckSlugCollision(parsed.Slug, file, seenSlugs, contentType: "music",
                urlExample: $"/music/{parsed.Slug}.html");

            string description = parsed.FrontMatter.GetString("description");
            Dictionary<string, object?> extra = new()
            {
                [nameof(BaseMusicLayout.Date)] = parsed.Date,
                [nameof(BaseMusicLayout.Type)] = parsed.FrontMatter.GetString("type"),
                [nameof(BaseMusicLayout.Ensemble)] = parsed.FrontMatter.GetString("ensemble"),
                [nameof(BaseMusicLayout.Role)] = parsed.FrontMatter.GetString("role"),
                [nameof(BaseMusicLayout.Venue)] = parsed.FrontMatter.GetString("venue"),
                [nameof(BaseMusicLayout.EmbedUrl)] = parsed.FrontMatter.GetString("embedUrl"),
                [nameof(BaseMusicLayout.CoverImage)] = parsed.FrontMatter.GetString("coverImage"),
                [nameof(BaseMusicLayout.Description)] = description
            };

            if (parsed.Update) RewriteFrontMatter(file, parsed.FrontMatter, parsed.MarkdownLines);

            result.Add(new MusicMetaData(
                parsed.FrontMatter.GetString("title", "Untitled"),
                parsed.FrontMatter.GetString("subtitle"),
                $"/music/{parsed.Slug}",
                parsed.Date,
                parsed.Content,
                parsed.RazorComponents,
                parsed.ScriptTags,
                $"{parsed.FrontMatter.GetString("layout", "music").ToUpperFirstChar()}Layout",
                description,
                parsed.OutputPath,
                parsed.Update,
                extra));
        }
        return result;
    }

    public static List<ShowMetaData> GenerateShowMetaDatas()
    {
        BlogSettings settings = Generator.BlogSettings!;
        if (string.IsNullOrWhiteSpace(settings.ShowsContentPath) || !Directory.Exists(settings.ShowsContentPath))
        {
            return [];
        }

        string[] files = Directory.GetFiles(settings.ShowsContentPath, "*.md", SearchOption.AllDirectories);
        List<ShowMetaData> result = [];

        foreach (string file in files)
        {
            ParsedEntry? parsed = ParseEntryFile(
                file,
                Resources.PostNameRegex,
                m => new DateTime(int.Parse(m.Groups[1].Value), int.Parse(m.Groups[2].Value), int.Parse(m.Groups[3].Value)),
                (slug, d) => Path.Combine(settings.OutputWebRootPath, "show", d.Year.ToString(), d.Month.ToString(), d.Day.ToString(), $"{slug}.html"),
                "show");
            if (parsed is null) continue;

            string description = parsed.FrontMatter.GetString("description");
            Dictionary<string, object?> extra = new()
            {
                [nameof(BaseShowLayout.PerformanceDate)] = parsed.Date,
                [nameof(BaseShowLayout.Time)] = parsed.FrontMatter.GetString("time"),
                [nameof(BaseShowLayout.Venue)] = parsed.FrontMatter.GetString("venue"),
                [nameof(BaseShowLayout.City)] = parsed.FrontMatter.GetString("city"),
                [nameof(BaseShowLayout.TicketUrl)] = parsed.FrontMatter.GetString("ticketUrl"),
                [nameof(BaseShowLayout.Program)] = parsed.FrontMatter.GetString("program"),
                [nameof(BaseShowLayout.Role)] = parsed.FrontMatter.GetString("role"),
                [nameof(BaseShowLayout.Description)] = description
            };

            if (parsed.Update) RewriteFrontMatter(file, parsed.FrontMatter, parsed.MarkdownLines);

            result.Add(new ShowMetaData(
                parsed.FrontMatter.GetString("title", "Untitled"),
                parsed.FrontMatter.GetString("subtitle"),
                $"/show/{parsed.Date.Year}/{parsed.Date.Month}/{parsed.Date.Day}/{parsed.Slug}",
                parsed.Date,
                parsed.Content,
                parsed.RazorComponents,
                parsed.ScriptTags,
                $"{parsed.FrontMatter.GetString("layout", "show").ToUpperFirstChar()}Layout",
                description,
                parsed.OutputPath,
                parsed.Update,
                extra));
        }
        return result;
    }

    public static List<GalleryMetaData> GenerateGalleryMetaDatas()
    {
        BlogSettings settings = Generator.BlogSettings!;
        if (string.IsNullOrWhiteSpace(settings.GalleryContentPath) || !Directory.Exists(settings.GalleryContentPath))
        {
            return [];
        }

        string[] files = Directory.GetFiles(settings.GalleryContentPath, "*.md", SearchOption.AllDirectories);
        List<GalleryMetaData> result = [];
        Dictionary<string, string> seenSlugs = new(StringComparer.OrdinalIgnoreCase);

        foreach (string file in files)
        {
            ParsedEntry? parsed = ParseEntryFile(
                file,
                Resources.GalleryNameRegex,
                m => new DateTime(int.Parse(m.Groups[1].Value), int.Parse(m.Groups[2].Value), 1),
                (slug, _) => Path.Combine(settings.OutputWebRootPath, "gallery", $"{slug}.html"),
                "gallery");
            if (parsed is null) continue;

            CheckSlugCollision(parsed.Slug, file, seenSlugs, contentType: "gallery",
                urlExample: $"/gallery/{parsed.Slug}.html");

            string description = parsed.FrontMatter.GetString("description");
            List<GalleryImage> images = parsed.FrontMatter.GetMappingList("images")
                .Select(d => new GalleryImage(
                    d.GetValueOrDefault("src", string.Empty),
                    d.GetValueOrDefault("caption", string.Empty)))
                .ToList();

            Dictionary<string, object?> extra = new()
            {
                [nameof(BaseGalleryLayout.Date)] = parsed.Date,
                [nameof(BaseGalleryLayout.Images)] = images,
                [nameof(BaseGalleryLayout.Description)] = description
            };

            if (parsed.Update) RewriteFrontMatter(file, parsed.FrontMatter, parsed.MarkdownLines);

            result.Add(new GalleryMetaData(
                parsed.FrontMatter.GetString("title", "Untitled"),
                parsed.FrontMatter.GetString("subtitle"),
                $"/gallery/{parsed.Slug}",
                parsed.Date,
                parsed.Content,
                parsed.RazorComponents,
                parsed.ScriptTags,
                $"{parsed.FrontMatter.GetString("layout", "gallery").ToUpperFirstChar()}Layout",
                description,
                parsed.OutputPath,
                parsed.Update,
                images,
                extra));
        }
        return result;
    }

    /// <summary>Common parse output: filename slug, date, frontmatter, rendered markdown, scripts, update flag.</summary>
    private record ParsedEntry(
        string Slug,
        DateTime Date,
        FrontMatter FrontMatter,
        string Content,
        Dictionary<string, string> RazorComponents,
        List<string> ScriptTags,
        string OutputPath,
        bool Update,
        List<string> MarkdownLines);

    /// <summary>
    /// Reads, regex-matches, frontmatter-parses, lastmodified-merges, and markdown-renders an entry file.
    /// On any parse error, logs and returns null (matches <see cref="GeneratePostMetaData"/> behavior).
    /// Does not perform slug uniqueness — call <see cref="CheckSlugCollision"/> from the typed parser when needed.
    /// </summary>
    private static ParsedEntry? ParseEntryFile(
        string file,
        Regex nameRegex,
        Func<Match, DateTime> dateFromMatch,
        Func<string, DateTime, string> outputPathResolver,
        string entryTypeForLogging)
    {
        string fileName = Path.GetFileNameWithoutExtension(file);
        if (nameRegex.Match(fileName) is not { Success: true } match)
        {
            Console.WriteLine($"Skipping {entryTypeForLogging} entry {fileName} due to invalid name format.");
            return null;
        }

        string slug = match.Groups["fileName"].Value;
        DateTime date;
        try
        {
            date = dateFromMatch(match);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error parsing date for {entryTypeForLogging} entry {fileName}: {ex.Message}");
            return null;
        }

        try
        {
            string fileContent = File.ReadAllText(file);
            Match yamlMatch = Resources.FileRegex.Match(fileContent);
            if (!yamlMatch.Success)
            {
                Console.WriteLine($"{entryTypeForLogging} entry {fileName}: missing YAML frontmatter.");
                return null;
            }

            FrontMatter frontMatter = FrontMatter.Parse(yamlMatch.Groups[1].Value);
            string markdownContent = yamlMatch.Groups[2].Value;

            DateTime fileLastModified = File.GetLastWriteTimeUtc(file);
            DateTime? lastModified = frontMatter.GetDateTime("lastmodified");
            if (lastModified.HasValue && lastModified.Value > fileLastModified)
            {
                fileLastModified = lastModified.Value;
            }

            string outputPath = outputPathResolver(slug, date);
            Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
            DateTime outFileLastModified = File.Exists(outputPath)
                ? File.GetLastWriteTimeUtc(outputPath)
                : DateTime.MinValue;
            bool update = !(outFileLastModified >= fileLastModified && outFileLastModified >= date);

            List<string> markdownLines = markdownContent.Split(Environment.NewLine).ToList();
            List<string> resultLines = [];
            Dictionary<string, string> razorComponents = [];
            List<string> scripts = [];
            string content = ParseMarkdownLines(markdownLines, ref resultLines, ref razorComponents, ref scripts);

            return new ParsedEntry(slug, date, frontMatter, content, razorComponents, scripts,
                outputPath, update, markdownLines);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error generating {entryTypeForLogging} entry {fileName}: {ex.Message}");
            return null;
        }
    }

    /// <summary>Throws on duplicate slug for flat-URL collections (D11). Aborts the build with a message naming both files.</summary>
    private static void CheckSlugCollision(string slug, string file, Dictionary<string, string> seen,
        string contentType, string urlExample)
    {
        if (seen.TryGetValue(slug, out string? previousFile))
        {
            throw new InvalidOperationException(
                $"Duplicate {contentType} slug '{slug}'. Conflicting files: '{previousFile}' and '{file}'. " +
                $"{contentType} URLs are flat ('{urlExample}'); rename one of the files.");
        }
        seen[slug] = file;
    }

    private static void RewriteFrontMatter(string file, FrontMatter frontMatter, List<string> markdownLines)
    {
        frontMatter.Set("lastmodified", DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss"));

        StringBuilder newYamlBuilder = new();
        newYamlBuilder.AppendLine("---");
        newYamlBuilder.Append(frontMatter.Serialize());
        newYamlBuilder.AppendLine("---");
        newYamlBuilder.AppendLine(string.Join(Environment.NewLine, markdownLines));
        File.WriteAllText(file, newYamlBuilder.ToString());
    }

    public static async Task<List<PageMetaData>> GeneratePageMetaDatas(List<LinkData> navLinks)
    {
        string[] pages = Directory.GetFiles(Generator.BlogSettings!.PagesContentPath, "*.md", SearchOption.AllDirectories)
            .Concat(Directory.GetFiles(Generator.BlogSettings.PagesContentPath, "*.razor", SearchOption.AllDirectories))
            .ToArray();
        List<PageMetaData> pageMetaDatas = [];
        foreach (string page in pages)
        {
            bool isRazorComponent = page.EndsWith(".razor", StringComparison.OrdinalIgnoreCase);
            string fileName = Path.GetFileNameWithoutExtension(page);
            string content = await File.ReadAllTextAsync(page);

            try
            {
                PageMetaData pageMetaData;
                if (isRazorComponent)
                {
                    pageMetaData = await GeneratePageMetaDataFromRazorComponent(fileName, content, navLinks);
                }
                else
                {
                    pageMetaData = GeneratePageMetaDataFromMarkdown(fileName, content, page);
                }
                
                pageMetaDatas.Add(pageMetaData);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error generating Razor content for page {fileName}: {ex.Message}");
            }
        }

        return pageMetaDatas;
    }

    private static PageMetaData GeneratePageMetaDataFromMarkdown(string fileName, string content, string filePath)
    {
        // Extract YAML front-matter using regex
        Match match = Resources.FileRegex.Match(content);
        if (!match.Success) throw new ArgumentException("Content does not contain valid YAML front-matter.");
        string yaml = match.Groups[1].Value;
        string markdownContent = match.Groups[2].Value;
        List<string> markdownLines = markdownContent.Split(Environment.NewLine).ToList();
        List<string> resultLines = [];
        Dictionary<string, string> razorComponentSections = [];
        List<string> scripts = [];

        string postContent = ParseMarkdownLines(markdownLines, ref resultLines,
            ref razorComponentSections, ref scripts);

        FrontMatter frontMatter = FrontMatter.Parse(yaml);
        string urlPath = fileName == "index" ? "/" : fileName;

        string title = frontMatter.GetString("title", "Untitled");
        int navOrder = int.TryParse(frontMatter.GetString("navorder", "0"), out var order) ? order : 0;
        string subTitle = frontMatter.GetString("subtitle");
        string layout = frontMatter.GetString("layout", "page");
        layout = $"{layout.ToUpperFirstChar()}Layout";
        string description = frontMatter.GetString("description");

        // Forward any non-standard frontmatter fields as ExtraFrontMatter so custom layouts can declare
        // typed [Parameter] props for arbitrary keys (e.g. heroImage on the home page).
        Dictionary<string, string> extras = new(StringComparer.OrdinalIgnoreCase);
        foreach (string key in frontMatter.Keys)
        {
            if (StandardPageFrontMatterKeys.Contains(key)) continue;
            extras[key] = frontMatter.GetString(key);
        }

        // Prefer the page's own `lastmodified` frontmatter (admin keeps it current on every save);
        // fall back to the source file's mtime so freshly-created pages still get a sitemap date.
        // If the source file is missing, keep this null so sitemap generation can omit `<lastmod>`.
        DateTime? lastModified = frontMatter.GetDateTime("lastmodified")
            ?? (File.Exists(filePath) ? File.GetLastWriteTimeUtc(filePath) : null);

        return new PageMetaData(title, subTitle, urlPath, postContent,
            razorComponentSections, scripts, layout, description, navOrder,
            extras.Count > 0 ? extras : null,
            lastModified);
    }

    /// <summary>Keys that already flow through dedicated PageMetaData fields and shouldn't be re-emitted as extras.</summary>
    private static readonly HashSet<string> StandardPageFrontMatterKeys =
        new(StringComparer.OrdinalIgnoreCase) { "title", "subtitle", "description", "layout", "navorder", "lastmodified" };
    
    private static string ParseMarkdownLines(List<string> markdownLines,
        ref List<string> resultLines,
        ref Dictionary<string, string> razorComponentSections, 
        ref List<string> scripts)
    {
        bool inSampleCodeBlock = false;
        bool inRazorComponentCodeBlock = false;
        int razorCodeBlockIndex = 1;
        string? razorCodeBlockCurrentKey = null;
        StringBuilder? razorCodeBlockContent = null;
        int codeBlockIndex = 1;
        string? currentCodeBlockLanguage = null;
        string? codeBlockCurrentKey = null;
        StringBuilder? codeBlockContent = null;
        StringBuilder? currentScriptBuilder = null;
        bool inScriptBlock = false;
        
        foreach (var line in markdownLines)
        {
            if (inSampleCodeBlock)
            {
                if (line.StartsWith("```") || line.StartsWith("~~~"))
                {
                    inSampleCodeBlock = false; // End of code block
                    if (inRazorComponentCodeBlock)
                    {
                        inRazorComponentCodeBlock = false;
                        if (string.IsNullOrWhiteSpace(razorCodeBlockCurrentKey))
                        {
                            continue;
                        }
                        // Store the code block content for the Razor component
                        razorComponentSections[razorCodeBlockCurrentKey] = razorCodeBlockContent!.ToString();
                        razorCodeBlockContent.Clear();
                        resultLines.Add(GenerateLoadingDiv(razorCodeBlockCurrentKey));
                        razorCodeBlockCurrentKey = string.Empty;
                        continue;
                    }
                    
                    if (string.IsNullOrEmpty(codeBlockCurrentKey))
                    {
                        continue;
                    }
                    // Store the code block content for regular code blocks
                    int lineCount = codeBlockContent!.ToString().Split(Environment.NewLine).Length;
                    resultLines.Add($"<div id=\"{codeBlockCurrentKey}\" class=\"monaco-editor-block\" style=\"height: {lineCount + 2}rem;\"></div>");
                    scripts.Add(GenerateMonacoScript(codeBlockCurrentKey, codeBlockContent.ToString(), currentCodeBlockLanguage!));
                    codeBlockContent.Clear();
                    codeBlockCurrentKey = string.Empty;
                    currentCodeBlockLanguage = null;
                    continue;
                }

                if (inRazorComponentCodeBlock)
                {
                    // Collect the content of the Razor component code block
                    razorCodeBlockContent!.AppendLine(line);
                    continue;
                }
                
                // Collect the content of the regular code block
                codeBlockContent!.AppendLine(line);
                continue;
            }

            if (line.StartsWith("```") || line.StartsWith("~~~"))
            {
                inSampleCodeBlock = true; // Start of code block
                if (line.Length > 3 && line.Substring(3).StartsWith("blazor-component"))
                {
                    // This is a Blazor component code block
                    inRazorComponentCodeBlock = true;
                    razorCodeBlockCurrentKey = line.Length > 19 && line.Split(' ').Length > 1
                        ? $"{line.Split(' ')[1]}{razorCodeBlockIndex++}"
                        : $"code-block{razorCodeBlockIndex++}";
                    razorCodeBlockContent = new StringBuilder();
                    continue;
                }
                // This is a regular code block
                codeBlockCurrentKey = $"code-block{codeBlockIndex++}";
                currentCodeBlockLanguage = line.Length > 3 ? line.Substring(3) : "plaintext";
                codeBlockContent = new StringBuilder();
                continue;
            }

            if (inScriptBlock)
            {
                if (ScriptEndRegex.Match(line) is { Success: true })
                {
                    // This is the end of a script block
                    inScriptBlock = false;
                    currentScriptBuilder!.AppendLine(line);
                    scripts.Add(currentScriptBuilder.ToString());
                    currentScriptBuilder = null;
                }
                else
                {
                    // Continue collecting lines for the script block
                    currentScriptBuilder!.AppendLine(line);
                }

                continue;
            }

            if (ScriptStartRegex.Match(line) is { Success: true } scriptStartMatch)
            {
                if (!scriptStartMatch.Groups["scriptEnd"].Success)
                {
                    inScriptBlock = true;
                    currentScriptBuilder = new StringBuilder(line);
                    // This script block continues, so we will collect lines until we find the end
                }
                else
                {
                    // This script block ends immediately, so we can process it right away
                    scripts.Add(line);
                    inScriptBlock = false;
                }
                
                continue;
            }

            // Collect the content of the sample code block
            resultLines.Add(line);
        }

        MarkdownDocument document = MarkdownParser.Parse(string.Join(Environment.NewLine, resultLines), Resources.Pipeline);
        return document.ToRazor();
    }
    
    private static async Task<PageMetaData> GeneratePageMetaDataFromRazorComponent(string fileName, string content,
        List<LinkData> navLinks)
    {
        // Extract the @page directive to get the URL path
        Match match = PagePathRegex.Match(content);
        if (!match.Success)
        {
            throw new ArgumentException("Razor component does not contain a valid @page directive.");
        }
        string urlPath = match.Groups["path"].Value.Trim('"');
        
        Match pageTitleMatch = PageTitleRegex.Match(content);
        string title = pageTitleMatch.Success
            ? pageTitleMatch.Groups["title"].Value.Trim()
            : fileName.PascalToTitleCase();

        // Substitute any @BlogSettings.<Property> tokens with their values. Note the parser runs
        // BEFORE Razor compilation, so this isn't a real expression evaluator — anything outside the
        // BlogSettings prefix (page-local fields, navlinks, computed properties) is left untouched
        // and will render literally. Authors needing dynamic titles should keep PageTitle a literal
        // string or stick to BlogSettings.* references.
        title = SubstituteBlogSettingsTokens(title);
        
        List<string> razorLines = content.Split(Environment.NewLine).ToList();
        List<string> resultLines = [];
        bool inRazorComponentCodeBlock = false;
        int razorCodeBlockIndex = 1;
        string? razorCodeBlockCurrentKey = null;
        string? currentComponentName = null;
        StringBuilder? razorCodeBlockContent = null;
        Dictionary<string, string> razorComponentSections = [];
        List<string> scripts = [];
        StringBuilder? currentScriptBuilder = null;
        bool inScriptBlock = false;
        
        foreach (var line in razorLines)
        {
            if (inRazorComponentCodeBlock)
            {
                razorCodeBlockContent!.AppendLine(line);
                if (ComponentEndRegex.Match(line) is { Success: true } componentEndMatch)
                {
                    // This is the end of a Razor component
                    inRazorComponentCodeBlock = false;
                    string componentName = componentEndMatch.Groups["componentName"].Value;
                    
                    // skip if names don't match, could be nested component
                    if (componentName == currentComponentName) 
                    {
                        razorComponentSections[razorCodeBlockCurrentKey!] = razorCodeBlockContent.ToString();
                        razorCodeBlockContent.Clear();
                        currentComponentName = null;
                        resultLines.Add(GenerateLoadingDiv(razorCodeBlockCurrentKey!));
                        razorCodeBlockCurrentKey = string.Empty;
                    }
                }

                continue;
            }
            
            if (ComponentStartRegex.Match(line) is { Success: true } componentStartMatch)
            {
                string componentName = componentStartMatch.Groups["componentName"].Value;
                if (componentName != "PageTitle" && componentName != "NavMenu")
                {
                    // This is a Blazor component code block
                    razorCodeBlockCurrentKey = $"{componentName.PascalToKebabCase()}{razorCodeBlockIndex++}";
                    razorCodeBlockContent = new StringBuilder();
                    razorCodeBlockContent.AppendLine(line);
                    if (line.EndsWith("/>") || ComponentEndRegex.Match(line) is { Success: true })
                    {
                        // single-line, self-closing component
                        razorComponentSections[razorCodeBlockCurrentKey] = razorCodeBlockContent.ToString();
                        razorCodeBlockContent.Clear();
                        currentComponentName = null;
                        resultLines.Add(GenerateLoadingDiv(razorCodeBlockCurrentKey));
                        razorCodeBlockCurrentKey = string.Empty;
                    }
                    else
                    {
                        // this is a multi-line component block
                        inRazorComponentCodeBlock = true; // Start of code block   
                    }
                }
                
                continue;
            }

            if (inScriptBlock)
            {
                if (ScriptEndRegex.Match(line) is { Success: true })
                {
                    // This is the end of a script block
                    inScriptBlock = false;
                    currentScriptBuilder!.AppendLine(line);
                    currentScriptBuilder.AppendLine();
                    scripts.Add(currentScriptBuilder.ToString());
                    currentScriptBuilder = null;
                }
                else
                {
                    // Continue collecting lines for the script block
                    currentScriptBuilder!.AppendLine(line);
                }

                continue;
            }
            
            if (ScriptStartRegex.Match(line) is { Success: true } scriptStartMatch)
            {
                if (!scriptStartMatch.Groups["scriptEnd"].Success)
                {
                    inScriptBlock = true;
                    currentScriptBuilder = new StringBuilder(line);
                    // This script block continues, so we will collect lines until we find the end
                }
                else
                {
                    // This script block ends immediately, so we can process it right away
                    scripts.Add(line);
                    inScriptBlock = false;
                }
                continue;
            }
            
            // Collect the content of the Razor component
            resultLines.Add(line);
        }
        
        // run the Razor content through the Razor rendering engine
        string razorContent = string.Join(Environment.NewLine, resultLines);
        Type? componentType = RazorGenerator.GenerateRazorTypeFromFile(fileName, razorContent);

        if (componentType == null)
        {
            throw new InvalidOperationException($"No component type found in assembly for {fileName}.");
        }
        
        Dictionary<string, object?> parameters = new()
        {
            { "ComponentType", componentType },
            { "Title", (MarkupString)title },
            { "NavLinks", navLinks },
            { "Url", urlPath },
            { nameof(BlogSettings.SiteName), Generator.BlogSettings!.SiteName },
            { nameof(BlogSettings.HeaderLinks), (MarkupString)string.Join(Environment.NewLine, Generator.BlogSettings.HeaderLinks) },
            { nameof(BlogSettings.SiteTitle), Generator.BlogSettings.SiteTitle },
            { nameof(BlogSettings.SiteDescription), (MarkupString)Generator.BlogSettings.SiteDescription },
            // Type-specific collections — MarkupComponent.OnParametersSet filters these to whatever the page declares.
            // TODO Phase 2: markdown-authored pages don't go through MarkupComponent today, so they can't access these collections.
            // If a markdown landing page ever needs typed access, plumb forwarding through RenderRoot or convert it to .razor.
            { nameof(MarkupComponent.MusicEntries), Generator.MusicEntries },
            { nameof(MarkupComponent.ShowEntries), Generator.ShowEntries },
            { nameof(MarkupComponent.GalleryEntries), Generator.GalleryEntries }
        };

        string htmlContent = await Generator.RenderComponent(parameters);
        
        return new PageMetaData(title, string.Empty, urlPath, htmlContent, 
            razorComponentSections, scripts, "PageLayout", 
            string.Empty, 0);
    }

    private static string GenerateLoadingDiv(string codeBlockKey)
    {
        return $"""

                <div id="{codeBlockKey}" class="component-block">
                    <svg class="loading-progress">
                        <circle r="40%" cx="50%" cy="50%" />
                        <circle r="40%" cx="50%" cy="50%" />
                    </svg>
                    <div class="loading-progress-text"></div>
                </div>

                """;
    }
    
    private static string GenerateMonacoScript(string codeBlockKey, string codeBlockContent, string language)
    {
        return $$"""
                 
                 <script>
                    require(['vs/editor/editor.main'], function () {
                 	    var editor = monaco.editor.create(document.getElementById('{{codeBlockKey}}'), {
                 		    value: `{{codeBlockContent.Replace("`", "\\`")}}`,
                 	        automaticLayout: true,
                 		    language: '{{language}}'
                 	    });
                 	});
                 </script>
                 
                 """;
    }
    
    /// <summary>
    /// Replaces any <c>@BlogSettings.Property</c> token inside <paramref name="text"/> with the
    /// corresponding string property's value from the configured <see cref="BlogSettings"/>. Tokens
    /// that don't resolve to a public string property are left in place verbatim — they were not
    /// recognized, so the author probably wanted a literal or used an unsupported expression.
    /// </summary>
    /// <remarks>
    /// Handles both whole-expression titles (<c>&lt;PageTitle&gt;@BlogSettings.SiteTitle&lt;/PageTitle&gt;</c>)
    /// and compound forms (<c>&lt;PageTitle&gt;Welcome — @BlogSettings.SiteTitle&lt;/PageTitle&gt;</c>).
    /// Page-local fields, navlinks, and other non-BlogSettings expressions are NOT evaluated; the
    /// parser runs before Razor compilation, so it can't access page-instance state.
    /// When a matching <see cref="BlogSettings"/> string property exists but its value is <see langword="null"/>,
    /// the token is replaced with an empty string.
    /// </remarks>
    private static string SubstituteBlogSettingsTokens(string text)
    {
        BlogSettings? settings = Generator.BlogSettings;
        if (settings is null || string.IsNullOrEmpty(text)) return text;

        return BlogSettingsTokenRegex.Replace(text, match =>
        {
            string propName = match.Groups["prop"].Value;
            System.Reflection.PropertyInfo? prop = typeof(BlogSettings).GetProperty(propName);
            if (prop is not null && prop.PropertyType == typeof(string))
            {
                return (prop.GetValue(settings) as string) ?? string.Empty;
            }
            return match.Value;
        });
    }

    private static readonly Regex PagePathRegex = new(@"^@page ""(?<path>.+?)""",
        RegexOptions.Compiled | RegexOptions.Singleline);
    private static readonly Regex ComponentStartRegex = new(@"^\s*<(?<componentName>[A-Z][A-Za-z]+)",
        RegexOptions.Compiled | RegexOptions.Singleline);
    private static readonly Regex ComponentEndRegex = new(@"^\s*</(?<componentName>[A-Z][A-Za-z]+)>",
        RegexOptions.Compiled | RegexOptions.Singleline);
    private static readonly Regex PageTitleRegex = new("<PageTitle>(?<title>.*?)</PageTitle>",
        RegexOptions.Compiled | RegexOptions.Singleline);
    private static readonly Regex ScriptStartRegex = new("<script.*?>[^<]*?(?<scriptEnd></script>)?",
        RegexOptions.Compiled | RegexOptions.Singleline);
    private static readonly Regex ScriptEndRegex = new("</script>",
        RegexOptions.Compiled | RegexOptions.Singleline);
    private static readonly Regex BlogSettingsTokenRegex = new(@"@BlogSettings\.(?<prop>[A-Za-z][A-Za-z0-9]*)(?![.\(\[])",
        RegexOptions.Compiled);
}