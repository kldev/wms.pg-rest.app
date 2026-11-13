using System.Net;

namespace PgRestWms.Tests.Api;

[Collection(PostgRestCollection.Name)]
public sealed class SchemaReloadTests(PostgRestFixture api)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task NewViewColumn_AppearsInApiAndOpenApi_AfterReload_WithoutAnyCSharp()
    {
        using var client = await api.SignedInAsync();
        Assert.Equal(
            HttpStatusCode.BadRequest,
            (await client.GetAsync("warehouses?select=code_length", Ct)).StatusCode
        );

        // A developer's change: one more column at the end of an api view, then PostgREST reloads its schema cache.
        await api.SqlAsync(
            """
            SET ROLE wms_owner;
            CREATE OR REPLACE VIEW api.warehouses AS
              SELECT id, code, name, created_at, updated_at, length(code) AS code_length FROM data.warehouses;
            NOTIFY pgrst, 'reload schema';
            """
        );

        var status = HttpStatusCode.BadRequest;
        for (var attempt = 0; attempt < 50 && status != HttpStatusCode.OK; attempt++)
        {
            await Task.Delay(200, Ct);
            status = (await client.GetAsync("warehouses?select=code_length", Ct)).StatusCode;
        }
        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Contains(
            "code_length",
            await client.GetStringAsync("", Ct),
            StringComparison.Ordinal
        );
    }
}
