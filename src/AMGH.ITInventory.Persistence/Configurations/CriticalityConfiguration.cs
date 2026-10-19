using AMGH.ITInventory.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using AMGH.ITInventory.Persistence.Extensions;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AMGH.ITInventory.Persistence.Configurations;

/// <summary>Maps <see cref="Criticality"/> to dbo.tblCriticality. GENERATED - matches deployment/SQLScripts/02_Tables.sql.</summary>
internal sealed class CriticalityConfiguration : IEntityTypeConfiguration<Criticality>
{
    public void Configure(EntityTypeBuilder<Criticality> builder)
    {
        builder.ToTable("tblCriticality", "dbo", tb =>
        {
            tb.HasCheckConstraint("CK_tblCriticality_CriticalityLevel", "[CriticalityLevel] BETWEEN 1 AND 9");
            tb.HasCheckConstraint("CK_tblCriticality_RtoHours", "[RtoHours] IS NULL OR [RtoHours] >= 0");
            tb.HasCheckConstraint("CK_tblCriticality_RpoHours", "[RpoHours] IS NULL OR [RpoHours] >= 0");
        });

        builder.ConfigureAuditColumns();
        builder.HasKey(e => e.CriticalityId).HasName("PK_tblCriticality");

        builder.Property(e => e.CompanyId).HasColumnType("int").IsRequired();
        builder.Property(e => e.CriticalityId).HasColumnType("int").IsRequired().UseIdentityColumn();
        builder.Property(e => e.CriticalityCode).HasColumnType("nvarchar(20)").HasMaxLength(20).IsRequired();
        builder.Property(e => e.CriticalityName).HasColumnType("nvarchar(100)").HasMaxLength(100).IsRequired();
        builder.Property(e => e.CriticalityLevel).HasColumnType("tinyint").IsRequired();
        builder.Property(e => e.RtoHours).HasColumnType("int");
        builder.Property(e => e.RpoHours).HasColumnType("int");
        builder.Property(e => e.Description).HasColumnType("nvarchar(500)").HasMaxLength(500);
        builder.Property(e => e.SortOrder).HasColumnType("int").IsRequired();
        builder.Property(e => e.IsActive).HasColumnType("bit").IsRequired();

        builder.HasIndex(e => new { e.CompanyId, e.CriticalityCode }, "UX_tblCriticality_CriticalityCode").IsUnique().HasFilter(null);
        builder.HasIndex(e => new { e.CompanyId, e.CriticalityLevel }, "UX_tblCriticality_CriticalityLevel").IsUnique().HasFilter(null);
        builder.HasIndex(e => e.CompanyId, "IX_tblCriticality_CompanyId").HasFilter(null);
    }
}
