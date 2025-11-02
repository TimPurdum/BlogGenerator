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
                Dictionary<string, string> yamlData = GetYamlData(yaml);
                DateTime? lastModified = yamlData.ContainsKey("lastmodified")
                    ? DateTime.Parse(yamlData["lastmodified"].Trim('"'))
                    : null;
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

                string title = yamlData.GetValueOrDefault("title", "Untitled").Trim('"');
                string subTitle = yamlData.GetValueOrDefault("subtitle", string.Empty).Trim('"');
                string authorName = yamlData.GetValueOrDefault("author", string.Empty).Trim('"');
                string layout = yamlData.GetValueOrDefault("layout", "post").Trim('"');
                layout = $"{layout.ToUpperFirstChar()}Layout";
                string description = yamlData.GetValueOrDefault("description", string.Empty).Trim('"');

                if (update)
                {
                    // add or update the lastmodified in the yaml data
                    yamlData["lastmodified"] = $"\"{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss}\"";
                
                    // rewrite the yaml front-matter with the updated lastmodified date
                    StringBuilder newYamlBuilder = new StringBuilder();
                    newYamlBuilder.AppendLine("---");
                    foreach (var kvp in yamlData)
                    {
                        newYamlBuilder.AppendLine($"{kvp.Key}: {kvp.Value}");
                    }
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
        Dictionary<string, string> razorComponentSections = [];
        List<string> scripts = [];
        
        string postContent = ParseMarkdownLines(markdownLines, ref resultLines, 
            ref razorComponentSections, ref scripts);

        Dictionary<string, string> yamlData = GetYamlData(yaml);
        string urlPath = fileName == "index" ? "/" : fileName;

        string title = yamlData.GetValueOrDefault("title", "Untitled").Trim('"');
        string navOrderString = yamlData.GetValueOrDefault("navorder", "0").Trim('"');
        int navOrder = int.TryParse(navOrderString, out var order) ? order : 0;
        string subTitle = yamlData.GetValueOrDefault("subtitle", string.Empty).Trim('"');
        string layout = yamlData.GetValueOrDefault("layout", "page").Trim('"');
        layout = $"{layout.ToUpperFirstChar()}Layout";
        string description = yamlData.GetValueOrDefault("description", string.Empty).Trim('"');
        
        return new PageMetaData(title, subTitle, urlPath, postContent, 
            razorComponentSections, scripts, layout, description, navOrder);
    }
    
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
                    resultLines.Add($"<div id=\"{codeBlockCurrentKey}\" class=\"monaco-editor-block\"></div>");
                    scripts.Add(GenerateMonacoScript(codeBlockCurrentKey, codeBlockContent!.ToString(), currentCodeBlockLanguage!));
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
        
        if (title == "@BlogSettings.SiteTitle")
        {
            title = Generator.BlogSettings!.SiteTitle;
        }
        
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
            { nameof(BlogSettings.SiteDescription), (MarkupString)Generator.BlogSettings.SiteDescription }
        };

        string htmlContent = await Generator.RenderComponent(parameters);
        
        return new PageMetaData(title, string.Empty, urlPath, htmlContent, 
            razorComponentSections, scripts, "PageLayout", 
            string.Empty, 0);
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