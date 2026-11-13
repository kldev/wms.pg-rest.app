using System.Threading.RateLimiting;
using JetBrains.Annotations;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;

namespace PgRestWms.Web.Auth;

public static class AuthSetup
{
    public const string SignInRateLimit = "sign-in";
    private const string CookieName = "wms.session";

    public static WebApplicationBuilder AddWmsAuth(this WebApplicationBuilder builder)
    {
        builder
            .Services.AddOptions<AuthOptions>()
            .BindConfiguration(AuthOptions.SectionName)
            .ValidateDataAnnotations()
            .ValidateOnStart();

        builder
            .Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
            .AddCookie(cookie =>
            {
                cookie.Cookie.Name = CookieName;
                cookie.Cookie.HttpOnly = true;
                cookie.Cookie.SameSite = SameSiteMode.Lax;
                cookie.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
                cookie.SlidingExpiration = false;
                cookie.LoginPath = "/login";
                cookie.AccessDeniedPath = "/login";
                // A cookie whose token is gone (expired, host restarted) is no session any more.
                cookie.Events.OnValidatePrincipal = context =>
                {
                    var tokens =
                        context.HttpContext.RequestServices.GetRequiredService<SessionTokens>();
                    if (tokens.Find(context.Principal) is null)
                    {
                        context.RejectPrincipal();
                    }
                    return Task.CompletedTask;
                };
            });

        builder
            .Services.AddAuthorizationBuilder()
            // Everything needs a signed-in user unless it says otherwise (sign-in page, assets, /health).
            .SetFallbackPolicy(new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build());

        builder.Services.AddRateLimiter(limiter =>
        {
            limiter.OnRejected = (context, _) =>
            {
                context.HttpContext.Response.Redirect("/login?error=throttled");
                return ValueTask.CompletedTask;
            };
            limiter.AddPolicy(SignInRateLimit, http =>
            {
                var options = http
                    .RequestServices.GetRequiredService<IOptions<AuthOptions>>()
                    .Value;
                return RateLimitPartition.GetFixedWindowLimiter(
                    http.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                    _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = options.SignInAttemptsPerMinute,
                        Window = TimeSpan.FromMinutes(1),
                    }
                );
            });
        });

        builder.Services.AddCascadingAuthenticationState();
        builder.Services.AddSingleton(TimeProvider.System);
        builder.Services.AddSingleton<SessionTokens>();
        return builder;
    }
}

// Set by the configuration binder (BindConfiguration).
[UsedImplicitly(ImplicitUseKindFlags.Assign, ImplicitUseTargetFlags.Members)]
public sealed class AuthOptions
{
    public const string SectionName = "Auth";

    /// <summary>Password guessing guard per client address. Environment variable: <c>Auth__SignInAttemptsPerMinute</c>.</summary>
    [System.ComponentModel.DataAnnotations.Range(1, 1000)]
    public int SignInAttemptsPerMinute { get; init; } = 10;
}
