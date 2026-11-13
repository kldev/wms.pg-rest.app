using System.Security.Claims;
using Bunit;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Extensions.DependencyInjection;
using PgRestWms.Tests.Api;
using PgRestWms.Web.Auth;
using PgRestWms.Web.Components.Forms;
using PgRestWms.Web.PostgRest;

namespace PgRestWms.Tests.Web;

/// <summary>
/// The interactive forms against the real PostgREST: the database refuses, the form shows its message next to the
/// field. Rendered with bUnit, signed in with a real token kept in <see cref="SessionTokens"/>.
/// </summary>
[Collection(PostgRestCollection.Name)]
public sealed class FormErrorTests(PostgRestFixture api) : IAsyncLifetime
{
    private readonly BunitContext _bunit = new();
    private readonly HttpClient _postgRest = new() { BaseAddress = api.BaseAddress };
    private HttpClient _data = null!;

    public async ValueTask InitializeAsync()
    {
        _data = await api.SignedInAsync();
        var tokens = new SessionTokens(TimeProvider.System);
        var token = await PostgRestFixture.LoginAsync(
            _postgRest,
            PostgRestFixture.Username,
            PostgRestFixture.Password
        );
        var sessionId = tokens.Add(token, DateTimeOffset.UtcNow.AddHours(1));
        var user = new ClaimsPrincipal(
            new ClaimsIdentity(
                [
                    new Claim(ClaimTypes.Name, PostgRestFixture.Username),
                    new Claim(SessionTokens.SessionIdClaim, sessionId),
                ],
                "test"
            )
        );
        _bunit.Services.AddSingleton(tokens);
        _bunit.Services.AddSingleton<AuthenticationStateProvider>(new FixedUser(user));
        _bunit.Services.AddSingleton(sp => new PostgRestClient(
            _postgRest,
            tokens,
            sp.GetRequiredService<AuthenticationStateProvider>()
        ));
    }

    public async ValueTask DisposeAsync()
    {
        _data.Dispose();
        _postgRest.Dispose();
        await _bunit.DisposeAsync();
    }

    [Fact]
    public async Task ProductEditor_DuplicateSku_ShowsTheDatabaseMessageAtSku()
    {
        var sku = ApiData.UniqueCode("E");
        await ApiData.CreateProductAsync(_data, sku);
        var saved = false;
        var form = _bunit.Render<ProductEditor>(p => p.Add(x => x.OnSaved, () => saved = true));

        await form.Find("#sku").ChangeAsync(sku.ToLowerInvariant());
        await form.Find("#name").ChangeAsync("Twin");
        await form.Find("form").SubmitAsync();

        await form.WaitForAssertionAsync(() =>
            Assert.Equal(
                "A product with this SKU already exists.",
                form.Find("[data-field-error=sku]").TextContent
            )
        );
        Assert.False(saved);
    }

    [Fact]
    public async Task ProductEditor_InvalidSku_ShowsTheFormatRule()
    {
        var form = _bunit.Render<ProductEditor>();

        await form.Find("#sku").ChangeAsync("has space");
        await form.Find("#name").ChangeAsync("x");
        await form.Find("form").SubmitAsync();

        await form.WaitForAssertionAsync(() =>
            Assert.StartsWith(
                "SKU: up to 32 characters",
                form.Find("[data-field-error=sku]").TextContent,
                StringComparison.Ordinal
            )
        );
    }

    [Fact]
    public async Task WarehouseEditor_DuplicateCode_ShowsTheDatabaseMessageAtCode()
    {
        var code = ApiData.UniqueCode("C");
        await ApiData.CreateAsync(_data, "warehouses", new { code, name = "First" });
        var form = _bunit.Render<WarehouseEditor>();

        await form.Find("#code").ChangeAsync(code);
        await form.Find("#name").ChangeAsync("Second");
        await form.Find("form").SubmitAsync();

        await form.WaitForAssertionAsync(() =>
            Assert.Equal(
                "A warehouse with this code already exists.",
                form.Find("[data-field-error=code]").TextContent
            )
        );
    }

    [Fact]
    public async Task BookingForm_IssueMoreThanStock_ShowsAvailableQuantityAtQuantity()
    {
        var product = await ApiData.CreateProductAsync(_data);
        var warehouse = await ApiData.CreateWarehouseAsync(_data);
        await ApiData.ReceiveAsync(_data, product, warehouse, 5m);
        var form = _bunit.Render<BookingForm>(p =>
            p.Add(x => x.Products, [Product(product)])
                .Add(x => x.Warehouses, [Warehouse(warehouse)])
                .Add(x => x.Kind, MovementKinds.Issue)
                .Add(x => x.ProductId, product)
                .Add(x => x.WarehouseId, warehouse)
        );

        await form.Find("#quantity").ChangeAsync("6");
        await form.Find("form").SubmitAsync();

        await form.WaitForAssertionAsync(() =>
            Assert.Equal(
                "Not enough stock: 5 available",
                form.Find("[data-field-error=quantity]").TextContent
            )
        );
        Assert.Equal(5m, await ApiData.StockAsync(_data, product, warehouse));
    }

    [Fact]
    public async Task BookingForm_Receive_ShowsTheNewStock()
    {
        var product = await ApiData.CreateProductAsync(_data);
        var warehouse = await ApiData.CreateWarehouseAsync(_data);
        var form = _bunit.Render<BookingForm>(p =>
            p.Add(x => x.Products, [Product(product)])
                .Add(x => x.Warehouses, [Warehouse(warehouse)])
                .Add(x => x.ProductId, product)
                .Add(x => x.WarehouseId, warehouse)
        );

        await form.Find("#quantity").ChangeAsync("2,5");
        await form.Find("form").SubmitAsync();

        await form.WaitForAssertionAsync(() =>
            Assert.Contains(
                "Stock now 2.5.",
                form.Find("[data-testid=success]").TextContent,
                StringComparison.Ordinal
            )
        );
    }

    private static Product Product(Guid id) =>
        new(id, "SKU", "Test product", "pcs", DateTimeOffset.UtcNow);

    private static Warehouse Warehouse(Guid id) =>
        new(id, "WH", "Test warehouse", DateTimeOffset.UtcNow);

    private sealed class FixedUser(ClaimsPrincipal user) : AuthenticationStateProvider
    {
        public override Task<AuthenticationState> GetAuthenticationStateAsync() =>
            Task.FromResult(new AuthenticationState(user));
    }
}
