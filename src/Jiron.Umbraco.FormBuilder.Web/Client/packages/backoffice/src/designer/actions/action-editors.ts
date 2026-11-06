// The editors of the action types' settings that the inspector has none of (ROADMAP 10.2b, ADR-0189): a field picker
// that offers the draft's fields of the setting's kind; addresses as a list with a box that adds them; the email's To
// box, which holds every kind of recipient the schema puts within it (addresses, an email field's answer, the person
// who created the form, the addresses an answer chooses) and offers the others under Add recipient; the addresses an
// answer chooses on their own, where a host locks the To box; and the layout. Each is labelled, described and says its
// problems as the inspector's editors do, under ids the form check's Fix it lands on.
import { isEmailAddress } from '@jfb/logic';
import { css, html, live, nothing, type TemplateResult } from '@umbraco-cms/backoffice/external/lit';
import type { FieldSettingResponseModel } from '../../api';
import { boxId, pickedKeys, settingEditor, settingFrame, type EditorContext } from '../inspector/setting-editor';

/** A field of the draft as a picker offers it. */
export interface PickField {
  readonly key: string;
  /** Its label in the language shown, or its alias. */
  readonly label: string;
  /** Whether it is of the email type. */
  readonly email: boolean;
  /** Whether it has options. */
  readonly choice: boolean;
  /** Whether it stores files. */
  readonly file: boolean;
  readonly sensitive: boolean;
  /** Whether it sits in a group, not inside a repeatable group, so it has one answer. */
  readonly top: boolean;
  /** Its options, each with its label; null while those of a source are read. */
  readonly options: readonly PickOption[] | null;
}

export interface PickOption {
  readonly value: string;
  readonly label: string;
}

/** A layout a mail may name: its name a `#` key for a built-in one. */
export interface LayoutChoice {
  readonly key: string;
  readonly name: string;
}

/** A setting a box's editor draws within it, with its value. */
export interface Within {
  readonly setting: FieldSettingResponseModel;
  readonly value: unknown;
}

/** What the action editors need of the sidebar, beyond the inspector's context. */
export interface ActionEditorContext {
  readonly editor: EditorContext;
  /** A term of the actions' dictionary area, without its `jfbActions_`. */
  readonly words: (key: string, ...args: unknown[]) => string;
  /** The draft's fields that hold a value, in document order. */
  readonly fields: readonly PickField[];
  readonly layouts: readonly LayoutChoice[];
  /** The key of the layout a mail has without one of its own. */
  readonly defaultLayout: string;
  /** The settings drawn within a box, by the box's name. */
  readonly within: (box: string) => readonly Within[];
  /** Puts the focus on the element with this id once the sidebar is drawn anew. */
  readonly focus: (id: string) => void;
  /** Says why what was typed under a box can't be kept, or that it can (null). */
  readonly problem: (id: string, text: string | null) => void;
  /** Asks for the options of a field whose options come from a source. */
  readonly options: (field: string) => void;
  /** The addresses each choice field's options had while the sidebar is open, by the field's key. */
  readonly routes: Map<string, readonly RecipientRoute[]>;
}

export type ActionEditor = (
  setting: FieldSettingResponseModel,
  value: unknown,
  path: string,
) => TemplateResult | typeof nothing;

/** The editors by their aliases, for the inspector's `editors`. */
export function actionEditors(context: ActionEditorContext): Readonly<Record<string, ActionEditor>> {
  return {
    fieldPicker: (setting, value, path) => fieldPicker(context, setting, value, path),
    addresses: (setting, value, path) => addresses(context, setting, value, path),
    recipients: (setting, value, path) => recipients(context, setting, value, path),
    recipientMap: (setting, value, path) => mapAlone(context, setting, value, path),
    emailLayoutPicker: (setting, value, path) => layoutPicker(context, setting, value, path),
  };
}

