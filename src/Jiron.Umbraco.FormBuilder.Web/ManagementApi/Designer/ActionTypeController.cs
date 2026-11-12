using System.Text.Json;
using Asp.Versioning;
using Jiron.Umbraco.FormBuilder.Core.Access;
using Jiron.Umbraco.FormBuilder.Core.Actions;
using Jiron.Umbraco.FormBuilder.Core.Definitions;
using Jiron.Umbraco.FormBuilder.Core.FieldTypes;
using Jiron.Umbraco.FormBuilder.Core.Forms;
using Jiron.Umbraco.FormBuilder.Web.ManagementApi.Access;
using Jiron.Umbraco.FormBuilder.Web.ManagementApi.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Umbraco.Cms.Core.Services;
using Umbraco.Cms.Web.Common.Routing;

namespace Jiron.Umbraco.FormBuilder.Web.ManagementApi.Designer;

/// <summary>
/// The site's action types for the designer (ADR-0158), each with its settings schema, the host's locks of its settings
/// and the site's items its settings offer (ADR-0192). Any user of the Forms section may ask: the registry and the locks
/// describe the site's code and configuration, and the items its structure, the same for everyone.
/// </summary>
[ApiVersion("1.0")]
[BackOfficeRoute(FormBuilderManagementApi.Route + "/action-type")]
[ApiExplorerSettings(GroupName = "Action type")]
internal sealed class ActionTypeController(ActionTypeRegistry registry, ActionSettingLocks locks) : FormBuilderManagementApiController
{
    /// <summary>Every action type of the site, in the designer's order.</summary>
    /// <param name="cancellationToken">The request's cancellation.</param>
    [HttpGet]
    [MapToApiVersion("1.0")]
    [FormBuilderSection]
    [ProducesResponseType(typeof(ActionTypesResponseModel), StatusCodes.Status200OK)]
    public async Task<IActionResult> List(CancellationToken cancellationToken = default)
    {
        var types = new List<ActionTypeResponseModel>();
        foreach (var type in registry.Types)
        {
            types.Add(ActionTypeResponseModel.Of(type, locks.Of(type.Declaration.Alias), await type.SiteChoicesAsync(cancellationToken)));
        }

        return Ok(new ActionTypesResponseModel { Types = types });
    }
}

