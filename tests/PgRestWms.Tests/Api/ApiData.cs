using System.Net.Http.Json;
using System.Text.Json.Nodes;

namespace PgRestWms.Tests.Api;

/// <summary>Test data through the API itself; codes are unique per call so tests do not share state.</summary>
internal static class ApiData
{
    public static string UniqueCode(string prefix) =>
        $"{prefix}-{Guid.NewGuid():N}"[..16].ToUpperInvariant();

    public static async Task<Guid> CreateProductAsync(HttpClient client, string? sku = null)
    {
        var row = await CreateAsync(
            client,
            "products",
            new
            {
                sku = sku ?? UniqueCode("P"),
                name = "Test product",
                unit = "pcs",
            }
        );
        return row["id"]!.GetValue<Guid>();
    }

    public static async Task<Guid> CreateWarehouseAsync(HttpClient client)
    {
        var row = await CreateAsync(
            client,
            "warehouses",
            new { code = UniqueCode("W"), name = "Test warehouse" }
        );
        return row["id"]!.GetValue<Guid>();
    }

    public static async Task<JsonObject> CreateAsync(HttpClient client, string path, object body)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, path);
        request.Content = JsonContent.Create(body);
        // PostgREST returns the inserted row only when asked; with a single-object Accept it is an object, not an array.
        request.Headers.Add("Prefer", "return=representation");
        request.Headers.Accept.ParseAdd("application/vnd.pgrst.object+json");
        var response = await client.SendAsync(request);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<JsonObject>())!;
    }

    public static Task<HttpResponseMessage> ReceiveAsync(
        HttpClient client,
        Guid productId,
        Guid warehouseId,
        decimal quantity,
        string? note = null
    ) =>
        client.PostAsJsonAsync(
            "rpc/receive",
            new
            {
                product_id = productId,
                warehouse_id = warehouseId,
                quantity,
                note,
            }
        );

    public static Task<HttpResponseMessage> IssueAsync(
        HttpClient client,
        Guid productId,
        Guid warehouseId,
        decimal quantity,
        string? note = null
    ) =>
        client.PostAsJsonAsync(
            "rpc/issue",
            new
            {
                product_id = productId,
                warehouse_id = warehouseId,
                quantity,
                note,
            }
        );

    public static async Task<decimal?> StockAsync(
        HttpClient client,
        Guid productId,
        Guid warehouseId
    )
    {
        var rows = await client.GetFromJsonAsync<JsonArray>(
            $"stock?product_id=eq.{productId}&warehouse_id=eq.{warehouseId}&select=quantity"
        );
        return rows is [JsonObject row] ? row["quantity"]!.GetValue<decimal>() : null;
    }

    public static async Task<JsonObject> ErrorAsync(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<JsonObject>())!;
}
