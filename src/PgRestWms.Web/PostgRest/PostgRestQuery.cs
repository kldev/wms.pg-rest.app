using System.Text;

namespace PgRestWms.Web.PostgRest;

/// <summary>
/// A PostgREST read: filters, order and paging as query parameters (https://docs.postgrest.org - "Tables and Views").
/// Column names come from code, never from the user; values are escaped.
/// </summary>
public sealed class PostgRestQuery(string resource)
{
    private readonly List<KeyValuePair<string, string>> _parameters = [];

    private string Resource { get; } = resource;
    private int Offset { get; set; }
    private int Limit { get; set; } = 50;

    public PostgRestQuery Eq(string column, string? value)
    {
        if (!string.IsNullOrEmpty(value))
        {
            _parameters.Add(new(column, $"eq.{value}"));
        }
        return this;
    }

    /// <summary>Case-insensitive "contains" over several columns: <c>or=(a.ilike."*x*",b.ilike."*x*")</c>.</summary>
    public PostgRestQuery Search(string? text, params string[] columns)
    {
        if (!string.IsNullOrWhiteSpace(text))
        {
            var pattern = Quote($"*{text.Trim()}*");
            _parameters.Add(
                new("or", $"({string.Join(',', columns.Select(c => $"{c}.ilike.{pattern}"))})")
            );
        }
        return this;
    }

    /// <summary>Order by one column, then by <paramref name="tieBreaker"/> so pages are stable.</summary>
    public PostgRestQuery OrderBy(string column, bool descending, string tieBreaker)
    {
        var direction = descending ? "desc" : "asc";
        var order =
            column == tieBreaker
                ? $"{column}.{direction}"
                : $"{column}.{direction},{tieBreaker}.asc";
        _parameters.Add(new("order", order));
        return this;
    }

    public PostgRestQuery Page(int page, int size)
    {
        Limit = size;
        Offset = Math.Max(0, page - 1) * size;
        return this;
    }

    public string ToRelativeUrl()
    {
        var url = new StringBuilder(Resource);
        var parameters = _parameters.Concat([
            new("limit", Limit.ToString()),
            new("offset", Offset.ToString()),
        ]);
        var first = true;
        foreach (var (key, value) in parameters)
        {
            url.Append(first ? '?' : '&')
                .Append(key)
                .Append('=')
                .Append(Uri.EscapeDataString(value));
            first = false;
        }
        return url.ToString();
    }

    /// <summary>PostgREST's quoting for values with reserved characters (<c>,.:()</c>) inside <c>or=(...)</c>.</summary>
    private static string Quote(string value) =>
        $"\"{value.Replace("\\", "\\\\").Replace("\"", "\\\"")}\"";
}
