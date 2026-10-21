using AMGH.ITInventory.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AMGH.ITInventory.Persistence.Configurations;

/// <summary>Maps <see cref="Company"/> to dbo.tblCompany (script 10).</summary>
internal sealed class CompanyConfiguration : IEntityTypeConfiguration<Company>
{
    public void Configure(EntityTypeBuilder<Company> builder)
    {
        builder.ToTable("tblCompany", "dbo", tb =>
        {
            tb.HasCheckConstraint("CK_tblCompany_Code", "CompanyCode NOT LIKE N'%[^A-Z0-9]%' AND LEN(CompanyCode) BETWEEN 2 AND 10");
            tb.HasCheckConstraint("CK_tblCompany_Status", "Status IN (N'Active', N'Suspended', N'Archived')");
        });
        builder.Property(e => e.NameArabic).HasColumnType("nvarchar(200)").HasMaxLength(200);
        builder.Property(e => e.CompanyType).HasColumnType("nvarchar(50)").HasMaxLength(50);
        builder.Property(e => e.VatNumber).HasColumnType("nvarchar(30)").HasMaxLength(30);
        builder.Property(e => e.Region).HasColumnType("nvarchar(100)").HasMaxLength(100);
        builder.Property(e => e.DateFormat).HasColumnType("nvarchar(20)").HasMaxLength(20).IsRequired();
        builder.Property(e => e.MfaPolicy).HasColumnType("tinyint").IsRequired().HasDefaultValue((byte)0);
        builder.Property(e => e.AllowedIpRanges).HasColumnType("nvarchar(1000)").HasMaxLength(1000);
        builder.Property(e => e.SsoEnabled).HasColumnType("bit").IsRequired().HasDefaultValue(false);
        builder.Property(e => e.SsoAuthority).HasColumnType("nvarchar(300)").HasMaxLength(300);
        builder.Property(e => e.SsoClientId).HasColumnType("nvarchar(200)").HasMaxLength(200);
        builder.Property(e => e.SsoClientSecret).HasColumnType("nvarchar(1000)").HasMaxLength(1000);
        builder.Property(e => e.SsoAllowedDomain).HasColumnType("nvarchar(200)").HasMaxLength(200);
        builder.HasKey(e => e.CompanyId).HasName("PK_tblCompany");
        builder.Property(e => e.CompanyId).UseIdentityColumn();
        builder.Property(e => e.CompanyCode).HasColumnType("nvarchar(10)").HasMaxLength(10).IsRequired();
        builder.Property(e => e.LegalName).HasColumnType("nvarchar(200)").HasMaxLength(200).IsRequired();
        builder.Property(e => e.DisplayName).HasColumnType("nvarchar(100)").HasMaxLength(100).IsRequired();
        builder.Property(e => e.RegistrationNo).HasColumnType("nvarchar(50)").HasMaxLength(50);
        builder.Property(e => e.Industry).HasColumnType("nvarchar(100)").HasMaxLength(100);
        builder.Property(e => e.Email).HasColumnType("nvarchar(256)").HasMaxLength(256);
        builder.Property(e => e.Phone).HasColumnType("nvarchar(30)").HasMaxLength(30);
        builder.Property(e => e.Website).HasColumnType("nvarchar(250)").HasMaxLength(250);
        builder.Property(e => e.Country).HasColumnType("nvarchar(100)").HasMaxLength(100).IsRequired();
        builder.Property(e => e.City).HasColumnType("nvarchar(100)").HasMaxLength(100);
        builder.Property(e => e.AddressLine).HasColumnType("nvarchar(500)").HasMaxLength(500);
        builder.Property(e => e.TimeZone).HasColumnType("nvarchar(64)").HasMaxLength(64).IsRequired();
        builder.Property(e => e.LanguageCode).HasColumnType("nvarchar(10)").HasMaxLength(10).IsRequired();
        builder.Property(e => e.AssetNumberPrefix).HasColumnType("nvarchar(20)").HasMaxLength(20).IsRequired();
        builder.Property(e => e.Status).HasColumnType("nvarchar(20)").HasMaxLength(20).IsRequired();
        builder.Property(e => e.DocNumberingMode).HasColumnType("nvarchar(10)").HasMaxLength(10).IsRequired();
        builder.Property(e => e.FiscalYearStartMonth).HasColumnType("tinyint").IsRequired();
        builder.Property(e => e.DocYearFormat).HasColumnType("nvarchar(10)").HasMaxLength(10).IsRequired();
        builder.Property(e => e.DocNumberDigits).HasColumnType("tinyint").IsRequired();
        builder.Property(e => e.CreatedBy).HasColumnType("nvarchar(100)").HasMaxLength(100).IsRequired();
        builder.Property(e => e.CreatedDate).HasColumnType("datetime2(3)").HasDefaultValueSql("SYSUTCDATETIME()");
        builder.Property(e => e.ModifiedBy).HasColumnType("nvarchar(100)").HasMaxLength(100);
        builder.Property(e => e.ModifiedDate).HasColumnType("datetime2(3)");
        builder.Property(e => e.RowVersion).IsRowVersion();
        builder.HasIndex(e => e.CompanyCode, "UQ_tblCompany_CompanyCode").IsUnique();
        builder.HasIndex(e => e.AssetNumberPrefix, "UQ_tblCompany_AssetNumberPrefix").IsUnique();
    }
}

