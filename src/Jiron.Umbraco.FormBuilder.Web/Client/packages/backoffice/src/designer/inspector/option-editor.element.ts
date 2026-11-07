// The inspector's option editor (ROADMAP 9.4b, IA 5.1, ADR-0172): where a choice field's options come from, its own
// options, and a grid's rows. "Options from" picks the field itself, an option list, a standard list, pages of the
// site, a data type (ADR-0173), the country list or a JSON address; a kind's configuration is drawn by the generic renderer over its served schema. Each own option
// is a row with its handle, its label in the language shown and a button for its details: what entries store, a place
// limit, a description and an image on cards, and the answers of the field it depends on that it is shown for. A row
// moves by its handle with the pointer, or with Alt+Up and Alt+Down, which says where it went; a press on the handle
// that never became a drag opens its "Place in the list", as an outcome's and an action's, so a pointer moves it without
// dragging (WCAG 2.5.7). "Paste options..." adds one option a line. Every change goes to the inspector, which makes it
// one step to undo.
import {
  html,
  live,
  nothing,
  repeat,
  type PropertyValues,
  type TemplateResult,
} from '@umbraco-cms/backoffice/external/lit';
import { UmbLitElement } from '@umbraco-cms/backoffice/lit-element';
import { UMB_MEDIA_PICKER_MODAL } from '@umbraco-cms/backoffice/media';
import { umbOpenModal } from '@umbraco-cms/backoffice/modal';
import type {
  FieldSettingResponseModel,
  FieldTypeResponseModel,
  OptionListItemResponseModel,
  OptionSourcesResponseModel,
} from '../../api';
import type { DesignField, LocalizedText } from '../definition';
import { dragFrom } from '../drag/pointer-drag';
import { textIn } from '../preview/field-models';
import { boxValue } from './box-value';
import { readOptionLists, readOptionSources } from './option-data';
import {
  isValue,
  itemsOf,
  moved,
  newOption,
  optionsOf,
  pasteOptions,
  pasteRows,
  repeats,
  replaced,
  rowKeys,
  rowsOf,
  sourceOf,
  withItems,
  withMember,
  withOptionProperty,
  withOptions,
  withRows,
  withSource,
  type DesignOption,
  type DesignRow,
  type OptionSourceKind,
} from './option-values';
import { problemText, settingEditor, type EditorContext } from './setting-editor';
import { ownText, valueOf, withKey, withText, type Parsed } from './setting-values';

/** How the editor hands a change to the inspector: the field's update, what Undo calls it, and a typed box. */
export type OptionChange = (update: (field: DesignField) => DesignField, name: string, typed?: string) => void;

/** The kinds "Options from" offers, in its order (IA 5.1, ADR-0173). */
const OFFERED: readonly OptionSourceKind[] = ['items', 'list', 'code', 'content', 'dataType', 'countries', 'url'];

/** Where a drag of a row is: the row taken and the place it would go, before the row at `to`. */
interface RowDrag {
  readonly from: number;
  readonly to: number;
}

export class OptionEditorElement extends UmbLitElement {
  static override properties = {
    field: { attribute: false },
    type: { attribute: false },
    culture: { attribute: false },
    defaultCulture: { attribute: false },
    parents: { attribute: false },
    sources: { attribute: false },
    lists: { attribute: false },
    edits: {},
    cards: { attribute: false },
    change: { attribute: false },
    _open: { state: true },
    _pasting: { state: true },
    _said: { state: true },
    _problems: { state: true },
    _drag: { state: true },
    _placing: { state: true },
  };

  declare field: DesignField;
  declare type: FieldTypeResponseModel;
  declare culture: string;
  declare defaultCulture: string;
  /** The choice fields before this one, which its options may depend on. */
  declare parents: readonly DesignField[];
  /** The kinds and code sources the site serves, or null while they are read. */
  declare sources: OptionSourcesResponseModel | null;
  /** The library's option lists, or null while they are read. */
  declare lists: readonly OptionListItemResponseModel[] | null;
  /** What it edits: the options, or a grid's rows. */
  declare edits: 'options' | 'rows';
  /** Whether the field's options show as cards, its own layout or the form's: they then take a description and an image. */
  declare cards: boolean;
  declare change: OptionChange;
  /** The rows whose details are open, by an option's value or a row's key. */
  declare private _open: ReadonlySet<string>;
  declare private _pasting: boolean;
  declare private _said: string;
  declare private _problems: ReadonlyMap<string, string>;
  declare private _drag: RowDrag | null;
  /** The row whose "Place in the list" is open, by an option's value or a row's key. */
  declare private _placing: string | null;

  constructor() {
    super();
    this.parents = [];
    this.sources = null;
    this.lists = null;
    this.edits = 'options';
    this.cards = false;
    this.culture = '';
    this.defaultCulture = '';
    this.change = () => undefined;
    this._open = new Set();
    this._pasting = false;
    this._said = '';
    this._problems = new Map();
    this._drag = null;
    this._placing = null;
  }

  #reading = { sources: false, lists: false };

