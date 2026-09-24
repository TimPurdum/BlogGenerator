using System.Text.Json;
using Microsoft.JSInterop;

namespace TimPurdum.Dev.BlogGenerator.Admin.Services;

/// <summary>
/// A snapshot of an editor session kept in localStorage, so leaving without clicking Save doesn't
/// lose the work. Holds the built markdown document (front-matter + body, via the content type's
/// own serializer) plus the File fieldset values, which live outside the document.
/// </summary>
/// <param name="FileSha">SHA of the repo file the backup was taken against, null for new entries. Used
/// to notice that the saved version changed after the copy was made (saved from another browser/device).</param>
/// <param name="Markdown">The full document, as BuildDocument would emit it.</param>
/// <param name="Slug">The slug input, so a rename-in-progress survives the round trip.</param>
/// <param name="DateFull">The full date input (YYYY-MM-DD) for dated content types.</param>
/// <param name="DateMonth">The month input (YYYY-MM) for YearMonth content types.</param>
/// <param name="SavedAt">When the copy was stored, local time — shown in the restore notice.</param>
public sealed record EditorBackup(
    string? FileSha,
    string Markdown,
    string Slug,
    string DateFull,
    string DateMonth,
    DateTime SavedAt);

/// <summary>
/// Reads and writes <see cref="EditorBackup"/> entries to the browser's localStorage, one key per
/// content file. Mirrors the interop style of <see cref="AuthService"/> — localStorage is small,
/// synchronous, and needs no wrapper JS.
/// </summary>
public sealed class EditorBackupService(IJSRuntime js)
{
    /// <summary>Load the backup for <paramref name="key"/>. Returns null when none exists or a stored
    /// payload can't be deserialized (unreadable copies are treated as absent).</summary>
    public async Task<EditorBackup?> LoadAsync(string key, CancellationToken ct = default)
    {
        string? json = await js.InvokeAsync<string?>("localStorage.getItem", ct, key);
        if (string.IsNullOrEmpty(json)) return null;
        try
        {
            return JsonSerializer.Deserialize<EditorBackup>(json);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public Task SaveAsync(string key, EditorBackup backup, CancellationToken ct = default) =>
        js.InvokeVoidAsync("localStorage.setItem", ct, key, JsonSerializer.Serialize(backup)).AsTask();

    public Task ClearAsync(string key, CancellationToken ct = default) =>
        js.InvokeVoidAsync("localStorage.removeItem", ct, key).AsTask();
}