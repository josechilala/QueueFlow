using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
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

namespace QueueFlow.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("QueueFlowDatabase")
            ?? throw new InvalidOperationException("Connection string 'QueueFlowDatabase' is not configured.");

        services.AddDbContext<ApplicationDbContext>(options => options.UseNpgsql(connectionString));
        services.AddScoped<IApplicationDbContext>(provider => provider.GetRequiredService<ApplicationDbContext>());
        services.AddScoped<ITicketOperations, PostgresTicketOperations>();
        services.AddHttpContextAccessor();
        services.AddScoped<ICurrentUser, CurrentUser>();
        services.AddSingleton<ITokenService, JwtTokenService>();
        services.AddSingleton<IPasswordService, PasswordService>();
        services.AddSingleton<IQueueRealtimeNotifier, SignalRQueueRealtimeNotifier>();
        services.AddScoped<INotificationSender, InAppNotificationSender>();
        services.AddHostedService<NotificationDispatchJob>();
        services.AddSingleton<IClock, SystemClock>();

        return services;
    }
}
