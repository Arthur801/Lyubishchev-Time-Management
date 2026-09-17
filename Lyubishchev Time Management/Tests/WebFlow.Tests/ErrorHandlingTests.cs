using System.Net;
using System.Net.Http.Json;
using System.Text.RegularExpressions;
using Lyubishchev_Time_Management.Data;
using Lyubishchev_Time_Management.Infrastructure.Clock;
using Lyubishchev_Time_Management.Infrastructure.Logging;
using Lyubishchev_Time_Management.Models.Entities;
using Lyubishchev_Time_Management.Services;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Xunit;

namespace WebFlow.Tests;

public sealed class ErrorHandlingTests(CustomWebApplicationFactory factory) : IClassFixture<CustomWebApplicationFactory>
{
    private static async Task<string> GetCsrfTokenAsync(HttpClient client, string path)
    {
        var html = await client.GetStringAsync(path);
        var match = Regex.Match(html, "name=\"csrf-token\" content=\"([^\"]+)\"");
        Assert.True(match.Success, $"csrf token not found on {path}");
        return match.Groups[1].Value;
    }

    // AccountController's auth cookie is marked Secure, so a CookieContainer only keeps it for an
    // https origin -- WebApplicationFactory.CreateClient()'s default http://localhost base address
    // would silently drop it (Register would still "succeed", but every later request looks
    // unauthenticated). The in-memory TestServer transport doesn't care about the scheme itself;
    // this only affects how HttpClient's cookie jar treats Secure cookies.
    private static readonly Uri HttpsBaseAddress = new("https://localhost");

