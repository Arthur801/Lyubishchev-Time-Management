using Lyubishchev_Time_Management.Data;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

namespace WebFlow.Tests;

/// <summary>
/// Hosts the real application (Program.cs, unmodified pipeline) with two swaps needed to run it
/// off-network: the MySQL-backed AppDbContext is replaced with a SQLite shared-cache in-memory
/// one (same technique as TestDatabase in the other test projects), and the "Testing"
/// environment name is set so Program.cs maps its test-only /api/test/throw and /test/throw
/// endpoints, which exist solely to force an unhandled exception through the real middleware.
/// </summary>
public sealed class CustomWebApplicationFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly string _connectionString = $"Data Source=file:{Guid.NewGuid():N}?mode=memory&cache=shared;Default Timeout=5";
    private SqliteConnection? _keepAlive;

    public CapturingLoggerProvider Logs { get; } = new();

    public CustomWebApplicationFactory()
    {
        // WebApplication.CreateBuilder(args) inside Program.cs reads environment variables as
        // part of its own default configuration sources -- unlike ConfigureWebHost's
        // ConfigureAppConfiguration hook, which composes too late to satisfy Program.cs's own
        // connection-string/signing-key guards that run immediately after CreateBuilder returns.
        // The DbContext this connection string would build is replaced below before anything
        // ever actually connects with it.
        Environment.SetEnvironmentVariable("ConnectionStrings__DefaultConnection", "unused-placeholder");
        Environment.SetEnvironmentVariable("Jwt__Issuer", "WebFlowTestsIssuer");
        Environment.SetEnvironmentVariable("Jwt__Audience", "WebFlowTestsAudience");
        Environment.SetEnvironmentVariable("Jwt__SigningKey", "webflow-tests-signing-key-needs-at-least-32-bytes!!");
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");

        builder.ConfigureLogging(logging => logging.AddProvider(Logs));

        builder.ConfigureServices(services =>
        {
            // Program.cs skips its own AddDbContext call in the "Testing" environment (set
            // above) specifically so this is the only AppDbContext registration in play.
            _keepAlive = new SqliteConnection(_connectionString);
            _keepAlive.Open();
            services.AddDbContext<AppDbContext>(options => options.UseSqlite(_connectionString));
        });
    }

    public async Task InitializeAsync()
    {
        // Force host creation (ConfigureWebHost above) before touching the database.
        using var client = CreateClient();
        using var scope = Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await dbContext.Database.EnsureCreatedAsync();
    }

    public new Task DisposeAsync()
    {
        _keepAlive?.Dispose();
        return base.DisposeAsync().AsTask();
    }
}