/** The look of the editors, beside the inspector's control styles. */
export const actionEditorStyles = css`
  .chips {
    display: flex;
    flex-wrap: wrap;
    gap: var(--uui-size-space-2);
    margin: 0;
    padding: 0;
    list-style: none;
  }

  .chip {
    display: inline-flex;
    align-items: center;
    gap: var(--uui-size-space-1);
    max-inline-size: 100%;
    padding-inline-start: var(--uui-size-space-3);
    border: 1px solid var(--uui-color-border-emphasis);
    border-radius: var(--jfb-bo-radius-round);
    background: var(--uui-color-surface);
  }

  .chip-text {
    overflow-wrap: anywhere;
  }

  .remove {
    display: inline-grid;
    flex: none;
    place-items: center;
    inline-size: 32px;
    block-size: 32px;
    padding: 0;
    border: 0;
    border-radius: var(--jfb-bo-radius-round);
    background: none;
    color: var(--jfb-bo-color-text-muted);
    cursor: pointer;
  }

  @media (hover: hover) {
    .remove:hover {
      background: var(--uui-color-surface-emphasis);
      color: var(--uui-color-text);
    }

    .add-kind:hover,
    .add > summary:hover {
      color: var(--uui-color-interactive-emphasis);
    }
  }

  .remove:focus-visible,
  .add-kind:focus-visible,
  .add > summary:focus-visible {
    outline: 2px solid var(--jfb-bo-color-focus);
    outline-offset: 2px;
  }

  .address-add {
    display: flex;
    gap: var(--uui-size-space-2);
  }

  .address-add input {
    flex: 1 1 auto;
    min-inline-size: 0;
  }

  /* A recipient of another kind: what it is, its choice, and its remove button, on one line where there is room. */
  .recipient {
    display: flex;
    flex-wrap: wrap;
    align-items: center;
    gap: var(--uui-size-space-2) var(--uui-size-space-3);
    padding: var(--uui-size-space-2) var(--uui-size-space-2) var(--uui-size-space-2) var(--uui-size-space-4);
    border: 1px solid var(--uui-color-border);
    border-radius: var(--uui-border-radius);
    background: var(--uui-color-surface);
  }

  .recipient > select {
    flex: 1 1 12rem;
    inline-size: auto;
    min-inline-size: 0;
  }

  .recipient > .grow {
    flex: 1 1 auto;
  }

  .recipient > .help,
  .recipient > .why,
  .recipient > .routes {
    flex-basis: 100%;
  }

  .routes {
    display: grid;
    grid-template-columns: minmax(0, 10rem) minmax(0, 1fr);
    align-items: center;
    gap: var(--uui-size-space-2) var(--uui-size-space-3);
    padding-inline-start: var(--uui-size-space-4);
    border-inline-start: 2px solid var(--uui-color-border);
  }

  .routes > .help {
    grid-column: 1 / -1;
  }

  .routes label {
    overflow-wrap: anywhere;
  }

  @container (max-width: 480px) {
    .routes {
      grid-template-columns: minmax(0, 1fr);
    }
  }

  .routes > .why {
    grid-column: 2;
  }

  @container (max-width: 480px) {
    .routes > .why {
      grid-column: 1;
    }
  }

  .add {
    display: grid;
    gap: var(--uui-size-space-3);
  }

  .add > summary,
  .add-kind {
    justify-self: start;
    min-block-size: 36px;
    padding: 0 var(--uui-size-space-2);
    border: 0;
    border-radius: var(--uui-border-radius);
    background: none;
    color: var(--uui-color-interactive);
    font: inherit;
    font-weight: 600;
    line-height: 36px;
    cursor: pointer;
  }

  .add > summary {
    list-style: none;
  }

  .add > summary::-webkit-details-marker {
    display: none;
  }

  .add > summary uui-icon,
  .add-kind uui-icon {
    margin-inline-end: var(--uui-size-space-1);
    vertical-align: -2px;
  }

  .add-panel {
    display: grid;
    gap: var(--uui-size-space-2);
    padding: var(--uui-size-space-4);
    border: 1px solid var(--uui-color-border);
    border-radius: var(--uui-border-radius);
  }

  .kinds {
    display: grid;
    justify-items: start;
    margin-block-start: var(--uui-size-space-2);
  }
`;

// ---- Values -----------------------------------------------------------------------------------------------------

/** The fields a picker of this kind offers: an email field's answer and a choice's are one answer each. */
export function fieldsOfKind(fields: readonly PickField[], picks: string | null | undefined): PickField[] {
  switch (picks) {
    case 'email':
      return fields.filter((field) => field.email && field.top);
    case 'choice':
      return fields.filter((field) => field.choice && field.top);
    case 'file':
      return fields.filter((field) => field.file);
    case 'sensitive':
      return fields.filter((field) => field.sensitive);
    default:
      return [...fields];
  }
}

