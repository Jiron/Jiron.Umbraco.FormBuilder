using Jiron.Umbraco.FormBuilder.Core.Definitions;
using Jiron.Umbraco.FormBuilder.Core.Definitions.Serialization;
using Jiron.Umbraco.FormBuilder.Core.FieldTypes;
using Jiron.Umbraco.FormBuilder.Core.MergeTags;
using Jiron.Umbraco.FormBuilder.Logic;
using MergeTagEngine = Jiron.Umbraco.FormBuilder.Logic.MergeTags;

namespace Jiron.Umbraco.FormBuilder.Core.Forms;

/// <summary>
/// What an anonymous form cannot have (ADR-0231): its entries keep no member and its submissions run without one, so a
/// form for members, one entry per member and a field that takes a value from the member, which need the member or would
/// keep their values in the answers, are errors; a merge tag that reads the member renders empty there, a warning. Rules
/// may still look at whether a member is signed in, which keeps nothing.
/// </summary>
/// <param name="fieldTypes">The field types, whose settings schemas say which settings take tags.</param>
internal sealed class AnonymousFormCheck(FieldTypeRegistry fieldTypes) : IFormCheck
{
    /// <summary>An anonymous form for members (an error).</summary>
    public const string AnonymousForMembers = "anonymous_for_members";

    /// <summary>An anonymous form that takes one entry per member (an error).</summary>
    public const string AnonymousOnePerMember = "anonymous_one_per_member";

    /// <summary>A field of an anonymous form that takes a value from the member (an error).</summary>
    public const string AnonymousTakesMember = "anonymous_takes_member";

    /// <summary>A merge tag of an anonymous form that reads the member (a warning).</summary>
    public const string AnonymousMemberTag = "anonymous_member_tag";

    /// <inheritdoc />
    public ValueTask<IReadOnlyList<FormCheckIssue>> CheckAsync(FormDefinition definition, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(definition);
        if (!definition.Settings.Privacy.Anonymous)
        {
            return ValueTask.FromResult<IReadOnlyList<FormCheckIssue>>([]);
        }

        var issues = new List<FormCheckIssue>();
        var availability = JsonPaths.Property(JsonPaths.Property(JsonPaths.Root, "settings"), "availability");
        if (definition.Settings.Availability.Access != MemberAccess.Anyone)
        {
            issues.Add(new FormCheckIssue(FormCheckSeverity.Error, AnonymousForMembers, JsonPaths.Property(availability, "members"), "An anonymous form keeps no member, so it cannot be for members: it checks who sends it."));
        }

        if (definition.Settings.Availability.OnePerMember)
        {
            issues.Add(new FormCheckIssue(FormCheckSeverity.Error, AnonymousOnePerMember, JsonPaths.Property(availability, "onePerMember"), "An anonymous form keeps no member, so it cannot tell a member's second entry from their first."));
        }

        foreach (var (field, path) in DefinitionFields.Of(definition))
        {
            if (field.Prefill?.Member is not null)
            {
                issues.Add(new FormCheckIssue(FormCheckSeverity.Error, AnonymousTakesMember, JsonPaths.Property(JsonPaths.Property(path, "prefill"), "member"), "This field takes a value from the member, which an anonymous form would keep in the answer."));
            }
        }

        foreach (var template in FormTemplates.Of(definition, fieldTypes))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (ReadsTheMember(template.Text, template.Context))
            {
                issues.Add(new FormCheckIssue(FormCheckSeverity.Warning, AnonymousMemberTag, template.Path, "An anonymous form runs without the member, so this text's member tags stay empty."));
            }
        }

        return ValueTask.FromResult<IReadOnlyList<FormCheckIssue>>(issues);
    }

    // Whether a text holds a tag that reads the member: member, or anything below it.
    private static bool ReadsTheMember(string text, MergeTagContext context) =>
        MergeTagEngine.Parse(text, context).Parts
            .OfType<TagPart>()
            .Any(tag => tag.Path is { } path && (path == "member" || path.StartsWith("member.", StringComparison.Ordinal)));
}
