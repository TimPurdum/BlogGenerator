using TimPurdum.Dev.BlogGenerator.Admin.Components;
using TimPurdum.Dev.BlogGenerator.Admin.ContentTypes;
using TimPurdum.Dev.BlogGenerator.Admin.FrontMatter;
using TimPurdum.Dev.BlogGenerator.Admin.Services;

namespace TimPurdum.Dev.BlogGenerator.Admin;

/// <summary>
/// Configuration the consumer site passes to <c>AddBlogAdmin</c>. All site-specific strings —
/// repo coordinates, brand, image paths, content type registrations — flow through here.
/// </summary>
public sealed class BlogAdminOptions
{
    /// <summary>GitHub owner + repo the admin commits against. Required.</summary>
    public GitHubRepoConfig Repo { get; set; } = new("", "");

    /// <summary>localStorage key used to persist the PAT across reloads. Should be unique per site
    /// to avoid collisions when multiple admins are open in the same browser.</summary>
    public string PatStorageKey { get; set; } = "blog.admin.pat";

    /// <summary>Brand name shown in the admin header and the document title suffix.</summary>
    public string SiteName { get; set; } = "Admin";

    /// <summary>Path inside the repo where uploaded images live, e.g. <c>YourProject/wwwroot/images</c>.
    /// The admin appends subfolders from <see cref="ImageFolders"/> and the resized JPEG filename.</summary>
    public string ImagesRoot { get; set; } = "wwwroot/images";

    /// <summary>Subfolders inside <see cref="ImagesRoot"/> the upload UI offers. Empty list = consumer
    /// has no image categorization preference; uploads go into a single bucket named "images".</summary>
    public IReadOnlyList<string> ImageFolders { get; set; } = ["images"];

    /// <summary>URL prefix the public site serves images from, used to build paths returned by
    /// <c>ImageUploadService.UploadAsync</c>. Defaults to <c>/images</c>, which works as long as
    /// the public site exposes <c>wwwroot/images/*</c> at that root.</summary>
    public string PublicImageUrlPrefix { get; set; } = "/images";

    internal List<IContentTypeDescriptor> ContentTypes { get; } = [];

    internal Action<DefaultContentTypeConfig<PostFrontMatter>>? PostConfig { get; private set; }
    internal Action<DefaultContentTypeConfig<PageFrontMatter>>? PageConfig { get; private set; }
    internal bool PostRemoved { get; private set; }
    internal bool PageRemoved { get; private set; }

    /// <summary>Override the default Post content type registration (paths, label, order, etc.).</summary>
    public void ConfigurePost(Action<DefaultContentTypeConfig<PostFrontMatter>> configure) => PostConfig = configure;

    /// <summary>Override the default Page content type registration.</summary>
    public void ConfigurePage(Action<DefaultContentTypeConfig<PageFrontMatter>> configure) => PageConfig = configure;

    /// <summary>Remove the built-in Post registration (for sites that don't have a blog).</summary>
    public void RemoveDefaultPost() => PostRemoved = true;

    /// <summary>Remove the built-in Page registration (for sites with no static pages).</summary>
    public void RemoveDefaultPage() => PageRemoved = true;

    /// <summary>Register a custom content type with its front-matter record and editor form component.</summary>
    public void AddContentType<TFront, TForm>(
        string slug,
        string displayName,
        string contentPath,
        ContentNamePattern namePattern,
        string? singularNoun = null,
        string? dashboardHint = null,
        int order = 100,
        string? urlStem = null,
        Func<string, string>? buildLiveUrl = null)
        where TFront : class, new()
        where TForm : Microsoft.AspNetCore.Components.IComponent
    {
        ContentTypes.Add(new ContentTypeDescriptor<TFront>
        {
            Slug = slug,
            DisplayName = displayName,
            SingularNoun = singularNoun ?? displayName.TrimEnd('s').ToLowerInvariant(),
            DashboardHint = dashboardHint ?? "",
            ContentPath = contentPath,
            NamePattern = namePattern,
            Order = order,
            EditorFormType = typeof(TForm),
            UrlStemOverride = urlStem,
            BuildLiveUrl = buildLiveUrl
        });
    }
}

/// <summary>Mutable bag the consumer fills in to override a built-in default content type registration.</summary>
public sealed class DefaultContentTypeConfig<TFront> where TFront : class, new()
{
    public string Slug { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string SingularNoun { get; set; } = "";
    public string DashboardHint { get; set; } = "";
    public string ContentPath { get; set; } = "";
    public ContentNamePattern NamePattern { get; set; }
    public int Order { get; set; }
    public Type EditorFormType { get; set; } = typeof(object);
    public string? UrlStem { get; set; }
    public Func<string, string>? BuildLiveUrl { get; set; }
}
