# Draft and publish for BlogGenerator content

Date: 2026-08-15
Status: approved, ready for implementation planning
Repos affected: `BlogGenerator` (this repo, both NuGet packages), plus a submodule bump in each consuming site

## Problem

Every markdown file under a content path is published the moment it is committed. The admin portal commits directly to `main` through the GitHub Contents API, and that push triggers the deploy workflow, so saving a half-written post puts it on the live site, in the index, in `feed.xml`, and in `sitemap.xml`. There is no way to save work in progress.

The goal is to write a post over several sittings, saving as often as you like, with nothing visible on the public site until you explicitly publish it — and to be able to pull a published post back down again.

## Decisions

These were settled during design and are not open for reinterpretation during implementation.

**Draft state is a `draft: true` key in the YAML front matter.** An absent key means published, so the 20 existing posts on timpurdum.dev and everything on Elliot's site keep working with no migration.

**All content types are in scope**, through an opt-in interface rather than a change to each type.

**Publishing re-dates the file to the day you publish, for dated content types only.** A draft's filename date is a scratch value; the permanent date and URL are fixed at publish time, so a draft that sits for three weeks lands at the top of the archive rather than buried mid-list. This applies to the `Dated` and `YearMonth` values of `ContentNamePattern`. A `Plain` type such as a page has no date in its filename, and renaming one would change its URL and break its route, so publishing a page is a flag flip with no rename.

**Drafts render nowhere.** No HTML file is written for a draft, not even an unlisted one. A file at a guessable `/post/2026/8/15/slug.html` is on the site, which is the thing this feature exists to prevent. The admin's preview pane already renders drafts with the live site's stylesheets, so previewing does not require publishing.

## Data model

A new interface in `TimPurdum.Dev.BlogGenerator.Admin/ContentTypes/`, following the existing `IHasLastmodified` precedent:

```csharp
public interface IDraftable
{
    bool? Draft { get; set; }
}
```

`PostFrontMatter` and `PageFrontMatter` implement it and gain:

```csharp
[YamlMember(Alias = "draft")] public bool? Draft { get; set; }
```

Those two are the only front-matter models this repo ships. Custom types registered by a consuming site through `AddContentType<TFront, TForm>` adopt the interface whenever they want the feature; until they do, they behave exactly as they do today. Note the asymmetry between the two halves: the compiler already understands five content collections (posts, pages, music, shows, galleries), while the admin ships descriptors for posts and pages only.

### Why `bool?` and not `bool`

`MarkdownDocument`'s serializer is built with `ConfigureDefaultValuesHandling(DefaultValuesHandling.OmitNull)`. A nullable property left null emits no YAML key at all, so a published post's front matter stays byte-identical to what is on disk now. A plain `bool` would start writing `draft: false` into every file the admin touches, producing diff noise across the whole archive on unrelated edits.

### Why an interface and not just a YAML key

`MarkdownDocument.Build<TFront>` serializes only the typed model — it does not round-trip unknown keys. Any front-matter key absent from a content type's class is erased when the admin saves that file. If `draft` were a loose convention rather than a modeled property, opening a draft of a type whose model lacked the field and pressing Save would silently strip the flag and publish it.

That erasure is a pre-existing behavior, not something this feature introduces. It is worth flagging separately: the compiler's `PageMetaData` carries an `ExtraFrontMatter` dictionary, which implies pages legitimately hold keys beyond the modeled ones, and the admin drops those keys today. Out of scope here.

### New content defaults to draft

`ContentTypeDescriptor.CreateFrontMatter()` (`ContentTypes/ContentTypeDescriptor.cs:29`) is called from exactly one place: the new-document path in `Editor.razor:190`. Set `Draft = true` there, on the freshly constructed instance, when it implements `IDraftable`.

This must not be expressed as a `true` default on the property. Every existing published post would then deserialize as a draft — its front matter has no `draft` key, so the property keeps its default — and the next save in the admin would unpublish it.

## Compiler

### Reading the flag

Add `FrontMatter.GetBool(string key, bool defaultValue = false)` alongside the existing `GetString` and `GetDateTime` accessors.

`MarkupParser` reads `draft` and carries it as a `bool Draft` on the metadata records: `PostMetaData` and `PageMetaData` in `Compiler/PostMetaData.cs`, and `MusicMetaData` / `ShowMetaData` / `GalleryMetaData` in `Shared/ContentMetaData.cs`. The shared `ParseEntryFile` helper (`MarkupParser.cs:303`) is the single choke point for the three typed collections; posts and pages have their own parse methods and need the read added in each.

