using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using QueueFlow.Domain.Enums;
using QueueFlow.Api.Health;
using QueueFlow.Application.Features.Auth;
using QueueFlow.Application.Features.Catalog;
using QueueFlow.Application.Features.Queues;
using QueueFlow.Application.Features.Tenants;
using QueueFlow.Application.Features.Reports;
using QueueFlow.Application.Features.Users;
using QueueFlow.Application.Features.Auditing;
using QueueFlow.Application.Features.Appointments;
using QueueFlow.Application.Features.Platform;
using QueueFlow.Infrastructure;
using QueueFlow.Infrastructure.Realtime;
using QueueFlow.Api.Middleware;
using QueueFlow.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using QueueFlow.Infrastructure.Observability;
using Serilog;

var builder = WebApplication.CreateBuilder(args);
if (string.Equals(builder.WebHost.GetSetting("FORWARDEDHEADERS_ENABLED"), "true", StringComparison.OrdinalIgnoreCase))
    throw new InvalidOperationException("Use ReverseProxy configuration instead of ASPNETCORE_FORWARDEDHEADERS_ENABLED.");

builder.Services.AddSerilog((services, logger) => logger
    .ReadFrom.Configuration(builder.Configuration)
    .ReadFrom.Services(services)
    .MinimumLevel.Override("Microsoft.AspNetCore", Serilog.Events.LogEventLevel.Warning)
    .MinimumLevel.Override("Microsoft.EntityFrameworkCore", Serilog.Events.LogEventLevel.Fatal)
    .Enrich.With<SafeRequestLogEnricher>()
    .Enrich.FromLogContext()
    .Enrich.WithProperty("Service", "queueflow-api")
    .WriteTo.Console(new Serilog.Formatting.Json.JsonFormatter()));
builder.Services.AddQueueFlowObservability(builder.Configuration, "queueflow-api", instrumentAspNetCore: true);

