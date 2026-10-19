using AMGH.ITInventory.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using AMGH.ITInventory.Persistence.Extensions;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AMGH.ITInventory.Persistence.Configurations;

/// <summary>Maps <see cref="Classification"/> to dbo.tblClassification. GENERATED - matches deployment/SQLScripts/02_Tables.sql.</summary>
internal sealed class ClassificationConfiguration : IEntityTypeConfiguration<Classification>
{
    public void Configure(EntityTypeBuilder<Classification> builder)
    {
        builder.ToTable("tblClassification", "dbo", tb =>
        {
            tb.HasCheckConstraint("CK_tblClassification_ClassificationLevel", "[ClassificationLevel] BETWEEN 1 AND 9");
            tb.HasCheckConstraint("CK_tblClassification_ColorHex", "[ColorHex] IS NULL OR [ColorHex] LIKE '#[0-9A-Fa-f][0-9A-Fa-f][0-9A-Fa-f][0-9A-Fa-f][0-9A-Fa-f][0-9A-Fa-f]'");
        });

        builder.ConfigureAuditColumns();
        builder.HasKey(e => e.ClassificationId).HasName("PK_tblClassification");

        builder.Property(e => e.CompanyId).HasColumnType("int").IsRequired();
        builder.Property(e => e.ClassificationId).HasColumnType("int").IsRequired().UseIdentityColumn();
        builder.Property(e => e.ClassificationCode).HasColumnType("nvarchar(20)").HasMaxLength(20).IsRequired();
        builder.Property(e => e.ClassificationName).HasColumnType("nvarchar(100)").HasMaxLength(100).IsRequired();
        builder.Property(e => e.ClassificationLevel).HasColumnType("tinyint").IsRequired();
        builder.Property(e => e.HandlingRequirements).HasColumnType("nvarchar(1000)").HasMaxLength(1000);
        builder.Property(e => e.ColorHex).HasColumnType("char(7)").HasMaxLength(7).IsFixedLength().IsUnicode(false);
        builder.Property(e => e.Description).HasColumnType("nvarchar(500)").HasMaxLength(500);
        builder.Property(e => e.SortOrder).HasColumnType("int").IsRequired();
        builder.Property(e => e.IsActive).HasColumnType("bit").IsRequired();

        builder.HasIndex(e => new { e.CompanyId, e.ClassificationCode }, "UX_tblClassification_ClassificationCode").IsUnique().HasFilter(null);
        builder.HasIndex(e => new { e.CompanyId, e.ClassificationLevel }, "UX_tblClassification_ClassificationLevel").IsUnique().HasFilter(null);
        builder.HasIndex(e => e.CompanyId, "IX_tblClassification_CompanyId").HasFilter(null);
    }
}
