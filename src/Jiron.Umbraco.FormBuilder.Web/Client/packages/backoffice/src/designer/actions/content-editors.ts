// The content action's editors (ROADMAP 10.2e, ADR-0192). Where it goes is one box: under the item picked, under the
// page the form is sent from, or under the root of the visitor's site, then the item picked, whose label and help say
// what it is for that choice. The properties are a table of rows, each a property of the document type and what it
// takes: the answer of a field it can hold, or a value written for it in the form its property stores. Another document
// type keeps the table of the one before while the sidebar is open, and starts with the properties a field of the same
// alias can fill. Each box has the id the form check's Fix it lands on, a row's boxes their row's.
import { css, html, live, nothing, type TemplateResult } from '@umbraco-cms/backoffice/external/lit';
import type { FieldSettingResponseModel } from '../../api';
import { boxId, settingEditor } from '../inspector/setting-editor';
import type { ActionEditorContext, PickField, Within } from './action-editors';
import {
  BOOLEAN_VALUES,
  decimalText,
  fieldsFor,
  mappingOf,
  parentSourceOf,
  rowsOf,
  sameMapping,
  starterRows,
  type ContentProperty,
  type ContentType,
  type MappingRow,
  type ParentSource,
  type TypeRead,
} from './content-model';
import { foundAt } from './editor-lines';

/** The content action's editors by their aliases, for the inspector's `editors`. */
export function contentEditors(context: ActionEditorContext) {
  return {
    contentParent: (setting: FieldSettingResponseModel, value: unknown, path: string) =>
      place(context, setting, value, path),
    contentMapping: (setting: FieldSettingResponseModel, value: unknown, path: string) =>
      table(context, setting, value, path),
    contentMediaFolder: (setting: FieldSettingResponseModel, value: unknown, path: string) =>
      mediaFolder(context, setting, value, path),
  };
}

// The media folder for files, Umbraco's media picker, where a row takes a file upload's answer or it holds a folder.
function mediaFolder(context: ActionEditorContext, setting: FieldSettingResponseModel, value: unknown, path: string) {
  const takesFiles = rowsOf(context.value('mapping')).some(
    (row) => row.field !== undefined && context.fields.some((field) => field.key === row.field && field.file),
  );
  if (!takesFiles && (typeof value !== 'string' || value === '')) return nothing;
  return settingEditor(context.editor, { ...setting, editor: 'mediaPicker' }, value, path);
}

/** The look of the content action's editors, beside the webhook's, whose choices and box within a box they share. */
export const contentEditorStyles = css`
  /* The item picked is named by the choice above it; who hears it hears its own name. */
  .parent .label {
    position: absolute;
    inline-size: 1px;
    block-size: 1px;
    overflow: hidden;
    clip-path: inset(50%);
    white-space: nowrap;
  }

  /* A property, what it takes and the remove button; a value written under what it takes. Rows stand further apart than
     a row's own boxes, so a value reads as its row's. */
  .map {
    display: grid;
    gap: var(--uui-size-space-4);
    min-inline-size: 0;
  }

  .map-head,
  .map-row {
    display: grid;
    grid-template-columns: minmax(0, 1fr) minmax(0, 1fr) 32px;
    align-items: center;
    gap: var(--uui-size-space-2);
  }

  .map-head {
    color: var(--jfb-bo-color-text-muted);
    font-size: var(--uui-type-small-size);
    font-weight: 600;
  }

  /* The value written goes under what the row takes, the remove button stays on the row's first line. */
  .map-row > .value {
    grid-column: 2;
  }

  .map-row > .remove {
    grid-row: 1;
    grid-column: 3;
  }

  .map-row > .why {
    grid-column: 1 / -1;
  }

  @container (max-width: 480px) {
    .map-head {
      display: none;
    }

    .map-row {
      grid-template-columns: minmax(0, 1fr) 32px;
      padding-block-end: var(--uui-size-space-3);
      border-block-end: 1px solid var(--uui-color-divider-standalone);
    }

    .map-row > .from,
    .map-row > .value {
      grid-column: 1;
    }

    .map-row > .remove {
      grid-row: 1 / span 2;
      grid-column: 2;
    }
  }
`;

