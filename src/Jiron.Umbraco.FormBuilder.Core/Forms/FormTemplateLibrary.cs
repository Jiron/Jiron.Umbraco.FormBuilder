using System.Text.Json;
using Jiron.Umbraco.FormBuilder.Core.Audit;
using Jiron.Umbraco.FormBuilder.Core.Definitions;
using Jiron.Umbraco.FormBuilder.Core.Definitions.Exchange;
using Jiron.Umbraco.FormBuilder.Core.Definitions.Serialization;
using Jiron.Umbraco.FormBuilder.Core.Persistence.Repositories;

namespace Jiron.Umbraco.FormBuilder.Core.Forms;

/// <summary>A template saved, or the problems that refused it, with how many rules of a group pointed outside it.</summary>
/// <param name="Template">The template, or null when problems refused it.</param>
/// <param name="Problems">What refused it.</param>
/// <param name="RulesChanged">The rules of a group that lost a condition naming an element outside it, or went.</param>
internal sealed record FormTemplateResult(StoredFormTemplate? Template, IReadOnlyList<DefinitionProblem> Problems, int RulesChanged = 0);

/// <summary>
/// The templates users save (ADR-0217): a form's stored draft as a form template, and a group as the canvas shows it as a
/// group template, kept as a definition of one page holding it, with every condition that names an element outside the
/// group left out; a template renamed or deleted. Each change is audited.
/// </summary>
internal sealed class FormTemplateLibrary(
    IFormTemplateRepository templates,
    IFormRepository forms,
    FormDocuments documents,
    FormDefinitionSerializer serializer,
    IAuditRepository audit)
{
    /// <summary>The audit action of a template saved.</summary>
    public const string SavedAction = "template.saved";

    /// <summary>The audit action of a template renamed.</summary>
    public const string RenamedAction = "template.renamed";

    /// <summary>The audit action of a template deleted.</summary>
    public const string DeletedAction = "template.deleted";

    private const string EntityType = "template";

    private static readonly string GroupPath = JsonPaths.Property(JsonPaths.Root, "group");

    /// <summary>Saves a form's stored draft as a form template; null when the form or its draft does not exist.</summary>
    public async Task<FormTemplateResult?> SaveFormAsync(Guid formKey, string name, string? description, Guid? userKey, CancellationToken cancellationToken = default)
    {
        if (Check(name, description) is { Count: > 0 } problems)
        {
            return new FormTemplateResult(null, problems);
        }

        if (await forms.GetAsync(formKey, cancellationToken) is not { } form || await documents.DraftOfAsync(form, cancellationToken) is not { } draft)
        {
            return null;
        }

        var saved = await CreateAsync(FormTemplateKind.Form, documents.Readable(draft), name, description, userKey, form.Key, cancellationToken);
        return new FormTemplateResult(saved, []);
    }

    /// <summary>
    /// Saves a group, its JSON as a definition holds it, as a group template; its problems at <c>$.group</c> refuse it. A
    /// condition that names an element outside the group is left out, a rule without conditions goes, and a list whose
    /// parent is outside keeps its options without the parent.
    /// </summary>
    public async Task<FormTemplateResult> SaveGroupAsync(JsonElement group, string name, string? description, Guid? userKey, CancellationToken cancellationToken = default)
    {
        if (Check(name, description) is { Count: > 0 } problems)
        {
            return new FormTemplateResult(null, problems);
        }

        var json = $$"""{"schemaVersion":1,"key":"{{Guid.NewGuid()}}","pages":[{"key":"{{Guid.NewGuid()}}","groups":[{{group.GetRawText()}}]}]}""";
        var read = serializer.Read(json);
        if (read.Definition is not { Pages: [{ Groups.Count: 1 }] } definition)
        {
            var groupAt = JsonPaths.Index(JsonPaths.Property(JsonPaths.Index(JsonPaths.Property(JsonPaths.Root, "pages"), 0), "groups"), 0);
            return new FormTemplateResult(null, [.. read.Problems.Select(problem => problem with { Path = GroupPath + problem.Path[Math.Min(problem.Path.Length, groupAt.Length)..] })]);
        }

        var (within, changed) = GroupTemplates.WithinItself(definition);
        var saved = await CreateAsync(FormTemplateKind.Group, within, name, description, userKey, null, cancellationToken);
        return new FormTemplateResult(saved, [], changed);
    }

    /// <summary>Renames a saved template; null when it does not exist.</summary>
    public async Task<FormTemplateResult?> RenameAsync(Guid key, string name, string? description, Guid? userKey, CancellationToken cancellationToken = default)
    {
        if (Check(name, description) is { Count: > 0 } problems)
        {
            return new FormTemplateResult(null, problems);
        }

        if (await templates.GetAsync(key, cancellationToken) is not { } template)
        {
            return null;
        }

        var renamed = await templates.UpdateAsync(new FormTemplateUpdate(template.Id, name.Trim(), Description(description), template.Category, template.Definition, userKey), cancellationToken);
        if (renamed is null)
        {
            return null;
        }

        await AuditAsync(RenamedAction, renamed, userKey, null);
        return new FormTemplateResult(renamed, []);
    }

    /// <summary>Deletes a saved template; false when it does not exist.</summary>
    public async Task<bool> DeleteAsync(Guid key, Guid? userKey, CancellationToken cancellationToken = default)
    {
        if (await templates.GetAsync(key, cancellationToken) is not { } template || !await templates.DeleteAsync(template.Id, cancellationToken))
        {
            return false;
        }

        await AuditAsync(DeletedAction, template, userKey, null);
        return true;
    }

    // A name of 1 to the column's length, and a description that fits its column.
    private static List<DefinitionProblem> Check(string name, string? description)
    {
        var problems = new List<DefinitionProblem>();
        var trimmed = name?.Trim() ?? string.Empty;
        if (trimmed.Length == 0)
        {
            problems.Add(new DefinitionProblem(DefinitionErrors.MissingProperty, "$.name", "A template needs a name."));
        }
        else if (trimmed.Length > FormTemplateRepository.MaxNameLength)
        {
            problems.Add(new DefinitionProblem(DefinitionErrors.LimitExceeded, "$.name", $"A name has at most {FormTemplateRepository.MaxNameLength} characters."));
        }

        if (Description(description) is { Length: > FormTemplateRepository.MaxDescriptionLength })
        {
            problems.Add(new DefinitionProblem(DefinitionErrors.LimitExceeded, "$.description", $"A description has at most {FormTemplateRepository.MaxDescriptionLength} characters."));
        }

        return problems;
    }

    private static string? Description(string? description) => string.IsNullOrWhiteSpace(description) ? null : description.Trim();

    private async Task<StoredFormTemplate> CreateAsync(FormTemplateKind kind, FormDefinition definition, string name, string? description, Guid? userKey, Guid? formKey, CancellationToken cancellationToken)
    {
        documents.CheckSaveable(definition);
        var created = await templates.CreateAsync(new NewFormTemplate(Guid.NewGuid(), kind, name.Trim(), FormDefinitionSerializer.WriteString(definition), Description(description), UserKey: userKey), cancellationToken);
        await AuditAsync(SavedAction, created, userKey, formKey);
        return created;
    }

    private Task<StoredAuditEntry> AuditAsync(string action, StoredFormTemplate template, Guid? userKey, Guid? formKey)
    {
        var details = new AuditDetails().Add("kind", template.Kind);
        if (formKey is { } form)
        {
            details.Add("form", form);
        }

        return audit.AddAsync(new NewAuditEntry(action, EntityType, template.Key, userKey, details), CancellationToken.None);
    }
}