  // What takes the focus once the inspector hands the changed field back: the change goes through the Design view and
  // its history first, and the rows drawn again after it may have moved, so the focus waits for them. A change that
  // never comes back takes nothing with it after two seconds.
  #afterChange: { readonly focus: () => void; readonly until: number } | null = null;

  #focusAfterChange(focus: () => void) {
    this.#afterChange = { focus, until: performance.now() + 2000 };
  }

  // Light DOM: the inspector's styles and its focus handling reach every box, and the canvas's drag finds the rows.
  protected override createRenderRoot(): HTMLElement {
    return this;
  }

  protected override willUpdate(changed: PropertyValues) {
    super.willUpdate(changed);
    // The kinds and sources once; the lists when a field takes its options from one.
    if (this.sources === null && !this.#reading.sources) {
      this.#reading.sources = true;
      void readOptionSources(this).then((read) => (this.sources = read ?? { kinds: [], sources: [] }));
    }
    if (this.edits === 'options' && sourceOf(this.field) === 'list' && this.lists === null && !this.#reading.lists) {
      this.#reading.lists = true;
      void readOptionLists(this).then((read) => (this.lists = read));
    }
    if (changed.has('field') && (changed.get('field') as DesignField | undefined)?.key !== this.field.key) {
      this._open = new Set();
      this._pasting = false;
      this._problems = new Map();
    }
  }

  protected override updated(changed: PropertyValues) {
    super.updated(changed);
    const pending = this.#afterChange;
    if (!pending || !changed.has('field')) return;
    this.#afterChange = null;
    if (performance.now() <= pending.until) pending.focus();
  }

  get #isDefault(): boolean {
    return this.culture.toLowerCase() === this.defaultCulture.toLowerCase();
  }

  #term = (key: string, ...args: unknown[]) => this.localize.term(`jfbInspector_${key}`, ...args);

  #say = (text: string | null | undefined) => (text ? this.localize.string(text) : '');

  #announce(text: string) {
    // The same words twice are still said: the region is emptied first.
    this._said = '';
    requestAnimationFrame(() => (this._said = text));
  }

  #problem(id: string, text: string | null) {
    if ((this._problems.get(id) ?? null) === text) return;
    const next = new Map(this._problems);
    if (text === null) next.delete(id);
    else next.set(id, text);
    this._problems = next;
  }

  #labelOf(texts: LocalizedText | null | undefined, fallback: string): string {
    return textIn(texts, this.culture) || textIn(texts, this.defaultCulture) || fallback;
  }

  #placeholderOf(texts: LocalizedText | undefined): string | typeof nothing {
    return this.#isDefault ? nothing : textIn(texts, this.defaultCulture) || nothing;
  }

  #text(texts: LocalizedText | undefined, text: string): LocalizedText | undefined {
    return withText(texts, this.culture, text, this.#isDefault);
  }

  // A text's box holds the shown language's own words: in another language, empty where only the default has some.
  #own(texts: LocalizedText | undefined): string {
    return this.#isDefault ? textIn(texts, this.culture) : ownText(texts, this.culture);
  }

  protected override render(): TemplateResult {
    return html`${this.edits === 'rows' ? this.#rows() : this.#options()}
      <p class="visually-hidden" role="status">${this._said}</p>`;
  }

  // ---- Where the options come from ----------------------------------------------------------------------------------

  #offered(): OptionSourceKind[] {
    const kinds = new Set((this.sources?.kinds ?? []).map((kind) => kind.kind));
    return OFFERED.filter(
      (source) =>
        source === 'items' ||
        source === 'list' ||
        (source === 'code' ? kinds.has('code') && (this.sources?.sources.length ?? 0) > 0 : kinds.has(source)),
    );
  }

  #options(): TemplateResult {
    const source = sourceOf(this.field);
    const offered = this.#offered();
    if (!offered.includes(source)) offered.push(source);
    // A grid's options are its columns, and its words say so: "Columns from", "Add column".
    const name = this.#term(this.type.features.includes('rows') ? 'options_columnsFrom' : 'options_from');
    return html`<div class="item">
        <label class="label" for="options-source">${name}</label>
        <select
          id="options-source"
          @change=${(event: Event) => {
            const next = (event.target as HTMLSelectElement).value as OptionSourceKind;
            this._open = new Set();
            this._pasting = false;
            this.change((field) => withSource(field, next), name);
          }}
        >
          ${offered.map(
            (each) =>
              html`<option value=${each} ?selected=${each === source}>${this.#term(`options_from_${each}`)}</option>`,
          )}
        </select>
      </div>
      ${this.#from(source)} ${this.#dependsOn()}`;
  }

  #from(source: OptionSourceKind): TemplateResult | typeof nothing {
    switch (source) {
      case 'items':
        return this.#items();
      case 'list':
        return this.#list();
      case 'code':
        return this.#code();
      default:
        return this.#config(source);
    }
  }

  #list(): TemplateResult {
    const name = this.#term('options_list');
    const chosen = optionsOf(this.field).list ?? '';
    if (this.lists === null) return html`<div class="item"><uui-loader></uui-loader></div>`;
    if (this.lists.length === 0) return html`<p class="item help">${this.#term('options_noLists')}</p>`;
    return html`<div class="item">
      <label class="label" for="options-list">${name}</label>
      <select
        id="options-list"
        @change=${(event: Event) => {
          const list = (event.target as HTMLSelectElement).value || undefined;
          this.change((field) => withOptions(field, { ...optionsOf(field), list }), name);
        }}
      >
        <option value="" ?selected=${chosen === ''}>${this.#term('options_listNone')}</option>
        ${this.lists.map(
          (list) =>
            html`<option value=${list.key} ?selected=${list.key === chosen}>
              ${this.#term(
                'options_listItem',
                list.name,
                list.count === null || list.count === undefined
                  ? this.#term(`options_listFrom_${list.kind ?? 'unknown'}`)
                  : this.#term('options_count', list.count),
              )}
            </option>`,
        )}
      </select>
    </div>`;
  }

  #code(): TemplateResult {
    const name = this.#term('options_standard');
    const chosen = optionsOf(this.field).config?.['source'];
    return html`<div class="item">
      <label class="label" for="options-code">${name}</label>
      <select
        id="options-code"
        @change=${(event: Event) => {
          const value = (event.target as HTMLSelectElement).value;
          this.change(
            (field) => withOptions(field, { ...optionsOf(field), config: value ? { source: value } : {} }),
            name,
          );
        }}
      >
        <option value="" ?selected=${!chosen}>${this.#term('options_standardNone')}</option>
        ${(this.sources?.sources ?? []).map(
          (each) =>
            html`<option value=${each.name} ?selected=${each.name === chosen}>${this.#say(each.label)}</option>`,
        )}
      </select>
    </div>`;
  }

  // A kind's configuration, drawn by the generic renderer over the kind's served schema, at `options.config`.
  #config(source: OptionSourceKind): TemplateResult | typeof nothing {
    const kind = this.sources?.kinds.find((each) => each.kind === source);
    if (!kind) return this.sources === null ? html`<div class="item"><uui-loader></uui-loader></div>` : nothing;
    const config = optionsOf(this.field).config ?? {};
    const context: EditorContext = {
      say: this.#say,
      term: this.#term,
      culture: this.culture,
      defaultCulture: this.defaultCulture,
      problems: this._problems,
      set: (path, setting, parsed, done) => this.#setConfig(path, setting, parsed, done),
    };
    return html`${kind.settings.map((setting) =>
      settingEditor(context, setting, valueOf(config, setting), `config.${setting.name}`),
    )}`;
  }

  #setConfig(path: string, setting: FieldSettingResponseModel, parsed: Parsed, done: boolean) {
    const id = `setting-${path.replace(/[^a-zA-Z0-9]/g, '-')}`;
    if (!parsed.ok) {
      if (done) this.#problem(id, problemText(this.#term, parsed.problem));
      return;
    }
    this.#problem(id, null);
    const value =
      parsed.value === undefined || JSON.stringify(parsed.value) === JSON.stringify(setting.default)
        ? undefined
        : parsed.value;
    this.change(
      (field) => {
        const options = optionsOf(field);
        return withOptions(field, { ...options, config: withKey(options.config ?? {}, setting.name, value) });
      },
      this.#say(setting.label),
      done ? undefined : path,
    );
  }

  #dependsOn(): TemplateResult | typeof nothing {
    if (!this.type.features.includes('needsOptions') || this.parents.length === 0) return nothing;
    const name = this.#term('options_dependsOn');
    const parent = optionsOf(this.field).parent ?? '';
    return html`<div class="item">
      <label class="label" for="options-parent">${name}</label>
      <p class="help" id="options-parent-help">${this.#term('options_dependsOnHelp')}</p>
      <select
        id="options-parent"
        aria-describedby="options-parent-help"
        @change=${(event: Event) => {
          const key = (event.target as HTMLSelectElement).value || undefined;
          // Answers of the field it depended on mean nothing to another, or to none: each option is shown for all again.
          this.change(
            (field) =>
              withOptions(field, {
                ...optionsOf(field),
                parent: key,
                items: itemsOf(field).map((item) => withOptionProperty(item, 'parentValues', undefined)),
              }),
            name,
          );
        }}
      >
        <option value="" ?selected=${parent === ''}>${this.#term('options_dependsOnNothing')}</option>
        ${this.parents.map(
          (each) =>
            html`<option value=${each.key} ?selected=${each.key === parent}>
              ${this.#labelOf(each.label, each.alias ?? each.type)}
            </option>`,
        )}
      </select>
    </div>`;
  }

  // ---- A field's own options ----------------------------------------------------------------------------------------

  #items(): TemplateResult {
    const items = itemsOf(this.field);
    const grid = this.type.features.includes('rows');
    const heading = this.#term(grid ? 'options_columns' : 'options_options');
    const drag = this._drag;
    const keys = rowKeys(items.map((item) => item.value));
    return html`<div class="item item-options">
      <p class="label" id="options-heading">${heading}</p>
      ${
        items.length === 0
          ? html`<p class="help">${this.#term('options_none')}</p>`
          : html`<ol class="option-rows" aria-labelledby="options-heading">
              ${repeat(
                items,
                (_item, at) => keys[at],
                (item, at) => this.#option(item, at, items.length, drag),
              )}
            </ol>`
      }
      <div class="row-actions">
        <uui-button
          class="add"
          look="secondary"
          label=${this.#term(grid ? 'options_addColumn' : 'options_add')}
          @click=${() => this.#addOption()}
        >
          <uui-icon name="icon-add" aria-hidden="true"></uui-icon
          >${this.#term(grid ? 'options_addColumn' : 'options_add')}
        </uui-button>
        <uui-button
          class="paste-toggle"
          look="secondary"
          label=${this.#term('options_paste')}
          aria-expanded=${this._pasting ? 'true' : 'false'}
          @click=${() => {
            this._pasting = !this._pasting;
            if (this._pasting)
              void this.updateComplete.then(() => this.querySelector<HTMLElement>('#options-paste')?.focus());
          }}
        ></uui-button>
      </div>
      ${this._pasting ? this.#paste('options') : nothing}
    </div>`;
  }

  #option(item: DesignOption, at: number, count: number, drag: RowDrag | null): TemplateResult {
    const label = this.#labelOf(item.label, item.value);
    const open = this._open.has(item.value);
    const details = `option-${at}-details`;
    const name = this.#term('options_options');
    return html`<li
      class="option-row"
      data-at=${at}
      ?data-line-before=${drag !== null && drag.to === at && drag.to !== drag.from && drag.to !== drag.from + 1}
      ?data-line-after=${drag !== null && at === count - 1 && drag.to === count && drag.from !== count - 1}
      ?data-dragged=${drag?.from === at}
    >
      <div class="row-line">
        <button
          type="button"
          class="row-handle"
          aria-label=${this.#term('options_move', label)}
          aria-expanded=${count < 2 ? nothing : this._placing === item.value ? 'true' : 'false'}
          aria-controls=${count < 2 ? nothing : `option-${at}-place`}
          @pointerdown=${(event: PointerEvent) => this.#dragRow(event, at, label)}
          @keydown=${(event: KeyboardEvent) => this.#keyMove(event, at, count, label)}
          @click=${() => this.#togglePlace(item.value, at, count)}
        >
          <uui-icon name="icon-grip" aria-hidden="true"></uui-icon>
        </button>
        <input
          class="row-label"
          type="text"
          aria-label=${this.#term('options_labelOf', at + 1)}
          lang=${this.culture || nothing}
          placeholder=${this.#placeholderOf(item.label)}
          .value=${boxValue(this.#own(item.label))}
          @input=${(event: Event) => {
            const text = (event.target as HTMLInputElement).value;
            this.#updateOption(
              item.value,
              (option) => withOptionProperty(option, 'label', this.#text(option.label, text)),
              name,
              `option-${item.value}-label`,
            );
          }}
        />
        <button
          type="button"
          class="row-tool"
          aria-expanded=${open ? 'true' : 'false'}
          aria-controls=${details}
          aria-label=${this.#term('options_details', label)}
          @click=${() => {
            const next = new Set(this._open);
            if (open) next.delete(item.value);
            else next.add(item.value);
            this._open = next;
          }}
        >
          <uui-icon name=${open ? 'icon-navigation-up' : 'icon-navigation-down'} aria-hidden="true"></uui-icon>
        </button>
        <button
          type="button"
          class="row-tool"
          aria-label=${this.#term('options_remove', label)}
          @click=${() => this.#removeOption(at, label)}
        >
          <uui-icon name="icon-trash" aria-hidden="true"></uui-icon>
        </button>
      </div>
      ${this._placing === item.value ? this.#place(`option-${at}-place`, at, count, label) : nothing}
      ${open ? html`<div class="row-details" id=${details}>${this.#details(item, at)}</div>` : nothing}
    </li>`;
  }

  #details(item: DesignOption, at: number): TemplateResult {
    const name = this.#term('options_options');
    const cards = this.cards;
    const places = this.type.features.includes('places');
    const id = (part: string) => `option-${at}-${part}`;
    const problem = this._problems.get(id('value'));
    return html`<div class="item">
        <label class="label" for=${id('value')}>${this.#term('options_value')}</label>
        <p class="help" id=${id('value-help')}>${this.#term('options_valueHelp')}</p>
        <input
          id=${id('value')}
          type="text"
          maxlength="200"
          aria-describedby=${problem ? `${id('value-help')} ${id('value-why')}` : id('value-help')}
          aria-invalid=${problem ? 'true' : 'false'}
          .value=${boxValue(item.value, problem !== undefined)}
          @change=${(event: Event) => this.#setValue(item.value, (event.target as HTMLInputElement).value, id('value'))}
        />
        ${problem ? html`<p class="why" id=${id('value-why')}>${problem}</p>` : nothing}
      </div>
      ${
        cards
          ? html`<div class="item">
                <label class="label" for=${id('description')}>${this.#term('options_description')}</label>
                <textarea
                  id=${id('description')}
                  rows="2"
                  lang=${this.culture || nothing}
                  placeholder=${this.#placeholderOf(item.description)}
                  .value=${boxValue(this.#own(item.description))}
                  @input=${(event: Event) => {
                    const text = (event.target as HTMLTextAreaElement).value;
                    this.#updateOption(
                      item.value,
                      (option) => withOptionProperty(option, 'description', this.#text(option.description, text)),
                      name,
                      id('description'),
                    );
                  }}
                ></textarea>
              </div>
              <div class="item">
                <p class="label">${this.#term('options_image')}</p>
                ${item.image ? html`<p class="help">${this.#term('options_imageChosen')}</p>` : nothing}
                <div class="row-actions">
                  <uui-button
                    look="secondary"
                    label=${this.#term('options_chooseImage')}
                    @click=${() => this.#chooseImage(item.value)}
                  ></uui-button>
                  ${
                    item.image
                      ? html`<uui-button
                          look="secondary"
                          label=${this.#term('options_removeImage')}
                          @click=${() => this.#updateOption(item.value, (option) => withOptionProperty(option, 'image', undefined), name)}
                        ></uui-button>`
                      : nothing
                  }
                </div>
              </div>`
          : nothing
      }
      ${places ? this.#places(item, id('places')) : nothing} ${this.#shownFor(item, id('shown'))}`;
  }

  #places(item: DesignOption, id: string): TemplateResult {
    const problem = this._problems.get(id);
    return html`<div class="item">
      <label class="label" for=${id}>${this.#term('options_places')}</label>
      <p class="help" id=${`${id}-help`}>${this.#term('options_placesHelp')}</p>
      <input
        id=${id}
        type="text"
        inputmode="numeric"
        aria-describedby=${problem ? `${id}-help ${id}-why` : `${id}-help`}
        aria-invalid=${problem ? 'true' : 'false'}
        .value=${boxValue(item.places === undefined ? '' : String(item.places), problem !== undefined)}
        @change=${(event: Event) => {
          const text = (event.target as HTMLInputElement).value.trim();
          const places = text === '' ? undefined : /^\d{1,7}$/.test(text) ? Number(text) : NaN;
          if (places !== undefined && (Number.isNaN(places) || places > 1_000_000)) {
            this.#problem(id, this.#term('options_placesInvalid'));
            return;
          }
          this.#problem(id, null);
          this.#updateOption(
            item.value,
            (option) => withOptionProperty(option, 'places', places),
            this.#term('options_places'),
          );
        }}
      />
      ${problem ? html`<p class="why" id=${`${id}-why`}>${problem}</p>` : nothing}
    </div>`;
  }

  // The answers of the field these options depend on that an option is shown for: ticked from its own options, or one a
  // line where its options come from elsewhere.
  #shownFor(item: DesignOption, id: string): TemplateResult | typeof nothing {
    const parentKey = optionsOf(this.field).parent;
    const parent = parentKey ? this.parents.find((each) => each.key === parentKey) : undefined;
    if (!parent) return nothing;
    const name = this.#term('options_shownFor');
    const chosen = new Set(item.parentValues ?? []);
    const parentName = this.#labelOf(parent.label, parent.alias ?? parent.type);
    const write = (values: readonly string[]) =>
      this.#updateOption(item.value, (option) => withOptionProperty(option, 'parentValues', values), name);
    if (sourceOf(parent) === 'items') {
      // An answer the parent no longer offers stays ticked under its value, so it can be unticked.
      const offered = itemsOf(parent).map((option) => ({
        value: option.value,
        label: this.#labelOf(option.label, option.value),
      }));
      const gone = [...chosen]
        .filter((value) => !offered.some((option) => option.value === value))
        .map((value) => ({ value, label: value }));
      const answers = [...offered, ...gone];
      return html`<fieldset class="item item-group" aria-describedby=${`${id}-help`}>
        <legend class="label">${name}</legend>
        <p class="help" id=${`${id}-help`}>${this.#term('options_shownForHelp', parentName)}</p>
        ${answers.map(
          (option, at) =>
            html`<label class="check" for=${`${id}-${at}`}>
              <input
                id=${`${id}-${at}`}
                type="checkbox"
                .checked=${chosen.has(option.value)}
                @change=${(event: Event) => {
                  const next = new Set(chosen);
                  if ((event.target as HTMLInputElement).checked) next.add(option.value);
                  else next.delete(option.value);
                  write(answers.map((each) => each.value).filter((value) => next.has(value)));
                }}
              />
              ${option.label}
            </label>`,
        )}
      </fieldset>`;
    }
    return html`<div class="item">
      <label class="label" for=${id}>${name}</label>
      <p class="help" id=${`${id}-help`}>${this.#term('options_shownForLines', parentName)}</p>
      <textarea
        id=${id}
        rows="3"
        aria-describedby=${`${id}-help`}
        .value=${boxValue([...chosen].join('\n'))}
        @change=${(event: Event) =>
          write(
            [
              ...new Set((event.target as HTMLTextAreaElement).value.split(/\r\n|\r|\n/).map((line) => line.trim())),
            ].filter((line) => isValue(line)),
          )}
      ></textarea>
    </div>`;
  }

  #updateOption(value: string, update: (option: DesignOption) => DesignOption, name: string, typed?: string) {
    this.change(
      (field) => {
        const items = itemsOf(field);
        const at = items.findIndex((each) => each.value === value);
        return at < 0 ? field : withItems(field, replaced(items, at, update(items[at] as DesignOption)));
      },
      name,
      typed,
    );
  }

  #setValue(before: string, typed: string, id: string) {
    const items = itemsOf(this.field);
    const at = items.findIndex((each) => each.value === before);
    if (typed === before || at < 0) {
      this.#problem(id, null);
      return;
    }
    if (!isValue(typed)) {
      this.#problem(id, this.#term('options_valueInvalid'));
      return;
    }
    if (repeats(items, typed, at)) {
      this.#problem(id, this.#term('options_valueRepeats'));
      return;
    }
    this.#problem(id, null);
    // The details stay open on the option under its new value, and its box keeps the focus: the row is drawn anew.
    if (this._open.has(before)) this._open = new Set([...this._open].map((each) => (each === before ? typed : each)));
    this.#focusAfterChange(() => this.querySelector<HTMLElement>(`#${id}`)?.focus());
    this.#updateOption(before, (option) => ({ ...option, value: typed }), this.#term('options_value'));
  }

  async #chooseImage(value: string) {
    const answer = await umbOpenModal(this, UMB_MEDIA_PICKER_MODAL, { data: { multiple: false } }).catch(
      () => undefined,
    );
    const image = answer?.selection.find((each): each is string => typeof each === 'string');
    if (!image) return;
    this.#updateOption(value, (option) => withOptionProperty(option, 'image', image), this.#term('options_image'));
  }

  #addOption() {
    const items = itemsOf(this.field);
    const label = this.localize.term('jfbDesigner_newOption', items.length + 1);
    const option = newOption(label, items, this.culture, this.#isDefault);
    // The new option's label takes the focus, its words selected to be typed over.
    this.#focusAfterChange(() => this.#focusLastLabel());
    this.change((field) => withItems(field, [...itemsOf(field), option]), this.#term('options_add'));
  }

  #focusLastLabel() {
    const boxes = this.querySelectorAll<HTMLInputElement>('.row-label');
    const box = boxes[boxes.length - 1];
    box?.focus();
    box?.select();
  }

  // The handle of the row now at `at`, or the one before, or the first button below the rows where none is left.
  #focusRowAt(at: number) {
    const handles = this.querySelectorAll<HTMLElement>('.row-handle');
    (handles[Math.min(at, handles.length - 1)] ?? this.querySelector<HTMLElement>('.add'))?.focus();
  }

  #removeOption(at: number, label: string) {
    // The focus goes to the row now in its place, or the one before, or Add option.
    this.#focusAfterChange(() => this.#focusRowAt(at));
    this.change(
      (field) =>
        withItems(
          field,
          itemsOf(field).filter((_each, index) => index !== at),
        ),
      this.#term('options_removeNamed', label),
    );
    this.#announce(this.#term('options_removed', label));
  }

  // ---- Moving a row -------------------------------------------------------------------------------------------------

  // A press on the handle that never became a drag opens the row's "Place in the list", or closes it; a list of one
  // row has no other place.
  #togglePlace(key: string, at: number, count: number) {
    if (count < 2) return;
    this._placing = this._placing === key ? null : key;
    if (this._placing !== null)
      void this.updateComplete.then(() => this.querySelector<HTMLElement>(`#${this.#placeIdOf(at)}-select`)?.focus());
  }

  #placeIdOf(at: number): string {
    return `${this.edits === 'rows' ? 'row' : 'option'}-${at}-place`;
  }

  // Where the row is in the list, which a pointer chooses with single presses (WCAG 2.5.7); choosing moves it there,
  // its handle keeping the focus, and Escape closes it.
  #place(id: string, at: number, count: number, label: string): TemplateResult {
    const handle = () => this.querySelector<HTMLElement>(`.option-rows > .option-row[data-at="${at}"] .row-handle`);
    return html`<div class="row-place" id=${id}>
      <label class="label" for=${`${id}-select`}>${this.#term('options_place')}</label>
      <select
        id=${`${id}-select`}
        .value=${live(String(at))}
        @change=${(event: Event) => {
          const to = Number((event.target as HTMLSelectElement).value);
          this._placing = null;
          if (to !== at) this.#move(at, to, label);
          else void this.updateComplete.then(() => handle()?.focus());
        }}
        @keydown=${(event: KeyboardEvent) => {
          if (event.key !== 'Escape') return;
          // The inspector's own Escape leaves the field; here it closes the place.
          event.preventDefault();
          event.stopPropagation();
          this._placing = null;
          void this.updateComplete.then(() => handle()?.focus());
        }}
      >
        ${Array.from(
          { length: count },
          (_, place) =>
            html`<option value=${String(place)} ?selected=${place === at}>
              ${this.#term('options_placeOf', place + 1, count)}
            </option>`,
        )}
      </select>
    </div>`;
  }

  #keyMove(event: KeyboardEvent, at: number, count: number, label: string) {
    if (!event.altKey || (event.key !== 'ArrowUp' && event.key !== 'ArrowDown')) return;
    // The canvas's own Alt+Up and Alt+Down move the selected field; here they move the row.
    event.preventDefault();
    event.stopPropagation();
    const to = event.key === 'ArrowUp' ? at - 1 : at + 1;
    if (to < 0 || to >= count) {
      this.#announce(this.#term('options_edge'));
      return;
    }
    this.#move(at, to, label);
  }

  #move(from: number, to: number, label: string) {
    const rows = this.edits === 'rows';
    const count = rows ? rowsOf(this.field).length : itemsOf(this.field).length;
    // The moved row's handle keeps the focus, or takes it after a drop.
    this.#focusAfterChange(() => this.#focusRowAt(to));
    this.change(
      (field) =>
        rows ? withRows(field, moved(rowsOf(field), from, to)) : withItems(field, moved(itemsOf(field), from, to)),
      this.#term('options_moveNamed', label),
    );
    this.#announce(this.#term('options_moved', label, to + 1, count));
  }

  // The place a pointer at y would put a row: before the first row whose middle lies below it, or after the last.
  #placeAt(y: number): number {
    const rows = [...this.querySelectorAll<HTMLElement>('.option-rows > .option-row')];
    const below = rows.findIndex((row) => {
      const box = row.getBoundingClientRect();
      return y < box.top + box.height / 2;
    });
    return below < 0 ? rows.length : below;
  }

  #dragRow(event: PointerEvent, from: number, label: string) {
    const handle = event.currentTarget as Element;
    dragFrom(event, handle, {
      begin: () => (this._drag = { from, to: from }),
      over: (_x, y) => {
        const to = this.#placeAt(y);
        if (this._drag?.to !== to) this._drag = { from, to };
      },
      drop: (_x, y) => {
        const to = this.#placeAt(y);
        this._drag = null;
        // Before itself or the row after it is where it is.
        if (to === from || to === from + 1) return;
        this.#move(from, to > from ? to - 1 : to, label);
      },
      cancel: () => (this._drag = null),
      scroller: () => this.closest<HTMLElement>('.body'),
    });
  }

  // ---- Pasting --------------------------------------------------------------------------------------------------------

  #paste(part: 'options' | 'rows'): TemplateResult {
    const id = `${part}-paste`;
    return html`<div class="paste">
      <label class="label" for=${id}>${this.#term(`${part}_pasteLabel`)}</label>
      <textarea id=${id} rows="5" lang=${this.culture || nothing}></textarea>
      <div class="row-actions">
        <uui-button
          look="primary"
          label=${this.#term(`${part}_pasteAdd`)}
          @click=${() => this.#addPasted(part, id)}
        ></uui-button>
        <uui-button
          look="secondary"
          label=${this.#term('options_cancel')}
          @click=${() => {
            this._pasting = false;
            void this.updateComplete.then(() => this.querySelector<HTMLElement>('.paste-toggle')?.focus());
          }}
        ></uui-button>
      </div>
    </div>`;
  }

  #addPasted(part: 'options' | 'rows', id: string) {
    const text = this.querySelector<HTMLTextAreaElement>(`#${id}`)?.value ?? '';
    const culture = this.culture;
    const isDefault = this.#isDefault;
    if (part === 'rows') {
      const before = rowsOf(this.field).length;
      const rows = pasteRows(text, rowsOf(this.field), culture, isDefault, () => crypto.randomUUID());
      this.change((field) => withRows(field, [...rowsOf(field), ...rows.slice(before)]), this.#term('rows_pasteAdd'));
      this.#announce(this.#term('rows_pasted', rows.length - before));
    } else {
      const pasted = pasteOptions(text, itemsOf(this.field), culture, isDefault);
      this.change(
        (field) => withItems(field, pasteOptions(text, itemsOf(field), culture, isDefault).items),
        this.#term('options_pasteAdd'),
      );
      this.#announce(this.#term('options_pasted', pasted.added, pasted.repeated));
    }
    // The focus goes back to the button that opened the panel.
    this._pasting = false;
    void this.updateComplete.then(() => this.querySelector<HTMLElement>('.paste-toggle')?.focus());
  }

  // ---- A grid's rows ------------------------------------------------------------------------------------------------

  #rows(): TemplateResult {
    const rows = rowsOf(this.field);
    const drag = this._drag;
    const keys = rowKeys(rows.map((row) => row.key));
    // "Needs an answer" is said once, over the rows' boxes, and each box names its row.
    return html`<div class="item item-options">
      <div class="rows-head">
        <p class="label" id="rows-heading">${this.#term('rows_rows')}</p>
        ${rows.length > 0 ? html`<span class="rows-caption" aria-hidden="true">${this.#term('rows_required')}</span>` : nothing}
      </div>
      ${
        rows.length === 0
          ? html`<p class="help">${this.#term('rows_none')}</p>`
          : html`<ol class="option-rows" aria-labelledby="rows-heading">
              ${repeat(
                rows,
                (_row, at) => keys[at],
                (row, at) => this.#row(row, at, rows.length, drag),
              )}
            </ol>`
      }
      <div class="row-actions">
        <uui-button class="add" look="secondary" label=${this.#term('rows_add')} @click=${() => this.#addRow()}>
          <uui-icon name="icon-add" aria-hidden="true"></uui-icon>${this.#term('rows_add')}
        </uui-button>
        <uui-button
          class="paste-toggle"
          look="secondary"
          label=${this.#term('rows_paste')}
          aria-expanded=${this._pasting ? 'true' : 'false'}
          @click=${() => {
            this._pasting = !this._pasting;
            if (this._pasting)
              void this.updateComplete.then(() => this.querySelector<HTMLElement>('#rows-paste')?.focus());
          }}
        ></uui-button>
      </div>
      ${this._pasting ? this.#paste('rows') : nothing}
    </div>`;
  }

  #row(row: DesignRow, at: number, count: number, drag: RowDrag | null): TemplateResult {
    const label = this.#labelOf(row.label, this.localize.term('jfbDesigner_newRow', at + 1));
    const name = this.#term('rows_rows');
    const update = (change: (row: DesignRow) => DesignRow, typed?: string) =>
      this.change(
        (field) => {
          const rows = rowsOf(field);
          const index = rows.findIndex((each) => each.key === row.key);
          return index < 0 ? field : withRows(field, replaced(rows, index, change(rows[index] as DesignRow)));
        },
        name,
        typed,
      );
    return html`<li
      class="option-row"
      data-at=${at}
      ?data-line-before=${drag !== null && drag.to === at && drag.to !== drag.from && drag.to !== drag.from + 1}
      ?data-line-after=${drag !== null && at === count - 1 && drag.to === count && drag.from !== count - 1}
      ?data-dragged=${drag?.from === at}
    >
      <div class="row-line">
        <button
          type="button"
          class="row-handle"
          aria-label=${this.#term('options_move', label)}
          aria-expanded=${count < 2 ? nothing : this._placing === row.key ? 'true' : 'false'}
          aria-controls=${count < 2 ? nothing : `row-${at}-place`}
          @pointerdown=${(event: PointerEvent) => this.#dragRow(event, at, label)}
          @keydown=${(event: KeyboardEvent) => this.#keyMove(event, at, count, label)}
          @click=${() => this.#togglePlace(row.key, at, count)}
        >
          <uui-icon name="icon-grip" aria-hidden="true"></uui-icon>
        </button>
        <input
          class="row-label"
          type="text"
          aria-label=${this.#term('rows_labelOf', at + 1)}
          lang=${this.culture || nothing}
          placeholder=${this.#placeholderOf(row.label)}
          .value=${boxValue(this.#own(row.label))}
          @input=${(event: Event) => {
            const text = (event.target as HTMLInputElement).value;
            update((each) => withMember(each, 'label', this.#text(each.label, text)), `row-${row.key}-label`);
          }}
        />
        <span class="row-check">
          <input
            id=${`row-${at}-required`}
            type="checkbox"
            aria-label=${this.#term('rows_requiredOf', label)}
            .checked=${row.required === true}
            @change=${(event: Event) =>
              update((each) => withMember(each, 'required', (event.target as HTMLInputElement).checked || undefined))}
          />
        </span>
        <button
          type="button"
          class="row-tool"
          aria-label=${this.#term('options_remove', label)}
          @click=${() => {
            this.#focusAfterChange(() => this.#focusRowAt(at));
            this.change(
              (field) =>
                withRows(
                  field,
                  rowsOf(field).filter((_each, index) => index !== at),
                ),
              this.#term('options_removeNamed', label),
            );
            this.#announce(this.#term('options_removed', label));
          }}
        >
          <uui-icon name="icon-trash" aria-hidden="true"></uui-icon>
        </button>
      </div>
      ${this._placing === row.key ? this.#place(`row-${at}-place`, at, count, label) : nothing}
    </li>`;
  }

  #addRow() {
    const rows = rowsOf(this.field);
    const label = this.localize.term('jfbDesigner_newRow', rows.length + 1);
    const text = withText(undefined, this.culture, label, this.#isDefault);
    const row: DesignRow = { key: crypto.randomUUID(), ...(text ? { label: text } : {}) };
    this.#focusAfterChange(() => this.#focusLastLabel());
    this.change((field) => withRows(field, [...rowsOf(field), row]), this.#term('rows_add'));
  }
}

if (!customElements.get('jfb-option-editor')) customElements.define('jfb-option-editor', OptionEditorElement);

declare global {
  interface HTMLElementTagNameMap {
    'jfb-option-editor': OptionEditorElement;
  }
}