builder.Services.AddControllers().AddJsonOptions(options => options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddQueueFlowRealtimeBackplane(builder.Configuration);
builder.Services.AddOpenApi();
builder.Services.AddSwaggerGen(options =>
{
    options.AddSecurityDefinition("bearer", new OpenApiSecurityScheme
    {
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        Description = "Informe somente o access token JWT retornado pelo endpoint de login.",
    });
    options.AddSecurityRequirement(document => new OpenApiSecurityRequirement
    {
        [new OpenApiSecuritySchemeReference("bearer", document)] = [],
    });
});
builder.Services.AddInfrastructure(builder.Configuration);
if (!args.Contains("--migrate-only", StringComparer.Ordinal) && !args.Contains("--healthcheck", StringComparer.Ordinal)) builder.Services.AddQueueRealtimeProcessing();
builder.Services.AddScoped<AuthService>();
builder.Services.AddScoped<TenantService>();
builder.Services.AddScoped<OnboardingService>();
builder.Services.AddScoped<CatalogService>();
builder.Services.AddScoped<QueueOperationsService>();
builder.Services.AddScoped<ReportingService>();
builder.Services.AddScoped<UserManagementService>();
builder.Services.AddScoped<AuditQueryService>();
builder.Services.AddScoped<SchedulingConfigurationService>();
builder.Services.AddScoped<IAppointmentAvailabilityService, AppointmentAvailabilityService>();
builder.Services.AddScoped<AppointmentBookingService>();
builder.Services.AddScoped<PublicAppointmentService>();
builder.Services.AddScoped<AppointmentManagementService>();
builder.Services.AddScoped<OperationalAppointmentService>();
builder.Services.Configure<OnboardingOptions>(builder.Configuration.GetSection("Onboarding"));
builder.Services.PostConfigure<OnboardingOptions>(options =>
{
    options.AllowLocalPublicUrl = builder.Environment.IsDevelopment() || builder.Environment.IsEnvironment("Test");
    options.ExposeVerificationCodeForDevelopment &= builder.Environment.IsDevelopment() || builder.Environment.IsEnvironment("Test");
});
builder.Services.AddScoped<PlatformAuthService>();
builder.Services.AddScoped<OrganizationInvitationService>();
builder.Services.AddScoped<PlatformAdministrationService>();
builder.Services.AddScoped<PlatformBootstrapService>();
var jwtKey = builder.Configuration["Authentication:JwtKey"] ?? throw new InvalidOperationException("Authentication:JwtKey must be provided through secrets or environment variables.");
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(options =>
{
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer = true,
        ValidateAudience = true,
        ValidateLifetime = true,
        ValidateIssuerSigningKey = true,
        ValidIssuer = builder.Configuration["Authentication:Issuer"],
        ValidAudience = builder.Configuration["Authentication:Audience"],
        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey)),
        ClockSkew = TimeSpan.FromSeconds(30),
    };
});
builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("TenantIdentity", policy => policy.RequireAuthenticatedUser().RequireClaim("identity_type", "tenant").RequireClaim("organization_id"));
    options.AddPolicy("AdminPanel", policy => policy.RequireAuthenticatedUser().RequireClaim("identity_type", "tenant").RequireClaim("organization_id").RequireRole(UserRole.Owner.ToString(), UserRole.Admin.ToString(), UserRole.Manager.ToString()));
    options.AddPolicy("UserManagement", policy => policy.RequireAuthenticatedUser().RequireClaim("identity_type", "tenant").RequireClaim("organization_id").RequireRole(UserRole.Owner.ToString(), UserRole.Admin.ToString(), UserRole.Manager.ToString()));
    options.AddPolicy("AttendantPanel", policy => policy.RequireAuthenticatedUser().RequireClaim("identity_type", "tenant").RequireClaim("organization_id").RequireRole(UserRole.Owner.ToString(), UserRole.Admin.ToString(), UserRole.Manager.ToString(), UserRole.Attendant.ToString()));
    options.AddPolicy("OrganizationManagement", policy => policy.RequireAuthenticatedUser().RequireClaim("identity_type", "tenant").RequireClaim("organization_id").RequireRole(UserRole.Owner.ToString()));
    options.AddPolicy("SubscriptionManagement", policy => policy.RequireAuthenticatedUser().RequireClaim("identity_type", "tenant").RequireClaim("organization_id").RequireRole(UserRole.Owner.ToString()));
    options.AddPolicy("ReportRead", policy => policy.RequireAuthenticatedUser().RequireClaim("identity_type", "tenant").RequireClaim("organization_id").RequireRole(UserRole.Owner.ToString(), UserRole.Admin.ToString(), UserRole.Manager.ToString(), UserRole.Viewer.ToString()));
    options.AddPolicy("AuditRead", policy => policy.RequireAuthenticatedUser().RequireClaim("identity_type", "tenant").RequireClaim("organization_id").RequireRole(UserRole.Owner.ToString(), UserRole.Admin.ToString()));
    foreach (var name in Program.TenantPolicyNames)
    {
        var tenantPolicy = new Microsoft.AspNetCore.Authorization.AuthorizationPolicyBuilder(options.GetPolicy(name)!).RequireAssertion(context =>
            context.User.FindAll("identity_type").Count() == 1 && context.User.FindAll("organization_id").Count() == 1 &&
            Guid.TryParse(context.User.FindFirst("organization_id")?.Value, out var organizationId) && organizationId != Guid.Empty);
        options.AddPolicy(name, tenantPolicy.Build());
    }
    options.AddPolicy("RequirePlatformAdmin", policy => policy.RequireAuthenticatedUser().RequireClaim("identity_type", "platform").RequireRole("PlatformAdmin").RequireAssertion(context => !context.User.HasClaim(claim => claim.Type == "organization_id")));
});
var corsOrigins = builder.Configuration.GetSection("Cors:Origins").Get<string[]>() ?? [];
var invalidCorsOrigin = corsOrigins.Any(origin =>
{
    if (origin.Contains('*', StringComparison.Ordinal)) return true;
    if (builder.Environment.IsDevelopment() || origin.StartsWith("https://", StringComparison.OrdinalIgnoreCase)) return false;
    return !(builder.Environment.IsStaging() && Uri.TryCreate(origin, UriKind.Absolute, out var uri) && uri.IsLoopback);
});
if ((!builder.Environment.IsDevelopment() && corsOrigins.Length == 0) || invalidCorsOrigin) throw new InvalidOperationException("Non-development CORS origins must be explicit HTTPS origins; Staging also permits explicit loopback origins.");
builder.Services.AddCors(options => options.AddPolicy("web", policy => policy.WithOrigins(corsOrigins).WithHeaders("Authorization", "Content-Type", "X-Correlation-ID", "X-SignalR-User-Agent", "X-Requested-With").WithMethods("GET", "POST", "PUT", "PATCH", "DELETE").AllowCredentials()));
builder.Services.AddRateLimiter(options =>
{
    var globalPermitLimit = builder.Configuration.GetValue("RateLimiting:GlobalPermitLimit", 300);
    var publicPermitLimit = builder.Configuration.GetValue("RateLimiting:PublicPermitLimit", 30);
    var authPermitLimit = builder.Configuration.GetValue("RateLimiting:AuthPermitLimit", 10);
    var refreshPermitLimit = builder.Configuration.GetValue("RateLimiting:RefreshPermitLimit", 30);
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.OnRejected = (context, _) =>
    {
        if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
            context.HttpContext.Response.Headers.RetryAfter = Math.Ceiling(retryAfter.TotalSeconds).ToString(System.Globalization.CultureInfo.InvariantCulture);
        return ValueTask.CompletedTask;
    };
    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context => RateLimitPartition.GetFixedWindowLimiter(
        context.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value ?? context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = globalPermitLimit, Window = TimeSpan.FromMinutes(1), QueueLimit = 0, AutoReplenishment = true }));
    options.AddPolicy("public", context => RateLimitPartition.GetFixedWindowLimiter(context.Connection.RemoteIpAddress?.ToString() ?? "unknown", _ => new FixedWindowRateLimiterOptions { PermitLimit = publicPermitLimit, Window = TimeSpan.FromMinutes(1), QueueLimit = 0, AutoReplenishment = true }));
    options.AddPolicy("auth", context => RateLimitPartition.GetFixedWindowLimiter(LoginRateLimitKeyMiddleware.PartitionKey(context), _ => new FixedWindowRateLimiterOptions { PermitLimit = authPermitLimit, Window = TimeSpan.FromMinutes(1), QueueLimit = 0, AutoReplenishment = true }));
    options.AddPolicy("refresh", context => RateLimitPartition.GetFixedWindowLimiter(LoginRateLimitKeyMiddleware.RefreshPartitionKey(context), _ => new FixedWindowRateLimiterOptions { PermitLimit = refreshPermitLimit, Window = TimeSpan.FromMinutes(1), QueueLimit = 0, AutoReplenishment = true }));
});
builder.Services
    .AddHealthChecks()
    .AddCheck<PostgreSqlHealthCheck>("postgresql", tags: ["ready"])
    .AddCheck<RedisHealthCheck>("redis", tags: ["ready"]);

