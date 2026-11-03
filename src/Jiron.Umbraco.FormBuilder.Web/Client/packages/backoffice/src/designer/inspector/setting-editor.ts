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
  /** "Insert merge tag" under the box of a setting that takes tags, which puts a tag into it (ADR-0173). */
  readonly mergeTags?: (setting: FieldSettingResponseModel, path: string) => TemplateResult | typeof nothing;
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
    case 'mediaPicker':
    case 'contentPicker':
    case 'documentPicker':
    case 'documentTypePicker':
    case 'dataTypePicker': {
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

/** The label, the description and the problem's words around a setting's box. */
function frame(
  context: EditorContext,
  setting: FieldSettingResponseModel,
  path: string,
  box: (described: string | undefined, invalid: boolean) => TemplateResult,
  extra?: string,
): TemplateResult {
  const id = boxId(path);
  const problem = context.problems.get(id);
  const help = setting.description ? `${id}-help` : undefined;
  const why = problem ? `${id}-why` : undefined;
  const described = [help, extra, why].filter(Boolean).join(' ') || undefined;
  return html`<div class="item">
    <label class="label" for=${id}>${context.say(setting.label)}</label>
    ${help ? html`<p class="help" id=${help}>${context.say(setting.description)}</p>` : nothing}
    ${box(described, problem !== undefined)} ${problem ? html`<p class="why" id=${why ?? ''}>${problem}</p>` : nothing}
    ${setting.supportsMergeTags ? (context.mergeTags?.(setting, path) ?? nothing) : nothing}
  </div>`;
}

/** The editor for one setting of a type's schema, at a path in the field's settings; nothing for one 9.4a doesn't draw. */
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
    case 'boolean':
      return html`<div class="item item-switch">
        <input
          type="checkbox"
          role="switch"
          id=${id}
          .checked=${value === true}
          @change=${(event: Event) =>
            context.set(path, setting, { ok: true, value: (event.target as HTMLInputElement).checked }, true)}
        />
        <label class="label" for=${id}>${context.say(setting.label)}</label>
      </div>`;
    case 'choice':
      return frame(
        context,
        setting,
        path,
        (described) =>
          html`<select
            id=${id}
            aria-describedby=${described ?? nothing}
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
      return frame(
        context,
        setting,
        path,
        (described, invalid) =>
          html`<input
            type="text"
            id=${id}
            lang=${context.culture || nothing}
            autocomplete="off"
            placeholder=${isDefault ? nothing : textIn(texts, context.defaultCulture) || nothing}
            aria-describedby=${described ?? nothing}
            aria-invalid=${invalid ? 'true' : 'false'}
            .value=${boxValue(isDefault ? textIn(texts, context.culture) : ownText(texts, context.culture), invalid)}
            @input=${(event: Event) => typed((event.target as HTMLInputElement).value, false)}
            @change=${(event: Event) => typed((event.target as HTMLInputElement).value, true)}
          />`,
      );
    }
    case 'wholeNumber':
    case 'number':
    case 'text':
    case 'time':
    case 'formula':
      return frame(
        context,
        setting,
        path,
        (described, invalid) =>
          html`<input
            type=${setting.editor === 'time' ? 'time' : 'text'}
            id=${id}
            class=${setting.editor === 'formula' ? 'code' : nothing}
            inputmode=${setting.editor === 'wholeNumber' ? 'numeric' : setting.editor === 'number' ? 'decimal' : nothing}
            spellcheck=${setting.editor === 'formula' ? 'false' : nothing}
            autocomplete="off"
            aria-describedby=${described ?? nothing}
            aria-invalid=${invalid ? 'true' : 'false'}
            .value=${boxValue(asText(value), invalid)}
            @input=${(event: Event) => typed((event.target as HTMLInputElement).value, false)}
            @change=${(event: Event) => typed((event.target as HTMLInputElement).value, true)}
          />`,
      );
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
      return nothing;
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
    return frame(context, setting, path, (described) =>
      choices.length === 0
        ? html`<p class="help" id=${id}>${context.term('siteChoicesNone')}</p>`
        : html`<select
            id=${id}
            aria-describedby=${described ?? nothing}
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
  const described = [help, problem ? `${id}-why` : undefined].filter(Boolean).join(' ') || undefined;
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
    ${problem ? html`<p class="why" id=${`${id}-why`}>${problem}</p>` : nothing}
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
  return frame(
    context,
    setting,
    path,
    (described, invalid) =>
      html`<textarea
          id=${id}
          rows="3"
          spellcheck="false"
          aria-describedby=${described ?? nothing}
          aria-invalid=${invalid ? 'true' : 'false'}
          .value=${boxValue(lines, invalid)}
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
