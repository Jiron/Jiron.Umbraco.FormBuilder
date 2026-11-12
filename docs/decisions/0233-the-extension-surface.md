# 0233. The extension surface: services, entry notifications and the backoffice's extension points

- Status: accepted; completes section 4.9 and [ADR-0053](0053-form-service-in-parts.md) (the services' parts), and the
  headless element of [ADR-0128](0128-the-element-writes-the-servers-view-model.md) for a site's own field types
- Date: 2026-11-11
- Roadmap step: 15.1 (part 15.1, the public C# surface and the backoffice's extension points)

## Context

Section 4.9 lists what a site's own code builds on. Most of it existed by Phase 14: the registries of
`builder.JironFormBuilder()`, the form and folder notifications, the action notifications, `ExportCompleted`, the merge
tag and option services, the tag helpers. Missing were:

- the form library's services, which were internal although 4.9 names them (`IFormBuilderFormService` and its parts,
  `IFormBuilderFolderService`), with their records and the exceptions they throw;
- the entry notifications `EntrySubmitting` (cancelable, field errors), `EntryStateChanged` and `EntryDeleting`;
- `IFormBuilderEntryService`, `IFormBuilderEntryReader` and `IFormBuilderRenderer`;
- in the backoffice, the manifest types `jfbSettingEditor` and `jfbFieldPreview`, workspace views of a site's own on the
  form workspace, and TypeScript declarations for their authors;
- on a headless site, the control `<jfb-form>` writes for a site's own field type, which it left empty with a warning.

Whatever becomes public is a promise kept for a major version (D21): it has to be small, documented, and reviewed
whenever it changes.

## Decision

1. **The public surface is listed and reviewed.** `PublicApiSnapshotTests` lists every public type of Core and Web with
   every public and protected member, its nullability, `required`, defaults and the record members the compiler adds,
   in `docs/api/<assembly>.api.txt`. Any change fails the test until the listing is written again
   (`JFB_UPDATE_SNAPSHOTS=1`) and its diff reviewed with the change. The form library's services became public with
   what they take and give, and so did the exceptions they throw (`FormBuilderValidationException`,
   `FormBuilderConcurrencyException`, `FormBuilderConflictException` with `FormBuilderConflict`,
   `FormBuilderCancelledException`, `FormBuilderQueryTimeoutException`), so a site can catch them by type. Two
   parameters named as a VB keyword were renamed while still free to (`formAlias`, `state`).
2. **The entry notifications live in `Jiron.Umbraco.FormBuilder.Core.Entries`,** the entry events of 8.4 moved there from
   the webhooks' namespace before anything was released.
3. **`EntrySubmittingNotification`** is Umbraco's `CancelableNotification`, published once a submission passed every
   check and the protection, before anything is written: for a visitor's entry (Spam's too, with `IsSpam`), the
   preview's test entry (`IsTest`) and one a site's code creates (`IsFromCode`); never for a page's check or a saved
   draft, which store no entry. It carries the form, its version, the culture, the answers as merge tags see them
   (`AnswerOf(alias)`), the member, the site, the page and the context values.
   - A handler refuses the entry in its own words: `AddError(field, message, row)` at a field, `AddFormError(message)`
     or `CancelOperation(EventMessage)` at the form. The submission answers `422 validation_failed` with the code
     `custom` and the words as `text`; the runtime, `<jfb-form>` and the post without JavaScript show them where the
     form's own errors show, as written, a form's error first in the summary, linked to the first field as the empty
     submission's error is. A cancel without any words shows the words of a check without its own message ("Check this
     answer"), and is logged, so no new visitor text is needed in seven languages.
   - A handler that throws, or that holds the visitor up longer than 5 seconds, is logged and has no word: the entry is
     stored. A site's failure never loses a visitor's entry (D6).
   - Rejected: a separate validation interface (a second way to do what a notification does); refusing the entry when a
     handler fails (a site's outage would turn away every visitor); an error code per handler (the page could not word
     it).
4. **`EntryStateChangedNotification`** tells an entry's state change once it stands, with `From`: at once for a move
   without a user (an action, `IFormBuilderEntryService`), once the undo window of a user's move has passed, and when an
   entry leaves Spam or is verified. An entry that arrives is told by `EntrySubmitted`, not by this. It sets off no
   webhook of its own; the approved and rejected events still do.
5. **`EntryDeletingNotification`** is told before entries are deleted for good, one for each, while a handler can still
   read the entry (to archive it). It is not cancelable: deletes keep the site's retention and privacy promises (ADR-0222,
   ADR-0223). An entry a hold keeps after all, or that another delete took first, gets no `EntryDeleted`.