var app = builder.Build();

if (args.Contains("--preflight-user-email", StringComparer.Ordinal))
{
    await using var preflightScope = app.Services.CreateAsyncScope();
    var users = await preflightScope.ServiceProvider.GetRequiredService<ApplicationDbContext>().Users.IgnoreQueryFilters().AsNoTracking().Select(user => new { user.Email, user.OrganizationId }).ToListAsync();
    var duplicates = users.GroupBy(user => user.Email.Trim().ToLowerInvariant()).Where(group => group.Count() > 1)
        .Select(group => new { Email = group.Key, Count = group.Count(), Organizations = group.Select(user => user.OrganizationId).ToArray() }).ToArray();
    Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(duplicates));
    var noncanonical = users.Count(user => !string.Equals(user.Email, user.Email.Trim().ToLowerInvariant(), StringComparison.Ordinal));
    Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(new { NoncanonicalEmails = noncanonical }));
    Environment.ExitCode = duplicates.Length == 0 && noncanonical == 0 ? 0 : 2;
    return;
}

if (args.Contains("--provision-platform-admin", StringComparer.Ordinal))
{
    if (!builder.Configuration.GetValue("PlatformBootstrap:Enabled", false)) throw new InvalidOperationException("Platform bootstrap is disabled.");
    var email = builder.Configuration["PlatformBootstrap:Email"] ?? throw new InvalidOperationException("Platform bootstrap email is required.");
    var password = Environment.GetEnvironmentVariable("PlatformBootstrap__Password") ?? throw new InvalidOperationException("Platform bootstrap password must be supplied through the environment.");
    var name = builder.Configuration["PlatformBootstrap:Name"] ?? "QueueFlow Platform Admin";
    await using var bootstrapScope = app.Services.CreateAsyncScope();
    var provisioned = await bootstrapScope.ServiceProvider.GetRequiredService<PlatformBootstrapService>().ProvisionFirstAdminAsync(name, email, password, CancellationToken.None);
    if (!provisioned.IsSuccess) throw new InvalidOperationException(provisioned.Error.Description);
    return;
}

