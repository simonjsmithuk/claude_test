// =============================================================================
// DataViewer.API — Application entry point
// =============================================================================
// Clean Architecture dependency note:
//   Program.cs is the composition root. It may reference Infrastructure types
//   for DI registration purposes only.  Business logic MUST NOT live here.
// =============================================================================

using System.Text;
using DataViewer.Application.DependencyInjection;
using DataViewer.Infrastructure.DependencyInjection;
using DataViewer.Infrastructure.Logging;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using Serilog;

// ── Step 1: Bootstrap early configuration ─────────────────────────────────────
// Load appsettings.json + appsettings.{Environment}.json before anything else so
// that SerilogConfiguration.Configure() can read Logging:MinimumLevel.
// This two-phase approach (bootstrap config → Serilog → full host build) ensures
// that host-build failures and DI registration errors are captured in the log.

var bootstrapConfig = new ConfigurationBuilder()
    .SetBasePath(Directory.GetCurrentDirectory())
    .AddJsonFile("appsettings.json", optional: false, reloadOnChange: false)
    .AddJsonFile(
        $"appsettings.{Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "Production"}.json",
        optional: true,
        reloadOnChange: false)
    .AddEnvironmentVariables()
    .Build();

// Determine hosting environment early so SerilogConfiguration can pick the
// correct console formatter (JSON vs human-readable).
var bootstrapEnvironment = new BootstrapHostEnvironment(
    Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? Environments.Production);

// ── Step 2: Configure Serilog BEFORE building the host ────────────────────────
// Acceptance criterion: "SerilogConfiguration.Configure() is a static method
// called in Program.cs before the host is built." (TASK-020)
SerilogConfiguration.Configure(bootstrapConfig, bootstrapEnvironment);

