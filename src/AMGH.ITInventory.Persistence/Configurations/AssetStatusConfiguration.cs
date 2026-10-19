using AMGH.ITInventory.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using AMGH.ITInventory.Persistence.Extensions;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AMGH.ITInventory.Persistence.Configurations;

/// <summary>Maps <see cref="AssetStatus"/> to dbo.tblAssetStatus. GENERATED - matches deployment/SQLScripts/02_Tables.sql.</summary>
internal sealed class AssetStatusConfiguration : IEntityTypeConfiguration<AssetStatus>
{
    public void Configure(EntityTypeBuilder<AssetStatus> builder)
    {
        builder.ToTable("tblAssetStatus", "dbo", tb =>
        {
        });

        builder.ConfigureAuditColumns();
        builder.HasKey(e => e.AssetStatusId).HasName("PK_tblAssetStatus");

        builder.Property(e => e.AssetStatusId).HasColumnType("int").IsRequired().UseIdentityColumn();
        builder.Property(e => e.StatusCode).HasColumnType("nvarchar(20)").HasMaxLength(20).IsRequired();
        builder.Property(e => e.StatusName).HasColumnType("nvarchar(100)").HasMaxLength(100).IsRequired();
        builder.Property(e => e.IsAssignable).HasColumnType("bit").IsRequired();
        builder.Property(e => e.CountsAsInventory).HasColumnType("bit").IsRequired();
        builder.Property(e => e.IsTerminal).HasColumnType("bit").IsRequired();
        builder.Property(e => e.ColorHex).HasColumnType("char(7)").HasMaxLength(7).IsFixedLength().IsUnicode(false);
        builder.Property(e => e.Description).HasColumnType("nvarchar(500)").HasMaxLength(500);
        builder.Property(e => e.SortOrder).HasColumnType("int").IsRequired();
        builder.Property(e => e.IsActive).HasColumnType("bit").IsRequired();

        builder.HasIndex(e => e.StatusCode, "UX_tblAssetStatus_StatusCode").IsUnique().HasFilter(null);
    }
}