### One filter, applied once

`Generator.GenerateSite` (`Generator.cs:23`) partitions the post list immediately after `MarkupParser.GeneratePostMetaDatas()` returns at line 30, into `published` and `drafts`. Everything downstream consumes `published` only:

- the `navLinks` loop at line 36, which feeds both the home page index and the site-wide nav menu
- the per-post render and write loop at line 60
- `RssFeedGenerator.GenerateRssFeed` at line 93
- `SitemapGenerator.GenerateSitemap` at line 99

Neither generator's signature changes; they receive a shorter list. The same partition applies to pages and to any typed collection whose front matter model has adopted `IDraftable`.

### Removing output when a post is unpublished

Generated HTML is committed to the consuming repo and served directly, so a post that becomes a draft needs its `.html` **deleted**, not merely skipped. Skipping leaves the previously published file in place and the URL stays live forever.

Implement this as an orphan sweep rather than a draft-targeted delete. After the render loop, enumerate every `.html` under the post output root — `Path.Combine(BlogSettings.OutputWebRootPath, "post")`, the literal already used at `MarkupParser.cs:60-65` — and delete any file no published post claims as its `OutputPath`.

The sweep is the better mechanism because it covers three cases with one rule: unpublishing, renaming, and deleting the source markdown. It also fixes an existing bug, since renaming a published post today orphans its old HTML permanently, and publish-time re-dating makes renames routine rather than rare.

Factor it as a reusable helper taking an output root and the set of claimed paths. Apply it to posts now, and to the music, show, and gallery roots (`"music"`, `"show"`, `"gallery"`, same construction) once those types adopt the interface.

Pages are the exception. Their output lands directly in `OutputWebRootPath` alongside hand-maintained files such as `404.html`, so a sweep there could delete something the compiler did not generate. A draft page gets a targeted delete of its own expected output path instead.

The sweep covers `.html` only. A post that embeds a `blazor-component` block also emits a `.razor` file into the consuming project's `Components/` directory, and unpublishing leaves that file behind to be compiled into the shipped WASM bundle. That is deliberate: those component names must be unique across the project, the generated file is inert with no page referencing it, and sweeping a directory that developers also hand-author is a much worse risk than a few kilobytes of dead code. Publishing the draft again regenerates it in place.

### The gate this must not go behind

`Generator.cs:62` reads `if (!post.Update) continue;`. `Update` is computed at `MarkupParser.cs:73` from source file mtime against output file mtime. A fresh `actions/checkout` in CI stamps every file with checkout time, so `Update` is not dependably true in the deploy environment.

Deletion logic placed inside that gate would work on a developer machine and silently no-op in CI — unpublish would appear to succeed in the admin, the deploy would go green, and the post would still be live. The sweep runs unconditionally, outside the loop.

### Minor cleanup in the same area

`MarkupParser.cs:66` calls `Directory.CreateDirectory(outputFolder)` during parsing, before anything is known about whether the post will be written. Drafts would create empty dated directories as a side effect. Move the call to the write site in `Generator.GenerateSite`.

## Admin portal

### Publish and unpublish are actions, not a form field

The control belongs in the shared chrome of `Pages/Editor.razor`, beside Save — not in `Components/PostEditorForm.razor`. Putting it in the per-type form means every content type's editor needs its own copy, including custom forms in consuming sites. In the shared chrome it lands once and every type inherits it. It renders only when the current type's front matter implements `IDraftable`.

The editor header shows a Draft or Published badge reflecting current state.

**Publish** sets `Draft = null` and, for a dated type, assigns today to the editor's bound date field — `_dateFull` for `Dated`, `_dateMonth` for `YearMonth` (`Editor.razor:131-132`) — then runs the normal save. `BuildNewFileName` (`Editor.razor:406`) composes the filename from those fields, so the new name differs from the original, `WillRename` (`:144`) becomes true on its own, and the existing two-phase rename does the work. No new file-writing path is needed. For a `Plain` type nothing is re-dated and the save is a single ordinary update. Commit message: `admin: publish <slug> entry <name>`.

Worth recording, since it is easy to misread the code: the date fields are pre-filled from the existing filename by `PrefillFilenameInputs` (`Editor.razor:232`) when an entry is opened for editing, not left at today. Ordinary saves therefore do not re-date anything, and publish re-dating is a deliberate assignment rather than a side effect.

