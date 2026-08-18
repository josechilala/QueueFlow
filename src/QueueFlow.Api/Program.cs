using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.IdentityModel.Tokens;
using System.Text;
using QueueFlow.Api.Health;
using QueueFlow.Application.Features.Auth;
using QueueFlow.Application.Features.Catalog;
using QueueFlow.Application.Features.Queues;
using QueueFlow.Application.Features.Tenants;
using QueueFlow.Application.Features.Reports;
using QueueFlow.Infrastructure;
using QueueFlow.Infrastructure.Realtime;
using QueueFlow.Api.Middleware;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddSignalR();
builder.Services.AddOpenApi();
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddScoped<AuthService>();
builder.Services.AddScoped<TenantService>();
builder.Services.AddScoped<CatalogService>();
builder.Services.AddScoped<QueueOperationsService>();
builder.Services.AddScoped<ReportingService>();
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
builder.Services.AddAuthorization();
builder.Services.AddCors(options => options.AddPolicy("web", policy => policy.WithOrigins(builder.Configuration.GetSection("Cors:Origins").Get<string[]>() ?? []).AllowAnyHeader().AllowAnyMethod().AllowCredentials()));
builder.Services.AddRateLimiter(options => options.AddFixedWindowLimiter("public", limiter => { limiter.PermitLimit = 30; limiter.Window = TimeSpan.FromMinutes(1); limiter.QueueLimit = 0; }));
builder.Services
    .AddHealthChecks()
    .AddCheck<PostgreSqlHealthCheck>("postgresql", tags: ["ready"]);

var app = builder.Build();

app.UseMiddleware<CorrelationIdMiddleware>();
app.UseMiddleware<ExceptionHandlingMiddleware>();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();
app.UseRateLimiter();
app.UseCors("web");
app.UseAuthentication();
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

public partial class Program;
