using AMGH.ITInventory.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using AMGH.ITInventory.Persistence.Extensions;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AMGH.ITInventory.Persistence.Configurations;

/// <summary>Maps <see cref="RiskLevel"/> to dbo.tblRiskLevel. GENERATED - matches deployment/SQLScripts/02_Tables.sql.</summary>
internal sealed class RiskLevelConfiguration : IEntityTypeConfiguration<RiskLevel>
{
    public void Configure(EntityTypeBuilder<RiskLevel> builder)
    {
        builder.ToTable("tblRiskLevel", "dbo", tb =>
        {
            tb.HasCheckConstraint("CK_tblRiskLevel_ReviewFrequencyMonths", "[ReviewFrequencyMonths] IS NULL OR [ReviewFrequencyMonths] > 0");
            tb.HasCheckConstraint("CK_tblRiskLevel_Rule01", "[MaxScore] >= [MinScore]");
        });

        builder.ConfigureAuditColumns();
        builder.HasKey(e => e.RiskLevelId).HasName("PK_tblRiskLevel");

        builder.Property(e => e.CompanyId).HasColumnType("int").IsRequired();
        builder.Property(e => e.RiskLevelId).HasColumnType("int").IsRequired().UseIdentityColumn();
        builder.Property(e => e.RiskLevelCode).HasColumnType("nvarchar(20)").HasMaxLength(20).IsRequired();
        builder.Property(e => e.RiskLevelName).HasColumnType("nvarchar(100)").HasMaxLength(100).IsRequired();
        builder.Property(e => e.MinScore).HasColumnType("tinyint").IsRequired();
        builder.Property(e => e.MaxScore).HasColumnType("tinyint").IsRequired();
        builder.Property(e => e.ReviewFrequencyMonths).HasColumnType("smallint");
        builder.Property(e => e.ColorHex).HasColumnType("char(7)").HasMaxLength(7).IsFixedLength().IsUnicode(false);
        builder.Property(e => e.Description).HasColumnType("nvarchar(500)").HasMaxLength(500);
        builder.Property(e => e.SortOrder).HasColumnType("int").IsRequired();
        builder.Property(e => e.IsActive).HasColumnType("bit").IsRequired();

        builder.HasIndex(e => new { e.CompanyId, e.RiskLevelCode }, "UX_tblRiskLevel_RiskLevelCode").IsUnique().HasFilter(null);
        builder.HasIndex(e => e.CompanyId, "IX_tblRiskLevel_CompanyId").HasFilter(null);
    }
}
