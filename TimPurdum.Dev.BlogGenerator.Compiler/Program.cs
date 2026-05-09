using System.Reflection;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;
using TimPurdum.Dev.BlogGenerator.Compiler;
using TimPurdum.Dev.BlogGenerator.Shared;

string? blogProjectFolderPath = null;
string? blogProjectName = null;

string? currentFolder = Assembly.GetExecutingAssembly().Location;
string oldFolder = currentFolder;

Regex webAssemblySdkRegex = new(@"<Project\s+Sdk=""Microsoft\.NET\.Sdk\.BlazorWebAssembly""\s*>", RegexOptions.Compiled);

while (blogProjectFolderPath is null)
{
    currentFolder = Directory.GetParent(currentFolder)?.FullName;
    if (currentFolder is null)
    {
        throw new Exception("Unable to find project folder");
    }
    string[] projectFiles = Directory.GetFiles(currentFolder, "*.csproj", SearchOption.AllDirectories);

    // Collect every Blazor WebAssembly project at this level — a repo may have more than one
    // (e.g. a public site plus a separate admin app), and the first match isn't necessarily the blog host.
    List<string> wasmProjects = projectFiles
        .Where(p => Path.GetDirectoryName(p) != oldFolder)
        .Where(p => File.ReadAllLines(p).Any(line => webAssemblySdkRegex.IsMatch(line)))
        .ToList();

    if (wasmProjects.Count == 0)
    {
        continue; // walk further up the tree
    }

    string? selected;
    if (wasmProjects.Count == 1)
    {
        selected = wasmProjects[0];
    }
    else
    {
        // Disambiguate: the blog project is the one with a wwwroot/appsettings.json that BlogGenerator
        // can read its BlogSettings from. Other Blazor WebAssembly projects in the same repo (admin
        // portals, satellite tools) won't have one.
        List<string> withConfig = wasmProjects
            .Where(p => File.Exists(Path.Combine(Path.GetDirectoryName(p)!, "wwwroot", "appsettings.json")))
            .ToList();
        if (withConfig.Count == 1)
        {
            selected = withConfig[0];
        }
        else
        {
            throw new InvalidOperationException(
                $"Multiple Blazor WebAssembly projects found and unable to choose one: {string.Join(", ", wasmProjects)}. " +
                "Place wwwroot/appsettings.json next to exactly one of them so BlogGenerator can identify the blog host.");
        }
    }

    blogProjectFolderPath = Path.GetDirectoryName(selected)!;
    blogProjectName = Path.GetFileNameWithoutExtension(selected);
}
if (blogProjectFolderPath is null)
{
    throw new InvalidOperationException("Could not find a Blazor WebAssembly project in the parent directories.");
}

Console.WriteLine($"Found Blazor WebAssembly project at: {blogProjectFolderPath}");

IServiceCollection services = new ServiceCollection();
services.AddLogging();
string appSettingsPath = Path.Combine(blogProjectFolderPath, "wwwroot", "appsettings.json");
IConfiguration configuration = new ConfigurationBuilder()
    .AddJsonFile(appSettingsPath, optional: false, reloadOnChange: true)
    .AddEnvironmentVariables()
    .Build();
services.AddSingleton(configuration);
services.AddOptions<BlogSettings>()
    .Configure<IConfiguration>((settings, config) =>
{
    config.GetSection("BlogSettings").Bind(settings);
});
services.AddSingleton<BlogSettings>(sp => sp.GetRequiredService<IOptions<BlogSettings>>().Value);
#pragma warning disable ASP0000
IServiceProvider serviceProvider = services.BuildServiceProvider();
#pragma warning restore ASP0000

BlogSettings blogSettings = serviceProvider.GetRequiredService<BlogSettings>();
blogSettings.OutputWebRootPath = Path.GetFullPath(
    Path.Combine(blogProjectFolderPath, blogSettings.OutputWebRootPath));
blogSettings.OutputComponentsPath = Path.GetFullPath(
    Path.Combine(blogProjectFolderPath, blogSettings.OutputComponentsPath));
string sourceProjectFolderPath = Path.GetDirectoryName(
    Path.GetFullPath(Path.Combine(blogProjectFolderPath, blogSettings.SourceProject)))!;
blogSettings.PagesContentPath = Path.GetFullPath(
    Path.Combine(sourceProjectFolderPath, blogSettings.PagesContentPath));
blogSettings.PostsContentPath = Path.GetFullPath(
    Path.Combine(sourceProjectFolderPath, blogSettings.PostsContentPath));
if (!string.IsNullOrWhiteSpace(blogSettings.MusicContentPath))
{
    blogSettings.MusicContentPath = Path.GetFullPath(
        Path.Combine(sourceProjectFolderPath, blogSettings.MusicContentPath));
}
if (!string.IsNullOrWhiteSpace(blogSettings.ShowsContentPath))
{
    blogSettings.ShowsContentPath = Path.GetFullPath(
        Path.Combine(sourceProjectFolderPath, blogSettings.ShowsContentPath));
}
if (!string.IsNullOrWhiteSpace(blogSettings.GalleryContentPath))
{
    blogSettings.GalleryContentPath = Path.GetFullPath(
        Path.Combine(sourceProjectFolderPath, blogSettings.GalleryContentPath));
}
blogSettings.SourceTemplatesPath = Path.GetFullPath(
    Path.Combine(sourceProjectFolderPath, blogSettings.SourceTemplatesPath));
blogSettings.BlogRootPath = blogProjectFolderPath;
string currentAssemblyPath = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)!;
string relativeOutputPath = currentAssemblyPath.Substring(currentAssemblyPath.IndexOf("bin", StringComparison.Ordinal));
string sourceProjectName = Path.GetFileNameWithoutExtension(blogSettings.SourceProject);
blogSettings.SourceAssemblyOutputPath = Path.Combine(sourceProjectFolderPath, relativeOutputPath, $"{sourceProjectName}.dll");
Directory.CreateDirectory(blogSettings.OutputWebRootPath);
Directory.CreateDirectory(blogSettings.OutputComponentsPath);

await Generator.GenerateSite(serviceProvider);