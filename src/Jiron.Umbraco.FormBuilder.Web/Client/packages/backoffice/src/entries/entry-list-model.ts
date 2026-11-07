// What a form's Entries view says of its entries and filters (ROADMAP 11.1, IA 6.1, ADR-0207): a row's name and its
// action status, the chips of the filters, the columns a filter fixes, which bulk actions apply to the ticked entries,
// and the request a list sends. Pure, so the view, its dialogs and the tests say each thing alike.
import type {
  EntryFieldFilterRequestModel,
  EntryListActionsResponseModel,
  EntryListFieldResponseModel,
  EntryListFilterRequestModel,
  EntryListOptionResponseModel,
  EntryListRequestModel,
  EntryListRowResponseModel,
} from '../api';
import { dayText } from '../library/rows';

/** A localized term by its full key. */
export type Term = (key: string, ...args: unknown[]) => string;

/** The states an entry of a list can be in: drafts are in no list (ADR-0163). */
export type EntryState = 'unverified' | 'submitted' | 'approved' | 'rejected' | 'spam';

/** The states a filter offers, in the order an entry passes them. */
export const FILTER_STATES: readonly EntryState[] = ['unverified', 'submitted', 'approved', 'rejected', 'spam'];

/** The Received presets, in the order the menu lists them. */
export const PRESETS = ['today', 'thisWeek', 'last7Days', 'thisMonth', 'lastMonth'] as const;

/** The Actions filter's values. */
export const ACTION_FILTERS = ['failed', 'uncertain', 'delivered'] as const;

/** A filter with nothing set: every entry a list holds, newest first. */
export const emptyFilter = (): EntryListFilterRequestModel => ({ states: [], fields: [] });

/** Whether a filter narrows the list by anything but its search and sort, which the chips show. */
export const isFiltered = (filter: EntryListFilterRequestModel): boolean =>
  filter.states.length > 0 || !!filter.received || !!filter.actions || filter.fields.length > 0;

/** A row's name: its title, or its number when the form has none. */
export const rowName = (row: Pick<EntryListRowResponseModel, 'title' | 'number'>, term: Term): string =>
  row.title ?? term('jfbEntries_number', row.number);

/** A row's name in the controls about it: its title, or "entry 1042". */
export const rowLabel = (row: Pick<EntryListRowResponseModel, 'title' | 'number'>, term: Term): string =>
  row.title ?? term('jfbEntries_entryNumber', row.number);

/** A state's chip. */
export const stateText = (state: string, term: Term): string => term(`jfbEntries_state_${state}`);

/** When an entry was received, as a list says it: "Today, 07:48", "20 Oct, 09:30". */
export function receivedText(received: string, now: Date, culture: string, term: Term): string {
  const when = new Date(received);
  const day = dayText(when, now, culture, { today: term('jfbEntries_today'), yesterday: term('jfbEntries_yesterday') });
  return `${day}, ${new Intl.DateTimeFormat(culture, { timeStyle: 'short' }).format(when)}`;
}

/** How an entry's action runs stand, the worst first; null for an entry without runs, which says nothing. */
export interface RunsText {
  text: string;
  tone: 'danger' | 'warning' | 'muted' | 'default';
}

/** An entry's action status: "1 failed", "1 uncertain", "2 waiting", or "3/3 delivered"; runs never run don't count. */
export function runsText(runs: EntryListActionsResponseModel, term: Term): RunsText | null {
  const total = runs.delivered + runs.failed + runs.uncertain + runs.waiting + runs.skipped;
  if (total === 0) return null;
  if (runs.failed > 0) return { text: term('jfbEntries_runs_failed', runs.failed), tone: 'danger' };
  if (runs.uncertain > 0) return { text: term('jfbEntries_runs_uncertain', runs.uncertain), tone: 'warning' };
  if (runs.waiting > 0) return { text: term('jfbEntries_runs_waiting', runs.waiting), tone: 'default' };
  const ran = total - runs.skipped;
  return ran === 0
    ? { text: term('jfbEntries_runs_skipped'), tone: 'muted' }
    : { text: term('jfbEntries_runs_delivered', runs.delivered, ran), tone: 'default' };
}

