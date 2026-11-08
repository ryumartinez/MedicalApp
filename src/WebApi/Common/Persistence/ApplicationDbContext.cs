using MedicalApp.Common.Domain.Base.Interfaces;
using MedicalApp.Common.Identity;
using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using System.Reflection;

namespace MedicalApp.Common.Persistence;

/// <summary>
/// Holds the domain model, Identity accounts, OpenIddict registrations and tokens, and data protection keys,
/// so account provisioning can change accounts and mappings in one transaction.
/// </summary>
public partial class ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
    : IdentityDbContext<ApplicationUser, IdentityRole<Guid>, Guid>(options), IDataProtectionKeyContext
{
    public DbSet<DataProtectionKey> DataProtectionKeys => Set<DataProtectionKey>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        // Identity's model first, so application configurations can refine it.
        base.OnModelCreating(builder);

        builder.UseOpenIddict<Guid>();
        builder.ApplyConfigurationsFromAssembly(Assembly.GetExecutingAssembly());
    }

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        base.ConfigureConventions(configurationBuilder);

        configurationBuilder.RegisterAllInVogenEfCoreConverters();
    }

    private DbSet<T> AggregateRootSet<T>() where T : class, IAggregateRoot => Set<T>();
}
