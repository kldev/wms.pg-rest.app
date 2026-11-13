using PgRestWms.Web.Auth;
using PgRestWms.Web.Components;
using PgRestWms.Web.PostgRest;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorComponents().AddInteractiveServerComponents();
builder.Services.AddHealthChecks();
builder.AddPostgRest();
builder.AddWmsAuth();

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
}

// TLS ends in front of the container (reverse proxy) - the app itself listens on plain HTTP.
app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();
app.UseAntiforgery();

app.MapStaticAssets().AllowAnonymous();
app.MapHealthChecks("/health").AllowAnonymous();
app.MapAuth();
app.MapRazorComponents<App>().AddInteractiveServerRenderMode();

app.Run();
