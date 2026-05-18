using Microsoft.AspNetCore.Components.Forms;
using Microsoft.JSInterop;

namespace TimPurdum.Dev.BlogGenerator.Admin.Services;

/// <summary>
/// Browser-side image upload: read a file via Blazor's InputFile, resize via a JS canvas wrapper,
/// then PUT through the GitHub Contents API. Output is always JPEG at quality 85, max 2000px wide.
/// Paths inside the repo are derived from <see cref="BlogAdminOptions.ImagesRoot"/> + the chosen
/// subfolder + a normalized filename; the returned public URL uses
/// <see cref="BlogAdminOptions.PublicImageUrlPrefix"/> so consumer sites with non-standard image
/// roots stay flexible.
/// </summary>
public sealed class ImageUploadService(GitHubApiService api, IJSRuntime js, BlogAdminOptions options)
{
    public const int MaxWidth = 2000;
    public const double JpegQuality = 0.85;
    public const long MaxSourceBytes = 25 * 1024 * 1024; // 25MB cap on the source file Blazor will accept

    /// <summary>
    /// Upload <paramref name="file"/> into <paramref name="subfolder"/>. Returns the public URL
    /// (e.g. <c>/images/music/foo.jpg</c>) suitable for use in markdown / image src attributes.
    /// </summary>
    public async Task<string> UploadAsync(IBrowserFile file, string subfolder,
        string? overrideFileName = null, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(subfolder)) throw new ArgumentException("Subfolder is required.", nameof(subfolder));
        string folder = subfolder.Trim('/');

        // Pull the original bytes out of the browser via Blazor's IBrowserFile.
        await using Stream stream = file.OpenReadStream(MaxSourceBytes, ct);
        await using MemoryStream ms = new();
        await stream.CopyToAsync(ms, ct);
        byte[] sourceBytes = ms.ToArray();

        // Hand off to JS for the canvas resize. Result is always JPEG.
        byte[] resized = await js.InvokeAsync<byte[]>("adminResizeImage", ct,
            sourceBytes, MaxWidth, JpegQuality);

        string fileName = NormalizeFileName(overrideFileName ?? file.Name);
        string repoPath = $"{options.ImagesRoot.TrimEnd('/')}/{folder}/{fileName}";
        string publicUrl = $"{options.PublicImageUrlPrefix.TrimEnd('/')}/{folder}/{fileName}";

        await api.PutBytesAsync(repoPath, resized,
            commitMessage: $"admin: upload image {folder}/{fileName}",
            sha: null, ct: ct);

        return publicUrl;
    }

    /// <summary>List the images currently committed under <paramref name="subfolder"/>.</summary>
    public async Task<IReadOnlyList<ImageEntry>> ListAsync(string subfolder, CancellationToken ct = default)
    {
        string folder = subfolder.Trim('/');
        string path = $"{options.ImagesRoot.TrimEnd('/')}/{folder}";
        IReadOnlyList<RepoEntry> entries = await api.ListDirectoryAsync(path, ct);
        return entries
            .Where(e => e.Type == "file" && LooksLikeImage(e.Name))
            .Select(e => new ImageEntry(e.Name, $"{options.PublicImageUrlPrefix.TrimEnd('/')}/{folder}/{e.Name}", e.Sha, e.Size))
            .OrderBy(e => e.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>Delete an image at <paramref name="publicUrl"/>.</summary>
    public async Task DeleteAsync(string publicUrl, string sha, CancellationToken ct = default)
    {
        string prefix = options.PublicImageUrlPrefix.TrimEnd('/') + "/";
        if (!publicUrl.StartsWith(prefix, StringComparison.Ordinal))
            throw new ArgumentException($"publicUrl must start with {prefix}.", nameof(publicUrl));
        string relPath = publicUrl[prefix.Length..];
        string repoPath = $"{options.ImagesRoot.TrimEnd('/')}/{relPath}";
        await api.DeleteFileAsync(repoPath, sha, $"admin: delete image {relPath}", ct);
    }

    /// <summary>Lowercase, replace whitespace with hyphens, ensure a sensible extension.</summary>
    public static string NormalizeFileName(string name)
    {
        // Strip directory components — IBrowserFile.Name on some browsers can include them.
        name = Path.GetFileName(name);
        string baseName = Path.GetFileNameWithoutExtension(name);
        // Force JPEG extension since the resize output is always JPEG.
        string normalized = string.Concat(baseName
            .Trim()
            .Select(c => char.IsLetterOrDigit(c) || c == '-' || c == '_' ? c
                       : char.IsWhiteSpace(c) ? '-'
                       : '\0'))
            .Replace("\0", "");
        if (string.IsNullOrEmpty(normalized)) normalized = $"image-{DateTime.UtcNow:yyyyMMddHHmmss}";
        return $"{normalized}.jpg";
    }

    private static bool LooksLikeImage(string fileName)
    {
        string ext = Path.GetExtension(fileName).ToLowerInvariant();
        return ext is ".jpg" or ".jpeg" or ".png" or ".gif" or ".webp" or ".avif";
    }
}

public sealed record ImageEntry(string Name, string Url, string Sha, long Size);
