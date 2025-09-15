namespace TimPurdum.Dev.BlogGenerator.Compiler;

public static class HtmlFileGenerator
{
    public static string GenerateHtmlFile(PostMetaData postMetaData, string htmlTemplate)
    {
        string htmlContent = htmlTemplate
            .Replace("@*TITLE*@", postMetaData.Title)
            .Replace("@*SUBTITLE*@", postMetaData.SubTitle)
            .Replace("@*URL*@", postMetaData.Url)
            .Replace("@*CONTENT*@", postMetaData.Content);
        
        htmlContent = htmlContent.Substring(htmlContent.IndexOf("<article", StringComparison.Ordinal));

        return htmlContent;
    }
}