using Microsoft.EntityFrameworkCore;
using ModularPlatform.Abstractions;
using ModularPlatform.Cqrs;
using ModularPlatform.Crm.Entities;
using ModularPlatform.Crm.Persistence;

namespace ModularPlatform.Crm.Features.Deals.CreateDeal;

/// <summary>
/// Write slice WITHOUT an event. If a contact is linked, it must be owned by the caller (foreign/missing ⇒ 404).
/// Owner is the token; TenantId is stamped by the interceptor. A deal created directly in a terminal stage records
/// ClosedAt. No raw SQL.
/// </summary>
internal sealed class CreateDealHandler(CrmDbContext db, IClock clock)
    : ICommandHandler<CreateDealCommand, CreateDealResponse>
{
    public async Task<CreateDealResponse> Handle(CreateDealCommand command, CancellationToken ct)
    {
        Guid? companyIdFromContact = null;
        if (command.ContactId is { } contactId)
        {
            var contact = await db.Contacts
                .Where(c => c.Id == contactId && c.UserId == command.UserId)
                .Select(c => new { c.CompanyId })
                .FirstOrDefaultAsync(ct);
            if (contact is null)
            {
                throw new NotFoundException("crm.contact_not_found", "Contact not found.");
            }

            companyIdFromContact = contact.CompanyId;
        }

        var companyId = command.CompanyId ?? companyIdFromContact;

        if (companyId is { } cid
            && !await db.Companies.AnyAsync(c => c.Id == cid && c.UserId == command.UserId, ct))
        {
            throw new NotFoundException("crm.company_not_found", "Company not found.");
        }

        var deal = new Deal
        {
            UserId = command.UserId,
            ContactId = command.ContactId,
            CompanyId = companyId,
            Title = command.Title.Trim(),
            AmountCents = command.AmountCents,
            Currency = command.Currency,
            Stage = command.Stage,
            ProbabilityPercent = command.ProbabilityPercent ?? DealStages.DefaultProbability(command.Stage),
            LeadSource = string.IsNullOrWhiteSpace(command.LeadSource) ? null : command.LeadSource,
            ExpectedCloseAt = command.ExpectedCloseAt,
            ClosedAt = DealStages.IsTerminal(command.Stage) ? clock.UtcNow : null,
            NextStep = string.IsNullOrWhiteSpace(command.NextStep) ? null : command.NextStep,
            Notes = string.IsNullOrWhiteSpace(command.Notes) ? null : command.Notes,
        };

        db.Deals.Add(deal);
        await db.SaveChangesAsync(ct);

        return new CreateDealResponse(deal.Id);
    }
}