/// <summary>
/// A document type a content action can create, with the properties a form can set and what each takes (ADR-0192). Any
/// user of the Forms section may ask, as for the action types: it describes the site's structure, which Umbraco shows
/// only to users of its Settings section.
/// </summary>
[ApiVersion("1.0")]
[BackOfficeRoute(FormBuilderManagementApi.Route + "/action-type/content/document-type/{alias}")]
[ApiExplorerSettings(GroupName = "Action type")]
internal sealed class ContentDocumentTypeController(ContentItemTypes types) : FormBuilderManagementApiController
{
    /// <summary>A document type a content item may have, with its properties a form can set.</summary>
    /// <param name="alias">The document type's alias.</param>
    [HttpGet]
    [MapToApiVersion("1.0")]
    [FormBuilderSection]
    [ProducesResponseType(typeof(ContentDocumentTypeResponseModel), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public IActionResult Get(string alias) =>
        types.Find(alias) is { } type ? Ok(ContentDocumentTypeResponseModel.Of(type)) : FormBuilderProblems.DocumentTypeNotFound();
}

/// <summary>A document type a content action can create (ADR-0192).</summary>
internal sealed record ContentDocumentTypeResponseModel
{
    /// <summary>The type's alias, which the action names.</summary>
    public required string Alias { get; init; }

    /// <summary>The type's name.</summary>
    public required string Name { get; init; }

    /// <summary>The properties a form can set, its compositions' included, in the order its groups show them.</summary>
    public required IReadOnlyList<ContentPropertyResponseModel> Properties { get; init; }

    /// <summary>The model of a type.</summary>
    public static ContentDocumentTypeResponseModel Of(ContentItemType type)
    {
        ArgumentNullException.ThrowIfNull(type);
        return new ContentDocumentTypeResponseModel
        {
            Alias = type.Alias,
            Name = type.Name,
            Properties =
            [
                .. type.Properties.Select(property => new ContentPropertyResponseModel
                {
                    Alias = property.Alias,
                    Name = property.Name,
                    Group = property.Group,
                    FieldTypes = property.FieldTypes,
                    Value = property.Value,
                }),
            ],
        };
    }
}

/// <summary>A property a content action can set (ADR-0192).</summary>
internal sealed record ContentPropertyResponseModel
{
    /// <summary>The property's alias, which the mapping names.</summary>
    public required string Alias { get; init; }

    /// <summary>The property's name, as Umbraco shows it.</summary>
    public required string Name { get; init; }

    /// <summary>The name of the group it sits in; null for none.</summary>
    public string? Group { get; init; }

    /// <summary>The field types whose answer it can hold, in the palette's order.</summary>
    public required IReadOnlyList<string> FieldTypes { get; init; }

    /// <summary>
    /// What a value written for it must be: <c>text</c>, which may hold merge tags, <c>wholeNumber</c>, <c>decimal</c>,
    /// <c>date</c> or <c>boolean</c>; null where it takes only a field's answer.
    /// </summary>
    public string? Value { get; init; }
}

/// <summary>
/// A test run of a draft's action against a sample entry (section 4.4, ADR-0158): nothing is stored and no run changes. It
/// needs <c>manageActions</c> on the form, and a type that reaches what the action is set up to reach runs only after a
/// confirmation. The draft is the one the request carries, as the designer holds it, saved or not, or the stored one
/// (ADR-0190).
/// </summary>
[ApiVersion("1.0")]
[BackOfficeRoute(FormBuilderManagementApi.Route + "/form/{key:guid}/action/{actionKey:guid}/test")]
[ApiExplorerSettings(GroupName = "Action type")]
internal sealed class ActionTestController(ActionTestRuns tests, FormDefinitionSerializer serializer, IUserService users, IFormBuilderFormService forms, Library.ActionGrantChecks grants, TestLimits limits) : FormBuilderManagementApiController
{
    /// <summary>Runs an action of the form's draft against a sample entry.</summary>
    /// <param name="key">The form.</param>
    /// <param name="actionKey">The action in its draft.</param>
    /// <param name="request">Whether the user confirmed it, and the draft as the designer holds it.</param>
    /// <param name="cancellationToken">The request's cancellation.</param>
    [HttpPost]
    [MapToApiVersion("1.0")]
    [FormBuilderPermission(FormBuilderPermissions.ManageActions, On = FormBuilderAccessResourceKind.Form, Key = "key")]
    [ProducesResponseType(typeof(ActionTestResponseModel), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> Test(Guid key, Guid actionKey, ActionTestRequestModel? request = null, CancellationToken cancellationToken = default)
    {
        if (limits.Refusal(RequestAccess.User.Key, Response) is { } limited)
        {
            return limited;
        }

        FormDefinition? held = null;
        if (request?.Definition is { } sent)
        {
            var read = serializer.Read(sent.GetRawText());
            held = read.Definition ?? throw new FormBuilderValidationException(read.Problems);

            // A test of an action that is not as it was saved, and names a secret, is one only who manages settings runs: it
            // would send the secret where the unsaved action says (16.1's review, ADR-0235).
            var tested = held.Actions.FirstOrDefault(action => action.Key == actionKey);
            var saved = (await forms.GetDraftAsync(key, cancellationToken))?.Definition?.Actions.FirstOrDefault(action => action.Key == actionKey);
            if (tested is not null && tested != saved && await grants.SecretRefusalAsync(RequestAccess.User, tested, cancellationToken) is { } refusal)
            {
                return refusal;
            }
        }

        var user = await users.GetAsync(RequestAccess.User.Key);
        var asking = new ActionTestUser(RequestAccess.User.Key, string.IsNullOrWhiteSpace(user?.Email) ? null : user.Email, user?.Name ?? string.Empty)
        {
            Culture = user?.Language,
        };
        var test = await tests.TestAsync(key, actionKey, asking, request?.Confirmed == true, held, cancellationToken);
        return test.Status switch
        {
            ActionTestStatus.Done => Ok(ActionTestResponseModel.Of(test.Result!, asking.Email)),
            ActionTestStatus.UnknownType => FormBuilderProblems.ActionTypeUnknown(),
            ActionTestStatus.NeedsConfirmation => FormBuilderProblems.ActionTestNeedsConfirmation(),
            ActionTestStatus.SettingsInvalid => FormBuilderProblems.ActionSettingsInvalid(),
            _ => FormBuilderProblems.ActionNotFound(),
        };
    }
}

/// <summary>The site's action types (ADR-0158).</summary>
internal sealed record ActionTypesResponseModel
{
    /// <summary>The types, in the designer's order.</summary>
    public required IReadOnlyList<ActionTypeResponseModel> Types { get; init; }
}

/// <summary>An action type: what the designer shows of it and the settings it edits.</summary>
internal sealed record ActionTypeResponseModel
{
    /// <summary>The alias actions name it by.</summary>
    public required string Alias { get; init; }

    /// <summary>Its name: plain text, or a <c>#</c> key of the backoffice's localisation.</summary>
    public required string Name { get; init; }

    /// <summary>One of Umbraco's backoffice icons.</summary>
    public required string Icon { get; init; }

    /// <summary>What a test run of it reaches: <c>dryRun</c>, <c>toCurrentUser</c> or <c>afterConfirmation</c>.</summary>
    public required string Test { get; init; }

    /// <summary>Its settings, in the order they are shown, each with the host's lock of it.</summary>
    public required IReadOnlyList<FieldSettingResponseModel> Settings { get; init; }

    /// <summary>The ready-made starts the designer offers, each with its name and its settings as JSON.</summary>
    public required IReadOnlyList<ActionPresetResponseModel> Presets { get; init; }

    /// <summary>The model of a type, with the host's locks of its settings and the site's items as the choices of the settings that offer them.</summary>
    public static ActionTypeResponseModel Of(
        IFormBuilderActionType type,
        IReadOnlyList<FieldSettingLock> locks,
        IReadOnlyDictionary<string, IReadOnlyList<FieldSettingChoice>>? choices = null)
    {
        ArgumentNullException.ThrowIfNull(type);
        var settings = FieldSettingResponseModel.Of(type.SettingsSchema.Settings, locks);
        return new ActionTypeResponseModel
        {
            Alias = type.Declaration.Alias,
            Name = type.Declaration.Name,
            Icon = type.Declaration.Icon,
            Test = type.Declaration.Test switch
            {
                ActionTestKind.ToCurrentUser => "toCurrentUser",
                ActionTestKind.AfterConfirmation => "afterConfirmation",
                _ => "dryRun",
            },
            Settings =
            [
                .. settings.Select(setting => choices is not null && choices.TryGetValue(setting.Name, out var items)
                    ? setting with { Choices = [.. items.Select(item => new FieldSettingChoiceResponseModel(item.Value, item.Label))] }
                    : setting),
            ],
            Presets = [.. type.Declaration.Presets.Select(preset => new ActionPresetResponseModel { Name = preset.Name, Settings = preset.Settings })],
        };
    }
}

/// <summary>A ready-made start for an action (ADR-0160).</summary>
internal sealed class ActionPresetResponseModel
{
    /// <summary>The name to show: plain text, or a <c>#</c> key of the backoffice's localisation.</summary>
    public required string Name { get; init; }

    /// <summary>The settings an action starts with, as a JSON object.</summary>
    public required string Settings { get; init; }
}

/// <summary>A request that may need the user's confirmation.</summary>
internal sealed record ActionConfirmationRequestModel
{
    /// <summary>Whether the user confirmed it, having been told what it reaches.</summary>
    public bool Confirmed { get; init; }
}

/// <summary>What a test run is asked for.</summary>
internal sealed record ActionTestRequestModel
{
    /// <summary>Whether the user confirmed it, having been told what it reaches.</summary>
    public bool Confirmed { get; init; }

    /// <summary>
    /// The draft's definition as the designer holds it, saved or not, read as a draft save reads it; null for the stored draft.
    /// </summary>
    public JsonElement? Definition { get; init; }
}

/// <summary>What a test run came to.</summary>
internal sealed record ActionTestResponseModel
{
    /// <summary>What it came to: <c>sent</c>, <c>dryRun</c> or <c>failed</c>.</summary>
    public required string Outcome { get; init; }

    /// <summary>What it came to as a code the backoffice words, such as <c>sent_to_you</c> or <c>mail_off</c>; null for none.</summary>
    public string? Code { get; init; }

    /// <summary>What the code's words take, such as the status a host answered; empty for none.</summary>
    public required IReadOnlyList<string> Args { get; init; }

    /// <summary>What to tell the editor, or null.</summary>
    public string? Message { get; init; }

    /// <summary>The address a test that reached the asking user went to; null for another.</summary>
    public string? Recipient { get; init; }

    /// <summary>What a run would do, one line each.</summary>
    public required IReadOnlyList<ActionTestLineResponseModel> Preview { get; init; }

    /// <summary>The model of a type's result, for the user who asked.</summary>
    public static ActionTestResponseModel Of(ActionTestResult result, string? address)
    {
        ArgumentNullException.ThrowIfNull(result);
        return new ActionTestResponseModel
        {
            Outcome = result.Outcome switch
            {
                ActionTestOutcome.Sent => "sent",
                ActionTestOutcome.Failed => "failed",
                _ => "dryRun",
            },
            Code = result.Code,
            Args = result.Args,
            Message = result.Message,
            Recipient = result.Code == ActionTestCodes.SentToYou ? address : null,
            Preview = [.. (result.Preview ?? []).Select(line => new ActionTestLineResponseModel { Text = line.Text, Code = line.Code, Args = line.Args })],
        };
    }
}

/// <summary>One line of what a run would do (ADR-0192).</summary>
internal sealed record ActionTestLineResponseModel
{
    /// <summary>The line as an English sentence, for a code the backoffice does not know.</summary>
    public required string Text { get; init; }

    /// <summary>What the line says as a code the backoffice words, such as <c>content_under</c>; null for none.</summary>
    public string? Code { get; init; }

    /// <summary>What the code's words take, such as a document type's name; empty for none.</summary>
    public required IReadOnlyList<string> Args { get; init; }
}
