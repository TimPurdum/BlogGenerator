using System.Text;
using System.Text.RegularExpressions;
using Markdig.Parsers;
using Markdig.Syntax;
using Microsoft.AspNetCore.Components;
using TimPurdum.Dev.BlogGenerator.Shared;

namespace TimPurdum.Dev.BlogGenerator.Compiler;

public static class MarkupParser
{
    public static List<PostMetaData> GeneratePostMetaDatas()
    {
        string[] posts = Directory.GetFiles(Generator.BlogSettings!.PostsContentPath, "*.md", SearchOption.AllDirectories);
        List<PostMetaData> postMetaDatas = [];
        foreach (string post in posts)
        {
            string fileName = Path.GetFileNameWithoutExtension(post);
            if (Resources.PostNameRegex.Match(fileName) is not { Success: true } match)
            {
                Console.WriteLine($"Skipping post {fileName} due to invalid name format.");
                continue; // Skip posts with invalid names
            }
            fileName = match.Groups["fileName"].Value;
            DateTime publishedDate = new DateTime(
                int.Parse(match.Groups[1].Value),
                int.Parse(match.Groups[2].Value),
                int.Parse(match.Groups[3].Value));
            string content = File.ReadAllText(post);

            try
            {
                // Parse the content and generate PostMetaData
                PostMetaData postMetaData = GeneratePostMetaData(fileName, content, publishedDate);
                postMetaDatas.Add(postMetaData);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error generating Razor content for post {fileName}: {ex.Message}");
            }
        }

        return postMetaDatas;
    }

