// An email layout's one view (ROADMAP 12.1, IA 7, ADR-0215): the look, the logo from the media library or by address,
// the colours of each scheme with the checks of what mail reads on its surface, and the header, the footer and the
// logo's text in the language chosen, beside a sample entry's mail in the layout (under it below 900 px, with a
// switch), light or dark, following each change after a pause. Send test email saves the layout first and mails the
// saved layout to the user's own address.
import { css, html, nothing, type TemplateResult } from '@umbraco-cms/backoffice/external/lit';
import { UmbLitElement } from '@umbraco-cms/backoffice/lit-element';
import type { EmailLayoutEditorResponseModel, EmailLayoutPreviewResponseModel } from '../api';
import { mailDocument } from '../designer/actions/email-preview';
import { controlStyles } from '../designer/inspector/control.styles';
import { say } from '../library/items/library-items';
import { codeOf, sayRefusal } from '../library/library-changes';
import { jfbBackofficeTokens, jfbVisuallyHidden } from '../styles/tokens';
import { jfbSkeleton, skeletonLines } from '../ui/skeleton';
import { contrast, hexOf, suggestion } from '../themes/theme-model';
import { EMAIL_LAYOUT_WORKSPACE_CONTEXT } from './constants';
import {
  COLOURS,
  textKey,
  withColour,
  withText,
  type Colour,
  type EmailLayoutDesign,
  type Scheme,
  type Texts,
} from './email-layout-model';
import type { EmailLayoutWorkspaceContext } from './email-layout-workspace.context';
import { previewLayout, readEditor, sendTest } from './email-layouts-api';
import type { LibraryProblem } from '../library/items/library-items';

/** How long the preview waits after a change before it asks. */
const PAUSE_MS = 400;

/** The colours mail reads as text on its surface, which need 4.5:1 there. */
const TEXT: ReadonlySet<Colour> = new Set(['text', 'muted', 'accent']);

export class EmailLayoutViewElement extends UmbLitElement {
  static override properties = {
    _design: { state: true },
    _editor: { state: true },
    _scheme: { state: true },
    _shown: { state: true },
    _culture: { state: true },
    _problems: { state: true },
    _preview: { state: true },
    _side: { state: true },
    _sending: { state: true },
  };

  static override styles = [
    jfbBackofficeTokens,
    jfbVisuallyHidden,
    jfbSkeleton,
    controlStyles,
    css`
      :host {
        display: block;
        padding: var(--uui-size-layout-1);
        container-type: inline-size;
      }

      .switch {
        display: flex;
        justify-content: flex-end;
      }

      .page {
        display: grid;
        gap: var(--uui-size-space-5);
        align-items: start;
      }

      @container (min-width: 900px) {
        .page {
          grid-template-columns: minmax(22rem, 440px) minmax(0, 1fr);
        }

        .switch {
          display: none;
        }
      }

      @container (max-width: 899px) {
        .page[data-side='settings'] .preview,
        .page[data-side='preview'] .settings {
          display: none;
        }
      }

      /* The box lays its content out itself, so the groups' rhythm lives in a block of their own inside it. */
      .fields {
        display: grid;
        gap: var(--uui-size-space-6);
      }

      .group {
        display: grid;
        gap: var(--uui-size-space-4);
      }

      h3 {
        margin: 0;
        font-size: var(--uui-type-default-size);
        font-weight: 700;
      }

      .alert {
        margin: 0;
        padding: 0;
        list-style: none;
        color: var(--jfb-bo-color-danger-text);
      }

      .colour {
        display: grid;
        grid-template-columns: 8rem 32px minmax(0, 1fr) auto;
        gap: var(--uui-size-space-2) var(--uui-size-space-3);
        align-items: center;
      }

      .colour .help,
      .colour .why {
        grid-column: 2 / -1;
      }

      input[type='color'] {
        box-sizing: border-box;
        inline-size: 32px;
        block-size: 32px;
        padding: 2px;
        border: 1px solid var(--jfb-bo-color-control-border);
        border-radius: var(--uui-border-radius);
        background: var(--uui-color-surface);
        cursor: pointer;
      }

      .fail {
        color: var(--jfb-bo-color-danger-text);
      }

      .link {
        padding: 0;
        border: 0;
        background: none;
        color: var(--uui-color-interactive);
        font: inherit;
        font-weight: 600;
        text-decoration: underline;
        cursor: pointer;
      }

      .link:focus-visible {
        outline: 2px solid var(--jfb-bo-color-focus);
        outline-offset: 2px;
      }

      .head {
        display: flex;
        flex-wrap: wrap;
        align-items: center;
        justify-content: space-between;
        gap: var(--uui-size-space-3);
        margin-block-end: var(--uui-size-space-4);
      }

      .schemes {
        display: inline-flex;
        border: 1px solid var(--uui-color-border-emphasis);
        border-radius: var(--uui-border-radius);
      }

      .schemes button {
        min-block-size: 32px;
        padding: 0 var(--uui-size-space-4);
        border: 0;
        background: none;
        color: var(--uui-color-text);
        font: inherit;
        cursor: pointer;
      }

      .schemes button + button {
        border-inline-start: 1px solid var(--uui-color-border-emphasis);
      }

      .schemes button[aria-pressed='true'] {
        background: var(--uui-color-selected);
        color: var(--uui-color-selected-contrast);
        font-weight: 700;
      }

      .schemes button:focus-visible {
        outline: 2px solid var(--jfb-bo-color-focus);
        outline-offset: 1px;
      }

      iframe {
        display: block;
        inline-size: 100%;
        min-block-size: 28rem;
        border: 1px solid var(--uui-color-border);
        border-radius: var(--uui-border-radius);
        background: var(--uui-color-surface);
      }
    `,
  ];

