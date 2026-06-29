using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ModularPlatform.Abstractions;
using ModularPlatform.Persistence;
using ModularPlatform.Persistence.Encryption;
using ModularPlatform.Persistence.Entities;
using Shouldly;

namespace ModularPlatform.BuildingBlocks.Tests;

public sealed class PersonalDataEncryptionInterceptorTests
{
    [Fact]
    public void Decrypting_converter_surfaces_erased_marker_when_protected_value_cannot_be_revealed()
    {
        var previous = PersonalDataEncryption.Protector;
        try
        {
            PersonalDataEncryption.Protector = new AlwaysMissingProtector();
            var converter = new PersonalDataDecryptingConverter();

            var revealed = converter.ConvertFromProvider("penc:v2:unreadable-envelope");

            revealed.ShouldBe(PersonalDataProtection.ErasedMarker);
        }
        finally
        {
            PersonalDataEncryption.Protector = previous;
        }
    }

    [Fact]
    public async Task Save_of_encrypted_plaintext_without_a_protector_is_rejected()
    {
        await using var services = new ServiceCollection().BuildServiceProvider();
        var tenant = new TestTenantContext();
        var options = new DbContextOptionsBuilder<TestDbContext>()
            .UseInMemoryDatabase($"pii-encryption-guard-{Guid.CreateVersion7():N}")
            .AddInterceptors(new PersonalDataEncryptionInterceptor(services))
            .Options;
        await using var db = new TestDbContext(options, tenant);

        db.Entities.Add(new EncryptedEntity
        {
            OwnerId = tenant.UserId!.Value,
            Secret = "plain-pii"
        });

        var exception = await Should.ThrowAsync<InvalidOperationException>(() => db.SaveChangesAsync());
        exception.Message.ShouldContain("is [Encrypted] but no IPersonalDataProtector is registered");
    }

    [Fact]
    public async Task Failed_save_restores_tracked_encrypted_property_to_plaintext()
    {
        await using var services = new ServiceCollection()
            .AddSingleton<IPersonalDataProtector, DeterministicProtector>()
            .BuildServiceProvider();
        var tenant = new TestTenantContext();
        var options = new DbContextOptionsBuilder<TestDbContext>()
            .UseNpgsql(
                "Host=127.0.0.1;Port=1;Database=modularplatform_missing;Username=postgres;Password=postgres;Timeout=1;Command Timeout=1")
            .AddInterceptors(new PersonalDataEncryptionInterceptor(services))
            .Options;
        await using var db = new TestDbContext(options, tenant);
        var entity = new EncryptedEntity
        {
            OwnerId = tenant.UserId!.Value,
            Secret = "plain-pii"
        };
        db.Entities.Add(entity);

        await Should.ThrowAsync<Exception>(() => db.SaveChangesAsync());

        entity.Secret.ShouldBe("plain-pii");
        db.Entry(entity).Property(e => e.Secret).CurrentValue.ShouldBe("plain-pii");
    }

    private sealed class TestTenantContext : ITenantContext
    {
        public Guid? UserId { get; } = Guid.CreateVersion7();
        public Guid? TenantId { get; } = Guid.CreateVersion7();
        public bool IsSystem => false;
        public string? IpAddress => "127.0.0.1";
    }

    private sealed class TestDbContext(DbContextOptions<TestDbContext> options, ITenantContext tenant)
        : PlatformDbContext(options, tenant)
    {
        public override string ModuleName => "Test";
        public DbSet<EncryptedEntity> Entities => Set<EncryptedEntity>();
    }

    private sealed class EncryptedEntity : Entity, IDataSubject
    {
        public Guid OwnerId { get; init; }
        public Guid SubjectId => OwnerId;

        [PersonalData]
        [Encrypted]
        public string Secret { get; set; } = string.Empty;
    }

    private sealed class AlwaysMissingProtector : IPersonalDataProtector
    {
        public bool IsProtected(string value) => PersonalDataEncryption.LooksProtected(value);

        public string Protect(Guid subjectId, string plaintext) => throw new NotSupportedException();

        public bool TryReveal(string protectedValue, out string plaintext)
        {
            plaintext = string.Empty;
            return false;
        }
    }

    private sealed class DeterministicProtector : IPersonalDataProtector
    {
        public bool IsProtected(string value) => PersonalDataEncryption.LooksProtected(value);

        public string Protect(Guid subjectId, string plaintext) => $"penc:v2:{subjectId:N}:{plaintext}";

        public bool TryReveal(string protectedValue, out string plaintext)
        {
            plaintext = protectedValue.Split(':').Last();
            return true;
        }
    }

}
