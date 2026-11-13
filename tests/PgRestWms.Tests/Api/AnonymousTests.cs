using System.Net;
using System.Net.Http.Json;

namespace PgRestWms.Tests.Api;

[Collection(PostgRestCollection.Name)]
public sealed class AnonymousTests(PostgRestFixture api)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData("products")]
    [InlineData("warehouses")]
    [InlineData("stock")]
    [InlineData("movements")]
    public async Task Reads_WithoutToken_Return401(string path)
    {
        using var client = api.Anonymous();

        var response = await client.GetAsync(path, Ct);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [InlineData("products")]
    [InlineData("rpc/receive")]
    [InlineData("rpc/issue")]
    [InlineData("rpc/me")]
    public async Task Writes_WithoutToken_Return401(string path)
    {
        using var client = api.Anonymous();
        // PostgREST picks a function by its argument names, so the body must match the signature.
        object body = path switch
        {
            "products" => new { sku = "ANON", name = "x" },
            "rpc/me" => new { },
            _ => new
            {
                product_id = Guid.NewGuid(),
                warehouse_id = Guid.NewGuid(),
                quantity = 1,
            },
        };

        var response = await client.PostAsJsonAsync(path, body, Ct);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task OpenApi_ForAnonymous_ShowsOnlyLogin()
    {
        using var client = api.Anonymous();

        var openApi = await client.GetStringAsync("", Ct);

        Assert.Contains("/rpc/login", openApi, StringComparison.Ordinal);
        Assert.DoesNotContain("/products", openApi, StringComparison.Ordinal);
    }
}
