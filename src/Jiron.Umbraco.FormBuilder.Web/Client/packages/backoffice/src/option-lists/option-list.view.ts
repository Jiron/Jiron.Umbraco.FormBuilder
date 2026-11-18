// An option list's one view (ROADMAP 12.1, IA 7, ADR-0214): a box headed by the list's kind with the inspector's option
// editor, its source fixed to that kind, so every kind reads and behaves as it does in the inspector; an upload for a
// list of fixed options or of a file; the time a JSON address or a standard list keeps its options, with what this server
// fetched and Fetch now; and beside it (under it below 900 px) the options as visitors get them, in the language chosen,
// as a dropdown and as radio buttons, following each change after a pause.
import { css, html, nothing, type TemplateResult } from '@umbraco-cms/backoffice/external/lit';
import { UmbLitElement } from '@umbraco-cms/backoffice/lit-element';
import { UMB_NOTIFICATION_CONTEXT } from '@umbraco-cms/backoffice/notification';
import type { OptionListEditorResponseModel, OptionListFetchResponseModel } from '../api';
import type { DesignField } from '../designer/definition';
import { inspectorStyles } from '../designer/inspector/inspector.styles';
import type { DesignOption } from '../designer/inspector/option-values';
import '../designer/inspector/option-editor.element';
import { jfbBackofficeTokens, jfbVisuallyHidden } from '../styles/tokens';
import { jfbSkeleton, skeletonLines } from '../ui/skeleton';
import { UNDO_DURATION_MS, UNDO_NOTIFICATION, type UndoNotificationData } from '../ui/undo-notification.element';
import { OPTION_LIST_WORKSPACE_CONTEXT } from './constants';
import {
  contentsOf,
  fieldOf,
  firstOf,
  keepTimes,
  keepUnit,
  LIST_TYPE,
  problemsAt,
  type ListKind,
  type ListProblem,
  type OptionListDetailModel,
} from './option-list-model';
import type { OptionListWorkspaceContext } from './option-list-workspace.context';
import { fetchNow, previewList, readEditor, readFetched, readFile, type PreviewAnswer } from './option-lists-api';

/** How long the preview waits after a change before it asks. */
const PAUSE_MS = 400;

/** How many options the radio buttons show before they say how many more. */
const SHOWN = 8;

type Preview =
  | { readonly state: 'waiting' }
  | { readonly state: 'failed' }
  | { readonly state: 'read'; readonly answer: PreviewAnswer };

export class OptionListViewElement extends UmbLitElement {
  static override properties = {
    _list: { state: true },
    _editor: { state: true },
    _culture: { state: true },
    _problems: { state: true },
    _fileProblems: { state: true },
    _preview: { state: true },
    _fetched: { state: true },
    _fetching: { state: true },
    _saved: { state: true },
  };

  static override styles = [
    jfbBackofficeTokens,
    jfbVisuallyHidden,
    jfbSkeleton,
    inspectorStyles,
    css`
      /* The inspector's own frame stays the inspector's: this view is a page of the workspace, padded as the others. */
      :host {
        display: block;
        min-height: 0;
        padding: var(--uui-size-layout-1);
        background: none;
        border-inline-start: 0;
        container-type: inline-size;
      }

      .page {
        display: grid;
        gap: var(--uui-size-space-5);
        align-items: start;
      }

      /* Settings beside the preview where both fit (IA 7). */
      @container (min-width: 900px) {
        .page {
          grid-template-columns: minmax(0, 3fr) minmax(0, 2fr);
        }

        .wide {
          grid-column: 1 / -1;
        }
      }

      /* One switch for the editor's texts and the preview, kept small at the end of the row above them. */
      .language {
        display: flex;
        flex-wrap: wrap;
        align-items: center;
        justify-content: flex-end;
        gap: var(--uui-size-space-3);
      }

      .language select {
        inline-size: auto;
        max-inline-size: 100%;
      }

      .alert,
      .problems {
        margin: 0;
        color: var(--jfb-bo-color-danger-text);
      }

      .problems {
        display: grid;
        gap: var(--uui-size-space-2);
        padding: 0;
        list-style: none;
      }

      /* The kind's settings, its upload and its keep time, as the inspector spaces a section's settings. */
      .stack {
        display: grid;
        gap: var(--uui-size-space-5);
        min-inline-size: 0;
      }

      .upload {
        display: grid;
        gap: var(--uui-size-space-3);
      }

      .upload > div {
        display: flex;
        flex-wrap: wrap;
        align-items: center;
        gap: var(--uui-size-space-3);
      }

      .fetched {
        display: flex;
        flex-wrap: wrap;
        align-items: center;
        gap: var(--uui-size-space-3);
        margin-block-start: var(--uui-size-space-3);
      }

      .fetched p,
      .lead {
        margin: 0;
      }

      .muted {
        color: var(--jfb-bo-color-text-muted);
      }

      .preview {
        display: grid;
        gap: var(--uui-size-space-5);
      }

      .preview select {
        inline-size: 100%;
      }

      fieldset {
        margin: 0;
        padding: 0;
        border: 0;
      }

      legend {
        padding: 0;
        margin-block-end: var(--uui-size-space-3);
        font-weight: 700;
      }

      .radios {
        display: grid;
        gap: var(--uui-size-space-2);
      }

      .radios label {
        display: flex;
        align-items: center;
        gap: var(--uui-size-space-3);
        min-block-size: 24px;
      }

      /* The browser's own radio, not the boxes' look, which every input but a checkbox takes from the controls' styles. */
      .radios input[type='radio'] {
        flex: none;
        box-sizing: border-box;
        inline-size: 18px;
        block-size: 18px;
        min-height: 0;
        margin: 0;
        padding: 0;
        accent-color: var(--uui-color-selected);
      }
    `,
  ];