/// <summary>A group as a template holds it (ADR-0217): only what points inside it.</summary>
internal static class GroupTemplates
{
    /// <summary>
    /// The definition of one page holding the group, with every condition naming an element outside the group left out, a
    /// rule left without conditions taken out, and a list whose parent is outside without its parent; and how many rules
    /// changed or went.
    /// </summary>
    public static (FormDefinition Definition, int RulesChanged) WithinItself(FormDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        var own = DefinitionKeys.Of(definition).ToHashSet();
        var changed = 0;
        var page = definition.Pages[0];
        var group = page.Groups[0];
        var within = group with { Rules = Rules(group.Rules), Fields = Fields(group.Fields) };
        return (definition with { Pages = [page with { Groups = [within] }] }, changed);

        IReadOnlyList<FormField> Fields(IReadOnlyList<FormField> fields) =>
            [.. fields.Select(field => field with { Rules = Rules(field.Rules), Options = Options(field.Options), Fields = Fields(field.Fields) })];

        FieldOptions? Options(FieldOptions? options) =>
            options is { Parent: { } parent } && !own.Contains(parent)
                ? options with { Parent = null, Items = [.. options.Items.Select(item => item with { ParentValues = [] })] }
                : options;

        IReadOnlyList<FormRule> Rules(IReadOnlyList<FormRule> rules)
        {
            var kept = new List<FormRule>();
            foreach (var rule in rules)
            {
                var conditions = rule.Conditions.Select(Inside).OfType<RuleItem>().ToList();
                if (conditions.Count != rule.Conditions.Count || !conditions.SequenceEqual(rule.Conditions))
                {
                    changed++;
                }

                if (conditions.Count > 0)
                {
                    kept.Add(rule with { Conditions = conditions });
                }
            }

            return kept;
        }

        RuleItem? Inside(RuleItem item) => item switch
        {
            RuleCondition { Field: { } field } when !own.Contains(field) => null,
            RuleCondition { Value: ConditionOperand.Field { Key: var other } } when !own.Contains(other) => null,
            RuleGroup nested => nested.Conditions.Select(Inside).OfType<RuleItem>().ToList() is { Count: > 0 } inner ? nested with { Conditions = inner } : null,
            _ => item,
        };
    }
}