/** The addresses typed in a box, apart by commas, semicolons or spaces, and the first of them that is no address. */
export function readAddresses(raw: string): { readonly addresses: string[]; readonly wrong: string | null } {
  const addresses = raw.split(/[\s,;]+/).filter((part) => part !== '');
  return { addresses, wrong: addresses.find((address) => !isEmailAddress(address)) ?? null };
}

/** A list of addresses with others added after them, each once whatever its letter case. */
export function withAddresses(list: readonly string[], added: readonly string[]): string[] {
  const seen = new Set(list.map((address) => address.toLowerCase()));
  const next = [...list];
  for (const address of added) {
    if (seen.has(address.toLowerCase())) continue;
    seen.add(address.toLowerCase());
    next.push(address);
  }
  return next;
}

/** A recipient map as the setting holds it. */
export interface RecipientMap {
  readonly field: string;
  readonly routes: readonly RecipientRoute[];
}

export interface RecipientRoute {
  readonly value: string;
  readonly to: readonly string[];
}

/** The recipient map a setting holds, or null for none, or one that is no map. */
export function readMap(value: unknown): RecipientMap | null {
  if (!value || typeof value !== 'object' || Array.isArray(value)) return null;
  const { field, routes } = value as { field?: unknown; routes?: unknown };
  if (typeof field !== 'string') return null;
  const read = Array.isArray(routes)
    ? routes.flatMap((route): RecipientRoute[] => {
        if (!route || typeof route !== 'object') return [];
        const { value: option, to } = route as { value?: unknown; to?: unknown };
        return typeof option === 'string'
          ? [
              {
                value: option,
                to: Array.isArray(to) ? to.filter((each): each is string => typeof each === 'string') : [],
              },
            ]
          : [];
      })
    : [];
  return { field, routes: read };
}

/**
 * The map with an option's addresses: its route replaced, or added, or gone without addresses. The routes follow the
 * options' order; one of a value that is no option any more stays after them, so it can be emptied.
 */
export function withRoute(
  map: RecipientMap,
  value: string,
  to: readonly string[],
  order: readonly string[],
): RecipientMap {
  const others = map.routes.filter((route) => route.value !== value);
  const routes = to.length > 0 ? [...others, { value, to: [...to] }] : others;
  const at = (route: RecipientRoute) => {
    const index = order.indexOf(route.value);
    return index < 0 ? order.length : index;
  };
  return {
    field: map.field,
    routes: routes
      .map((route, index) => ({ route, index }))
      .sort((a, b) => at(a.route) - at(b.route) || a.index - b.index)
      .map((each) => each.route),
  };
}

/** A field's label by its key, or the words for one that is gone. */
function labelOf(context: ActionEditorContext, key: string): string {
  return context.fields.find((field) => field.key === key)?.label ?? context.words('pickGone');
}

/**
 * A picked field the picker does not offer: one in a repeatable group, or of another kind, by its label and what it is
 * not; one the draft no longer has, said gone.
 */
export function unoffered(
  context: Pick<ActionEditorContext, 'fields' | 'words'>,
  key: string,
  picks: string | null | undefined,
): string {
  const field = context.fields.find((each) => each.key === key);
  if (!field || !picks || picks === 'any') return field?.label ?? context.words('pickGone');
  const ofKind = fieldsOfKind([{ ...field, top: true }], picks).length > 0;
  return context.words(ofKind ? 'pickNested' : `pickWrong_${picks}`, field.label);
}

// ---- Read-only ----------------------------------------------------------------------------------------------------

/** A setting's value in words, for who may not change it; undefined for a setting of another editor. */
export function actionSettingText(
  context: ActionEditorContext,
  setting: FieldSettingResponseModel,
  value: unknown,
): string | undefined {
  const list = Array.isArray(value) ? value.map(String) : [];
  switch (setting.editor) {
    case 'fieldPicker':
      return pickedKeys(setting, value)
        .map((key) => labelOf(context, key))
        .join(', ');
    case 'addresses':
      return list.join(', ');
    case 'recipients':
      return [...list, ...context.within(setting.name).flatMap((part) => recipientWords(context, part))].join(', ');
    case 'recipientMap': {
      const map = readMap(value);
      return map ? context.words('recipientOf_map', labelOf(context, map.field)) : '';
    }
    case 'emailLayoutPicker':
      return layoutName(context, typeof value === 'string' ? value : '');
    default:
      return undefined;
  }
}

