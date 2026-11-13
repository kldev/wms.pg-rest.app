using Microsoft.AspNetCore.Components;
using PgRestWms.Web.PostgRest;

namespace PgRestWms.Web.Components.Pages;

/// <summary>A page that calls PostgREST: one place that turns its errors into a message or a new sign-in.</summary>
public abstract class ApiPage : ComponentBase
{
    [Inject]
    protected PostgRestClient Api { get; set; } = null!;

    [Inject]
    protected NavigationManager Navigation { get; set; } = null!;

    /// <summary>The error of the last call, shown above the content.</summary>
    protected string? Error { get; private set; }

    /// <summary>Runs a call; false when it failed (then <see cref="Error"/> says why).</summary>
    protected async Task<bool> RunAsync(Func<Task> call)
    {
        Error = null;
        try
        {
            await call();
            return true;
        }
        catch (PostgRestException e) when (e.SessionExpired)
        {
            Navigation.NavigateTo("auth/session-expired", forceLoad: true);
        }
        catch (PostgRestException e)
        {
            Error = e.UserMessage;
        }
        catch (HttpRequestException)
        {
            Error = "PostgREST is not reachable. Is the api container running?";
        }
        return false;
    }

    /// <summary>The current page's URL with some query parameters changed (null removes one).</summary>
    protected string Href(Dictionary<string, object?> changes) =>
        Navigation.GetUriWithQueryParameters(changes);
}
