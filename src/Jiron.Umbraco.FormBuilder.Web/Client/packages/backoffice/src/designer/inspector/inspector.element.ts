// The inspector (ROADMAP 9.4a, IA 5.1, ADR-0170): what the selected field, group or page is ("Email field") and its
// sections, each a button that opens or closes it, Essentials open and the others as the user left them last. A closed
// section says in one line what is set; an open one shows seven items at most before "More options". It edits texts in
// the language the canvas shows, and says so where that is not the default. Every change goes to the Design view as a
// request, which makes it an edit of the form: typing in one box is one step to undo. Beside the canvas it folds to a
// rail; over a narrow canvas it carries the element's own bar.
import { html, nothing, type PropertyValues, type TemplateResult } from '@umbraco-cms/backoffice/external/lit';
import { UmbLitElement } from '@umbraco-cms/backoffice/lit-element';
import type { FieldSettingResponseModel, FieldTypeResponseModel } from '../../api';
import { jfbBackofficeTokens, jfbVisuallyHidden } from '../../styles/tokens';
import {
  allFields,
  locate,
  type DesignDefinition,
  type DesignField,
  type DesignGroup,
  type DesignPage,
  type LocalizedText,
} from '../definition';
import { RESERVED_ALIASES } from '../editor-model';
import { textIn } from '../preview/field-models';
import type { SiteLanguage } from '../preview/look';
import {
  fieldSections,
  groupSections,
  pageSections,
  splitItems,
  SECTIONS,
  takesSeveral,
  type Item,
  type Section,
  type SectionId,
} from './inspector-model';
import { boxValue } from './box-value';
import { inspectorStyles } from './inspector.styles';
import './merge-tag-picker.element';
import { placeOf, withTag, type MergeTagPlace } from './merge-tags';
import './option-editor.element';
import { itemsOf, optionsOf, parentsBefore, rowsOf, showsCards, sourceOf } from './option-values';
import { boxId, problemText, settingEditor, settingText, type EditorContext } from './setting-editor';
import {
  ownText,
  same,
  settingsOf,
  valueOf,
  withKey,
  without,
  withSetting,
  withText,
  type Parsed,
} from './setting-values';

/** The event an inspector's request travels in, up to the Design view. */
export const INSPECTOR_REQUEST = 'jfb-inspector-request';

/** The selected element's own actions, from the inspector's bar over a narrow canvas or the element's "...". */
export type ElementAction = 'moveTo' | 'duplicate' | 'delete' | 'newPageAfter' | 'copyAlias';

type Element = DesignPage | DesignGroup | DesignField;

/** What the inspector asks the Design view to do. */
export type InspectorRequest =
  | {
      readonly kind: 'change';
      readonly key: string;
      readonly update: (element: Element) => Element;
      /** What Undo names: "Changed Maximum length". */
      readonly said: string;
      /**
       * For typing, the box and the visit to it the change came from: what is typed in one box, until the focus leaves
       * it, is one step to undo. A click's change is a step of its own.
       */
      readonly merge: string | undefined;
    }
  /** A field's new alias, checked already; `revert` puts the box back when the change doesn't go ahead. */
  | { readonly kind: 'alias'; readonly key: string; readonly alias: string; readonly revert: () => void }
  | { readonly kind: 'fold' }
  | { readonly kind: 'unfold' }
  | { readonly kind: 'close' }
  | { readonly kind: 'element'; readonly action: ElementAction; readonly key: string };

/** How the inspector shows: beside the canvas, folded to a rail, or over the canvas. */
export type InspectorMode = 'docked' | 'rail' | 'overlay';

const OPEN_KEY = 'jfb.designer.inspector.open';
const ALIAS = /^[a-z][a-zA-Z0-9]{0,63}$/;

/** The sections this browser left open last; Essentials alone at first. */
export function readOpen(): ReadonlySet<SectionId> {
  try {
    const kept: unknown = JSON.parse(localStorage.getItem(OPEN_KEY) ?? 'null');
    if (Array.isArray(kept)) return new Set(SECTIONS.filter((id) => kept.includes(id)));
  } catch {
    // A browser that keeps nothing opens Essentials.
  }
  return new Set<SectionId>(['essentials']);
}

function keepOpen(open: ReadonlySet<SectionId>) {
  try {
    localStorage.setItem(OPEN_KEY, JSON.stringify([...open]));
  } catch {
    // Kept for this page only.
  }
}

/** An element with one property set, or taken away for undefined. */
function withProperty<T extends Element>(element: T, name: string, value: unknown): T {
  return withKey<unknown>(element, name, value) as T;
}

/** What the inspector shows: the selected element with what it needs to know about where it is. */
type Subject =
  | {
      readonly kind: 'field';
      readonly field: DesignField;
      readonly type: FieldTypeResponseModel | undefined;
      readonly columns: number;
    }
  | { readonly kind: 'group'; readonly group: DesignGroup }
  | { readonly kind: 'page'; readonly page: DesignPage };

export class InspectorElement extends UmbLitElement {
  static override properties = {
    definition: { attribute: false },
    selection: { attribute: false },
    types: { attribute: false },
    culture: { attribute: false },
    languages: { attribute: false },
    form: { attribute: false },
    mode: { reflect: true },
    _open: { state: true },
    _more: { state: true },
    _problems: { state: true },
  };

  static override styles = [jfbBackofficeTokens, jfbVisuallyHidden, inspectorStyles];

