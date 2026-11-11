// The plain setting editors (ROADMAP 9.4a, ADR-0170): one template for each editor a type's schema names, drawn with
// the browser's own boxes so each is labelled, described and said as screen readers expect. A box keeps what was typed
// while it can't be kept, says why under it once the box is left, and hands each value it can keep to the inspector; a
// locked setting shows its value and the configuration key behind it, read-only, and a hidden one shows nothing.
import '@umbraco-cms/backoffice/document';
import { html, nothing, type TemplateResult } from '@umbraco-cms/backoffice/external/lit';
import '@umbraco-cms/backoffice/media';
import type { FieldSettingResponseModel } from '../../api';
import type { LocalizedText } from '../definition';
import { textIn } from '../preview/field-models';
import { boxValue } from './box-value';
import './dictionary-pick.element';
import './rich-text.element';
import {
  ownText,
  parseChoices,
  parseSetting,
  readDateBound,
  writeDateBound,
  type DateBound,
  type Parsed,
  type Problem,
} from './setting-values';

/** What an editor needs from the inspector. */
export interface EditorContext {
  /** A `#` key or a plain text, in the user's language. */
  readonly say: (text: string | null | undefined) => string;
  /** A term of the inspector's dictionary area, without its `jfbInspector_`. */
  readonly term: (key: string, ...args: unknown[]) => string;
  /** The language the inspector edits texts in. */
  readonly culture: string;
  /** The site's default language, whose text visitors see where the edited language has none. */
  readonly defaultCulture: string;
  /** Why a box's last value couldn't be kept, in words, by the box's id. */
  readonly problems: ReadonlyMap<string, string>;
  /**
   * Hands on a value, or why it can't be kept, for the setting at a path ("format.decimals"); `done` once the box is
   * left, when a problem is shown.
   */
  readonly set: (path: string, setting: FieldSettingResponseModel, parsed: Parsed, done: boolean) => void;
  /**
   * What visitors see for a text that names a dictionary item, said under its box (ADR-0228); null for a text that
   * names none. A host without it offers no dictionary picker either.
   */
  readonly dictionaryLine?: (text: string) => string | null;
  /** Says that an item was picked from the dictionary, whose texts are then read again. */
  readonly dictionaryPicked?: () => void;
  /** "Insert merge tag" under the box of a setting that takes tags, which puts a tag into it (ADR-0173). */
  readonly mergeTags?: (setting: FieldSettingResponseModel, path: string) => TemplateResult | typeof nothing;
  /**
   * What the form check found at a box, by its id: the ids that describe the box, the lines that say it, and whether
   * one is an error. Said under the box, it never holds what the box shows, as a value typed that can't be kept does.
   */
  readonly found?: (id: string) => Found | undefined;
  /**
   * The host's own editors by their aliases, for settings the inspector draws none of: the action sidebar's field
   * pickers, addresses, recipients and layouts (ADR-0189).
   */
  readonly editors?: Readonly<
    Record<
      string,
      (setting: FieldSettingResponseModel, value: unknown, path: string) => TemplateResult | typeof nothing
    >
  >;
}

/**
 * The inspector's words, for a screen outside the form's workspace that draws settings with its editors, such as the
 * global settings (ADR-0218): the screen loads the workspace's words first, as the option lists' workspace does
 * (ADR-0214).
 */
export const inspectorWords =
  (localize: { term(key: string, ...args: unknown[]): string }): EditorContext['term'] =>
  (key, ...args) =>
    localize.term(`jfbInspector_${key}`, ...args);

/** What the form check found at a box, as an editor says it. */
export interface Found {
  readonly ids: readonly string[];
  readonly lines: TemplateResult | typeof nothing;
  readonly error: boolean;
}

/** The units of a date's limit from today, in their order. */
export const UNITS = ['d', 'w', 'm', 'y'] as const;
type Unit = (typeof UNITS)[number];

/** A box's id from the setting's path, unique in the inspector's shadow root. */
export const boxId = (path: string) => `setting-${path.replace(/[^a-zA-Z0-9]/g, '-')}`;