if (args.Contains("--healthcheck", StringComparer.Ordinal))
{
    try { using var healthClient = new HttpClient { Timeout = TimeSpan.FromSeconds(3) }; using var healthResponse = await healthClient.GetAsync("http://127.0.0.1:8080/health"); Environment.ExitCode = healthResponse.IsSuccessStatusCode ? 0 : 1; }
    catch (HttpRequestException) { Environment.ExitCode = 1; }
    catch (TaskCanceledException) { Environment.ExitCode = 1; }
    return;
}

if (args.Contains("--migrate-only", StringComparer.Ordinal))
{
    await using var migrationScope = app.Services.CreateAsyncScope();
    await migrationScope.ServiceProvider.GetRequiredService<ApplicationDbContext>().Database.MigrateAsync();
    return;
}

// Temporary: log only configuration booleans once per API startup, not for CLI commands.
app.Services.LogActivationEmailConfiguration();

app.UseMiddleware<CorrelationIdMiddleware>();
app.UseSerilogRequestLogging(options => options.EnrichDiagnosticContext = (diagnostic, context) =>
{
    diagnostic.Set("RequestPath", (context.GetEndpoint() as Microsoft.AspNetCore.Routing.RouteEndpoint)?.RoutePattern.RawText ?? "unmatched");
    diagnostic.Set("CorrelationId", context.TraceIdentifier);
    diagnostic.Set("RequestHost", context.Request.Host.Value);
});
app.UseMiddleware<SecurityHeadersMiddleware>();
app.UseMiddleware<ExceptionHandlingMiddleware>();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.UseSwagger();
    app.UseSwaggerUI(options =>
    {
        options.SwaggerEndpoint("/swagger/v1/swagger.json", "QueueFlow API v1");
        options.RoutePrefix = "swagger";
    });
}

if (!app.Environment.IsDevelopment()) app.UseHsts();
app.UseForwardedHeaders(TrustedProxyOptions.Create(builder.Configuration));
app.UseHttpsRedirection();
app.UseCors("web");
app.UseAuthentication();
app.UseMiddleware<LoginRateLimitKeyMiddleware>();
app.UseRateLimiter();
app.UseAuthorization();

app.MapControllers();
app.MapHub<QueueHub>("/hubs/queue");
app.MapHealthChecks("/health/live", new HealthCheckOptions
{
    Predicate = _ => false,
});
app.MapHealthChecks("/health", new HealthCheckOptions
{
    Predicate = registration => registration.Tags.Contains("ready"),
});

app.Run();

public partial class Program
{
    internal static readonly string[] TenantPolicyNames = ["TenantIdentity", "AdminPanel", "UserManagement", "AttendantPanel", "OrganizationManagement", "SubscriptionManagement", "ReportRead", "AuditRead"];
}
