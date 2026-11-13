using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Configurations;
using DotNet.Testcontainers.Containers;
using DotNet.Testcontainers.Networks;
using JetBrains.Annotations;
using Testcontainers.PostgreSql;

namespace PgRestWms.Tests.Api;

/// <summary>
/// The stack of infrastructure/docker-compose.yml without the Blazor host: a throw-away PostgreSQL 18.6 initialised by
/// db/init (exactly as the compose volume) and PostgREST connecting as authenticator. Secrets are random per run.
/// </summary>
// Created by xUnit for ICollectionFixture<PostgRestFixture>.
[UsedImplicitly]
public sealed class PostgRestFixture : IAsyncLifetime
{
    public const string Username = "tester";
    public static readonly string Password = $"pw-{Guid.NewGuid():N}";
    private static readonly string JwtSecret = $"{Guid.NewGuid():N}{Guid.NewGuid():N}";
    private static readonly string AuthenticatorPassword = $"{Guid.NewGuid():N}";

    private readonly INetwork _network = new NetworkBuilder().Build();
    private PostgreSqlContainer? _db;
    private IContainer? _api;

    private Uri BaseAddress { get; set; } = null!;

    public async ValueTask InitializeAsync()
    {
        await _network.CreateAsync();
        _db = new PostgreSqlBuilder("postgres:18.6-alpine")
            .WithNetwork(_network)
            .WithNetworkAliases("db")
            .WithDatabase("wms")
            .WithUsername("postgres")
            .WithPassword($"{Guid.NewGuid():N}")
            .WithEnvironment("AUTHENTICATOR_PASSWORD", AuthenticatorPassword)
            .WithEnvironment("JWT_SECRET", JwtSecret)
            .WithEnvironment("JWT_LIFETIME", "1 hour")
            .WithEnvironment("APP_USERNAME", Username)
            .WithEnvironment("APP_PASSWORD", Password)
            .WithEnvironment("DEMO_DATA", "false")
            .WithBindMount(InitDirectory(), "/docker-entrypoint-initdb.d", AccessMode.ReadOnly)
            .Build();
        await _db.StartAsync();

        _api = new ContainerBuilder("postgrest/postgrest:v16.5")
            .WithNetwork(_network)
            .WithEnvironment(
                "PGRST_DB_URI",
                $"postgres://authenticator:{AuthenticatorPassword}@db:5432/wms"
            )
            .WithEnvironment("PGRST_DB_SCHEMAS", "api")
            .WithEnvironment("PGRST_DB_ANON_ROLE", "web_anon")
            .WithEnvironment("PGRST_JWT_SECRET", JwtSecret)
            .WithPortBinding(3000, true)
            // PostgREST answers 503 until its pool and schema cache are ready (the database restarts after init).
            .WithWaitStrategy(
                Wait.ForUnixContainer()
                    .UntilHttpRequestIsSucceeded(request => request.ForPort(3000).ForPath("/"))
            )
            .Build();
        await _api.StartAsync();

        BaseAddress = new Uri($"http://{_api.Hostname}:{_api.GetMappedPublicPort(3000)}/");
    }

    public async ValueTask DisposeAsync()
    {
        if (_api is not null)
        {
            await _api.DisposeAsync();
        }
        if (_db is not null)
        {
            await _db.DisposeAsync();
        }
        await _network.DisposeAsync();
    }

    public HttpClient Anonymous() => new() { BaseAddress = BaseAddress };

    /// <summary>A client with the token of <see cref="Username"/> from rpc/login.</summary>
    public async Task<HttpClient> SignedInAsync()
    {
        var client = Anonymous();
        var token = await LoginAsync(client, Username, Password);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    public static async Task<string> LoginAsync(HttpClient client, string username, string password)
    {
        var response = await client.PostAsJsonAsync("rpc/login", new { username, password });
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<JsonObject>();
        return body?["token"]?.GetValue<string>()
            ?? throw new InvalidOperationException("No token in the response.");
    }

    /// <summary>SQL as the superuser, for tests that change the schema like a developer would.</summary>
    public async Task SqlAsync(string sql)
    {
        var db = _db ?? throw new InvalidOperationException("Not started.");
        var result = await db.ExecAsync([
            "psql",
            "-U",
            "postgres",
            "-d",
            "wms",
            "-v",
            "ON_ERROR_STOP=1",
            "-c",
            sql,
        ]);
        if (result.ExitCode != 0)
        {
            throw new InvalidOperationException($"psql failed: {result.Stderr}");
        }
    }

    private static string InitDirectory()
    {
        for (
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            dir is not null;
            dir = dir.Parent
        )
        {
            if (File.Exists(Path.Combine(dir.FullName, "PgRestWms.slnx")))
            {
                return Path.Combine(dir.FullName, "db", "init");
            }
        }
        throw new InvalidOperationException("Repository root (PgRestWms.slnx) not found.");
    }
}

[CollectionDefinition(Name)]
public sealed class PostgRestCollection : ICollectionFixture<PostgRestFixture>
{
    public const string Name = "PostgREST";
}
