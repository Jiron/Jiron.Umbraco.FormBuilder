# The AI add-on

`Jiron.Umbraco.FormBuilder.AI` is an optional package that helps editors in the designer through
[Umbraco.AI](https://docs.umbraco.com/umbraco-ai): it drafts a form from a description or a pasted questionnaire,
translates a form's texts into another of the site's languages, and suggests help texts and clearer wording for a
field. Everything it writes is a draft for an editor to review; nothing is published, or even saved, until an editor
saves it. Nothing a visitor enters ever reaches an AI. The decisions behind it are in
[ADR-0018](decisions/0018-the-ai-add-on.md).

## Setting it up

1. Install `Jiron.Umbraco.FormBuilder.AI` beside `Jiron.Umbraco.FormBuilder`. It brings Umbraco.AI (17.5.2 or later
   in the 17 line), whose tables Umbraco.AI creates as the site starts.
2. Install the Umbraco.AI provider package for the AI service you use, and in Umbraco.AI's own screens create a
   connection with your key and a chat profile on it. Keys stay in Umbraco.AI's configuration; the add-on never sees
   or stores one.
3. In the Form Builder section, open **Settings › AI** (users with the `manageSettings` permission) and choose the chat
   profile the add-on uses. Without a choice it uses Umbraco.AI's default chat profile.

The health check **AI add-on** (Settings › Health checks in Umbraco, and the section's Overview) says whether the
chosen profile exists and can chat, and whether its connection is active and answers a test, which costs no tokens.
Until the AI is set up, each feature says what is missing instead of offering a box that cannot work.

## The features

| Feature | Where | What it does |
| --- | --- | --- |
| **Draft a form with AI** | the library's create menu, wherever the user may create a form | Takes a description, or the pasted text of a questionnaire of up to 20,000 characters, and opens a new form in the designer with the new-form defaults. The form is marked "Drafted by AI, please review" until its first save, and its first version carries the same comment. Questions the AI drafted in a way the form cannot hold are left out and counted. |
| **Translate with AI** | the "…" of the row above the designer's form | Translates the draft's texts that lack the chosen language, or all of them, saving what is not saved yet first. The sidebar shows each text beside its translation, which the editor may change or untick, and **Apply** writes them into the draft as one step Undo takes back. Merge tags stay as they are, and texts taken from dictionary items are skipped. A run asks for at most 400 texts; Translate again for the rest. |
| **Suggest help text** | beside a field's help text in the inspector, while the designer shows the site's default language | Opens a list under the label, saving what is not saved yet first: a help text and up to three changes to the field's label, help or placeholder that make it clearer or easier to use with assistive technology, each with its reason. **Use** writes one into the draft as one step Undo takes back. |

Each feature needs the `design` permission on the form, or where a new form lands. Umbraco.AI's own usage and audit
settings apply to the add-on's requests as to any other, and a drafted form is in Form Builder's audit log as "Drafted
the form with AI".

## What the AI sees

- **Draft:** the editor's text, and the list of field types it may use.
- **Translate:** the default texts to be translated, and the target language's name.
- **Suggest:** the field's type, texts, options and whether it is required, the names of its page and group, and the
  labels of the fields near it.

Entries, answers, files, members and users never are, and a test checks every prompt for that. The editor's text and
the form's texts are sent as data inside marked blocks that the instructions tell the AI never to follow. The add-on
logs only the kind of a failure, never a prompt or an answer.

## When it cannot help

| Message | What to do |
| --- | --- |
| AI isn't set up yet | Choose a profile in Settings › AI, or set a default chat profile in Umbraco.AI. |
| The chosen profile no longer exists, or is not a chat profile | Choose another one in Settings › AI. |
| The AI didn't answer | The provider failed or timed out; try again, and check the health check. |
| The AI's answer could not be used, or its draft was not a form | Try again, or describe the form differently. |

## For developers

The add-on's two designer features are built on public extension points that a site's own code may use too:
`jfbDesignerAction` and `jfbFieldTextAction` ([extending.md](extending.md#actions-in-the-designer)). Its endpoints sit
in the management API below `/umbraco/jfb/management/api/v1/ai`, described by their own OpenAPI document `jfb-ai`
([docs/api/jfb-ai.json](api/jfb-ai.json)). Its tests run against a fake Umbraco.AI provider and never use a real key
([tests/README.md](../tests/README.md)).
