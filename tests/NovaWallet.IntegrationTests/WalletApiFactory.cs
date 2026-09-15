using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Testcontainers.PostgreSql;

namespace NovaWallet.IntegrationTests;

/// <summary>
/// Boots the real host (Program.cs, unmodified) against a throwaway Postgres container. The
/// container is started once per test class (see IClassFixture usage) and the host's own
/// startup migration step (see Program.cs) creates the schema — no separate migration
/// bootstrapping is needed here.
/// </summary>
public sealed class WalletApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private const string JwtSigningKey = "integration-test-signing-key-at-least-32-bytes-long!";
    private const string JwtIssuer = "novawallet-ledger";
    private const string JwtAudience = "novawallet-clients";

    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder()
        .WithImage("postgres:16-alpine")
        .WithDatabase("novawallet_test")
        .WithUsername("novawallet")
        .WithPassword("novawallet")
        .Build();

    public Task InitializeAsync() => _postgres.StartAsync();

    Task IAsyncLifetime.DisposeAsync() => _postgres.DisposeAsync().AsTask();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.ConfigureAppConfiguration((_, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Default"] = _postgres.GetConnectionString(),
                ["Jwt:SigningKey"] = JwtSigningKey,
                ["Jwt:Issuer"] = JwtIssuer,
                ["Jwt:Audience"] = JwtAudience,
            });
        });
    }

    /// <summary>Goes through the real (dev-only) token endpoint rather than minting a token in-process, so tests exercise the actual auth middleware.</summary>
    public async Task<string> IssueTokenAsync(HttpClient client, Guid customerId, string role = "customer")
    {
        var response = await client.PostAsJsonAsync("/api/auth/dev-token", new { customerId, role });
        response.EnsureSuccessStatusCode();
        var payload = await response.Content.ReadFromJsonAsync<TokenResponse>();
        return payload!.AccessToken;
    }

    public HttpClient CreateAuthenticatedClient(string accessToken)
    {
        var client = CreateClient();
        client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);
        return client;
    }

    private sealed record TokenResponse(string AccessToken, DateTime ExpiresAtUtc);
}
