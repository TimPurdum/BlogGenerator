using System.Text;
using System.Xml.Linq;
using TimPurdum.Dev.BlogGenerator.Shared;

namespace TimPurdum.Dev.BlogGenerator.Compiler;

/// <summary>
/// Emits a sitemaps.org-compliant sitemap.xml covering pages, posts, and any of the typed
/// collections (music / shows / galleries) that the site has configured. URLs are written
/// extensionless to match the convention used in canonical links and the RSS feed — GitHub
/// Pages resolves them via implicit-extension lookup. Each &lt;url&gt; gets a &lt;lastmod&gt;
/// when a meaningful date is available on the source record.
/// </summary>
public static class SitemapGenerator
{
    private static readonly XNamespace Ns = "http://www.sitemaps.org/schemas/sitemap/0.9";

    public static string GenerateSitemap(
        List<PageMetaData> pages,
        List<PostMetaData> posts,
        List<MusicMetaData> music,
        List<ShowMetaData> shows,
        List<GalleryMetaData> galleries)
    {
        Uri baseUri = new(Generator.BlogSettings!.SiteUrl, UriKind.Absolute);

        IEnumerable<XElement> urls =
            pages.Select(p => UrlEntry(baseUri, p.Url, p.LastModified))
                .Concat(posts.Select(p => UrlEntry(baseUri, p.Url, p.PublishedDate)))
                .Concat(music.Select(m => UrlEntry(baseUri, m.Url, m.Date)))
                .Concat(shows.Select(s => UrlEntry(baseUri, s.Url, s.PerformanceDate)))
                .Concat(galleries.Select(g => UrlEntry(baseUri, g.Url, g.Date)));

        XDocument doc = new(
            new XDeclaration("1.0", "utf-8", null),
            new XElement(Ns + "urlset", urls));

        StringBuilder sb = new();
        using (StringWriter sw = new StringWriterWithEncoding(sb, Encoding.UTF8))
        {
            doc.Save(sw);
        }
        return sb.ToString();
    }

    private static XElement UrlEntry(Uri baseUri, string relativeUrl, DateTime? lastModified)
    {
        // Url field on metas is extensionless and begins with "/" (except the home page which is "/"
        // itself). new Uri(baseUri, relativeUrl) joins them correctly for both shapes.
        Uri absolute = new(baseUri, relativeUrl);

        XElement entry = new(Ns + "url", new XElement(Ns + "loc", absolute.ToString()));
        if (lastModified.HasValue)
        {
            // W3C Datetime, date-only form — adequate for sitemap protocol and stable across rebuilds.
            entry.Add(new XElement(Ns + "lastmod",
                lastModified.Value.ToUniversalTime().ToString("yyyy-MM-dd")));
        }
        return entry;
    }

    // XDocument.Save defaults to UTF-16 when written to a StringWriter because StringWriter.Encoding
    // reports UTF-16. The RSS generator uses the same workaround.
    private sealed class StringWriterWithEncoding(StringBuilder sb, Encoding encoding) : StringWriter(sb)
    {
        public override Encoding Encoding { get; } = encoding;
    }
}
