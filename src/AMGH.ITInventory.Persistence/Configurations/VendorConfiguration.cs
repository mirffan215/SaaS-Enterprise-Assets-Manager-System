using AMGH.ITInventory.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using AMGH.ITInventory.Persistence.Extensions;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AMGH.ITInventory.Persistence.Configurations;

/// <summary>Maps <see cref="Vendor"/> to dbo.tblVendor. GENERATED - matches deployment/SQLScripts/02_Tables.sql.</summary>
internal sealed class VendorConfiguration : IEntityTypeConfiguration<Vendor>
{
    public void Configure(EntityTypeBuilder<Vendor> builder)
    {
        builder.ToTable("tblVendor", "dbo", tb =>
        {
            tb.HasCheckConstraint("CK_tblVendor_Email", "[Email] IS NULL OR [Email] LIKE N'%_@_%._%'");
            tb.HasCheckConstraint("CK_tblVendor_Rule01", "[IsApproved] = 0 OR [ApprovalDate] IS NOT NULL");
        });

        builder.ConfigureAuditColumns();
        builder.HasKey(e => e.VendorId).HasName("PK_tblVendor");

        builder.Property(e => e.CompanyId).HasColumnType("int").IsRequired();
        builder.Property(e => e.VendorId).HasColumnType("int").IsRequired().UseIdentityColumn();
        builder.Property(e => e.VendorCode).HasColumnType("nvarchar(20)").HasMaxLength(20).IsRequired();
        builder.Property(e => e.VendorName).HasColumnType("nvarchar(200)").HasMaxLength(200).IsRequired();
        builder.Property(e => e.ContactPerson).HasColumnType("nvarchar(150)").HasMaxLength(150);
        builder.Property(e => e.Email).HasColumnType("nvarchar(256)").HasMaxLength(256);
        builder.Property(e => e.Phone).HasColumnType("nvarchar(30)").HasMaxLength(30);
        builder.Property(e => e.Address).HasColumnType("nvarchar(500)").HasMaxLength(500);
        builder.Property(e => e.CommercialRegistrationNo).HasColumnType("nvarchar(30)").HasMaxLength(30);
        builder.Property(e => e.VatNumber).HasColumnType("nvarchar(30)").HasMaxLength(30);
        builder.Property(e => e.IsApproved).HasColumnType("bit").IsRequired();
        builder.Property(e => e.ApprovalDate).HasColumnType("date");
        builder.Property(e => e.SecurityAssessmentDate).HasColumnType("date");
        builder.Property(e => e.Notes).HasColumnType("nvarchar(1000)").HasMaxLength(1000);
        builder.Property(e => e.IsActive).HasColumnType("bit").IsRequired();

        builder.HasIndex(e => new { e.CompanyId, e.VendorCode }, "UX_tblVendor_VendorCode").IsUnique().HasFilter(null);
        builder.HasIndex(e => e.CompanyId, "IX_tblVendor_CompanyId").HasFilter(null);
    }
}