  declare private _list: OptionListDetailModel | undefined;
  declare private _editor: OptionListEditorResponseModel | undefined;
  /** The language the editor's texts and the preview show; the site's default until another is chosen. */
  declare private _culture: string;
  declare private _problems: readonly ListProblem[];
  /** Why the last file was refused, each naming its line. */
  declare private _fileProblems: readonly ListProblem[];
  declare private _preview: Preview;
  declare private _fetched: OptionListFetchResponseModel | undefined;
  declare private _fetching: boolean;
  /** Whether the list is saved and holds what was saved, so Fetch now fetches what the screen shows. */
  declare private _saved: boolean;

  #context: OptionListWorkspaceContext | undefined;
  #timer: ReturnType<typeof setTimeout> | undefined;
  #asking: AbortController | undefined;
  /** The contents the last preview was asked for, so a change of nothing it shows asks nothing. */
  #asked = '';
  #undo: { close(): void } | undefined;
  /** The list whose fetched state is being read, so a read is asked once while it runs. */
  #readingFetched: string | undefined;

  constructor() {
    super();
    this._list = undefined;
    this._editor = undefined;
    this._culture = '';
    this._problems = [];
    this._fileProblems = [];
    this._preview = { state: 'waiting' };
    this._fetched = undefined;
    this._fetching = false;
    this._saved = false;
    this.consumeContext(OPTION_LIST_WORKSPACE_CONTEXT, (context) => {
      this.#context = context;
      this.observe(context?.data, (data) => this.#show(data), 'data');
      this.observe(context?.persistedData, () => this.#readSaved(), 'persisted');
      this.observe(context?.problems, (problems) => (this._problems = problems ?? []), 'problems');
    });
  }

  override connectedCallback() {
    super.connectedCallback();
    void readEditor(this).then((editor) => {
      this._editor = editor;
      this._culture ||= this.#defaultCulture;
      this.#schedule();
    });
  }

  override disconnectedCallback() {
    super.disconnectedCallback();
    clearTimeout(this.#timer);
    this.#asking?.abort();
    this.#undo?.close();
  }

  #term = (key: string, ...args: unknown[]) => this.localize.term(`jfbOptionLists_${key}`, ...args);

  get #defaultCulture(): string {
    return this._editor?.languages.find((language) => language.isDefault)?.culture ?? '';
  }

  #kindOf(kind: string | null | undefined) {
    return this._editor?.kinds.find((each) => each.kind === kind);
  }

  #show(data: OptionListDetailModel | undefined) {
    const before = this._list;
    this._list = data;
    if (data && data.unique !== before?.unique) {
      this._fileProblems = [];
      this._fetched = undefined;
    }
    this.#readSaved();
    this.#schedule();
  }

