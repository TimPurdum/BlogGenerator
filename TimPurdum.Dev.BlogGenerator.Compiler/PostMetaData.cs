public record PostMetaData(string Title, string SubTitle, string Url, DateTime PublishedDate, 
    string Author, string Content, Dictionary<string, string> RazorComponents, List<string> ScriptTags,
    string Layout, string Description);
    
public record PageMetaData(string Title, string SubTitle, string Url, string Content,
    Dictionary<string, string> RazorComponents, List<string> ScriptTags, string Layout, string Description, 
    int NavOrder);