6. **`IFormBuilderEntryService`** is for a site's own server code, which reads every answer, sensitive ones too, and checks
   no user's permissions (the code that serves a user checks theirs):
   - `GetAsync`, and `ListAsync` of a form's entries newest first, by state and time, a page at a time with an opaque
     cursor (the submission time and the entry's id); drafts are no entries here;
   - `CreateAsync` runs the submission pipeline as a headless server's post with its server key does, under the reserved
     key name `@code`, which no configured key can have: no challenge, honeypot or captcha, no address kept, the same
     submission id answering the same entry again; a form for members takes it when it names the member, whom the
     code vouches for. Its result is the entry, or the public API's code with the errors;
   - `ChangeStateAsync` moves an entry as an action does: at once, without an undo window, with the actions the state
     sets off.
7. **`IFormBuilderEntryReader`** reads a published form's approved entries for a site's own pages, newest first, each
   answer in the texts its export shows, in the culture asked for. A page never gets an entry's key, reference, member,
   address or files, nor the answers of sensitive fields, hidden fields, signatures and consents, and test entries never
   show. An entry shows once approved, by a reviewer or on arrival where the form approves automatically; whether the
   form tells its visitors so is the site's.
8. **`IFormBuilderRenderer`** renders a published form to HTML from code where no view invokes `<jfb-render>`, with the
   view component's rendering, its assets once per request unless asked not to, and nothing for a reference that names no
   form.
9. **The backoffice's extension points** need no module of Jiron Form Builder at run time, only names:
   - `jfbSettingEditor` (`meta.editor`): the element of a custom editor for every setting whose schema names that editor
     (`[FormBuilderSetting(Editor = "...")]`), in a field type's and an action type's settings. The designer draws the
     label, help and problem around it and gives it `value`, `setting`, `label`, `culture` and `invalid`; it sets `value`
     and dispatches `change`, and the server checks the value as it checks every setting;
   - `jfbFieldPreview` (`meta.fieldType`): the element the canvas and the preview draw in place of a site's own field
     type's control, given the field with its effective settings and the culture; it draws and takes no input;
   - workspace views: Umbraco's own `workspaceView` with the condition `Umb.Condition.WorkspaceAlias` on
     `Jiron.FormBuilder.Workspace.Form`, reading the open form from the workspace context by its entity type `jfb-form`;
   - the declarations: `jiron-form-builder.d.ts`, shipped in `App_Plugins/JironFormBuilder/types/`, which also adds both
     manifest types to Umbraco's manifest map. Jiron Form Builder's own hosts compile against that very file, so it
     cannot drift from what runs.
10. **A site's field type on `<jfb-form>`:** `customElements.get('jfb-form').defineControl(type, write)` registers the
    function that writes the type's control from the field's view model and the type's part of the element endpoint's
    answer; forms shown before it are shown again with it. A type without one keeps its label and the warning.

## Consequences

- A change of the public surface is a reviewed diff in `docs/api/`; a breaking one waits for a major version with its
  deprecation (D21).
- A site validates against its own systems in a handler, in its visitors' language, without JavaScript of its own, and
  every transport shows the errors alike.
- The entry notifications a webhook carries (8.4) are unchanged; the two new events are for code only.
- Extension authors compile against one shipped file; Jiron Form Builder's own build breaks before the file and the
  backoffice could disagree.

## Amendment (Phase 15 review, 2026-11-11)

The review found two deletes that take entries without `EntryDeletingNotification`, which section 5 had promised for
every delete for good. Both stay as they are, and the promise is made exact:

- **A form deleted for good** takes its entries in batches inside units that hold the form. `FormDeletingNotification`
  (or `FolderDeletingNotification` and `TrashEmptyingNotification` for forms deleted with their folder or the trash)
  is told before the first batch, while every entry can still be read, so a site that archives entries archives a
  form's there, once, rather than hearing of each entry in turn.
- **A form that keeps no entries** (`Store: never`) removes each entry inside the unit that settles it, once no mail,
  run or move can come for it. A handler could not read it there, as the services refuse a caller's unit, and the
  editor chose not to keep it; a site sees such an entry as `EntrySubmittedNotification`.

`EntryDeletingNotification` is therefore told before a user's delete, retention and the privacy tools' erase, each
entry once. Its declaration and docs/extending.md say so.

## Amendment (step 16.7, 2026-11-12)

The code quality pass read the whole public surface against this record and `docs/extending.md`, and 143 types left
it, each used only inside the packages and their friend assemblies:

- **The Management API's 118 request and response models** that were still public in Web: the controllers are
  internal and found by the package's own provider, as the other 239 models already were.
- **The built-in actions' settings** (email, webhook with its format and target, content with its parent source, set
  state with its target) and **the option list kinds** with their settings: their action types and kinds are internal.
- **`BuiltInThemes`**, the registration list of the themes (the theme types stay public, so a site can remove one);
  **the webhook events** of the entry notifications (Umbraco finds them by registration; the notifications stay); and
  **`FormPickerDeliveryMarker`**, the in-between value the Delivery API middleware rewrites.

Kept public on purpose, though they may look internal: the built-in field types, themes and option sources with their
settings (a site removes or replaces them by type), composers, collections and builders, options classes, tag helpers,
the view component and surface controller (MVC's discovery), the form picker's editor and converter (Umbraco's type
scan), the rendering models and `EmailViewModel` (a site's own views compile against them), the projection of the
public JSON (4.2b), and the types the public services' signatures use (`FolderVisibility`, `FormAccessTarget`,
`DeletionCandidate`, `Page<T>`, `PageRequest`, `FormVersionKind`, `FieldKind`) along with the codes a site reads
(`ActionTestCodes`, `MergeTagProblems`, `FormBuilderFileId`, `FormBuilderSettingsSections`, `FormBuilderPermissions`).
Naming that differs between older and newer contracts (a key with and without its `Key` suffix, `Name` or `Alias` as an
extension's id) stays: each contract is consistent within itself and named so in the extension guide and the samples,
and renaming across them would touch every sample for a difference of words. A new contract takes the names of the
contracts it sits beside.
