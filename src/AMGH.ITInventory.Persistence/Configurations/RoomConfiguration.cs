using AMGH.ITInventory.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using AMGH.ITInventory.Persistence.Extensions;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AMGH.ITInventory.Persistence.Configurations;

/// <summary>Maps <see cref="Room"/> to dbo.tblRoom. GENERATED - matches deployment/SQLScripts/02_Tables.sql.</summary>
internal sealed class RoomConfiguration : IEntityTypeConfiguration<Room>
{
    public void Configure(EntityTypeBuilder<Room> builder)
    {
        builder.ToTable("tblRoom", "dbo", tb =>
        {
            tb.HasCheckConstraint("CK_tblRoom_RoomType_Domain", "[RoomType] IN (N'Office', N'ServerRoom', N'DataCenter', N'Store', N'MeetingRoom', N'Other')");
        });

        builder.ConfigureAuditColumns();
        builder.HasKey(e => e.RoomId).HasName("PK_tblRoom");

        builder.Property(e => e.CompanyId).HasColumnType("int").IsRequired();
        builder.Property(e => e.RoomId).HasColumnType("int").IsRequired().UseIdentityColumn();
        builder.Property(e => e.FloorId).HasColumnType("int").IsRequired();
        builder.Property(e => e.RoomCode).HasColumnType("nvarchar(20)").HasMaxLength(20).IsRequired();
        builder.Property(e => e.RoomName).HasColumnType("nvarchar(100)").HasMaxLength(100).IsRequired();
        builder.Property(e => e.RoomType).HasConversion<string>().HasColumnType("nvarchar(30)").HasMaxLength(30).IsRequired();
        builder.Property(e => e.IsSecureArea).HasColumnType("bit").IsRequired();
        builder.Property(e => e.IsActive).HasColumnType("bit").IsRequired();

        builder.HasOne(e => e.Floor).WithMany(p => p.Rooms)
            .HasForeignKey(e => e.FloorId).IsRequired()
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("FK_tblRoom_FloorId");

        builder.HasIndex(e => new { e.CompanyId, e.FloorId, e.RoomCode }, "UX_tblRoom_FloorId_RoomCode").IsUnique().HasFilter(null);
        builder.HasIndex(e => e.FloorId, "IX_tblRoom_FloorId").HasFilter(null);
        builder.HasIndex(e => e.CompanyId, "IX_tblRoom_CompanyId").HasFilter(null);
    }
}
