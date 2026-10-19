using AMGH.ITInventory.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using AMGH.ITInventory.Persistence.Extensions;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AMGH.ITInventory.Persistence.Configurations;

/// <summary>Maps <see cref="WarrantyStatus"/> to dbo.tblWarrantyStatus. GENERATED - matches deployment/SQLScripts/02_Tables.sql.</summary>
internal sealed class WarrantyStatusConfiguration : IEntityTypeConfiguration<WarrantyStatus>
{
    public void Configure(EntityTypeBuilder<WarrantyStatus> builder)
    {
        builder.ToTable("tblWarrantyStatus", "dbo", tb =>
        {
        });

        builder.ConfigureAuditColumns();
        builder.HasKey(e => e.WarrantyStatusId).HasName("PK_tblWarrantyStatus");

        builder.Property(e => e.WarrantyStatusId).HasColumnType("int").IsRequired().UseIdentityColumn();
        builder.Property(e => e.StatusCode).HasColumnType("nvarchar(20)").HasMaxLength(20).IsRequired();
        builder.Property(e => e.StatusName).HasColumnType("nvarchar(100)").HasMaxLength(100).IsRequired();
        builder.Property(e => e.Description).HasColumnType("nvarchar(500)").HasMaxLength(500);
        builder.Property(e => e.SortOrder).HasColumnType("int").IsRequired();
        builder.Property(e => e.IsActive).HasColumnType("bit").IsRequired();

        builder.HasIndex(e => e.StatusCode, "UX_tblWarrantyStatus_StatusCode").IsUnique().HasFilter(null);
    }
}
