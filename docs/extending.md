# Extending Jiron Form Builder

Jiron Form Builder is built so that a site can add what it needs without changing the package: field types, actions,
option lists, exports, captchas, spam signals, merge tags, file scanners and stores, themes and views on the server;
handlers that hear what happens to forms and entries; services to read and write forms and entries from code; and
extension points in the backoffice and on headless sites. This guide shows each, in the order a site usually needs
them. The decisions behind it are in [ADR-0233](decisions/0233-the-extension-surface.md) and the ADRs it names.

Everything public is listed, member by member, in [`docs/api/`](api/): `Jiron.Umbraco.FormBuilder.Core.api.txt` and
`Jiron.Umbraco.FormBuilder.Web.api.txt`. A test fails whenever that surface changes, so a change to it is always a
reviewed diff, and a breaking one waits for the next major version with its deprecation (D21).

## Contents

1. [Registering your extensions](#1-registering-your-extensions)
2. [Types of your own](#2-types-of-your-own)
3. [Settings of your types](#3-settings-of-your-types)
4. [Notifications](#4-notifications)
5. [Services](#5-services)
6. [The website](#6-the-website)
7. [The backoffice](#7-the-backoffice)
8. [Headless sites](#8-headless-sites)

## 1. Registering your extensions

Register in a composer. `builder.JironFormBuilder()` gives the registries, each a collection your site can add to,
replace an entry of, or remove an entry from:

```csharp
using Jiron.Umbraco.FormBuilder.Core.Composing;
using Umbraco.Cms.Core.Composing;
using Umbraco.Cms.Core.DependencyInjection;

public sealed class SiteFormsComposer : IComposer
{
    public void Compose(IUmbracoBuilder builder)
    {
        builder.JironFormBuilder().FieldTypes().Append<ColourFieldType>();
        builder.JironFormBuilder().ActionTypes().Append<CrmActionType>();
        builder.JironFormBuilder().OptionSources().Append<ProductOptionSource>();
        builder.AddNotificationAsyncHandler<EntrySubmittingNotification, BlockedDomainsHandler>();
    }
}
```

| Registry | Holds |
| --- | --- |
| `FieldTypes()` | `IFormBuilderFieldType` |
| `ActionTypes()` | `IFormBuilderActionType` |
| `OptionSources()` | `IFormBuilderOptionSource` |
| `ExportFormats()` | `IFormBuilderExportFormat` |
| `CaptchaProviders()` | `IFormBuilderCaptchaProvider` |
| `SpamChecks()` | `IFormBuilderSpamCheck` |
| `MergeTagSources()`, `MergeTagFilters()` | `IFormBuilderMergeTagSource`, `IFormBuilderMergeTagFilter` |
| `FileScanners()` | `IFormBuilderFileScanner` |
| `Themes()` | `IFormBuilderTheme` |

The file store is one service, not a registry: `builder.Services.AddUnique<IFormBuilderFileStore, BlobFileStore>()`.

Start-up checks what you register and refuses a type it cannot use, naming it and why: two field types with one alias,
a setting whose editor draws nothing for its kind, a theme whose stylesheet is not local.

## 2. Types of your own

### Field types

A field type declares what it is (`FieldTypeDeclaration`: its alias, name, icon, palette group, how its answers are
stored, which messages and features it has), reads its settings from a class (section 3), and takes part in each step of
an answer's life. Derive from `FormBuilderFieldTypeBase<TSettings>` and override what your type needs:

| Override | When it runs |
| --- | --- |
| `Read` | the posted texts become the answer's values in their wire form |
| `Validate`, `ValidateRows` | the answer, or a repeatable group's rows, are checked; errors use the type's message kinds |
| `CheckUpload`, `UploadLimit` | a file's size and first bytes are checked before it is stored |
| `Display` | the answer is shown in mail, merge tags, exports and the entries screen |
| `CheckSettings` | a form is published: settings that cannot work together are refused, naming the setting |
| `AnswerLimits` | the longest answer and the most values the field takes |
| `CheckAvailabilityAsync` | the designer asks whether the type can be used now (a type that needs a key it lacks says so) |

Its view on the website is `Views/JironFormBuilder/<theme>/fields/<alias>.cshtml`, over the field's view model; the
markup contract (section 6) is what the runtime reads. On a headless site, `<jfb-form>` writes its control with the
function the site registers for it.

### Actions

An action type runs after a submission, in the background, with retries. Derive from
`FormBuilderActionTypeBase<TSettings>`: `RunAsync` returns `ActionResult.Succeeded`, `ActionResult.TryAgain(code)` for a
failure worth another attempt, `ActionResult.Failed(code)` for one that is not, or `ActionResult.Uncertain(code)` when
it cannot tell whether the receiver got it, which is never sent twice by itself. `TestAsync` answers the designer's
Test without sending anything real, and `MissingConfiguration` names what the host still has to configure. The run's
context holds the entry as merge tags see it; keep its answers out of your logs.

### Option sources

`IFormBuilderOptionSource` gives options from code by its `Name`, in the culture asked for and, for a cascade, for the
parent's answers. Options from a source are read again when a visitor's answer is checked, so a source answers the same
way for the page and for the server.

### Exports

`IFormBuilderExportFormat` writes an export's columns, tables and entries to a stream, reading entries page by page as it
asks for them. It names its content type and extension, and whether it takes the delimiter and header choices.

### Captchas, spam signals and file scanners

- `IFormBuilderCaptchaProvider` verifies a token for a form that chose it; a provider that fails or lost its keys sends
  the entry to Spam rather than turning the visitor away.
- `IFormBuilderSpamCheck` adds a score and a reason to an entry's spam score; one that throws or takes over 5 seconds
  counts for nothing.
- `IFormBuilderFileScanner` is asked about each uploaded file in the background; any scanner's quarantine decides, and
  one that fails or takes over 5 minutes leaves the file waiting.

### Merge tags

`IFormBuilderMergeTagSource` resolves paths under its `Name` (`{{ crm.segment }}`), in the places its `Places` allow, and
lists them for the designer's picker; `IsPerVisitor` says whether a value may differ between visitors, which keeps it
out of cached pages. `IFormBuilderMergeTagFilter` transforms a value (`{{ field.name | upper }}`).

### File stores

`IFormBuilderFileStore` stores uploaded files elsewhere: `SaveAsync(id, stream)` stores all of a file or nothing and
never replaces one, `OpenReadAsync(id)` gives a stream or null, `DeleteAsync(id)` removes it. Ids are random and never
URLs, and a reader never sees a file that is still being written.

### Themes and views

`IFormBuilderTheme` names a stylesheet, a class map onto the slots of `FormBuilderThemeSlots`, tokens for the
`--jfb-*` custom properties, a dark scheme and a view folder. Every theme shares one set of views; a site overrides a
view by placing its own at the same path below `Views/JironFormBuilder/<theme>/`, or below
`Views/JironFormBuilder/<theme>/_forms/<alias>/` for one form. Each view starts with its contract header
(`@* jfb-view: Field 1 *@`), which says the version of the markup contract it follows.

## 3. Settings of your types

A type's settings are a class whose properties carry `[FormBuilderSetting]`. The designer draws an editor for each
without any JavaScript, and the server checks every value against the schema before a form is saved or published:

```csharp
public sealed class ColourSettings
{
    [FormBuilderSetting("Colours", Description = "The colours a visitor chooses from.", Editor = "acme.swatches", Order = 1)]
    public List<string> Colours { get; init; } = ["#1f6feb", "#2da44e", "#cf222e"];

    [FormBuilderSetting("Show the colour's name", Order = 2)]
    public bool ShowNames { get; init; } = true;
}
```

`Editor` names the editor: by default the one of the property's kind (`boolean`, `number`, `localizedText`, ...). An
editor that no built-in one draws comes from a `jfbSettingEditor` manifest of your own, in the backoffice.

## 4. Notifications

Handlers are Umbraco's own: `INotificationAsyncHandler<T>`, registered with `builder.AddNotificationAsyncHandler<T,
THandler>()`. A notification ending in "-ing" comes before the change and can stop it; the other comes once it
committed, and a handler that fails then is logged while the change stands.

| Notifications | Namespace |
| --- | --- |
| `FormSaving`/`Saved`, `FormPublishing`/`Published`, `FormUnpublishing`/`Unpublished`, `FormRenaming`/`Renamed`, `FormMoving`/`Moved`, `FormVersionUpdating`/`Updated`, `FormTrashing`/`Trashed`, `FormRestoring`/`Restored`, `FormDeleting`/`Deleted` | `Core.Forms` |
| `FolderCreating`/`Created`, `FolderRenaming`/`Renamed`, `FolderMoving`/`Moved`, `FolderTrashing`/`Trashed`, `FolderRestoring`/`Restored`, `FolderDeleting`/`Deleted`, `TrashEmptying`/`Emptied` | `Core.Forms` |
| `EntrySubmitting`, `EntrySubmitted`, `EntryApproved`, `EntryRejected`, `EntryStateChanged`, `EntryDeleting`, `EntryDeleted`, `EntryActionFailed` | `Core.Entries` |
| `ActionRunning`, `ActionRunCompleted`, `ActionRunFailed` | `Core.Actions` |
| `ExportCompleted` | `Core.Exports` |
| `SettingsSaved` | `Core.Settings` |

Every name ends in `Notification` (`EntrySubmittingNotification`). The entry events of 8.4 also reach Umbraco's
webhooks while the integrations settings have them on ([webhooks](webhooks.md)); `EntryStateChanged` and
`EntryDeleting` are for code only.

### Refusing an entry in your own words

`EntrySubmittingNotification` comes once a submission passed every check of the form and its protection, before
anything is stored: for a visitor's entry (one going to Spam too, with `IsSpam`), the preview's test entry (`IsTest`)
and one your code creates (`IsFromCode`). A page's check and a saved draft do not raise it.

```csharp
using Jiron.Umbraco.FormBuilder.Core.Entries;
using Umbraco.Cms.Core.Events;

public sealed class BlockedDomainsHandler(IMemberDirectory directory) : INotificationAsyncHandler<EntrySubmittingNotification>
{
    public async Task HandleAsync(EntrySubmittingNotification notification, CancellationToken cancellationToken)
    {
        if (notification.FormAlias != "register" || notification.AnswerOf("email")?.Values.FirstOrDefault()?.Text is not { } email)
        {
            return;
        }

        if (await directory.IsRegisteredAsync(email, cancellationToken))
        {
            notification.AddError("email", notification.Culture.StartsWith("de", StringComparison.Ordinal)
                ? "Diese Adresse ist schon registriert."
                : "This address is registered already.");
        }
    }
}
```

- `AddError(field, message, row)` puts an error at a field (a repeatable group's child with its row);
  `AddFormError(message)`, or Umbraco's `CancelOperation(new EventMessage(category, message))`, puts one at the form.
- The visitor keeps every answer and reads your words where the form's own errors show, as you wrote them: below the
  field and in the error summary, a form's error first, linked to the first field. On the public API the answer is
  `422 validation_failed` with errors of the code `custom`, your words as `text`.
- Word errors in `notification.Culture`; a cancel without words shows "Check this answer" in the visitor's language.
- A handler that throws, or holds the visitor up longer than 5 seconds, is logged and has no word: the entry is stored.
  A site's failure never loses an entry.
- The answers are the visitor's: keep them out of your logs.

### Hearing of changes

- `EntryStateChangedNotification` tells a state change once it stands, with `From` and `State`: at once for a move
  without a user, after the undo window of a user's move, when an entry leaves Spam or is verified.
- `EntryDeletingNotification` comes before an entry is deleted for good, while your handler can still read it through
  `IFormBuilderEntryService.GetAsync`, for example to archive it. It cannot stop the delete: deletes keep the site's
  retention and privacy promises.

## 5. Services

Inject them; they are singletons and safe on every server of a cluster.

| Service | For |
| --- | --- |
| `IFormBuilderFormService` | forms: get, create, save a draft, publish, unpublish, rename, move, trash, restore, delete |
| `IFormBuilderFormVersionService` | versions: list, read, compare, restore, pin and comment |
| `IFormBuilderFormExchangeService` | duplicate, templates, import and export, copying actions between forms |
| `IFormBuilderFolderService` | the library's folders and its trash |
| `IFormBuilderEntryService` | entries from code: read, list, create, change state |
| `IFormBuilderEntryReader` | approved entries for a site's own pages |
| `IFormBuilderRenderer` (Web) | a form as HTML from code |
| `IFormBuilderMergeTagService` | render a template with merge tags for a scope |
| `IFormBuilderOptionService` | a field's or an option list's options, in a culture and for a parent's answers |

A change refused for its values throws `FormBuilderValidationException` (each problem with a code, a path and a
message), one that met someone else's change first `FormBuilderConcurrencyException`, one the database refused
`FormBuilderConflictException` (its `Conflict` says why), and one a handler stopped `FormBuilderCancelledException`.
None of them wrote anything. A change is refused inside a unit of work of your own, as it tells handlers and drops
caches once it committed.

### Entries from code

`IFormBuilderEntryService` is for your server code. It reads every answer, sensitive ones too, and checks no user's
permissions, so code that serves a user checks theirs first.

```csharp
var result = await entries.CreateAsync(new FormBuilderNewEntry
{
    Form = formKey,
    SubmissionId = importedRow.Id,
    Answers = new Dictionary<string, FormBuilderNewAnswer>
    {
        ["email"] = new("ada@example.com"),
        ["name"] = new() { Parts = new Dictionary<string, string> { ["first"] = "Ada", ["last"] = "Lovelace" } },
        ["topics"] = new("sales", "support"),
    },
});

if (result.Entry is { } entry)
{
    await entries.ChangeStateAsync(entry.Key, FormBuilderEntryState.Approved);
}
```

- `CreateAsync` runs the submission pipeline as a headless server's post with its server key does: the published
  version reads and checks the answers, its rules, places and unique answers hold, its actions run. No challenge,
  honeypot or captcha is asked, no address is kept, and the same submission id with the same answers answers the same
  entry again (`Replayed`). A form for members takes it when `Member` names the member. A refusal has the public API's
  code in `Refusal` and its errors in `Problems`. Files come only as a visitor's uploads.
- `ChangeStateAsync` moves an entry as an action does: at once, without an undo window, with the actions the state sets
  off.
- `ListAsync` lists a form's entries newest first, by state and time, a page at a time: pass the page's `Next` as the
  next query's `After`.

### Showing approved entries on a page

`IFormBuilderEntryReader.ReadApprovedAsync(form, request)` reads a published form's approved entries, newest first, each
answer as its form shows it in the culture asked for, for example for a guestbook:

```cshtml
@inject IFormBuilderEntryReader Reader
@{
    var reading = await Reader.ReadApprovedAsync("guestbook", new FormBuilderEntryReadRequest { Fields = ["name", "message"], Take = 20 });
}
@foreach (var entry in reading?.Entries ?? [])
{
    <article>
        @foreach (var answer in entry.Answers) { <p><strong>@answer.Label</strong> @string.Join(", ", answer.Texts)</p> }
    </article>
}
```

A page never gets an entry's key, reference, member, address or files, nor sensitive answers, hidden fields, signatures
or consents, and test entries never show. An entry shows once approved: by a reviewer, or as it arrives where the form
approves automatically, so tell your visitors on the form that their entry may be shown. Cache the page's output as
your site caches others.

### A form as HTML from code

`IFormBuilderRenderer.RenderAsync(httpContext, new FormBuilderRenderRequest("contact"))` renders a published form where
no view invokes `<jfb-render>`, for example in a controller that answers a fragment, with the same markup, assets and
post back to the page. Its assets come once per request; pass `Assets = false` for a page that has them already.

## 6. The website

- **Placing a form:** `<jfb-render form="contact" />` (by alias, key or the form picker's value), with `theme`,
  `assets` and `context-<name>` attributes, or `@await Component.InvokeAsync("FormBuilderForm", new { form = "contact" })`.
- **Assets:** `<jfb-assets />` writes the runtime and the stylesheets once per page, where you place it; otherwise the
  first form writes them. A CSP nonce comes from `<jfb-assets nonce="...">` or from an `IFormBuilderCspNonceProvider`
  your site registers.
- **The markup contract:** the `data-jfb-*` hooks, ids, names and states every theme's views keep and the runtime reads
  (ADR-0015). A view of your own keeps them, and `FormBuilderHtml` writes the contract's attributes.
- **JavaScript:** `window.JironFormBuilder.init(root)` wakes forms added to the page later; `get(form)` gives `validate()`,
  `next()`, `back()`, `submit()` and `values()`; `consent.grant('captcha')` and `withdraw()` follow a cookie banner.
- **Events** for your analytics: `jfb:ready`, `jfb:page`, `jfb:invalid`, `jfb:submitting`, `jfb:submitted` and
  `jfb:error` bubble from the form. Jiron Form Builder tracks nothing itself.

## 7. The backoffice

Your backoffice extensions are an ordinary Umbraco package: an `umbraco-package.json` in `App_Plugins/<your folder>/`
lists their manifests, and their scripts are ES modules the backoffice loads. They need no module of Jiron Form Builder
at run time, only the names below. Their types ship with the package as
`/App_Plugins/JironFormBuilder/types/jiron-form-builder.d.ts`; copy the file into your project, or point a `paths` entry
of your `tsconfig.json` at it. It also adds the two manifest types to Umbraco's manifest map, so a typed manifest of
yours is checked. Jiron Form Builder's own backoffice compiles against the same file.

### A setting's own editor

A setting whose `Editor` no built-in editor draws (section 3) is drawn by the element of a `jfbSettingEditor` manifest
whose `meta.editor` names it, in a field type's settings and in an action type's alike:

```json
{
  "type": "jfbSettingEditor",
  "alias": "Acme.SettingEditor.Swatches",
  "name": "Swatches",
  "element": "/App_Plugins/Acme/swatches.js",
  "meta": { "editor": "acme.swatches" }
}
```

The designer draws the setting's label, its help and any problem around your element, and gives it:

| Property | What it is |
| --- | --- |
| `value` | the setting's value as stored, or undefined for none |
| `setting` | the setting: its `name`, `kind`, `itemKind`, `editor`, `default` and `choices` |
| `label` | the setting's label in the backoffice's language; give it to your control as its accessible name |
| `culture` | the language the designer edits the form's texts in, such as `en-US` |
| `invalid` | whether the value you gave last cannot be kept |

Your element sets `value` and dispatches a `change` event on itself. The designer keeps the value as it is, and the
server holds it to the setting's kind when the draft is saved and to your type's `CheckSettings` when the form is
published, as it does every setting, so your editor needs no rules of its own beyond what helps the editor. A value that no built-in kind describes, such as a list of objects, is a setting of
the kind `Json` (a `JsonElement` property), which your type's `CheckSettings` holds to its shape.

The label above your element is the designer's. Words of your own come from a `localization` manifest of your
package, in English and German like the backoffice's, and a setting's label or a type's name may be such a term
(`#acme_colours`).

### A field type's preview

The canvas and the preview draw a site's own field type with its label and help, and with the element of a
`jfbFieldPreview` manifest whose `meta.fieldType` names the type in the control's place:

```json
{
  "type": "jfbFieldPreview",
  "alias": "Acme.FieldPreview.Colour",
  "name": "Colour preview",
  "element": "/App_Plugins/Acme/colour-preview.js",
  "meta": { "fieldType": "acme.colour" }
}
```

It gets `field` (its `key`, `type`, `alias`, `id`, `label`, `required` and `settings`, the type's settings with the
field's own values over the defaults) and `culture`, the language the designer shows, and it is given both again after
every change. It draws and takes no input: the canvas makes it inert, and a click on it selects the field. It lives in
the light DOM of the form's own markup, so the form's theme reaches it; use a shadow root for rules of your own. Without
a manifest the field shows its label alone.

### Views on the form workspace

A view of your own beside a form's Design, Settings, Actions and History is Umbraco's own `workspaceView`, with a
condition on the form workspace:

```json
{
  "type": "workspaceView",
  "alias": "Acme.WorkspaceView.Embed",
  "name": "Embed",
  "element": "/App_Plugins/Acme/embed-view.js",
  "weight": 10,
  "meta": { "label": "#acme_embed", "pathname": "embed", "icon": "icon-code" },
  "conditions": [{ "alias": "Umb.Condition.WorkspaceAlias", "match": "Jiron.FormBuilder.Workspace.Form" }]
}
```

The view reads the open form from Umbraco's workspace context, whose entity type for a form is `jfb-form`:

```js
const FORM_WORKSPACE = new UmbContextToken('UmbWorkspaceContext', undefined, (context) => context.getEntityType() === 'jfb-form');

this.consumeContext(FORM_WORKSPACE, (context) => {
  this.observe(context?.form, (form) => (this.alias = form?.alias));
});
```

`JfbFormWorkspaceContext` declares what it gives: the form's key, alias and name, and its draft's and live version's
numbers. It is read-only; a view that changes a form does so through the management API, as the designer does.

## 8. Headless sites

`<jfb-form>` writes every built-in type's control itself. For a site's own type it asks the site: register the function
that writes the type's control once the element is defined, and forms shown before it are shown again with it.

```js
const form = await customElements.whenDefined('jfb-form');
form.defineControl('acme.colour', (field, document) => {
  const choices = document.createElement('div');
  choices.setAttribute('role', 'radiogroup');
  choices.setAttribute('aria-describedby', `${field.helpId} ${field.errorId}`);
  for (const [at, colour] of (field.control?.settings?.colours ?? []).entries()) {
    const label = document.createElement('label');
    const input = document.createElement('input');
    input.type = 'radio';
    input.name = field.name;
    input.value = colour.value;
    input.checked = field.values.includes(colour.value);
    if (at === 0) input.id = field.id;
    label.append(input, ` ${colour.name}`);
    choices.append(label);
  }
  return choices;
});
```

The function gets the field as the element endpoint answers it (`JfbControlField`: its `id`, `name`, `helpId`,
`errorId`, `isRequired`, its `values`, and in `control` the type's public settings and the answer's parts) and the
document to create nodes in, and returns the node that goes between the label and the help, or null for none. It keeps
the markup contract, as the type's Razor view does: controls named as the field's `name` (a part as `name.part`), the
first with the field's `id`, described by its help and error. The runtime then reads, checks and posts them as it does
a built-in type's. A type with no function shows its label and a warning in the console.

