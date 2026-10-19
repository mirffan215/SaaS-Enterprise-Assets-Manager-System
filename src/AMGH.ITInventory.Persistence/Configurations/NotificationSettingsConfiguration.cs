using AMGH.ITInventory.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AMGH.ITInventory.Persistence.Configurations;

// Release 2.5 mappings: company e-mail settings, templates, reminder rules and user preferences (script 14).
// (Previously in NotificationConfiguration.cs, which replaced the in-app notification mapping by mistake.)

internal sealed class CompanyMailSettingsConfiguration : IEntityTypeConfiguration<CompanyMailSettings>
{
    public void Configure(EntityTypeBuilder<CompanyMailSettings> b)
    {
        b.ToTable("tblCompanyMailSettings", "dbo");
        b.HasKey(e => e.CompanyId).HasName("PK_tblCompanyMailSettings");
        b.Property(e => e.CompanyId).ValueGeneratedNever();
        b.Property(e => e.Mode).HasColumnType("nvarchar(30)").HasMaxLength(30).IsRequired();
        b.Property(e => e.Host).HasColumnType("nvarchar(255)").HasMaxLength(255).IsRequired();
        b.Property(e => e.Security).HasColumnType("nvarchar(20)").HasMaxLength(20).IsRequired();
        b.Property(e => e.CertificateThumbprint).HasColumnType("nvarchar(128)").HasMaxLength(128);
        b.Property(e => e.Username).HasColumnType("nvarchar(256)").HasMaxLength(256);
        b.Property(e => e.SecretProtected).HasColumnType("nvarchar(max)");
        b.Property(e => e.TenantId).HasColumnType("nvarchar(100)").HasMaxLength(100);
        b.Property(e => e.ClientId).HasColumnType("nvarchar(100)").HasMaxLength(100);
        b.Property(e => e.FromAddress).HasColumnType("nvarchar(256)").HasMaxLength(256).IsRequired();
        b.Property(e => e.FromName).HasColumnType("nvarchar(100)").HasMaxLength(100);
        b.Property(e => e.ReplyTo).HasColumnType("nvarchar(256)").HasMaxLength(256);
        b.Property(e => e.AppBaseUrl).HasColumnType("nvarchar(250)").HasMaxLength(250);
        b.Property(e => e.LastTestDate).HasColumnType("datetime2(3)");
        b.Property(e => e.LastTestResult).HasColumnType("nvarchar(500)").HasMaxLength(500);
        b.Property(e => e.ModifiedBy).HasColumnType("nvarchar(100)").HasMaxLength(100).IsRequired();
        b.Property(e => e.ModifiedDate).HasColumnType("datetime2(3)").IsRequired();
        b.Property(e => e.RowVersion).IsRowVersion();
    }
}

internal sealed class NotificationTemplateConfiguration : IEntityTypeConfiguration<NotificationTemplate>
{
    public void Configure(EntityTypeBuilder<NotificationTemplate> b)
    {
        b.ToTable("tblNotificationTemplate", "dbo");
        b.HasKey(e => e.NotificationTemplateId).HasName("PK_tblNotificationTemplate");
        b.Property(e => e.NotificationTemplateId).UseIdentityColumn();
        b.Property(e => e.TemplateKey).HasColumnType("nvarchar(50)").HasMaxLength(50).IsRequired();
        b.Property(e => e.Subject).HasColumnType("nvarchar(200)").HasMaxLength(200).IsRequired();
        b.Property(e => e.BodyHtml).HasColumnType("nvarchar(max)").IsRequired();
        b.Property(e => e.ModifiedBy).HasColumnType("nvarchar(100)").HasMaxLength(100).IsRequired();
        b.Property(e => e.ModifiedDate).HasColumnType("datetime2(3)").IsRequired();
        b.HasIndex(e => new { e.CompanyId, e.TemplateKey }, "UX_tblNotificationTemplate_Company_Key").IsUnique();
    }
}

internal sealed class NotificationRuleConfiguration : IEntityTypeConfiguration<NotificationRule>
{
    public void Configure(EntityTypeBuilder<NotificationRule> b)
    {
        b.ToTable("tblNotificationRule", "dbo");
        b.HasKey(e => e.NotificationRuleId).HasName("PK_tblNotificationRule");
        b.Property(e => e.NotificationRuleId).UseIdentityColumn();
        b.Property(e => e.RuleKey).HasColumnType("nvarchar(50)").HasMaxLength(50).IsRequired();
        b.Property(e => e.DaysBefore).HasColumnType("nvarchar(100)").HasMaxLength(100);
        b.Property(e => e.RecipientRoles).HasColumnType("nvarchar(400)").HasMaxLength(400);
        b.Property(e => e.LastRunDate).HasColumnType("date");
        b.Property(e => e.ModifiedBy).HasColumnType("nvarchar(100)").HasMaxLength(100).IsRequired();
        b.Property(e => e.ModifiedDate).HasColumnType("datetime2(3)").IsRequired();
        b.HasIndex(e => new { e.CompanyId, e.RuleKey }, "UX_tblNotificationRule_Company_Key").IsUnique();
    }
}

internal sealed class NotificationPreferenceConfiguration : IEntityTypeConfiguration<NotificationPreference>
{
    public void Configure(EntityTypeBuilder<NotificationPreference> b)
    {
        b.ToTable("tblNotificationPreference", "dbo");
        b.HasKey(e => new { e.CompanyId, e.UserId, e.RuleKey }).HasName("PK_tblNotificationPreference");
        b.Property(e => e.RuleKey).HasColumnType("nvarchar(50)").HasMaxLength(50).IsRequired();
        b.Property(e => e.ModifiedDate).HasColumnType("datetime2(3)").IsRequired();
    }
}
