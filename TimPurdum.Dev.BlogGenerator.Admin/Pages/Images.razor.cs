namespace TimPurdum.Dev.BlogGenerator.Admin.Pages;

public partial class Images
{
    private static string FormatSize(long bytes) => bytes switch
    {
        < 1024 => $"{bytes} B",
        < 1024 * 1024 => $"{bytes / 1024.0:N0} KB",
        _ => $"{bytes / 1024.0 / 1024.0:N1} MB",
    };
}
