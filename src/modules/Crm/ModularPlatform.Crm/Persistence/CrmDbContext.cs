using Microsoft.EntityFrameworkCore;
using ModularPlatform.Abstractions;
using ModularPlatform.Crm.Entities;
using ModularPlatform.Persistence;

namespace ModularPlatform.Crm.Persistence;

/// <summary>
/// CRM module's DbContext. Entity configs are discovered from this assembly; xmin concurrency, the tenant
/// filter, the soft-delete filter and the per-module audit table are applied by <see cref="PlatformDbContext"/>.
/// </summary>
internal sealed class CrmDbContext(DbContextOptions<CrmDbContext> options, ITenantContext tenant)
    : PlatformDbContext(options, tenant)
{
    public override string ModuleName => "crm";

    public DbSet<Contact> Contacts => Set<Contact>();
    public DbSet<ContactInteraction> ContactInteractions => Set<ContactInteraction>();
    public DbSet<Meeting> Meetings => Set<Meeting>();
    public DbSet<Deal> Deals => Set<Deal>();
    public DbSet<CrmTask> Tasks => Set<CrmTask>();
    public DbSet<CrmTaskComment> TaskComments => Set<CrmTaskComment>();
    public DbSet<Company> Companies => Set<Company>();
    public DbSet<KanbanBoard> KanbanBoards => Set<KanbanBoard>();
    public DbSet<KanbanColumn> KanbanColumns => Set<KanbanColumn>();
    public DbSet<KanbanCard> KanbanCards => Set<KanbanCard>();
}