/** A field filter that leaves one value, whose column then says nothing the chip doesn't (IA 6.1, say it once). */
const fixesOneValue = (condition: EntryFieldFilterRequestModel): boolean =>
  (condition.test === 'oneOf' && condition.values.length === 1) ||
  condition.test === 'checked' ||
  condition.test === 'notChecked' ||
  condition.test === 'notAnswered';

/** The columns hidden while a filter fixes their value: the state for one state, and fields fixed to one value. */
export function fixedColumns(filter: EntryListFilterRequestModel): { state: boolean; fields: ReadonlySet<string> } {
  return {
    state: filter.states.length === 1,
    fields: new Set(filter.fields.filter(fixesOneValue).map((condition) => condition.field)),
  };
}

/** A chip of the chip row: what it filters, said as the chip says it, and the part of the filter it is. */
export interface Chip {
  id: string;
  text: string;
  kind: 'state' | 'received' | 'actions' | 'field';
  /** The field filter's place, for a field's chip. */
  index?: number;
}

/** A day of a range as a chip writes it, in the user's language. */
const dayOf = (day: string, culture: string): string =>
  new Intl.DateTimeFormat(culture, { day: 'numeric', month: 'short', year: 'numeric', timeZone: 'UTC' }).format(
    new Date(`${day}T00:00:00Z`),
  );

/** Values joined as a chip says them: "a", "a or b", "a, b or c". */
function joined(values: readonly string[], term: Term): string {
  if (values.length <= 1) return values[0] ?? '';
  return term('jfbEntries_chip_or', values.slice(0, -1).join(', '), values[values.length - 1]);
}

/** A range as a chip says it: "1 to 5", "from 1", "until 5". */
function rangeText(from: string | undefined, to: string | undefined, term: Term): string {
  if (from !== undefined && to !== undefined) return term('jfbEntries_chip_between', from, to);
  return from !== undefined ? term('jfbEntries_chip_from', from) : term('jfbEntries_chip_until', to ?? '');
}

/**
 * A field filter's test as its chip says it; a value the field offers to tick is said by its label, as the dialog ticks
 * it.
 */
export function conditionText(
  condition: EntryFieldFilterRequestModel,
  culture: string,
  term: Term,
  options: readonly EntryListOptionResponseModel[] = [],
): string {
  const number = (value: number | null | undefined) =>
    value === null || value === undefined ? undefined : new Intl.NumberFormat(culture).format(value);
  const day = (value: string | null | undefined) => (value ? dayOf(value, culture) : undefined);
  const labels = () =>
    condition.values.map((value) => options.find((option) => option.value === value)?.label ?? value);
  switch (condition.test) {
    case 'oneOf':
      return joined(labels(), term);
    case 'noneOf':
      return term('jfbEntries_chip_not', joined(labels(), term));
    case 'startsWith':
      return term('jfbEntries_chip_startsWith', condition.values[0] ?? '');
    case 'between':
      return condition.from || condition.to
        ? rangeText(day(condition.from), day(condition.to), term)
        : rangeText(number(condition.min), number(condition.max), term);
    default:
      return term(`jfbEntries_chip_${condition.test}`);
  }
}

/** The chips of a filter, in the order the menu lists its parts: state, Received, Actions, then the fields. */
export function chipsOf(
  filter: EntryListFilterRequestModel,
  fields: ReadonlyMap<string, EntryListFieldResponseModel>,
  culture: string,
  term: Term,
): Chip[] {
  const chips: Chip[] = [];
  if (filter.states.length > 0) {
    const states = FILTER_STATES.filter((state) => filter.states.includes(state)).map((state) =>
      stateText(state, term),
    );
    chips.push({ id: 'state', kind: 'state', text: term('jfbEntries_chip_state', joined(states, term)) });
  }

  if (filter.received) {
    const received = filter.received;
    const span = received.preset
      ? term(`jfbEntries_received_${received.preset}`)
      : rangeText(
          received.from ? dayOf(received.from, culture) : undefined,
          received.to ? dayOf(received.to, culture) : undefined,
          term,
        );
    chips.push({ id: 'received', kind: 'received', text: term('jfbEntries_chip_received', span) });
  }

  if (filter.actions)
    chips.push({
      id: 'actions',
      kind: 'actions',
      text: term('jfbEntries_chip_actions', term(`jfbEntries_actions_${filter.actions}`)),
    });

  filter.fields.forEach((condition, index) => {
    const field = fields.get(condition.field);
    chips.push({
      id: `field-${index}`,
      kind: 'field',
      index,
      text: term('jfbEntries_chip_field', field?.label ?? '', conditionText(condition, culture, term, field?.options)),
    });
  });
  return chips;
}

