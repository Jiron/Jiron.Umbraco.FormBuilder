// Export... and Export selected... (ROADMAP 11.4, IA 6.4, ADR-0210): a sidebar that opens with the user's last choices for
// the form: the format, the entries it takes (the list's filter, or the ticked ones) with their number, the fields, and
// under "More options" what only some formats take. Its button names what happens and how many entries go. An export
// that fits a download saves its file at once, a printable view opens the browser's print dialog, and a larger export
// goes on in the background, where a toast follows it to its file. The user's background exports of the form are listed
// at the end, so a page that loaded again still finds them.
import { css, html, nothing, type TemplateResult } from '@umbraco-cms/backoffice/external/lit';
import { UmbModalBaseElement } from '@umbraco-cms/backoffice/modal';
import { UMB_NOTIFICATION_CONTEXT } from '@umbraco-cms/backoffice/notification';
import type {
  EntryExportFormatResponseModel,
  EntryExportPlanResponseModel,
  EntryExportStatusResponseModel,
  EntryExportViewResponseModel,
  EntryListFieldResponseModel,
} from '../api';
import { saveBlob } from '../library/actions/export-form.action';
import { jfbBackofficeTokens } from '../styles/tokens';
import { jfbSkeleton, skeletonLines } from '../ui/skeleton';
import { readView } from './entries-api';
import { planExport, readExportView, startExport, writeExportFile } from './export-api';
import { followExport, saveExport } from './export-follow';
import {
  choicesOf,
  DELIMITERS,
  entriesLine,
  unconfirmedLine,
  exportLabel,
  fieldsFor,
  hasSensitive,
  HEADERS,
  inBackground,
  narrows,
  requestOf,
  statusLine,
  type ExportChoices,
} from './export-model';
import { printDocument } from './export-print';
import type { EntryExportModalData, EntryExportModalValue } from './entry-modals';
import { loadEntryWords } from './entry-words';

export class EntryExportModalElement extends UmbModalBaseElement<EntryExportModalData, EntryExportModalValue> {
  static override properties = {
    ...UmbModalBaseElement.properties,
    _view: { state: true },
    _fields: { state: true },
    _choices: { state: true },
    _plan: { state: true },
    _planFailed: { state: true },
    _readFailed: { state: true },
    _options: { state: true },
    _busy: { state: true },
    _problem: { state: true },
  };

  static override styles = [
    jfbBackofficeTokens,
    jfbSkeleton,
    css`
      .stack {
        display: grid;
        gap: var(--uui-size-space-5);
      }

      .box {
        display: grid;
        gap: var(--uui-size-space-5);
      }

      .field {
        display: grid;
        gap: var(--uui-size-space-2);
        justify-items: start;
      }

      /* The browser's own dropdown, drawn as the section's others are, with the arrow the section draws. */
      .field > select {
        box-sizing: border-box;
        inline-size: 100%;
        min-block-size: 36px;
        padding: 0 var(--uui-size-space-3);
        border: 1px solid var(--jfb-bo-color-control-border);
        border-radius: var(--uui-border-radius);
        background-color: var(--uui-color-surface);
        color: var(--uui-color-text);
        font: inherit;
      }

      .field > select:focus-visible {
        outline: 2px solid var(--jfb-bo-color-focus);
        outline-offset: 1px;
      }

      fieldset {
        margin: 0;
        padding: 0;
        border: 0;
        min-inline-size: 0;
      }

      legend,
      .label {
        padding: 0;
        font-weight: 700;
      }

      /* A legend is no grid item of its fieldset, so it keeps its own space; a label's comes from the field's gap. */
      legend {
        margin: 0 0 var(--uui-size-space-2);
      }

      p {
        margin: 0;
      }

      .help {
        color: var(--jfb-bo-color-text-muted);
        font-size: var(--uui-type-small-size);
      }

      .chosen {
        display: grid;
        gap: var(--uui-size-space-2);
        margin-block-start: var(--uui-size-space-3);
        padding-inline-start: var(--uui-size-space-5);
      }

      .sensitive {
        margin-block-start: var(--uui-size-space-3);
      }

      .options {
        display: grid;
        gap: var(--uui-size-space-5);
        padding-block-start: var(--uui-size-space-2);
      }

      .toggle {
        justify-self: start;
      }

      .toggle uui-icon {
        transition: transform 160ms cubic-bezier(0.23, 1, 0.32, 1);
      }

      .toggle[aria-expanded='true'] uui-icon {
        transform: rotate(180deg);
      }

      @media (prefers-reduced-motion: reduce) {
        .toggle uui-icon {
          transition: none;
        }
      }

      .exports {
        display: grid;
        gap: var(--uui-size-space-3);
        margin: 0;
        padding: 0;
        list-style: none;
      }

      .exports li {
        display: flex;
        flex-wrap: wrap;
        align-items: center;
        justify-content: space-between;
        gap: var(--uui-size-space-2) var(--uui-size-space-4);
      }

      .failed {
        display: grid;
        gap: var(--uui-size-space-3);
        justify-items: start;
        margin: 0;
        color: var(--jfb-bo-color-danger-text);
      }

      .actions {
        display: flex;
        flex-wrap: wrap;
        justify-content: flex-end;
        gap: var(--uui-size-space-2);
      }
    `,
  ];

