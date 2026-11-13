using JetBrains.Annotations;

namespace PgRestWms.Web.PostgRest;

// Rows of the api schema's views, as PostgREST returns them (snake_case JSON, see PostgRestClient). Only the
// columns the pages show; PostgREST sends more, the deserializer skips them.

public sealed record Product(
    Guid Id,
    string Sku,
    string Name,
    string Unit,
    DateTimeOffset UpdatedAt
);

public sealed record Warehouse(Guid Id, string Code, string Name, DateTimeOffset UpdatedAt);

public sealed record StockRow(
    Guid ProductId,
    string Sku,
    string ProductName,
    string Unit,
    Guid WarehouseId,
    string WarehouseCode,
    string WarehouseName,
    decimal Quantity,
    DateTimeOffset UpdatedAt
);

public sealed record Movement(
    string Kind,
    string Sku,
    string ProductName,
    string Unit,
    string WarehouseCode,
    decimal Quantity,
    string? Note,
    string CreatedBy,
    DateTimeOffset CreatedAt
);

/// <summary>The answer of rpc/receive and rpc/issue: the stock after the change.</summary>
[UsedImplicitly(ImplicitUseKindFlags.InstantiatedNoFixedConstructorSignature)]
public sealed record StockChange(decimal Quantity);

[UsedImplicitly(ImplicitUseKindFlags.InstantiatedNoFixedConstructorSignature)]
public sealed record LoginResult(string Token, string Username, DateTimeOffset ExpiresAt);

public sealed record PageOf<T>(IReadOnlyList<T> Items, int Total);

public static class Units
{
    public static readonly string[] All = ["pcs", "kg", "m", "l"];
}

public static class MovementKinds
{
    public const string Receipt = "receipt";
    public const string Issue = "issue";
}
