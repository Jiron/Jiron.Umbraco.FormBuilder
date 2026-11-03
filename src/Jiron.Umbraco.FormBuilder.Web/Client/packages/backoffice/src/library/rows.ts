// What the library's screens say of their items (docs/ux/information-architecture.md, sections 3 and 4; ADR-0166): when an
// item changed and by whom, its recent entries, what a delete takes, and an import's notes. Pure, so the stores, the screens
// and the dialogs say each thing alike.
import type { FormReferencesResponseModel, ImportNoteResponseModel, LibraryRowResponseModel } from '../api';

/** The words a day needs, in the user's language. */
export interface DayWords {
  today: string;
  yesterday: string;
  /** "%day%, by %name%". */
  by: (day: string, name: string) => string;
}

const dayOf = (date: Date): number => new Date(date.getFullYear(), date.getMonth(), date.getDate()).getTime();

/** The day of a moment as a list says it: today, yesterday, the day and month this year, with the year before it. */
export function dayText(when: Date, now: Date, culture: string, words: Pick<DayWords, 'today' | 'yesterday'>): string {
  const days = Math.round((dayOf(now) - dayOf(when)) / 86_400_000);
  if (days <= 0) return words.today;
  if (days === 1) return words.yesterday;
  const options: Intl.DateTimeFormatOptions =
    when.getFullYear() === now.getFullYear()
      ? { day: 'numeric', month: 'short' }
      : { day: 'numeric', month: 'short', year: 'numeric' };
  return new Intl.DateTimeFormat(culture, options).format(when);
}

/** When a row last changed, or went to the trash, and by whom when that person is known. */
export function changedText(row: LibraryRowResponseModel, now: Date, culture: string, words: DayWords): string {
  const day = dayText(new Date(row.trashedUtc ?? row.updatedUtc), now, culture, words);
  return row.updatedBy ? words.by(day, row.updatedBy) : day;
}

/** A row's recent entries: a number, "none" for none, or nothing where the user may view none. */
export function entriesText(count: number | null | undefined, culture: string, none: string): string {
  if (count === null || count === undefined) return '';
  return count === 0 ? none : new Intl.NumberFormat(culture).format(count);
}

/** Whether a column of recent entries says anything: a user who may view no entries sees no such column. */
export const showsEntries = (rows: readonly LibraryRowResponseModel[]): boolean =>
  rows.some((row) => row.recentEntries !== null && row.recentEntries !== undefined);

/** The words an import's notes need, in the user's language. */
export interface NoteWords {
  themeMissing: (theme: string) => string;
  optionListReused: (list: string) => string;
  optionListCreated: (list: string) => string;
  optionListMissing: (list: string) => string;
  optionListUnnamed: string;
  actionConfigurationMissing: (key: string) => string;
  actionTypeMissing: (type: string) => string;
  pageMissing: string;
  other: (code: string) => string;
}

/**
 * An import's notes as sentences, each said once: a file that names one list from ten fields gets one sentence. Notes come
 * in file order, and a code this version does not know is still said.
 */
export function noteTexts(notes: readonly ImportNoteResponseModel[], words: NoteWords): string[] {
  const said = new Set<string>();
  for (const note of notes) {
    const alias = note.alias ?? '';
    switch (note.code) {
      case 'theme_missing':
        said.add(words.themeMissing(alias));
        break;
      case 'option_list_reused':
        said.add(words.optionListReused(alias));
        break;
      case 'option_list_created':
        said.add(words.optionListCreated(alias));
        break;
      case 'option_list_missing':
        said.add(alias ? words.optionListMissing(alias) : words.optionListUnnamed);
        break;
      case 'action_configuration_missing':
        said.add(words.actionConfigurationMissing(alias));
        break;
      case 'action_type_missing':
        said.add(words.actionTypeMissing(alias));
        break;
      case 'page_missing':
        said.add(words.pageMissing);
        break;
      default:
        said.add(words.other(note.code));
    }
  }

  return [...said];
}

/** Whether a note only says what came along (a list the site had, or one the import adds), not what is missing. */
export const isGoodNews = (note: ImportNoteResponseModel): boolean =>
  note.code === 'option_list_reused' || note.code === 'option_list_created';

/** The pages a form is picked on, as a confirmation names them: how many, the first few names, and how many more. */
export interface UsedOn {
  count: number;
  names: string[];
  more: number;
}

/** How many names a confirmation lists before it says how many more. */
export const NAMED_PAGES = 5;

/** The pages of a `referenced-by` answer: those the user may open by name, the others counted. */
export function usedOn(references: FormReferencesResponseModel | undefined): UsedOn {
  if (!references) return { count: 0, names: [], more: 0 };
  const count = references.total + references.notShown;
  const names = references.items
    .map((page) => page.name ?? page.variants.find((variant) => variant.name)?.name ?? '')
    .filter((name) => name !== '')
    .slice(0, NAMED_PAGES);
  return { count, names, more: count - names.length };
}
