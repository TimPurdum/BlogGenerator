namespace TimPurdum.Dev.BlogGenerator.Shared;

public class BlogSettings
{
    public string SiteName { get; init; } = "MyBlazorBlog";
    public string SiteTitle { get; init; } = "A Blazor Blog";
    public string SiteUrl { get; init; } = "https://www.example.com";
    public string AssemblyNamespace { get; init; } = "My.Blazor.Blog";
    public string SiteDescription { get; init; } = "A blog about software development and technology.";
    public string[] HeaderLinks { get; init; } = [];
    public string PostsContentPath { get; set; } = "Source/Content/Posts";
    public string PagesContentPath { get; set; } = "Source/Content/Pages";
    public string OutputWebRootPath { get; set; } = "wwwroot";
    public string OutputComponentsPath { get; set; } = "Components";
    public string SourceTemplatesPath { get; set; } = "Source/Templates";
    public string? BlogRootPath { get; set; }
    public string? AssemblyOutputPath { get; set; }
}