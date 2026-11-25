using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Npgsql;

namespace FaizaGold.Tests;

/// <summary>
/// Поднимает API на отдельной тестовой базе PostgreSQL. Строка подключения к серверу берётся из
/// переменной FAIZA_TEST_DB (например Host=localhost;Username=postgres;Password=...).
/// База faiza_test_* создаётся заново при каждом запуске и удаляется в конце.
/// </summary>
public sealed class ApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    public static readonly string? ServerConnection = Environment.GetEnvironmentVariable("FAIZA_TEST_DB");
    private readonly string _database = $"faiza_test_{Guid.NewGuid():N}"[..24];
    public const string AdminPassword = "Admin12345";
    public const string DemoPassword = "Demo12345";

    private string ConnectionString => new NpgsqlConnectionStringBuilder(ServerConnection) { Database = _database }.ToString();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("ConnectionStrings:Default", ConnectionString);
        builder.UseSetting("Seed:AdminLogin", "admin");
        builder.UseSetting("Seed:AdminPassword", AdminPassword);
        builder.UseSetting("Seed:Demo", "true");
        builder.UseSetting("Seed:DemoPassword", DemoPassword);
        builder.UseSetting("Security:LoginAttemptsPerMinute", "1000");
    }

    public async Task InitializeAsync()
    {
        await using var conn = new NpgsqlConnection(ServerConnection);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand($"CREATE DATABASE \"{_database}\"", conn);
        await cmd.ExecuteNonQueryAsync();
        _ = Server; // запускает приложение: миграции и начальные данные
    }

    async Task IAsyncLifetime.DisposeAsync()
    {
        await base.DisposeAsync();
        NpgsqlConnection.ClearAllPools();
        await using var conn = new NpgsqlConnection(ServerConnection);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand($"DROP DATABASE IF EXISTS \"{_database}\" WITH (FORCE)", conn);
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task<HttpClient> LoginAsync(string login, string? password = null)
    {
        var client = CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });
        var res = await client.PostAsJsonAsync("/api/auth/login", new { login, password = password ?? DemoPassword });
        res.EnsureSuccessStatusCode();
        return client;
    }

    public string DbConnectionString => ConnectionString;
}

/// <summary>Тест запускается, только если задана тестовая база PostgreSQL.</summary>
public sealed class PostgresFactAttribute : FactAttribute
{
    public PostgresFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(ApiFactory.ServerConnection))
            Skip = "Задайте FAIZA_TEST_DB, чтобы запустить интеграционные тесты с PostgreSQL.";
    }
}
