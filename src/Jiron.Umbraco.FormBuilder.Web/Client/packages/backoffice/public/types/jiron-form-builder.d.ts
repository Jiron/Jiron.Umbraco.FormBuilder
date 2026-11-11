/**
 * Jiron Form Builder's backoffice extension points (ROADMAP 15.1, section 4.9, ADR-0233), for a site's own extensions.
 * Shipped as /App_Plugins/JironFormBuilder/types/jiron-form-builder.d.ts; copy it into a project, or point a path of its
 * tsconfig at it. Jiron Form Builder's own backoffice compiles against this very file, so it is always what runs.
 *
 * Every name here is a string a manifest or a context token names: a site's extension needs no module of Jiron Form
 * Builder at run time, only Umbraco's own.
 */
import type { ManifestElement } from '@umbraco-cms/backoffice/extension-api';
import type { Observable } from '@umbraco-cms/backoffice/observable-api';

/** The form workspace's alias, which a site's `workspaceView` names in its `Umb.Condition.WorkspaceAlias` condition. */
export type JfbFormWorkspaceAlias = 'Jiron.FormBuilder.Workspace.Form';

/** The entity type of a form, which the form workspace's context gives as `getEntityType()`. */
export type JfbFormEntityType = 'jfb-form';

/**
 * What a site's workspace view on the form workspace reads of the open form, from Umbraco's workspace context:
 * `new UmbContextToken<UmbWorkspaceContext, JfbFormWorkspaceContext>('UmbWorkspaceContext', undefined,
 * (context): context is JfbFormWorkspaceContext => context.getEntityType() === 'jfb-form')`. It is read-only: a view
 * that changes a form does so through the management API, as the designer's do.
 */
export interface JfbFormWorkspaceContext {
  /** Always `jfb-form`. */
  getEntityType(): string;
  /** The form's key, once it is read. */
  getUnique(): string | null | undefined;
  /** The form as the last answer gave it: its key, alias and name among its facts; undefined before it is read. */
  readonly form: Observable<JfbFormFacts | undefined>;
  /** The draft's version number; null without a draft. */
  readonly draftVersion: Observable<number | null>;
  /** The live version's number; null with nothing published, undefined before the form is read. */
  readonly publishedVersion: Observable<number | null | undefined>;
}

/** What a workspace view can count on of the open form. */
export interface JfbFormFacts {
  readonly key: string;
  readonly alias: string;
  readonly name: string;
}

/** One choice a setting offers, with its label in the backoffice's language. */
export interface JfbSettingChoice {
  readonly value: string;
  readonly label: string;
}

/** A setting of a field type's or an action type's schema, as a custom editor gets it. */
export interface JfbSetting {
  /** The setting's name in the settings object, in camel case. */
  readonly name: string;
  /** How it is stored: `text`, `number`, `boolean`, `list`, `json`, ... as the schema names it. */
  readonly kind: string;
  /** For a list, how its items are stored. */
  readonly itemKind?: string | null;
  /** The editor's alias the setting names, which the manifest's `meta.editor` matches. */
  readonly editor: string;
  /** The setting's default value, as stored. */
  readonly default?: unknown;
  /** The choices the schema offers, if any. */
  readonly choices: readonly JfbSettingChoice[];
}

/**
 * The element of a `jfbSettingEditor`: a custom editor for one setting, drawn under the setting's label and help, which
 * the designer draws around it. It sets `value` and dispatches `change` (Umbraco's `UmbChangeEvent`, or any `Event`
 * named `change`) on itself; the designer then keeps `value` as the setting's value, which the server checks as it
 * checks every setting. Its own control carries the setting's label (`label`) as its accessible name.
 */
export interface JfbSettingEditorElement extends HTMLElement {
  /** The setting's value as stored, or undefined for none. */
  value: unknown;
  /** The setting. */
  setting: JfbSetting;
  /** The setting's label in the backoffice's language, its control's accessible name. */
  label: string;
  /** The language the designer edits texts in, such as `en-US`. */
  culture: string;
  /** Whether the value the editor last gave cannot be kept, which the designer says under it. */
  invalid: boolean;
}

/**
 * A custom editor for every setting whose schema names `meta.editor` as its editor, in a field type's settings and in
 * an action type's; C# names it as `[FormBuilderSetting("Colours", Editor = "acme.swatches")]`.
 */
export interface ManifestJfbSettingEditor extends ManifestElement<JfbSettingEditorElement> {
  type: 'jfbSettingEditor';
  meta: {
    /** The editor's alias a setting names. */
    editor: string;
  };
}

/** A field as the designer's preview of it gets it, in the language the designer shows. */
export interface JfbPreviewField {
  /** The field's key. */
  readonly key: string;
  /** The field type's alias, which the manifest's `meta.fieldType` matches. */
  readonly type: string;
  /** The field's alias, if it has one. */
  readonly alias?: string;
  /** The id its control takes, which the label above it names. */
  readonly id: string;
  /** The field's label, already drawn above the preview. */
  readonly label: string;
  /** Whether an answer is required. */
  readonly required: boolean;
  /** The type's settings with their effective values: the field's own, else the schema's defaults. */
  readonly settings: Readonly<Record<string, unknown>>;
}

/**
 * The element of a `jfbFieldPreview`: what the designer's canvas and preview draw in a field's place for a site's own
 * field type, between its label and help, which the designer draws around it. It draws, it takes no input: the canvas
 * makes it inert, and a click selects the field.
 */
export interface JfbFieldPreviewElement extends HTMLElement {
  /** The field. */
  field: JfbPreviewField;
  /** The language the designer shows, such as `en-US`. */
  culture: string;
}

/** The designer's preview of a site's own field type, by the type's alias. */
export interface ManifestJfbFieldPreview extends ManifestElement<JfbFieldPreviewElement> {
  type: 'jfbFieldPreview';
  meta: {
    /** The field type's alias, as its C# declaration names it. */
    fieldType: string;
  };
}

/**
 * What `<jfb-form>` writes in a field's place for a site's own field type, on a headless site (ADR-0128):
 * `customElements.whenDefined('jfb-form').then((form) => form.defineControl('colour', writeColour))`. It writes the
 * controls a visitor answers with: named as the field's `name` (a part as `name.part`), the first with the field's
 * `id`, described by `helpId` and `errorId`, as the Razor view `fields/<type>.cshtml` of the type writes them.
 */
export type JfbControlWriter = (field: JfbControlField, document: Document) => Node | null;

/** A field as `<jfb-form>` writes it, with what the element endpoint gave for the type (`control.settings`, `control.parts`). */
export interface JfbControlField {
  readonly key: string;
  readonly type: string;
  readonly alias?: string;
  readonly id: string;
  readonly name: string;
  readonly label?: string;
  readonly helpId: string;
  readonly errorId: string;
  readonly isRequired: boolean;
  readonly values: readonly string[];
  readonly control?: {
    readonly kind: 'custom';
    /** The type's public settings. */
    readonly settings?: unknown;
    /** The answer's parts by name, for a type whose answers have parts. */
    readonly parts: Readonly<Record<string, readonly string[]>>;
  };
}

/** `<jfb-form>`'s class, as `customElements.get('jfb-form')` gives it. */
export interface JfbFormElementConstructor extends CustomElementConstructor {
  /** Registers the control a site's own field type writes; forms already shown without it are shown again with it. */
  defineControl(type: string, write: JfbControlWriter): void;
}

declare global {
  interface UmbExtensionManifestMap {
    jfbSettingEditor: ManifestJfbSettingEditor;
    jfbFieldPreview: ManifestJfbFieldPreview;
  }
}
