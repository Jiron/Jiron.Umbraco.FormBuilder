using System.Globalization;
using Jiron.Umbraco.FormBuilder.Core.Definitions;
using Jiron.Umbraco.FormBuilder.Core.Definitions.Projection;
using Jiron.Umbraco.FormBuilder.Core.Entries;
using Jiron.Umbraco.FormBuilder.Core.FieldTypes;
using Jiron.Umbraco.FormBuilder.Core.Forms;
using Jiron.Umbraco.FormBuilder.Core.MergeTags;
using Jiron.Umbraco.FormBuilder.Core.Settings;
using Jiron.Umbraco.FormBuilder.Core.Submissions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewEngines;
using Microsoft.AspNetCore.Mvc.ViewFeatures;

namespace Jiron.Umbraco.FormBuilder.Web.Rendering;

/// <summary>An outcome's preview (ADR-0187).</summary>
/// <param name="Html">The outcome's markup as visitors get it after a send; null for a redirect, which shows them no page.</param>
/// <param name="Address">Where visitors go for the sample entry, after the message or at once; null for nowhere.</param>
/// <param name="DelaySeconds">The seconds the message waits before the redirect, as the outcome sets them; 0 without one.</param>
/// <param name="Direction">Which way the culture's text runs, <c>ltr</c> or <c>rtl</c>, as the visitor's page sets it.</param>
internal sealed record OutcomePreview(string? Html, string? Address, int DelaySeconds, string Direction);

/// <summary>
/// The previews of a draft's outcomes in the backoffice (ADR-0187): the outcome rendered for the sample entry as the
/// server renders a real entry's (its message's merge tags, the one sanitiser, the redirect's address held to the publish
/// rule, a sensitive answer masked), then written by the site's own <c>Outcome</c> view in the default theme, with the
/// copy of the answers where the outcome offers it. A redirect that cannot be followed shows what visitors get then, the
/// site's thank-you message.
/// </summary>
/// <param name="samples">The sample entry.</param>
/// <param name="outcomes">What renders an outcome for an entry.</param>
/// <param name="types">The field types, for the visitor's form.</param>
/// <param name="visitorForms">The visitor's form in the culture, which the copy of the answers is listed by.</param>
/// <param name="models">The outcome's view model.</param>
/// <param name="engine">The site's view engine.</param>
/// <param name="tempData">The site's temp data.</param>
/// <param name="zones">The site's time zone, whose year the reference holds.</param>
internal sealed class OutcomePreviews(
    SampleEntries samples,
    SubmissionOutcomes outcomes,
    FieldTypeRegistry types,
    VisitorForms visitorForms,
    FormModels models,
    ICompositeViewEngine engine,
    ITempDataProvider tempData,
    SiteTimeZones zones)
{
    /// <summary>The preview of one of a definition's outcomes.</summary>
    /// <param name="action">The request the view writes for.</param>
    /// <param name="form">The form.</param>
    /// <param name="definition">The definition, such as the draft as the outcome's sidebar would make it.</param>
    /// <param name="outcome">The key of one of its outcomes.</param>
    /// <param name="culture">A language of the site.</param>
    /// <param name="cancellationToken">Cancels the reads.</param>
    /// <exception cref="InvalidOperationException">The default theme's Outcome view cannot be found.</exception>
    public async Task<OutcomePreview> RenderAsync(ActionContext action, FormInfo form, FormDefinition definition, Guid outcome, string culture, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(action);
        ArgumentNullException.ThrowIfNull(form);
        ArgumentNullException.ThrowIfNull(definition);
        var scope = await samples.ScopeAsync(definition, form.Name, form.Alias, culture, cancellationToken);
        var rendered = await outcomes.RenderAsync(outcome.ToString("D"), scope, cancellationToken: cancellationToken);
        var cultureInfo = CultureInfo.GetCultureInfo(culture);
        var direction = cultureInfo.TextInfo.IsRightToLeft ? "rtl" : "ltr";
        if (rendered is { Kind: OutcomeKind.Redirect, Url: { } address })
        {
            return new OutcomePreview(Html: null, address, DelaySeconds: 0, direction);
        }

        // The form as the site runs it, the setting locks applied; its visitor's form only for a copy of the answers.
        var locked = scope.Form;
        var shown = rendered is { Kind: OutcomeKind.Message, ShowAnswers: true }
            ? await visitorForms.ShowAsync(PublicProjection.Create(locked, types), scope, cancellationToken)
            : null;
        var zone = await zones.GetAsync(null, cancellationToken);
        var entry = scope.Entry!;
        var reference = EntryReferences.Format(locked.Settings.Submission.EntryReference, entry.Number, TimeZoneInfo.ConvertTime(entry.Submitted, zone).Year);
        var model = await models.PreviewAsync(new OutcomePreviewSource(rendered, shown is null ? null : new CopyShown(shown.Form, shown.Tagged, scope), scope, reference, form.Alias, culture), cancellationToken);
        var found = engine.GetView(null, model.Views.Outcome, isMainPage: false);
        if (!found.Success)
        {
            throw new InvalidOperationException($"The view '{model.Views.Outcome}' cannot be found.");
        }

        // Written in the culture previewed, as a page in it is: a site's own view may read the dictionary or format a date.
        var html = await RazorViewText.WriteAsync(found.View, action, model, tempData, cultureInfo);
        return new OutcomePreview(html.Trim(), rendered.Url, rendered.Url is null ? 0 : rendered.DelaySeconds, direction);
    }
}
