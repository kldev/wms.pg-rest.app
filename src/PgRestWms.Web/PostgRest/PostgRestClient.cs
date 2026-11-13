using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.AspNetCore.Components.Authorization;
using PgRestWms.Web.Auth;

namespace PgRestWms.Web.PostgRest;

/// <summary>
/// The only way the host talks to data: HTTP to PostgREST, with the signed-in user's token as a bearer. Every rule
/// (unique codes, stock never below zero, who may write) is checked by the database behind it.
/// </summary>
public sealed class PostgRestClient(
    HttpClient http,
    SessionTokens tokens,
    AuthenticationStateProvider auth
)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
    };

    /// <summary>rpc/login - the only call without a token.</summary>
    public async Task<LoginResult> LoginAsync(
        string username,
        string password,
        CancellationToken ct = default
    )
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "rpc/login");
        request.Content = JsonContent.Create(new { username, password }, options: Json);
        return await SendAsync<LoginResult>(request, ct);
    }

    public async Task<PageOf<T>> ListAsync<T>(PostgRestQuery query, CancellationToken ct = default)
    {
        using var request = await AuthorizedAsync(HttpMethod.Get, query.ToRelativeUrl());
        // Total rows of the filter, returned in Content-Range: "0-19/57" ("*/0" when empty).
        request.Headers.Add("Prefer", "count=exact");
        using var response = await http.SendAsync(request, ct);
        await EnsureSuccessAsync(response, ct);
        var items = await response.Content.ReadFromJsonAsync<List<T>>(Json, ct) ?? [];
        var range = response.Content.Headers.TryGetValues("Content-Range", out var values)
            ? values.FirstOrDefault()
            : null;
        var total =
            range?.Split('/') is [_, var count] && int.TryParse(count, out var n) ? n : items.Count;
        return new PageOf<T>(items, total);
    }

    public async Task<IReadOnlyList<T>> AllAsync<T>(
        string relativeUrl,
        CancellationToken ct = default
    )
    {
        using var request = await AuthorizedAsync(HttpMethod.Get, relativeUrl);
        return await SendAsync<List<T>>(request, ct);
    }

    public async Task<T> CreateAsync<T>(string resource, object row, CancellationToken ct = default)
    {
        using var request = await AuthorizedAsync(HttpMethod.Post, resource);
        request.Content = JsonContent.Create(row, options: Json);
        // The inserted row back, as one object instead of an array of one.
        request.Headers.Add("Prefer", "return=representation");
        request.Headers.Accept.Add(
            new MediaTypeWithQualityHeaderValue("application/vnd.pgrst.object+json")
        );
        return await SendAsync<T>(request, ct);
    }

    public async Task UpdateAsync(
        string resource,
        Guid id,
        object changes,
        CancellationToken ct = default
    )
    {
        using var request = await AuthorizedAsync(HttpMethod.Patch, $"{resource}?id=eq.{id}");
        request.Content = JsonContent.Create(changes, options: Json);
        using var response = await http.SendAsync(request, ct);
        await EnsureSuccessAsync(response, ct);
    }

    public async Task<T> RpcAsync<T>(
        string function,
        object arguments,
        CancellationToken ct = default
    )
    {
        using var request = await AuthorizedAsync(HttpMethod.Post, $"rpc/{function}");
        request.Content = JsonContent.Create(arguments, options: Json);
        return await SendAsync<T>(request, ct);
    }

    private async Task<HttpRequestMessage> AuthorizedAsync(HttpMethod method, string relativeUrl)
    {
        var state = await auth.GetAuthenticationStateAsync();
        var token = tokens.Find(state.User)
            ?? throw new PostgRestException(
                HttpStatusCode.Unauthorized,
                null,
                "Sign in first",
                null,
                null
            );
        var request = new HttpRequestMessage(method, relativeUrl);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return request;
    }

    private async Task<T> SendAsync<T>(HttpRequestMessage request, CancellationToken ct)
    {
        using var response = await http.SendAsync(request, ct);
        await EnsureSuccessAsync(response, ct);
        return await response.Content.ReadFromJsonAsync<T>(Json, ct)
            ?? throw new InvalidOperationException($"Empty answer from {request.RequestUri}.");
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken ct)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }
        PostgRestErrorBody? body = null;
        try
        {
            body = await response.Content.ReadFromJsonAsync<PostgRestErrorBody>(Json, ct);
        }
        catch (JsonException)
        {
            // Not PostgREST's JSON (a proxy's page, say) - the status alone below.
        }
        throw new PostgRestException(
            response.StatusCode,
            body?.Code,
            body?.Message ?? $"PostgREST answered {(int)response.StatusCode}",
            body?.Details,
            body?.Hint
        );
    }

    private sealed record PostgRestErrorBody(
        string? Code,
        string? Message,
        string? Details,
        string? Hint
    );
}
