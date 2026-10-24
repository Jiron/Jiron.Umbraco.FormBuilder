using System.Net;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace Jiron.Umbraco.FormBuilder.Tests.Unit.Configuration;

/// <summary>
/// The XML documentation an assembly was built with, by member id (<c>T:</c>, <c>P:</c>), for the files generated from
/// the code: the appsettings schema now, the configuration reference later (17.x).
/// </summary>
internal sealed partial class XmlDocs
{
    private readonly Dictionary<string, XElement> _members;

    private XmlDocs(Dictionary<string, XElement> members) => _members = members;

    /// <summary>Reads the documentation file next to <paramref name="assembly"/>.</summary>
    public static XmlDocs Of(Assembly assembly)
    {
        ArgumentNullException.ThrowIfNull(assembly);
        var path = Path.ChangeExtension(assembly.Location, ".xml");
        var members = XDocument.Load(path).Descendants("member").ToDictionary(member => member.Attribute("name")!.Value, StringComparer.Ordinal);
        return new XmlDocs(members);
    }

    /// <summary>The documentation id of a type: <c>T:</c> and its full name, nested types joined by a dot.</summary>
    public static string Id(Type type) => $"T:{Name(type)}";

    /// <summary>The documentation id of a property.</summary>
    public static string Id(PropertyInfo property) => $"P:{Name(property.DeclaringType!)}.{property.Name}";

    /// <summary>
    /// A member's summary as plain text on one line: a reference becomes the name it refers to, code its text. Null
    /// when the member has none.
    /// </summary>
    public string? Summary(string id) =>
        _members.TryGetValue(id, out var member) && member.Element("summary") is { } summary
            ? Whitespace().Replace(WebUtility.HtmlDecode(string.Concat(summary.Nodes().Select(Text))), " ").Trim()
            : null;

    private static string Text(XNode node) => node switch
    {
        XElement { Name.LocalName: "see" } see => (see.Attribute("cref")?.Value ?? see.Attribute("langword")?.Value ?? string.Empty).Split('.', ':').Last(),
        XElement element => element.Value,
        _ => node.ToString(),
    };

    private static string Name(Type type) => type.FullName!.Replace('+', '.');

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();
}
