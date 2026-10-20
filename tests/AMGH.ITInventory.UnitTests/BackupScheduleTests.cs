using AMGH.ITInventory.Web.Controllers;
using AMGH.ITInventory.Web.Infrastructure;
using Xunit;

namespace AMGH.ITInventory.UnitTests;

public class BackupScheduleTests
{
    [Fact]
    public void Off_is_never_due() => Assert.False(new BackupSchedule().IsDue(new DateTime(2026, 10, 20, 12, 0, 0)));

    [Fact]
    public void Daily_is_due_after_the_time_until_it_has_run()
    {
        var s = new BackupSchedule { Mode = BackupSchedule.Daily, Time = "02:00" };
        Assert.False(s.IsDue(new DateTime(2026, 10, 20, 1, 0, 0)) && s.LastRunLocal is not null);
        Assert.True(s.IsDue(new DateTime(2026, 10, 20, 2, 1, 0)));
        s.LastRunLocal = new DateTime(2026, 10, 20, 2, 1, 5);
        Assert.False(s.IsDue(new DateTime(2026, 10, 20, 23, 0, 0)));
        Assert.True(s.IsDue(new DateTime(2026, 10, 21, 2, 0, 0)));
    }

    [Fact]
    public void Weekly_uses_the_chosen_day()
    {
        var s = new BackupSchedule { Mode = BackupSchedule.Weekly, Day = 0, Time = "03:30" }; // Sunday
        Assert.Equal(new DateTime(2026, 10, 18, 3, 30, 0), s.LastDueSlot(new DateTime(2026, 10, 20, 12, 0, 0)));
        Assert.Equal(new DateTime(2026, 10, 11, 3, 30, 0), s.LastDueSlot(new DateTime(2026, 10, 18, 3, 0, 0)));
    }

    [Fact]
    public void Malformed_time_is_not_due()
    {
        var s = new BackupSchedule { Mode = BackupSchedule.Daily, Time = "25:99" };
        Assert.Null(s.LastDueSlot(DateTime.Now));
    }

    [Theory]
    [InlineData("short1A!", false)]
    [InlineData("alllowercaseletters", false)]
    [InlineData("Correct-Horse-9", true)]
    public void Zip_password_rule(string password, bool ok) =>
        Assert.Equal(ok, CompanyBackupController.PasswordProblem(password) is null);
}
