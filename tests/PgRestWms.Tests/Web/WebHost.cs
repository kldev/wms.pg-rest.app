using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using PgRestWms.Tests.Api;

namespace PgRestWms.Tests.Web;

/// <summary>The Blazor host in memory, talking to the PostgREST of <see cref="PostgRestFixture"/>.</summary>
public sealed partial class WebHost(PostgRestFixture api) : IDisposable
{
    private readonly WebApplicationFactory<Program> _factory =
        new WebApplicationFactory<Program>().WithWebHostBuilder(host =>
        {
            host.UseSetting("PostgRest:BaseUrl", api.BaseAddress.ToString());
            // Many sign-ins from one address in a test run.
            host.UseSetting("Auth:SignInAttemptsPerMinute", "1000");
        });

    public IServiceProvider Services => _factory.Services;

    /// <summary>A browser-like client: keeps cookies, does not follow redirects (tests look at them).</summary>
    public HttpClient Browser() =>
        _factory.CreateClient(
            new WebApplicationFactoryClientOptions
            {
                AllowAutoRedirect = false,
                HandleCookies = true,
            }
        );

    public static async Task<HttpResponseMessage> SignInAsync(
        HttpClient browser,
        string username,
        string password,
        string returnUrl = "/",
        CancellationToken ct = default
    ) =>
        await browser.PostAsync(
            "auth/login",
            new FormUrlEncodedContent(
                new Dictionary<string, string>
                {
                    ["username"] = username,
                    ["password"] = password,
                    ["returnUrl"] = returnUrl,
                }
            ),
            ct
        );

    /// <summary>A signed-in browser of the fixture's user.</summary>
    public async Task<HttpClient> SignedInBrowserAsync(CancellationToken ct = default)
    {
        var browser = Browser();
        var response = await SignInAsync(
            browser,
            PostgRestFixture.Username,
            PostgRestFixture.Password,
            "/",
            ct
        );
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        return browser;
    }

    /// <summary>The session cookie's content, decrypted the way the host reads it.</summary>
    public System.Security.Claims.ClaimsPrincipal ReadSessionCookie(string cookieValue)
    {
        var options = Services
            .GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>()
            .Get(CookieAuthenticationDefaults.AuthenticationScheme);
        return options.TicketDataFormat.Unprotect(cookieValue)?.Principal
            ?? throw new InvalidOperationException("The cookie could not be read.");
    }

    /// <summary>The SKUs (or codes) of a rendered list, in order (rows carry data-row).</summary>
    public static string[] Rows(string html) =>
        RowPattern().Matches(html).Select(m => m.Groups[1].Value).ToArray();

    public static string? Range(string html) =>
        RangePattern().Match(html) is { Success: true } m ? m.Groups[1].Value : null;

    public void Dispose() => _factory.Dispose();

    [GeneratedRegex("<tr data-row=\"([^\"]+)\"")]
    private static partial Regex RowPattern();

    [GeneratedRegex("data-testid=\"range\">\\s*([^<]+?)\\s*<")]
    private static partial Regex RangePattern();
}