try
{
    Log.Information("Starting DataViewer API host");

    // ── Step 3: Build the WebApplication host ─────────────────────────────────
    var builder = WebApplication.CreateBuilder(args);

    // Replace the default .NET logging pipeline with Serilog so that all
    // ILogger<T> injections route through the configured sinks and destructuring
    // policies — including SensitiveFieldDestructuringPolicy.
    builder.Host.UseSerilog();

    // ── Step 4: Register service layers ───────────────────────────────────────

    // Infrastructure services (encryption, repositories, EF Core, external clients, etc.).
    // AesEncryptionService is registered here as Singleton; it validates the
    // DATAVIEWER_ENCRYPTION_KEY environment variable at construction time, causing
    // a fast startup failure with a clear error if the key is missing or invalid.
    builder.Services.AddInfrastructure(builder.Configuration);

    // Application services (use cases, audit service, body processing, etc.)
    builder.Services.AddApplicationServices();

    // ── JWT Authentication ─────────────────────────────────────────────────────
    var jwtSecret = Environment.GetEnvironmentVariable("JWT__SECRET")
        ?? throw new InvalidOperationException(
            "JWT__SECRET environment variable is required but not set.");

    var jwtKey = Encoding.UTF8.GetBytes(jwtSecret);

    builder.Services.AddAuthentication(options =>
    {
        options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
        options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
    })
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(jwtKey),
            ValidateIssuer = true,
            ValidIssuer = builder.Configuration["Jwt:Issuer"],
            ValidateAudience = true,
            ValidAudience = builder.Configuration["Jwt:Audience"],
            ValidateLifetime = true,
            ClockSkew = TimeSpan.Zero
        };
    });

    builder.Services.AddAuthorization(options =>
    {
        options.AddPolicy("Admin", policy => policy.RequireRole("Admin"));
    });

    // ── CORS ───────────────────────────────────────────────────────────────────
    var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? Array.Empty<string>();

    builder.Services.AddCors(options =>
    {
        options.AddDefaultPolicy(policy =>
        {
            if (allowedOrigins.Length > 0)
            {
                policy.WithOrigins(allowedOrigins)
                      .AllowAnyMethod()
                      .AllowAnyHeader()
                      .AllowCredentials();
            }
            else
            {
                // Development: allow all origins
                policy.AllowAnyOrigin()
                      .AllowAnyMethod()
                      .AllowAnyHeader();
            }
        });
    });

    // ── Health Checks ──────────────────────────────────────────────────────────
    builder.Services.AddHealthChecks()
        .AddCheck("self", () => HealthCheckResult.Healthy(), tags: new[] { "ready" });

    // ── Swagger / OpenAPI ──────────────────────────────────────────────────────
    builder.Services.AddEndpointsApiExplorer();
    builder.Services.AddSwaggerGen(options =>
    {
        options.SwaggerDoc("v1", new OpenApiInfo
        {
            Title = "DataViewer API",
            Version = "v1",
            Description = "DataViewer REST API for viewing HTTP transaction records stored on AWS S3",
            Contact = new OpenApiContact
            {
                Name = "DataViewer Team"
            }
        });

        // JWT Bearer authentication support in Swagger UI
        options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
        {
            Description = "JWT Authorization header using the Bearer scheme. Enter 'Bearer' [space] and then your token.",
            Name = "Authorization",
            In = ParameterLocation.Header,
            Type = SecuritySchemeType.ApiKey,
            Scheme = "Bearer",
            BearerFormat = "JWT"
        });

        options.AddSecurityRequirement(new OpenApiSecurityRequirement
        {
            {
                new OpenApiSecurityScheme
                {
                    Reference = new OpenApiReference
                    {
                        Type = ReferenceType.SecurityScheme,
                        Id = "Bearer"
                    }
                },
                Array.Empty<string>()
            }
        });
    });

    // ── Controllers ────────────────────────────────────────────────────────────
    builder.Services.AddControllers();

    var app = builder.Build();

    // ── Middleware pipeline ────────────────────────────────────────────────────

    // Serilog request logging middleware emits one structured log entry per HTTP
    // request, enriched with RequestId, UserId, Method, Path, StatusCode,
    // ElapsedMs, and IpAddress as required by section 7.6 of the SDD.
    app.UseSerilogRequestLogging(SerilogConfiguration.ConfigureRequestLogging);

    // Enable Swagger in all environments (can be restricted to Development if needed)
    app.UseSwagger();
    app.UseSwaggerUI(options =>
    {
        options.SwaggerEndpoint("/swagger/v1/swagger.json", "DataViewer API v1");
        options.RoutePrefix = "swagger";
    });

    // CORS must be called before Authentication and Authorization
    app.UseCors();

    // Authentication and Authorization
    app.UseAuthentication();
    app.UseAuthorization();

    // Health check endpoints
    app.MapHealthChecks("/health", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
    {
        Predicate = _ => true
    });

    app.MapHealthChecks("/health/ready", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
    {
        Predicate = check => check.Tags.Contains("ready")
    });

    // Map controllers
    app.MapControllers();

    Log.Information("DataViewer API started successfully — listening on {Urls}",
        string.Join(", ", builder.Configuration.GetSection("Kestrel:Endpoints").GetChildren()
            .Select(e => e.GetValue<string>("Url"))
            .Where(u => u != null) ?? new[] { "default" }));

    await app.RunAsync();

    return 0;
}
catch (Exception ex)
{
    // Capture fatal startup errors before Serilog is torn down.
    Log.Fatal(ex, "DataViewer API host terminated unexpectedly during startup");
    return 1;
}
finally
{
    // Flush all buffered log events and release file handles before the process exits.
    await Log.CloseAndFlushAsync();
}

// ── Internal helper: minimal IHostEnvironment for the bootstrap phase ─────────
/// <summary>
/// Minimal <see cref="IHostEnvironment"/> implementation used only during the
/// Serilog bootstrap phase, before the full ASP.NET Core host is built.
/// </summary>
/// <remarks>
/// This avoids taking a dependency on the full host builder just to determine the
/// environment name for the console sink format selection.
/// </remarks>
internal sealed class BootstrapHostEnvironment : IHostEnvironment
{
    public BootstrapHostEnvironment(string environmentName)
    {
        EnvironmentName = environmentName;
        // ASSUMPTION: ApplicationName and ContentRootPath are not used by
        // SerilogConfiguration.Configure(); safe to leave as defaults here.
        ApplicationName = "DataViewer";
        ContentRootPath = Directory.GetCurrentDirectory();
        ContentRootFileProvider =
            new Microsoft.Extensions.FileProviders.PhysicalFileProvider(ContentRootPath);
    }

    /// <inheritdoc />
    public string EnvironmentName { get; set; }

    /// <inheritdoc />
    public string ApplicationName { get; set; }

    /// <inheritdoc />
    public string ContentRootPath { get; set; }

    /// <inheritdoc />
    public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; }
}