/** The words for why a value can't be kept. */
export function problemText(term: EditorContext['term'], problem: Problem): string {
  switch (problem.kind) {
    case 'below':
      return term(problem.exclusive ? 'problem_aboveOnly' : 'problem_atLeast', problem.limit);
    case 'above':
      return term(problem.exclusive ? 'problem_belowOnly' : 'problem_atMost', problem.limit);
    case 'tooShort':
    case 'tooLong':
    case 'tooFew':
    case 'tooMany':
      return term(`problem_${problem.kind}`, problem.limit);
    default:
      return term(`problem_${problem.kind}`);
  }
}

const asText = (value: unknown) => (value === undefined || value === null ? '' : String(value));

/**
 * Rich text as one line of its words, for a summary or a hint: a paragraph, a list item or a line break a space, any
 * other tag gone without one, so a link's words keep the full stop after them; its spaces folded, its first 80
 * characters.
 */
export function plainOf(html: string): string {
  const words = html
    .replace(/<\/?(p|li|ul|ol|br|h[2-6]|blockquote|hr)\b[^>]*>/gi, ' ')
    .replace(/<[^>]*>/g, '')
    .replace(/&nbsp;/g, ' ')
    .replace(/&lt;/g, '<')
    .replace(/&gt;/g, '>')
    .replace(/&quot;/g, '"')
    .replace(/&#39;/g, "'")
    .replace(/&amp;/g, '&')
    .replace(/\s+/g, ' ')
    .trim();
  return words.length > 80 ? `${words.slice(0, 79)}${String.fromCharCode(0x2026)}` : words;
}

/** A date's limit in words: "2026-12-31", "2 weeks from today", "3 days before today". */
export function boundText(term: EditorContext['term'], bound: DateBound): string {
  switch (bound.kind) {
    case 'none':
      return term('bound_none');
    case 'date':
      return bound.date;
    case 'today':
      return bound.count === 0
        ? term('boundToday')
        : term(bound.count > 0 ? 'boundAfter' : 'boundBefore', Math.abs(bound.count), term(`unit_${bound.unit}`));
    case 'steps':
      return term('boundSteps', bound.steps);
  }
}

/** A setting's value as one line of words, for a locked setting and a closed section's summary. */
export function settingText(
  context: Pick<EditorContext, 'say' | 'term' | 'culture'>,
  setting: FieldSettingResponseModel,
  value: unknown,
): string {
  const choice = (one: unknown) => {
    const found = setting.choices.find((each) => each.value === asText(one));
    return found ? context.say(found.label) : asText(one);
  };
  switch (setting.editor) {
    case 'boolean':
      return context.term(value === true ? 'on' : 'off');
    case 'choice':
      return choice(value);
    case 'list':
      return Array.isArray(value)
        ? value.map((one) => (setting.itemKind === 'choice' ? choice(one) : asText(one))).join(', ')
        : '';
    case 'dateBound':
      return boundText(context.term, readDateBound(value));
    case 'localizedText':
      return textIn(value as LocalizedText | undefined, context.culture);
    case 'localizedHtml':
    case 'inlineHtml':
      return plainOf(textIn(value as LocalizedText | undefined, context.culture));
    case 'documentTypePicker':
    case 'dataTypePicker': {
      // The site's items its choices name, by their names (ADR-0173, ADR-0192); a key no choice names is counted.
      const keys = pickedKeys(setting, value);
      const named = keys.map((key) => setting.choices.find((each) => each.value === key));
      if (keys.length > 0 && named.every((one) => one !== undefined))
        return named.map((one) => context.say(one?.label)).join(', ');
      return keys.length === 0 ? context.term('pickedNone') : context.term('picked', keys.length);
    }
    case 'mediaPicker':
    case 'contentPicker':
    case 'documentPicker': {
      const count = pickedKeys(setting, value).length;
      return count === 0 ? context.term('pickedNone') : context.term('picked', count);
    }
    case 'nested': {
      const own = (value && typeof value === 'object' ? value : {}) as Record<string, unknown>;
      return setting.settings
        .filter((inner) => !inner.lock?.hidden && Object.hasOwn(own, inner.name))
        .map((inner) => `${context.say(inner.label)}: ${settingText(context, inner, own[inner.name])}`)
        .join(', ');
    }
    default:
      return typeof value === 'object' && value !== null ? JSON.stringify(value) : asText(value);
  }
}

/** The label, the description and the problem's words around a setting's box, as every editor of a setting has them. */
export function settingFrame(
  context: EditorContext,
  setting: FieldSettingResponseModel,
  path: string,
  box: (described: string | undefined, invalid: boolean, held: boolean) => TemplateResult,
  extra?: string,
  dictionary?: { readonly said: string | null; readonly pick: (text: string) => void },
): TemplateResult {
  const id = boxId(path);
  const problem = context.problems.get(id);
  const found = context.found?.(id);
  const help = setting.description ? `${id}-help` : undefined;
  const why = problem ? `${id}-why` : undefined;
  const said = dictionary?.said ? `${id}-dictionary` : undefined;
  const described = [help, extra, said, why, ...(found?.ids ?? [])].filter(Boolean).join(' ') || undefined;
  const label = html`<label class="label" for=${id}>${context.say(setting.label)}</label>`;
  return html`<div class="item">
    ${
      dictionary
        ? html`<div class="label-row">
            ${label}
            <jfb-dictionary-pick .label=${context.say(setting.label)} .pick=${dictionary.pick}></jfb-dictionary-pick>
          </div>`
        : label
    }
    ${help ? html`<p class="help" id=${help}>${context.say(setting.description)}</p>` : nothing}
    ${box(described, problem !== undefined || (found?.error ?? false), problem !== undefined)}
    ${said ? html`<p class="help" id=${said}>${dictionary?.said}</p>` : nothing}
    ${problem ? html`<p class="why" id=${why ?? ''}>${problem}</p>` : nothing} ${found?.lines ?? nothing}
    ${setting.supportsMergeTags ? (context.mergeTags?.(setting, path) ?? nothing) : nothing}
  </div>`;
}

/**
 * The editor for one setting of a type's schema, at a path in the field's settings; the host's own for an editor it
 * brings, nothing for one neither draws.
 */
export function settingEditor(
  context: EditorContext,
  setting: FieldSettingResponseModel,
  value: unknown,
  path = setting.name,
): TemplateResult | typeof nothing {
  if (setting.lock?.hidden) return nothing;
  if (setting.lock) return lockedSetting(context, setting, value, path);
  const id = boxId(path);
  const typed = (raw: string, done: boolean) => context.set(path, setting, parseSetting(setting, raw), done);
  switch (setting.editor) {
    case 'boolean': {
      // A switch's help goes under it, as the sidebar's own Stop the rest has it.
      const help = setting.description ? `${id}-help` : undefined;
      const toggle = html`<input
          type="checkbox"
          role="switch"
          id=${id}
          aria-describedby=${help ?? nothing}
          .checked=${value === true}
          @change=${(event: Event) =>
            context.set(path, setting, { ok: true, value: (event.target as HTMLInputElement).checked }, true)}
        />
        <label class="label" for=${id}>${context.say(setting.label)}</label>`;
      if (!help) return html`<div class="item item-switch">${toggle}</div>`;
      return html`<div class="item">
        <div class="item-switch">${toggle}</div>
        <p class="help" id=${help}>${context.say(setting.description)}</p>
      </div>`;
    }
    case 'choice':
      return settingFrame(
        context,
        setting,
        path,
        (described, invalid) =>
          html`<select
            id=${id}
            aria-describedby=${described ?? nothing}
            aria-invalid=${invalid ? 'true' : 'false'}
            @change=${(event: Event) =>
              context.set(path, setting, { ok: true, value: (event.target as HTMLSelectElement).value }, true)}
          >
            ${setting.choices.map(
              (choice) =>
                html`<option value=${choice.value} ?selected=${asText(value) === choice.value}>
                  ${context.say(choice.label)}
                </option>`,
            )}
          </select>`,
      );
    case 'list':
      return setting.itemKind === 'choice'
        ? choiceList(context, setting, value, path)
        : textList(context, setting, value, path);
    case 'nested':
      return nested(context, setting, value, path);
    case 'dateBound':
      return dateBound(context, setting, value, path);
    case 'localizedText': {
      const texts = value as LocalizedText | undefined;
      const isDefault = context.culture === context.defaultCulture;
      const written = isDefault ? textIn(texts, context.culture) : ownText(texts, context.culture);
      // A host that says what a dictionary item shows offers the picker too (ADR-0228); the item chosen is written as
      // typing it would be.
      const dictionary = context.dictionaryLine
        ? {
            // An empty box in another language shows the default's text, so the line says what that shows there.
            said: context.dictionaryLine(written || textIn(texts, context.culture)),
            pick: (text: string) => {
              typed(text, true);
              context.dictionaryPicked?.();
            },
          }
        : undefined;
      return settingFrame(
        context,
        setting,
        path,
        (described, invalid, held) =>
          html`<input
            type="text"
            id=${id}
            lang=${context.culture || nothing}
            autocomplete="off"
            placeholder=${isDefault ? nothing : textIn(texts, context.defaultCulture) || nothing}
            aria-describedby=${described ?? nothing}
            aria-invalid=${invalid ? 'true' : 'false'}
            .value=${boxValue(written, held)}
            @input=${(event: Event) => typed((event.target as HTMLInputElement).value, false)}
            @change=${(event: Event) => typed((event.target as HTMLInputElement).value, true)}
          />`,
        undefined,
        dictionary,
      );
    }
    case 'wholeNumber':
    case 'number':
    case 'text':
    case 'time':
    case 'formula': {
      // A number with a unit, such as days, has the unit beside it, which a screen reader says with the box.
      const unit = setting.unit ? `${id}-unit` : undefined;
      return settingFrame(
        context,
        setting,
        path,
        (described, invalid, held) => {
          const box = html`<input
            type=${setting.editor === 'time' ? 'time' : 'text'}
            id=${id}
            class=${setting.editor === 'formula' ? 'code' : nothing}
            inputmode=${setting.editor === 'wholeNumber' ? 'numeric' : setting.editor === 'number' ? 'decimal' : nothing}
            spellcheck=${setting.editor === 'formula' ? 'false' : nothing}
            autocomplete="off"
            aria-describedby=${described ?? nothing}
            aria-invalid=${invalid ? 'true' : 'false'}
            .value=${boxValue(asText(value), held)}
            @input=${(event: Event) => typed((event.target as HTMLInputElement).value, false)}
            @change=${(event: Event) => typed((event.target as HTMLInputElement).value, true)}
          />`;
          return unit
            ? html`<div class="with-unit">${box}<span id=${unit}>${context.say(setting.unit)}</span></div>`
            : box;
        },
        unit,
      );
    }
    case 'mediaPicker':
    case 'contentPicker':
    case 'documentPicker':
      return picker(context, setting, value, path);
    case 'documentTypePicker':
    case 'dataTypePicker':
      return siteChoice(context, setting, value, path);
    case 'localizedHtml':
    case 'inlineHtml':
      return richText(context, setting, value, path);
    default:
      return context.editors?.[setting.editor]?.(setting, value, path) ?? nothing;
  }
}

/** The keys a picker's setting holds: a list's, or the one of a single key. */
export function pickedKeys(setting: FieldSettingResponseModel, value: unknown): string[] {
  if (setting.kind === 'list') return Array.isArray(value) ? value.map(asText).filter((key) => key !== '') : [];
  return typeof value === 'string' && value !== '' ? [value] : [];
}

// The site's document types and data types, which Umbraco's own pickers show only to users of its Settings section,
// come with the setting as its choices (ADR-0173): ticks where it takes several, a select where it takes one. One the
// site no longer has stays shown while it is picked, so it can be taken away.
function siteChoice(context: EditorContext, setting: FieldSettingResponseModel, value: unknown, path: string) {
  const id = boxId(path);
  const picked = pickedKeys(setting, value);
  const known = new Set(setting.choices.map((choice) => choice.value));
  const choices = [
    ...setting.choices.map((choice) => ({ value: choice.value, label: context.say(choice.label) })),
    ...picked.filter((key) => !known.has(key)).map((key) => ({ value: key, label: context.term('pickedGone') })),
  ];
  if (setting.kind !== 'list')
    return settingFrame(context, setting, path, (described, invalid) =>
      choices.length === 0
        ? html`<p class="help" id=${id}>${context.term('siteChoicesNone')}</p>`
        : html`<select
            id=${id}
            aria-describedby=${described ?? nothing}
            aria-invalid=${invalid ? 'true' : 'false'}
            @change=${(event: Event) => {
              const chosen = (event.target as HTMLSelectElement).value;
              context.set(path, setting, { ok: true, value: chosen === '' ? undefined : chosen }, true);
            }}
          >
            <option value="" ?disabled=${setting.rules.required} ?selected=${picked.length === 0}>
              ${context.term('pickedNone')}
            </option>
            ${choices.map(
              (choice) =>
                html`<option value=${choice.value} ?selected=${picked[0] === choice.value}>${choice.label}</option>`,
            )}
          </select>`,
    );
  const problem = context.problems.get(id);
  const help = setting.description ? `${id}-help` : undefined;
  const why = problem ? `${id}-why` : undefined;
  return html`<fieldset
    class="item item-group"
    id=${id}
    aria-describedby=${[help, why].filter(Boolean).join(' ') || nothing}
  >
    <legend class="label">${context.say(setting.label)}</legend>
    ${help ? html`<p class="help" id=${help}>${context.say(setting.description)}</p>` : nothing}
    ${choices.length === 0 ? html`<p class="help">${context.term('siteChoicesNone')}</p>` : nothing}
    ${choices.map(
      (choice, at) =>
        html`<div class="item-switch">
          <input
            type="checkbox"
            id=${`${id}-${at}`}
            .checked=${picked.includes(choice.value)}
            @change=${(event: Event) => {
              // The boxes as they are now, ticked faster than the field comes back drawn anew.
              const boxes = [
                ...((event.target as HTMLElement)
                  .closest('fieldset')
                  ?.querySelectorAll<HTMLInputElement>('input[type="checkbox"]') ?? []),
              ];
              const next = choices.map((each) => each.value).filter((_each, index) => boxes[index]?.checked);
              context.set(path, setting, parseChoices(setting, next), true);
            }}
          />
          <label for=${`${id}-${at}`}>${choice.label}</label>
        </div>`,
    )}
    ${problem ? html`<p class="why" id=${why ?? ''}>${problem}</p>` : nothing}
  </fieldset>`;
}

// Umbraco's own items, picked with Umbraco's own inputs (ADR-0173): each shows the items' names and opens Umbraco's
// picker; a list's setting takes as many as its rule allows, any other one item.
function picker(context: EditorContext, setting: FieldSettingResponseModel, value: unknown, path: string) {
  const id = boxId(path);
  const several = setting.kind === 'list';
  const selection = pickedKeys(setting, value);
  const max = several ? (setting.rules.maximumCount ?? Number.POSITIVE_INFINITY) : 1;
  const pick = (event: Event) => {
    const chosen = [...((event.target as { selection?: readonly string[] }).selection ?? [])];
    context.set(path, setting, { ok: true, value: several ? chosen : chosen[0] }, true);
  };
  const label = context.say(setting.label);
  const help = setting.description ? `${id}-help` : undefined;
  const problem = context.problems.get(id);
  const found = context.found?.(id);
  const described =
    [help, problem ? `${id}-why` : undefined, ...(found?.ids ?? [])].filter(Boolean).join(' ') || undefined;
  // Umbraco's input is no form control a label names: a group named by the label holds it, so its own Choose and Remove
  // are said with the setting they belong to.
  return html`<div class="item">
    <p class="label" id=${`${id}-label`}>${label}</p>
    ${help ? html`<p class="help" id=${help}>${context.say(setting.description)}</p>` : nothing}
    <div role="group" aria-labelledby=${`${id}-label`} aria-describedby=${described ?? nothing}>
      ${(() => {
        switch (setting.editor) {
          case 'mediaPicker':
            return html`<umb-input-media
              id=${id}
              .selection=${selection}
              .max=${max}
              @change=${pick}
            ></umb-input-media>`;
          default:
            return html`<umb-input-document
              id=${id}
              .selection=${selection}
              .max=${max}
              @change=${pick}
            ></umb-input-document>`;
        }
      })()}
    </div>
    ${problem ? html`<p class="why" id=${`${id}-why`}>${problem}</p>` : nothing} ${found?.lines ?? nothing}
  </div>`;
}

// Rich text in the language edited, on the inspector's own editor (ADR-0173): its label and help above it, said to
// screen readers as its name and description, and "Insert merge tag" under it where the setting takes tags. A text in a
// label is one line of words (inline); another language shows the default's words, without their markup, as its hint.
function richText(context: EditorContext, setting: FieldSettingResponseModel, value: unknown, path: string) {
  const id = boxId(path);
  const texts = value as LocalizedText | undefined;
  const isDefault = context.culture === context.defaultCulture;
  const label = context.say(setting.label);
  return html`<div class="item">
    <p class="label" id=${`${id}-label`}>${label}</p>
    ${setting.description ? html`<p class="help">${context.say(setting.description)}</p>` : nothing}
    <jfb-rich-text
      id=${id}
      .label=${label}
      .description=${setting.description ? context.say(setting.description) : ''}
      .language=${context.culture}
      .placeholder=${isDefault ? '' : plainOf(textIn(texts, context.defaultCulture))}
      .value=${isDefault ? textIn(texts, context.culture) : ownText(texts, context.culture)}
      .inline=${setting.editor === 'inlineHtml'}
      .change=${(text: string, done: boolean) => context.set(path, setting, { ok: true, value: text }, done)}
    ></jfb-rich-text>
    ${setting.supportsMergeTags ? (context.mergeTags?.(setting, path) ?? nothing) : nothing}
  </div>`;
}

// The host's value, read-only, with the configuration key that sets it (ADR-0060).
function lockedSetting(
  context: EditorContext,
  setting: FieldSettingResponseModel,
  value: unknown,
  path: string,
): TemplateResult {
  const id = boxId(path);
  const shown =
    value === undefined || value === null ? context.term('lockedDefault') : settingText(context, setting, value);
  return html`<div class="item item-locked" data-locked=${setting.name}>
    <span class="label" id=${`${id}-label`}>${context.say(setting.label)}</span>
    <p class="locked" id=${id}><uui-icon name="icon-lock" aria-hidden="true"></uui-icon><span>${shown}</span></p>
    <p class="help">${context.term('lockedBy')} <code>${setting.lock?.configurationKey ?? ''}</code></p>
  </div>`;
}

// A list of texts or dates, one a line.
function textList(context: EditorContext, setting: FieldSettingResponseModel, value: unknown, path: string) {
  const id = boxId(path);
  const lines = Array.isArray(value) ? value.map(asText).join('\n') : '';
  const typed = (raw: string, done: boolean) => context.set(path, setting, parseSetting(setting, raw), done);
  return settingFrame(
    context,
    setting,
    path,
    (described, invalid, held) =>
      html`<textarea
          id=${id}
          rows="3"
          spellcheck="false"
          aria-describedby=${described ?? nothing}
          aria-invalid=${invalid ? 'true' : 'false'}
          .value=${boxValue(lines, held)}
          @input=${(event: Event) => typed((event.target as HTMLTextAreaElement).value, false)}
          @change=${(event: Event) => typed((event.target as HTMLTextAreaElement).value, true)}
        ></textarea>
        <p class="help" id=${`${id}-lines`}>
          ${context.term(setting.itemKind === 'date' ? 'oneDateALine' : 'oneALine')}
        </p>`,
    `${id}-lines`,
  );
}

// A list of the setting's choices, each a box to tick.
function choiceList(context: EditorContext, setting: FieldSettingResponseModel, value: unknown, path: string) {
  const id = boxId(path);
  const chosen = Array.isArray(value) ? value.map(asText) : [];
  const problem = context.problems.get(id);
  return html`<fieldset class="item item-group" id=${id} aria-describedby=${problem ? `${id}-why` : nothing}>
    <legend class="label">${context.say(setting.label)}</legend>
    ${setting.choices.map(
      (choice, at) =>
        html`<div class="item-switch">
          <input
            type="checkbox"
            id=${`${id}-${at}`}
            .checked=${chosen.includes(choice.value)}
            @change=${(event: Event) => {
              const on = (event.target as HTMLInputElement).checked;
              const next = setting.choices
                .map((each) => each.value)
                .filter((each) => (each === choice.value ? on : chosen.includes(each)));
              context.set(path, setting, parseChoices(setting, next), true);
            }}
          />
          <label for=${`${id}-${at}`}>${context.say(choice.label)}</label>
        </div>`,
    )}
    ${problem ? html`<p class="why" id=${`${id}-why`}>${problem}</p>` : nothing}
  </fieldset>`;
}

// Settings that hold settings of their own, under the setting's name.
function nested(context: EditorContext, setting: FieldSettingResponseModel, value: unknown, path: string) {
  const own = (value && typeof value === 'object' ? value : {}) as Record<string, unknown>;
  return html`<fieldset class="item item-group">
    <legend class="label">${context.say(setting.label)}</legend>
    ${setting.settings.map((inner) =>
      settingEditor(
        context,
        inner,
        inner.lock && inner.lock.value !== undefined && inner.lock.value !== null
          ? inner.lock.value
          : Object.hasOwn(own, inner.name)
            ? own[inner.name]
            : (inner.default ?? undefined),
        `${path}.${inner.name}`,
      ),
    )}
  </fieldset>`;
}

// A date's limit: none, a date, or a number of days, weeks, months or years from today.
function dateBound(context: EditorContext, setting: FieldSettingResponseModel, value: unknown, path: string) {
  const id = boxId(path);
  const bound = readDateBound(value);
  const put = (next: DateBound) => context.set(path, setting, { ok: true, value: writeDateBound(next) }, true);
  const kind = bound.kind === 'steps' ? 'today' : bound.kind;
  return html`<fieldset class="item item-group item-bound">
    <legend class="label">${context.say(setting.label)}</legend>
    <select
      id=${id}
      aria-label=${context.term('boundKind', context.say(setting.label))}
      @change=${(event: Event) => {
        const chosen = (event.target as HTMLSelectElement).value;
        if (chosen === 'none') put({ kind: 'none' });
        else if (chosen === 'date') put({ kind: 'date', date: new Date().toISOString().slice(0, 10) });
        else put({ kind: 'today', count: 0, unit: 'd' });
      }}
    >
      ${(['none', 'date', 'today'] as const).map(
        (each) => html`<option value=${each} ?selected=${kind === each}>${context.term(`bound_${each}`)}</option>`,
      )}
    </select>
    ${
      bound.kind === 'date'
        ? html`<input
            type="date"
            id=${`${id}-date`}
            aria-label=${context.term('boundDate', context.say(setting.label))}
            .value=${bound.date}
            @change=${(event: Event) => {
              const date = (event.target as HTMLInputElement).value;
              if (date) put({ kind: 'date', date });
            }}
          />`
        : nothing
    }
    ${
      bound.kind === 'today'
        ? html`<div class="row">
              <input
                type="number"
                id=${`${id}-count`}
                step="1"
                min="-9999"
                max="9999"
                aria-label=${context.term('boundCount', context.say(setting.label))}
                .value=${String(bound.count)}
                @change=${(event: Event) => {
                  const count = Math.trunc(Number((event.target as HTMLInputElement).value));
                  if (Number.isFinite(count) && Math.abs(count) <= 9999) put({ ...bound, count });
                }}
              />
              <select
                id=${`${id}-unit`}
                aria-label=${context.term('boundUnit', context.say(setting.label))}
                @change=${(event: Event) => put({ ...bound, unit: (event.target as HTMLSelectElement).value as Unit })}
              >
                ${UNITS.map(
                  (unit) =>
                    html`<option value=${unit} ?selected=${bound.unit === unit}>
                      ${context.term(`unit_${unit}`)}
                    </option>`,
                )}
              </select>
            </div>
            <p class="help">${context.term('boundHelp')}</p>`
        : nothing
    }
    ${bound.kind === 'steps' ? html`<p class="help">${boundText(context.term, bound)}</p>` : nothing}
  </fieldset>`;
}
