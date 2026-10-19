using AMGH.ITInventory.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using AMGH.ITInventory.Persistence.Extensions;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AMGH.ITInventory.Persistence.Configurations;

/// <summary>Maps <see cref="DisposalMethod"/> to dbo.tblDisposalMethod. GENERATED - matches deployment/SQLScripts/02_Tables.sql.</summary>
internal sealed class DisposalMethodConfiguration : IEntityTypeConfiguration<DisposalMethod>
{
    public void Configure(EntityTypeBuilder<DisposalMethod> builder)
    {
        builder.ToTable("tblDisposalMethod", "dbo", tb =>
        {
        });

        builder.ConfigureAuditColumns();
        builder.HasKey(e => e.DisposalMethodId).HasName("PK_tblDisposalMethod");

        builder.Property(e => e.DisposalMethodId).HasColumnType("int").IsRequired().UseIdentityColumn();
        builder.Property(e => e.MethodCode).HasColumnType("nvarchar(20)").HasMaxLength(20).IsRequired();
        builder.Property(e => e.MethodName).HasColumnType("nvarchar(100)").HasMaxLength(100).IsRequired();
        builder.Property(e => e.RequiresDataSanitization).HasColumnType("bit").IsRequired();
        builder.Property(e => e.RequiresCertificate).HasColumnType("bit").IsRequired();
        builder.Property(e => e.Description).HasColumnType("nvarchar(500)").HasMaxLength(500);
        builder.Property(e => e.SortOrder).HasColumnType("int").IsRequired();
        builder.Property(e => e.IsActive).HasColumnType("bit").IsRequired();

        builder.HasIndex(e => e.MethodCode, "UX_tblDisposalMethod_MethodCode").IsUnique().HasFilter(null);
    }
}
