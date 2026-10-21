using System.Globalization;

namespace AMGH.ITInventory.Application.ConfigTransfer;

/// <summary>Compares the configuration in a file (or another company) with the current company's: additions, modifications, unchanged.</summary>
public static class ConfigurationDiff
{
    public static IReadOnlyList<ConfigChange> Compute(ConfigurationDocument current, ConfigurationDocument incoming)
    {
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(incoming);
        var changes = new List<ConfigChange>();

        var cats = current.Categories.ToDictionary(c => c.Code, StringComparer.OrdinalIgnoreCase);
        foreach (var c in incoming.Categories)
        {
            var key = "category:" + c.Code.ToUpperInvariant();
            var title = $"{c.Code} · {c.Name}";
            if (!cats.TryGetValue(c.Code, out var have))
            {
                changes.Add(new ConfigChange(key, "Asset categories", title, ChangeKind.Added, New(("Number series", c.SequenceCode), ("Useful life (months)", Int(c.DefaultUsefulLifeMonths)),
                    ("Needs host name", Flag(c.RequiresHostname)), ("Active", Flag(c.IsActive)))));
                foreach (var s in c.SubCategories)
                    changes.Add(new ConfigChange($"subcategory:{c.Code.ToUpperInvariant()}/{s.Code.ToUpperInvariant()}", "Sub-categories", $"{c.Code} / {s.Code} · {s.Name}", ChangeKind.Added, New(("Active", Flag(s.IsActive)))));
                continue;
            }
            var fields = new List<FieldChange>();
            Diff(fields, "Name", have.Name, c.Name);
            Diff(fields, "Useful life (months)", Int(have.DefaultUsefulLifeMonths), Int(c.DefaultUsefulLifeMonths));
            Diff(fields, "Needs host name", Flag(have.RequiresHostname), Flag(c.RequiresHostname));
            Diff(fields, "Description", have.Description, c.Description);
            Diff(fields, "Sort order", have.SortOrder.ToString(CultureInfo.InvariantCulture), c.SortOrder.ToString(CultureInfo.InvariantCulture));
            Diff(fields, "Active", Flag(have.IsActive), Flag(c.IsActive));
            changes.Add(new ConfigChange(key, "Asset categories", title, fields.Count == 0 ? ChangeKind.Unchanged : ChangeKind.Modified, fields));

            var subs = have.SubCategories.ToDictionary(s => s.Code, StringComparer.OrdinalIgnoreCase);
            foreach (var s in c.SubCategories)
            {
                var skey = $"subcategory:{c.Code.ToUpperInvariant()}/{s.Code.ToUpperInvariant()}";
                var stitle = $"{c.Code} / {s.Code} · {s.Name}";
                if (!subs.TryGetValue(s.Code, out var sh)) { changes.Add(new ConfigChange(skey, "Sub-categories", stitle, ChangeKind.Added, New(("Active", Flag(s.IsActive))))); continue; }
                var sf = new List<FieldChange>();
                Diff(sf, "Name", sh.Name, s.Name);
                Diff(sf, "Description", sh.Description, s.Description);
                Diff(sf, "Sort order", sh.SortOrder.ToString(CultureInfo.InvariantCulture), s.SortOrder.ToString(CultureInfo.InvariantCulture));
                Diff(sf, "Active", Flag(sh.IsActive), Flag(s.IsActive));
                changes.Add(new ConfigChange(skey, "Sub-categories", stitle, sf.Count == 0 ? ChangeKind.Unchanged : ChangeKind.Modified, sf));
            }
        }

        var cls = current.Classifications.ToDictionary(c => c.Code, StringComparer.OrdinalIgnoreCase);
        foreach (var c in incoming.Classifications)
        {
            var key = "classification:" + c.Code.ToUpperInvariant();
            var title = $"{c.Code} · {c.Name}";
            if (!cls.TryGetValue(c.Code, out var have)) { changes.Add(new ConfigChange(key, "Classifications", title, ChangeKind.Added, New(("Level", c.Level.ToString(CultureInfo.InvariantCulture)), ("Colour", c.ColorHex)))); continue; }
            var fields = new List<FieldChange>();
            Diff(fields, "Name", have.Name, c.Name);
            Diff(fields, "Level", have.Level.ToString(CultureInfo.InvariantCulture), c.Level.ToString(CultureInfo.InvariantCulture));
            Diff(fields, "Colour", have.ColorHex, c.ColorHex);
            Diff(fields, "Handling requirements", have.HandlingRequirements, c.HandlingRequirements);
            Diff(fields, "Description", have.Description, c.Description);
            Diff(fields, "Sort order", have.SortOrder.ToString(CultureInfo.InvariantCulture), c.SortOrder.ToString(CultureInfo.InvariantCulture));
            Diff(fields, "Active", Flag(have.IsActive), Flag(c.IsActive));
            changes.Add(new ConfigChange(key, "Classifications", title, fields.Count == 0 ? ChangeKind.Unchanged : ChangeKind.Modified, fields));
        }

        if (incoming.Numbering is { } n)
        {
            var have = current.Numbering;
            var fields = new List<FieldChange>();
            Diff(fields, "Mode", have?.Mode, n.Mode);
            Diff(fields, "Fiscal year starts in month", have?.FiscalYearStartMonth.ToString(CultureInfo.InvariantCulture), n.FiscalYearStartMonth.ToString(CultureInfo.InvariantCulture));
            Diff(fields, "Year format", have?.YearFormat, n.YearFormat);
            Diff(fields, "Digits", have?.Digits.ToString(CultureInfo.InvariantCulture), n.Digits.ToString(CultureInfo.InvariantCulture));
            changes.Add(new ConfigChange("numbering:company", "Document numbering", "Document number format", fields.Count == 0 ? ChangeKind.Unchanged : ChangeKind.Modified, fields));
        }

        var locs = current.Locations.ToDictionary(l => l.Code, StringComparer.OrdinalIgnoreCase);
        foreach (var l in incoming.Locations)
        {
            var key = "location:" + l.Code.ToUpperInvariant();
            var title = $"{l.Code} · {l.Name}";
            if (!locs.TryGetValue(l.Code, out var have)) { changes.Add(new ConfigChange(key, "Locations", title, ChangeKind.Added, New(("City", l.City), ("Country", l.Country)))); continue; }
            var fields = new List<FieldChange>();
            Diff(fields, "Name", have.Name, l.Name);
            Diff(fields, "Address", have.AddressLine, l.AddressLine);
            Diff(fields, "City", have.City, l.City);
            Diff(fields, "Country", have.Country, l.Country);
            Diff(fields, "Active", Flag(have.IsActive), Flag(l.IsActive));
            changes.Add(new ConfigChange(key, "Locations", title, fields.Count == 0 ? ChangeKind.Unchanged : ChangeKind.Modified, fields));
        }
        return changes;
    }

    private static void Diff(List<FieldChange> fields, string name, string? current, string? incoming)
    {
        if (!string.Equals(Norm(current), Norm(incoming), StringComparison.Ordinal)) fields.Add(new FieldChange(name, current, incoming));
    }

    private static string Norm(string? s) => (s ?? string.Empty).Trim();
    private static string Flag(bool b) => b ? "Yes" : "No";
    private static string? Int(int? v) => v?.ToString(CultureInfo.InvariantCulture);
    private static List<FieldChange> New(params (string Name, string? Value)[] values) =>
        values.Where(v => !string.IsNullOrWhiteSpace(v.Value)).Select(v => new FieldChange(v.Name, null, v.Value)).ToList();
}