    private static async Task<HttpClient> CreateAuthenticatedClientAsync(CustomWebApplicationFactory factory)
    {
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = HttpsBaseAddress });
        var csrf = await GetCsrfTokenAsync(client, "/Account/Register");

        var request = new HttpRequestMessage(HttpMethod.Post, "/Account/Register")
        {
            Content = JsonContent.Create(new
            {
                email = $"webflow_{Guid.NewGuid():N}@example.com",
                password = "P@ssword123!",
                confirmPassword = "P@ssword123!",
            }),
        };
        request.Headers.Add("X-CSRF-TOKEN", csrf);

        var response = await client.SendAsync(request);
        response.EnsureSuccessStatusCode();
        return client;
    }

    // ----- Task 1: global exception handler contract -----

    [Fact]
    public async Task Throwing_api_endpoint_returns_safe_problem_details_with_trace_id()
    {
        // Isolated (not the shared fixture): asserts on the *only* captured log entry, which
        // would be ambiguous against the shared factory's cumulative log history once other
        // tests also throw through it.
        await using var isolatedFactory = new CustomWebApplicationFactory();
        await isolatedFactory.InitializeAsync();
        var client = isolatedFactory.CreateClient();

        var response = await client.GetAsync("/api/test/throw");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.DoesNotContain("secret exception text", body);
        Assert.Contains("INTERNAL_SERVER_ERROR", body);

        var payload = await response.Content.ReadFromJsonAsync<Dictionary<string, object>>();
        Assert.True(payload!.TryGetValue("traceId", out var traceId));
        Assert.False(string.IsNullOrWhiteSpace(traceId?.ToString()));

        var logged = Assert.Single(isolatedFactory.Logs.Entries, e => e.Message == "Unhandled request exception.");
        Assert.Equal(LogLevel.Error, logged.Level);
        Assert.IsType<InvalidOperationException>(logged.Exception);
    }

    [Fact]
    public async Task Throwing_html_endpoint_renders_safe_error_page_with_trace_id()
    {
        await using var isolatedFactory = new CustomWebApplicationFactory();
        await isolatedFactory.InitializeAsync();
        var client = isolatedFactory.CreateClient();

        var response = await client.GetAsync("/test/throw");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Equal("text/html", response.Content.Headers.ContentType?.MediaType);
        Assert.DoesNotContain("secret exception text", body);
        Assert.DoesNotContain("Development Mode", body);
        Assert.Contains("Request ID", body);

        var logged = Assert.Single(isolatedFactory.Logs.Entries, e => e.Message == "Unhandled request exception.");
        Assert.Equal(LogLevel.Error, logged.Level);
        Assert.IsType<InvalidOperationException>(logged.Exception);
    }

    // ----- Task 2: safe operational event logging -----

    private static async Task<(SqliteConnection KeepAlive, DbContextOptions<AppDbContext> Options, ulong UserId)> CreateSharedDatabaseAsync()
    {
        var connectionString = $"Data Source=file:{Guid.NewGuid():N}?mode=memory&cache=shared;Default Timeout=5";
        var keepAlive = new SqliteConnection(connectionString);
        await keepAlive.OpenAsync();

        var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connectionString).Options;

        ulong userId;
        await using (var initContext = new AppDbContext(options))
        {
            await initContext.Database.EnsureCreatedAsync();
            var user = new User
            {
                Email = $"{Guid.NewGuid():N}@example.com",
                PasswordHash = "not-a-real-hash",
                TimeZoneId = "Asia/Taipei",
                CreatedAtUtc = DateTime.UtcNow,
                UpdatedAtUtc = DateTime.UtcNow,
            };
            initContext.Users.Add(user);
            await initContext.SaveChangesAsync();
            userId = user.Id;
        }

        return (keepAlive, options, userId);
    }

    private static CapturedLogEntry? FindState(IReadOnlyList<CapturedLogEntry> entries, string key, object? expected)
        => entries.FirstOrDefault(e => e.State.Any(kv => kv.Key == key && Equals(kv.Value, expected)));

    [Fact]
    public async Task TimerService_unexpected_failure_logs_safe_metadata_and_rethrows()
    {
        var (keepAlive, options, userId) = await CreateSharedDatabaseAsync();
        await using var _ = keepAlive;

        await using (var seedContext = new AppDbContext(options))
        {
            seedContext.RunningTimers.Add(new RunningTimer { UserId = userId, StartedAtUtc = DateTime.UtcNow.AddMinutes(-5), User = null! });
            await seedContext.SaveChangesAsync();
        }

        var provider = new CapturingLoggerProvider();
        using var loggerFactory = LoggerFactory.Create(builder => builder.AddProvider(provider));
        var operationalLogger = new OperationalEventLogger(loggerFactory.CreateLogger<OperationalEventLogger>());

        var dbContext = new AppDbContext(options);
        await dbContext.DisposeAsync(); // every subsequent call on this instance now throws ObjectDisposedException

        var clock = new SystemClock();
        var timerService = new TimerService(dbContext, clock, new TimeEntryService(dbContext, clock), operationalLogger);

        await Assert.ThrowsAnyAsync<ObjectDisposedException>(
            () => timerService.StopAsync(userId, "test", null, null, CancellationToken.None));

        var entries = provider.Entries;
        var failure = Assert.Single(entries, e => e.EventId.Id == 1001);
        Assert.Equal(LogLevel.Error, failure.Level);
        Assert.NotNull(failure.Exception);
        Assert.NotNull(FindState(entries, "UserId", userId));
        Assert.NotNull(FindState(entries, "Operation", "Stop"));
    }

    [Fact]
    public async Task CsvExportService_unexpected_failure_logs_safe_metadata_and_rethrows()
    {
        var (keepAlive, options, userId) = await CreateSharedDatabaseAsync();
        await using var _ = keepAlive;

        var provider = new CapturingLoggerProvider();
        using var loggerFactory = LoggerFactory.Create(builder => builder.AddProvider(provider));
        var operationalLogger = new OperationalEventLogger(loggerFactory.CreateLogger<OperationalEventLogger>());

        var dbContext = new AppDbContext(options);
        await dbContext.DisposeAsync();

        var clock = new SystemClock();
        var catalog = new Lyubishchev_Time_Management.Infrastructure.Time.TimeZoneCatalog();
        var csvExportService = new CsvExportService(dbContext, new UserSettingsService(dbContext, clock, catalog), catalog, operationalLogger);

        const string secretSearchText = "mysecretquery";
        var request = new CsvExportRequest(DateTime.UtcNow.AddDays(-1), DateTime.UtcNow, CategoryId: 42, Search: secretSearchText);

        await Assert.ThrowsAnyAsync<ObjectDisposedException>(() => csvExportService.ExportAsync(userId, request, CancellationToken.None));

        var entries = provider.Entries;
        var failure = Assert.Single(entries, e => e.EventId.Id == 1002);
        Assert.Equal(LogLevel.Error, failure.Level);
        Assert.NotNull(failure.Exception);
        Assert.NotNull(FindState(entries, "UserId", userId));
        Assert.NotNull(FindState(entries, "HasRange", true));
        Assert.NotNull(FindState(entries, "HasCategoryFilter", true));
        Assert.NotNull(FindState(entries, "HasSearch", true));
        Assert.DoesNotContain(secretSearchText, failure.Message);
        Assert.DoesNotContain(entries, e => e.State.Any(kv => Equals(kv.Value, secretSearchText)));
    }

    [Fact]
    public async Task TimerService_known_concurrency_outcome_is_not_logged_as_an_operational_error()
    {
        var (keepAlive, options, userId) = await CreateSharedDatabaseAsync();
        await using var _ = keepAlive;
        await using var dbContext = new AppDbContext(options);

        var provider = new CapturingLoggerProvider();
        using var loggerFactory = LoggerFactory.Create(builder => builder.AddProvider(provider));
        var operationalLogger = new OperationalEventLogger(loggerFactory.CreateLogger<OperationalEventLogger>());

        var clock = new SystemClock();
        var timerService = new TimerService(dbContext, clock, new TimeEntryService(dbContext, clock), operationalLogger);

        var result = await timerService.StopAsync(userId, null, null, null, CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal("TIMER_NOT_RUNNING", result.ErrorCode);
        Assert.DoesNotContain(provider.Entries, e => e.EventId.Id == 1001);
    }

    // ----- Task 3: known failures keep their documented status codes -----

    [Fact]
    public async Task Csv_export_with_an_invalid_range_still_returns_400()
    {
        var client = await CreateAuthenticatedClientAsync(factory);

        var start = DateTime.UtcNow.ToString("O");
        var end = DateTime.UtcNow.AddDays(-1).ToString("O");
        var response = await client.GetAsync($"/api/time-entries/export?startUtc={Uri.EscapeDataString(start)}&endUtc={Uri.EscapeDataString(end)}");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("INVALID_TIME_RANGE", body);
    }

    [Fact]
    public async Task Csv_export_without_authentication_still_returns_401()
    {
        var client = factory.CreateClient();

        var response = await client.GetAsync("/api/time-entries/export");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Timer_stop_with_no_running_timer_still_returns_404()
    {
        var client = await CreateAuthenticatedClientAsync(factory);
        var csrf = await GetCsrfTokenAsync(client, "/Dashboard");

        var request = new HttpRequestMessage(HttpMethod.Post, "/api/timer/stop")
        {
            Content = JsonContent.Create(new { name = "test", categoryId = (ulong?)null, tags = Array.Empty<string>() }),
        };
        request.Headers.Add("X-CSRF-TOKEN", csrf);

        var response = await client.SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Contains("TIMER_NOT_RUNNING", body);
    }

    [Fact]
    public async Task Rate_limit_rejection_still_returns_429()
    {
        // Isolated from the shared fixture: this consumes the entire 10-per-5-minutes "auth"
        // budget for whatever IP TestServer assigns these requests, which would otherwise break
        // every other test in this class that needs to Register/Login through the same factory.
        await using var isolatedFactory = new CustomWebApplicationFactory();
        await isolatedFactory.InitializeAsync();
        var client = isolatedFactory.CreateClient();
        var csrf = await GetCsrfTokenAsync(client, "/Account/Login");

        HttpResponseMessage? last = null;
        for (var attempt = 0; attempt < 11; attempt++)
        {
            var request = new HttpRequestMessage(HttpMethod.Post, "/Account/Login")
            {
                Content = JsonContent.Create(new { email = "nobody@example.com", password = "wrong-password" }),
            };
            request.Headers.Add("X-CSRF-TOKEN", csrf);
            last = await client.SendAsync(request);
        }

        Assert.Equal(HttpStatusCode.TooManyRequests, last!.StatusCode);
        var body = await last.Content.ReadAsStringAsync();
        Assert.Contains("TOO_MANY_REQUESTS", body);
    }
}
