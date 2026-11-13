using System.Net;
using System.Net.Http.Json;

namespace PgRestWms.Tests.Api;

/// <summary>
/// EXECUTE grants hold for every call. PostgreSQL checks EXECUTE of an inlined SQL function only while planning, and
/// PostgREST reuses prepared statements on pooled connections - a plan made for app_user would serve web_anon.
/// </summary>
[Collection(PostgRestCollection.Name)]
public sealed class FunctionPrivilegeTests(PostgRestFixture api)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Me_WithoutToken_Is401_EvenAfterSignedInCallsOnEveryPooledConnection()
    {
        using var signedIn = await api.SignedInAsync();
        // More calls than PostgREST's pool (10 connections), in parallel, so each connection prepares rpc/me as app_user.
        // The tasks are awaited before the client is disposed.
        // ReSharper disable once AccessToDisposedClosure
        await Task.WhenAll(
            Enumerable.Range(0, 60).Select(_ => signedIn.PostAsJsonAsync("rpc/me", new { }, Ct))
        );
        using var anonymous = api.Anonymous();

        var statuses = new List<HttpStatusCode>();
        for (var i = 0; i < 20; i++)
        {
            statuses.Add((await anonymous.PostAsJsonAsync("rpc/me", new { }, Ct)).StatusCode);
        }

        Assert.All(statuses, status => Assert.Equal(HttpStatusCode.Unauthorized, status));
    }

    [Fact]
    public async Task NoApiFunction_IsAnInlinableSqlFunction()
    {
        var sqlFunctions = await api.ScalarAsync(
            """
            SELECT coalesce(string_agg(p.proname, ','), '')
            FROM pg_proc p
            JOIN pg_namespace n ON n.oid = p.pronamespace
            JOIN pg_language l ON l.oid = p.prolang
            WHERE n.nspname = 'api' AND l.lanname = 'sql' AND NOT p.prosecdef
            """
        );

        Assert.Equal("", sqlFunctions);
    }
}