  declare private _design: EmailLayoutDesign | undefined;
  declare private _editor: EmailLayoutEditorResponseModel | undefined | null;
  /** The scheme whose colours the settings show. */
  declare private _scheme: Scheme;
  /** The scheme the preview shows. */
  declare private _shown: Scheme;
  declare private _culture: string;
  declare private _problems: readonly LibraryProblem[];
  declare private _preview: EmailLayoutPreviewResponseModel | 'failed' | undefined;
  declare private _side: 'settings' | 'preview';
  declare private _sending: boolean;

  #context: EmailLayoutWorkspaceContext | undefined;
  #timer: ReturnType<typeof setTimeout> | undefined;
  #asking: AbortController | undefined;

  constructor() {
    super();
    this._design = undefined;
    this._editor = undefined;
    this._scheme = 'light';
    this._shown = 'light';
    this._culture = '';
    this._problems = [];
    this._preview = undefined;
    this._side = 'settings';
    this._sending = false;
    this.consumeContext(EMAIL_LAYOUT_WORKSPACE_CONTEXT, (context) => {
      this.#context = context;
      this.observe(
        context?.design,
        (design) => {
          this._design = design;
          this.#schedule();
        },
        'design',
      );
      this.observe(context?.problems, (problems) => (this._problems = problems ?? []), 'problems');
    });
  }

  override connectedCallback() {
    super.connectedCallback();
    void readEditor(this).then((editor) => {
      this._editor = editor ?? null;
      this._culture = editor?.languages[0]?.culture ?? '';
      this.#schedule();
    });
  }

  override disconnectedCallback() {
    super.disconnectedCallback();
    clearTimeout(this.#timer);
    this.#asking?.abort();
  }

  #term = (key: string, ...args: unknown[]) => this.localize.term(`jfbEmailLayouts_${key}`, ...args);

  #schedule() {
    clearTimeout(this.#timer);
    const design = this._design;
    if (!design || !this._culture) return;
    const culture = this._culture;
    this.#timer = setTimeout(() => void this.#ask(design, culture), PAUSE_MS);
  }

  async #ask(design: EmailLayoutDesign, culture: string) {
    this.#asking?.abort();
    const asking = new AbortController();
    this.#asking = asking;
    const preview = await previewLayout(this, design, culture, asking.signal);
    if (!asking.signal.aborted) this._preview = preview ?? 'failed';
  }

