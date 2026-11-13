using System.Collections.Concurrent;
using System.Security.Claims;
using System.Security.Cryptography;

namespace PgRestWms.Web.Auth;

/// <summary>
/// PostgREST tokens of the signed-in users, kept in the server's memory under a random session id. The browser's
/// cookie carries only that id and the username, so the JWT never reaches the browser. A restart of the host ends
/// every session (sign in again) - fine for one instance; several would need a shared store.
/// </summary>
public sealed class SessionTokens(TimeProvider time)
{
    public const string SessionIdClaim = "sid";

    private readonly ConcurrentDictionary<string, SessionToken> _tokens = new();

    public string Add(string token, DateTimeOffset expiresAt)
    {
        RemoveExpired();
        var id = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        _tokens[id] = new SessionToken(token, expiresAt);
        return id;
    }

    public string? Find(ClaimsPrincipal? user) =>
        user?.FindFirstValue(SessionIdClaim) is { } id
        && _tokens.TryGetValue(id, out var session)
        && session.ExpiresAt > time.GetUtcNow()
            ? session.Token
            : null;

    public void Remove(ClaimsPrincipal? user)
    {
        if (user?.FindFirstValue(SessionIdClaim) is { } id)
        {
            _tokens.TryRemove(id, out _);
        }
    }

    private void RemoveExpired()
    {
        var now = time.GetUtcNow();
        foreach (var (id, session) in _tokens)
        {
            if (session.ExpiresAt <= now)
            {
                _tokens.TryRemove(id, out _);
            }
        }
    }

    private sealed record SessionToken(string Token, DateTimeOffset ExpiresAt);
}
