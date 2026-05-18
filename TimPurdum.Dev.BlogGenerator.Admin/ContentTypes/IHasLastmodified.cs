namespace TimPurdum.Dev.BlogGenerator.Admin.ContentTypes;

/// <summary>
/// Implemented by front-matter types that want their <c>lastmodified</c> field stamped automatically
/// when the editor saves. BlogGenerator uses this field as part of its regen heuristic, so keeping
/// it fresh on every edit is important.
/// </summary>
public interface IHasLastmodified
{
    string? Lastmodified { get; set; }
}
