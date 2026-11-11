using Jiron.Umbraco.FormBuilder.Core.Actions;
using Jiron.Umbraco.FormBuilder.Core.FieldTypes;
using Jiron.Umbraco.FormBuilder.Core.MergeTags;

namespace Jiron.Umbraco.FormBuilder.Samples.Crm;

/// <summary>
/// The CRM sample's action (docs/extending.md): after a submission it adds the visitor as a contact to a list of the
/// site's CRM, the contact's address and name written with merge tags, so an editor maps the form's fields without
/// code. It runs in the background with retries: an unavailable CRM is tried again, a contact without an address fails,
/// and an attempt cut off is uncertain, which is never sent twice by itself; the delivery key makes a repeated attempt
/// add the contact once. Its test shows the contact it would add and adds nothing. The CRM here is a stub
/// (<see cref="StubCrmClient"/>); a site's own <see cref="ICrmClient"/> calls its CRM.
/// </summary>
/// <param name="mergeTags">The merge tag service, which writes the contact's address and name.</param>
/// <param name="crm">The site's CRM.</param>
public sealed class CrmActionType(IFormBuilderMergeTagService mergeTags, ICrmClient crm) : FormBuilderActionTypeBase<CrmSettings>(new ActionTypeDeclaration
{
    Alias = TypeAlias,
    Name = "#jfbSamples_crm",
    Icon = "icon-users",
    Test = ActionTestKind.DryRun,
    AttemptTimeout = TimeSpan.FromSeconds(20),
})
{
    /// <summary>The type's alias.</summary>
    public const string TypeAlias = "sample.crm";

    /// <inheritdoc />
    protected override void CheckSettings(CrmSettings settings, ActionSettingsCheck check)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(check);
        if (string.IsNullOrWhiteSpace(settings.Email))
        {
            check.Invalid("email", "Say where the contact's email address comes from, such as {{ field.email }}.");
        }
    }

    /// <inheritdoc />
    protected override async Task<ActionResult> RunAsync(CrmSettings settings, ActionRunContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(context);
        var contact = await ContactAsync(settings, context.Scope, cancellationToken);
        if (contact.Email.Length == 0)
        {
            return ActionResult.Failed("no_email", "The entry gives the contact no email address.");
        }

        try
        {
            await crm.AddContactAsync(contact, context.DeliveryKey, cancellationToken);
            return ActionResult.Succeeded;
        }
        catch (CrmUnavailableException)
        {
            return ActionResult.TryAgain("crm_unavailable", "The CRM is not available; the contact is added on the next attempt.");
        }

        // An attempt cut off at its time is uncertain, as the CRM may have taken the contact; the runner says so itself.
    }

    /// <inheritdoc />
    protected override async Task<ActionTestResult> TestAsync(CrmSettings settings, ActionTestContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(context);
        var contact = await ContactAsync(settings, context.Scope, cancellationToken);
        return new ActionTestResult(
            ActionTestOutcome.DryRun,
            Preview: [new ActionTestLine($"{contact.Name} <{contact.Email}> to the list {contact.List}")])
        {
            Code = ActionTestCodes.DryRun,
        };
    }

    // The contact the entry gives: its address and name in plain text, as the merge tags write them.
    private async Task<CrmContact> ContactAsync(CrmSettings settings, MergeTagScope scope, CancellationToken cancellationToken)
    {
        var email = await mergeTags.RenderAsync(settings.Email, MergeTagPlace.Action, MergeTagContext.Text, scope, cancellationToken);
        var name = await mergeTags.RenderAsync(settings.Name ?? string.Empty, MergeTagPlace.Action, MergeTagContext.Text, scope, cancellationToken);
        return new CrmContact(email.Text.Trim(), name.Text.Trim(), settings.List);
    }
}

/// <summary>The settings of a CRM action.</summary>
public sealed class CrmSettings
{
    /// <summary>The contact's email address, with merge tags.</summary>
    [FormBuilderSetting("#jfbSamples_crmEmail", Order = 1, SupportsMergeTags = true)]
    public string Email { get; init; } = "{{ field.email }}";

    /// <summary>The contact's name, with merge tags; none for a contact without one.</summary>
    [FormBuilderSetting("#jfbSamples_crmName", Order = 2, SupportsMergeTags = true)]
    public string? Name { get; init; }

    /// <summary>The CRM's list the contact goes on.</summary>
    [FormBuilderSetting("#jfbSamples_crmList", Order = 3)]
    public CrmList List { get; init; } = CrmList.Newsletter;
}

/// <summary>The CRM's lists.</summary>
public enum CrmList
{
    /// <summary>Who reads the newsletter.</summary>
    [FormBuilderChoice("#jfbSamples_crmNewsletter")]
    Newsletter,

    /// <summary>Who may buy.</summary>
    [FormBuilderChoice("#jfbSamples_crmLeads")]
    Leads,

    /// <summary>Who bought.</summary>
    [FormBuilderChoice("#jfbSamples_crmCustomers")]
    Customers,
}
