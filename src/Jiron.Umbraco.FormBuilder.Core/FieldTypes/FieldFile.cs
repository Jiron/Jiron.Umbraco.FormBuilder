namespace Jiron.Umbraco.FormBuilder.Core.FieldTypes;

/// <summary>
/// A file a visitor uploaded, as a field's check sees it (ADR-0062): its name as the visitor's device gave it and its
/// size in bytes, which the pipeline takes from the upload store for each upload token (6.1).
/// </summary>
public sealed record FieldFile
{
    /// <summary>A file.</summary>
    /// <param name="name">The file's name, such as <c>plan.pdf</c>.</param>
    /// <param name="size">Its size in bytes.</param>
    /// <exception cref="ArgumentException">The name is null or empty, or the size is negative.</exception>
    public FieldFile(string name, long size)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        ArgumentOutOfRangeException.ThrowIfNegative(size);
        Name = name;
        Size = size;
    }

    /// <summary>The file's name, such as <c>plan.pdf</c>.</summary>
    public string Name { get; }

    /// <summary>Its size in bytes.</summary>
    public long Size { get; }
}
