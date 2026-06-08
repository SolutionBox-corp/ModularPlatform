using FluentValidation;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using ModularPlatform.Abstractions;
using ModularPlatform.Cqrs;
using ModularPlatform.Notifications.Channels;
using ModularPlatform.Notifications.Features.Notifications.GetMyNotifications;
using ModularPlatform.Notifications.Features.Notifications.MarkNotificationRead;
using ModularPlatform.Notifications.Features.Notifications.SendNotification;
using ModularPlatform.Notifications.Gdpr;
using ModularPlatform.Notifications.Persistence;
using ModularPlatform.Messaging;
using ModularPlatform.Persistence;
using Wolverine;

namespace ModularPlatform.Notifications;

/// <summary>
/// Notifications module wiring. Owns its DbContext, channel senders (email via MailKit SMTP, push stub),
/// CQRS handlers/validators and endpoints. Consumes Identity's UserRegistered event to send a welcome
/// notification, and hands off per-channel delivery durably to the Worker via the outbox.
/// </summary>
public sealed class NotificationsModule : IModule
{
    public string Name => "Notifications";

    public void RegisterServices(IServiceCollection services, IConfiguration configuration)
    {
        var write = configuration.GetConnectionString("Write")
            ?? throw new InvalidOperationException("Missing ConnectionStrings:Write");
        var read = configuration.GetConnectionString("Read") ?? write;

        services.AddCqrs(typeof(NotificationsModule).Assembly);
        services.AddValidatorsFromAssembly(typeof(NotificationsModule).Assembly, includeInternalTypes: true);

        services.AddModuleDbContext<NotificationsDbContext>(Name, write);
        services.AddModuleReadDbContext<NotificationsDbContext>(read);

        services.Configure<EmailOptions>(configuration.GetSection(EmailOptions.SectionName));
        services.AddScoped<IEmailSender, SmtpEmailSender>();
        services.AddScoped<IPushSender, NoOpPushSender>();

        services.AddScoped<IExportPersonalData, NotificationsPersonalDataExporter>();
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapSendNotification();
        endpoints.MapGetMyNotifications();
        endpoints.MapMarkNotificationRead();
    }

    public void ConfigureMessaging(WolverineOptions options)
    {
        // Consumes Identity's UserRegisteredIntegrationEvent (welcome) and its own EmailDeliveryRequested /
        // PushDeliveryRequested channel messages. Wolverine auto-discovers the Handle methods in this assembly.
    }

    public async Task ApplyMigrationsAsync(IServiceProvider services, CancellationToken ct)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NotificationsDbContext>();
        await db.Database.MigrateAsync(ct);
    }
}
