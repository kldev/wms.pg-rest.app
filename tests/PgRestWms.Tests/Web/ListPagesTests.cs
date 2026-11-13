using PgRestWms.Tests.Api;

namespace PgRestWms.Tests.Web;

/// <summary>Lists are prerendered on the server, so their HTML shows what PostgREST returned for the URL's filters.</summary>
[Collection(PostgRestCollection.Name)]
public sealed class ListPagesTests(PostgRestFixture api) : IDisposable
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private readonly WebHost _web = new(api);

    public void Dispose() => _web.Dispose();

    [Fact]
    public async Task Products_SearchSortAndPage_ThroughTheUrl()
    {
        using var client = await api.SignedInAsync();
        var prefix = ApiData.UniqueCode("S")[..8];
        var skus = Enumerable.Range(1, 12).Select(i => $"{prefix}-{i:00}").ToArray();
        foreach (var sku in skus)
        {
            await ApiData.CreateProductAsync(client, sku);
        }
        using var browser = await _web.SignedInBrowserAsync(Ct);

        var first = await browser.GetStringAsync(
            $"/products?q={prefix.ToLowerInvariant()}&sort=sku&desc=true&size=10",
            Ct
        );
        var second = await browser.GetStringAsync(
            $"/products?q={prefix}&sort=sku&desc=true&size=10&page=2",
            Ct
        );

        Assert.Equal(skus.Reverse().Take(10), WebHost.Rows(first));
        Assert.Equal("1–10 of 12", WebHost.Range(first));
        Assert.Equal([$"{prefix}-02", $"{prefix}-01"], WebHost.Rows(second));
        Assert.Equal("11–12 of 12", WebHost.Range(second));
    }

    [Fact]
    public async Task Stock_FiltersByWarehouse_AndMovements_ByKind()
    {
        using var client = await api.SignedInAsync();
        var sku = ApiData.UniqueCode("K");
        var product = await ApiData.CreateProductAsync(client, sku);
        var here = await ApiData.CreateAsync(
            client,
            "warehouses",
            new { code = ApiData.UniqueCode("H"), name = "Here" }
        );
        var there = await ApiData.CreateWarehouseAsync(client);
        await ApiData.ReceiveAsync(client, product, here["id"]!.GetValue<Guid>(), 5m);
        await ApiData.ReceiveAsync(client, product, there, 7m);
        await ApiData.IssueAsync(client, product, there, 2m);
        using var browser = await _web.SignedInBrowserAsync(Ct);

        var stock = await browser.GetStringAsync($"/?q={sku}&warehouse={here["code"]}", Ct);
        var issues = await browser.GetStringAsync($"/movements?q={sku}&kind=issue", Ct);
        var all = await browser.GetStringAsync($"/movements?q={sku}", Ct);

        Assert.Equal("1–1 of 1", WebHost.Range(stock));
        Assert.Contains("5<", stock, StringComparison.Ordinal);
        Assert.Equal("1–1 of 1", WebHost.Range(issues));
        Assert.Equal("1–3 of 3", WebHost.Range(all));
    }
}
