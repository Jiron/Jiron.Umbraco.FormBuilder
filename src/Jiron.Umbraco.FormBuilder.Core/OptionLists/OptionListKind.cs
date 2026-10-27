namespace Jiron.Umbraco.FormBuilder.Core.OptionLists;

/// <summary>
/// Where an option list's options come from (ADR-0073), stored and written as the camelCase name of its value
/// (<c>dataType</c>). A library list has one, and a field's source other than its own items or a library list is one.
/// </summary>
public enum OptionListKind
{
    /// <summary>Options an editor types or pastes (<c>manual</c>); a field's own options are of this kind too.</summary>
    Manual,

    /// <summary>Content pages (<c>content</c>).</summary>
    Content,

    /// <summary>The values of an Umbraco data type (<c>dataType</c>).</summary>
    DataType,

    /// <summary>A JSON document fetched from an allowed host (<c>url</c>).</summary>
    Url,

    /// <summary>A CSV or text file uploaded to a library list (<c>file</c>).</summary>
    File,

    /// <summary>The countries (<c>countries</c>).</summary>
    Countries,

    /// <summary>An option source registered in code (<c>code</c>), the standard lists among them.</summary>
    Code,
}
