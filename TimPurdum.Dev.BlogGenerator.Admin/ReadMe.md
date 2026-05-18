# TimPurdum.Dev.BlogGenerator.Admin

A reusable Blazor WebAssembly admin for editing markdown content + images on
[BlogGenerator](https://www.nuget.org/packages/TimPurdum.Dev.BlogGenerator)-powered
static sites via the GitHub Contents API.

The consumer site is a thin shell — typically ~30 lines of `Program.cs` plus an
`index.html` and any custom front-matter / editor-form components for content
types beyond `Post` and `Page` (which ship as defaults).

## Quick start

```csharp
// Program.cs
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using TimPurdum.Dev.BlogGenerator.Admin;
using TimPurdum.Dev.BlogGenerator.Admin.ContentTypes;

WebAssemblyHostBuilder builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

builder.Services.AddBlogAdmin(opts =>
{
    opts.Repo = new GitHubRepoConfig("YourGitHubOwner", "your-repo");
    opts.PatStorageKey = "yoursite.admin.pat";
    opts.SiteName = "Your Site Name";
    opts.ImagesRoot = "YourProject/wwwroot/images";
    opts.ImageFolders = ["hero", "gallery"]; // folders inside ImagesRoot
    opts.ConfigurePost(p => p.ContentPath = "YourSource/Content/Posts");
    opts.ConfigurePage(p => p.ContentPath = "YourSource/Content/Pages");
});

await builder.Build().RunAsync();
```

In `wwwroot/index.html`:

```html
<link rel="stylesheet" href="_content/TimPurdum.Dev.BlogGenerator.Admin/css/admin.css" />
<link rel="stylesheet" href="_content/TimPurdum.Dev.BlogGenerator.Admin/css/toastui-editor.min.css" />
<!-- optional: your palette override -->
<link rel="stylesheet" href="css/admin-theme.css" />

<script src="_content/TimPurdum.Dev.BlogGenerator.Admin/js/toastui-editor-all.min.js"></script>
<script src="_content/TimPurdum.Dev.BlogGenerator.Admin/js/admin-interop.js"></script>
<script src="_framework/blazor.webassembly.js"></script>
```

## Adding custom content types

```csharp
opts.AddContentType<MusicFrontMatter, MusicEditorForm>(
    slug: "music",
    displayName: "Music",
    contentPath: "YourSource/Content/Music",
    namePattern: ContentNamePattern.Dated);
```

The registry drives the nav, the dashboard tiles, and the editor's form dispatch.
`MusicFrontMatter` is a record implementing `IHasLastmodified`; `MusicEditorForm`
is a Razor component with a `[Parameter] public required MusicFrontMatter Frontmatter`.

## Theming

`admin.css` exposes its palette as CSS custom properties (`--admin-bg`,
`--admin-text`, `--admin-accent`, etc.). Override them in a second stylesheet
loaded after the library's. See the project README for the full list.
