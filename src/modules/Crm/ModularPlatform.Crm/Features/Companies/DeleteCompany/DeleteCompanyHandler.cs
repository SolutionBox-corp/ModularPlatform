using Microsoft.EntityFrameworkCore;
using ModularPlatform.Abstractions;
using ModularPlatform.Cqrs;
using ModularPlatform.Crm.Persistence;

namespace ModularPlatform.Crm.Features.Companies.DeleteCompany;

/// <summary>
/// Soft-deletes a tracked company owned by the caller and detaches it from the user's contacts/deals (sets their
/// CompanyId to null so no dangling references remain). Foreign/missing ⇒ 404. No event is published.
/// </summary>
internal sealed class DeleteCompanyHandler(CrmDbContext db, IClock clock)
    : ICommandHandler<DeleteCompanyCommand, Unit>
{
    public async Task<Unit> Handle(DeleteCompanyCommand command, CancellationToken ct)
    {
        var company = await db.Companies
            .FirstOrDefaultAsync(c => c.Id == command.CompanyId && c.UserId == command.UserId, ct)
            ?? throw new NotFoundException("crm.company_not_found", "Company not found.");

        // Atomic: the parent soft-delete (tracked, xmin-guarded) and the child detaches (bulk ExecuteUpdate, which
        // otherwise commits in its own statement) must land together — otherwise a failed SaveChanges could leave the
        // children already detached while the company stays live (a torn state).
        await using var tx = await db.Database.BeginTransactionAsync(ct);

        company.DeletedAt = clock.UtcNow;
        await db.SaveChangesAsync(ct);

        await db.Contacts.Where(c => c.UserId == command.UserId && c.CompanyId == company.Id)
            .ExecuteUpdateAsync(s => s.SetProperty(c => c.CompanyId, (Guid?)null), ct);
        await db.Deals.Where(d => d.UserId == command.UserId && d.CompanyId == company.Id)
            .ExecuteUpdateAsync(s => s.SetProperty(d => d.CompanyId, (Guid?)null), ct);

        await tx.CommitAsync(ct);

        return Unit.Value;
    }
}
