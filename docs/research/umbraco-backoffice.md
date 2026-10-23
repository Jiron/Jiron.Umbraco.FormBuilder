# Umbraco 17 backoffice: what the sandbox showed

Steps 1.2a and 1.2b built a throwaway extension in a sandbox site and drove every piece with Playwright in Edge.
This file records what works, the exact shapes that worked, and the traps. Sections 1 to 12 come from 1.2a
(navigation, workspaces, building blocks); sections 13 to 22 from 1.2b (search, localization, dashboards, health
checks, webhooks, references, member views, UFM, background jobs and the 17.5 floor).

- **Versions:** Umbraco 17.7.1, `@umbraco-cms/backoffice` 17.7.1 (MIT), `@hey-api/openapi-ts` 0.99.0, .NET 10.
- **The sandbox:** `node scripts/sandbox.mjs create|start|stop|status|reset|delete|login`. It builds
  `.local/sandbox/Sandbox` (the site, SQLite, unattended install, port 5510) and `.local/sandbox/Spike` (the
  extension). The admin login is generated into `.local/credentials.json` and never printed.
- **Evidence:** the Playwright probes and about 90 screenshots live in `.local/sandbox/checks/` (git-ignored).
  Each claim below was seen in the browser unless it says "read in the source".
- **Reading Umbraco's own code:** the npm package ships the compiled backoffice (`dist-cms/`) and official
  samples (`examples/`), and the NuGet cache holds the XML docs. Both are Umbraco's MIT-licensed code; they
  were read for API shapes only.

## 1. The extension template and the build

`dotnet new umbraco-extension -n <Name> --include-example` (in the private template hive, §3.4) creates a Razor
class library with `Client/` (Vite, Lit, TypeScript) and output to `wwwroot/App_Plugins/<Name>`.

Fix these before anything else:

| Template default (17.7.1) | Problem | What to do |
| --- | --- | --- |
| `@hey-api/openapi-ts ^0.85.2` | `@umbraco-cms/backoffice` 17.7.1 needs `>=0.99.0 <1`; `npm install` fails with ERESOLVE | pin `0.99.0` (the sandbox script does) |
| the pre-generated client in `src/api` | fails `tsc` against 0.99 (`QuerySerializerOptions`) | regenerate from the running site |
| `runtimeConfigPath` in `scripts/generate-openapi.js` | 0.99 resolves it from the project root, not the output folder | `'./src/hey-api.ts'` |
| `allowTelemetry: true` in `umbraco-package.json` | sends package telemetry by default | decided in 3.1: `false` (ADR-0025) |
| `sourcemap: true` in `vite.config.ts` | ships source maps | off for release builds |
| base controller `[Authorize(Policy = AuthorizationPolicies.SectionAccessContent)]` | a placeholder: anyone with the **Content** section may call the API | Jiron Form Builder's own section policy |
| `ControllerBase` as the base class | none of Umbraco's management API conventions | see section 2 |

More build facts:

- `hey-api.ts` takes its configuration from `umbHttpClient.getConfig()` in `@umbraco-cms/backoffice/http-client`,
  which brings the base URL and the credentials (section 3). §3.4 said `UMB_AUTH_CONTEXT.configureClient(client)`;
  1.2b corrected it.
- Umbraco 17.7.1 pulls transitive packages with advisories (NU1902/NU1903): `MessagePack` 3.1.8,
  `System.Security.Cryptography.Pkcs` 10.0.10 and `System.Security.Cryptography.Xml` 10.0.10. Step 3.1 decided: a
  reviewed suppression per advisory, never a pin (ADR-0024).
- **New bundle files need a site rebuild.** Vite emits hashed chunk names; the site serves a library's
  `wwwroot` through the static web assets manifest written at build time, so a new chunk name answers 404 until
  the site is rebuilt and restarted. A watch mode for development must account for this.
- `dotnet run` started detached leaves the site as a grandchild with its own console, so its log never reaches
  a file. Start the built executable directly (the sandbox script does).
- A class named `Constants` in the package's root namespace hides `Umbraco.Cms.Core.Constants`. The product
  avoids a root-level `Constants` class.

## 2. The management API

What worked (`SpikeFormsController`):

```csharp
[ApiController]
[BackOfficeRoute("spike/api/v{version:apiVersion}")]   // the product: "jfb/management/api/v{version:apiVersion}"
[MapToApi("spike")]
[ApiVersion("1.0")]                                     // on every concrete controller; it is not inherited
[ApiExplorerSettings(GroupName = "Spike")]              // the tag, which becomes the generated service's group
public sealed class SpikeFormsController(SpikeFormStore store) : SpikeApiControllerBase { … }
```