    private static PostMetaData GeneratePostMetaData(string fileName, string content, DateTime publishedDate)
    {
        // Extract YAML front-matter using regex
        Match match = Resources.FileRegex.Match(content);
        if (!match.Success) throw new ArgumentException("Content does not contain valid YAML front-matter.");

        string yaml = match.Groups[1].Value;
        string markdownContent = match.Groups[2].Value;
        List<string> markdownLines = markdownContent.Split(Environment.NewLine).ToList();
        List<string> resultLines = [];
        bool inSampleCodeBlock = false;
        bool inRazorComponentCodeBlock = false;
        int razorCodeBlockIndex = 1;
        string? razorCodeBlockCurrentKey = null;
        StringBuilder? razorCodeBlockContent = null;
        Dictionary<string, string> razorComponentSections = [];
        List<string> scripts = [];
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
                        if (string.IsNullOrWhiteSpace(razorCodeBlockCurrentKey))
                        {
                            inRazorComponentCodeBlock = false;
                            continue;
                        }
                        // Store the code block content for the Razor component
                        razorComponentSections[razorCodeBlockCurrentKey] = razorCodeBlockContent!.ToString();
                        razorCodeBlockContent.Clear();
                        resultLines.Add(GenerateLoadingDiv(razorCodeBlockCurrentKey));
                        razorCodeBlockCurrentKey = string.Empty;
                        continue;
                    }
                }

                if (inRazorComponentCodeBlock)
                {
                    // Collect the content of the Razor component code block
                    razorCodeBlockContent!.AppendLine(line);
                    continue;
                }
            }
            else if (line.StartsWith("```") || line.StartsWith("~~~"))
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
            }
            else if (inScriptBlock)
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
            else if (ScriptStartRegex.Match(line) is { Success: true } scriptStartMatch)
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
        string postContent = document.ToRazor();

        Dictionary<string, string> yamlData = GetYamlData(yaml);

        string urlPath = $"/post/{publishedDate.Year}/{publishedDate.Month}/{publishedDate.Day}/{fileName}";

        string title = yamlData.GetValueOrDefault("title", "Untitled").Trim('"');
        string subTitle = yamlData.GetValueOrDefault("subtitle", string.Empty).Trim('"');
        string authorName = yamlData.GetValueOrDefault("author", string.Empty).Trim('"');
        string layout = yamlData.GetValueOrDefault("layout", "post").Trim('"');
        layout = $"{layout.ToUpperFirstChar()}Layout";
        string description = yamlData.GetValueOrDefault("description", string.Empty).Trim('"');

        return new PostMetaData(title, subTitle, urlPath, publishedDate, authorName, postContent, 
            razorComponentSections, scripts, layout, description);
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
                    pageMetaData = GeneratePageMetaDataFromMarkdown(fileName, content);    
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

    private static PageMetaData GeneratePageMetaDataFromMarkdown(string fileName, string content)
    {
        // Extract YAML front-matter using regex
        Match match = Resources.FileRegex.Match(content);
        if (!match.Success) throw new ArgumentException("Content does not contain valid YAML front-matter.");
        string yaml = match.Groups[1].Value;
        string markdownContent = match.Groups[2].Value;
        List<string> markdownLines = markdownContent.Split(Environment.NewLine).ToList();
        List<string> resultLines = [];
        bool inSampleCodeBlock = false;
        bool inRazorComponentCodeBlock = false;
        int razorCodeBlockIndex = 1;
        string? razorCodeBlockCurrentKey = null;
        StringBuilder? razorCodeBlockContent = null;
        Dictionary<string, string> razorComponentSections = [];
        List<string> scripts = [];
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
                        // Store the code block content for the Razor component
                        razorComponentSections[razorCodeBlockCurrentKey!] = razorCodeBlockContent!.ToString();
                        razorCodeBlockContent.Clear();
                        resultLines.Add(GenerateLoadingDiv(razorCodeBlockCurrentKey!));
                        razorCodeBlockCurrentKey = string.Empty;
                        continue;
                    }
                }

                if (inRazorComponentCodeBlock)
                {
                    // Collect the content of the Razor component code block
                    razorCodeBlockContent!.AppendLine(line);
                    continue;
                }
            }
            else if (line.StartsWith("```") || line.StartsWith("~~~"))
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
            }
            else if (inScriptBlock)
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
            else if (ScriptStartRegex.Match(line) is { Success: true } scriptStartMatch)
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
        string postContent = document.ToRazor();

        Dictionary<string, string> yamlData = GetYamlData(yaml);
        string urlPath = fileName == "index" ? "/" : fileName;

        string title = yamlData.GetValueOrDefault("title", "Untitled").Trim('"');
        string navOrderString = yamlData.GetValueOrDefault("navorder", "0").Trim('"');
        int navOrder = int.TryParse(navOrderString, out var order) ? order : 0;
        string subTitle = yamlData.GetValueOrDefault("subtitle", string.Empty).Trim('"');
        string layout = yamlData.GetValueOrDefault("layout", "page").Trim('"');
        layout = $"{layout.ToUpperFirstChar()}Layout";
        string description = yamlData.GetValueOrDefault("description", string.Empty).Trim('"');
        
        return new PageMetaData(title, subTitle, urlPath, postContent, razorComponentSections, scripts, layout, 
            description, navOrder);
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
            { nameof(BlogSettings.SiteDescription), (MarkupString)Generator.BlogSettings.SiteDescription }
        };

        string htmlContent = await Generator.RenderComponent(parameters);
        
        return new PageMetaData(fileName, string.Empty, urlPath, htmlContent, 
            razorComponentSections, scripts, "PageLayout", string.Empty, 0);
    }

    private static Dictionary<string, string> GetYamlData(string yaml)
    {
        Dictionary<string, string> yamlData = new Dictionary<string, string>();
        // Parse YAML front-matter into a dictionary
        foreach (string line in yaml.Split(['\n', '\r'], StringSplitOptions.RemoveEmptyEntries))
        {
            string[] keyValue = line.Split([':'], 2);
            if (keyValue.Length == 2)
            {
                string key = keyValue[0].Trim();
                string value = keyValue[1].Trim();
                yamlData[key] = value;
            }
        }

        return yamlData;
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
}