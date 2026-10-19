using AMGH.ITInventory.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using AMGH.ITInventory.Persistence.Extensions;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AMGH.ITInventory.Persistence.Configurations;

/// <summary>Maps <see cref="AssetNumberSequence"/> to dbo.tblAssetNumberSequence. GENERATED - matches deployment/SQLScripts/02_Tables.sql.</summary>
internal sealed class AssetNumberSequenceConfiguration : IEntityTypeConfiguration<AssetNumberSequence>
{
    public void Configure(EntityTypeBuilder<AssetNumberSequence> builder)
    {
        builder.ToTable("tblAssetNumberSequence", "dbo", tb =>
        {
            tb.HasTrigger("trg_tblAssetNumberSequence_Protect");
            tb.HasTrigger("trg_tblAssetNumberSequence_NoDelete");
            tb.HasCheckConstraint("CK_tblAssetNumberSequence_SequenceCode", "[SequenceCode] NOT LIKE N'%[^A-Z]%' AND LEN([SequenceCode]) BETWEEN 2 AND 10");
            tb.HasCheckConstraint("CK_tblAssetNumberSequence_LastNumber", "[LastNumber] >= 0");
            tb.HasCheckConstraint("CK_tblAssetNumberSequence_PaddingLength", "[PaddingLength] BETWEEN 3 AND 10");
        });
        builder.Property(e => e.Separator).HasColumnType("nvarchar(1)").HasMaxLength(1).IsRequired();

        builder.ConfigureAuditColumns();
        builder.HasKey(e => e.AssetNumberSequenceId).HasName("PK_tblAssetNumberSequence");

        builder.Property(e => e.CompanyId).HasColumnType("int").IsRequired();
        builder.Property(e => e.AssetNumberSequenceId).HasColumnType("int").IsRequired().UseIdentityColumn();
        builder.Property(e => e.SequenceCode).HasColumnType("nvarchar(10)").HasMaxLength(10).IsRequired();
        builder.Property(e => e.Prefix).HasColumnType("nvarchar(20)").HasMaxLength(20).IsRequired();
        builder.Property(e => e.LastNumber).HasColumnType("bigint").IsRequired();
        builder.Property(e => e.PaddingLength).HasColumnType("tinyint").IsRequired();
        builder.Property(e => e.Description).HasColumnType("nvarchar(500)").HasMaxLength(500);
        builder.Property(e => e.SortOrder).HasColumnType("int").IsRequired();
        builder.Property(e => e.IsActive).HasColumnType("bit").IsRequired();

        builder.HasIndex(e => new { e.CompanyId, e.SequenceCode }, "UX_tblAssetNumberSequence_SequenceCode").IsUnique().HasFilter(null);
        builder.HasIndex(e => e.CompanyId, "IX_tblAssetNumberSequence_CompanyId").HasFilter(null);
    }
}
