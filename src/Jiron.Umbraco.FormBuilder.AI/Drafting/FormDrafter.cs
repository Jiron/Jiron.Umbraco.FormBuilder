using Jiron.Umbraco.FormBuilder.AI.Chat;
using Jiron.Umbraco.FormBuilder.AI.Marks;
using Jiron.Umbraco.FormBuilder.Core.Forms;
using Jiron.Umbraco.FormBuilder.Core.Persistence.Repositories;

namespace Jiron.Umbraco.FormBuilder.AI.Drafting;

/// <summary>What an editor asks a draft from: a description of the form, or the text of a questionnaire.</summary>
/// <param name="Text">The description or the pasted questionnaire, at most <see cref="FormDrafter.MaxTextLength"/> characters.</param>
/// <param name="FolderKey">The folder the form goes into, or null for the root.</param>
/// <param name="UserKey">The editor, whom the form's version and audit entry name.</param>
internal sealed record DraftRequest(string Text, Guid? FolderKey, Guid? UserKey);

/// <summary>A drafted form: the new form, and how many of the AI's fields were left out.</summary>
/// <param name="Form">The new form, a draft that is not published.</param>
/// <param name="DraftVersion">The number of its first version, which the mark names.</param>
/// <param name="Left">How many of the reply's fields were left out.</param>
internal sealed record DraftResult(FormInfo Form, int DraftVersion, int Left);

/// <summary>
/// Draft a form with AI (ADR-0018): the editor's text goes to the AI with the draft's instructions and schema, the reply
/// is read strictly and built into a definition the reader and the save checks accept, and only then is the form created,
/// as a draft that is never published, its first version commented "Drafted by AI, please review", its audit entry
/// <c>form.drafted</c>, its notifications naming <see cref="FormSource.Drafted"/>, and its mark kept for the designer.
/// Nothing of any entry, member or file is read.
/// </summary>
internal sealed class FormDrafter(
    AIChat chat,
    DraftDefinitions definitions,
    FormCreation creation,
    IFolderRepository folders,
    IFormBuilderFormService forms,
    IFormBuilderFormVersionService versions,
    DraftMarks marks)
{
    /// <summary>The longest text an editor may send.</summary>
    public const int MaxTextLength = 20_000;

    /// <summary>The feature's alias in Umbraco.AI's audit log and usage.</summary>
    public const string Alias = "jfb-draft-form";

    /// <summary>The limits of a draft.</summary>
    public static DraftLimits Limits { get; } = new();

    /// <summary>The question a text makes.</summary>
    public static AIQuestion QuestionOf(string text) => new(
        Alias,
        "Jiron Form Builder: draft a form",
        AIPrompts.DraftInstructions(Limits),
        AIPrompts.Content("Draft a form from this:", AIPrompts.EditorTextTag, text),
        AIPrompts.DraftSchema(),
        8000);

    /// <summary>Drafts the form and creates it.</summary>
    /// <exception cref="AIUnavailableException">No profile, a provider's failure, or a reply that is no usable form.</exception>
    public async Task<DraftResult> DraftAsync(DraftRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var text = request.Text.Trim();
        if (text.Length is 0 or > MaxTextLength)
        {
            throw FormBuilderValidationException.For("text_invalid", "$.text", $"The text is empty or longer than {MaxTextLength} characters.");
        }

        var folder = await LibraryFolders.OutsideTrashAsync(folders, request.FolderKey, cancellationToken);
        var reply = await chat.AskAsync(QuestionOf(text), cancellationToken);
        var draft = DraftReader.Read(AIReplies.ObjectOf(reply), Limits);
        var definition = await definitions.BuildAsync(draft, cancellationToken);
        var name = draft.Name;
        var form = await creation.CreateAsync(
            definition,
            name,
            alias: null,
            FormAliases.FromName(name),
            folder,
            request.UserKey,
            new FormOrigin(FormSource.Drafted, null, FormBuilderAI.DraftedAction, details => details.Add("left", draft.Left)),
            cancellationToken);
        var version = (await forms.GetDraftAsync(form.Key, cancellationToken))?.DraftVersion ?? 1;
        await versions.CommentVersionAsync(form.Key, version, FormBuilderAI.DraftedComment, request.UserKey, cancellationToken);
        await marks.MarkAsync(form.Key, version, cancellationToken);
        return new DraftResult(form, version, draft.Left);
    }
}
