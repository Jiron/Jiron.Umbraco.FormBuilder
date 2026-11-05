// The library's client stores (ADR-0166): the state behind a folder's or the bin's list, the template gallery and the import
// sidebar, apart from their elements so that tests can drive them. Each takes the API calls it makes, and says what changed
// through one callback, which an element answers by drawing again.
import type {
  ImportCheckResponseModel,
  LibraryRowResponseModel,
  PagedLibraryRowResponseModel,
  PagedTemplateResponseModel,
  TemplateResponseModel,
} from '../api';

/** An answer of the generated client, as `tryExecute` gives it. */
export interface Answer<T> {
  data?: T | undefined;
  error?: unknown;
}

/** Something that loads, has loaded, or failed to. */
export type Loaded<T> = { state: 'loading' } | { state: 'ready'; value: T } | { state: 'failed' };

/** How many rows a list asks for at once; "Load more" asks for the next. */
export const PAGE_SIZE = 100;

/** A folder's list, or the Recycle Bin's: its rows, page by page, and whether more are waiting. */
export class ListStore {
  rows: Loaded<LibraryRowResponseModel[]> = { state: 'loading' };
  total = 0;
  /** Whether the next page is being asked for: Load more waits, and a second press asks nothing more. */
  loadingMore = false;
  #generation = 0;

  constructor(
    private readonly fetch: (skip: number, take: number) => Promise<Answer<PagedLibraryRowResponseModel>>,
    private readonly changed: () => void,
  ) {}

  /** Whether rows are left to load. */
  get hasMore(): boolean {
    return this.rows.state === 'ready' && this.rows.value.length < this.total;
  }

  /** Loads the first page again; an answer to an older load that comes later is dropped. */
  async load(): Promise<void> {
    const generation = ++this.#generation;
    if (this.rows.state !== 'ready') this.rows = { state: 'loading' };
    this.changed();
    const { data } = await this.fetch(0, PAGE_SIZE);
    if (generation !== this.#generation) return;
    this.rows = data ? { state: 'ready', value: data.items } : { state: 'failed' };
    this.total = data?.total ?? 0;
    this.changed();
  }

  /** Loads the next page after the rows already there, once at a time. */
  async more(): Promise<void> {
    if (this.rows.state !== 'ready' || this.loadingMore) return;
    const generation = this.#generation;
    const shown = this.rows.value;
    this.loadingMore = true;
    this.changed();
    const { data } = await this.fetch(shown.length, PAGE_SIZE).finally(() => (this.loadingMore = false));
    if (generation !== this.#generation || !data) {
      this.changed();
      return;
    }
    const known = new Set(shown.map((row) => row.item.key));
    this.rows = { state: 'ready', value: [...shown, ...data.items.filter((row) => !known.has(row.item.key))] };
    this.total = data.total;
    this.changed();
  }
}

/** A template's name and description in the user's language: a built-in template's from the dictionary. */
export interface TemplateWords {
  name: (template: TemplateResponseModel) => string;
  description: (template: TemplateResponseModel) => string;
}

/** The template gallery: every template, the search, and the chosen one, which the preview shows. */
export class GalleryStore {
  templates: Loaded<TemplateResponseModel[]> = { state: 'loading' };
  query = '';
  selected: string | undefined;

  constructor(
    private readonly fetch: () => Promise<Answer<PagedTemplateResponseModel>>,
    private readonly words: TemplateWords,
    private readonly changed: () => void,
  ) {}

  /** Loads the gallery, and chooses the first template, so the preview never stands empty. */
  async load(): Promise<void> {
    this.templates = { state: 'loading' };
    this.changed();
    const { data } = await this.fetch();
    this.templates = data ? { state: 'ready', value: data.items } : { state: 'failed' };
    this.selected = this.visible[0]?.key;
    this.changed();
  }

  /** The templates the search leaves, in the gallery's order: built-in first, then saved. */
  get visible(): TemplateResponseModel[] {
    if (this.templates.state !== 'ready') return [];
    const query = this.query.trim().toLocaleLowerCase();
    if (!query) return this.templates.value;
    return this.templates.value.filter((template) =>
      `${this.words.name(template)} ${this.words.description(template)}`.toLocaleLowerCase().includes(query),
    );
  }

  /** The chosen template, when the search still shows it. */
  get chosen(): TemplateResponseModel | undefined {
    return this.visible.find((template) => template.key === this.selected);
  }

