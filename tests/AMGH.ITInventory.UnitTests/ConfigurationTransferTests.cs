using System.Text;
using AMGH.ITInventory.Application.ConfigTransfer;
using Xunit;

namespace AMGH.ITInventory.UnitTests;

public class ConfigurationTransferTests
{
    private static ConfigurationDocument Sample(string name = "Laptop", bool active = true) => new("ACME", "Acme Ltd", new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc),
        new[]
        {
            new CategoryConfig("LAP", name, "LAP", 5, 36, true, "Portable computers", 20, active, new[] { new SubCategoryConfig("STD", "Standard Laptop", null, 10, true) }),
            new CategoryConfig("SRV", "Server", "SRV", 5, 60, true, null, 30, true, Array.Empty<SubCategoryConfig>())
        },
        new[] { new ClassificationConfig("CONF", "Confidential", 3, "Encrypt at rest", "#AA0000", null, 30, true) },
        new NumberingConfig("Yearly", 4, "FY", 6),
        new[] { new LocationConfig("HQ", "Head office & \"annex\"", "1 Main St <north>", "London", "United Kingdom", true) });

    private static ConfigurationDocument Read(byte[] xml)
    {
        var (doc, error) = ConfigurationXml.Read(new MemoryStream(xml));
        Assert.True(doc is not null, error);
        return doc!;
    }

    [Fact]
    public void Export_then_import_round_trips_every_setting_and_special_characters()
    {
        var back = Read(ConfigurationXml.Write(Sample()));
        Assert.Equal("ACME", back.SourceCompanyCode);
        Assert.Equal(2, back.Categories.Count);
        var lap = back.Categories.Single(c => c.Code == "LAP");
        Assert.Equal((36, true, "Portable computers", 20), (lap.DefaultUsefulLifeMonths, lap.RequiresHostname, lap.Description, lap.SortOrder));
        Assert.Equal("STD", Assert.Single(lap.SubCategories).Code);
        Assert.Equal(("CONF", (byte)3, "#AA0000"), (back.Classifications[0].Code, back.Classifications[0].Level, back.Classifications[0].ColorHex));
        Assert.Equal(new NumberingConfig("Yearly", 4, "FY", 6), back.Numbering);
        Assert.Equal("Head office & \"annex\"", back.Locations[0].Name);
        Assert.Equal("1 Main St <north>", back.Locations[0].AddressLine);
    }

    [Fact]
    public void Schema_rejects_values_outside_the_allowed_ranges()
    {
        var bad = Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(ConfigurationXml.Write(Sample())).Replace("level=\"3\"", "level=\"42\"", StringComparison.Ordinal));
        var (doc, error) = ConfigurationXml.Read(new MemoryStream(bad));
        Assert.Null(doc);
        Assert.Contains("does not match", error, StringComparison.Ordinal);
    }

    [Fact]
    public void Doctype_and_external_entities_are_refused()
    {
        var xxe = "<?xml version=\"1.0\"?><!DOCTYPE x [<!ENTITY e SYSTEM \"file:///etc/passwd\">]><CompanyConfiguration xmlns=\"urn:eams:company-configuration:1\" version=\"1\" sourceCompany=\"&e;\"/>";
        var (doc, error) = ConfigurationXml.Read(new MemoryStream(Encoding.UTF8.GetBytes(xxe)));
        Assert.Null(doc);
        Assert.NotNull(error);
    }

    [Fact]
    public void Wrong_root_element_and_garbage_are_refused()
    {
        Assert.Null(ConfigurationXml.Read(new MemoryStream(Encoding.UTF8.GetBytes("<other/>"))).Document);
        Assert.Null(ConfigurationXml.Read(new MemoryStream(Encoding.UTF8.GetBytes("not xml"))).Document);
    }

    [Fact]
    public void Files_over_the_size_limit_are_refused()
    {
        var big = new MemoryStream(new byte[ConfigurationXml.MaxBytes + 1]);
        var (doc, error) = ConfigurationXml.Read(big);
        Assert.Null(doc);
        Assert.Contains("larger", error, StringComparison.Ordinal);
    }

    [Fact]
    public void Diff_reports_additions_modifications_and_unchanged_rows()
    {
        var current = new ConfigurationDocument("B", "B", DateTime.UtcNow,
            new[] { new CategoryConfig("LAP", "Notebook", "LAP", 5, 36, true, "Portable computers", 20, true, Array.Empty<SubCategoryConfig>()) },
            Array.Empty<ClassificationConfig>(), new NumberingConfig("Continuous", 1, "Year", 6), Array.Empty<LocationConfig>());
        var changes = ConfigurationDiff.Compute(current, Sample());

        var lap = changes.Single(c => c.Key == "category:LAP");
        Assert.Equal(ChangeKind.Modified, lap.Kind);
        Assert.Equal(new[] { "Name" }, lap.Fields.Select(f => f.Field));
        Assert.Equal(("Notebook", "Laptop"), (lap.Fields[0].Current, lap.Fields[0].Incoming));
        Assert.Equal(ChangeKind.Added, changes.Single(c => c.Key == "subcategory:LAP/STD").Kind);
        Assert.Equal(ChangeKind.Added, changes.Single(c => c.Key == "category:SRV").Kind);
        Assert.Equal(ChangeKind.Added, changes.Single(c => c.Key == "classification:CONF").Kind);
        Assert.Equal(ChangeKind.Added, changes.Single(c => c.Key == "location:HQ").Kind);
        Assert.Equal(ChangeKind.Modified, changes.Single(c => c.Key == "numbering:company").Kind);
    }

    [Fact]
    public void Identical_configuration_is_all_unchanged()
    {
        var changes = ConfigurationDiff.Compute(Sample(), Sample());
        Assert.All(changes, c => Assert.Equal(ChangeKind.Unchanged, c.Kind));
    }

    [Fact]
    public void Codes_match_without_regard_to_case()
    {
        var lower = Sample() with { Categories = new[] { Sample().Categories[0] with { Code = "lap" } } };
        Assert.Equal(ChangeKind.Unchanged, ConfigurationDiff.Compute(Sample(), lower).Single(c => c.Key == "category:LAP").Kind);
    }
}
