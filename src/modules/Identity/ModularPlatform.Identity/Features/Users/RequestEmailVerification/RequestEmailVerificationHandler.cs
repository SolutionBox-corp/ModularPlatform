using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using ModularPlatform.Abstractions;
using ModularPlatform.Cqrs;
using ModularPlatform.Identity.Authorization;
using ModularPlatform.Identity.Entities;
using ModularPlatform.Identity.Persistence;
using ModularPlatform.Identity.Security;
using ModularPlatform.Notifications.Contracts;
using ModularPlatform.Web;
using Wolverine.EntityFrameworkCore;

namespace ModularPlatform.Identity.Features.Users.RequestEmailVerification;

internal sealed class RequestEmailVerificationHandler(
    IDbContextOutbox<IdentityDbContext> outbox,
    ITokenIssuer tokenIssuer,
    IClock clock,
    IOptions<EmailVerificationOptions> options)
    : ICommandHandler<RequestEmailVerificationCommand, RequestEmailVerificationResponse>
{
    public async Task<RequestEmailVerificationResponse> Handle(RequestEmailVerificationCommand command, CancellationToken ct)
    {
        var db = outbox.DbContext;
        var now = clock.UtcNow;

        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == command.UserId, ct)
            ?? throw new NotFoundException("user.not_found", "User not found.");

        if (user.EmailConfirmed)
        {
            return new RequestEmailVerificationResponse(true, true);
        }

        await EmailVerificationIssuer.IssueAsync(db, outbox, tokenIssuer, clock, options.Value, user, now, ct);
        await outbox.SaveChangesAndFlushMessagesAsync();

        return new RequestEmailVerificationResponse(true, false);
    }
}
