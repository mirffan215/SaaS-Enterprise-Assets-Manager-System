using AMGH.ITInventory.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using AMGH.ITInventory.Persistence.Extensions;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AMGH.ITInventory.Persistence.Configurations;

/// <summary>Maps <see cref="Employee"/> to dbo.tblEmployee. GENERATED - matches deployment/SQLScripts/02_Tables.sql.</summary>
internal sealed class EmployeeConfiguration : IEntityTypeConfiguration<Employee>
{
    public void Configure(EntityTypeBuilder<Employee> builder)
    {
        builder.ToTable("tblEmployee", "dbo", tb =>
        {
            tb.HasCheckConstraint("CK_tblEmployee_Email", "[Email] IS NULL OR [Email] LIKE N'%_@_%._%'");
            tb.HasCheckConstraint("CK_tblEmployee_EmploymentStatus_Domain", "[EmploymentStatus] IN (N'Active', N'OnLeave', N'Terminated')");
            tb.HasCheckConstraint("CK_tblEmployee_Rule01", "[TerminationDate] IS NULL OR [HireDate] IS NULL OR [TerminationDate] >= [HireDate]");
            tb.HasCheckConstraint("CK_tblEmployee_Rule02", "[EmploymentStatus] <> N'Terminated' OR [TerminationDate] IS NOT NULL");
            tb.HasCheckConstraint("CK_tblEmployee_Rule03", "[ManagerEmployeeId] IS NULL OR [ManagerEmployeeId] <> [EmployeeId]");
        });

        builder.ConfigureAuditColumns();
        builder.HasKey(e => e.EmployeeId).HasName("PK_tblEmployee");

        builder.Property(e => e.CompanyId).HasColumnType("int").IsRequired();
        builder.Property(e => e.EmployeeId).HasColumnType("int").IsRequired().UseIdentityColumn();
        builder.Property(e => e.EmployeeNumber).HasColumnType("nvarchar(30)").HasMaxLength(30).IsRequired();
        builder.Property(e => e.FirstName).HasColumnType("nvarchar(100)").HasMaxLength(100).IsRequired();
        builder.Property(e => e.LastName).HasColumnType("nvarchar(100)").HasMaxLength(100).IsRequired();
        builder.Property(e => e.FullName).HasColumnType("nvarchar(201)").HasComputedColumnSql("CONCAT([FirstName], N' ', [LastName])", stored: true);
        builder.Property(e => e.Email).HasColumnType("nvarchar(256)").HasMaxLength(256);
        builder.Property(e => e.Phone).HasColumnType("nvarchar(30)").HasMaxLength(30);
        builder.Property(e => e.JobTitle).HasColumnType("nvarchar(150)").HasMaxLength(150);
        builder.Property(e => e.DepartmentId).HasColumnType("int").IsRequired();
        builder.Property(e => e.LocationId).HasColumnType("int");
        builder.Property(e => e.ManagerEmployeeId).HasColumnType("int");
        builder.Property(e => e.HireDate).HasColumnType("date");
        builder.Property(e => e.TerminationDate).HasColumnType("date");
        builder.Property(e => e.EmploymentStatus).HasConversion<string>().HasColumnType("nvarchar(30)").HasMaxLength(30).IsRequired();

        builder.HasOne(e => e.Department).WithMany(p => p.Employees)
            .HasForeignKey(e => e.DepartmentId).IsRequired()
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("FK_tblEmployee_DepartmentId");
        builder.HasOne(e => e.Location).WithMany()
            .HasForeignKey(e => e.LocationId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("FK_tblEmployee_LocationId");
        builder.HasOne(e => e.Manager).WithMany(p => p.DirectReports)
            .HasForeignKey(e => e.ManagerEmployeeId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("FK_tblEmployee_ManagerEmployeeId");

        builder.HasIndex(e => new { e.CompanyId, e.EmployeeNumber }, "UX_tblEmployee_EmployeeNumber").IsUnique().HasFilter(null);
        builder.HasIndex(e => new { e.CompanyId, e.Email }, "UX_tblEmployee_Email").IsUnique().HasFilter("[Email] IS NOT NULL AND [IsDeleted] = 0");
        builder.HasIndex(e => e.DepartmentId, "IX_tblEmployee_DepartmentId").HasFilter(null);
        builder.HasIndex(e => e.LocationId, "IX_tblEmployee_LocationId").HasFilter(null);
        builder.HasIndex(e => e.ManagerEmployeeId, "IX_tblEmployee_ManagerEmployeeId").HasFilter(null);
        builder.HasIndex(e => e.CompanyId, "IX_tblEmployee_CompanyId").HasFilter(null);
    }
}
