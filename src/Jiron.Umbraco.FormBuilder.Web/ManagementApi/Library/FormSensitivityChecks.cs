using Jiron.Umbraco.FormBuilder.Core.Access;
using Jiron.Umbraco.FormBuilder.Core.Definitions;
using Jiron.Umbraco.FormBuilder.Core.FieldTypes;
using Jiron.Umbraco.FormBuilder.Core.Forms;
using Jiron.Umbraco.FormBuilder.Core.Persistence.Repositories;

namespace Jiron.Umbraco.FormBuilder.Web.ManagementApi.Library;

/// <summary>
/// Whether a user may give a form a definition that makes a sensitive field no longer so (ADR-0150): only with
/// <c>viewSensitive</c>, or anyone who designs a form and sees its entries could read what they may not by publishing it so.
/// </summary>
/// <param name="forms">The forms, with their field catalogue.</param>
/// <param name="types">The field types.</param>
/// <param name="access">What a user holds on a form.</param>
internal sealed class FormSensitivityChecks(IFormRepository forms, FieldTypeRegistry types, IFormBuilderAccessService access)
{
    /// <summary>Whether the user lacks what it takes for this definition.</summary>
    /// <param name="user">The user.</param>
    /// <param name="form">The form.</param>
    /// <param name="definition">The definition that would go live.</param>
    /// <param name="cancellationToken">The request's cancellation.</param>
    public async Task<bool> RefusesAsync(AccessUser user, FormInfo form, FormDefinition definition, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(form);
        var held = await access.GetForFormAsync(user, FormAccessTarget.Of(form), cancellationToken);
        return (held & FormBuilderPermissions.ViewSensitive) == FormBuilderPermissions.None
            && FieldCatalogue.LowersSensitivity(FieldCatalogue.Of(definition, types), await StoredAsync(form.Key, cancellationToken));
    }

    private async Task<IReadOnlyList<CatalogueField>> StoredAsync(Guid form, CancellationToken cancellationToken) =>
        await forms.GetAsync(form, cancellationToken) is { } row ? await forms.ListFieldsAsync(row.Id, cancellationToken) : [];
}
