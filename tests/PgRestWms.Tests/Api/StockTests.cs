using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;

namespace PgRestWms.Tests.Api;

[Collection(PostgRestCollection.Name)]
public sealed class StockTests(PostgRestFixture api)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task ReceiveAndIssue_ChangeStock_AndWriteMovements()
    {
        using var client = await api.SignedInAsync();
        var product = await ApiData.CreateProductAsync(client);
        var warehouse = await ApiData.CreateWarehouseAsync(client);

        var receipt = await ApiData.ReceiveAsync(client, product, warehouse, 10.5m, "Delivery");
        var issue = await ApiData.IssueAsync(client, product, warehouse, 4m);

        Assert.Equal(HttpStatusCode.OK, receipt.StatusCode);
        Assert.Equal(
            6.5m,
            (await issue.Content.ReadFromJsonAsync<JsonObject>(Ct))![
                "quantity"
            ]!.GetValue<decimal>()
        );
        Assert.Equal(6.5m, await ApiData.StockAsync(client, product, warehouse));
        var movements = await client.GetFromJsonAsync<JsonArray>(
            $"movements?product_id=eq.{product}&order=created_at&select=kind,quantity,note,created_by",
            Ct
        );
        Assert.Equal(2, movements!.Count);
        Assert.Equal("receipt", movements[0]!["kind"]!.GetValue<string>());
        Assert.Equal("Delivery", movements[0]!["note"]!.GetValue<string>());
        Assert.Equal("issue", movements[1]!["kind"]!.GetValue<string>());
        Assert.All(movements, m =>
            Assert.Equal(PostgRestFixture.Username, m!["created_by"]!.GetValue<string>())
        );
    }

    [Fact]
    public async Task IssueMoreThanStock_Returns409_AndChangesNothing()
    {
        using var client = await api.SignedInAsync();
        var product = await ApiData.CreateProductAsync(client);
        var warehouse = await ApiData.CreateWarehouseAsync(client);
        await ApiData.ReceiveAsync(client, product, warehouse, 5m);

        var response = await ApiData.IssueAsync(client, product, warehouse, 6m);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(
            "Not enough stock: 5 available",
            (await ApiData.ErrorAsync(response))["message"]!.GetValue<string>()
        );
        Assert.Equal(5m, await ApiData.StockAsync(client, product, warehouse));
        var movements = await client.GetFromJsonAsync<JsonArray>(
            $"movements?product_id=eq.{product}&select=id",
            Ct
        );
        Assert.Single(movements!);
    }

    [Fact]
    public async Task IssueWithoutAnyStock_Returns409()
    {
        using var client = await api.SignedInAsync();
        var product = await ApiData.CreateProductAsync(client);
        var warehouse = await ApiData.CreateWarehouseAsync(client);

        var response = await ApiData.IssueAsync(client, product, warehouse, 1m);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Null(await ApiData.StockAsync(client, product, warehouse));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task NonPositiveQuantity_Returns400(int quantity)
    {
        using var client = await api.SignedInAsync();
        var product = await ApiData.CreateProductAsync(client);
        var warehouse = await ApiData.CreateWarehouseAsync(client);

        var response = await ApiData.ReceiveAsync(client, product, warehouse, quantity);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(
            "Quantity must be greater than zero",
            (await ApiData.ErrorAsync(response))["message"]!.GetValue<string>()
        );
    }

    [Fact]
    public async Task UnknownProduct_Returns404()
    {
        using var client = await api.SignedInAsync();
        var warehouse = await ApiData.CreateWarehouseAsync(client);

        var response = await ApiData.ReceiveAsync(client, Guid.NewGuid(), warehouse, 1m);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(
            "Unknown product",
            (await ApiData.ErrorAsync(response))["message"]!.GetValue<string>()
        );
    }

    [Fact]
    public async Task StockAndMovements_AreReadOnly()
    {
        using var client = await api.SignedInAsync();

        var stock = await client.PostAsJsonAsync("stock", new { quantity = 1 }, Ct);
        var movements = await client.PostAsJsonAsync("movements", new { quantity = 1 }, Ct);

        Assert.Equal(HttpStatusCode.Forbidden, stock.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, movements.StatusCode);
    }
}
