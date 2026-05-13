using System.ServiceModel.Syndication;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using TimPurdum.Dev.BlogGenerator.Shared;

namespace TimPurdum.Dev.BlogGenerator.Compiler;

public static class RssFeedGenerator
{
    /// <summary>
    /// Generate an RSS 2.0 feed covering posts, optionally interleaved with music and show entries.
    /// All sources are merged, sorted newest-first by their effective publish date, and emitted as a
    /// single chronological feed at <c>/feed.xml</c>.
    /// </summary>
    /// <remarks>
    /// Galleries are intentionally excluded — image collections are awkward in feed readers. Callers
    /// that want a gallery-only feed should generate a separate file via a future overload.
    /// </remarks>
    public static async Task<string> GenerateRssFeed(
        List<PostMetaData> posts,
        List<MusicMetaData>? music = null,
        List<ShowMetaData>? shows = null)
    {
        Uri baseUri = new(Generator.BlogSettings!.SiteUrl, UriKind.Absolute);
        string fallbackAuthor = Generator.BlogSettings.SiteName;

        // Project each typed collection onto a uniform FeedEntry so the feed-emission loop doesn't
        // have to switch on type. Posts use the frontmatter author; music/shows fall back to the
        // site name because per-event authorship isn't tracked on those records today.
        IEnumerable<FeedEntry> postEntries = posts.Select(p =>
            new FeedEntry(p.Title, p.SubTitle, p.Url, p.PublishedDate,
                string.IsNullOrWhiteSpace(p.Author) ? fallbackAuthor : p.Author, p.Content));

        IEnumerable<FeedEntry> musicEntries = (music ?? []).Select(m =>
            new FeedEntry(m.Title, m.SubTitle, m.Url, m.Date, fallbackAuthor, m.Content));

        IEnumerable<FeedEntry> showEntries = (shows ?? []).Select(s =>
            new FeedEntry(s.Title, s.SubTitle, s.Url, s.PerformanceDate, fallbackAuthor, s.Content));

        List<FeedEntry> entries = postEntries
            .Concat(musicEntries)
            .Concat(showEntries)
            .OrderByDescending(e => e.PublishDate)
            .ToList();

        List<SyndicationItem> items = [];
        foreach (FeedEntry e in entries)
        {
            SyndicationItem item = new(e.Title, e.SubTitle, new Uri(baseUri, e.Url))
            {
                PublishDate = e.PublishDate,
                Authors = { new SyndicationPerson(e.Author) }
            };
            items.Add(item);
        }

        SyndicationFeed feed = new(Generator.BlogSettings.SiteName,
            Generator.BlogSettings.SiteDescription, new Uri(Generator.BlogSettings.SiteUrl))
        {
            Items = items
        };

        XmlWriterSettings xmlSettings = new()
        {
            Encoding = Encoding.UTF8,
            NewLineHandling = NewLineHandling.Entitize,
            NewLineOnAttributes = true,
            Indent = true,
            Async = true
        };

        using var stream = new MemoryStream();
        await using var xmlWriter = XmlWriter.Create(stream, xmlSettings);
        // Create the RSS Feed
        var rssFormatter = new Rss20FeedFormatter(feed, false);
        rssFormatter.WriteTo(xmlWriter);
        await xmlWriter.FlushAsync();
        stream.Position = 0;
        string rssContent;
        using (var reader = new StreamReader(stream, Encoding.UTF8))
        {
            rssContent = await reader.ReadToEndAsync();
        }
        // Post-process XML to inject <content> element with CDATA, in the same order as `entries`
        // (SyndicationFeed preserves insertion order, so item N in the XML corresponds to entries[N]).
        var doc = XDocument.Parse(rssContent);
        var itemElements = doc.Descendants("item").ToList();
        for (int i = 0; i < entries.Count && i < itemElements.Count; i++)
        {
            FeedEntry entry = entries[i];
            var itemElem = itemElements[i];
            // Remove any existing <content> or nested <content> elements
            itemElem.Elements("content").Remove();
            // Add correct <content> element
            var contentElem = new XElement("content",
                new XAttribute("type", "html"),
                new XAttribute(XNamespace.Xml + "base", new Uri(baseUri, entry.Url)),
                new XCData(entry.Content)
            );
            itemElem.Add(contentElem);
        }

        await using var sw = new StringWriterWithEncoding(Encoding.UTF8);
        doc.Save(sw, SaveOptions.DisableFormatting);
        return sw.ToString();
    }


    /// <summary>Internal projection used to merge multiple typed collections into one feed.</summary>
    private sealed record FeedEntry(string Title, string SubTitle, string Url,
        DateTime PublishDate, string Author, string Content);

    // Helper for correct encoding
    class StringWriterWithEncoding(Encoding encoding) : StringWriter
    {
        public override Encoding Encoding => encoding;
    }
}
