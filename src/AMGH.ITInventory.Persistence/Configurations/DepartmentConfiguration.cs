using AMGH.ITInventory.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using AMGH.ITInventory.Persistence.Extensions;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AMGH.ITInventory.Persistence.Configurations;

/// <summary>Maps <see cref="Department"/> to dbo.tblDepartment. GENERATED - matches deployment/SQLScripts/02_Tables.sql.</summary>
internal sealed class DepartmentConfiguration : IEntityTypeConfiguration<Department>
{
    public void Configure(EntityTypeBuilder<Department> builder)
    {
        builder.ToTable("tblDepartment", "dbo", tb =>
        {
            tb.HasCheckConstraint("CK_tblDepartment_Rule01", "[ParentDepartmentId] IS NULL OR [ParentDepartmentId] <> [DepartmentId]");
        });

        builder.ConfigureAuditColumns();
        builder.HasKey(e => e.DepartmentId).HasName("PK_tblDepartment");

        builder.Property(e => e.CompanyId).HasColumnType("int").IsRequired();
        builder.Property(e => e.DepartmentId).HasColumnType("int").IsRequired().UseIdentityColumn();
        builder.Property(e => e.DepartmentCode).HasColumnType("nvarchar(20)").HasMaxLength(20).IsRequired();
        builder.Property(e => e.DepartmentName).HasColumnType("nvarchar(150)").HasMaxLength(150).IsRequired();
        builder.Property(e => e.ParentDepartmentId).HasColumnType("int");
        builder.Property(e => e.HeadEmployeeId).HasColumnType("int");
        builder.Property(e => e.CostCenter).HasColumnType("nvarchar(30)").HasMaxLength(30);
        builder.Property(e => e.IsActive).HasColumnType("bit").IsRequired();

        builder.HasOne(e => e.ParentDepartment).WithMany(p => p.SubDepartments)
            .HasForeignKey(e => e.ParentDepartmentId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("FK_tblDepartment_ParentDepartmentId");
        builder.HasOne(e => e.HeadEmployee).WithMany()
            .HasForeignKey(e => e.HeadEmployeeId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("FK_tblDepartment_HeadEmployeeId");

        builder.HasIndex(e => new { e.CompanyId, e.DepartmentCode }, "UX_tblDepartment_DepartmentCode").IsUnique().HasFilter(null);
        builder.HasIndex(e => e.ParentDepartmentId, "IX_tblDepartment_ParentDepartmentId").HasFilter(null);
        builder.HasIndex(e => e.HeadEmployeeId, "IX_tblDepartment_HeadEmployeeId").HasFilter(null);
        builder.HasIndex(e => e.CompanyId, "IX_tblDepartment_CompanyId").HasFilter(null);
    }
}