/** A kind of recipient within the To box in words, as who may not change it reads it; none while it has none. */
function recipientWords(context: ActionEditorContext, part: Within): string[] {
  switch (part.setting.editor) {
    case 'fieldPicker':
      return pickedKeys(part.setting, part.value).map((key) =>
        context.words('recipientOf_field', labelOf(context, key)),
      );
    case 'boolean':
      return part.value === true ? [context.words('recipientCreator')] : [];
    default: {
      const text = actionSettingText(context, part.setting, part.value);
      return text ? [text] : [];
    }
  }
}

// ---- Field picker -------------------------------------------------------------------------------------------------

// The draft's fields of the setting's kind: a select with None for one field, boxes to tick for a list. A field the
// draft no longer has, or one of another kind, stays shown while it is picked, so it can be taken away.
function fieldPicker(context: ActionEditorContext, setting: FieldSettingResponseModel, value: unknown, path: string) {
  const id = boxId(path);
  const words = context.words;
  const offered = fieldsOfKind(context.fields, setting.picks);
  const picked = pickedKeys(setting, value);
  const known = new Set(offered.map((field) => field.key));
  const choices = [
    ...offered.map((field) => ({ value: field.key, label: field.label })),
    ...picked
      .filter((key) => !known.has(key))
      .map((key) => ({ value: key, label: unoffered(context, key, setting.picks) })),
  ];
  const none = html`<p class="help" id=${id} tabindex="-1">${words(`pickNoneOf_${setting.picks ?? 'any'}`)}</p>`;
  if (setting.kind !== 'list')
    return settingFrame(context.editor, setting, path, (described, invalid) =>
      choices.length === 0
        ? none
        : html`<select
            id=${id}
            aria-describedby=${described ?? nothing}
            aria-invalid=${invalid ? 'true' : 'false'}
            .value=${live(picked[0] ?? '')}
            @change=${(event: Event) => {
              const chosen = (event.target as HTMLSelectElement).value;
              context.editor.set(path, setting, { ok: true, value: chosen === '' ? undefined : chosen }, true);
            }}
          >
            <option value="" ?selected=${picked.length === 0}>${words('pickNone')}</option>
            ${choices.map((choice) => html`<option value=${choice.value} ?selected=${picked[0] === choice.value}>${choice.label}</option>`)}
          </select>`,
    );
  const found = context.editor.found?.(id);
  const help = setting.description ? `${id}-help` : undefined;
  return html`<fieldset
    class="item item-group"
    id=${id}
    aria-describedby=${[help, ...(found?.ids ?? [])].filter(Boolean).join(' ') || nothing}
  >
    <legend class="label">${context.editor.say(setting.label)}</legend>
    ${help ? html`<p class="help" id=${help}>${context.editor.say(setting.description)}</p>` : nothing}
    ${choices.length === 0 ? html`<p class="help">${words(`pickNoneOf_${setting.picks ?? 'any'}`)}</p>` : nothing}
    ${choices.map(
      (choice, at) =>
        html`<div class="item-switch">
          <input
            type="checkbox"
            id=${`${id}-${at}`}
            .checked=${live(picked.includes(choice.value))}
            @change=${(event: Event) => {
              const on = (event.target as HTMLInputElement).checked;
              const next = choices
                .map((each) => each.value)
                .filter((each) => (each === choice.value ? on : picked.includes(each)));
              context.editor.set(path, setting, { ok: true, value: next.length > 0 ? next : undefined }, true);
            }}
          />
          <label for=${`${id}-${at}`}>${choice.label}</label>
        </div>`,
    )}
    ${found?.lines ?? nothing}
  </fieldset>`;
}

// ---- Addresses ----------------------------------------------------------------------------------------------------

/** Addresses as chips, each with its remove button, their list named where no legend names them; nothing for none. */
function chips(
  context: ActionEditorContext,
  id: string,
  label: string | null,
  list: readonly string[],
  remove: (index: number) => void,
) {
  if (list.length === 0) return nothing;
  return html`<ul class="chips" aria-label=${label ?? nothing}>
    ${list.map(
      (address, index) =>
        html`<li class="chip">
          <span class="chip-text">${address}</span>
          <button
            type="button"
            class="remove"
            id=${`${id}-remove-${index}`}
            aria-label=${context.words('addressRemove', address)}
            @click=${() => remove(index)}
          >
            <uui-icon name="icon-wrong" aria-hidden="true"></uui-icon>
          </button>
        </li>`,
    )}
  </ul>`;
}

