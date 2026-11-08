using System.Reflection;
using System.Text.RegularExpressions;
using Jiron.Umbraco.FormBuilder.Core.Settings;
using Jiron.Umbraco.FormBuilder.Testing;

namespace Jiron.Umbraco.FormBuilder.Tests.Unit.Settings;

/// <summary>
/// Every setting has a screen and a reader (ROADMAP 12.4a, ADR-0218): each property of every settings section, and of the
/// objects nested in one, is a field of its screen or host-only, and product code outside the settings module reads it.
/// The module is the machinery that stores, checks and shows the sections, with each section's own file, whose validator
/// reads every field; a service that lives beside it, such as the sites' base URLs, is product code. A reader names the
/// section, or the nested object, and reads the property by its name, or calls the section's member that reads it.
/// </summary>
[TestFixture]
public sealed class SettingsWiringTests
{
    private static readonly string Src = RepositoryRoot.Path(typeof(SettingsWiringTests).Assembly, "src");

    // The machinery of the settings module, in Core/Settings; the settings endpoints are part of it too.
    private static readonly string[] Machinery =
    [
        "FormBuilderSettingsCacheRefresher.cs", "FormBuilderSettingsSections.cs", "FormBuilderSettingsService.cs",
        "FormBuilderSettingsServices.cs", "ISettingsSection.cs", "SettingsFieldAttribute.cs", "SettingsHostRules.cs",
        "SettingsSavedNotification.cs", "SettingsSchema.cs", "SettingsSectionJson.cs", "SettingsValidation.cs",
    ];

    // The settings a section's own member reads for its callers, by that member: product code calls the member.
    private static readonly Dictionary<string, string> ReadThrough = new(StringComparer.Ordinal)
    {
        ["ProtectionSettings.BlockedRanges"] = "Blocks",
        ["FilesSettings.AllowedTypes"] = "Takes",
        ["FilesSettings.MaxFileBytes"] = "LimitWithin",
    };

    [Test]
    public void EverySetting_HasAScreen_AndIsReadByProductCode_OutsideTheSettingsModule()
    {
        var sources = Sources().ToList();
        var settings = Settings().ToList();
        var noScreen = settings.Where(each => each.Property.GetCustomAttribute<SettingsFieldAttribute>() is null && each.Property.GetCustomAttribute<HostOnlyAttribute>() is null);
        var unread = settings.Where(each => !sources.Any(source =>
            !source.Path.EndsWith(Path.DirectorySeparatorChar + each.Section.Name + ".cs", StringComparison.Ordinal)
            && (source.Text.Contains(each.Section.Name, StringComparison.Ordinal) || source.Text.Contains(each.Owner.Name, StringComparison.Ordinal))
            && Regex.IsMatch(source.Text, ReadThrough.TryGetValue(each.Name, out var member) ? $@"\.{member}\(" : $@"\.{each.Property.Name}\b")));

        Assert.Multiple(() =>
        {
            Assert.That(settings.Select(each => each.Section).Distinct().Count(), Is.GreaterThanOrEqualTo(5), "the sections the scan found");
            Assert.That(sources, Has.Count.GreaterThan(100), "the sources the scan read");
            Assert.That(noScreen.Select(each => each.Name), Is.Empty, "settings with no screen, and not host-only");
            Assert.That(unread.Select(each => each.Name), Is.Empty, "settings nothing outside the settings module reads");
            Assert.That(ReadThrough.Keys, Is.SubsetOf(settings.Select(each => each.Name)), "every setting read through a member is one");
        });
    }

    // Each setting of every section: its section, the record that declares it (the section or an object nested in it),
    // the property and its name.
    private static IEnumerable<(Type Section, Type Owner, PropertyInfo Property, string Name)> Settings()
    {
        var sections = typeof(ISettingsSection).Assembly.GetTypes()
            .Where(type => type is { IsClass: true, IsAbstract: false } && type.IsAssignableTo(typeof(ISettingsSection)));
        foreach (var section in sections)
        {
            foreach (var property in section.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                yield return (section, section, property, $"{section.Name}.{property.Name}");
                var type = Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType;
                if (type.IsClass && type != typeof(string) && type.Namespace?.StartsWith("Jiron.", StringComparison.Ordinal) == true)
                {
                    foreach (var nested in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
                    {
                        yield return (section, type, nested, $"{section.Name}.{property.Name}.{nested.Name}");
                    }
                }
            }
        }
    }

    // The product's C# sources outside the settings module's machinery and endpoints.
    private static IEnumerable<(string Path, string Text)> Sources()
    {
        var separator = Path.DirectorySeparatorChar;
        var settingsFolder = Path.Combine(Src, "Jiron.Umbraco.FormBuilder.Core", "Settings") + separator;
        var endpoints = Path.Combine(Src, "Jiron.Umbraco.FormBuilder.Web", "ManagementApi", "Settings") + separator;
        foreach (var file in Directory.EnumerateFiles(Src, "*.cs", SearchOption.AllDirectories))
        {
            var machinery = file.StartsWith(settingsFolder, StringComparison.Ordinal) && Machinery.Contains(Path.GetFileName(file), StringComparer.Ordinal);
            if (machinery || file.StartsWith(endpoints, StringComparison.Ordinal)
                || file.Contains($"{separator}obj{separator}", StringComparison.Ordinal) || file.Contains($"{separator}bin{separator}", StringComparison.Ordinal))
            {
                continue;
            }

            yield return (file, File.ReadAllText(file));
        }
    }
}