  declare private _view: EntryExportViewResponseModel | undefined;
  declare private _fields: readonly EntryListFieldResponseModel[];
  declare private _choices: ExportChoices | undefined;
  declare private _plan: EntryExportPlanResponseModel | undefined;
  declare private _planFailed: boolean;
  declare private _readFailed: boolean;
  declare private _options: boolean;
  declare private _busy: boolean;
  declare private _problem: string;

  constructor() {
    super();
    this._fields = [];
    this._planFailed = false;
    this._readFailed = false;
    this._options = false;
    this._busy = false;
    this._problem = '';
  }

  override connectedCallback() {
    super.connectedCallback();
    this.#closed = false;
    void this.#read();
  }

  override disconnectedCallback() {
    this.#closed = true;
    super.disconnectedCallback();
  }

  #term = (key: string, ...args: unknown[]) => this.localize.term(`jfbEntries_${key}`, ...args);

  get #ticked(): readonly string[] | undefined {
    const entries = this.data?.entries;
    return entries && entries.length > 0 ? entries : undefined;
  }

  get #format(): EntryExportFormatResponseModel | undefined {
    return this._view?.formats.find((format) => format.name === this._choices?.format);
  }

  get #offered(): EntryListFieldResponseModel[] {
    return fieldsFor(this._fields, this.#format, this._choices?.sensitive ?? false);
  }

  async #read() {
    const form = this.data?.form;
    if (!form) return;
    this._readFailed = false;
    const [, view, list] = await Promise.all([
      loadEntryWords(this.localize.lang()).catch(() => undefined),
      readExportView(this, form),
      readView(this, form),
    ]);
    if (!view || !list) {
      this._readFailed = true;
      return;
    }

    this._view = view;
    this._fields = list.fields;
    this._choices = choicesOf(view);
    void this.#count();
  }

  /**
   * Counts the entries once: neither the format nor the fields change which entries go. Whether they go to the
   * background, which a format that writes files decides too, is `inBackground`'s.
   */
  async #count() {
    const form = this.data?.form;
    const choices = this._choices;
    if (!form || !choices) return;
    this._planFailed = false;
    const planned = await planExport(this, form, this.#request(choices));
    if ('value' in planned) this._plan = planned.value;
    else this._planFailed = true;
  }

  #request(choices: ExportChoices) {
    return requestOf(choices, this.#offered, this.data?.form ?? '', this.data?.filter, this.#ticked);
  }

  #change(change: Partial<ExportChoices>) {
    if (!this._choices) return;
    this._choices = { ...this._choices, ...change };
    this._problem = '';
  }

  /** Ticks or unticks one field; unticking one of "All fields" keeps the others. */
  #tickField(key: string, ticked: boolean) {
    const choices = this._choices;
    if (!choices) return;
    const all = this.#offered.map((field) => field.key);
    const now = new Set(choices.fields ?? all);
    if (ticked) now.add(key);
    else now.delete(key);
    this.#change({ fields: all.filter((one) => now.has(one)) });
  }

  /** Whether the sidebar went, by Cancel or otherwise: a file that comes after it is neither saved nor printed. */
  #closed = false;

  async #export() {
    const form = this.data?.form;
    const choices = this._choices;
    const plan = this._plan;
    const format = this.#format;
    if (!form || !choices || !plan || !format || this._busy) return;
    this._busy = true;
    this._problem = '';
    const request = this.#request(choices);
    if (inBackground(plan, format)) {
      const started = await startExport(this, form, request);
      this._busy = false;
      if (!('value' in started)) {
        this._problem = started.code === 'exports_busy' ? 'export_busy' : 'export_failed';
        return;
      }

      await followExport(this, form, started.value);
      this.value = { done: 'background' };
      this._submitModal();
      return;
    }

    const file = await writeExportFile(this, form, request);
    // Cancelled meanwhile: the user no longer asks for the file.
    if (this.#closed) return;
    if (!('value' in file)) {
      this._busy = false;
      // Entries came in since the count, and the export no longer fits a download: counted again, it says so and goes
      // to the background on the next press.
      if (file.code === 'export_in_background') void this.#count();
      else this._problem = 'export_failed';
      return;
    }

    if (format.name === 'print') {
      const html = await file.value.text();
      if (this.#closed) return;
      const printed = await printDocument(html);
      this._busy = false;
      if (!printed) {
        this._problem = 'export_printFailed';
        return;
      }

      this.value = { done: 'printed' };
    } else {
      saveBlob(format.fileName, file.value);
      this._busy = false;
      this.value = { done: 'saved' };
    }

    this._submitModal();
  }

  async #download(status: EntryExportStatusResponseModel) {
    const form = this.data?.form;
    const notices = await this.getContext(UMB_NOTIFICATION_CONTEXT).catch(() => undefined);
    if (form && notices) await saveExport(notices, form, status);
  }

  #formatField(view: EntryExportViewResponseModel, choices: ExportChoices): TemplateResult {
    const options = view.formats.map((format) => ({
      name: this.localize.string(format.displayName),
      value: format.name,
      selected: format.name === choices.format,
    }));
    return html`<div class="field">
      <uui-label for="format">${this.#term('export_format')}</uui-label>
      <select
        id="format"
        aria-label=${this.#term('export_format')}
        @change=${(event: Event) => this.#change({ format: (event.target as HTMLSelectElement).value })}
      >
        ${options.map((one) => html`<option value=${one.value} ?selected=${one.selected}>${one.name}</option>`)}
      </select>
    </div>`;
  }

  #entriesField(): TemplateResult {
    const plan = this._plan;
    let line: TemplateResult;
    if (plan) {
      const unconfirmed = unconfirmedLine(this.#term, plan, !!this.#ticked);
      line = html`<p>${entriesLine(this.#term, plan, !!this.#ticked, narrows(this.data?.filter))}</p>
        ${unconfirmed ? html`<p class="help">${unconfirmed}</p>` : nothing}
        ${inBackground(plan, this.#format) ? html`<p class="help">${this.#term('export_background')}</p>` : nothing}`;
    } else if (this._planFailed) {
      line = html`<div class="failed" role="alert">
        <span>${this.#term('export_entriesFailed')}</span>
        <uui-button look="outline" compact label=${this.#term('retry')} @click=${() => void this.#count()}></uui-button>
      </div>`;
    } else {
      line = skeletonLines(this.#term('export_loading'), [70]);
    }

    return html`<div class="field">
      <span class="label">${this.#term('export_entries')}</span>
      ${line}
    </div>`;
  }

  #fieldsField(view: EntryExportViewResponseModel, choices: ExportChoices): TemplateResult {
    const format = this.#format;
    const offered = this.#offered;
    const all = choices.fields === null;
    const chosen = new Set(choices.fields ?? offered.map((field) => field.key));
    const sensitive = hasSensitive(this._fields);
    const files = format?.writesFiles ?? false;
    if (files && offered.length === 0 && !sensitive)
      return html`<div class="field">
        <span class="label">${this.#term('export_fields')}</span>
        <p class="help">${this.#term('export_noFileFields')}</p>
      </div>`;

    return html`<fieldset class="field">
      <legend>${this.#term('export_fields')}</legend>
      <uui-checkbox
        label=${this.#term(files ? 'export_allFileFields' : 'export_allFields', offered.length)}
        .checked=${all}
        @change=${(event: Event) =>
          this.#change({
            fields: (event.target as HTMLInputElement).checked ? null : offered.map((field) => field.key),
          })}
      ></uui-checkbox>
      ${
        all
          ? nothing
          : html`<div class="chosen">
              ${offered.map(
                (field) =>
                  html`<uui-checkbox
                    label=${field.label}
                    .checked=${chosen.has(field.key)}
                    @change=${(event: Event) => this.#tickField(field.key, (event.target as HTMLInputElement).checked)}
                  ></uui-checkbox>`,
              )}
            </div>`
      }
      ${
        !sensitive
          ? nothing
          : view.maySeeSensitive
            ? html`<div class="sensitive">
                <uui-checkbox
                  label=${this.#term('export_sensitive')}
                  .checked=${choices.sensitive}
                  @change=${(event: Event) => this.#change({ sensitive: (event.target as HTMLInputElement).checked })}
                ></uui-checkbox>
                ${choices.sensitive ? nothing : html`<p class="help">${this.#term('export_sensitiveLeftOut')}</p>`}
              </div>`
            : html`<p class="help sensitive">${this.#term('export_sensitiveLeftOut')}</p>`
      }
    </fieldset>`;
  }

  #optionsField(choices: ExportChoices): TemplateResult | typeof nothing {
    const format = this.#format;
    if (!format || (!format.takesDelimiter && !format.takesHeaders)) return nothing;
    return html`<uui-button
        class="toggle"
        look="default"
        compact
        label=${this.#term('export_moreOptions')}
        aria-expanded=${this._options ? 'true' : 'false'}
        aria-controls="options"
        @click=${() => (this._options = !this._options)}
      >
        ${this.#term('export_moreOptions')}
        <uui-icon name="icon-navigation-down" aria-hidden="true"></uui-icon>
      </uui-button>
      ${
        this._options
          ? html`<div class="options" id="options">
              ${
                format.takesDelimiter
                  ? html`<fieldset>
                      <legend>${this.#term('export_delimiter')}</legend>
                      <uui-radio-group
                        .value=${choices.delimiter}
                        @change=${(event: Event) =>
                          this.#change({
                            delimiter: (event.target as HTMLInputElement).value as ExportChoices['delimiter'],
                          })}
                      >
                        ${DELIMITERS.map(
                          (delimiter) =>
                            html`<uui-radio
                              value=${delimiter}
                              label=${this.#term(`export_delimiter_${delimiter}`)}
                            ></uui-radio>`,
                        )}
                      </uui-radio-group>
                    </fieldset>`
                  : nothing
              }
              ${
                format.takesHeaders
                  ? html`<fieldset>
                      <legend>${this.#term('export_headers')}</legend>
                      <uui-radio-group
                        .value=${choices.headers}
                        @change=${(event: Event) =>
                          this.#change({
                            headers: (event.target as HTMLInputElement).value as ExportChoices['headers'],
                          })}
                      >
                        ${HEADERS.map(
                          (headers) =>
                            html`<uui-radio
                              value=${headers}
                              label=${this.#term(`export_headers_${headers}`)}
                            ></uui-radio>`,
                        )}
                      </uui-radio-group>
                    </fieldset>`
                  : nothing
              }
              ${
                format.takesDelimiter
                  ? html`<div class="field">
                      <uui-checkbox
                        label=${this.#term('export_byteOrderMark')}
                        .checked=${choices.byteOrderMark}
                        @change=${(event: Event) =>
                          this.#change({ byteOrderMark: (event.target as HTMLInputElement).checked })}
                      ></uui-checkbox>
                      <p class="help">${this.#term('export_byteOrderMarkHelp')}</p>
                    </div>`
                  : nothing
              }
            </div>`
          : nothing
      }`;
  }

  #exports(view: EntryExportViewResponseModel): TemplateResult | typeof nothing {
    if (view.exports.length === 0) return nothing;
    return html`<uui-box headline=${this.#term('export_yours')}>
      <ul class="exports">
        ${view.exports.map(
          (status) =>
            html`<li>
              <span>${statusLine(this.#term, status, this.localize.lang())}</span>
              ${
                status.state === 'ready'
                  ? html`<uui-button
                      look="outline"
                      compact
                      label=${this.#term('export_download')}
                      @click=${() => void this.#download(status)}
                    ></uui-button>`
                  : nothing
              }
            </li>`,
        )}
      </ul>
    </uui-box>`;
  }

  #body(): TemplateResult {
    const view = this._view;
    const choices = this._choices;
    if (!view || !choices)
      return this._readFailed
        ? html`<uui-box>
            <div class="failed" role="alert">
              <span>${this.#term('export_readFailed')}</span>
              <uui-button look="outline" label=${this.#term('retry')} @click=${() => void this.#read()}></uui-button>
            </div>
          </uui-box>`
        : html`<uui-box>${skeletonLines(this.#term('export_loading'), [50, 70, 60, 40])}</uui-box>`;

    return html`<div class="stack">
      <uui-box>
        <div class="box">
          ${this.#formatField(view, choices)} ${this.#entriesField()} ${this.#fieldsField(view, choices)}
          ${this.#optionsField(choices)}
        </div>
      </uui-box>
      ${this.#exports(view)}
      ${this._problem ? html`<p class="failed" role="alert">${this.#term(this._problem)}</p>` : nothing}
    </div>`;
  }

  override render() {
    // An export of nothing is no file anyone needs: the button waits for an entry to export.
    const ready = !!this._plan && !!this._choices && (this._plan.entries > 0 || this._plan.more);
    return html`<umb-body-layout headline=${this.#term('export_headline')}>
      ${this.#body()}
      <div slot="actions" class="actions">
        <uui-button label=${this.#term('export_cancel')} @click=${this._rejectModal}></uui-button>
        <uui-button
          look="primary"
          color="positive"
          label=${exportLabel(this.#term, this._choices?.format ?? '', this._plan)}
          ?disabled=${!ready}
          .state=${this._busy ? 'waiting' : undefined}
          @click=${() => void this.#export()}
        ></uui-button>
      </div>
    </umb-body-layout>`;
  }
}

if (!customElements.get('jfb-entry-export-modal'))
  customElements.define('jfb-entry-export-modal', EntryExportModalElement);

export { EntryExportModalElement as element };

declare global {
  interface HTMLElementTagNameMap {
    'jfb-entry-export-modal': EntryExportModalElement;
  }
}