  declare definition: DesignDefinition;
  declare selection: string | null;
  declare types: Readonly<Record<string, FieldTypeResponseModel>>;
  /** The language the canvas shows, which the inspector edits texts in. */
  declare culture: string;
  declare languages: readonly SiteLanguage[];
  /** The form's key, whose draft the tag picker's paths come from; none, no picker. */
  declare form: string;
  declare mode: InspectorMode;
  declare private _open: ReadonlySet<SectionId>;
  declare private _more: ReadonlySet<SectionId>;
  /** Why a box's last value couldn't be kept, in words, by the box's id, until it can or another element is chosen. */
  declare private _problems: ReadonlyMap<string, string>;

  /** Counts the focus's visits to the inspector's boxes, so typing in a box again later is a step of its own. */
  #visit = 0;

  constructor() {
    super();
    this.definition = { pages: [] };
    this.selection = null;
    this.types = {};
    this.culture = '';
    this.languages = [];
    this.form = '';
    this.mode = 'docked';
    this._open = readOpen();
    this._more = new Set();
    this._problems = new Map();
    this.addEventListener('focusin', () => this.#visit++);
  }

  /** Forgets why boxes' values couldn't be kept, as Undo and Redo do: the boxes show the form's values again. */
  forget() {
    this._problems = new Map();
  }

  /** Moves the focus to the inspector's heading, as opening it over the canvas does. */
  focusHeading() {
    void this.updateComplete.then(() => this.shadowRoot?.querySelector<HTMLElement>('h2')?.focus());
  }

  /** Moves the focus to the rail's button that opens the inspector again, as folding it does. */
  focusUnfold() {
    void this.updateComplete.then(() => this.shadowRoot?.querySelector<HTMLElement>('uui-button')?.focus());
  }

  protected override willUpdate(changed: PropertyValues) {
    super.willUpdate(changed);
    if (changed.has('selection') || changed.has('culture')) this._problems = new Map();
  }

  protected override updated(changed: PropertyValues) {
    super.updated(changed);
    // Each element selected is shown from its top, not where the last one was scrolled to.
    if (changed.has('selection')) this.shadowRoot?.querySelector('.body')?.scrollTo?.(0, 0);
  }

  get #defaultCulture(): string {
    return this.languages.find((language) => language.isDefault)?.culture ?? this.culture;
  }