const textOf = (value: unknown) => (typeof value === 'string' ? value : '');

// ---- Read-only ----------------------------------------------------------------------------------------------------

/**
 * A content action's setting in words, for who may not change it: where it goes (the item picked shows beside it, as
 * Umbraco names it), and each row of the table as its property and what it takes; undefined for another editor's.
 */
export function contentSettingText(
  context: ActionEditorContext,
  setting: FieldSettingResponseModel,
  value: unknown,
): string | undefined {
  switch (setting.editor) {
    case 'contentParent': {
      const source = parentSourceOf(value);
      const parent = context.within(setting.name).find((part) => part.setting.name === 'parent')?.value;
      if (source === 'picked' && (typeof parent !== 'string' || parent === '')) return context.words('contentRoot');
      const choice = setting.choices.find((one) => one.value === source);
      return choice ? context.editor.say(choice.label) : source;
    }
    case 'contentMapping': {
      const alias = textOf(context.value('documentType'));
      const type = alias ? context.documentType(alias) : null;
      const properties = type !== null && typeof type === 'object' ? type.properties : [];
      return rowsOf(value)
        .filter((row) => row.property !== '')
        .map((row) => {
          const property = properties.find((each) => each.alias === row.property);
          const name = property?.name ?? row.property;
          if (row.field !== undefined) {
            const field = context.fields.find((each) => each.key === row.field);
            return field ? context.words('mapRead_field', name, field.label) : context.words('mapRead_fieldGone', name);
          }
          const written =
            property?.value === 'boolean'
              ? context.words(row.value === 'false' ? 'mapNo' : 'mapYes')
              : (row.value ?? '');
          return context.words('mapRead_value', name, written);
        })
        .join('\n');
    }
    default:
      return undefined;
  }
}

// ---- Where it goes ------------------------------------------------------------------------------------------------

// Where it goes, then the item picked within it, which is the parent itself under the first choice and the item the
// visitor's page or site root must stay within under the others.
function place(context: ActionEditorContext, setting: FieldSettingResponseModel, value: unknown, path: string) {
  const id = boxId(path);
  const source = parentSourceOf(value);
  const parent = context.within(setting.name).find((part) => part.setting.name === 'parent');
  const own = foundAt(context.editor, id);
  return html`<fieldset class="item" id=${id} aria-describedby=${own.ids.join(' ') || nothing}>
    <legend class="label">${context.editor.say(setting.label)}</legend>
    <div class="choices">
      ${setting.choices.map((choice) => {
        const each = parentSourceOf(choice.value);
        return html`<label class="choice">
            <input
              type="radio"
              name="content-parent"
              value=${each}
              aria-labelledby=${`content-parent-${each}-name`}
              aria-describedby=${`content-parent-${each}`}
              .checked=${live(source === each)}
              @change=${() => context.editor.set(path, setting, { ok: true, value: each }, true)}
            />
            <span class="choice-name" id=${`content-parent-${each}-name`}>${context.editor.say(choice.label)}</span>
            <span class="choice-line" id=${`content-parent-${each}`}
              >${context.words(`contentParentHelp_${each}`)}</span
            >
          </label>
          ${each === source && parent ? parentBox(context, parent, source) : nothing}`;
      })}
    </div>
    ${own.lines}
  </fieldset>`;
}

// The item picked, under its choice: named for it to who hears it, as the choice's line says what it is for; as it is
// where a host locks it.
function parentBox(context: ActionEditorContext, parent: Within, source: ParentSource): TemplateResult {
  if (parent.setting.lock) return html`${settingEditor(context.editor, parent.setting, parent.value)}`;
  const shown: FieldSettingResponseModel = {
    ...parent.setting,
    label: context.words(`contentParent_${source}`),
    description: null,
  };
  return html`<div class="mode parent">
    ${settingEditor(context.editor, shown, parent.value, parent.setting.name)}
  </div>`;
}

