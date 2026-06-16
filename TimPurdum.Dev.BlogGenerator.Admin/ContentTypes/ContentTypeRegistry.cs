namespace TimPurdum.Dev.BlogGenerator.Admin.ContentTypes;

/// <summary>
/// Singleton lookup of content types registered via <c>AddBlogAdmin</c>. The Editor, Dashboard,
/// MainLayout, and ContentListPage all read from this registry instead of hardcoding type lists,
/// so consumer sites can add/remove content types without forking the admin pages.
/// </summary>
public sealed class ContentTypeRegistry
{
    private readonly List<IContentTypeDescriptor> _descriptors;

    public ContentTypeRegistry(IEnumerable<IContentTypeDescriptor> descriptors)
    {
        _descriptors = descriptors
            .OrderBy(d => d.Order)
            .ThenBy(d => d.Slug, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>All registered descriptors in display order.</summary>
    public IReadOnlyList<IContentTypeDescriptor> All => _descriptors;

    /// <summary>Resolve a descriptor by its URL slug, or null if unregistered.</summary>
    public IContentTypeDescriptor? FindBySlug(string slug) =>
        _descriptors.FirstOrDefault(d =>
            string.Equals(d.Slug, slug, StringComparison.OrdinalIgnoreCase));
}
