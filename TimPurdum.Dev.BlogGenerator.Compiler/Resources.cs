using System.Text.RegularExpressions;
using Markdig;

namespace TimPurdum.Dev.BlogGenerator.Compiler;

public static class Resources
{
    public static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder()
        .UseAdvancedExtensions()
        .UseYamlFrontMatter()
        .Build();
    public static readonly Regex FileRegex = new(@"^---\s*(.*?)\s*---\s*(.*)$", 
        RegexOptions.Compiled | RegexOptions.Singleline);
    public static readonly Regex PostNameRegex = new(@"^(\d{4})-(\d{1,2})-(\d{1,2})-(?<fileName>.+)$",
        RegexOptions.Compiled | RegexOptions.Singleline);
}