using Jiron.Umbraco.FormBuilder.Core.Audit;
using Jiron.Umbraco.FormBuilder.Core.Persistence.Repositories;

namespace Jiron.Umbraco.FormBuilder.Core.Forms;

/// <summary>
/// How a change to a form is named (ADR-0053): in the audit log, by the form services and the jobs that change forms,
/// and in the error log when a handler of its notification fails.
/// </summary>
internal static class FormAudit
{
    /// <summary>The audit entity type of a form; the item is the form's key.</summary>
    public const string EntityType = "form";

    /// <summary>The audit entry of a change to a form.</summary>
    public static NewAuditEntry Entry(string action, StoredForm form, Guid? userKey, AuditDetails details)
    {
        ArgumentNullException.ThrowIfNull(form);
        return new NewAuditEntry(action, EntityType, form.Key, userKey, details);
    }

    /// <summary>What a change names when a handler of its notification fails: the form.</summary>
    public static string Subject(Guid formKey) => $"the form {formKey}";
}
