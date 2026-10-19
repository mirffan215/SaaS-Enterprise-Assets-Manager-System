using AMGH.ITInventory.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using AMGH.ITInventory.Persistence.Extensions;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AMGH.ITInventory.Persistence.Configurations;

/// <summary>Maps <see cref="Currency"/> to dbo.tblCurrency. GENERATED - matches deployment/SQLScripts/02_Tables.sql.</summary>
internal sealed class CurrencyConfiguration : IEntityTypeConfiguration<Currency>
{
    public void Configure(EntityTypeBuilder<Currency> builder)
    {
        builder.ToTable("tblCurrency", "dbo", tb =>
        {
            tb.HasCheckConstraint("CK_tblCurrency_CurrencyCode", "[CurrencyCode] LIKE '[A-Z][A-Z][A-Z]'");
            tb.HasCheckConstraint("CK_tblCurrency_DecimalPlaces", "[DecimalPlaces] <= 4");
        });

        builder.ConfigureAuditColumns();
        builder.HasKey(e => e.CurrencyId).HasName("PK_tblCurrency");

        builder.Property(e => e.CurrencyId).HasColumnType("int").IsRequired().UseIdentityColumn();
        builder.Property(e => e.CurrencyCode).HasColumnType("char(3)").HasMaxLength(3).IsFixedLength().IsUnicode(false).IsRequired();
        builder.Property(e => e.CurrencyName).HasColumnType("nvarchar(100)").HasMaxLength(100).IsRequired();
        builder.Property(e => e.Symbol).HasColumnType("nvarchar(10)").HasMaxLength(10);
        builder.Property(e => e.DecimalPlaces).HasColumnType("tinyint").IsRequired();
        builder.Property(e => e.IsBaseCurrency).HasColumnType("bit").IsRequired();
        builder.Property(e => e.IsActive).HasColumnType("bit").IsRequired();

        builder.HasIndex(e => e.CurrencyCode, "UX_tblCurrency_CurrencyCode").IsUnique().HasFilter(null);
        builder.HasIndex(e => e.IsBaseCurrency, "UX_tblCurrency_IsBaseCurrency").IsUnique().HasFilter("[IsBaseCurrency] = 1 AND [IsDeleted] = 0");
    }
}
