using System.Net;
using JetBrains.Annotations;

namespace PgRestWms.Web.PostgRest;

/// <summary>
/// An error answer of PostgREST: <c>{code, message, details, hint}</c>, where code is the SQLSTATE raised by the
/// database (or PGRSTxxx from PostgREST itself). <see cref="Field"/> and <see cref="UserMessage"/> turn it into what a
/// form shows next to an input.
/// </summary>
[UsedImplicitly(ImplicitUseTargetFlags.Members)] // Code and Details: for logs and the debugger.
public sealed class PostgRestException(
    HttpStatusCode status,
    string? code,
    string message,
    string? details,
    string? hint
) : Exception(message)
{
    public HttpStatusCode Status { get; } = status;

    /// <summary>SQLSTATE from the database (23505, PT409, ...) or PGRSTxxx from PostgREST.</summary>
    public string? Code { get; } = code;

    /// <summary>PostgREST's "details", e.g. which key already exists.</summary>
    public string? Details { get; } = details;

    private string? Hint { get; } = hint;

    /// <summary>The token is missing, expired or no longer valid: the session has to start again.</summary>
    public bool SessionExpired => Status == HttpStatusCode.Unauthorized;

    /// <summary>The form field the error belongs to, or null for a message above the form.</summary>
    public string? Field => Known?.Field ?? (IsFieldName(Hint) ? Hint : null);

    /// <summary>The database's own message, made friendlier where a constraint is known.</summary>
    public string UserMessage => Known?.Message ?? Message;

    private (string Field, string Message)? Known
    {
        get
        {
            foreach (var (constraint, column, message) in Constraints)
            {
                if (Message.Contains($"\"{constraint}\"", StringComparison.Ordinal))
                {
                    return (column, message);
                }
            }
            return null;
        }
    }

    // The functions of db/init/020-api.sql put the argument's name into HINT (quantity, product_id, ...).
    private static bool IsFieldName(string? hint) =>
        hint is { Length: > 0 } && hint.All(c => char.IsAsciiLetterLower(c) || c == '_');

    // Constraints of db/init/010-tables.sql that a form can break; PostgREST reports them by name.
    private static readonly (string Constraint, string Field, string Message)[] Constraints =
    [
        ("products_sku_key", "sku", "A product with this SKU already exists."),
        (
            "products_sku_check",
            "sku",
            "SKU: up to 32 characters - letters, digits, '.', '_' or '-'."
        ),
        ("products_name_check", "name", "Name: 1 to 200 characters."),
        ("products_unit_check", "unit", "Choose a unit from the list."),
        ("warehouses_code_key", "code", "A warehouse with this code already exists."),
        (
            "warehouses_code_check",
            "code",
            "Code: up to 16 characters - letters, digits, '.', '_' or '-'."
        ),
        ("warehouses_name_check", "name", "Name: 1 to 200 characters."),
        ("movements_note_check", "note", "Note: up to 500 characters."),
    ];
}
