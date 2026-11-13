using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;

namespace PgRestWms.Tests.Api;

[Collection(PostgRestCollection.Name)]
public sealed class ProductsTests(PostgRestFixture api)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Create_StoresSkuUpperCase_AndTrimsName()
    {
        using var client = await api.SignedInAsync();
        var sku = ApiData.UniqueCode("p").ToLowerInvariant();

        var row = await ApiData.CreateAsync(
            client,
            "products",
            new
            {
                sku = $" {sku} ",
                name = "  Bolt  ",
                unit = "kg",
            }
        );

        Assert.Equal(sku.ToUpperInvariant(), row["sku"]!.GetValue<string>());
        Assert.Equal("Bolt", row["name"]!.GetValue<string>());
        Assert.Equal("kg", row["unit"]!.GetValue<string>());
    }

    [Fact]
    public async Task Patch_ChangesName()
    {
        using var client = await api.SignedInAsync();
        var id = await ApiData.CreateProductAsync(client);

        var response = await client.PatchAsJsonAsync(
            $"products?id=eq.{id}",
            new { name = "Renamed" },
            Ct
        );

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        var rows = await client.GetFromJsonAsync<JsonArray>($"products?id=eq.{id}&select=name", Ct);
        Assert.Equal("Renamed", rows![0]!["name"]!.GetValue<string>());
    }

    [Fact]
    public async Task DuplicateSku_WhateverTheCase_Returns409()
    {
        using var client = await api.SignedInAsync();
        var sku = ApiData.UniqueCode("D");
        await ApiData.CreateProductAsync(client, sku);

        var response = await client.PostAsJsonAsync(
            "products",
            new { sku = sku.ToLowerInvariant(), name = "Twin" },
            Ct
        );

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var error = await ApiData.ErrorAsync(response);
        Assert.Equal("23505", error["code"]!.GetValue<string>());
        Assert.Contains(
            "products_sku_key",
            error["message"]!.GetValue<string>(),
            StringComparison.Ordinal
        );
    }

    [Theory]
    [InlineData("BAD SKU", "pcs")]
    [InlineData("OK-SKU", "box")]
    public async Task InvalidValues_AreRefusedByTheDatabase(string sku, string unit)
    {
        using var client = await api.SignedInAsync();

        var response = await client.PostAsJsonAsync(
            "products",
            new
            {
                sku = sku == "OK-SKU" ? ApiData.UniqueCode("U") : sku,
                name = "x",
                unit,
            },
            Ct
        );

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("23514", (await ApiData.ErrorAsync(response))["code"]!.GetValue<string>());
    }

    [Fact]
    public async Task Delete_IsNotGranted()
    {
        using var client = await api.SignedInAsync();
        var id = await ApiData.CreateProductAsync(client);

        var response = await client.DeleteAsync($"products?id=eq.{id}", Ct);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task List_FiltersOrdersAndPages()
    {
        using var client = await api.SignedInAsync();
        var prefix = ApiData.UniqueCode("L")[..8];
        foreach (var suffix in new[] { "C", "A", "B" })
        {
            await ApiData.CreateProductAsync(client, $"{prefix}-{suffix}");
        }

        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            $"products?sku=like.{prefix}-*&order=sku.desc&limit=2&offset=1&select=sku"
        );
        request.Headers.Add("Prefer", "count=exact");
        var response = await client.SendAsync(request, Ct);

        response.EnsureSuccessStatusCode();
        var rows = await response.Content.ReadFromJsonAsync<JsonArray>(Ct);
        Assert.Equal(
            [$"{prefix}-B", $"{prefix}-A"],
            rows!.Select(row => row!["sku"]!.GetValue<string>()).ToArray()
        );
        Assert.Equal("1-2/3", response.Content.Headers.GetValues("Content-Range").Single());
    }

    [Fact]
    public async Task Warehouses_CreateAndDuplicateCode()
    {
        using var client = await api.SignedInAsync();
        var code = ApiData.UniqueCode("w").ToLowerInvariant();

        var row = await ApiData.CreateAsync(client, "warehouses", new { code, name = "North" });
        var duplicate = await client.PostAsJsonAsync(
            "warehouses",
            new { code, name = "North again" },
            Ct
        );

        Assert.Equal(code.ToUpperInvariant(), row["code"]!.GetValue<string>());
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
    }
}