/** An action of the bulk bar, in the order IA 6.1 lists them. */
export type BulkAction = 'approve' | 'reject' | 'markSpam' | 'notSpam' | 'delete' | 'rerun';

/** The bulk bar's order. */
export const BULK_ORDER: readonly BulkAction[] = ['approve', 'reject', 'markSpam', 'notSpam', 'delete', 'rerun'];

/** The state a move reaches. */
export const MOVE_STATE: Partial<Record<BulkAction, string>> = {
  approve: 'approved',
  reject: 'rejected',
  markSpam: 'spam',
  notSpam: 'submitted',
};

/** Whether an action applies to an entry in a state, as the server moves or runs it. */
export const APPLIES: Record<BulkAction, (state: string) => boolean> = {
  approve: (state) => state === 'submitted' || state === 'rejected',
  reject: (state) => state === 'submitted' || state === 'approved',
  markSpam: (state) => state !== 'spam',
  notSpam: (state) => state === 'spam',
  delete: () => true,
  rerun: (state) => state === 'submitted' || state === 'approved' || state === 'rejected',
};

/** The permission an action needs on the form. */
export const NEEDS: Record<BulkAction, string> = {
  approve: 'editEntries',
  reject: 'editEntries',
  markSpam: 'editEntries',
  notSpam: 'editEntries',
  delete: 'deleteEntries',
  rerun: 'manageActions',
};

/** The bulk actions offered for the ticked entries: those the user may use that apply to one of them at least. */
export function bulkActions(states: readonly string[], permissions: readonly string[]): BulkAction[] {
  return BULK_ORDER.filter(
    (action) => permissions.includes(NEEDS[action]) && states.some((state) => APPLIES[action](state)),
  );
}

/** How many of the ticked entries an action leaves out, as its toast says. */
export const leftOut = (action: BulkAction, states: readonly string[]): number =>
  states.filter((state) => !APPLIES[action](state)).length;

/** A filter as a list sends it and a set keeps it: the parts it sets, and no blank search. */
export function cleanFilter(filter: EntryListFilterRequestModel): EntryListFilterRequestModel {
  const search = filter.search?.trim();
  return {
    states: [...filter.states],
    fields: filter.fields.map((condition) => ({ ...condition, values: [...condition.values] })),
    ...(filter.received ? { received: { ...filter.received } } : {}),
    ...(filter.actions ? { actions: filter.actions } : {}),
    ...(search ? { search } : {}),
    ...(filter.sort ? { sort: { ...filter.sort } } : {}),
  };
}

/** The request of a page: the first counts the matches, and every page sends the columns it shows. */
export function requestOf(
  filter: EntryListFilterRequestModel,
  columns: readonly string[],
  after: string | null,
): EntryListRequestModel {
  return { filter: cleanFilter(filter), columns: [...columns], ...(after ? { after } : {}), count: after === null };
}

/** The sort after a header is chosen: the same column turns, another starts with its natural order. */
export function nextSort(
  current: EntryListFilterRequestModel['sort'],
  field: string | null,
): NonNullable<EntryListFilterRequestModel['sort']> {
  const same = (current?.field ?? null) === field;
  if (same) return { field, descending: !(current?.descending ?? true) };
  // Received starts newest first; a field starts from its smallest, as a reader expects of a column.
  return { field, descending: field === null };
}

/** Whether a key press belongs to the list rather than to a field the user types in. */
export function isTyping(target: EventTarget | null): boolean {
  if (!(target instanceof HTMLElement)) return false;
  const tag = target.tagName.toLowerCase();
  return (
    target.isContentEditable ||
    tag === 'input' ||
    tag === 'textarea' ||
    tag === 'select' ||
    tag === 'uui-input' ||
    tag === 'uui-textarea'
  );
}