// ---- The properties -----------------------------------------------------------------------------------------------

/** The rows a table holds while the sidebar is open, and the value they were read from. */
interface Typed {
  readonly rows: MappingRow[];
  readonly from: string;
}

const ROWS = 'rows:mapping';

/** The value a source select holds for "A value you write", which no field's key is. */
const WRITTEN = '=value';

/** A row's part, as the form check's problems name it. */
export type RowPart = 'property' | 'field' | 'value';

/** The id of a part of the row at a place in the setting, the path of the form check's problems at it. */
export const rowBox = (path: string, at: number, part: RowPart) => boxId(`${path}[${at}].${part}`);

// The ids of a row's parts: by its place among the rows the setting holds, which the check's problems name; a row
// that holds no property or no source yet, which the setting leaves out, by its place in the table.
const rowIds = (path: string, rows: readonly MappingRow[], at: number) => {
  const complete = (row: MappingRow) => row.property !== '' && (row.field !== undefined || row.value !== undefined);
  const row = rows[at];
  const kept = rows.slice(0, at).filter(complete).length;
  return (part: RowPart) => (row && complete(row) ? rowBox(path, kept, part) : `${boxId(path)}-blank-${at}-${part}`);
};

function rowsIn(context: ActionEditorContext, value: unknown): MappingRow[] {
  const now = JSON.stringify(rowsOf(value));
  const typed = context.kept.get(ROWS) as Typed | undefined;
  if (typed && typed.from === now) return typed.rows;
  const rows = rowsOf(value);
  context.kept.set(ROWS, { rows, from: now });
  return rows;
}

/** The keys of the sensitive fields the action may copy. */
function included(context: ActionEditorContext): string[] {
  const keys = context.value('includeSensitive');
  return Array.isArray(keys) ? keys.map(String) : [];
}

// Another document type, chosen while the sidebar is open, swaps the table once its properties are there: the one
// before keeps its rows for when it is chosen again, and a type chosen for the first time starts with the properties
// a field of the same alias can fill. The table an action had when the sidebar opened is its own and stays.
function followType(
  context: ActionEditorContext,
  setting: FieldSettingResponseModel,
  value: unknown,
  path: string,
  alias: string,
  type: TypeRead | null,
) {
  const seen = context.kept.get('content:type') as string | undefined;
  if (type === 'reading' || type === 'failed' || seen === alias) return;
  context.kept.set('content:type', alias);
  if (seen === undefined) return;
  // A table emptied by hand is kept as empty, not as one never set, which would start with its starter rows again.
  context.kept.set(`content:mapping:${seen}`, value ?? []);
  // Rows not finished belong to the type before, whose properties they name.
  context.kept.delete(ROWS);
  const back = context.kept.get(`content:mapping:${alias}`);
  const next =
    back !== undefined
      ? mappingOf(rowsOf(back))
      : type !== null && type !== 'gone'
        ? mappingOf(starterRows(type, context.fields, included(context)))
        : value;
  if (sameMapping(next, value)) return;
  // The table is drawn as it is now; the setting changes once the drawing is done.
  queueMicrotask(() => {
    context.kept.delete(ROWS);
    context.editor.set(path, setting, { ok: true, value: next }, true);
  });
}