/**
 * The box that adds addresses: one or several at once, apart by commas, as Enter, Add or leaving the box keeps them; one
 * that is no address keeps what was typed and says why under the editor's box. That several go at once is said under
 * the To box's, once on the sidebar (section 4.13); Copy to's boxes take them the same way.
 */
function addressBox(
  context: ActionEditorContext,
  id: string,
  problemAt: string,
  described: string | undefined,
  invalid: boolean,
  add: (addresses: string[]) => void,
  sayHow = false,
) {
  const keep = (input: HTMLInputElement | null) => {
    if (!input) return;
    const { addresses, wrong } = readAddresses(input.value);
    if (wrong !== null) {
      context.problem(problemAt, context.words('notAnAddress', wrong));
      return;
    }
    context.problem(problemAt, null);
    if (addresses.length === 0) return;
    add(addresses);
    input.value = '';
  };
  return html`<div class="address-add">
      <input
        type="text"
        inputmode="email"
        id=${id}
        autocomplete="off"
        spellcheck="false"
        aria-describedby=${[described, sayHow ? `${id}-several` : undefined].filter(Boolean).join(' ') || nothing}
        aria-invalid=${invalid ? 'true' : 'false'}
        @keydown=${(event: KeyboardEvent) => {
          if (event.key !== 'Enter') return;
          event.preventDefault();
          keep(event.target as HTMLInputElement);
        }}
        @change=${(event: Event) => keep(event.target as HTMLInputElement)}
      />
      <uui-button
        look="outline"
        label=${context.words('addressAddNamed')}
        @click=${(event: Event) =>
          keep((event.target as HTMLElement).parentElement?.querySelector<HTMLInputElement>('input') ?? null)}
        >${context.words('addressAdd')}</uui-button
      >
    </div>
    ${sayHow ? html`<p class="help" id=${`${id}-several`}>${context.words('addressHelp')}</p>` : nothing}`;
}

/** The line under a box that says why what was typed in it can't be kept. */
function typedLine(context: ActionEditorContext, id: string) {
  const text = context.editor.problems.get(id);
  return text ? html`<p class="why" id=${`${id}-why`}>${text}</p>` : nothing;
}

/** After a chip is removed, its place's chip takes the focus, or the one before it, or the box. */
function focusAfterRemove(context: ActionEditorContext, id: string, input: string, index: number, left: number) {
  context.focus(left === 0 ? input : `${id}-remove-${Math.min(index, left - 1)}`);
}

// Fixed addresses, such as Copy to: their chips, then the box that adds them.
function addresses(context: ActionEditorContext, setting: FieldSettingResponseModel, value: unknown, path: string) {
  const id = boxId(path);
  const list = Array.isArray(value) ? value.map(String) : [];
  const put = (next: readonly string[]) =>
    context.editor.set(path, setting, { ok: true, value: next.length > 0 ? [...next] : undefined }, true);
  const label = context.editor.say(setting.label);
  return settingFrame(
    context.editor,
    setting,
    path,
    (described, invalid) =>
      html`${chips(context, id, label, list, (index) => {
        put(list.filter((_, at) => at !== index));
        focusAfterRemove(context, id, id, index, list.length - 1);
      })}
      ${addressBox(context, id, id, described, invalid, (added) => put(withAddresses(list, added)))}`,
  );
}

// ---- The To box ---------------------------------------------------------------------------------------------------

// The problems the check found at a part of the To box, said under it.
function foundAt(context: ActionEditorContext, id: string) {
  const typed = context.editor.problems.get(id);
  const found = context.editor.found?.(id);
  return {
    ids: [...(typed ? [`${id}-why`] : []), ...(found?.ids ?? [])],
    invalid: typed !== undefined || (found?.error ?? false),
    lines: html`${typed ? html`<p class="why" id=${`${id}-why`}>${typed}</p>` : nothing}${found?.lines ?? nothing}`,
  };
}