  /** Searches; a choice the search hides gives way to the first template it shows. */
  search(query: string): void {
    this.query = query;
    if (!this.chosen) this.selected = this.visible[0]?.key;
    this.changed();
  }

  select(key: string): void {
    this.selected = key;
    this.changed();
  }

  /** Moves the choice by one, as the arrow keys of a radio group do, wrapping at the ends; without a choice, to the first or last. */
  step(by: 1 | -1): void {
    const visible = this.visible;
    const at = visible.findIndex((template) => template.key === this.selected);
    const index = at < 0 ? (by === 1 ? 0 : visible.length - 1) : (at + by + visible.length) % visible.length;
    const next = visible[index];
    if (!next) return;
    this.selected = next.key;
    this.changed();
  }
}

/** The largest file the import sidebar reads: far more than a form with every limit used. */
export const MAX_IMPORT_BYTES = 5 * 1024 * 1024;

/** Where the import sidebar stands. */
export type ImportState =
  | { step: 'choose' }
  | { step: 'checking'; file: ChosenFile }
  | { step: 'checked'; file: ChosenFile; check: ImportCheckResponseModel }
  | { step: 'refused'; file: ChosenFile; reason: ImportRefusal }
  | { step: 'importing'; file: ChosenFile; check: ImportCheckResponseModel }
  | { step: 'imported'; formKey: string };

/** A file the editor chose: its name, size and text. */
export interface ChosenFile {
  name: string;
  size: number;
  text: string;
}

/** Why a file can't be imported: too large, not a file this reads (with the server's problem), no permission, or a failure. */
export type ImportRefusal =
  | { kind: 'tooLarge' }
  | { kind: 'unreadable'; message: string | undefined }
  | { kind: 'forbidden'; permission: string | undefined }
  | { kind: 'failed' };

/** What the import sidebar calls, and how it reads a refusal. */
export interface ImportCalls {
  check: (json: string) => Promise<Answer<ImportCheckResponseModel>>;
  import: (json: string) => Promise<Answer<{ form: { key: string } }>>;
  refusal: (error: unknown) => ImportRefusal;
}

/** The import: choose a file, see what it holds and what it can't bring, then import it as a new copy. */
export class ImportStore {
  state: ImportState = { step: 'choose' };
  /** Counts the files chosen, so that the answer for one that another followed is dropped. */
  #generation = 0;

  constructor(
    private readonly calls: ImportCalls,
    private readonly changed: () => void,
  ) {}

  /** Reads a file and checks it on the server, which writes nothing; a file that does not read is refused as unreadable. */
  async choose(file: { name: string; size: number; text: () => Promise<string> }): Promise<void> {
    const generation = ++this.#generation;
    if (file.size > MAX_IMPORT_BYTES) {
      this.#set({
        step: 'refused',
        file: { name: file.name, size: file.size, text: '' },
        reason: { kind: 'tooLarge' },
      });
      return;
    }

    const read = await file.text().then(
      (text) => text,
      () => undefined,
    );
    if (generation !== this.#generation) return;
    const chosen: ChosenFile = { name: file.name, size: file.size, text: read ?? '' };
    if (read === undefined) {
      this.#set({ step: 'refused', file: chosen, reason: { kind: 'unreadable', message: undefined } });
      return;
    }

    this.#set({ step: 'checking', file: chosen });
    const { data, error } = await this.calls.check(chosen.text);
    if (generation !== this.#generation) return;
    this.#set(
      data
        ? { step: 'checked', file: chosen, check: data }
        : { step: 'refused', file: chosen, reason: this.calls.refusal(error) },
    );
  }

  /** Imports the checked file; the new form's key ends the sidebar. */
  async import(): Promise<void> {
    if (this.state.step !== 'checked') return;
    const { file, check } = this.state;
    this.#set({ step: 'importing', file, check });
    const { data, error } = await this.calls.import(file.text);
    this.#set(
      data
        ? { step: 'imported', formKey: data.form.key }
        : { step: 'refused', file, reason: this.calls.refusal(error) },
    );
  }

  /** Back to choosing a file. */
  reset(): void {
    this.#generation++;
    this.#set({ step: 'choose' });
  }

  #set(state: ImportState) {
    this.state = state;
    this.changed();
  }
}
