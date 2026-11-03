using Jiron.Umbraco.FormBuilder.Core.Definitions;
using Jiron.Umbraco.FormBuilder.Core.Definitions.Exchange;
using Jiron.Umbraco.FormBuilder.Core.Persistence;
using Jiron.Umbraco.FormBuilder.Core.Persistence.Repositories;
using Microsoft.Extensions.Logging;

namespace Jiron.Umbraco.FormBuilder.Core.Forms;

/// <summary>A template of the gallery (ADR-0166): a built-in one or one saved on this site, of kind Form.</summary>
/// <param name="Key">Its key, which a form from it names.</param>
/// <param name="BuiltIn">Whether it ships with Jiron Form Builder.</param>
/// <param name="Alias">A built-in template's alias, by which the backoffice names and describes it in its languages; null for a saved one.</param>
/// <param name="Name">Its name: a built-in template's in English, the default text.</param>
/// <param name="Description">A saved template's description, or null.</param>
/// <param name="Definition">Its definition, for the preview.</param>
internal sealed record GalleryTemplate(Guid Key, bool BuiltIn, string? Alias, string Name, string? Description, FormDefinition Definition);

/// <summary>
/// The template gallery (ADR-0046, ADR-0166): the built-in templates in their order, then the site's saved form templates by
/// name. A saved template whose stored definition cannot be read is left out, with a warning in the log, so one damaged
/// row never empties the gallery.
/// </summary>
internal sealed partial class FormTemplateGallery(IFormTemplateRepository templates, FormDefinitionSerializer serializer, ILogger<FormTemplateGallery> logger)
{
    /// <summary>A page of the gallery, with the number of templates in it.</summary>
    public async Task<Page<GalleryTemplate>> ListAsync(PageRequest page, CancellationToken cancellationToken = default)
    {
        var builtIn = BuiltInFormTemplates.All.Select(BuiltIn).ToList();
        var items = builtIn.Skip((int)Math.Min(page.Skip, builtIn.Count)).Take(page.Take).ToList();
        var savedSkip = Math.Max(0, page.Skip - builtIn.Count);
        var saved = await templates.ListAsync(new FormTemplateFilter(FormTemplateKind.Form), new PageRequest(savedSkip, Math.Max(1, page.Take - items.Count)), cancellationToken);
        if (items.Count < page.Take)
        {
            items.AddRange(saved.Items.Select(Saved).OfType<GalleryTemplate>());
        }

        return new Page<GalleryTemplate>(items, builtIn.Count + saved.Total);
    }

    /// <summary>The template with this key, built-in or saved, or null.</summary>
    public async Task<GalleryTemplate?> FindAsync(Guid key, CancellationToken cancellationToken = default) =>
        BuiltInFormTemplates.Find(key) is { } builtIn
            ? BuiltIn(builtIn)
            : await templates.GetAsync(key, cancellationToken) is { Kind: FormTemplateKind.Form } saved ? Saved(saved) : null;

    private GalleryTemplate BuiltIn(BuiltInFormTemplate template)
    {
        var read = FormExportFormat.Read(template.Envelope, serializer);
        var envelope = read.Envelope ?? throw new InvalidOperationException($"The built-in template {template.Alias} does not read.");
        return new GalleryTemplate(template.Key, true, template.Alias, envelope.Name, null, envelope.Definition);
    }

    private GalleryTemplate? Saved(StoredFormTemplate template)
    {
        if (serializer.ReadStored(template.Definition).Definition is { } definition)
        {
            return new GalleryTemplate(template.Key, false, null, template.Name, template.Description, definition);
        }

        LogUnreadable(logger, template.Key);
        return null;
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "The saved template {TemplateKey} was left out of the gallery because its stored definition does not read")]
    private static partial void LogUnreadable(ILogger logger, Guid templateKey);
}
