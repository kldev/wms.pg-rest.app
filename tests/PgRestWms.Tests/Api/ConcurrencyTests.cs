using System.Net;

namespace PgRestWms.Tests.Api;

[Collection(PostgRestCollection.Name)]
public sealed class ConcurrencyTests(PostgRestFixture api)
{
    [Fact]
    public async Task ConcurrentIssues_NeverTakeStockBelowZero()
    {
        using var client = await api.SignedInAsync();
        var product = await ApiData.CreateProductAsync(client);
        var warehouse = await ApiData.CreateWarehouseAsync(client);
        await ApiData.ReceiveAsync(client, product, warehouse, 10m);

        // Ten issues of 3 at once against a stock of 10: exactly three can succeed. The tasks are awaited before the
        // client is disposed.
        // ReSharper disable once AccessToDisposedClosure
        var responses = await Task.WhenAll(
            Enumerable.Range(0, 10).Select(_ => ApiData.IssueAsync(client, product, warehouse, 3m))
        );

        Assert.Equal(3, responses.Count(r => r.StatusCode == HttpStatusCode.OK));
        Assert.Equal(7, responses.Count(r => r.StatusCode == HttpStatusCode.Conflict));
        Assert.Equal(1m, await ApiData.StockAsync(client, product, warehouse));
    }
}