// Who gets the mail, in one box: the addresses as chips, then each other kind of recipient the schema puts within it,
// and Add recipient, which offers the kinds not there yet; a kind a host locks shows as the host set it.
function recipients(context: ActionEditorContext, setting: FieldSettingResponseModel, value: unknown, path: string) {
  const id = boxId(path);
  const words = context.words;
  const list = Array.isArray(value) ? value.map(String) : [];
  const parts = context.within(setting.name);
  const part = (name: string) => parts.find((each) => each.setting.name === name);
  const field = part('toField');
  const creator = part('toCreator');
  const map = part('recipientMap');
  const put = (next: readonly string[]) =>
    context.editor.set(path, setting, { ok: true, value: next.length > 0 ? [...next] : undefined }, true);
  const set = (within: Within, next: unknown) =>
    context.editor.set(within.setting.name, within.setting, { ok: true, value: next }, true);
  const emails = fieldsOfKind(context.fields, field?.setting.picks ?? 'email');
  const choices = fieldsOfKind(context.fields, 'choice');
  const free = (within: Within | undefined) => within !== undefined && !within.setting.lock;
  const closeAnd = (event: Event, then: () => void) => {
    const details = (event.target as HTMLElement).closest('details');
    if (details) details.open = false;
    then();
  };
  const label = context.editor.say(setting.label);
  const own = foundAt(context, id);
  const help = setting.description ? `${id}-help` : undefined;
  const kinds = [
    free(field) && !field?.value && emails.length > 0
      ? html`<button
          type="button"
          class="add-kind"
          @click=${(event: Event) =>
            closeAnd(event, () => {
              if (!field) return;
              set(field, emails[0]?.key);
              context.focus(boxId(field.setting.name));
            })}
        >
          <uui-icon name="icon-add" aria-hidden="true"></uui-icon>${words('recipientField')}
        </button>`
      : nothing,
    free(creator) && creator?.value !== true
      ? html`<button
          type="button"
          class="add-kind"
          @click=${(event: Event) =>
            closeAnd(event, () => {
              if (!creator) return;
              set(creator, true);
              context.focus(boxId(creator.setting.name));
            })}
        >
          <uui-icon name="icon-add" aria-hidden="true"></uui-icon>${words('recipientCreator')}
        </button>`
      : nothing,
    free(map) && !readMap(map?.value) && choices.length > 0
      ? html`<button
          type="button"
          class="add-kind"
          @click=${(event: Event) =>
            closeAnd(event, () => {
              if (!map || !choices[0]) return;
              set(map, { field: choices[0].key, routes: [] });
              context.options(choices[0].key);
              context.focus(boxId(map.setting.name));
            })}
        >
          <uui-icon name="icon-add" aria-hidden="true"></uui-icon>${words('recipientMap')}
        </button>`
      : nothing,
  ];
  return html`<fieldset
    class="item item-group recipients"
    aria-describedby=${[help, ...own.ids].filter(Boolean).join(' ') || nothing}
  >
    <legend class="label">${label}</legend>
    ${help ? html`<p class="help" id=${help}>${context.editor.say(setting.description)}</p>` : nothing}
    ${chips(context, id, null, list, (index) => {
      put(list.filter((_, at) => at !== index));
      focusAfterRemove(context, id, id, index, list.length - 1);
    })}
    ${field ? fieldRow(context, field, emails, () => context.focus(id)) : nothing}
    ${creator ? creatorRow(context, creator, () => context.focus(id)) : nothing}
    ${map ? mapRow(context, map, choices, false, () => context.focus(id)) : nothing}
    <details class="add">
      <summary id=${id}><uui-icon name="icon-add" aria-hidden="true"></uui-icon>${words('recipientsAdd')}</summary>
      <div class="add-panel">
        <label class="label" for=${`${id}-address`}>${words('recipientAddress')}</label>
        ${addressBox(context, `${id}-address`, `${id}-address`, context.editor.problems.has(`${id}-address`) ? `${id}-address-why` : undefined, context.editor.problems.has(`${id}-address`), (added) => put(withAddresses(list, added)), true)}
        ${typedLine(context, `${id}-address`)}
        <div class="kinds">${kinds}</div>
      </div>
    </details>
    ${own.lines}
  </fieldset>`;
}

