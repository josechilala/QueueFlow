using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using QueueFlow.Application.Features.Auth;
using StackExchange.Redis;
using Microsoft.Extensions.DependencyInjection;
using QueueFlow.Application.Abstractions.Clock;
using QueueFlow.Application.Abstractions.Authentication;
using QueueFlow.Application.Abstractions.Persistence;
using QueueFlow.Infrastructure.Clock;
using QueueFlow.Infrastructure.Authentication;
using QueueFlow.Infrastructure.Persistence;
using QueueFlow.Application.Abstractions.Realtime;
using QueueFlow.Infrastructure.Realtime;
using QueueFlow.Application.Abstractions.Notifications;
using QueueFlow.Infrastructure.Notifications;
using QueueFlow.Infrastructure.Jobs;
using QueueFlow.Application.Abstractions.Auditing;
using QueueFlow.Infrastructure.Auditing;
using QueueFlow.Infrastructure.Billing;

namespace QueueFlow.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("QueueFlowDatabase")
            ?? throw new InvalidOperationException("Connection string 'QueueFlowDatabase' is not configured.");

        // Billing is deliberately opt-in and sandbox-only. Production credentials/endpoints are not supported.
        var asaasEnvironment = configuration["Asaas:Environment"];
        if (!string.IsNullOrWhiteSpace(asaasEnvironment))
        {
            if (!string.Equals(asaasEnvironment, "Sandbox", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Only Asaas Sandbox is supported.");
            if (string.IsNullOrWhiteSpace(configuration["Asaas:SandboxApiKey"]))
                throw new InvalidOperationException("Asaas:SandboxApiKey is required when Asaas Sandbox is enabled.");
            services.AddHttpClient<AsaasSandboxClient>(client =>
            {
                client.BaseAddress = new Uri(AsaasSandboxClient.SandboxBaseUrl);
                client.Timeout = TimeSpan.FromSeconds(20);
            })
            .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
            {
                AllowAutoRedirect = false,
                UseCookies = false
            })
            .RemoveAllLoggers();
        }

        services.AddDbContext<ApplicationDbContext>(options => options.UseNpgsql(connectionString));
        services.AddScoped<IApplicationDbContext>(provider => provider.GetRequiredService<ApplicationDbContext>());
        services.AddScoped<ITicketOperations, PostgresTicketOperations>();
        services.AddScoped<IAppointmentOperations, PostgresAppointmentOperations>();
        services.AddHttpContextAccessor();
        services.AddScoped<ICurrentUser, CurrentUser>();
        services.AddSingleton<ITokenService, JwtTokenService>();
        services.AddSingleton<IPasswordService, PasswordService>();
        services.AddSingleton<IQueueRealtimeNotifier, SignalRQueueRealtimeNotifier>();
        services.AddScoped<INotificationSender, InAppNotificationSender>();
        services.Configure<ResendOptions>(configuration.GetSection("Resend"));
        services.AddHttpClient(ResendEmailTransport.HttpClientName, client => client.Timeout = TimeSpan.FromSeconds(15))
            .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false })
            .RemoveAllLoggers();
        services.AddScoped<ResendEmailTransport>();
        services.Configure<AppointmentEmailOptions>(configuration.GetSection("AppointmentEmails"));
        services.AddScoped<AppointmentReceiptSender>();
        services.AddScoped<IActivationEmailSender, ActivationEmailSender>();
        services.AddScoped<IAuditWriter, AuditWriter>();
        services.AddSingleton<IClock, SystemClock>();

        return services;
    }

    public static IServiceCollection AddAuthenticationProtection(this IServiceCollection services, IConfiguration configuration, IHostEnvironment environment)
    {
        var local = environment.IsDevelopment() || environment.IsEnvironment("Test");
        var useRedis = configuration.GetValue("RateLimiting:UseRedis", !local);
        if (!local && !useRedis) throw new InvalidOperationException("Distributed authentication protection requires Redis outside Development/Test.");
        var limit = configuration.GetValue("RateLimiting:AuthPermitLimit", 10);
        if (configuration.GetValue("RateLimiting:RefreshPermitLimit", 30) <= 0)
            throw new InvalidOperationException("The refresh request limit must be positive.");
        if (limit <= 0) throw new InvalidOperationException("The authentication failure limit must be positive.");
        services.AddSingleton(new LoginProtectionOptions { FailureLimit = limit });
        services.AddScoped<LoginProtection>();
        if (useRedis)
        {
            var connection = configuration["Redis:ConnectionString"];
            if (string.IsNullOrWhiteSpace(connection)) throw new InvalidOperationException("Redis:ConnectionString is required for distributed authentication protection.");
            services.AddSingleton<IConnectionMultiplexer>(_ =>
            {
                var options = ConfigurationOptions.Parse(connection);
                options.AbortOnConnectFail = false;
                options.ConnectTimeout = 2000; options.SyncTimeout = 2000; options.AsyncTimeout = 2000; options.ConnectRetry = 0;
                return ConnectionMultiplexer.Connect(options);
            });
            services.AddSingleton<IAuthenticationThrottleStore, RedisAuthenticationThrottleStore>();
        }
        else services.AddSingleton<IAuthenticationThrottleStore>(_ => new InMemoryAuthenticationThrottleStore(TimeProvider.System));
        return services;
    }

    public static IServiceCollection AddQueueRealtimeProcessing(this IServiceCollection services)
    {
        services.AddHostedService(provider => new OutboxProcessor(provider.GetRequiredService<IServiceScopeFactory>(), provider.GetRequiredService<Microsoft.Extensions.Logging.ILogger<OutboxProcessor>>(), queueEventsOnly: true));
        return services;
    }
    public static IServiceCollection AddAppointmentReceiptProcessing(this IServiceCollection services)
    {
        services.AddSingleton<Microsoft.Extensions.Hosting.IHostedService>(provider => new OutboxProcessor(provider.GetRequiredService<IServiceScopeFactory>(), provider.GetRequiredService<Microsoft.Extensions.Logging.ILogger<OutboxProcessor>>(), receiptsOnly: true));
        return services;
    }
    public static IServiceCollection AddApiBackgroundProcessing(this IServiceCollection services) { services.AddHostedService<OutboxProcessor>(); return services; }
    public static IServiceCollection AddWorkerBackgroundProcessing(this IServiceCollection services) { services.AddHostedService<OutboxProcessor>(); services.AddHostedService<QueueMetricsJob>(); services.AddHostedService<ExpiredTicketJob>(); services.AddHostedService<AppointmentReminderJob>(); services.AddHostedService<AppointmentNoShowJob>(); services.AddHostedService<CleanupJob>(); return services; }
    public static IServiceCollection AddQueueFlowRealtimeBackplane(this IServiceCollection services, IConfiguration configuration)
    {
        var signalR = services.AddSignalR();
        if (configuration.GetValue("Redis:UseBackplane", false)) signalR.AddStackExchangeRedis(configuration["Redis:ConnectionString"] ?? "localhost:6379");
        return services;
    }
}