// The table: its head, a row for each property set, Add property, and Insert merge tag for a text written last in focus.
// What a row takes is offered by what the property holds: the fields whose answer it can hold, and a value written for
// it where it takes one. A row reaches the setting once it has a property and what it takes.
function table(context: ActionEditorContext, setting: FieldSettingResponseModel, value: unknown, path: string) {
  const id = boxId(path);
  const alias = textOf(context.value('documentType'));
  const type = alias ? context.documentType(alias) : null;
  followType(context, setting, value, path, alias, type);
  const words = context.words;
  const label = html`<p class="label" id=${`${id}-label`}>${context.editor.say(setting.label)}</p>`;
  const own = foundAt(context.editor, id);
  const said = (key: string, busy = false) =>
    html`<div class="item">
      ${label}
      <p class="help" id=${id} tabindex="-1" aria-busy=${busy ? 'true' : nothing}>${words(key)}</p>
      ${own.lines}
    </div>`;
  if (type === null) return said('mappingNoType');
  if (type === 'reading') return said('mappingReading', true);
  if (type === 'gone') return said('mappingTypeGone');
  if (type === 'failed')
    return html`<div class="item">
      ${label}
      <p class="help" id=${id} tabindex="-1">${words('mappingUnread')}</p>
      <button type="button" class="add-kind" @click=${() => context.readAgain(alias)}>${words('mappingRetry')}</button>
      ${own.lines}
    </div>`;

  const rows = rowsIn(context, value);
  const properties = new Map(type.properties.map((property) => [property.alias, property]));
  const write = (next: MappingRow[], done: boolean) => {
    const written = mappingOf(next);
    context.kept.set(ROWS, {
      rows: next,
      from: done ? JSON.stringify(rowsOf(written)) : JSON.stringify(rowsOf(value)),
    });
    if (done) context.editor.set(path, setting, { ok: true, value: written }, true);
    else context.redraw();
  };
  const change = (at: number, row: MappingRow, done = true) =>
    write(
      rows.map((each, i) => (i === at ? row : each)),
      done,
    );
  const used = new Set(rows.map((row) => row.property));
  // The properties not set yet that this form can fill: from a field's answer, or with a value written for it.
  const free = type.properties.filter(
    (property) =>
      !used.has(property.alias) && (property.value !== null || fieldsFor(property, context.fields).length > 0),
  );
  const isText = (row: MappingRow) => row.value !== undefined && properties.get(row.property)?.value === 'text';
  const texts = rows.some(isText);
  // A tag goes into the text value last in focus, else the last row's text value.
  const lastText = rows.reduce((found, row, at) => (isText(row) ? at : found), -1);
  const last = () =>
    (context.kept.get('last:mapping') as string | undefined) ??
    (lastText >= 0 ? rowIds(path, rows, lastText)('value') : '');
  return html`<div class="item">
    ${label}
    <div
      class="map"
      id=${id}
      role="group"
      aria-labelledby=${`${id}-label`}
      aria-describedby=${own.ids.join(' ') || nothing}
    >
      ${
        type.properties.length === 0
          ? html`<p class="help">${words('mappingNone')}</p>`
          : rows.length > 0
            ? html`<div class="map-head" aria-hidden="true">
                <span>${words('mapProperty')}</span><span>${words('mapFrom')}</span>
              </div>`
            : nothing
      }
      ${rows.map((_, at) => mapRow(context, path, type, rows, at, free, change, write))}
      ${
        free.length > 0
          ? html`<button
              type="button"
              class="add-kind"
              id=${`${id}-add`}
              @click=${() => {
                // A blank row holds nothing the setting keeps: the sidebar draws it anew to show it.
                const next = [...rows, { property: '' }];
                context.kept.set(ROWS, { rows: next, from: JSON.stringify(rowsOf(value)) });
                context.redraw();
                context.focus(rowIds(path, next, rows.length)('property'));
              }}
            >
              <uui-icon name="icon-add" aria-hidden="true"></uui-icon>${words('mapAdd')}
            </button>`
          : nothing
      }
      ${texts ? context.tags(last) : nothing} ${own.lines}
    </div>
  </div>`;
}