// The answer of an email field: which field, and its remove button; nothing while it has none.
function fieldRow(context: ActionEditorContext, field: Within, offered: readonly PickField[], removed: () => void) {
  if (field.setting.lock) return settingEditor(context.editor, field.setting, field.value, field.setting.name);
  if (typeof field.value !== 'string' || field.value === '') return nothing;
  const id = boxId(field.setting.name);
  const key = field.value;
  const known = offered.some((each) => each.key === key);
  const shown = foundAt(context, id);
  return html`<div class="recipient">
    <label for=${id}>${context.words('recipientFieldRow')}</label>
    <select
      id=${id}
      aria-describedby=${shown.ids.join(' ') || nothing}
      aria-invalid=${shown.invalid ? 'true' : 'false'}
      .value=${live(key)}
      @change=${(event: Event) =>
        context.editor.set(
          field.setting.name,
          field.setting,
          { ok: true, value: (event.target as HTMLSelectElement).value },
          true,
        )}
    >
      ${known ? nothing : html`<option value=${key} selected>${unoffered(context, key, field.setting.picks)}</option>`}
      ${offered.map((each) => html`<option value=${each.key} ?selected=${each.key === key}>${each.label}</option>`)}
    </select>
    <button
      type="button"
      class="remove"
      aria-label=${context.words('removeFieldRecipient', labelOf(context, key))}
      @click=${() => {
        context.editor.set(field.setting.name, field.setting, { ok: true, value: undefined }, true);
        removed();
      }}
    >
      <uui-icon name="icon-wrong" aria-hidden="true"></uui-icon>
    </button>
    ${shown.lines}
  </div>`;
}

// The person who created the form, and its remove button; nothing while the mail does not go to them.
function creatorRow(context: ActionEditorContext, creator: Within, removed: () => void) {
  if (creator.setting.lock) return settingEditor(context.editor, creator.setting, creator.value, creator.setting.name);
  if (creator.value !== true) return nothing;
  const id = boxId(creator.setting.name);
  const shown = foundAt(context, id);
  return html`<div class="recipient">
    <span class="grow" id=${`${id}-label`}>${context.words('recipientCreator')}</span>
    <button
      type="button"
      class="remove"
      id=${id}
      aria-label=${context.words('removeCreator')}
      aria-describedby=${shown.ids.join(' ') || nothing}
      @click=${() => {
        context.editor.set(creator.setting.name, creator.setting, { ok: true, value: false }, true);
        removed();
      }}
    >
      <uui-icon name="icon-wrong" aria-hidden="true"></uui-icon>
    </button>
    ${shown.lines}
  </div>`;
}

