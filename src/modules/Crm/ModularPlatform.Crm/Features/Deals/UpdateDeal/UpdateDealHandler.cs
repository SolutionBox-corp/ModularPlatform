using Microsoft.EntityFrameworkCore;
using ModularPlatform.Cqrs;
using ModularPlatform.Crm.Features.Deals;
using ModularPlatform.Crm.Persistence;

namespace ModularPlatform.Crm.Features.Deals.UpdateDeal;

/// <summary>Loads the caller's OWN tracked deal (foreign/deleted ⇒ 404) and applies a PARTIAL patch (null = unchanged). Stage is unchanged here.</summary>
internal sealed class UpdateDealHandler(CrmDbContext db)
    : ICommandHandler<UpdateDealCommand, DealResponse>
{
    public async Task<DealResponse> Handle(UpdateDealCommand command, CancellationToken ct)
    {
        var deal = await db.Deals
            .FirstOrDefaultAsync(d => d.Id == command.DealId && d.UserId == command.UserId, ct)
            ?? throw new NotFoundException("crm.deal_not_found", "Deal not found.");

        if (command.Title is not null)
        {
            deal.Title = command.Title.Trim();
        }

        if (command.AmountCents is { } amount)
        {
            deal.AmountCents = amount;
        }

        if (command.Currency is not null)
        {
            deal.Currency = command.Currency.Trim().ToUpperInvariant();
        }

        if (command.ExpectedCloseAt is not null)
        {
            deal.ExpectedCloseAt = command.ExpectedCloseAt;
        }

        if (command.Notes is not null)
        {
            deal.Notes = string.IsNullOrWhiteSpace(command.Notes) ? null : command.Notes;
        }

        await db.SaveChangesAsync(ct);

        return new DealResponse(
            deal.Id, deal.ContactId, deal.Title, deal.AmountCents, deal.Currency, deal.Stage, deal.ExpectedCloseAt,
            deal.ClosedAt, deal.Notes, deal.CreatedAt, deal.UpdatedAt);
    }
}
