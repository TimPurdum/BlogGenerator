using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace TimPurdum.Dev.BlogGenerator.Admin.Services;

/// <summary>
/// Minimal wrapper around GitHub's Contents API. All operations target the configured owner/repo
/// on the default branch. <see cref="BlogAdminOptions.Repo"/> is consulted via DI.
/// </summary>
public sealed class GitHubApiService(HttpClient http, AuthService auth, BlogAdminOptions options)
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    /// <summary>Fetch the authenticated user — used to validate a token.</summary>
    public async Task<GitHubUser?> GetAuthenticatedUserAsync(string? tokenOverride = null, CancellationToken ct = default)
    {
        using HttpRequestMessage req = new(HttpMethod.Get, "user");
        ApplyAuth(req, tokenOverride);
        HttpResponseMessage res = await http.SendAsync(req, ct);
        if (!res.IsSuccessStatusCode) return null;
        return await res.Content.ReadFromJsonAsync<GitHubUser>(JsonOptions, ct);
    }

    /// <summary>List the contents of a directory in the repo.</summary>
    public async Task<IReadOnlyList<RepoEntry>> ListDirectoryAsync(string path, CancellationToken ct = default)
    {
        using HttpRequestMessage req = new(HttpMethod.Get, ContentsUrl(path));
        ApplyAuth(req);
        HttpResponseMessage res = await http.SendAsync(req, ct);
        if (res.StatusCode == HttpStatusCode.NotFound) return [];
        res.EnsureSuccessStatusCode();
        List<RepoEntry>? list = await res.Content.ReadFromJsonAsync<List<RepoEntry>>(JsonOptions, ct);
        return list ?? [];
    }

    /// <summary>Read a single file's metadata + decoded UTF-8 contents.</summary>
    public async Task<RepoFile?> GetFileAsync(string path, CancellationToken ct = default)
    {
        using HttpRequestMessage req = new(HttpMethod.Get, ContentsUrl(path));
        ApplyAuth(req);
        HttpResponseMessage res = await http.SendAsync(req, ct);
        if (res.StatusCode == HttpStatusCode.NotFound) return null;
        res.EnsureSuccessStatusCode();
        RepoEntry? entry = await res.Content.ReadFromJsonAsync<RepoEntry>(JsonOptions, ct);
        if (entry is null || entry.Type != "file") return null;
        string text = entry.Content is null
            ? string.Empty
            : Encoding.UTF8.GetString(Convert.FromBase64String(
                new string(entry.Content.Where(static c => !char.IsWhiteSpace(c)).ToArray())));
        return new RepoFile(entry.Path, entry.Sha, text);
    }

    /// <summary>Create or update a text file. Pass <paramref name="sha"/> when updating; omit for creates.</summary>
    public async Task<RepoCommitResult> PutTextFileAsync(string path, string text, string commitMessage,
        string? sha = null, CancellationToken ct = default)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(text);
        return await PutBytesAsync(path, bytes, commitMessage, sha, ct);
    }

    /// <summary>Create or update a binary file (e.g. images). Caller passes raw bytes; we base64 here.</summary>
    public async Task<RepoCommitResult> PutBytesAsync(string path, byte[] bytes, string commitMessage,
        string? sha = null, CancellationToken ct = default)
    {
        PutBody body = new(commitMessage, Convert.ToBase64String(bytes), sha);
        using HttpRequestMessage req = new(HttpMethod.Put, ContentsUrl(path))
        {
            Content = JsonContent.Create(body, options: JsonOptions)
        };
        ApplyAuth(req);
        HttpResponseMessage res = await http.SendAsync(req, ct);
        res.EnsureSuccessStatusCode();
        PutResponse? wrapped = await res.Content.ReadFromJsonAsync<PutResponse>(JsonOptions, ct);
        return new RepoCommitResult(
            wrapped?.Content?.Path ?? path,
            wrapped?.Content?.Sha ?? string.Empty,
            wrapped?.Commit?.Sha ?? string.Empty);
    }

    /// <summary>Fetch the most recent workflow run that ran on a specific commit, or null if one hasn't started yet.</summary>
    public async Task<WorkflowRun?> GetLatestWorkflowRunForCommitAsync(string commitSha, CancellationToken ct = default)
    {
        using HttpRequestMessage req = new(HttpMethod.Get,
            $"repos/{options.Repo.Owner}/{options.Repo.Repo}/actions/runs?head_sha={commitSha}&per_page=1");
        ApplyAuth(req);
        HttpResponseMessage res = await http.SendAsync(req, ct);
        if (!res.IsSuccessStatusCode) return null;
        WorkflowRunListResponse? wrapped = await res.Content.ReadFromJsonAsync<WorkflowRunListResponse>(JsonOptions, ct);
        return wrapped?.WorkflowRuns?.FirstOrDefault();
    }

    /// <summary>Delete a file at <paramref name="path"/>. <paramref name="sha"/> is required.</summary>
    public async Task DeleteFileAsync(string path, string sha, string commitMessage, CancellationToken ct = default)
    {
        DeleteBody body = new(commitMessage, sha);
        using HttpRequestMessage req = new(HttpMethod.Delete, ContentsUrl(path))
        {
            Content = JsonContent.Create(body, options: JsonOptions)
        };
        ApplyAuth(req);
        HttpResponseMessage res = await http.SendAsync(req, ct);
        res.EnsureSuccessStatusCode();
    }

    private string ContentsUrl(string path)
        => $"repos/{options.Repo.Owner}/{options.Repo.Repo}/contents/{path.TrimStart('/')}";

    private void ApplyAuth(HttpRequestMessage req, string? tokenOverride = null)
    {
        string? token = tokenOverride ?? auth.Token;
        if (!string.IsNullOrEmpty(token))
        {
            req.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        }
        req.Headers.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        req.Headers.Add("X-GitHub-Api-Version", "2022-11-28");
    }

    private sealed record PutBody(string Message, string Content, string? Sha);
    private sealed record DeleteBody(string Message, string Sha);
    private sealed record PutResponse(RepoEntry? Content, PutCommit? Commit);
    private sealed record PutCommit(string Sha);
    private sealed record WorkflowRunListResponse(List<WorkflowRun>? WorkflowRuns);
}

public sealed record WorkflowRun(
    long Id,
    string Status,        // "queued" | "in_progress" | "completed"
    string? Conclusion,   // "success" | "failure" | "cancelled" | "skipped" | "timed_out" | null
    string HtmlUrl);

public sealed record GitHubUser(string Login, string? Name, string? AvatarUrl);

/// <summary>One row in a directory listing OR the metadata for a single file.</summary>
public sealed record RepoEntry(
    string Name,
    string Path,
    string Sha,
    long Size,
    string Type,           // "file" | "dir"
    string? Content,       // base64; only present for files when fetched directly
    string? DownloadUrl);

public sealed record RepoFile(string Path, string Sha, string Text);

/// <summary><paramref name="Sha"/> is the file's new sha; <paramref name="CommitSha"/> is the commit that created/updated it.</summary>
public sealed record RepoCommitResult(string Path, string Sha, string CommitSha);
