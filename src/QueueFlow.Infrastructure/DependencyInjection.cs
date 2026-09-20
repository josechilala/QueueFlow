using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
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

namespace QueueFlow.Infrastructure;

public static class DependencyInjection
{
    // Temporary diagnostic for the actual bound provider, without making an HTTP request.
    public static void LogActivationEmailConfiguration(this IServiceProvider services)
    {
        using var scope = services.CreateScope();
        if (scope.ServiceProvider.GetRequiredService<IActivationEmailSender>() is ActivationEmailSender sender)
            sender.LogConfiguration(scope.ServiceProvider.GetRequiredService<ILogger<ActivationEmailSender>>());
    }

    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("QueueFlowDatabase")
            ?? throw new InvalidOperationException("Connection string 'QueueFlowDatabase' is not configured.");

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
        services.AddHttpClient(ActivationEmailSender.HttpClientName, client => client.Timeout = TimeSpan.FromSeconds(15))
            .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false })
            .RemoveAllLoggers();
        services.AddScoped<IActivationEmailSender, ActivationEmailSender>();
        services.AddScoped<IAuditWriter, AuditWriter>();
        services.AddSingleton<IClock, SystemClock>();

        return services;
    }

    public static IServiceCollection AddQueueRealtimeProcessing(this IServiceCollection services)
    {
        services.AddHostedService(provider => new OutboxProcessor(provider.GetRequiredService<IServiceScopeFactory>(), provider.GetRequiredService<Microsoft.Extensions.Logging.ILogger<OutboxProcessor>>(), queueEventsOnly: true));
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
