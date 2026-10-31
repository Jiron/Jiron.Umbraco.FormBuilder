using Umbraco.Cms.Core.PropertyEditors;

namespace Jiron.Umbraco.FormBuilder.Web.Picker;

/// <summary>
/// The form picker's settings (ADR-0129): one form or several, the folders and forms it may pick, and the theme and the
/// page after a send for every form it places. Each value is kept as the backoffice wrote it and read leniently, so a
/// value that is not a key counts as none: the configuration editor refuses such values when the data type is saved.
/// </summary>
public sealed class FormPickerConfiguration
{
    /// <summary>Whether the picker takes several forms; one by default.</summary>
    [ConfigurationField("multiple")]
    public bool Multiple { get; set; }

    /// <summary>The keys of the folders whose forms, and those of the folders below them, the picker may pick.</summary>
    [ConfigurationField("allowedFolders")]
    public IReadOnlyList<string>? AllowedFolders { get; set; }

    /// <summary>The keys of forms the picker may pick.</summary>
    [ConfigurationField("allowedForms")]
    public IReadOnlyList<string>? AllowedForms { get; set; }

    /// <summary>The alias of the theme every form it places renders in, or none for each form's own.</summary>
    [ConfigurationField("theme")]
    public string? Theme { get; set; }

    /// <summary>The key of the page a visitor goes to after a send, in place of the form's outcome, or none.</summary>
    [ConfigurationField("redirect")]
    public string? Redirect { get; set; }

    /// <summary>The allowed folders' keys that are keys.</summary>
    public IReadOnlySet<Guid> Folders => Keys(AllowedFolders);

    /// <summary>The allowed forms' keys that are keys.</summary>
    public IReadOnlySet<Guid> Forms => Keys(AllowedForms);

    /// <summary>The theme's alias, or null when none is set.</summary>
    public string? ThemeAlias => string.IsNullOrWhiteSpace(Theme) ? null : Theme.Trim();

    /// <summary>The page after a send, or null when none is set.</summary>
    public Guid? RedirectKey => Guid.TryParse(Redirect, out var key) && key != Guid.Empty ? key : null;

    /// <summary>
    /// Whether the picker may pick a form in the given folders (its own first): when it limits nothing, when it names the
    /// form, or when it names one of the folders.
    /// </summary>
    /// <param name="form">The form's key.</param>
    /// <param name="folders">The keys of the form's folder and of the folders above it.</param>
    public bool Allows(Guid form, IEnumerable<Guid> folders)
    {
        ArgumentNullException.ThrowIfNull(folders);
        var allowedFolders = Folders;
        var allowedForms = Forms;
        return (allowedFolders.Count == 0 && allowedForms.Count == 0) || allowedForms.Contains(form) || folders.Any(allowedFolders.Contains);
    }

    private static HashSet<Guid> Keys(IReadOnlyList<string>? texts) =>
        [.. (texts ?? []).Select(text => Guid.TryParse(text, out var key) ? key : Guid.Empty).Where(key => key != Guid.Empty)];
}
