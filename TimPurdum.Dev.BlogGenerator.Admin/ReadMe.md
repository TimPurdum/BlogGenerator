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

Keep that script order. `admin-interop.js` must load **before** `blazor.webassembly.js` — it
restores the SPA-fallback URL (below) and the router reads `location` during boot.

## Hosting under a sub-path

The admin is normally published as a second Blazor WASM app staged into a subfolder of the
public site, e.g. `/admin/`. Two things have to line up.

**1. `<base href>` must match the mount point.** All asset URLs in `index.html` are relative,
so this is what points `_framework/` and `_content/` at the admin's copies:

```html
<base href="/admin/" />
```

Getting this wrong is quiet and confusing rather than a clean 404: with `<base href="/" />`, a
page served from `/admin/index.html` resolves `_framework/blazor.webassembly.js` to the site
root and boots **the public site's** Blazor app instead of the admin.

**2. Deep links need a 404 bounce.** Static hosts serve their 404 page for any path that isn't a
real file, so `/admin/edit/post/my-slug` never reaches `index.html`. `admin-interop.js` restores
the URL on the way in; the consumer supplies the outbound half in the **site-level** 404 page
(`/404.html` on GitHub Pages — the host only ever serves the one at the root):

```html
<script>
    (function () {
        var MOUNT = "/admin/";
        var path = window.location.pathname || "/";
        // Reaching exactly the mount point here means the admin isn't deployed — its index.html
        // would have been served instead — so bouncing would redirect to itself forever. Fall
        // through to the public 404 instead.
        if (path !== MOUNT && (path.indexOf(MOUNT) === 0 || path === "/admin")) {
            try {
                sessionStorage.setItem("admin.spa.redirect",
                    path + window.location.search + window.location.hash);
            } catch (e) { /* private mode: falls through to the dashboard */ }
            window.location.replace(MOUNT);
            return;
        }
        // ...public 404 content below
    })();
</script>
```

The `admin.spa.redirect` key is the contract between the two halves. The restore side validates
that the stashed path sits inside `<base href>` before applying it, and derives the mount point
from `document.baseURI` — so the same build works at `/admin/`, at the site root, or anywhere
else without reconfiguration.

If the public site's own 404 page boots a Blazor app of its own, put this block ahead of that
boot so the redirect wins.

Ship an `index.html`-shaped `404.html` inside the admin's own `wwwroot` too, with the same
`<base href>`. Hosts that *do* resolve a nested 404 (and local `dotnet run`) will use it.

## The editor is markdown-only

`MarkdownEditor` runs Toast UI in markdown mode with a live preview pane. **WYSIWYG mode is
switched off** and its toggle is not rendered.

That's deliberate. WYSIWYG is backed by ProseMirror, whose schema has no node type for
arbitrary raw HTML — so a post carrying styled `<div>` cards or a `<style>` block had that
markup **silently deleted** on the first keystroke. It also canonicalized the whole document
on every round-trip (`-` bullets to `*`, `---` to `***`, CRLF to LF, trailing spaces trimmed),
so a one-word edit produced a diff touching unrelated lines.

The preview pane already provides what WYSIWYG was for — seeing rendered output while you
type — without either problem. Markdown mode returns the document as-is: an edit changes only
what you edited.

What this means in practice:

- **Raw HTML is safe.** It's plain text in the source pane, and the preview renders it.
- **Line endings are normalized to LF.** Toast UI strips CR from anything it is given, so a
  file checked out with CRLF comes back LF once you edit it. This is the one transformation
  markdown mode still applies, it is unavoidable at this layer, and it was true of the old
  WYSIWYG path too. Set `* text=auto eol=lf` (or `.gitattributes` equivalent) on your content
  directory if the churn bothers you.
- **`<style>` blocks are applied in the preview.** Toast UI's sanitizer strips them from the
  rendered output, so that CSS is re-injected separately, rewritten to be confined to the
  preview pane. A post carrying `body { display: none }` cannot restyle the admin around it,
  and `@import` is dropped rather than scoped so preview rendering never reaches the network.
  CSS inside a fenced code block is *not* applied — the document goes through the editor's own
  CommonMark parse, so a post documenting CSS is left alone.
- **Pasting rich content still produces markdown.** Copy a formatted chunk from a web page or
  document and the `text/html` flavour is converted on paste, so headings, links, emphasis and
  lists survive. Two cases pass straight through untouched: a paste carrying only plain text
  (including the browser's paste-as-plain-text), and text copied out of this editor.
- **Tables are hand-edited.** There's no cell-by-cell table UI; the toolbar button inserts a
  markdown table skeleton.

### Upgrading from 1.2.x

`MarkdownEditor.StartMode` has been **removed**. It selected between WYSIWYG and markdown, and
there is no longer a choice to make. Delete the attribute if you set it — `Height` and the
`Value` / `ValueChanged` pair are unchanged.

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