  /** Whether the list holds what was saved, and for a saved JSON address what this server fetched of it. */
  #readSaved() {
    const saved = this.#context?.isSavedAsShown() ?? false;
    this._saved = saved;
    const list = this._list;
    if (!saved || list?.kind !== 'url' || this._fetched !== undefined || this.#readingFetched === list.unique) return;
    this.#readingFetched = list.unique;
    void readFetched(this, list.unique).then((fetched) => {
      this.#readingFetched = undefined;
      if (this._list?.unique === list.unique) this._fetched = fetched;
    });
  }

  // ---- The preview ---------------------------------------------------------------------------------------------------

  #schedule() {
    const list = this._list;
    if (!list || !this._editor) return;
    const asked = JSON.stringify([list.kind, list.config, list.items, list.cacheSeconds, this._culture]);
    if (asked === this.#asked) return;
    this.#asked = asked;
    clearTimeout(this.#timer);
    this.#timer = setTimeout(() => void this.#ask(list), PAUSE_MS);
  }

  async #ask(list: OptionListDetailModel) {
    this.#asking?.abort();
    const asking = new AbortController();
    this.#asking = asking;
    const culture = this._culture === this.#defaultCulture ? '' : this._culture;
    const answer = await previewList(this, list, culture, asking.signal).catch(() => undefined);
    if (asking.signal.aborted) return;
    this._preview = answer ? { state: 'read', answer } : { state: 'failed' };
  }

  // ---- Changes -------------------------------------------------------------------------------------------------------