  get #isDefault(): boolean {
    return this.culture.toLowerCase() === this.#defaultCulture.toLowerCase();
  }

  // A language's name in the user's own: "German", for de-DE in an English backoffice.
  #languageName(culture: string): string {
    try {
      const names = new Intl.DisplayNames([this.localize.lang()], { type: 'language' });
      return names.of(culture.split('-')[0] ?? culture) ?? culture;
    } catch {
      return this.languages.find((language) => language.culture === culture)?.name ?? culture;
    }
  }

  #term = (key: string, ...args: unknown[]) => this.localize.term(`jfbInspector_${key}`, ...args);

  #say = (text: string | null | undefined) => (text ? this.localize.string(text) : '');

  #subject(): Subject | null {
    const key = this.selection;
    const found = key ? locate(this.definition, key) : undefined;
    if (!found) return null;
    if (found.kind === 'page') return { kind: 'page', page: found.element };
    if (found.kind === 'group') return { kind: 'group', group: found.element };
    const type = Object.hasOwn(this.types, found.element.type) ? this.types[found.element.type] : undefined;
    // A repeatable group's fields lie in one column of their own.
    const container = found.container;
    const columns =
      'type' in container ? 1 : Math.min(4, Math.max(1, Math.trunc(Number(container['columns'] ?? 1)) || 1));
    return { kind: 'field', field: found.element, type, columns };
  }

  #request(request: InspectorRequest) {
    this.dispatchEvent(
      new CustomEvent<InspectorRequest>(INSPECTOR_REQUEST, { detail: request, bubbles: true, composed: true }),
    );
  }

  /** Asks for a change; `typed` names the box typed in, whose changes until the focus leaves it are one step. */
  #change(key: string, update: (element: Element) => Element, name: string, typed?: string) {
    this.#request({
      kind: 'change',
      key,
      update,
      said: this.#term('saidChanged', name),
      merge: typed === undefined ? undefined : `${key}:${typed}:${this.#visit}`,
    });
  }

  #problem(id: string, text: string | null) {
    if ((this._problems.get(id) ?? null) === text) return;
    const next = new Map(this._problems);
    if (text === null) next.delete(id);
    else next.set(id, text);
    this._problems = next;
  }

  #toggle(id: SectionId) {
    const next = new Set(this._open);
    if (next.has(id)) next.delete(id);
    else next.add(id);
    this._open = next;
    keepOpen(next);
  }

  #toggleMore(id: SectionId) {
    const next = new Set(this._more);
    if (next.has(id)) next.delete(id);
    else next.add(id);
    this._more = next;
  }

  #heading(subject: Subject): string {
    if (subject.kind === 'page') return this.#term('headingPage');
    if (subject.kind === 'group') return this.#term('headingGroup');
    return this.#term('headingField', subject.type ? this.#say(subject.type.name) : subject.field.type);
  }

  #sections(subject: Subject): Section[] {
    if (subject.kind === 'page') return pageSections();
    if (subject.kind === 'group') return groupSections();
    return subject.type ? fieldSections(subject.type, subject.columns) : [];
  }

  #keyOf(subject: Subject): string {
    return subject.kind === 'field'
      ? subject.field.key
      : subject.kind === 'group'
        ? subject.group.key
        : subject.page.key;
  }

  #elementOf(subject: Subject): Element {
    return subject.kind === 'field' ? subject.field : subject.kind === 'group' ? subject.group : subject.page;
  }

  // ---- Values -------------------------------------------------------------------------------------------------------

  // A text shown in the language edited: its own words, where another language than the default has none yet.
  #textOf(texts: LocalizedText | null | undefined): string {
    return this.#isDefault ? textIn(texts, this.culture) : ownText(texts, this.culture);
  }

  #placeholderOf(texts: LocalizedText | null | undefined): string | typeof nothing {
    return this.#isDefault ? nothing : textIn(texts, this.#defaultCulture) || nothing;
  }

  #editorContext(field: DesignField, type: FieldTypeResponseModel): EditorContext {
    return {
      say: this.#say,
      term: this.#term,
      culture: this.culture,
      defaultCulture: this.#defaultCulture,
      problems: this._problems,
      set: (path, setting, parsed, done) => this.#setSetting(field, type, path, setting, parsed, done),
      mergeTags: (setting, path) => this.#tagPicker(placeOf(type.alias, setting.name), boxId(path)),
    };
  }

  // "Insert merge tag" for the box with this id, its tags of this place (ADR-0173).
  #tagPicker(place: MergeTagPlace, id: string): TemplateResult | typeof nothing {
    if (!this.form) return nothing;
    return html`<jfb-merge-tag-picker
      .place=${place}
      .form=${this.form}
      .culture=${this.culture}
      .definition=${() => this.definition}
      .insert=${(tag: string) => this.#insertTag(id, tag)}
    ></jfb-merge-tag-picker>`;
  }

  // A tag put into a box where its caret was, the box given the focus with the caret after the tag, and the change sent
  // as the box sends one: a setting's box as a value it may keep, a property's as typing.
  #insertTag(id: string, tag: string) {
    const box = this.shadowRoot?.getElementById(id);
    if (box instanceof HTMLInputElement || box instanceof HTMLTextAreaElement) {
      const end = box.value.length;
      const { text, caret } = withTag(box.value, box.selectionStart ?? end, box.selectionEnd ?? end, tag);
      box.value = text;
      box.focus();
      box.setSelectionRange(caret, caret);
      box.dispatchEvent(new Event(id.startsWith('setting-') ? 'change' : 'input', { bubbles: true }));
    } else if (box && 'insertText' in box) {
      (box as unknown as { insertText: (text: string) => void }).insertText(tag);
    }
  }

  // A setting's new value, at its path: a nested setting's own value sits in its parent's object.
  #setSetting(
    field: DesignField,
    type: FieldTypeResponseModel,
    path: string,
    setting: FieldSettingResponseModel,
    parsed: Parsed,
    done: boolean,
  ) {
    const id = `setting-${path.replace(/[^a-zA-Z0-9]/g, '-')}`;
    if (!parsed.ok) {
      // While typing, a value that can't be kept yet says nothing new; leaving the box says why.
      if (done) this.#problem(id, problemText(this.#term, parsed.problem));
      return;
    }
    this.#problem(id, null);
    const [first, inner] = path.split('.');
    const top = type.settings.find((each) => each.name === first);
    if (!top) return;
    const culture = this.culture;
    const isDefault = this.#isDefault;
    const label = this.#say(setting.label);
    this.#change(
      field.key,
      (element) => {
        const own = element as DesignField;
        const current = settingsOf(own);
        const write = (target: FieldSettingResponseModel, before: unknown, value: unknown) =>
          (target.kind === 'localizedText' || target.kind === 'localizedHtml') &&
          (typeof value === 'string' || value === undefined)
            ? withText(before as LocalizedText | undefined, culture, (value as string | undefined) ?? '', isDefault)
            : value;
        if (inner === undefined) return withSetting(own, top, write(top, current[top.name], parsed.value));
        const before = (valueOf(current, top) as Readonly<Record<string, unknown>> | undefined) ?? {};
        const value = write(setting, before[inner], parsed.value);
        const parent = withKey(before, inner, value === undefined || same(value, setting.default) ? undefined : value);
        return withSetting(own, top, Object.keys(parent).length > 0 ? parent : undefined);
      },
      label,
      done ? undefined : path,
    );
  }

  // ---- Items --------------------------------------------------------------------------------------------------------

  #itemName(item: Item): string {
    switch (item.kind) {
      case 'text':
      case 'switch':
      case 'plain':
        return this.#term(`property_${item.property}`);
      case 'message':
        return this.#term(`message_${item.message}`);
      case 'setting':
        return this.#say(item.setting.label);
      default:
        return this.#term(`property_${item.kind}`);
    }
  }

  #item(subject: Subject, item: Item): TemplateResult | typeof nothing {
    const key = this.#keyOf(subject);
    const element = this.#elementOf(subject);
    const own = element as Readonly<Record<string, unknown>>;
    const name = this.#itemName(item);
    switch (item.kind) {
      case 'text': {
        const id = `prop-${item.property}`;
        const texts = own[item.property] as LocalizedText | null | undefined;
        const write = (text: string) =>
          this.#change(
            key,
            (one) =>
              withProperty(
                one,
                item.property,
                withText(
                  (one as Readonly<Record<string, unknown>>)[item.property] as LocalizedText | undefined,
                  this.culture,
                  text,
                  this.#isDefault,
                ),
              ),
            name,
            item.property,
          );
        const box = item.long
          ? html`<textarea
              id=${id}
              rows="2"
              lang=${this.culture || nothing}
              placeholder=${this.#placeholderOf(texts)}
              .value=${boxValue(this.#textOf(texts))}
              @input=${(event: Event) => write((event.target as HTMLTextAreaElement).value)}
            ></textarea>`
          : html`<input
              type="text"
              id=${id}
              autocomplete="off"
              lang=${this.culture || nothing}
              placeholder=${this.#placeholderOf(texts)}
              .value=${boxValue(this.#textOf(texts))}
              @input=${(event: Event) => write((event.target as HTMLInputElement).value)}
            />`;
        return html`<div class="item"><label class="label" for=${id}>${name}</label>${box}</div>`;
      }
      case 'switch': {
        const id = `prop-${item.property}`;
        const standard = item.property === 'includeInEmails';
        const value = typeof own[item.property] === 'boolean' ? (own[item.property] as boolean) : standard;
        return html`<div class="item">
          <div class="item-switch">
            <input
              type="checkbox"
              role="switch"
              id=${id}
              aria-describedby=${this.#helpOf(item) ? `${id}-help` : nothing}
              .checked=${value}
              @change=${(event: Event) => {
                const checked = (event.target as HTMLInputElement).checked;
                this.#change(
                  key,
                  (one) => withProperty(one, item.property, checked === standard ? undefined : checked),
                  name,
                );
              }}
            />
            <label class="label" for=${id}>${name}</label>
          </div>
          ${this.#helpOf(item) ? html`<p class="help" id=${`${id}-help`}>${this.#helpOf(item)}</p>` : nothing}
        </div>`;
      }
      case 'span':
        return this.#select(
          'prop-span',
          name,
          String(Math.min(item.columns, Math.max(1, Number(own['span'] ?? 1) || 1))),
          Array.from({ length: item.columns }, (_, at) => [String(at + 1), this.#term('spanOf', at + 1, item.columns)]),
          (value) =>
            this.#change(key, (one) => withProperty(one, 'span', value === '1' ? undefined : Number(value)), name),
        );
      case 'columns':
        return this.#select(
          'prop-columns',
          name,
          String(own['columns'] ?? 1),
          [1, 2, 3, 4].map((count) => [String(count), this.#term('columnsCount', count)]),
          (value) =>
            this.#change(
              key,
              (one) => {
                const columns = Number(value);
                const group = withProperty(one, 'columns', columns === 1 ? undefined : columns) as DesignGroup;
                // A field wider than the group's columns now takes their width, as the server needs.
                const fields = group.fields.map((each) =>
                  Number(each['span'] ?? 1) > columns
                    ? withProperty(each, 'span', columns === 1 ? undefined : columns)
                    : each,
                );
                return { ...group, fields };
              },
              name,
            ),
        );
      case 'style':
        return this.#select(
          'prop-style',
          name,
          String(own['style'] ?? 'plain'),
          ['plain', 'card'].map((style) => [style, this.#term(`style_${style}`)]),
          (value) =>
            this.#change(key, (one) => withProperty(one, 'style', value === 'plain' ? undefined : value), name),
        );
      case 'keep':
        return this.#select(
          'prop-keep',
          name,
          String(own['keep'] ?? 'always'),
          ['always', 'untilActionsRun'].map((keep) => [keep, this.#term(`keep_${keep}`)]),
          (value) =>
            this.#change(key, (one) => withProperty(one, 'keep', value === 'always' ? undefined : value), name),
        );
      case 'plain':
        return this.#plain(`prop-${item.property}`, name, String(own[item.property] ?? ''), (text) =>
          this.#change(
            key,
            (one) => withProperty(one, item.property, text.trim() === '' ? undefined : text.trim()),
            name,
            item.property,
          ),
        );
      case 'defaultValue':
        return this.#defaultValue(
          subject,
          subject.kind === 'field' && subject.type !== undefined && takesSeveral(subject.type, subject.field),
          name,
        );
      case 'alias':
        return subject.kind === 'field' ? this.#alias(subject.field, name) : nothing;
      case 'prefill':
        return this.#prefill(key, own['prefill'] as Readonly<Record<string, unknown>> | undefined, name);
      case 'message':
        return this.#message(key, own, item.message, name);
      case 'unique':
      case 'matches':
        return subject.kind === 'field' ? this.#check(subject.field, item.kind, name) : nothing;
      case 'setting': {
        if (subject.kind !== 'field' || !subject.type) return nothing;
        const value = valueOf(settingsOf(subject.field), item.setting);
        return settingEditor(this.#editorContext(subject.field, subject.type), item.setting, value);
      }
      case 'options':
      case 'rows':
        return subject.kind === 'field' && subject.type
          ? this.#options(subject.field, subject.type, item.kind)
          : nothing;
    }
  }

  // A choice field's options or a grid's rows, in an editor of their own whose every change comes back here.
  #options(field: DesignField, type: FieldTypeResponseModel, part: 'options' | 'rows'): TemplateResult {
    const isChoice = (each: DesignField) => {
      const features = (Object.hasOwn(this.types, each.type) ? this.types[each.type] : undefined)?.features ?? [];
      return features.includes('options') && !features.includes('rows');
    };
    return html`<jfb-option-editor
      .field=${field}
      .type=${type}
      .culture=${this.culture}
      .defaultCulture=${this.#defaultCulture}
      .parents=${this.#parentsOf(field, isChoice)}
      .edits=${part}
      .cards=${showsCards(field, type, this.definition)}
      .change=${(update: (one: DesignField) => DesignField, name: string, typed?: string) =>
        this.#change(
          field.key,
          (element) => update(element as DesignField),
          name,
          typed === undefined ? undefined : `${part}:${typed}`,
        )}
    ></jfb-option-editor>`;
  }

  // The choice fields before this one, which its options may depend on, and the one they depend on wherever it is now:
  // a move may have put it after this one, and "Depends on" still names it.
  #parentsOf(field: DesignField, isChoice: (each: DesignField) => boolean): DesignField[] {
    const fields = allFields(this.definition);
    const before = parentsBefore(fields, field.key, isChoice);
    const key = optionsOf(field).parent;
    const parent =
      key && !before.some((each) => each.key === key) ? fields.find((each) => each.key === key) : undefined;
    return parent ? [...before, parent] : before;
  }

  #helpOf(item: Item): string {
    if (item.kind === 'switch' && item.property !== 'required') return this.#term(`help_${item.property}`);
    if (item.kind === 'alias' || item.kind === 'keep' || item.kind === 'unique') return this.#term(`help_${item.kind}`);
    return '';
  }

  #select(
    id: string,
    name: string,
    value: string,
    choices: readonly (readonly [string, string])[],
    chosen: (value: string) => void,
  ): TemplateResult {
    return html`<div class="item">
      <label class="label" for=${id}>${name}</label>
      <select id=${id} @change=${(event: Event) => chosen((event.target as HTMLSelectElement).value)}>
        ${choices.map(([one, label]) => html`<option value=${one} ?selected=${one === value}>${label}</option>`)}
      </select>
    </div>`;
  }

  #plain(
    id: string,
    name: string,
    value: string,
    write: (text: string) => void,
    help = '',
    after: TemplateResult | typeof nothing = nothing,
  ): TemplateResult {
    return html`<div class="item">
      <label class="label" for=${id}>${name}</label>
      <input
        type="text"
        id=${id}
        autocomplete="off"
        spellcheck="false"
        aria-describedby=${help ? `${id}-help` : nothing}
        .value=${boxValue(value)}
        @input=${(event: Event) => write((event.target as HTMLInputElement).value)}
      />
      ${help ? html`<p class="help" id=${`${id}-help`}>${help}</p>` : nothing} ${after}
    </div>`;
  }

  #defaultValue(subject: Subject, many: boolean, name: string): TemplateResult | typeof nothing {
    if (subject.kind !== 'field') return nothing;
    const key = subject.field.key;
    const value = subject.field['defaultValue'];
    const id = 'prop-defaultValue';
    if (!many)
      return this.#plain(
        id,
        name,
        typeof value === 'string' ? value : '',
        (text) =>
          this.#change(
            key,
            (one) => withProperty(one, 'defaultValue', text === '' ? undefined : text),
            name,
            'defaultValue',
          ),
        '',
        this.#tagPicker('defaultValue', id),
      );
    const lines = Array.isArray(value) ? value.map(String).join('\n') : typeof value === 'string' ? value : '';
    return html`<div class="item">
      <label class="label" for=${id}>${name}</label>
      <textarea
        id=${id}
        rows="3"
        spellcheck="false"
        aria-describedby=${`${id}-help`}
        .value=${boxValue(lines)}
        @input=${(event: Event) => {
          const items = (event.target as HTMLTextAreaElement).value
            .split(/\r?\n/)
            .map((line) => line.trim())
            .filter((line) => line !== '');
          this.#change(
            key,
            (one) => withProperty(one, 'defaultValue', items.length > 0 ? items : undefined),
            name,
            'defaultValue',
          );
        }}
      ></textarea>
      <p class="help" id=${`${id}-help`}>${this.#term('oneALine')}</p>
      ${this.#tagPicker('defaultValue', id)}
    </div>`;
  }

  // The alias: checked as the server checks it when the box is left; a change the form allows goes to the Design view,
  // which asks first when entries may know the alias before.
  #alias(field: DesignField, name: string): TemplateResult {
    const id = 'prop-alias';
    const problem = this._problems.get(id);
    const others = allFields(this.definition).filter((each) => each.key !== field.key);
    const check = (alias: string): string | null => {
      if (alias === '') return this.#term('aliasRequired');
      if (!ALIAS.test(alias)) return this.#term('aliasPattern');
      if (RESERVED_ALIASES.has(alias.toLowerCase())) return this.#term('aliasReserved');
      const lower = alias.toLowerCase();
      const taken = others.some(
        (each) =>
          each.alias?.toLowerCase() === lower || (each.formerAliases ?? []).some((one) => one.toLowerCase() === lower),
      );
      return taken ? this.#term('aliasTaken') : null;
    };
    const described = [`${id}-help`, problem ? `${id}-why` : ''].filter(Boolean).join(' ');
    return html`<div class="item">
      <label class="label" for=${id}>${name}</label>
      <input
        type="text"
        id=${id}
        class="code"
        autocomplete="off"
        spellcheck="false"
        autocapitalize="off"
        aria-describedby=${described}
        aria-invalid=${problem ? 'true' : 'false'}
        .value=${boxValue(field.alias ?? '', problem !== undefined)}
        @input=${(event: Event) => {
          // A problem shown goes once the alias is one the form takes.
          if (problem && check((event.target as HTMLInputElement).value.trim()) === null) this.#problem(id, null);
        }}
        @change=${(event: Event) => {
          const box = event.target as HTMLInputElement;
          const alias = box.value.trim();
          const why = check(alias);
          this.#problem(id, why);
          if (why || alias === field.alias) return;
          // The way back puts this field's alias in the box only while the field is the one shown: another selected
          // meanwhile has the box now.
          const revert = () => {
            const now = this.selection === field.key ? locate(this.definition, field.key) : undefined;
            if (now?.kind === 'field') box.value = now.element.alias ?? '';
          };
          this.#request({ kind: 'alias', key: field.key, alias, revert });
        }}
      />
      <p class="help" id=${`${id}-help`}>${this.#term('help_alias')}</p>
      ${problem ? html`<p class="why" id=${`${id}-why`}>${problem}</p>` : nothing}
    </div>`;
  }

  #prefill(key: string, prefill: Readonly<Record<string, unknown>> | undefined, name: string): TemplateResult {
    const own = prefill ?? {};
    const write = (part: string, value: string | undefined, typed: boolean) =>
      this.#change(
        key,
        (one) => {
          const before =
            ((one as Readonly<Record<string, unknown>>)['prefill'] as Readonly<Record<string, unknown>> | undefined) ??
            {};
          const set = withKey(before, part, value);
          // Whether a member's value shows means nothing without a member property.
          const next = set['member'] === undefined ? without(set, 'memberMode') : set;
          const sources = ['query', 'member', 'context'].some((each) => next[each] !== undefined);
          return withProperty(one, 'prefill', sources ? next : undefined);
        },
        name,
        typed ? `prefill.${part}` : undefined,
      );
    const text = (part: 'query' | 'member' | 'context') =>
      this.#plain(`prop-prefill-${part}`, this.#term(`prefill_${part}`), String(own[part] ?? ''), (value) =>
        write(part, value.trim() === '' ? undefined : value.trim(), true),
      );
    return html`<fieldset class="item item-group">
      <legend class="label">${name}</legend>
      ${text('query')} ${text('member')}
      ${
        own['member'] !== undefined
          ? this.#select(
              'prop-prefill-memberMode',
              this.#term('prefill_memberMode'),
              String(own['memberMode'] ?? 'show'),
              ['show', 'hide'].map((mode) => [mode, this.#term(`memberMode_${mode}`)]),
              (value) => write('memberMode', value === 'show' ? undefined : value, false),
            )
          : nothing
      }
      ${text('context')}
    </fieldset>`;
  }

  #message(key: string, own: Readonly<Record<string, unknown>>, message: string, name: string): TemplateResult {
    const id = `prop-message-${message}`;
    const messages = (own['messages'] as Readonly<Record<string, LocalizedText>> | undefined) ?? {};
    const texts = messages[message];
    return html`<div class="item">
      <label class="label" for=${id}>${name}</label>
      <input
        type="text"
        id=${id}
        autocomplete="off"
        lang=${this.culture || nothing}
        placeholder=${this.#isDefault ? this.#term('messageStandard') : this.#placeholderOf(texts)}
        .value=${boxValue(this.#textOf(texts))}
        @input=${(event: Event) => {
          const text = (event.target as HTMLInputElement).value;
          this.#change(
            key,
            (one) => {
              const next: Record<string, LocalizedText> = {
                ...(((one as Readonly<Record<string, unknown>>)['messages'] as
                  Record<string, LocalizedText> | undefined) ?? {}),
              };
              const kept = withKey(next, message, withText(next[message], this.culture, text, this.#isDefault));
              return withProperty(one, 'messages', Object.keys(kept).length > 0 ? kept : undefined);
            },
            name,
            `message.${message}`,
          );
        }}
      />
    </div>`;
  }

  // The checks no type owns that 9.4a edits: each answer once per form, and an answer that matches another field's.
  #check(field: DesignField, kind: 'unique' | 'matches', name: string): TemplateResult {
    const wire = kind === 'unique' ? 'uniquePerForm' : 'matchesField';
    const checks = (field['validation'] as readonly Readonly<Record<string, unknown>>[] | undefined) ?? [];
    const check = checks.find((each) => each['kind'] === wire);
    const id = `prop-${kind}`;
    // The check in its place among the field's checks, or none; a check's message is typed, the rest is a click.
    const put = (next: Readonly<Record<string, unknown>> | undefined, typed = false) =>
      this.#change(
        field.key,
        (one) => {
          const all =
            ((one as Readonly<Record<string, unknown>>)['validation'] as
              readonly Readonly<Record<string, unknown>>[] | undefined) ?? [];
          const at = all.findIndex((each) => each['kind'] === wire);
          const kept = all.filter((each) => each['kind'] !== wire);
          if (next) kept.splice(at < 0 ? kept.length : at, 0, next);
          return withProperty(one, 'validation', kept.length > 0 ? kept : undefined);
        },
        name,
        typed ? `${kind}.message` : undefined,
      );
    const said = check?.['message'] as LocalizedText | undefined;
    const message = check
      ? html`<div class="item">
          <label class="label" for=${`${id}-message`}>${this.#term('checkMessage')}</label>
          <input
            type="text"
            id=${`${id}-message`}
            autocomplete="off"
            lang=${this.culture || nothing}
            placeholder=${this.#isDefault ? this.#term('messageStandard') : this.#placeholderOf(said)}
            .value=${boxValue(this.#textOf(said))}
            @input=${(event: Event) => {
              const written = withText(said, this.culture, (event.target as HTMLInputElement).value, this.#isDefault);
              put(withKey<unknown>(check, 'message', written), true);
            }}
          />
        </div>`
      : nothing;
    if (kind === 'unique')
      return html`<div class="item item-stack">
        <div class="item-switch">
          <input
            type="checkbox"
            role="switch"
            id=${id}
            aria-describedby=${`${id}-help`}
            .checked=${check !== undefined}
            @change=${(event: Event) => put((event.target as HTMLInputElement).checked ? { kind: wire } : undefined)}
          />
          <label class="label" for=${id}>${name}</label>
        </div>
        <p class="help" id=${`${id}-help`}>${this.#term('help_unique')}</p>
        ${message}
      </div>`;
    const kindOf = this.types[field.type]?.ruleKind ?? null;
    const candidates = allFields(this.definition).filter(
      (each) =>
        each.key !== field.key &&
        (this.types[each.type]?.holdsValue ?? false) &&
        (this.types[each.type]?.ruleKind ?? null) === kindOf,
    );
    return html`<div class="item item-stack">
      ${this.#select(
        id,
        name,
        String(check?.['field'] ?? ''),
        [
          ['', this.#term('matchesNone')],
          ...candidates.map((each): [string, string] => [
            each.key,
            textIn(each.label, this.culture) || each.alias || this.#say(this.types[each.type]?.name),
          ]),
        ],
        (value) =>
          put(
            value === ''
              ? undefined
              : { kind: wire, field: value, ...(check?.['message'] ? { message: check['message'] } : {}) },
          ),
      )}
      ${message}
    </div>`;
  }

  // ---- Summaries ----------------------------------------------------------------------------------------------------

  /** What an item says in its section's one line when it is set; null while it is as it comes. */
  #summaryOf(subject: Subject, item: Item): string | null {
    const own = this.#elementOf(subject) as Readonly<Record<string, unknown>>;
    const name = this.#itemName(item);
    switch (item.kind) {
      case 'text': {
        const text = textIn(own[item.property] as LocalizedText | undefined, this.culture);
        return text ? (item.property === 'label' || item.property === 'title' ? text : name) : null;
      }
      case 'switch': {
        const standard = item.property === 'includeInEmails';
        const value = typeof own[item.property] === 'boolean' ? own[item.property] : standard;
        if (value === standard) return null;
        return value ? name : `${name}: ${this.#term('off')}`;
      }
      case 'span':
        return Number(own['span'] ?? 1) > 1 ? this.#term('spanOf', Number(own['span']), item.columns) : null;
      case 'columns':
        return Number(own['columns'] ?? 1) > 1 ? this.#term('columnsCount', Number(own['columns'])) : null;
      case 'style':
        return own['style'] && own['style'] !== 'plain' ? this.#term(`style_${String(own['style'])}`) : null;
      case 'keep':
        return own['keep'] && own['keep'] !== 'always' ? this.#term(`keep_${String(own['keep'])}`) : null;
      case 'alias':
        return typeof own['alias'] === 'string' ? `${name}: ${own['alias']}` : null;
      case 'plain':
      case 'defaultValue':
      case 'prefill':
        return own[item.kind === 'plain' ? item.property : item.kind] !== undefined ? name : null;
      case 'message':
        return (own['messages'] as Readonly<Record<string, unknown>> | undefined)?.[item.message] ? name : null;
      case 'unique':
      case 'matches': {
        const wire = item.kind === 'unique' ? 'uniquePerForm' : 'matchesField';
        return ((own['validation'] as readonly Readonly<Record<string, unknown>>[] | undefined) ?? []).some(
          (each) => each['kind'] === wire,
        )
          ? name
          : null;
      }
      case 'options': {
        if (subject.kind !== 'field') return null;
        const source = sourceOf(subject.field);
        return source === 'items'
          ? itemsOf(subject.field).length > 0
            ? this.#term('options_count', itemsOf(subject.field).length)
            : null
          : this.#term(`options_from_${source}`);
      }
      case 'rows':
        return subject.kind === 'field' && rowsOf(subject.field).length > 0
          ? this.#term('rows_count', rowsOf(subject.field).length)
          : null;
      case 'setting': {
        if (subject.kind !== 'field') return null;
        const settings = settingsOf(subject.field);
        const locked = item.setting.lock && item.setting.lock.value !== undefined && item.setting.lock.value !== null;
        if (!locked && !Object.hasOwn(settings, item.setting.name)) return null;
        const value = valueOf(settings, item.setting);
        if (item.setting.editor === 'boolean') return value === true ? name : `${name}: ${this.#term('off')}`;
        const text = settingText({ say: this.#say, term: this.#term, culture: this.culture }, item.setting, value);
        return text ? `${name}: ${text}` : null;
      }
    }
  }

  #summary(subject: Subject, section: Section): string {
    if (section.id === 'rules') {
      const rules =
        ((this.#elementOf(subject) as Readonly<Record<string, unknown>>)['rules'] as readonly unknown[] | undefined) ??
        [];
      return rules.length === 0 ? this.#term('rulesNone') : this.#term('rulesCount', rules.length);
    }
    const set = section.items.flatMap((item) => this.#summaryOf(subject, item) ?? []);
    return set.length > 0 ? set.join(' \u00b7 ') : this.#term('summaryNone');
  }

  // ---- Frame --------------------------------------------------------------------------------------------------------

  #section(subject: Subject, section: Section): TemplateResult {
    const open = this._open.has(section.id);
    const id = `section-${section.id}`;
    const { shown, more } = splitItems(section.items);
    const showsMore = this._more.has(section.id);
    return html`<section class="section" data-section=${section.id}>
      <h3 class="section-head">
        <button
          type="button"
          aria-expanded=${open ? 'true' : 'false'}
          aria-controls=${id}
          @click=${() => this.#toggle(section.id)}
        >
          <uui-icon name=${open ? 'icon-navigation-down' : 'icon-navigation-right'} aria-hidden="true"></uui-icon>
          <span class="section-name">${this.#term(`section_${section.id}`)}</span>
          ${open ? nothing : html`<span class="summary">${this.#summary(subject, section)}</span>`}
        </button>
      </h3>
      <div class="section-body" id=${id} ?hidden=${!open}>
        ${
          open
            ? section.id === 'rules'
              ? html`<p class="help">${this.#summary(subject, section)}</p>
                  <p class="help">${this.#term('rulesHelp')}</p>`
              : html`${shown.map((item) => this.#item(subject, item))}
                ${showsMore ? more.map((item) => this.#item(subject, item)) : nothing}
                ${
                  more.length > 0
                    ? html`<button
                        type="button"
                        class="more"
                        aria-expanded=${showsMore ? 'true' : 'false'}
                        @click=${() => this.#toggleMore(section.id)}
                      >
                        ${this.#term(showsMore ? 'fewerOptions' : 'moreOptions', more.length)}
                      </button>`
                    : nothing
                }`
            : nothing
        }
      </div>
    </section>`;
  }

  #bar(subject: Subject): TemplateResult {
    const key = this.#keyOf(subject);
    const act = (action: ElementAction) => this.#request({ kind: 'element', action, key });
    const menu = subject.kind !== 'page';
    return html`<div class="tools" role="group" aria-label=${this.#term('elementActions')}>
      <button type="button" @click=${() => act('moveTo')}>
        <uui-icon name="icon-navigation" aria-hidden="true"></uui-icon>${this.localize.term('jfbDesigner_moveTo')}
      </button>
      ${
        subject.kind === 'page'
          ? nothing
          : html`<button type="button" @click=${() => act('duplicate')}>
              <uui-icon name="icon-documents" aria-hidden="true"></uui-icon
              >${this.localize.term('jfbDesigner_duplicate')}
            </button>`
      }
      <button type="button" @click=${() => act('delete')}>
        <uui-icon name="icon-trash" aria-hidden="true"></uui-icon>${this.localize.term('jfbDesigner_delete')}
      </button>
      ${
        menu
          ? html`<button
                type="button"
                popovertarget="jfb-inspector-more"
                aria-label=${this.localize.term('jfbDesigner_moreElement')}
              >
                <uui-symbol-more></uui-symbol-more>
              </button>
              <uui-popover-container
                id="jfb-inspector-more"
                placement="bottom-end"
                @toggle=${(event: ToggleEvent) => {
                  if (event.newState === 'open')
                    (event.target as HTMLElement).querySelector<HTMLElement>('button')?.focus();
                }}
              >
                <div class="menu">
                  <button type="button" @click=${() => this.#menu('newPageAfter', key)}>
                    ${this.localize.term('jfbDesigner_newPageAfter')}
                  </button>
                  ${
                    subject.kind === 'field' && subject.field.alias
                      ? html`<button type="button" @click=${() => this.#menu('copyAlias', key)}>
                          ${this.localize.term('jfbDesigner_copyAlias')}
                        </button>`
                      : nothing
                  }
                </div>
              </uui-popover-container>`
          : nothing
      }
    </div>`;
  }

  #menu(action: ElementAction, key: string) {
    this.shadowRoot?.querySelector<HTMLElement & { hidePopover?: () => void }>('#jfb-inspector-more')?.hidePopover?.();
    this.#request({ kind: 'element', action, key });
  }

  override render() {
    const subject = this.#subject();
    if (!subject) return nothing;
    if (this.mode === 'rail')
      return html`<uui-button
        compact
        look="secondary"
        label=${this.#term('unfold')}
        @click=${() => this.#request({ kind: 'unfold' })}
      >
        <uui-icon name="icon-navigation-left"></uui-icon>
      </uui-button>`;
    const sections = this.#sections(subject);
    return html`<div class="head">
        <h2 tabindex="-1">${this.#heading(subject)}</h2>
        ${
          this.mode === 'docked'
            ? html`<uui-button compact label=${this.#term('fold')} @click=${() => this.#request({ kind: 'fold' })}>
                <uui-icon name="icon-navigation-right"></uui-icon>
              </uui-button>`
            : nothing
        }
        <uui-button
          compact
          label=${this.localize.term('general_close')}
          @click=${() => this.#request({ kind: 'close' })}
        >
          <uui-icon name="icon-wrong"></uui-icon>
        </uui-button>
        ${this.mode === 'overlay' ? this.#bar(subject) : nothing}
      </div>
      ${
        this.#isDefault
          ? nothing
          : html`<p class="note">
              ${this.#term('editingLanguage', this.#languageName(this.culture), this.#languageName(this.#defaultCulture))}
            </p>`
      }
      <div class="body">
        ${
          subject.kind === 'field' && !subject.type
            ? // While the types are read, nothing is said of a type the site may well have.
              Object.keys(this.types).length === 0
              ? html`<uui-loader></uui-loader>`
              : html`<p class="note">${this.#term('typeGone', subject.field.type)}</p>`
            : sections.map((section) => this.#section(subject, section))
        }
      </div>`;
  }
}

if (!customElements.get('jfb-designer-inspector')) customElements.define('jfb-designer-inspector', InspectorElement);

declare global {
  interface HTMLElementTagNameMap {
    'jfb-designer-inspector': InspectorElement;
  }
}