// A row: the property, what it takes, the value written where it takes one, and its remove button, each box with the
// lines the form check says at it.
function mapRow(
  context: ActionEditorContext,
  path: string,
  type: ContentType,
  rows: readonly MappingRow[],
  at: number,
  free: readonly ContentProperty[],
  change: (at: number, row: MappingRow, done?: boolean) => void,
  write: (rows: MappingRow[], done: boolean) => void,
): TemplateResult {
  const words = context.words;
  const row = rows[at] ?? { property: '' };
  const property = type.properties.find((each) => each.alias === row.property);
  const name = property?.name ?? row.property;
  const ids = rowIds(path, rows, at);
  const propertyId = ids('property');
  const fromId = ids('field');
  const valueId = ids('value');
  const atProperty = foundAt(context.editor, propertyId);
  const atFrom = foundAt(context.editor, fromId);
  const atValue = foundAt(context.editor, valueId);
  const fits = fieldsFor(property, context.fields);
  // A field the row names that the property can't hold stays shown, said so, while it is chosen; one the form no longer
  // has shows as gone.
  const named = row.field !== undefined ? context.fields.find((field) => field.key === row.field) : undefined;
  const extra: readonly PickField[] = named && !fits.includes(named) ? [named] : [];
  const gone = row.field !== undefined && !named;
  const from = row.field ?? (row.value !== undefined ? WRITTEN : '');
  const choose = (alias: string) => {
    const next = type.properties.find((each) => each.alias === alias);
    if (row.field !== undefined) {
      const keeps = fieldsFor(next, context.fields).some((field) => field.key === row.field);
      change(at, keeps ? { property: alias, field: row.field } : { property: alias });
    } else if (row.value !== undefined && next?.value) {
      // A value written stays where the property stores it as the one before did, or as text; otherwise its box starts
      // again in the new form, a yes-or-no one at Yes as its box shows.
      const keeps = next.value === 'text' || next.value === property?.value;
      change(at, { property: alias, value: keeps ? row.value : next.value === 'boolean' ? 'true' : '' });
    } else change(at, { property: alias });
  };
  const take = (chosen: string) => {
    if (chosen === WRITTEN) {
      const written: MappingRow = { property: row.property, value: property?.value === 'boolean' ? 'true' : '' };
      change(at, written);
      // A row with what it takes is one of the setting's: its value's box has the id of its place there.
      context.focus(
        rowIds(
          path,
          rows.map((each, i) => (i === at ? written : each)),
          at,
        )('value'),
      );
    } else if (chosen !== '') change(at, { property: row.property, field: chosen });
  };
  // Every free property, and the row's own; one the document type lacks stays shown while it is chosen.
  const offered = type.properties.filter((each) => each.alias === row.property || free.includes(each));
  const groups = [...new Set(offered.map((each) => each.group ?? ''))];
  return html`<div class="map-row">
    <select
      id=${propertyId}
      class="property"
      aria-label=${words('mapPropertyOf', at + 1)}
      aria-describedby=${atProperty.ids.join(' ') || nothing}
      aria-invalid=${atProperty.invalid ? 'true' : 'false'}
      .value=${live(row.property)}
      @change=${(event: Event) => choose((event.target as HTMLSelectElement).value)}
    >
      <option value="" disabled ?selected=${row.property === ''}>${words('mapChoose')}</option>
      ${
        row.property !== '' && !property
          ? html`<option value=${row.property} selected>${words('mapGone', row.property)}</option>`
          : nothing
      }
      ${groups.map((group) => {
        const options = offered
          .filter((each) => (each.group ?? '') === group)
          .map(
            (each) => html`<option value=${each.alias} ?selected=${each.alias === row.property}>${each.name}</option>`,
          );
        return groups.length > 1 && group !== '' ? html`<optgroup label=${group}>${options}</optgroup>` : options;
      })}
    </select>
    ${
      property && fits.length + extra.length === 0 && !gone && !property.value && row.value === undefined
        ? html`<p class="from help" id=${fromId} tabindex="-1">${words('mapNothingFits', name)}</p>`
        : html`<select
            id=${fromId}
            class="from"
            aria-label=${row.property === '' ? words('mapFromOfRow', at + 1) : words('mapFromOf', name)}
            aria-describedby=${atFrom.ids.join(' ') || nothing}
            aria-invalid=${atFrom.invalid ? 'true' : 'false'}
            ?disabled=${row.property === ''}
            .value=${live(from)}
            @change=${(event: Event) => take((event.target as HTMLSelectElement).value)}
          >
            <option value="" disabled ?selected=${from === ''}>${words('mapChooseFrom')}</option>
            ${fits.map(
              (field) =>
                html`<option value=${field.key} ?selected=${field.key === row.field}>
                  ${words('mapAnswerTo', field.label)}
                </option>`,
            )}
            ${extra.map(
              (field) =>
                html`<option value=${field.key} selected>${words('mapAnswerCantHold', field.label, name)}</option>`,
            )}
            ${gone ? html`<option value=${row.field ?? ''} selected>${words('pickGone')}</option>` : nothing}
            ${
              property?.value || row.value !== undefined
                ? html`<option value=${WRITTEN} ?selected=${from === WRITTEN}>${words('mapValue')}</option>`
                : nothing
            }
          </select>`
    }
    ${row.value !== undefined ? valueBox(context, property, row, at, valueId, name, change, atValue) : nothing}
    <button
      type="button"
      class="remove"
      aria-label=${row.property === '' ? words('mapRemoveRow', at + 1) : words('mapRemove', name)}
      @click=${() => {
        const left = rows.filter((_, i) => i !== at);
        context.kept.delete('last:mapping');
        write(left, true);
        context.focus(
          left.length > 0 ? rowIds(path, left, Math.min(at, left.length - 1))('property') : `${boxId(path)}-add`,
        );
      }}
    >
      <uui-icon name="icon-wrong" aria-hidden="true"></uui-icon>
    </button>
    ${atProperty.lines}${atFrom.lines}${row.value !== undefined ? atValue.lines : nothing}
  </div>`;
}

