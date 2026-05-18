using Microsoft.Extensions.DependencyInjection;
using TimPurdum.Dev.BlogGenerator.Admin.Components;
using TimPurdum.Dev.BlogGenerator.Admin.ContentTypes;
using TimPurdum.Dev.BlogGenerator.Admin.FrontMatter;
using TimPurdum.Dev.BlogGenerator.Admin.Services;

namespace TimPurdum.Dev.BlogGenerator.Admin;

/// <summary>
/// Service-collection extensions consumer sites call from <c>Program.cs</c> to wire up the admin
/// services and content type registry. The <see cref="HttpClient"/> targeting <c>api.github.com</c>
/// is registered automatically; consumers shouldn't add their own.
/// </summary>
public static class AddBlogAdminExtensions
{
    /// <summary>
    /// Configure the admin and register all required services. After calling this, the consumer
    /// only needs to mount <c>App</c> as a root component (typically <c>builder.RootComponents.Add&lt;App&gt;("#app")</c>).
    /// </summary>
    public static IServiceCollection AddBlogAdmin(this IServiceCollection services,
        Action<BlogAdminOptions> configure)
    {
        BlogAdminOptions options = new();
        configure(options);

        // Apply any built-in content type overrides + registrations, then snapshot the descriptor list.
        IReadOnlyList<IContentTypeDescriptor> descriptors = BuildDescriptors(options);

        services.AddSingleton(options);
        services.AddSingleton(new ContentTypeRegistry(descriptors));

        services.AddSingleton(_ => new HttpClient { BaseAddress = new Uri("https://api.github.com/") });
        services.AddSingleton<AuthService>();
        services.AddSingleton<GitHubApiService>();
        services.AddSingleton<DeployStatusService>();
        services.AddSingleton<ImageUploadService>();

        return services;
    }

    private static IReadOnlyList<IContentTypeDescriptor> BuildDescriptors(BlogAdminOptions options)
    {
        List<IContentTypeDescriptor> all = new();

        if (!options.PostRemoved)
        {
            DefaultContentTypeConfig<PostFrontMatter> cfg = new()
            {
                Slug = "posts",
                DisplayName = "Posts",
                SingularNoun = "post",
                DashboardHint = "Blog entries",
                ContentPath = "Source/Content/Posts",
                NamePattern = ContentNamePattern.Dated,
                Order = 10,
                EditorFormType = typeof(PostEditorForm),
                UrlStem = "post"
            };
            options.PostConfig?.Invoke(cfg);
            all.Add(ToDescriptor(cfg));
        }

        if (!options.PageRemoved)
        {
            DefaultContentTypeConfig<PageFrontMatter> cfg = new()
            {
                Slug = "pages",
                DisplayName = "Pages",
                SingularNoun = "page",
                DashboardHint = "Static pages",
                ContentPath = "Source/Content/Pages",
                NamePattern = ContentNamePattern.Plain,
                Order = 5,
                EditorFormType = typeof(PageEditorForm)
            };
            options.PageConfig?.Invoke(cfg);
            all.Add(ToDescriptor(cfg));
        }

        all.AddRange(options.ContentTypes);
        return all;
    }

    private static IContentTypeDescriptor ToDescriptor<TFront>(DefaultContentTypeConfig<TFront> cfg)
        where TFront : class, new() => new ContentTypeDescriptor<TFront>
    {
        Slug = cfg.Slug,
        DisplayName = cfg.DisplayName,
        SingularNoun = cfg.SingularNoun,
        DashboardHint = cfg.DashboardHint,
        ContentPath = cfg.ContentPath,
        NamePattern = cfg.NamePattern,
        Order = cfg.Order,
        EditorFormType = cfg.EditorFormType,
        UrlStemOverride = cfg.UrlStem,
        BuildLiveUrl = cfg.BuildLiveUrl
    };
}