- **Generic response types need a schema id handler.** Two `SpikePaged<T>` responses in one document made
  Swagger answer 500 ("SpikePaged`1" twice). A subclass of Umbraco's `SchemaIdHandler`, registered as
  `ISchemaIdHandler`, with `CanHandle(type) => type.Namespace starts with the package's namespace`, fixes it.
  Schema names then come out as `SpikePagedSpikeTreeItemModel` (Umbraco's style adds `Model`).
- **Problem details reach the editor as they are.** A `Problem(title: "The folder is not empty.", statusCode: 400)`
  showed as a red toast "An error occurred / The folder is not empty." So every refusal title is a complete,
  translated sentence written for editors.
- **Creating:** return 201 with the header `Umb-Generated-Resource: <id>`; the client reads it back.
- **No content:** a 204 reaches `tryExecute` as `data: undefined`; the recycle bin's "original parent" uses it
  for "gone or at the root".
- **The section grant:** an `INotificationAsyncHandler<UmbracoApplicationStartedNotification>` that loads the
  administrators' group with `IUserGroupService.GetAsync(Constants.Security.AdminGroupAlias)`, calls
  `AddAllowedSection(alias)` and saves with `UpdateAsync(group, Constants.Security.SuperUserKey)`. It worked on the
  first start. (Read in the source: Umbraco maps the section alias `forms` to `Umb.Section.Forms`, so neither is
  usable as Jiron Form Builder's section alias.)

## 3. Calling the API from the backoffice

- `tryExecute(host, promise)` from `@umbraco-cms/backoffice/resources` returns **what the promise resolves to**
  (`UmbApiResponse<T> = T & { error }`), and shows a toast on failure. Pass the generated client call itself:
  `tryExecute(this, SpikeService.treeRoot({ query }))` gives `{ data, error, response }`. Passing
  `.then(r => r.data)` loses the wrapper, and the tree silently renders nothing.
- Tree requests carry `args.paging` (offset `{ skip, take }` or target-based); the older `skip`/`take`
  arguments were deprecated in 16.3 and go away in 18. Accept both until then.
- **The token never reaches scripts.** Every request from the backoffice carried `Authorization: Bearer [redacted]`,
  a fixed placeholder; the server takes the real token from an HttpOnly cookie, and `localStorage`,
  `sessionStorage` and `document.cookie` held no token. A `fetch` without that header got 401, and with it 200.
  The product always calls through the generated client; tests that call the API from a signed-in page send the
  placeholder the same way.

## 4. A section with a sidebar menu

```ts
{ type: 'section', alias: 'Jfb.Spike.Section', meta: { label: 'Spike forms', pathname: 'spike-forms' },
  conditions: [{ alias: 'Umb.Condition.SectionUserPermission', match: 'Jfb.Spike.Section' }] },
{ type: 'sectionSidebarApp', kind: 'menuWithEntityActions', alias: '…',
  meta: { label: 'Spike forms', menu: MENU_ALIAS, entityType: FORM_ROOT_ENTITY_TYPE },
  conditions: [{ alias: 'Umb.Condition.SectionAlias', match: 'Jfb.Spike.Section' }] },
{ type: 'menu', alias: MENU_ALIAS },
{ type: 'menuItem', alias: '…', weight: 200,
  meta: { label: 'Entries', icon: 'icon-inbox', entityType: ENTRIES_ROOT_ENTITY_TYPE, menus: [MENU_ALIAS] } },
```

- `menuWithEntityActions` puts the root entity's actions ("…" and "+") in the sidebar header.
- The sidebar heading repeats the section's label ("Spike forms" in the top bar and again above the tree). This
  breaks §4.13 "say it once"; the product needs its own sidebar element or a heading that adds information.
- A plain `menuItem` with an `entityType` opens `section/<pathname>/workspace/<entityType>`, so every such entity
  type needs a workspace (a `default` kind is enough).

## 5. A tree with folders, actions and a recycle bin

### Pieces

| Piece | Shape that worked |
| --- | --- |
| Tree | `{ type: 'tree', kind: 'default', meta: { repositoryAlias } }`; repository extends `UmbTreeRepositoryBase`, data source extends `UmbTreeServerDataSourceBase` with `getRootItems`, `getChildrenOf`, `getAncestorsOf` and a `mapper`; `requestTreeRoot()` returns the root model. Tree stores are deprecated: no store. |
| Tree items | `treeItem` kind `default` for root and folders; forms use an own `api` that extends `UmbDefaultTreeItemContext` and sets `UmbIsTrashedEntityContext` from the item (without it, Restore never appears) |
| Menu items | `menuItem` kind `tree`, `meta: { treeAlias, hideTreeRoot: true }` shows the root's children directly; a second `menuItem` kind `tree` shows the bin |
| Item repository | `UmbItemRepositoryBase` + `UmbItemServerDataSourceBase({ getItems, mapper })` + an `itemStore` (`UmbItemStoreBase`); it must resolve forms **and** folders, with names |
| Create | `entityAction` kind `create` for root and folders; `entityCreateOptionAction` with an api extending `UmbEntityCreateOptionActionBase` whose `getHref()` returns the workspace's create route; a second option with kind `folder` and `meta.folderRepositoryAlias` |
| Folders | a detail repository over `UmbFolderModel { unique, entityType, name }` **with a store** (`UmbDetailStoreBase`); `entityAction` kinds `folderUpdate` (rename) and `folderDelete` |
| Move | `entityAction` kind `moveTo`, `meta: { treeRepositoryAlias, moveRepositoryAlias, treeAlias, foldersOnly: true }`; the move repository extends `UmbRepositoryBase` and implements `requestMoveTo({ unique, destination: { unique } })` |
| Trash | kind `trash`, `meta: { itemRepositoryAlias, recycleBinRepositoryAlias }`, condition `Umb.Condition.EntityIsNotTrashed` |
| Restore | kind `restoreFromRecycleBin`, same meta plus `pickerModal` (a `UmbModalToken(UMB_TREE_PICKER_MODAL_ALIAS, { modal: { type: 'sidebar', size: 'small' }, data: { treeAlias, foldersOnly: true } })`), condition `Umb.Condition.EntityIsTrashed` |
| Delete for good | kind `delete`, `meta: { itemRepositoryAlias, detailRepositoryAlias }`, condition `Umb.Condition.EntityIsTrashed` |
| Empty the bin | kind `emptyRecycleBin` on the bin root, `meta: { recycleBinRepositoryAlias }`, condition `Umb.Condition.EntityHasChildren` |
| Recycle bin | `UmbRecycleBinRepositoryBase` over a data source with `trash`, `restore`, `empty` and `getOriginalParent` (`null` = the root, `{ unique }` = the parent, an error = ask with the picker); the bin is its own tree with `treeItem` kind `recycleBin` and `meta.supportedEntityTypes`; its items map to the normal entity type plus `isTrashed: true` |
| Workspaces | root and bin: kind `default`; folders: kind `routable` with `<umb-folder-workspace-editor>` and a Save action; a `workspaceView` kind `tree` (`meta.treeAlias`) lists children in the root, folder and bin workspaces, but it arrives in 17.6.0: section 22 has the view that works on 17.5 too |

### Behaviour seen

- **Create:** the root's "+" and a folder's "Create…" open one dialog listing "Form / A new, empty form" and
  "Folder…". The folder option opens a sidebar "Create folder"; Enter saves and the tree reloads.
- **Rename:** "Rename…" opens the same folder modal, filled in; Enter saves and the label updates. There is no
  generic rename for other custom entities: a form is renamed in its workspace header.
- **Move:** the "Move to" sidebar shows only folders (expanders only where subfolders exist) with Close and
  Move; Move stays disabled until a pick. Umbraco reloads only the source (a TODO in 17.7.1), so the move
  repository dispatches `UmbRequestReloadChildrenOfEntityEvent` for the destination through
  `UMB_ACTION_EVENT_CONTEXT`. The sidebar title "Forms" sits above a box whose root is "Forms" again: the
  product's picker shows the root once.
- **Trash:** a confirm "Are you sure you want to move **Newsletter** to the Recycle Bin?" with Cancel and Trash.
  Inside the bin a form offers "Delete…" and "Restore…".
- **Restore:** a sidebar "Restore **Newsletter** to **Contact**" (the original parent from the server); after
  Restore the form is back in its folder.
- **Delete for good:** a confirm, then a green "Deleted" toast.
- **A non-empty folder:** "Delete…" is still offered; the server refuses with "The folder is not empty.", which
  appears as a red toast, and two console errors are logged. The product says what blocks the delete and how to
  fix it, and offers the action only when it can work.
- **Empty the bin:** a confirm "Empty recycle bin". Afterwards only "Reload children" remains and, as the only
  action, it shows as an icon without the "…" menu.
- **The bin root's `hasChildren`** must come from the server. Hard-coded `true`, the empty bin shows an
  expander and the empty action after every restart.
- Every action menu shows its first action as an extra icon next to "…".

## 6. A routable workspace with views, save and actions

```ts
// The context: create and edit routes on a named detail workspace.
export class SpikeFormWorkspaceContext extends UmbEntityNamedDetailWorkspaceContextBase<SpikeFormModel, SpikeFormDetailRepository> {
  constructor(host: UmbControllerHost) {
    super(host, { workspaceAlias, entityType, detailRepositoryAlias });
    this.routes.setRoutes([
      { path: 'create/parent/:parentEntityType/:parentUnique', component: Editor, setup: async (_c, info) => {
          await this.createScaffold({ parent: { entityType: …, unique: … } });
          new UmbWorkspaceIsNewRedirectController(this, this, routerSlot); } },
      { path: 'edit/:unique', component: Editor, setup: (_c, info) => {
          this.removeUmbControllerByAlias(UmbWorkspaceIsNewRedirectControllerAlias);
          this.load(info.match.params.unique); } },
    ]);
  }
}
```

- **Manifest:** `{ type: 'workspace', kind: 'routable', meta: { entityType }, api }`. URLs:
  `section/<pathname>/workspace/<entityType>/create/parent/<parentType>/<parentId|null>` and `…/edit/<id>`.
- **Editor:** `<umb-entity-detail-workspace-editor .backPath=…>` with
  `<umb-workspace-header-name-editable slot="header" label="Form name">`. It draws the back arrow, the name
  field, the entity "…" menu and the views as tabs on the right.
- **Data:** `UmbDetailRepositoryBase(host, DataSource, storeToken)` **needs a store**, or reads wait forever.
  The data source has `createScaffold`, `read`, `create(model, parentUnique)`, `update` and `delete`, and
  `create` and `update` return the saved entity (read back). Scaffold ids come from `UmbId.new()`.
- **Views:** `workspaceView` with `meta: { label, pathname, icon }` and a `Umb.Condition.WorkspaceAlias`
  condition; tabs appear only with two or more views; each view has the route `view/<pathname>`.
- **Save:** `workspaceAction` kind `default` with api `UmbSubmitWorkspaceAction`, `meta: { label, look: 'primary',
  color: 'positive' }`. Saving a new form redirected from the create route to `edit/<id>`, and the parent folder
  gained an expander.
- **A menu next to Save:** `workspaceActionMenuItem` kind `default` with `forWorkspaceActions: <save alias>` and an
  api extending `UmbWorkspaceActionMenuItemBase`; "Save and preview" (eye icon) appeared under the caret, and our
  `execute()` called `requestSubmit()` and then showed a toast.
- **Validation:** `umbBindToValidation(this, '$.description', value)` on a `required` input. With the Settings
  view open, Save was blocked and "This field is required" showed under the field. **A view that has not been
  opened does not validate:** a new form saved without its required description because the Settings view
  had never rendered. So the server validates everything, and the workspace context adds messages for fields
  outside the open view itself (`validationContext.messages.addMessage`). Tabs show no error badge.
- **Unsaved changes:** editing the name and clicking another tree item opened "Discard unsaved changes"
  (Stay / Discard changes). The check compares the saved and current models as JSON strings, so key order
  matters: the model must be built in one stable order.

## 7. A collection with a table, filter, paging and bulk actions

```ts
{ type: 'workspace', kind: 'default', alias: WS, meta: { entityType: ENTRIES_ROOT, headline: 'Entries' } },
{ type: 'workspaceView', kind: 'collection', meta: { label, pathname, icon, collectionAlias: COLLECTION },
  conditions: [{ alias: 'Umb.Condition.WorkspaceAlias', match: WS }] },
{ type: 'collection', kind: 'default', alias: COLLECTION, meta: { repositoryAlias, noItemsLabel: 'No entries match.' } },
{ type: 'collectionView', kind: 'table', meta: { label: 'Table', icon: 'icon-list', pathName: 'table',
  columns: [{ field: 'form', label: 'Form' }, { field: 'email', label: 'Email' }, …] }, conditions: inCollection },
{ type: 'collectionTextFilter', kind: 'default', conditions: inCollection },
{ type: 'collectionAction', kind: 'button', meta: { label: 'Export', href: '…' }, conditions: inCollection },
{ type: 'entityBulkAction', kind: 'default', api, forEntityTypes: [ENTRY], meta: { icon: 'icon-check', label: 'Approve' },
  conditions: inCollection },   // inCollection = [{ alias: 'Umb.Condition.CollectionAlias', match: COLLECTION }]
```

- The repository implements `requestCollection({ skip, take, filter })` and returns `{ data: { items, total } }`.
- **The table kind** always adds a "Name" column (a link only when the item has an href) and an entity-actions
  column; the manifest columns come between. Values are plain text, or rendered through a `valueType`. There
  is no column sorting.
- **Selection:** a select-all box in the header; row boxes appear on hover; once one row is selected, a click
  anywhere on a row toggles it. Every row box is named just "Select", without the row's name: screen readers
  hear the same name for every row, so the product's table names each box ("Select entry 1042").
- **Bulk bar:** at the bottom, "Clear selection", "1 of 57 selected" and the bulk actions as buttons. Approve and
  delete worked; the action consumes `UMB_COLLECTION_CONTEXT`, clears the selection and calls `loadCollection()`.
  A reload event does not reach this collection, because a default workspace's entity context has no unique.
- **Paging:** 50 items per page by default; 57 entries gave 50 rows and a pager. The page is not in the URL.
- **The filter** sends free text to the server (debounced); the server decides what it searches.
- Dates formatted with `toLocaleString()` follow the browser's locale, not the backoffice language; the product
  formats with the backoffice's localization.

## 8. Modals

```ts
export const NOTE_MODAL = new UmbModalToken<NoteData, NoteValue>('Jfb.Spike.Modal.Note', { modal: { type: 'dialog', size: 'small' } });
// manifest: { type: 'modal', alias: 'Jfb.Spike.Modal.Note', element }   (alias equals the token's)
class NoteModal extends UmbModalBaseElement<NoteData, NoteValue> {
  render() { return html`<umb-body-layout headline=…> … <uui-button slot="actions" @click=${this._rejectModal}>
    <uui-button slot="actions" look="primary" @click=${this._submitModal}>`; }   // value via this.updateValue({...})
}
const value = await umbOpenModal(this, NOTE_MODAL, { modal: { type: 'sidebar', size: 'small' }, data });
```

- The same element opened as a dialog and as a sidebar (overriding `modal.type` per call).
- `umbOpenModal` resolves with the value, which is `undefined` when nothing was set, and rejects on Cancel or
  Escape.
- **Initial focus goes to the `<dialog>`,** not to the first field. Typing right after opening went nowhere. The
  product's modals move focus to their first field themselves.
- **`umbConfirmModal` renders a string `content` as HTML (confirmed in 17.7.1).** A string holding
  `<img src=x onerror="…">` ran the handler; the same text in an `html` template was escaped and nothing ran.
  Rule for Jiron Form Builder: confirm and notification texts are always Lit templates, never strings that
  contain a name, a label or anything else an editor or visitor typed. A lint rule or test guards it (Phase 9).

## 9. Inputs inside our own elements

All of these rendered and worked inside a custom element once their packages were imported
(`import '@umbraco-cms/backoffice/document'`, `/media`, `/member`, `/tiptap`, `/user-group`, and in 1.2b
`/code-editor`, `/data-type`, `/member-group`):

| Element | Notes |
| --- | --- |
| `<umb-input-tiptap>` | `.configuration` is an `UmbPropertyEditorConfigCollection` set before the first render: `extensions` (for example `Umb.Tiptap.RichTextEssentials`, `Umb.Tiptap.Bold`, `Umb.Tiptap.Italic`, `Umb.Tiptap.Link`, `Umb.Tiptap.BulletList`), `toolbar` as rows of groups of aliases (`[[['Umb.Tiptap.Toolbar.Bold', 'Umb.Tiptap.Toolbar.Italic'], ['Umb.Tiptap.Toolbar.Link', 'Umb.Tiptap.Toolbar.BulletList']]]`), and `maxImageSize`. A toolbar button whose extension is missing stays disabled. The value is an HTML string; `{{name}}` merge tags survive as text. |
| `<umb-input-document>` | `.max`, `.selection` (ids), `value` (comma-separated); a "Choose" button |
| `<umb-input-media>` | a large dashed "+ Choose" tile |
| `<umb-user-group-input>` | a "Choose" button |
| `<umb-input-member>` | a "Choose" button |
| `<umb-input-member-group>` | a "Choose" button |
| `<umb-data-type-input>` | a "Choose" button; `value` is a comma-separated string, `selection` the ids |
| `<umb-code-editor>` | `.code` (not `value`) and `.language` (`'css'` coloured a theme's CSS); the `input` event carries `target.code`; it needs a height (`autoHeight` arrives in 17.6) |

`<umb-property-layout label description>` with the input in `slot="editor"` gives the standard label column.

## 10. Drag and drop

Measured side by side on the same nested model; the decision is [ADR-0016](../decisions/0016-designer-interaction.md).

- **Umbraco's sorter** (`UmbSorterController`, one per level with its own `identifier`, `itemSelector`,
  `containerSelector`, `handleSelector`, `getUniqueOfElement`, `getUniqueOfModel`, `onChange`): mouse drag
  across groups works and autoscroll works. Handles cannot take focus, there is no keyboard path, a screen reader
  gets only text, and a finger drag does nothing. The model changes during the drag.
- **Our pointer drag** (Pointer Events, pointer capture, a 6 px threshold, `touch-action: none`, an insertion
  line, autoscroll, Alt+Up/Down, a live region): mouse and finger drags work across groups; Alt+Down moved
  "Phone" into the next group with focus kept on its handle and the announcement "Moved. Phone: position 1 of 5
  in Address, Page 1: About you."; each group is a list named by its heading with buttons "Move <label>".
- **Finding the scrolling area:** content slotted into a workspace scrolls inside the slot's container in
  another shadow root. Walking up with `parentNode` and `host` alone found nothing; walking with
  `assignedSlot ?? parentNode ?? host` found it, and autoscroll then moved the area from 0 to about 420 px.

## 11. A property editor with value converters

- **Server:** `[DataEditor("Jfb.Spike.FormPicker", ValueType = ValueTypes.String, ValueEditorIsReusable = true)]` on
  a class deriving from `DataEditor(IDataValueEditorFactory)`; it is found automatically. Don't name the alias
  constant `Alias`: it hides `DataEditor.Alias` (CS0108).
- **Client:** `{ type: 'propertyEditorSchema', alias: 'Jfb.Spike.FormPicker', meta: { defaultPropertyEditorUiAlias } }`
  and `{ type: 'propertyEditorUi', alias: 'Jfb.Spike.PropertyEditorUi.FormPicker', element, meta: { label, icon,
  group: 'pickers', propertyEditorSchemaAlias: 'Jfb.Spike.FormPicker', supportsReadOnly: true } }` (`group` is
  required in 17). The element implements `UmbPropertyEditorUiElement` (`value`, `readonly`) and fires
  `UmbChangeEvent`. In the content editor it showed as a select with "Contact / Contact us".
- **Seeding on start-up** (all returned Success):
  `new DataType(editor, IConfigurationEditorJsonSerializer) { Key, Name, EditorUiAlias, DatabaseType }` +
  `IDataTypeService.CreateAsync(dataType, superUser)`; `ITemplateService.CreateAsync(name, alias, content,
  superUser, key)`; `IContentTypeEditingService.CreateAsync(new ContentTypeCreateModel { Key, Alias, Name, Icon,
  AllowedAsRoot, AllowedTemplateKeys, DefaultTemplateKey, Containers = [{ Key, Name, Type = "Group" }],
  Properties = [{ Key, ContainerKey, Alias, Name, DataTypeKey }] }, superUser)`;
  `IContentEditingService.CreateAsync(new ContentCreateModel { Key, ContentTypeKey, TemplateKey, Variants =
  [{ Name }], Properties = [{ Alias, Value }] }, superUser)`; `IContentPublishingService.PublishAsync(key,
  [new CulturePublishScheduleModel { Culture = null }], superUser)`. This settles §3.4's *(verify)* on content
  creation: the async editing and publishing services work in 17.
- **Razor:** a `PropertyValueConverterBase` turned the stored id into a typed reference; the page rendered
  "Form: Contact us (5f1c…)".
- **Delivery API:** the same class implements `IDeliveryApiPropertyValueConverter`;
  `GET /umbraco/delivery/api/v2/content/item/{id}` returned
  `"form": { "id": "…", "name": "Contact us", "definitionUrl": "/jfb/api/v1/forms/…" }` (with
  `Umbraco:CMS:DeliveryApi:Enabled`).
- **Cache level:** a form lives outside the content cache and can be renamed or trashed, so the converter uses
  `PropertyCacheLevel.None` for both outputs. Renaming the form changed the page and the API output at once,
  without republishing. (`PropertyCacheLevel.Snapshot` is obsolete in 17.)

## 12. Themes and width

- **Themes:** `localStorage['umb-theme-alias']` = `umb-light-theme`, `umb-dark-theme` or `umb-high-contrast-theme`,
  then reload (or `UMB_THEME_CONTEXT.setThemeByAlias`, read in the source). Our elements followed both themes
  because they use only `--uui-color-*` tokens. The theme leaves no attribute on `<html>`.
- **Width:** the backoffice has a minimum width of 920 px. Below it the whole page scrolls sideways (100 px at
  820, 152 px at 768, 545 px at 375); the sidebar stays 300 px. At 920 px the top bar folds sections into "…"
  and a workspace has about 620 px. So Jiron Form Builder's screens are designed to work in a 620 px workspace;
  phones are not a backoffice target (the website forms are).

## 13. Search

```ts
{ type: 'searchProvider', alias: PROVIDER, name, api },             // api implements UmbSearchProvider
{ type: 'globalSearch', alias, name,
  meta: { label: 'Forms (spike)', searchProviderAlias: PROVIDER, sectionAlias: SECTION },
  conditions: [{ alias: 'Umb.Condition.SectionUserPermission', match: SECTION }] },
{ type: 'searchResultItem', alias, name, forEntityTypes: [FORM] },  // optional: the default shows icon and name
```

- The provider's `search({ query, paging })` returns `{ data: { items, total } }`, each item
  `{ entityType, unique, name, icon, href }` with a relative `href`
  (`section/<pathname>/workspace/<entityType>/edit/<id>`). A result is only a link.
- The header's search dialog shows one tab per `globalSearch`; a `searchProvider` alone appears nowhere. Opened
  inside our section, our tab was preselected, and "contact" listed "Contact us (Contact)" (name and folder) with
  the package icon.
- **The tab label is shown as written** (`meta.label`, read in the source): there is no `#key` lookup, and
  Umbraco's own tabs ("Documents") are English in every language too.
- There is no server-side search registry. The provider calls our own endpoint (`GET search/form?query&skip&take`),
  and the server decides what matches.

## 14. Localization

```ts
{ type: 'localization', alias, name, meta: { culture: 'en' }, js: () => import('./localization/en.js') },
{ type: 'localization', alias, name, meta: { culture: 'de' }, js: () => import('./localization/de.js') },
// en.js: export default { jfbSpike: { section: 'Spike forms',
//   dashboardIntro: (forms: number, entries: number) => `There are ${forms} forms and ${entries} entries.` } };
```

- Keys become `<area>_<key>` (`jfbSpike_section`). Elements call `this.localize.term('jfbSpike_dashboardIntro',
  forms, entries)`; manifests use `'#jfbSpike_section'` as a label (section, dashboard and workspace view labels
  all worked).
- Setting the admin's "UI Culture" to `de-de` on the user workspace turned the section, the dashboard, the member
  view's tab and our texts German ("Spike-Formulare", "Es gibt 4 Formulare und 57 Einträge."); `de-de` fell back
  to our `de` file. The lookup goes regional culture, then language, then `en` (read in the source).
- Function values receive the arguments, so each language handles its own plural forms and word order.
- Umbraco's own field labels follow the culture too: probes that find fields by label must not depend on English
  ("UI Culture" became "Sprache").

## 15. Dashboards, icons and conditions

- **Dashboard:** `{ type: 'dashboard', element, meta: { label: '#jfbSpike_dashboardTitle', pathname: 'overview' },
  conditions: [{ alias: 'Umb.Condition.SectionAlias', match: SECTION }, { alias: <own condition> }] }`, at
  `section/<section>/dashboard/<pathname>`. A section with a single dashboard shows no tab bar; the dashboard is the
  section's start page. Without a `SectionAlias` condition a dashboard appears in every section (read in the source).
- **Icons:** `{ type: 'icons', js }`, where the module's default export is
  `[{ name: 'jfb-spike-icon-form', path: () => import('./icon-form.js') }]` and `icon-form.js` default-exports
  the SVG text. The name then works in `<umb-icon name>` and in every manifest `icon`: the dashboard, search
  results and the member view's tab showed it. Drawn with `stroke="currentColor"`, it followed the theme.
  `<umb-icon>` placed in a paragraph took a line of its own; give it an explicit layout when it sits in text.
- **Condition:** a class extending `UmbConditionBase<Config>` that sets `this.permitted`, registered as
  `{ type: 'condition', alias, api }` and declared in `UmbExtensionConditionConfigMap`. Ours observed
  `UMB_CURRENT_USER_CONTEXT`'s `isAdmin` and kept the dashboard for administrators; the admin saw it. A refusal
  for other users is for Phase 9's permission tests, which use more than one user.

## 16. Workspace contexts and duplicate

- **Workspace context:** `{ type: 'workspaceContext', api, conditions: [{ alias: 'Umb.Condition.WorkspaceAlias',
  match: WS }] }`, where `api` extends `UmbContextBase` with its own `UmbContextToken('UmbWorkspaceContext', alias)`.
  Ours consumed the form workspace's context, observed its pages and counted fields; the Info view showed
  "Fields 4" for a form with four fields in three groups on two pages, and 0 for an empty one.
- **Duplicate:** `entityAction` kind `duplicate`, `meta: { duplicateRepositoryAlias, treeRepositoryAlias }`, and a
  repository with `requestDuplicate({ unique })`. "Duplicate…" created "Newsletter (copy)" through
  `POST form/{id}/duplicate` (201 with `Umb-Generated-Resource`). The repository reloads the parent itself with
  `UmbRequestReloadChildrenOfEntityEvent`. Kind `duplicateTo` adds a destination picker
  (`requestDuplicateTo({ unique, destination })`, read in the source).

## 17. Health checks

```csharp
[HealthCheck("2f7d1c9e-…", "Spike form storage",
    Description = "Checks the spike's form store and its recycle bin.", Group = "Jiron Form Builder (spike)")]
public sealed class SpikeHealthCheck(SpikeFormStore store) : HealthCheck
{
    public override Task<IEnumerable<HealthCheckStatus>> GetStatusAsync() => …;   // one status per finding
    public override HealthCheckStatus ExecuteAction(HealthCheckAction action) => …;
}
// a warning with an action:
new HealthCheckStatus($"{trashed} forms are in the recycle bin.") { ResultType = StatusResultType.Warning,
    Actions = [new HealthCheckAction("empty-bin", Id) { Name = "Empty the recycle bin", Description = "…" }] }
```

- Found without registration. Settings › Health Check showed a "Jiron Form Builder (spike)" card among Umbraco's
  groups; its page (the group name in the URL) lists each check with its description and one line per status,
  a tick for Success and a warning sign for Warning.
- An action renders as a box with its description and a green button named after it. **Clicking it ran
  `ExecuteAction` at once, with no confirmation,** and the check ran again ("The recycle bin is empty.").
- The texts are server strings shown as written, so the product translates them on the server and handles plurals
  itself (the spike's "1 forms are in the recycle bin." shows why).

## 18. Webhooks

```csharp
public sealed record SpikeEntriesApprovedNotification(IReadOnlyList<Guid> EntryIds, DateTime ApprovedUtc) : INotification;

[WebhookEvent("Spike entries approved", Constants.WebhookEvents.Types.Other)]
public sealed class SpikeEntriesApprovedWebhookEvent(IWebhookFiringService firing, IWebhookService webhooks,
    IOptionsMonitor<WebhookSettings> settings, IServerRoleAccessor roles)
    : WebhookEventBase<SpikeEntriesApprovedNotification>(firing, webhooks, settings, roles)
{
    public override string Alias => "jfbSpikeEntriesApproved";
    public override object? ConvertNotificationToRequestPayload(SpikeEntriesApprovedNotification n) =>
        new { entryIds = n.EntryIds, approvedUtc = n.ApprovedUtc };
}
// composer: builder.AddWebhookEvent<SpikeEntriesApprovedWebhookEvent>();
// on approve: await eventAggregator.PublishAsync(new SpikeEntriesApprovedNotification(ids, now), cancellationToken);
```

- Settings › Webhooks › Create listed "Spike entries approved" in the event picker after Umbraco's content and
  media events. A webhook to a local listener (port 5520) saved with the URL, the event and Enabled.
- Bulk-approving two entries in the collection published the notification. About nine seconds later the
  listener received one `POST` with the headers `Umb-Webhook-Event: jfbSpikeEntriesApproved`,
  `Umb-Webhook-RetryCount: 0` and `User-Agent: Umbraco-Cms/17.7.1`, and the body
  `{"entryIds":["…","…"],"approvedUtc":"…"}`. Delivery is a distributed job (every 10 s by default, with retries;
  settings under `Umbraco:CMS:Webhook`, read in the source).
- The body is exactly our payload; Umbraco adds no envelope. The event name in the picker is a server string,
  shown as written.

## 19. References ("used on")

```csharp
UdiParser.RegisterUdiType("jfb-spike-form", UdiType.GuidUdi);                 // once, in a composer
builder.DataValueReferenceFactories().Append<SpikeFormReferenceFactory>();

public sealed class SpikeFormReferenceFactory : IDataValueReferenceFactory, IDataValueReference
{
    public bool IsForEditor(IDataEditor? dataEditor) => dataEditor?.Alias == SpikeFormPicker.EditorAlias;
    public IDataValueReference GetDataValueReference() => this;
    public IEnumerable<UmbracoEntityReference> GetReferences(object? value) =>
        value is string text && Guid.TryParse(text, out var id)
            ? [new UmbracoEntityReference(new GuidUdi("jfb-spike-form", id), Constants.Conventions.RelationTypes.RelatedDocumentAlias)]
            : [];
}
```

- **Umbraco calls the factory on every save but keeps only references to nodes.** It stores references as
  relations between `umbracoNode` rows; for our form it logged at Debug level that the reference to
  `umb://jfb-spike-form/…` has no node id and cannot be saved as a relation. The relation service takes integer
  node ids, and the management API's "referenced by" endpoints cover only documents, media, members and data types.
- **The walk covers blocks.** `DataValueReferenceFactoryCollection.GetAllReferences(content.Properties, editors)`
  found the form both in a page's picker property and inside a Block List item. Called per property,
  `GetReferences(editor, values)` found the same and tells which property (`form`, `blocks`) and culture. Both
  exist in 17.5.0; `GetAllReferences` with a third argument is newer.
- **Our index, Umbraco's UI.** Handlers on content saved, published, moved to the recycle bin and deleted, plus a
  pass over all content at start-up, filled an in-memory index. The info app

  ```ts
  { type: 'workspaceInfoApp', kind: 'entityReferences', alias, name,
    meta: { referenceRepositoryAlias: REFERENCES }, conditions: [{ alias: 'Umb.Condition.WorkspaceAlias', match: WS }] }
  ```

  takes a repository implementing `UmbEntityReferenceRepository` (`requestReferencedBy(unique, skip, take)` and
  `requestAreReferenced`). Our endpoint returned items shaped like Umbraco's document reference
  (`$type: "DocumentReferenceResponseModel"`, `id`, `name`, `published`, `documentType { id, icon, alias, name }`,
  `variants`), mapped with `UmbManagementApiDataMapper` by `$type`. The form's Info view then showed Umbraco's own
  "Referenced by" box: "Spike blocks page" with a Draft badge and "Spike contact page", each with its
  document type's icon.
- An info app appears only where a view renders `<umb-extension-slot type="workspaceInfoApp">`. Umbraco's Info views
  do; a package's own Info view must include the slot.
- The decision is [ADR-0017](../decisions/0017-references.md).

## 20. A view on the member workspace

```ts
{ type: 'workspaceView', alias, name, element, weight: 50,
  meta: { label: '#jfbSpike_memberEntries', pathname: 'jfb-entries', icon: 'jfb-spike-icon-form' },
  conditions: [{ alias: 'Umb.Condition.WorkspaceAlias', match: 'Umb.Workspace.Member' },
               { alias: 'Umb.Condition.WorkspaceEntityIsNew', match: false }] }
```

- The member workspace gained a third tab, "Form entries" with the package icon, after Details and Info, at
  `…/member/edit/<key>/invariant/view/jfb-entries` (members are routed with a variant segment). The view read
  `UMB_MEMBER_WORKSPACE_CONTEXT` (`unique`, and `data.email`) and showed the member's email. In German the tab read
  "Formulareinträge".
- `WorkspaceEntityIsNew` with `match: false` keeps the tab off the create screen, where no member exists yet.
- The Members section opens on an empty page; members are listed under its "Members" menu item.

## 21. UFM components in block labels

```ts
{ type: 'ufmComponent', alias, name, api, meta: { alias: 'jfbFormName' } }
// the block's label in the Block List configuration: "Form: {jfbFormName: form}"

export class FormNameUfmComponent extends UmbUfmComponentBase {
  render(token: UfmToken) {
    return token.text ? `<ufm-jfb-spike-form-name ${super.getAttributes(token.text)}></ufm-jfb-spike-form-name>` : undefined;
  }
}
@customElement('ufm-jfb-spike-form-name')
export class FormNameUfmElement extends UmbUfmElementBase { /* consume UMB_UFM_RENDER_CONTEXT, look up the name, set this.value */ }
```

- The block in the content editor read "Form: Contact us" instead of the form's id; the element looked the name
  up through the item endpoint.
- **`render` is called unbound (17.7.1).** Umbraco hands `render` to the markdown parser without its instance, so
  `this.getAttributes(…)` threw "this.getAttributes is not a function" and the label stayed empty. `super` is
  bound at definition time and works; Umbraco's own components use it.
- **The tag must start with `ufm-`, `umb-` or `uui-`:** the label's HTML is sanitised, and other custom elements are
  removed (read in the source).
- `UMB_UFM_RENDER_CONTEXT.value` is the block's content (its properties plus `$settings` and `$index`); the element
  picks its property by the alias in the token.

## 22. Background jobs and the 17.5 floor

- **A distributed job:**

  ```csharp
  public sealed class SpikeStatisticsJob(IRuntimeState runtime, SpikeFormStore store, ILogger<SpikeStatisticsJob> logger)
      : IDistributedBackgroundJob
  {
      public string Name => "Jfb.Spike.Statistics";
      public TimeSpan Period => TimeSpan.FromMinutes(1);
      public Task ExecuteAsync() => ExecuteAsync(CancellationToken.None);   // still abstract in 17.5 to 17.7
      public Task ExecuteAsync(CancellationToken cancellationToken) { … }    // 17.5.0+
  }
  // builder.Services.AddSingleton<IDistributedBackgroundJob, SpikeStatisticsJob>();
  ```

  It logged once a minute on the sandbox. `AlignToClock` (17.6.2) is not used.
- **The floor build:** the whole spike, server and client, was built against Umbraco 17.5.0: the template from a
  17.5.0 hive, `Umbraco.Cms.*` 17.5.0 (confirmed in `project.assets.json`), `@umbraco-cms/backoffice` 17.5.0 and
  `@hey-api/openapi-ts` 0.99.0, with the throwaway-build settings, in `%TEMP%\jfb-floor-17.5.0`.

| Piece | On 17.5.0 | What to do |
| --- | --- | --- |
| Server code: management API, schema ids, section grant, property editor and converters, seeding, health check, webhook event, reference factory and walk, distributed job | builds, 0 errors | nothing |
| Client: every manifest and import of 1.2a and 1.2b | `tsc` clean after one change | nothing else |
| `workspaceView` kind `tree` (and its `meta.treeAlias`) | missing until 17.6.0 | own element hosting `<umb-tree alias .props=${{ hideTreeRoot, startNode, selectionConfiguration }}>`, with the tree alias in the element; it listed a folder's forms on 17.7.1 and compiles on 17.5.0. 17.5's tree has no toolbar or tree-action props |
| Manifest kinds `entityReferences`, `duplicate`, the menu item kind `tree` | present (read in its package) | nothing |
| `IDistributedBackgroundJob.AlignToClock` | missing until 17.6.2 | not used |
| UUI | 1.x (UUI 2 from 17.6) | the host supplies UUI at run time; the 18.3 smoke on a 17.5 site checks the screens |
| Cache busting of package files | none (from 17.6) | 18.3 checks an upgrade on a 17.5 site |
| `@hey-api/openapi-ts` peer range | `>=0.85.2` (17.7: `>=0.99.0`) | pin 0.99.x, which satisfies both |
| NuGet audit | `MessagePack` 3.1.7, `Microsoft.OpenApi` 2.4.1 (high), `System.Security.Cryptography.Pkcs` and `.Xml` 10.0.6 | more than on 17.7.1; both sets are listed in `NuGetAudit.props` (ADR-0024) |

- **So D3's floor holds:** nothing Jiron Form Builder needs is newer than 17.5.0. Step 3.2 already pins
  `@umbraco-cms/backoffice` to the minimum supported 17.x, and NuGet resolves a range such as `[17.5.0, 18.0.0)`
  to its lowest version, so the library compiles against 17.5.0's APIs while the demo site runs the latest 17.x.

## 23. What this means for Jiron Form Builder

- **Security:** the API uses its own section policy, never the template's Content placeholder; confirm and
  toast texts are Lit templates only (section 8); refusals are complete sentences (section 2).
- **Validation:** the server validates every field; the workspace context adds messages for views that are not
  open; the model keeps one key order for the unsaved-changes check.
- **Tree:** offer actions only when they can work (a non-empty folder), get the bin's `hasChildren` from the
  server, reload the move destination ourselves, show the picker root once.
- **Sidebar:** do not repeat the section label as the sidebar heading. ADR-0021 later kept Umbraco's own header,
  because it carries the root's "+" and "…"; the name appears there and in the top bar, never a third time.
- **Collections:** name each row's selection box, format dates through localization, and read paging from the
  collection rather than the URL.
- **Modals:** focus the first field on open.
- **Designer:** ADR-0016.
- **Property editors:** `PropertyCacheLevel.None` for anything that points at a form.
- **Width:** design every screen for a 620 px workspace.
- **Search:** one `globalSearch` tab whose results link to the form and name its folder. Its label stays as
  written in every language, like Umbraco's own tabs.
- **Localization:** en and de `localization` manifests, every manifest label a `#key`, plurals inside function
  values. Server strings (health checks, webhook event names, problem titles) are translated on the server.
- **Health checks:** only safe, repeatable actions, because an action runs without a confirmation.
- **Webhooks:** one `WebhookEventBase` per entry event; Umbraco sends our payload as it is, so the payloads are
  documented and kept stable.
- **"Used on":** our own index, shown with Umbraco's references info app (ADR-0017).
- **Members:** a "Form entries" view on the member workspace, hidden on the create screen.
- **UFM:** components call `super.getAttributes`, and their elements' tags start with `ufm-`.
- **The 17.5 floor:** no `tree` workspace-view kind (our own view hosts `<umb-tree>`), both `ExecuteAsync`
  overloads implemented, no `AlignToClock`; an upgrade on a 17.5 site is checked in 18.3.
- **API calls in tests:** from a signed-in page with the backoffice's placeholder bearer header; no test reads a
  token.
- **§3.4** was corrected in 1.2b: the client configuration, the template's `@hey-api/openapi-ts` range, test-site
  creation, the manifest types and building blocks, "used on", webhooks, localization, health checks and jobs.
