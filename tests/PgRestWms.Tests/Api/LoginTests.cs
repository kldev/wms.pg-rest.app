using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Nodes;

namespace PgRestWms.Tests.Api;

[Collection(PostgRestCollection.Name)]
public sealed class LoginTests(PostgRestFixture api)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Login_ReturnsJwt_ThatPostgRestAccepts()
    {
        using var client = api.Anonymous();

        var token = await PostgRestFixture.LoginAsync(
            client,
            PostgRestFixture.Username,
            PostgRestFixture.Password
        );

        Assert.Equal(3, token.Split('.').Length);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var response = await client.PostAsync("rpc/me", null, Ct);
        var me = await response.Content.ReadFromJsonAsync<JsonObject>(Ct);
        Assert.Equal(PostgRestFixture.Username, me!["username"]!.GetValue<string>());
        Assert.Equal("app_user", me["role"]!.GetValue<string>());
    }

    [Fact]
    public async Task Login_UsernameIsCaseInsensitive()
    {
        using var client = api.Anonymous();

        var token = await PostgRestFixture.LoginAsync(
            client,
            $" {PostgRestFixture.Username.ToUpperInvariant()} ",
            PostgRestFixture.Password
        );

        Assert.NotEmpty(token);
    }

    [Theory]
    [InlineData(PostgRestFixture.Username, "wrong-password")]
    [InlineData("nobody", "wrong-password")]
    public async Task Login_WrongCredentials_Returns403_WithoutSayingWhichPart(
        string username,
        string password
    )
    {
        using var client = api.Anonymous();

        var response = await client.PostAsJsonAsync("rpc/login", new { username, password }, Ct);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        var error = await ApiData.ErrorAsync(response);
        Assert.Equal("Invalid username or password", error["message"]!.GetValue<string>());
    }

    [Fact]
    public async Task TokenSignedWithAnotherSecret_IsRejected()
    {
        using var client = api.Anonymous();
        var token = await PostgRestFixture.LoginAsync(
            client,
            PostgRestFixture.Username,
            PostgRestFixture.Password
        );
        var parts = token.Split('.');
        var forged =
            $"{parts[0]}.{parts[1]}.{Convert.ToBase64String("not-the-signature"u8.ToArray())
            .TrimEnd('=')}";
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            forged
        );

        var response = await client.GetAsync("products", Ct);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
