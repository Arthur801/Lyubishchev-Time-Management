using System.Text;
using System.Threading.RateLimiting;
using Lyubishchev_Time_Management.Data;
using Lyubishchev_Time_Management.Infrastructure.Clock;
using Lyubishchev_Time_Management.Infrastructure.Errors;
using Lyubishchev_Time_Management.Infrastructure.Logging;
using Lyubishchev_Time_Management.Infrastructure.Time;
using Lyubishchev_Time_Management.Security;
using Lyubishchev_Time_Management.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using MySql.EntityFrameworkCore.Extensions;

var builder = WebApplication.CreateBuilder(args);

// Surfaces GlobalExceptionHandler's TraceId/RequestMethod/RequestPath scope in Console/systemd
// journal output; the default simple console formatter otherwise discards ILogger scopes.
builder.Logging.AddSimpleConsole(options => options.IncludeScopes = true);

// Add services to the container.
builder.Services.AddControllersWithViews();
builder.Services.AddHttpContextAccessor();

if (builder.Environment.IsEnvironment("Testing"))
{
    // WebFlow.Tests hosts this app via WebApplicationFactory<Program> and registers its own
    // SQLite-backed AppDbContext. A WebApplicationFactory's ConfigureServices override composes
    // before this file's own code runs (it uses a deferred host builder for minimal-hosting
    // apps), so an AddDbContext call here would simply overwrite that test override rather than
    // the other way around -- skipping registration entirely in this environment is what lets
    // the test project's own registration stick.
}
else
{
    var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");
    if (string.IsNullOrWhiteSpace(connectionString))
    {
        throw new InvalidOperationException(
            "Connection string 'DefaultConnection' is required. Set it with dotnet user-secrets before running the application.");
    }

    builder.Services.AddDbContext<AppDbContext>(options => options.UseMySQL(connectionString));
}

var jwtOptions = builder.Configuration.GetSection(JwtOptions.SectionName).Get<JwtOptions>()
    ?? throw new InvalidOperationException("Configuration section 'Jwt' is required.");
if (string.IsNullOrWhiteSpace(jwtOptions.SigningKey))
{
    throw new InvalidOperationException(
        "Jwt:SigningKey is required. Set it with dotnet user-secrets before running the application.");
}

builder.Services.Configure<JwtOptions>(builder.Configuration.GetSection(JwtOptions.SectionName));
builder.Services.AddSingleton<JwtTokenService>();
builder.Services.AddSingleton<IAuthEventLogger, AuthEventLogger>();
builder.Services.AddSingleton<IClock, SystemClock>();
builder.Services.AddScoped<CurrentUserService>();
builder.Services.AddScoped<AuthService>();
builder.Services.AddScoped<TimerService>();
builder.Services.AddScoped<TimeEntryService>();
builder.Services.AddScoped<CategoryService>();
builder.Services.AddScoped<TagService>();
builder.Services.AddSingleton<TimeZoneCatalog>();
builder.Services.AddScoped<UserSettingsService>();
builder.Services.AddScoped<TimeAggregationService>();
builder.Services.AddScoped<DashboardService>();
builder.Services.AddScoped<ReportService>();
builder.Services.AddScoped<CsvExportService>();
builder.Services.AddScoped<IOperationalEventLogger, OperationalEventLogger>();

builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();

builder.Services.AddAntiforgery(options => options.HeaderName = "X-CSRF-TOKEN");

builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

    // Basic brute-force protection for Login/Register: 10 attempts per IP per 5-minute window.
    options.AddPolicy(RateLimiterPolicies.Auth, httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 10,
                Window = TimeSpan.FromMinutes(5),
                QueueLimit = 0,
            }));

    options.OnRejected = async (context, cancellationToken) =>
    {
        var authEventLogger = context.HttpContext.RequestServices.GetRequiredService<IAuthEventLogger>();
        authEventLogger.RateLimitExceeded(context.HttpContext.Request.Path.Value ?? "unknown", context.HttpContext.Connection.RemoteIpAddress?.ToString());

        context.HttpContext.Response.ContentType = "application/problem+json";
        await context.HttpContext.Response.WriteAsJsonAsync(new
        {
            title = "TOO_MANY_REQUESTS",
            status = StatusCodes.Status429TooManyRequests,
            detail = "請求過於頻繁，請稍後再試。",
        }, cancellationToken);
    };
});

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = jwtOptions.Issuer,
            ValidateAudience = true,
            ValidAudience = jwtOptions.Audience,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtOptions.SigningKey)),
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromMinutes(1),
        };
        options.Events = new JwtBearerEvents
        {
            OnMessageReceived = context =>
            {
                if (context.Request.Cookies.TryGetValue(AuthConstants.CookieName, out var token))
                {
                    context.Token = token;
                }

                return Task.CompletedTask;
            },
            OnChallenge = context =>
            {
                if (context.Request.Path.StartsWithSegments("/api"))
                {
                    return Task.CompletedTask;
                }

                context.HandleResponse();
                var returnUrl = Uri.EscapeDataString(context.Request.Path + context.Request.QueryString);
                context.Response.Redirect($"/Account/Login?returnUrl={returnUrl}");
                return Task.CompletedTask;
            },
        };
    });

builder.Services.AddAuthorization();

var app = builder.Build();

// Configure the HTTP request pipeline. The global handler runs before everything else so it can
// catch failures from any later stage; it never leaks exception text, stack traces, or dev-only
// diagnostics into a response -- that's why it runs the same way in every environment instead of
// only outside Development.
app.UseExceptionHandler(new ExceptionHandlerOptions { ExceptionHandlingPath = "/Home/Error" });

if (!app.Environment.IsDevelopment())
{
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseRouting();

app.UseRateLimiter();

app.UseAuthentication();
app.UseAuthorization();

app.MapStaticAssets();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}")
    .WithStaticAssets();

if (app.Environment.IsEnvironment("Testing"))
{
    // WebFlow.Tests exercises the global exception handler end to end; these routes exist only so
    // an integration test can force a genuinely unhandled exception through the real pipeline.
    // They are never mapped outside the "Testing" environment.
    app.MapGet("/api/test/throw", (HttpContext _) => throw new InvalidOperationException("secret exception text"));
    app.MapGet("/test/throw", (HttpContext _) => throw new InvalidOperationException("secret exception text"));
}


app.Run();

// Exposes the top-level-statement-generated Program class so WebFlow.Tests can host the real
// app via WebApplicationFactory<Program> instead of duplicating startup wiring.
public partial class Program;
