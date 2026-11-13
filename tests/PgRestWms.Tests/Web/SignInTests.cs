using System.Net;
using Microsoft.Extensions.DependencyInjection;
using PgRestWms.Tests.Api;
using PgRestWms.Web.Auth;

namespace PgRestWms.Tests.Web;

[Collection(PostgRestCollection.Name)]
public sealed class SignInTests(PostgRestFixture api) : IDisposable
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private readonly WebHost _web = new(api);

    public void Dispose() => _web.Dispose();

    [Theory]
    [InlineData("/")]
    [InlineData("/products")]
    [InlineData("/book")]
    public async Task Pages_WithoutSession_RedirectToLogin(string path)
    {
        using var browser = _web.Browser();

        var response = await browser.GetAsync(path, Ct);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.StartsWith(
            "/login",
            response.Headers.Location!.PathAndQuery,
            StringComparison.Ordinal
        );
    }

    [Fact]
    public async Task WrongPassword_BackToLogin_WithMessage()
    {
        using var browser = _web.Browser();

        var response = await WebHost.SignInAsync(
            browser,
            PostgRestFixture.Username,
            "wrong",
            "/",
            Ct
        );

        Assert.Equal(
            "/login?error=invalid&returnUrl=%2F",
            response.Headers.Location!.OriginalString
        );
        var page = await browser.GetStringAsync("/login?error=invalid", Ct);
        Assert.Contains("Invalid username or password.", page, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SignIn_OpensThePage_AndTheTokenNeverReachesTheBrowser()
    {
        using var browser = _web.Browser();

        var login = await WebHost.SignInAsync(
            browser,
            PostgRestFixture.Username,
            PostgRestFixture.Password,
            "/products",
            Ct
        );

        Assert.Equal("/products", login.Headers.Location!.OriginalString);
        var cookie = login
            .Headers.GetValues("Set-Cookie")
            .Single(c => c.StartsWith("wms.session=", StringComparison.Ordinal));
        Assert.Contains("httponly", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=lax", cookie, StringComparison.OrdinalIgnoreCase);

        // The cookie holds the username and a session id only; the token stays in the server's memory.
        var principal = _web.ReadSessionCookie(cookie["wms.session=".Length..cookie.IndexOf(';')]);
        Assert.Equal(
            [
                "http://schemas.xmlsoap.org/ws/2005/05/identity/claims/name",
                SessionTokens.SessionIdClaim,
            ],
            principal.Claims.Select(c => c.Type).Order().ToArray()
        );
        var token = _web.Services.GetRequiredService<SessionTokens>().Find(principal);
        Assert.NotNull(token);
        Assert.DoesNotContain(token, cookie, StringComparison.Ordinal);

        var page = await browser.GetStringAsync("/products", Ct);
        Assert.Contains(PostgRestFixture.Username, page, StringComparison.Ordinal);
        Assert.DoesNotContain(token, page, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SignOut_EndsTheSession_AndForgetsTheToken()
    {
        using var browser = await _web.SignedInBrowserAsync(Ct);
        Assert.Equal(HttpStatusCode.OK, (await browser.GetAsync("/", Ct)).StatusCode);

        var logout = await browser.PostAsync("auth/logout", null, Ct);

        Assert.Equal("/login", logout.Headers.Location!.OriginalString);
        var after = await browser.GetAsync("/", Ct);
        Assert.Equal(HttpStatusCode.Redirect, after.StatusCode);
    }

    [Theory]
    [InlineData("//evil.example/", "/")]
    [InlineData("https://evil.example/", "/")]
    [InlineData("/movements", "/movements")]
    public async Task ReturnUrl_IsOnlyALocalPath(string returnUrl, string expected)
    {
        using var browser = _web.Browser();

        var login = await WebHost.SignInAsync(
            browser,
            PostgRestFixture.Username,
            PostgRestFixture.Password,
            returnUrl,
            Ct
        );

        Assert.Equal(expected, login.Headers.Location!.OriginalString);
    }
}
