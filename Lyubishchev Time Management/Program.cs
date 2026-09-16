using System.Text;
using System.Threading.RateLimiting;
using Lyubishchev_Time_Management.Data;
using Lyubishchev_Time_Management.Infrastructure.Clock;
using Lyubishchev_Time_Management.Infrastructure.Logging;
using Lyubishchev_Time_Management.Security;
using Lyubishchev_Time_Management.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using MySql.EntityFrameworkCore.Extensions;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddControllersWithViews();
builder.Services.AddHttpContextAccessor();

var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");
if (string.IsNullOrWhiteSpace(connectionString))
{
    throw new InvalidOperationException(
        "Connection string 'DefaultConnection' is required. Set it with dotnet user-secrets before running the application.");
}

builder.Services.AddDbContext<AppDbContext>(options => options.UseMySQL(connectionString));

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

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
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


app.Run();
