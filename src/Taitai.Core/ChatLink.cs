namespace Taitai.Core;

public sealed record ChatLink(string Url, string ThreadId, bool IsLocal)
{
    public static ChatLink? Parse(string? input)
    {
        if (!Uri.TryCreate(input?.Trim(), UriKind.Absolute, out var uri) || uri.UserInfo.Length > 0 || !uri.IsDefaultPort || uri.Query.Length > 0 || uri.Fragment.Length > 0) return null;
        if (uri.Scheme == "codex" && uri.Host == "threads" && Guid.TryParse(uri.AbsolutePath.Trim('/'), out var id)) return new("codex://threads/" + id, id.ToString(), true);
        if (uri.Scheme == "https" && uri.Host is "chatgpt.com" or "chat.openai.com")
        {
            var pieces = uri.AbsolutePath.Trim('/').Split('/');
            if (pieces.Length == 2 && pieces[0] == "c" && Guid.TryParse(pieces[1], out var cloudId)) return new("https://chatgpt.com/c/" + cloudId, cloudId.ToString(), false);
        }
        return null;
    }
    public static ChatLink? Local(string? id) => Guid.TryParse(id, out var parsed) ? new("codex://threads/" + parsed, parsed.ToString(), true) : null;
}
