using System.Globalization;
using System.Reflection;
using System.Xml;
using System.Xml.Linq;
using System.Xml.Schema;

namespace AMGH.ITInventory.Application.ConfigTransfer;

/// <summary>Writes and reads the company configuration XML (schema: CompanyConfiguration.xsd, embedded). Reading is validating and XXE-safe.</summary>
public static class ConfigurationXml
{
    public const string Namespace = "urn:eams:company-configuration:1";
    public const int MaxBytes = 2 * 1024 * 1024;
    private static readonly XNamespace Ns = Namespace;

    private static XmlSchemaSet LoadSchema()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("AMGH.ITInventory.Application.ConfigTransfer.CompanyConfiguration.xsd")
            ?? throw new InvalidOperationException("The configuration schema is missing from the assembly.");
        var set = new XmlSchemaSet { XmlResolver = null };
        using var reader = XmlReader.Create(stream, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null });
        set.Add(Namespace, reader);
        return set;
    }

    private static readonly Lazy<XmlSchemaSet> Schema = new(LoadSchema);

    // ---------------------------------------------------------------- write
    public static byte[] Write(ConfigurationDocument doc)
    {
        ArgumentNullException.ThrowIfNull(doc);
        var root = new XElement(Ns + "CompanyConfiguration",
            new XAttribute("version", 1),
            new XAttribute("exportedUtc", doc.ExportedUtc.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture)),
            new XAttribute("sourceCompany", Trim(doc.SourceCompanyCode, 100)),
            new XAttribute("sourceCompanyName", Trim(doc.SourceCompanyName, 100)),
            new XElement(Ns + "AssetCategories", doc.Categories.OrderBy(c => c.SortOrder).ThenBy(c => c.Code, StringComparer.OrdinalIgnoreCase).Select(Category)),
            new XElement(Ns + "Classifications", doc.Classifications.OrderBy(c => c.Level).ThenBy(c => c.Code, StringComparer.OrdinalIgnoreCase).Select(Classification)));
        if (doc.Numbering is { } n)
            root.Add(new XElement(Ns + "DocumentNumbering", new XAttribute("mode", n.Mode), new XAttribute("fiscalYearStartMonth", n.FiscalYearStartMonth),
                new XAttribute("yearFormat", n.YearFormat), new XAttribute("digits", n.Digits)));
        root.Add(new XElement(Ns + "Locations", doc.Locations.OrderBy(l => l.Code, StringComparer.OrdinalIgnoreCase).Select(Location)));

        using var ms = new MemoryStream();
        using (var writer = XmlWriter.Create(ms, new XmlWriterSettings { Indent = true, Encoding = new System.Text.UTF8Encoding(false) }))
            new XDocument(new XDeclaration("1.0", "utf-8", null), root).Save(writer);
        return ms.ToArray();
    }

    private static XElement Category(CategoryConfig c)
    {
        var e = new XElement(Ns + "Category", new XAttribute("code", c.Code), new XAttribute("name", Trim(c.Name, 100)), new XAttribute("sequence", c.SequenceCode),
            new XAttribute("sequencePadding", c.SequencePadding), new XAttribute("requiresHostname", Bool(c.RequiresHostname)),
            new XAttribute("sortOrder", c.SortOrder), new XAttribute("isActive", Bool(c.IsActive)));
        if (c.DefaultUsefulLifeMonths is > 0) e.Add(new XAttribute("defaultUsefulLifeMonths", c.DefaultUsefulLifeMonths));
        if (!string.IsNullOrWhiteSpace(c.Description)) e.Add(new XElement(Ns + "Description", Trim(c.Description, 500)));
        foreach (var s in c.SubCategories.OrderBy(s => s.SortOrder).ThenBy(s => s.Code, StringComparer.OrdinalIgnoreCase))
        {
            var se = new XElement(Ns + "SubCategory", new XAttribute("code", s.Code), new XAttribute("name", Trim(s.Name, 100)),
                new XAttribute("sortOrder", s.SortOrder), new XAttribute("isActive", Bool(s.IsActive)));
            if (!string.IsNullOrWhiteSpace(s.Description)) se.Add(new XElement(Ns + "Description", Trim(s.Description, 500)));
            e.Add(se);
        }
        return e;
    }

    private static XElement Classification(ClassificationConfig c)
    {
        var e = new XElement(Ns + "Classification", new XAttribute("code", c.Code), new XAttribute("name", Trim(c.Name, 100)), new XAttribute("level", c.Level),
            new XAttribute("sortOrder", c.SortOrder), new XAttribute("isActive", Bool(c.IsActive)));
        if (!string.IsNullOrWhiteSpace(c.ColorHex)) e.Add(new XAttribute("color", c.ColorHex));
        if (!string.IsNullOrWhiteSpace(c.HandlingRequirements)) e.Add(new XElement(Ns + "HandlingRequirements", Trim(c.HandlingRequirements, 1000)));
        if (!string.IsNullOrWhiteSpace(c.Description)) e.Add(new XElement(Ns + "Description", Trim(c.Description, 500)));
        return e;
    }

    private static XElement Location(LocationConfig l)
    {
        var e = new XElement(Ns + "Location", new XAttribute("code", l.Code), new XAttribute("name", Trim(l.Name, 150)), new XAttribute("country", Trim(l.Country, 100)),
            new XAttribute("isActive", Bool(l.IsActive)));
        if (!string.IsNullOrWhiteSpace(l.City)) e.Add(new XAttribute("city", Trim(l.City, 100)));
        if (!string.IsNullOrWhiteSpace(l.AddressLine)) e.Add(new XElement(Ns + "AddressLine", Trim(l.AddressLine, 500)));
        return e;
    }

    private static string Bool(bool b) => b ? "true" : "false";
    private static string Trim(string? s, int max) => (s ?? string.Empty).Length <= max ? s ?? string.Empty : s![..max];

    // ---------------------------------------------------------------- read
    /// <summary>Parses and validates a file. The failure message lists what is wrong in plain words.</summary>
    public static (ConfigurationDocument? Document, string? Error) Read(Stream input)
    {
        ArgumentNullException.ThrowIfNull(input);
        var bytes = ReadLimited(input);
        if (bytes is null) return (null, $"The file is larger than {MaxBytes / 1024 / 1024} MB.");
        XDocument xml;
        var errors = new List<string>();
        try
        {
            var settings = new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersFromEntities = 0, MaxCharactersInDocument = MaxBytes * 2L,
                ValidationType = ValidationType.Schema, Schemas = Schema.Value
            };
            settings.ValidationEventHandler += (_, e) => { if (errors.Count < 5) errors.Add($"line {e.Exception?.LineNumber}: {e.Message}"); };
            using var reader = XmlReader.Create(new MemoryStream(bytes), settings);
            xml = XDocument.Load(reader, LoadOptions.SetLineInfo);
        }
        catch (XmlException ex)
        {
            return (null, "The file is not valid XML: " + ex.Message);
        }
        if (errors.Count > 0) return (null, "The file does not match the configuration format: " + string.Join("; ", errors));
        if (xml.Root is not { } root || root.Name != Ns + "CompanyConfiguration") return (null, "This is not a company configuration file.");

        try
        {
            return (new ConfigurationDocument(
                (string?)root.Attribute("sourceCompany") ?? string.Empty, (string?)root.Attribute("sourceCompanyName") ?? string.Empty,
                DateTime.TryParse((string?)root.Attribute("exportedUtc"), CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var at) ? at : DateTime.UtcNow,
                root.Element(Ns + "AssetCategories")?.Elements(Ns + "Category").Select(ReadCategory).ToList() ?? new List<CategoryConfig>(),
                root.Element(Ns + "Classifications")?.Elements(Ns + "Classification").Select(ReadClassification).ToList() ?? new List<ClassificationConfig>(),
                root.Element(Ns + "DocumentNumbering") is { } n
                    ? new NumberingConfig((string)n.Attribute("mode")!, ByteOr(n.Attribute("fiscalYearStartMonth"), 1), (string?)n.Attribute("yearFormat") ?? "Year", ByteOr(n.Attribute("digits"), 6))
                    : null,
                root.Element(Ns + "Locations")?.Elements(Ns + "Location").Select(ReadLocation).ToList() ?? new List<LocationConfig>()), null);
        }
        catch (Exception ex) when (ex is FormatException or OverflowException or InvalidOperationException)
        {
            return (null, "The file contains a value that cannot be read: " + ex.Message);
        }
    }

    private static byte[]? ReadLimited(Stream input)
    {
        using var ms = new MemoryStream();
        var buffer = new byte[16 * 1024];
        int read;
        while ((read = input.Read(buffer, 0, buffer.Length)) > 0)
        {
            ms.Write(buffer, 0, read);
            if (ms.Length > MaxBytes) return null;
        }
        return ms.ToArray();
    }

    private static CategoryConfig ReadCategory(XElement e) => new(
        (string)e.Attribute("code")!, (string)e.Attribute("name")!, (string)e.Attribute("sequence")!, ByteOr(e.Attribute("sequencePadding"), 5),
        e.Attribute("defaultUsefulLifeMonths") is { } m ? int.Parse(m.Value, CultureInfo.InvariantCulture) : null,
        BoolOr(e.Attribute("requiresHostname"), false), Text(e, "Description"), IntOr(e.Attribute("sortOrder")), BoolOr(e.Attribute("isActive"), true),
        e.Elements(Ns + "SubCategory").Select(s => new SubCategoryConfig((string)s.Attribute("code")!, (string)s.Attribute("name")!, Text(s, "Description"),
            IntOr(s.Attribute("sortOrder")), BoolOr(s.Attribute("isActive"), true))).ToList());

    private static ClassificationConfig ReadClassification(XElement e) => new(
        (string)e.Attribute("code")!, (string)e.Attribute("name")!, ByteOr(e.Attribute("level"), 1), Text(e, "HandlingRequirements"), (string?)e.Attribute("color"),
        Text(e, "Description"), IntOr(e.Attribute("sortOrder")), BoolOr(e.Attribute("isActive"), true));

    private static LocationConfig ReadLocation(XElement e) => new(
        (string)e.Attribute("code")!, (string)e.Attribute("name")!, Text(e, "AddressLine"), (string?)e.Attribute("city"), (string)e.Attribute("country")!, BoolOr(e.Attribute("isActive"), true));

    private static string? Text(XElement e, string child) => e.Element(Ns + child)?.Value is { Length: > 0 } v ? v : null;
    private static bool BoolOr(XAttribute? a, bool fallback) => a is null ? fallback : XmlConvert.ToBoolean(a.Value);
    private static int IntOr(XAttribute? a) => a is null ? 0 : int.Parse(a.Value, CultureInfo.InvariantCulture);
    private static byte ByteOr(XAttribute? a, byte fallback) => a is null ? fallback : byte.Parse(a.Value, CultureInfo.InvariantCulture);
}