// The addresses an answer chooses: the choice field, and for each of its options the addresses it sends to. In the To
// box it has a remove button; on its own, the choice of field has None.
function mapRow(
  context: ActionEditorContext,
  map: Within,
  offered: readonly PickField[],
  alone: boolean,
  removed: () => void,
) {
  if (map.setting.lock) return settingEditor(context.editor, map.setting, map.value, map.setting.name);
  const read = readMap(map.value);
  if (!read && !alone) return nothing;
  const id = boxId(map.setting.name);
  const words = context.words;
  const name = map.setting.name;
  const put = (next: RecipientMap | undefined) =>
    context.editor.set(name, map.setting, { ok: true, value: next }, true);
  const shown = foundAt(context, id);
  const chosen = read ? context.fields.find((field) => field.key === read.field) : undefined;
  if (read && chosen && chosen.options === null) context.options(chosen.key);
  const known = read ? offered.some((field) => field.key === read.field) : true;
  const options = chosen?.options ?? [];
  const values = options.map((option) => option.value);
  const gone = read ? read.routes.filter((route) => !values.includes(route.value)) : [];
  const route = (value: string) => read?.routes.find((each) => each.value === value)?.to.join(', ') ?? '';
  const keep = (value: string, input: HTMLInputElement) => {
    if (!read) return;
    const { addresses: typed, wrong } = readAddresses(input.value);
    if (wrong !== null) {
      context.problem(input.id, words('notAnAddress', wrong));
      return;
    }
    context.problem(input.id, null);
    put(withRoute(read, value, withAddresses([], typed), values));
  };
  const row = (value: string, label: string, at: number) => {
    const box = `${id}-route-${at}`;
    const why = context.editor.problems.get(box);
    return html`<label for=${box}>${label}</label>
      <input
        type="text"
        inputmode="email"
        id=${box}
        autocomplete="off"
        spellcheck="false"
        aria-describedby=${[`${id}-routes-help`, why ? `${box}-why` : undefined].filter(Boolean).join(' ')}
        aria-invalid=${why ? 'true' : 'false'}
        .value=${route(value)}
        @change=${(event: Event) => keep(value, event.target as HTMLInputElement)}
      />
      ${typedLine(context, box)}`;
  };
  return html`<div class="recipient">
    <label for=${id}>${words(alone ? 'recipientMapAlone' : 'recipientMapRow')}</label>
    <select
      id=${id}
      aria-describedby=${shown.ids.join(' ') || nothing}
      aria-invalid=${shown.invalid ? 'true' : 'false'}
      .value=${live(read?.field ?? '')}
      @change=${(event: Event) => {
        const field = (event.target as HTMLSelectElement).value;
        // Another field has other options: the addresses of the one before stay with it, and come back with it.
        if (read) context.routes.set(read.field, read.routes);
        if (field === '') {
          put(undefined);
          return;
        }
        context.options(field);
        put({ field, routes: context.routes.get(field) ?? [] });
      }}
    >
      ${alone ? html`<option value="" ?selected=${!read}>${words('pickNone')}</option>` : nothing}
      ${known || !read ? nothing : html`<option value=${read.field} selected>${unoffered(context, read.field, 'choice')}</option>`}
      ${offered.map((field) => html`<option value=${field.key} ?selected=${field.key === read?.field}>${field.label}</option>`)}
    </select>
    ${
      alone
        ? nothing
        : html`<button
            type="button"
            class="remove"
            aria-label=${words('removeMap', read ? labelOf(context, read.field) : '')}
            @click=${() => {
              put(undefined);
              removed();
            }}
          >
            <uui-icon name="icon-wrong" aria-hidden="true"></uui-icon>
          </button>`
    }
    ${
      read
        ? html`<div class="routes">
            <p class="help" id=${`${id}-routes-help`}>
              ${chosen?.options === null ? words('optionsReading') : options.length === 0 ? words('optionsNone') : words('routesHelp')}
            </p>
            ${options.map((option, at) => row(option.value, option.label, at))}
            ${gone.map((each, at) => row(each.value, words('optionGone', each.value), options.length + at))}
          </div>`
        : nothing
    }
    ${shown.lines}
  </div>`;
}

// The addresses an answer chooses on their own, where the To box is locked.
function mapAlone(context: ActionEditorContext, setting: FieldSettingResponseModel, value: unknown, path: string) {
  return html`<div class="item">
    ${mapRow(context, { setting, value }, fieldsOfKind(context.fields, 'choice'), true, () => context.focus(boxId(path)))}
  </div>`;
}

// ---- Layout -------------------------------------------------------------------------------------------------------

/** A layout's name: the site's default named after the layout it is, one that is gone said so. */
function layoutName(context: ActionEditorContext, key: string): string {
  const name = (wanted: string) => context.layouts.find((layout) => layout.key === wanted)?.name;
  if (key === '') return context.words('layoutDefault', context.editor.say(name(context.defaultLayout) ?? ''));
  const found = name(key);
  return found ? context.editor.say(found) : context.words('layoutGone');
}

// The site's default, the built-in card and letter, and the library's layouts.
function layoutPicker(context: ActionEditorContext, setting: FieldSettingResponseModel, value: unknown, path: string) {
  const id = boxId(path);
  const key = typeof value === 'string' ? value : '';
  const known = key === '' || context.layouts.some((layout) => layout.key === key);
  return settingFrame(
    context.editor,
    setting,
    path,
    (described, invalid) =>
      html`<select
        id=${id}
        aria-describedby=${described ?? nothing}
        aria-invalid=${invalid ? 'true' : 'false'}
        .value=${live(key)}
        @change=${(event: Event) => {
          const chosen = (event.target as HTMLSelectElement).value;
          context.editor.set(path, setting, { ok: true, value: chosen === '' ? undefined : chosen }, true);
        }}
      >
        <option value="" ?selected=${key === ''}>${layoutName(context, '')}</option>
        ${known ? nothing : html`<option value=${key} selected>${context.words('layoutGone')}</option>`}
        ${context.layouts.map(
          (layout) =>
            html`<option value=${layout.key} ?selected=${layout.key === key}>
              ${context.editor.say(layout.name)}
            </option>`,
        )}
      </select>`,
  );
}