  #change = (update: (field: DesignField) => DesignField) => {
    const list = this._list;
    if (!list) return;
    this.#context?.setContents(contentsOf(update(fieldOf(list))));
  };

  #onFile = async (event: Event) => {
    const input = event.target as HTMLInputElement;
    const file = input.files?.[0];
    input.value = '';
    const list = this._list;
    if (!file || !list) return;
    const largest = this._editor?.maxFileBytes ?? 4 * 1024 * 1024;
    if (file.size > largest) {
      this._fileProblems = [
        { code: 'limit_exceeded', path: '$.file', message: this.#term('uploadTooLarge', largest / (1024 * 1024)) },
      ];
      return;
    }
    const answer = await readFile(this, file);
    if (answer.kind !== 'read') {
      this._fileProblems =
        answer.kind === 'invalid'
          ? answer.problems
          : [{ code: '', path: '$.file', message: this.#term('uploadFailed') }];
      return;
    }
    this._fileProblems = [];
    const before = list.items;
    this.#context?.setContents({ config: list.config, items: answer.items });
    // Fixed options the file replaced come back with Undo; a file list's are its file's, as its name says.
    if (list.kind === 'manual' && before.length > 0) await this.#offerUndo(before, answer.items.length);
  };

  async #offerUndo(before: readonly DesignOption[], count: number) {
    this.#undo?.close();
    const data: UndoNotificationData = {
      message: this.#term('replaced', count),
      undoLabel: this.#term('undo'),
      undo: async () => {
        const list = this._list;
        if (list) this.#context?.setContents({ config: list.config, items: before });
      },
    };
    this.#undo = (await this.getContext(UMB_NOTIFICATION_CONTEXT))?.peek('positive', {
      elementName: UNDO_NOTIFICATION,
      duration: UNDO_DURATION_MS,
      data,
    });
  }

  async #fetch() {
    const list = this._list;
    if (!list || this._fetching) return;
    this._fetching = true;
    const fetched = await fetchNow(this, list.unique).finally(() => (this._fetching = false));
    if (this._list?.unique !== list.unique) return;
    if (fetched) {
      this._fetched = fetched;
      // The preview asks again, now that the copy is new.
      this.#asked = '';
      this.#schedule();
    } else
      (await this.getContext(UMB_NOTIFICATION_CONTEXT))?.peek('danger', {
        data: { message: this.#term('fetchFailed') },
      });
  }

  // ---- Drawing -------------------------------------------------------------------------------------------------------

  #when(iso: string): string {
    const at = new Date(iso);
    const lang = this.localize.lang() || 'en';
    const time = new Intl.DateTimeFormat(lang, { timeStyle: 'short' }).format(at);
    return at.toDateString() === new Date().toDateString()
      ? this.#term('today', time)
      : new Intl.DateTimeFormat(lang, { dateStyle: 'medium', timeStyle: 'short' }).format(at);
  }

  #keepLabel(seconds: number): string {
    const { unit, count } = keepUnit(seconds);
    return this.#term(`keep_${unit}`, count);
  }

  /** The problems of a place as one list, the server's words: they name the setting and what is wrong with it. */
  #problemList(problems: readonly ListProblem[], id: string): TemplateResult | typeof nothing {
    if (problems.length === 0) return nothing;
    return html`<ul class="problems" id=${id} role="alert">
      ${problems.map((problem) => html`<li>${problem.message}</li>`)}
    </ul>`;
  }

  #headerProblems(): TemplateResult | typeof nothing {
    const said = [
      ...problemsAt(this._problems, 'name').map(() => this.#term('nameMissing')),
      ...problemsAt(this._problems, 'alias').map((problem) =>
        problem.code === 'alias_duplicate' ? this.#term('aliasTaken') : this.#term('aliasInvalid'),
      ),
      ...problemsAt(this._problems, 'other').map((problem) => problem.message),
    ];
    if (said.length === 0) return nothing;
    return html`<p class="alert wide" role="alert">${said.join(' ')}</p>`;
  }

  #languageSwitch(): TemplateResult | typeof nothing {
    const languages = this._editor?.languages ?? [];
    if (languages.length < 2) return nothing;
    return html`<div class="language wide">
      <label for="language">${this.#term('previewLanguage')}</label>
      <select
        id="language"
        @change=${(event: Event) => {
          this._culture = (event.target as HTMLSelectElement).value;
          this.#schedule();
        }}
      >
        ${languages.map(
          (language) =>
            html`<option value=${language.culture} ?selected=${language.culture === this._culture}>
              ${language.name}
            </option>`,
        )}
      </select>
    </div>`;
  }

  #upload(list: OptionListDetailModel): TemplateResult {
    const label = list.kind === 'file' && list.items.length > 0 ? this.#term('uploadNew') : this.#term('upload');
    const problems = [...this._fileProblems, ...problemsAt(this._problems, 'file')];
    return html`<div class="upload">
      ${
        list.kind === 'file'
          ? html`<p class="lead">
              ${list.items.length > 0 ? this.#term('fileOptions', list.items.length) : this.#term('fileNeeded')}
            </p>`
          : nothing
      }
      <div>
        <input
          class="visually-hidden"
          type="file"
          id="option-file"
          accept=".csv,.txt,text/csv,text/plain"
          tabindex="-1"
          aria-hidden="true"
          @change=${this.#onFile}
        />
        <uui-button
          look="secondary"
          label=${label}
          aria-describedby="option-file-help${problems.length > 0 ? ' option-file-problems' : ''}"
          @click=${() => this.shadowRoot?.querySelector<HTMLInputElement>('#option-file')?.click()}
        ></uui-button>
      </div>
      <p class="help" id="option-file-help">${this.#term('uploadHelp')}</p>
      ${this.#problemList(problems, 'option-file-problems')}
    </div>`;
  }

  #keep(list: OptionListDetailModel): TemplateResult | typeof nothing {
    const kind = this.#kindOf(list.kind);
    if (!kind?.keepsOptions) return nothing;
    const url = list.kind === 'url';
    const problems = problemsAt(this._problems, 'keep');
    return html`<div class="item">
      <label class="label" for="keep">${url ? this.#term('keepUrl') : this.#term('keepCode')}</label>
      <p class="help" id="keep-help">${url ? this.#term('keepUrlHelp') : this.#term('keepCodeHelp')}</p>
      <select
        id="keep"
        aria-describedby="keep-help${problems.length > 0 ? ' keep-problems' : ''}"
        @change=${(event: Event) => this.#context?.setCacheSeconds(Number((event.target as HTMLSelectElement).value))}
      >
        ${keepTimes(kind.minCacheSeconds, list.cacheSeconds).map(
          (seconds) =>
            html`<option value=${seconds} ?selected=${seconds === list.cacheSeconds}>
              ${this.#keepLabel(seconds)}
            </option>`,
        )}
      </select>
      ${this.#problemList(problems, 'keep-problems')} ${url ? this.#fetchedLine(list) : nothing}
    </div>`;
  }

  #fetchedLine(list: OptionListDetailModel): TemplateResult | typeof nothing {
    if (list.concurrencyVersion === 0) return nothing;
    if (!this._saved) return html`<p class="help">${this.#term('fetchAfterSave')}</p>`;
    const fetched = this._fetched;
    const line = !fetched
      ? ''
      : fetched.fetchedUtc
        ? fetched.failing
          ? this.#term('fetchFailing', this.#when(fetched.fetchedUtc))
          : this.#term('fetched', this.#when(fetched.fetchedUtc), fetched.count)
        : fetched.failing
          ? this.#term('fetchFailingNone')
          : this.#term('fetchedNever');
    return html`<div class="fetched">
      <p class="muted" role="status">${line}</p>
      <uui-button
        look="secondary"
        label=${this.#term('fetchNow')}
        .state=${this._fetching ? 'waiting' : undefined}
        @click=${() => void this.#fetch()}
      ></uui-button>
    </div>`;
  }

  #contents(list: OptionListDetailModel): TemplateResult {
    const kind: ListKind | 'unknown' = list.kind ?? 'unknown';
    const contents = problemsAt(this._problems, 'contents');
    const heading = this.#term(`kind_${kind}`);
    return html`<uui-box headline=${heading} headline-variant="h2">
      <div class="stack">
        ${
          list.kind && list.kind !== 'file'
            ? html`<jfb-option-editor
                fixed
                .heading=${heading}
                .field=${fieldOf(list)}
                .type=${LIST_TYPE}
                .culture=${this._culture || this.#defaultCulture}
                .defaultCulture=${this.#defaultCulture}
                .cards=${true}
                .change=${this.#change}
              ></jfb-option-editor>`
            : nothing
        }
        ${list.kind === 'manual' || list.kind === 'file' ? this.#upload(list) : nothing} ${this.#keep(list)}
        ${this.#problemList(contents, 'contents-problems')}
      </div>
    </uui-box>`;
  }

  #previewBody(list: OptionListDetailModel): TemplateResult {
    if (list.kind === 'content' && !list.config['root'])
      return html`<p class="lead">${this.#term('previewOnPage')}</p>`;
    const preview = this._preview;
    if (preview.state === 'waiting') return skeletonLines(this.#term('previewLoading'), [70, 45, 55]);
    if (preview.state === 'failed') return html`<p class="lead" role="alert">${this.#term('previewFailed')}</p>`;
    const answer = preview.answer;
    if (answer.problems.length > 0) return html`<p class="lead">${this.#term('previewProblems')}</p>`;
    if (!answer.isAvailable) return html`<p class="lead">${this.#term('previewUnavailable')}</p>`;
    if (answer.options.length === 0) return html`<p class="lead">${this.#term('previewEmpty')}</p>`;
    const labelOf = (option: (typeof answer.options)[number]) => option.label || option.value;
    const { first, more } = firstOf(answer.options, SHOWN);
    return html`<div class="item">
        <label class="label" for="as-dropdown">${this.#term('asDropdown')}</label>
        <select id="as-dropdown">
          <option value="">${this.#term('choose')}</option>
          ${answer.options.map((option) => html`<option value=${option.value}>${labelOf(option)}</option>`)}
        </select>
      </div>
      <fieldset>
        <legend>${this.#term('asRadios')}</legend>
        <div class="radios">
          ${first.map(
            (option, at) =>
              html`<label
                ><input
                  type="radio"
                  name="as-radios"
                  value=${option.value}
                  id=${`as-radio-${at}`}
                />${labelOf(option)}</label
              >`,
          )}
        </div>
        ${more > 0 ? html`<p class="help">${this.#term('more', more)}</p>` : nothing}
      </fieldset>`;
  }

  override render() {
    const list = this._list;
    if (!list || !this._editor) return html`<uui-box>${skeletonLines('', [64, 48, 56])}</uui-box>`;
    return html`<div class="page">
      ${this.#headerProblems()} ${this.#languageSwitch()} ${this.#contents(list)}
      <uui-box headline=${this.#term('preview')} headline-variant="h2">
        <div class="preview" aria-live="polite">${this.#previewBody(list)}</div>
      </uui-box>
    </div>`;
  }
}

if (!customElements.get('jfb-option-list-view')) customElements.define('jfb-option-list-view', OptionListViewElement);

export { OptionListViewElement as element };

declare global {
  interface HTMLElementTagNameMap {
    'jfb-option-list-view': OptionListViewElement;
  }
}