// A value written for a property, in the form it stores: words, with merge tags, for text; a whole or a decimal number;
// a date; or yes or no.
function valueBox(
  context: ActionEditorContext,
  property: ContentProperty | undefined,
  row: MappingRow,
  at: number,
  id: string,
  name: string,
  change: (at: number, row: MappingRow, done?: boolean) => void,
  found: ReturnType<typeof foundAt>,
): TemplateResult {
  const words = context.words;
  const kind = property?.value ?? 'text';
  const described = found.ids.join(' ') || nothing;
  const invalid = found.invalid ? 'true' : 'false';
  const set = (text: string, done: boolean) => change(at, { property: row.property, value: text }, done);
  if (kind === 'boolean')
    return html`<select
      id=${id}
      class="value"
      aria-label=${words('mapValueOf', name)}
      aria-describedby=${described}
      aria-invalid=${invalid}
      .value=${live(row.value === 'false' ? 'false' : 'true')}
      @change=${(event: Event) => set((event.target as HTMLSelectElement).value, true)}
    >
      ${BOOLEAN_VALUES.map(
        (choice) =>
          html`<option value=${choice} ?selected=${(row.value === 'false' ? 'false' : 'true') === choice}>
            ${words(choice === 'true' ? 'mapYes' : 'mapNo')}
          </option>`,
      )}
    </select>`;
  return html`<input
    type=${kind === 'date' ? 'date' : 'text'}
    id=${id}
    class="value"
    autocomplete="off"
    spellcheck=${kind === 'text' ? nothing : 'false'}
    inputmode=${kind === 'wholeNumber' ? 'numeric' : kind === 'decimal' ? 'decimal' : nothing}
    placeholder=${kind === 'wholeNumber' || kind === 'decimal' ? words(`mapHint_${kind}`) : nothing}
    aria-label=${words('mapValueOf', name)}
    aria-describedby=${described}
    aria-invalid=${invalid}
    .value=${live(row.value ?? '')}
    @focus=${() => {
      if (kind === 'text') context.kept.set('last:mapping', id);
    }}
    @input=${(event: Event) => set((event.target as HTMLInputElement).value, false)}
    @change=${(event: Event) => {
      const text = (event.target as HTMLInputElement).value;
      set(kind === 'decimal' ? decimalText(text) : text, true);
    }}
  />`;
}
