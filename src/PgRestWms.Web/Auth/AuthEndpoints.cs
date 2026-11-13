using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using PgRestWms.Web.PostgRest;

namespace PgRestWms.Web.Auth;

/// <summary>
/// /auth/login (the sign-in form posts here), /auth/logout and /auth/session-expired. Plain form posts without an
/// antiforgery token: the cookie is SameSite=Lax, so other sites cannot post with it.
/// </summary>
public static class AuthEndpoints
{
    public static IEndpointRouteBuilder MapAuth(this IEndpointRouteBuilder app)
    {
        app.MapPost("/auth/login", LoginAsync)
            .AllowAnonymous()
            .DisableAntiforgery()
            .RequireRateLimiting(AuthSetup.SignInRateLimit);
        app.MapPost("/auth/logout", SignOutAsync).AllowAnonymous().DisableAntiforgery();
        // A page got 401 from PostgREST (token expired or the host restarted): end the session, sign in again.
        app.MapGet("/auth/session-expired", async (HttpContext http, SessionTokens tokens) =>
            {
                tokens.Remove(http.User);
                await http.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
                return Results.LocalRedirect("/login?error=expired");
            })
            .AllowAnonymous();
        return app;
    }

    private static async Task<IResult> LoginAsync(
        HttpContext http,
        PostgRestClient postgRest,
        SessionTokens tokens,
        ILogger<PostgRestClient> logger,
        CancellationToken ct
    )
    {
        var form = await http.Request.ReadFormAsync(ct);
        var returnUrl = LocalUrl(form["returnUrl"]);
        LoginResult login;
        try
        {
            login = await postgRest.LoginAsync(
                form["username"].ToString(),
                form["password"].ToString(),
                ct
            );
        }
        catch (PostgRestException e) when (e.Status == System.Net.HttpStatusCode.Forbidden)
        {
            return Results.LocalRedirect(
                $"/login?error=invalid&returnUrl={Uri.EscapeDataString(returnUrl)}"
            );
        }
        catch (Exception e) when (e is PostgRestException or HttpRequestException)
        {
            logger.LogError(e, "Sign-in failed: PostgREST did not answer as expected");
            return Results.LocalRedirect(
                $"/login?error=unavailable&returnUrl={Uri.EscapeDataString(returnUrl)}"
            );
        }

        var sessionId = tokens.Add(login.Token, login.ExpiresAt);
        var identity = new ClaimsIdentity(
            [
                new Claim(ClaimTypes.Name, login.Username),
                new Claim(SessionTokens.SessionIdClaim, sessionId),
            ],
            CookieAuthenticationDefaults.AuthenticationScheme
        );
        await http.SignInAsync(
            CookieAuthenticationDefaults.AuthenticationScheme,
            new ClaimsPrincipal(identity),
            // The session ends with the token (no sliding).
            new AuthenticationProperties { ExpiresUtc = login.ExpiresAt }
        );
        return Results.LocalRedirect(returnUrl);
    }

    private static async Task<IResult> SignOutAsync(HttpContext http, SessionTokens tokens)
    {
        tokens.Remove(http.User);
        await http.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return Results.LocalRedirect("/login");
    }

    /// <summary>Only a path of this site: an absolute URL in returnUrl would be an open redirect.</summary>
    public static string LocalUrl(string? url) =>
        url is { Length: > 1 }
        && url.StartsWith('/')
        && !url.StartsWith("//")
        && !url.StartsWith("/\\")
            ? url
            : "/";
}
