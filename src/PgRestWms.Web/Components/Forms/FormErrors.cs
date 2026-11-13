using PgRestWms.Web.PostgRest;

namespace PgRestWms.Web.Components.Forms;

/// <summary>
/// What a form shows after PostgREST refused a write: the database's message next to the field it names, or above the
/// form when it names none of the form's fields.
/// </summary>
public sealed class FormErrors(params string[] fields)
{
    private readonly Dictionary<string, string> _byField = [];

    public string? Form { get; private set; }

    public string? this[string field] => _byField.GetValueOrDefault(field);

    public bool Any => Form is not null || _byField.Count > 0;

    public void Clear()
    {
        Form = null;
        _byField.Clear();
    }

    public void Set(string field, string message) => _byField[field] = message;

    public void Show(PostgRestException error)
    {
        if (error.Field is { } field && fields.Contains(field))
        {
            _byField[field] = error.UserMessage;
        }
        else
        {
            Form = error.UserMessage;
        }
    }

    public void ShowUnreachable() =>
        Form = "PostgREST is not reachable. Is the api container running?";
}
