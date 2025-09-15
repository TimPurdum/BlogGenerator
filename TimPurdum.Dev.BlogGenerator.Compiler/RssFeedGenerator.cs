using System.ServiceModel.Syndication;
using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace TimPurdum.Dev.BlogGenerator.Compiler;

public static class RssFeedGenerator
{
    public static async Task<string> GenerateRssFeed(List<PostMetaData> posts)
    {
        Uri baseUri = new Uri(Generator.BlogSettings!.SiteUrl, UriKind.Absolute);
        List<SyndicationItem> items = [];

        foreach (var post in posts)
        {
            SyndicationItem item = new(post.Title, post.SubTitle, new Uri(baseUri, post.Url))
            {
                PublishDate = post.PublishedDate,
                Authors = { new SyndicationPerson(post.Author) }
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
        // Post-process XML to inject <content> element with CDATA
        var doc = XDocument.Parse(rssContent);
        var itemElements = doc.Descendants("item").ToList();
        for (int i = 0; i < posts.Count && i < itemElements.Count; i++)
        {
            var post = posts[i];
            var itemElem = itemElements[i];
            // Remove any existing <content> or nested <content> elements
            itemElem.Elements("content").Remove();
            // Add correct <content> element
            var contentElem = new XElement("content",
                new XAttribute("type", "html"),
                new XAttribute(XNamespace.Xml + "base", new Uri(baseUri, post.Url)),
                new XCData(post.Content)
            );
            itemElem.Add(contentElem);
        }

        await using var sw = new StringWriterWithEncoding(Encoding.UTF8);
        doc.Save(sw, SaveOptions.DisableFormatting);
        return sw.ToString();
    }
    
    
    // Helper for correct encoding
    class StringWriterWithEncoding(Encoding encoding) : StringWriter
    {
        public override Encoding Encoding => encoding;
    }
}