  #change(change: (design: EmailLayoutDesign) => EmailLayoutDesign) {
    if (this._design) this.#context?.setDesign(change(this._design));
  }

  /** The key the language chosen keeps its texts under. */
  get #key(): string {
    const language = this._editor?.languages.find((one) => one.culture === this._culture);
    return textKey(this._culture, language?.isDefault ?? true);
  }

  #problemAt(path: string): string | undefined {
    const problem = [
      ...this._problems,
      ...(this._preview && this._preview !== 'failed' ? this._preview.problems : []),
    ].find((one) => one.path === path);
    if (!problem) return undefined;
    if (path.includes('logoMedia')) return this.#term('problem_logoMedia');
    if (path.endsWith('logoUrl')) return this.#term('problem_logoUrl');
    return this.#term(problem.code === 'limit_exceeded' ? 'problem_long' : 'problem_colour');
  }

  async #sendTest() {
    if (this._sending || !this.#context) return;
    this._sending = true;
    try {
      // The test mail is the saved layout, as forms send it: a change not saved yet is saved first.
      if (!(await this.#context.saveFirst())) return;
      const key = this.#context.getUnique();
      if (!key) return;
      const { to, error } = await sendTest(this, key, this._culture);
      if (to !== undefined) {
        await say(this, 'positive', this.#term('testSent', to));
        return;
      }
      const code = codeOf(error);
      if (code === 'email_not_configured') await say(this, 'danger', this.#term('testNotConfigured'));
      else if (code === 'email_no_address') await say(this, 'danger', this.#term('testNoAddress'));
      else await sayRefusal(this, error);
    } finally {
      this._sending = false;
    }
  }

  #schemes(value: Scheme, set: (scheme: Scheme) => void, label: string): TemplateResult {
    return html`<div class="schemes" role="group" aria-label=${label}>
      ${(['light', 'dark'] as const).map(
        (scheme) =>
          html`<button type="button" aria-pressed=${scheme === value ? 'true' : 'false'} @click=${() => set(scheme)}>
            ${this.#term(scheme)}
          </button>`,
      )}
    </div>`;
  }

  #check(scheme: Scheme, name: Colour, value: string | undefined, id: string): TemplateResult {
    const editor = this._editor;
    const design = this._design;
    if (!editor || !design || !TEXT.has(name)) return html`<span id=${id} hidden></span>`;
    const colour = hexOf(value);
    const ground = hexOf(design[scheme].surface ?? editor[scheme].surface ?? undefined);
    if (!colour || !ground) return html`<p class="help" id=${id}>${this.#term('checkUnchecked')}</p>`;
    const ratio = contrast(colour, ground);
    const said = new Intl.NumberFormat(this.localize.lang(), {
      minimumFractionDigits: 1,
      maximumFractionDigits: 1,
    }).format(Math.floor(ratio * 10) / 10);
    if (ratio >= 4.5) return html`<p class="help" id=${id}>${this.#term('checkPass', said)}</p>`;
    const better = suggestion(colour, ground, 4.5);
    return html`<p class="help fail" id=${id}>
      ${this.#term('checkFail', said)}
      ${
        better
          ? html`<button
              type="button"
              class="link"
              @click=${() => this.#change((one) => ({ ...one, [scheme]: withColour(one[scheme], name, better, editor[scheme][name]) }))}
            >
              ${this.#term('useSuggestion', better)}
            </button>`
          : nothing
      }
    </p>`;
  }

  #colour(scheme: Scheme, name: Colour): TemplateResult {
    const editor = this._editor;
    const design = this._design;
    if (!editor || !design) return html``;
    const own = editor[scheme][name] ?? '';
    const set = design[scheme][name];
    const label = this.#term(`colour_${name}`);
    const id = `colour-${scheme}-${name}`;
    const problem = this.#problemAt(`$.design.${scheme}.${name}`);
    const change = (value: string) =>
      this.#change((one) => ({ ...one, [scheme]: withColour(one[scheme], name, value, own) }));
    return html`<div class="colour">
      <label class="label" for=${id}>${label}</label>
      <input
        type="color"
        aria-label=${this.#term('colourPicker', label)}
        .value=${hexOf(set ?? own) ?? '#000000'}
        @change=${(event: Event) => change((event.target as HTMLInputElement).value)}
      />
      <input
        id=${id}
        class="code"
        .value=${set ?? ''}
        placeholder=${own}
        aria-invalid=${problem ? 'true' : 'false'}
        aria-describedby=${`${id}-check`}
        @change=${(event: Event) => change((event.target as HTMLInputElement).value)}
      />
      ${
        set === undefined
          ? html`<span></span>`
          : html`<uui-button compact look="secondary" label=${this.#term('reset', label)} @click=${() => change('')}>
              <uui-icon name="icon-undo"></uui-icon>
            </uui-button>`
      }
      ${problem ? html`<p class="why">${problem}</p>` : nothing} ${this.#check(scheme, name, set ?? own, `${id}-check`)}
    </div>`;
  }

  #text(id: string, which: 'header' | 'footer' | 'logoAlt', rows: number): TemplateResult {
    const design = this._design;
    if (!design) return html``;
    const texts: Texts = design[which];
    const key = this.#key;
    const problem = this.#problemAt(`$.design.${which}['${key}']`);
    const change = (text: string) => this.#change((one) => ({ ...one, [which]: withText(one[which], key, text) }));
    return html`<div class="item">
      <label class="label" for=${id}>${this.#term(`text_${which}`)}</label>
      ${which === 'logoAlt' ? nothing : html`<p class="help" id=${`${id}-help`}>${this.#term('textHelp')}</p>`}
      ${
        which === 'logoAlt'
          ? html`<input
              id=${id}
              .value=${texts[key] ?? ''}
              aria-invalid=${problem ? 'true' : 'false'}
              @change=${(event: Event) => change((event.target as HTMLInputElement).value)}
            />`
          : html`<textarea
              id=${id}
              rows=${rows}
              aria-describedby=${`${id}-help`}
              aria-invalid=${problem ? 'true' : 'false'}
              .value=${texts[key] ?? ''}
              @change=${(event: Event) => change((event.target as HTMLTextAreaElement).value)}
            ></textarea>`
      }
      ${problem ? html`<p class="why">${problem}</p>` : nothing}
    </div>`;
  }

  #settings(design: EmailLayoutDesign, editor: EmailLayoutEditorResponseModel): TemplateResult {
    const general = this._problems.flatMap((problem) => {
      if (problem.path === '$.name') return [this.#term('problem_name')];
      if (problem.path === '$.design.look') return [this.#term('problem_other')];
      if (problem.path !== '$.alias') return [];
      return [this.#term(problem.code === 'alias_duplicate' ? 'problem_aliasTaken' : 'problem_alias')];
    });
    const logoProblem = this.#problemAt('$.design.logoMedia') ?? this.#problemAt('$.design.logoUrl');
    return html`<uui-box class="settings"
      ><div class="fields">
        ${
          general.length > 0
            ? html`<ul class="alert" role="alert">
                ${general.map((said) => html`<li>${said}</li>`)}
              </ul>`
            : nothing
        }
        <div class="group">
          <div class="item">
            <label class="label" for="look">${this.#term('look')}</label>
            <select
              id="look"
              @change=${(event: Event) =>
                this.#change((one) => ({
                  ...one,
                  look: (event.target as HTMLSelectElement).value === 'letter' ? 'letter' : 'card',
                }))}
            >
              ${(['card', 'letter'] as const).map(
                (look) =>
                  html`<option value=${look} ?selected=${design.look === look}>${this.#term(`look_${look}`)}</option>`,
              )}
            </select>
            <p class="help">${this.#term(`lookHelp_${design.look}`)}</p>
          </div>
        </div>
        <div class="group" role="group" aria-labelledby="logo-heading">
          <h3 id="logo-heading">${this.#term('logo')}</h3>
          <umb-input-media
            .selection=${design.logoMedia ? [design.logoMedia] : []}
            .max=${1}
            @change=${(event: Event) => {
              const picked = (event.target as HTMLElement & { selection: string[] }).selection[0] ?? null;
              this.#change((one) => ({ ...one, logoMedia: picked }));
            }}
          ></umb-input-media>
          ${
            design.logoMedia
              ? nothing
              : html`<div class="item">
                  <label class="label" for="logo-url">${this.#term('logoUrl')}</label>
                  <input
                    id="logo-url"
                    class="code"
                    inputmode="url"
                    placeholder="https://"
                    .value=${design.logoUrl ?? ''}
                    aria-invalid=${logoProblem ? 'true' : 'false'}
                    @change=${(event: Event) =>
                      this.#change((one) => ({
                        ...one,
                        logoUrl: (event.target as HTMLInputElement).value.trim() || null,
                      }))}
                  />
                </div>`
          }
          ${logoProblem ? html`<p class="why">${logoProblem}</p>` : nothing}
        </div>
        <div class="group" role="group" aria-labelledby="colours-heading">
          <h3 id="colours-heading">${this.#term('colours')}</h3>
          ${this.#schemes(this._scheme, (scheme) => (this._scheme = scheme), this.#term('coloursScheme'))}
          <p class="help">${this.#term(this._scheme === 'dark' ? 'coloursDarkHelp' : 'coloursHelp')}</p>
          ${COLOURS.map((name) => this.#colour(this._scheme, name))}
        </div>
        <div class="group" role="group" aria-labelledby="texts-heading">
          <h3 id="texts-heading">${this.#term('texts')}</h3>
          ${
            editor.languages.length > 1
              ? html`<div class="item">
                  <label class="label" for="language">${this.#term('language')}</label>
                  <select
                    id="language"
                    aria-describedby="language-help"
                    @change=${(event: Event) => {
                      this._culture = (event.target as HTMLSelectElement).value;
                      this.#schedule();
                    }}
                  >
                    ${editor.languages.map(
                      (language) =>
                        html`<option value=${language.culture} ?selected=${language.culture === this._culture}>
                          ${language.name}
                        </option>`,
                    )}
                  </select>
                  <p class="help" id="language-help">${this.#term('languageHelp')}</p>
                </div>`
              : nothing
          }
          ${this.#text('header', 'header', 3)} ${this.#text('footer', 'footer', 4)}
          ${this.#text('logo-alt', 'logoAlt', 1)}
        </div>
      </div></uui-box
    >`;
  }

  #preview(): TemplateResult {
    const preview = this._preview;
    const body =
      preview === undefined
        ? skeletonLines(this.#term('previewLoading'))
        : preview === 'failed'
          ? html`<p class="alert" role="alert">${this.#term('previewFailed')}</p>`
          : preview.problems.length > 0
            ? html`<p class="help" role="status">${this.#term('previewHeld')}</p>`
            : html`<iframe
                sandbox="allow-same-origin"
                title=${this.#term(this._shown === 'dark' ? 'previewDark' : 'previewLight')}
                style=${`color-scheme: ${this._shown}`}
                .srcdoc=${mailDocument(preview.html, this._shown)}
              ></iframe>`;
    return html`<section class="preview" aria-labelledby="preview-heading">
      <div class="head">
        <h3 id="preview-heading">${this.#term('preview')}</h3>
        ${this.#schemes(this._shown, (scheme) => (this._shown = scheme), this.#term('previewScheme'))}
        <uui-button
          look="secondary"
          label=${this.#term('sendTest')}
          .state=${this._sending ? 'waiting' : undefined}
          @click=${() => void this.#sendTest()}
        ></uui-button>
      </div>
      ${body}
    </section>`;
  }

  override render() {
    const design = this._design;
    const editor = this._editor;
    if (!design || editor === undefined) return html`<uui-box>${skeletonLines('')}</uui-box>`;
    if (editor === null)
      return html`<uui-box><p class="alert" role="alert">${this.#term('editorFailed')}</p></uui-box>`;
    return html`<div class="switch">
        <uui-button-group>
          ${(['settings', 'preview'] as const).map(
            (side) =>
              html`<uui-button
                look=${side === this._side ? 'primary' : 'secondary'}
                aria-pressed=${side === this._side ? 'true' : 'false'}
                label=${this.#term(side === 'settings' ? 'showSettings' : 'showPreview')}
                @click=${() => (this._side = side)}
              ></uui-button>`,
          )}
        </uui-button-group>
      </div>
      <div class="page" data-side=${this._side}>${this.#settings(design, editor)} ${this.#preview()}</div>`;
  }
}

if (!customElements.get('jfb-email-layout-view'))
  customElements.define('jfb-email-layout-view', EmailLayoutViewElement);

export { EmailLayoutViewElement as element };

declare global {
  interface HTMLElementTagNameMap {
    'jfb-email-layout-view': EmailLayoutViewElement;
  }
}
