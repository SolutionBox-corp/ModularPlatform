using FluentValidation;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using ModularPlatform.Abstractions;
using ModularPlatform.Cqrs;
using ModularPlatform.Notifications.Channels;
using ModularPlatform.Notifications.Features.Notifications.GetMyNotifications;
using ModularPlatform.Notifications.Features.Notifications.GetUnreadCount;
using ModularPlatform.Notifications.Features.Notifications.MarkAllRead;
using ModularPlatform.Notifications.Features.Notifications.MarkNotificationRead;
using ModularPlatform.Notifications.Features.Notifications.SendNotification;
using ModularPlatform.Notifications.Gdpr;
using ModularPlatform.Notifications.Persistence;
using ModularPlatform.Notifications.Seeding;
using ModularPlatform.Messaging;
using ModularPlatform.Persistence;
using ModularPlatform.Persistence.Rls;
using Wolverine;

namespace ModularPlatform.Notifications;

/// <summary>
/// Notifications module wiring. Owns its DbContext, channel senders (email via MailKit SMTP, push webhook/no-op),
/// CQRS handlers/validators and endpoints. Consumes Identity's UserRegistered event to send a welcome
/// notification, and hands off per-channel delivery durably to the Worker via the outbox.
/// </summary>
public sealed class NotificationsModule : IModule
{
    public string Name => "Notifications";

    public void RegisterServices(IServiceCollection services, IConfiguration configuration)
    {
        var (write, read) = ModuleConnectionStrings.GetWriteAndRead(configuration);

        services.AddCqrs(typeof(NotificationsModule).Assembly);
        services.AddValidatorsFromAssembly(typeof(NotificationsModule).Assembly, includeInternalTypes: true);

        services.AddModuleDbContext<NotificationsDbContext>(Name, write);
        services.AddModuleReadDbContext<NotificationsDbContext>(read);

        services.Configure<EmailOptions>(configuration.GetSection(EmailOptions.SectionName));
        services.AddScoped<IEmailSender, SmtpEmailSender>();
        services.Configure<PushOptions>(configuration.GetSection(PushOptions.SectionName));
        services.AddHttpClient("notifications-push");
        if (string.IsNullOrWhiteSpace(configuration["Notifications:Push:WebhookUrl"]))
        {
            services.AddScoped<IPushSender, NoOpPushSender>();
        }
        else
        {
            services.AddScoped<IPushSender, WebhookPushSender>();
        }

        services.AddScoped<IExportPersonalData, NotificationsPersonalDataExporter>();
        services.AddScoped<IErasePersonalData, NotificationsPersonalDataEraser>();

        services.AddHostedService<NotificationsSeeder>();
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapSendNotification();
        endpoints.MapGetMyNotifications();
        endpoints.MapGetUnreadCount();
        endpoints.MapMarkNotificationRead();
        endpoints.MapMarkAllRead();
    }

    public void ConfigureMessaging(WolverineOptions options)
    {
        // Register this module's message handlers explicitly (cross-assembly conventional discovery is unreliable).
        options.Discovery.IncludeType<Messaging.SendWelcomeHandler>();
        options.Discovery.IncludeType<Messaging.SendPurchaseCompletedHandler>();
        options.Discovery.IncludeType<Messaging.EmailDeliveryHandler>();
        options.Discovery.IncludeType<Messaging.PushDeliveryHandler>();
    }

    public async Task ApplyMigrationsAsync(IServiceProvider services, CancellationToken ct)
    {
        // Migrations run on the ADMIN connection — the DI-registered context uses the RLS runtime role,
        // which cannot run DDL. The RLS bootstrapper (host, post-migration) then provisions role + policies.
        var adminConnectionString = services.GetRequiredService<IConfiguration>().GetConnectionString("Write")
            ?? throw new InvalidOperationException("Missing ConnectionStrings:Write");
        await PlatformMigrator.MigrateAsync<NotificationsDbContext>(services, adminConnectionString, Name, ct);
    }
}