/// <summary>Maps <see cref="CompanyUser"/> to dbo.tblCompanyUser (script 10).</summary>
internal sealed class CompanyUserConfiguration : IEntityTypeConfiguration<CompanyUser>
{
    public void Configure(EntityTypeBuilder<CompanyUser> builder)
    {
        builder.ToTable("tblCompanyUser", "dbo");
        builder.HasKey(e => new { e.CompanyId, e.UserId }).HasName("PK_tblCompanyUser");
        builder.Property(e => e.IsActive).HasColumnType("bit").IsRequired();
        builder.Property(e => e.IsDefault).HasColumnType("bit").IsRequired();
        builder.Property(e => e.CreatedBy).HasColumnType("nvarchar(100)").HasMaxLength(100).IsRequired();
        builder.Property(e => e.CreatedDate).HasColumnType("datetime2(3)").HasDefaultValueSql("SYSUTCDATETIME()");
        builder.Property(e => e.ModifiedBy).HasColumnType("nvarchar(100)").HasMaxLength(100);
        builder.Property(e => e.ModifiedDate).HasColumnType("datetime2(3)");
        builder.Property(e => e.RowVersion).IsRowVersion();
        builder.HasOne(e => e.Company).WithMany(c => c.Members).HasForeignKey(e => e.CompanyId)
            .OnDelete(DeleteBehavior.Restrict).HasConstraintName("FK_tblCompanyUser_CompanyId");
        builder.HasOne(e => e.User).WithMany().HasForeignKey(e => e.UserId)
            .OnDelete(DeleteBehavior.Restrict).HasConstraintName("FK_tblCompanyUser_UserId");
        builder.HasIndex(e => e.UserId, "UX_tblCompanyUser_UserId_Default").IsUnique().HasFilter("[IsDefault] = 1");
    }
}

/// <summary>Maps <see cref="CompanyBranding"/> to dbo.tblCompanyBranding (script 11).</summary>
internal sealed class CompanyBrandingConfiguration : IEntityTypeConfiguration<CompanyBranding>
{
    public void Configure(EntityTypeBuilder<CompanyBranding> builder)
    {
        builder.ToTable("tblCompanyBranding", "dbo");
        builder.Property(e => e.LogoSourceUrl).HasColumnType("nvarchar(500)").HasMaxLength(500);
        builder.HasKey(e => e.CompanyId).HasName("PK_tblCompanyBranding");
        builder.Property(e => e.CompanyId).ValueGeneratedNever();
        builder.Property(e => e.LogoImage).HasColumnType("varbinary(max)");
        builder.Property(e => e.LogoContentType).HasColumnType("nvarchar(50)").HasMaxLength(50);
        builder.Property(e => e.HeaderColor).HasColumnType("char(7)").HasMaxLength(7).IsFixedLength().IsUnicode(false).IsRequired();
        builder.Property(e => e.PrimaryColor).HasColumnType("char(7)").HasMaxLength(7).IsFixedLength().IsUnicode(false).IsRequired();
        builder.Property(e => e.AccentColor).HasColumnType("char(7)").HasMaxLength(7).IsFixedLength().IsUnicode(false).IsRequired();
        builder.Property(e => e.LoginTagline).HasColumnType("nvarchar(150)").HasMaxLength(150);
        builder.Property(e => e.ModifiedBy).HasColumnType("nvarchar(100)").HasMaxLength(100).IsRequired();
        builder.Property(e => e.ModifiedDate).HasColumnType("datetime2(3)").IsRequired();
        builder.Property(e => e.RowVersion).IsRowVersion();
        builder.HasOne(e => e.Company).WithOne(c => c.Branding).HasForeignKey<CompanyBranding>(e => e.CompanyId)
            .OnDelete(DeleteBehavior.Restrict).HasConstraintName("FK_tblCompanyBranding_CompanyId");
    }
}
