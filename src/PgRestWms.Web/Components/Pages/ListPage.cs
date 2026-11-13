using Microsoft.AspNetCore.Components;
using PgRestWms.Web.Components.Ui;
using PgRestWms.Web.PostgRest;

namespace PgRestWms.Web.Components.Pages;

/// <summary>
/// A list over one api view. Search, filters, sort and page live in the URL (?q=, ?sort=, ?desc=, ?page=, ?size=), so a
/// list can be linked and Back works; PostgREST does the filtering, sorting and counting.
/// </summary>
public abstract class ListPage<TRow> : ApiPage
{
    [SupplyParameterFromQuery(Name = "q")]
    public string? SearchParameter { get; set; }

    [SupplyParameterFromQuery(Name = "sort")]
    public string? SortParameter { get; set; }

    [SupplyParameterFromQuery(Name = "desc")]
    public bool? DescParameter { get; set; }

    [SupplyParameterFromQuery(Name = "page")]
    public int? PageParameter { get; set; }

    [SupplyParameterFromQuery(Name = "size")]
    public int? SizeParameter { get; set; }

    // [PersistentState] restores public, settable properties only.
    // ReSharper disable MemberCanBeProtected.Global, MemberCanBePrivate.Global, PropertyCanBeMadeInitOnly.Global, AutoPropertyCanBeMadeGetOnly.Global

    /// <summary>Loaded while prerendering and handed to the circuit, so the page does not ask PostgREST twice.</summary>
    [PersistentState]
    public PageOf<TRow>? Rows { get; set; }

    [PersistentState]
    public string? LoadedUrl { get; set; }

    // ReSharper restore MemberCanBeProtected.Global, MemberCanBePrivate.Global, PropertyCanBeMadeInitOnly.Global, AutoPropertyCanBeMadeGetOnly.Global

    protected string SearchText { get; set; } = "";

    protected abstract string Resource { get; }

    /// <summary>Columns the list may be sorted by; the first one is the default.</summary>
    protected abstract IReadOnlyList<string> SortColumns { get; }

    /// <summary>A unique column, so rows with equal sort values keep their order between pages.</summary>
    protected abstract string TieBreaker { get; }

    protected virtual bool DefaultDescending => false;

    protected virtual void Filter(PostgRestQuery query) { }

    protected string Sort => SortParameter is { } s && SortColumns.Contains(s) ? s : SortColumns[0];
    protected bool Descending => SortParameter is null ? DefaultDescending : DescParameter == true;
    protected int PageNumber => Math.Max(1, PageParameter ?? 1);
    protected int PageSize => SizeParameter is { } s && Pager.Sizes.Contains(s) ? s : 20;

    protected override async Task OnParametersSetAsync()
    {
        SearchText = SearchParameter ?? "";
        await LoadAsync(force: false);
    }

    protected async Task LoadAsync(bool force = true)
    {
        var query = new PostgRestQuery(Resource);
        Filter(query);
        query.OrderBy(Sort, Descending, TieBreaker).Page(PageNumber, PageSize);
        var url = query.ToRelativeUrl();
        if (!force && Rows is not null && url == LoadedUrl)
        {
            return;
        }
        await RunAsync(async () =>
        {
            Rows = await Api.ListAsync<TRow>(query);
            LoadedUrl = url;
        });
    }

    /// <summary>Same column again flips the direction; another column starts ascending.</summary>
    protected string SortHref(string column) =>
        Href(
            new()
            {
                ["sort"] = column,
                ["desc"] = column == Sort && !Descending ? true : null,
                ["page"] = null,
            }
        );

    protected string PageHref(int page) => Href(new() { ["page"] = page == 1 ? null : page });

    protected string SizeHref(int size) =>
        Href(new() { ["size"] = size == 20 ? null : size, ["page"] = null });

    protected void ApplySearch() =>
        SetFilter("q", string.IsNullOrWhiteSpace(SearchText) ? null : SearchText.Trim());

    /// <summary>A new filter value starts again on page 1.</summary>
    protected void SetFilter(string name, string? value) =>
        Navigation.NavigateTo(
            Href(new() { [name] = string.IsNullOrEmpty(value) ? null : value, ["page"] = null })
        );
}
