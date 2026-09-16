using System.IdentityModel.Tokens.Jwt;
using System.Text;
using System.Threading.RateLimiting;
using Carter;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using NovaWallet.Api.Auth;
using NovaWallet.Api.Infrastructure;
using NovaWallet.BuildingBlocks.Application;
using NovaWallet.Wallet.API;
using NovaWallet.Wallet.Application.Abstractions;
using NovaWallet.Wallet.Infrastructure;
using NovaWallet.Wallet.Infrastructure.Persistence;
using Scalar.AspNetCore;
using Serilog;
using Serilog.Context;

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseSerilog((context, configuration) => configuration
    .ReadFrom.Configuration(context.Configuration)
    .Enrich.FromLogContext()
    .WriteTo.Console());

builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentUser, CurrentUser>();

// Shared MediatR pipeline behaviours (logging, validation) + the Wallet module's own
// DbContext/repository/handlers. Each module scans and registers only its own Application
// assembly with MediatR — see WalletModuleExtensions.AddWalletModule.
builder.Services.AddBuildingBlocks();
builder.Services.AddWalletModule(builder.Configuration);

// Deliberately no signing key ships in appsettings.json — only Issuer/Audience/lifetime live
// there. The key itself must come from an environment variable or user-secrets (see README),
// and this fails fast at startup with a clear message if it's missing or too short, rather than
// silently booting with an empty/weak key.
builder.Services.AddOptions<JwtOptions>()
    .Bind(builder.Configuration.GetSection(JwtOptions.SectionName))
    .Validate(
        options => !string.IsNullOrWhiteSpace(options.SigningKey) && Encoding.UTF8.GetByteCount(options.SigningKey) >= 32,
        "Jwt:SigningKey is missing or shorter than 32 bytes. Set it via the Jwt__SigningKey environment " +
        "variable (docker-compose.yml already does this) or `dotnet user-secrets set \"Jwt:SigningKey\" \"...\"` for a local `dotnet run`.")
    .ValidateOnStart();
builder.Services.AddSingleton<JwtTokenService>();

// JwtOptions is resolved from IOptions<JwtOptions> at the point JwtBearer actually needs it
// (first request), not read straight off IConfiguration here — that keeps this in lock-step
// with whatever JwtTokenService sees, including configuration overridden after builder
// creation (e.g. WalletApiFactory in the integration tests).
builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer();
builder.Services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
    .Configure<IOptions<JwtOptions>>((options, jwtOptions) =>
    {
        var jwt = jwtOptions.Value;
        options.MapInboundClaims = false;
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = jwt.Issuer,
            ValidateAudience = true,
            ValidAudience = jwt.Audience,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.SigningKey)),
            ClockSkew = TimeSpan.FromSeconds(30),
        };
    });

builder.Services.AddAuthorizationBuilder()
    .SetDefaultPolicy(new Microsoft.AspNetCore.Authorization.AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build())
    .AddPolicy("System", policy => policy.RequireClaim(ClaimNames.Role, "system"));

// Rate-limits only the transfer endpoint (see WalletModule.Transfer .RequireRateLimiting("transfer"))
// — the one place a runaway or scripted client could otherwise hammer the ledger.
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy("transfer", httpContext => RateLimitPartition.GetFixedWindowLimiter(
        // The JWT here never carries a "name"-typed claim (only "sub" and "role" — see
        // JwtTokenService), so `User.Identity.Name` is always null and this used to silently
        // fall back to IP for every authenticated caller, putting every customer behind the
        // same NAT/gateway in one shared 30-req/min budget. Partition on the actual customer id
        // claim instead, so the limit is genuinely per-customer.
        partitionKey: httpContext.User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value
            ?? httpContext.Connection.RemoteIpAddress?.ToString()
            ?? "anonymous",
        factory: _ => new FixedWindowRateLimiterOptions
        {
            Window = TimeSpan.FromMinutes(1),
            PermitLimit = 30,
            QueueLimit = 0,
        }));
});

builder.Services.AddCarter(configurator: config =>
{
    config.WithModule<WalletModule>();

    // The dev-token issuer mints a valid JWT for any customerId/role with no credential check —
    // fine for exercising the auth middleware in this take-home, never something to expose
    // in a real deployment.
    if (!builder.Environment.IsProduction())
        config.WithModule<AuthModule>();
});

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo { Title = "NovaWallet Ledger Service", Version = "v1" });
    options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Name = "Authorization",
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        In = ParameterLocation.Header,
        Description = "Paste the access token from POST /api/auth/dev-token (no 'Bearer ' prefix needed).",
    });
    options.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme { Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "Bearer" } },
            Array.Empty<string>()
        },
    });
});

builder.Services.AddExceptionHandler<GlobalExceptionHandler>();
builder.Services.AddProblemDetails();

builder.Services.AddHealthChecks()
    .AddNpgSql(builder.Configuration.GetConnectionString("Default")!, name: "postgres");

var app = builder.Build();

// Applying migrations at startup keeps `docker compose up` a single command with no separate
// migration step — acceptable for a service this size; a larger modular monolith with many
// modules would instead reflect over every registered DbContext and migrate each.
using (var scope = app.Services.CreateScope())
{
    await scope.ServiceProvider.GetRequiredService<WalletDbContext>().Database.MigrateAsync();
}

app.UseExceptionHandler();

app.Use(async (context, next) =>
{
    using (LogContext.PushProperty("CorrelationId", context.TraceIdentifier))
    {
        await next();
    }
});
app.UseSerilogRequestLogging();

if (!app.Environment.IsProduction())
{
    // Swashbuckle still generates the OpenAPI document (net8.0's Microsoft.AspNetCore.OpenApi
    // package doesn't yet ship the native AddOpenApi()/MapOpenApi() generator — that's a .NET 9
    // addition) — but it's Scalar, not Swagger's own UI, serving it.
    app.UseSwagger();
    app.MapScalarApiReference(options =>
    {
        options.WithOpenApiRoutePattern("/swagger/{documentName}/swagger.json");
        options.Title = "NovaWallet Ledger Service";
    });
}

app.UseAuthentication();
app.UseRateLimiter();
app.UseAuthorization();

app.MapCarter();
app.MapHealthChecks("/health/live");
app.MapHealthChecks("/health/ready");

app.Run();

public partial class Program
{
    protected Program() { }
}