**Unpublish** sets `Draft = true` and leaves the filename alone, so a re-publish gets a fresh date. Commit message: `admin: unpublish <slug> entry <name>`. The live HTML disappears on the next successful build.

### Deploy tracking across the two-commit publish

The rename path is two commits — a PUT at the new path, then a DELETE at the old one (`Editor.razor:367-381`) — because the Contents API has no atomic rename. `Deploy.Track` is currently handed `result.CommitSha` from the PUT, and the DELETE that follows pushes a second commit whose workflow run supersedes it. The banner then follows a run that gets canceled.

That is pre-existing behavior of rename, but publish makes it the common case rather than a rare one, so fix it here: change `GitHubApiService.DeleteFileAsync` (`Services/GitHubApiService.cs:100`) to read its response and return a `RepoCommitResult` like `PutTextFileAsync` does, and track the delete's sha on the rename path. It currently returns `Task` and discards the response body entirely.

`BuildViewLiveUrl` (`Editor.razor:422`) is static and does not see front-matter state, so guard it at the call site (`:360`): a draft passes null to `Deploy.Track`, and the banner offers no "view live" link to a URL that would 404.

### Status on the content list

`Pages/ContentListPage.razor` gets a status column and a Draft / Published / All filter.

`LoadAsync` currently calls `Api.ListDirectoryAsync` only, and the GitHub directory listing carries no front matter, so status requires reading each file. Fetch them concurrently with `Task.WhenAll` over `Api.GetFileAsync` and cache the parsed status in memory for the session, invalidating on save.

For a 20 to 50 post archive against an authenticated rate limit of 5,000 requests per hour, this is acceptable. If a consuming site grows large enough for the fan-out to drag, the escape hatch is GitHub's GraphQL API, which can alias many blobs into a single request. Deliberately not built now — it adds a second API surface to `GitHubApiService` for a problem neither site has.

A row whose file cannot be fetched or parsed shows an unknown status rather than failing the whole list.

## Compatibility

No content migration. A file with no `draft` key is published, which is every file that exists today on both sites.

The behavior change Elliot will notice is that new content created through the admin starts as a draft and needs an explicit publish. Note it in the package release notes and in `TimPurdum.Dev.BlogGenerator.Admin/ReadMe.md`.

Any custom content type a consuming site has registered keeps working untouched, and gains draft support the day its front-matter record adds `IDraftable`. Document that adoption step in the "Adding custom content types" section of the admin ReadMe.

## Versioning

Two packages version independently and both change:

- `TimPurdum.Dev.BlogGenerator` — 1.1.6 to **1.2.0** (compiler filtering, orphan sweep)
- `TimPurdum.Dev.BlogGenerator.Admin` — 1.4.0-preview to **1.5.0-preview** (interface, publish actions, list status)

Each consuming site picks the change up through a submodule bump.

## Verification

The repository has no unit test project, so verification is manual and runs against the real pipeline.

1. Build the site with all 20 existing posts and confirm `TimPurdum.Dev/wwwroot/` is byte-identical to what is committed. This is the regression that matters most — an absent `draft` key must change nothing. Scope the comparison to `wwwroot/`: the compiler stamps `lastmodified` back into source markdown when it regenerates a post (`MarkupParser.cs:92-104`), so the content directory is expected to move.
2. Add `draft: true` to one existing post, rebuild, and confirm its `.html` is deleted from `wwwroot/post/...`, and that it is gone from the index, the nav menu, `feed.xml`, and `sitemap.xml`.
3. Remove the flag, rebuild, confirm the post returns.
4. Rename a published post's source file, rebuild, and confirm the orphaned HTML at the old path is removed — the pre-existing bug this sweep also fixes.
5. In the admin against a scratch branch: create a post, confirm it saves with `draft: true` and offers no "view live" link; confirm the list shows it as a draft; publish it and confirm the file is renamed to today's date and the flag is gone; unpublish and confirm the flag returns with the filename unchanged.
6. Open and save an existing published post in the admin, and confirm the diff touches only what was edited — no `draft: false` line appears.

## Out of scope

- Scheduled or future-dated publishing.
- A drafts branch or any staging deployment. Drafts are commits on `main` that produce no output.
- Rescuing the `ExtraFrontMatter` keys the admin drops on save for pages, noted above as pre-existing.
- Batch operations on the content list, such as publishing several drafts at once.
