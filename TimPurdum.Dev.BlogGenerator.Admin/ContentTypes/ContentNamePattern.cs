namespace TimPurdum.Dev.BlogGenerator.Admin.ContentTypes;

/// <summary>How a content file's name encodes its date and slug.</summary>
public enum ContentNamePattern
{
    /// <summary><c>YYYY-MM-DD-slug.md</c> — posts, music, shows.</summary>
    Dated,
    /// <summary><c>YYYY-MM-slug.md</c> — galleries (no day component).</summary>
    YearMonth,
    /// <summary><c>slug.md</c> — static pages, no date prefix.</summary>
    Plain
}
