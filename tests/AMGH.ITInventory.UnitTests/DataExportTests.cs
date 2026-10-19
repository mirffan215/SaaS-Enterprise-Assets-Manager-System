using System.Text;
using AMGH.ITInventory.Application.Abstractions.Mail;
using AMGH.ITInventory.Application.CompanyAdmin;
using AMGH.ITInventory.Domain.Entities;
using Xunit;

namespace AMGH.ITInventory.UnitTests;

public class DataExportTests
{
    [Theory]
    [InlineData("plain", "plain")]
    [InlineData("a,b", "\"a,b\"")]
    [InlineData("say \"hi\"", "\"say \"\"hi\"\"\"")]
    [InlineData("=HYPERLINK(\"x\")", "\"'=HYPERLINK(\"\"x\"\")\"")]
    [InlineData("+966 12 345", "'+966 12 345")]
    [InlineData("@SUM(A1)", "'@SUM(A1)")]
    public void Csv_cells_are_quoted_and_formulas_are_defused(string input, string expected) =>
        Assert.Equal(expected, Csv.Format(input));

    [Fact]
    public void Csv_formats_numbers_dates_and_flags_with_the_invariant_culture()
    {
        Assert.Equal("1234.5", Csv.Format(1234.5m));
        Assert.Equal("-5", Csv.Format(-5));
        Assert.Equal("2026-03-04", Csv.Format(new DateOnly(2026, 3, 4)));
        Assert.Equal("1", Csv.Format(true));
        Assert.Equal(string.Empty, Csv.Format(null));
    }

    [Fact]
    public void Csv_writes_a_header_row_from_the_row_shape()
    {
        var text = Encoding.UTF8.GetString(Csv.Write(new[] { new { Id = 1, Name = "A" }, new { Id = 2, Name = "B,C" } }));
        Assert.Equal("﻿Id,Name\r\n1,A\r\n2,\"B,C\"\r\n", text);
    }

    private sealed class FakeProtector : ISecretProtector
    {
        public string Protect(string secret) => "P:" + secret;
        public string? Unprotect(string protectedSecret) => protectedSecret.StartsWith("P:", StringComparison.Ordinal) ? protectedSecret[2..] : null;
    }

    [Fact]
    public void Credentials_are_random_protected_and_expire_in_24_hours()
    {
        var now = new DateTime(2026, 1, 1, 10, 0, 0, DateTimeKind.Utc);
        var a = new DataExportRequest();
        var b = new DataExportRequest();
        ExportCredentialFactory.Issue(a, now, new FakeProtector());
        ExportCredentialFactory.Issue(b, now, new FakeProtector());
        Assert.StartsWith("P:", a.TokenProtected);
        Assert.NotEqual(a.TokenProtected, b.TokenProtected);
        Assert.NotEqual(a.PasswordProtected, b.PasswordProtected);
        Assert.Equal(24, a.PasswordProtected!.Length - 2);
        Assert.Equal(now.AddHours(24), a.TokenExpiresDate);
    }
}
