namespace TimPurdum.Dev.BlogGenerator.Admin.ContentTypes;

/// <summary>
/// Implemented by front-matter types that support draft and publish. New entries of an implementing
/// type start as drafts and render nowhere on the public site until published.
///
/// The property is nullable on purpose. <see cref="Services.MarkdownDocument"/> serializes with
/// <c>OmitNull</c>, so a published entry carries no <c>draft</c> key at all and existing files keep
/// their exact on-disk bytes. A plain <c>bool</c> would write <c>draft: false</c> into every file the
/// editor touched.
///
/// The interface exists rather than a loose YAML convention because <c>MarkdownDocument.Build</c>
/// serializes only the typed model — a key absent from a content type's class is erased on save. A
/// type that carried <c>draft</c> without modeling it would publish itself the first time it was saved.
/// </summary>
public interface IDraftable
{
    /// <summary>True when this entry is a draft. Null (the serialized absence of the key) means published.</summary>
    bool? Draft { get; set; }
}
