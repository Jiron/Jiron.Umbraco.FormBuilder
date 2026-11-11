// A theme's one view (ROADMAP 12.1, IA 7, ADR-0215): the settings in sections that open and close, each closed one saying
// in one line what it holds, beside the sample form as the website renders it (under it below 900 px, with a switch):
// the colours of each scheme with their contrast checks and a passing colour to use, the type, the spacing and corners,
// the buttons and the focus ring, every other token the base theme declares, and the custom CSS with what the sanitiser
// took out. The preview is the base theme's own page in a frame without scripts, and each change writes only the new
// layer into it, after a pause.
import { css, html, nothing, type TemplateResult } from '@umbraco-cms/backoffice/external/lit';
import { UmbLitElement } from '@umbraco-cms/backoffice/lit-element';
import type {
  ThemeBaseResponseModel,
  ThemeEditorResponseModel,
  ThemePreviewResponseModel,
  ThemeSampleResponseModel,
} from '../api';
import { controlStyles } from '../designer/inspector/control.styles';
import type { LibraryProblem } from '../library/items/library-items';
import { jfbBackofficeTokens, jfbVisuallyHidden } from '../styles/tokens';
import { jfbSkeleton, skeletonLines } from '../ui/skeleton';
import { THEME_WORKSPACE_CONTEXT } from './constants';
import {
  BUTTON_COLOURS,
  changedOf,
  checkOf,
  choicesOf,
  COLOUR_GROUPS,
  FONTS,
  hexOf,
  othersOf,
  RADII,
  RINGS,
  SCALES,
  SIZES,
  SPACINGS,
  valueIn,
  withChoice,
  withToken,
  type ColourCheck,
  type Scheme,
  type ThemeDetailModel,
} from './theme-model';
import type { ThemeWorkspaceContext } from './theme-workspace.context';
import { previewTheme, readEditor, readSample } from './themes-api';
import { withThemeLabel } from './theme-label';

/** How long the preview waits after a change before it asks. */
const PAUSE_MS = 300;

type Section = 'colours' | 'type' | 'spacing' | 'buttons' | 'other' | 'more';
type Shown = Pick<ThemeDetailModel, 'baseTheme' | 'tokens' | 'customCss'>;
type Sample =
  | { readonly state: 'loading' }
  | { readonly state: 'failed' }
  | { readonly state: 'ready'; readonly sample: ThemeSampleResponseModel };

/** What the sections draw while no theme has come yet; they draw only once one has. */
const NONE: Shown = { baseTheme: '', tokens: { light: {}, dark: {} }, customCss: '' };

/** The words of the editor's own colours, by token. */
const camel = (name: string) =>
  name.replace('--jfb-color-', '').replace(/-([a-z])/g, (_, letter: string) => letter.toUpperCase());
const OWN = new Set([...COLOUR_GROUPS.flatMap((group) => group.tokens), ...BUTTON_COLOURS]);

export class ThemeViewElement extends UmbLitElement {
  static override properties = {
    _theme: { state: true },
    _editor: { state: true },
    _scheme: { state: true },
    _open: { state: true },
    _problems: { state: true },
    _preview: { state: true },
    _sample: { state: true },
    _side: { state: true },
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

      /* The settings beside the preview where both fit (IA 7); one of them, by the switch, where they do not. */
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

      .settings {
        --uui-box-default-padding: 0;
      }

      .base,
      .muted {
        margin: 0;
        color: var(--jfb-bo-color-text-muted);
      }

      .base {
        padding: var(--uui-size-space-4) var(--uui-size-space-5);
      }

      .alert {
        margin: 0;
        padding: var(--uui-size-space-4) var(--uui-size-space-5);
        list-style: none;
        color: var(--jfb-bo-color-danger-text);
      }

      .section {
        border-block-start: 1px solid var(--uui-color-border);
      }

      .section h3 {
        margin: 0;
        font: inherit;
      }

      .section h3 button {
        display: grid;
        grid-template-columns: auto minmax(0, 1fr);
        column-gap: var(--uui-size-space-3);
        align-items: center;
        inline-size: 100%;
        min-height: 44px;
        padding: var(--uui-size-space-3) var(--uui-size-space-5) var(--uui-size-space-3) var(--uui-size-space-4);
        border: 0;
        background: none;
        color: inherit;
        font: inherit;
        text-align: start;
        cursor: pointer;
      }

      @media (hover: hover) {
        .section h3 button:hover {
          background: var(--uui-color-surface-emphasis);
        }
      }

      .section h3 button:focus-visible {
        outline: 2px solid var(--jfb-bo-color-focus);
        outline-offset: -2px;
      }

      .name {
        font-weight: 700;
      }

      .summary {
        grid-column: 2;
        color: var(--jfb-bo-color-text-muted);
        font-size: var(--uui-type-small-size);
        overflow-wrap: anywhere;
      }

      .body {
        display: grid;
        gap: var(--uui-size-space-5);
        padding: var(--uui-size-space-2) var(--uui-size-space-5) var(--uui-size-space-6);
      }

      h4 {
        margin: 0;
        font-size: var(--uui-type-small-size);
        font-weight: 700;
        text-transform: none;
      }

      .group {
        display: grid;
        gap: var(--uui-size-space-4);
      }

      /* A colour: its name, its swatch and its value on one line, its check under the swatch and the value. */
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

      .pair {
        display: grid;
        grid-template-columns: repeat(auto-fit, minmax(10rem, 1fr));
        gap: var(--uui-size-space-4);
      }

      textarea.code {
        min-height: 12rem;
      }

      .removed {
        margin: 0;
        padding-inline-start: 1.25em;
      }

      .frames {
        display: grid;
        gap: var(--uui-size-space-5);
      }

      iframe {
        display: block;
        inline-size: 100%;
        min-block-size: 24rem;
        border: 1px solid var(--uui-color-border);
        border-radius: var(--uui-border-radius);
        background: var(--uui-color-surface);
      }
    `,
  ];

  declare private _theme: Shown | undefined;
  declare private _editor: ThemeEditorResponseModel | undefined | null;
  declare private _scheme: Scheme;
  declare private _open: ReadonlySet<Section>;
  declare private _problems: readonly LibraryProblem[];
  declare private _preview: ThemePreviewResponseModel | undefined;
  declare private _sample: Sample;
  declare private _side: 'settings' | 'preview';

  #context: ThemeWorkspaceContext | undefined;
  #timer: ReturnType<typeof setTimeout> | undefined;
  #asking: AbortController | undefined;
  /** The base theme and culture of the sample read last, so it is read again only when they change. */
  #sampleOf = '';

  constructor() {
    super();
    this._theme = undefined;
    this._editor = undefined;
    this._scheme = 'light';
    this._open = new Set(['colours']);
    this._problems = [];
    this._preview = undefined;
    this._sample = { state: 'loading' };
    this._side = 'settings';
    this.consumeContext(THEME_WORKSPACE_CONTEXT, (context) => {
      this.#context = context;
      this.observe(
        context?.theme,
        (theme) => {
          this._theme = theme;
          this.#schedule();
          void this.#readSample();
        },
        'theme',
      );
      this.observe(context?.problems, (problems) => (this._problems = problems ?? []), 'problems');
    });
  }

  override connectedCallback() {
    super.connectedCallback();
    void readEditor(this).then((editor) => {
      this._editor = editor ?? null;
      void this.#readSample();
    });
  }

  override disconnectedCallback() {
    super.disconnectedCallback();
    clearTimeout(this.#timer);
    this.#asking?.abort();
  }

  #term = (key: string, ...args: unknown[]) => this.localize.term(`jfbThemes_${key}`, ...args);

  get #shown(): Shown {
    return this._theme ?? NONE;
  }

  get #base(): ThemeBaseResponseModel | undefined {
    return this._editor?.bases.find((base) => base.alias === this._theme?.baseTheme);
  }

  async #readSample() {
    const base = this.#base;
    if (!base) return;
    const culture = this.localize.lang();
    const wanted = `${base.alias}|${culture}`;
    if (wanted === this.#sampleOf) return;
    this.#sampleOf = wanted;
    this._sample = { state: 'loading' };
    const sample = await readSample(this, base.alias, culture);
    if (wanted === this.#sampleOf) this._sample = sample ? { state: 'ready', sample } : { state: 'failed' };
  }

  #schedule() {
    clearTimeout(this.#timer);
    const theme = this._theme;
    if (!theme) return;
    this.#timer = setTimeout(() => void this.#ask(theme), PAUSE_MS);
  }

  async #ask(theme: Shown) {
    this.#asking?.abort();
    const asking = new AbortController();
    this.#asking = asking;
    const preview = await previewTheme(this, theme, asking.signal);
    if (asking.signal.aborted) return;
    this._preview = preview;
    this.#paint();
  }

  /** Writes the layer into each frame of the sample, the scheme on its root, and fits the frame to the page. */
  #paint() {
    const ready = this._sample;
    if (ready.state !== 'ready') return;
    for (const frame of this.shadowRoot?.querySelectorAll<HTMLIFrameElement>('iframe') ?? []) {
      const page = frame.contentDocument;
      const layer = page?.getElementById(ready.sample.layerId);
      if (!page || !layer) continue;
      layer.textContent = this._preview?.problems.length === 0 ? this._preview.css : layer.textContent;
      page.documentElement.classList.toggle(ready.sample.darkClass, frame.dataset['scheme'] === 'dark');
      requestAnimationFrame(() => (frame.style.blockSize = `${page.documentElement.scrollHeight + 2}px`));
    }
  }

  #setTokens(change: (tokens: Shown['tokens'], base: ThemeBaseResponseModel) => Shown['tokens']) {
    const theme = this._theme;
    const base = this.#base;
    if (theme && base) this.#context?.setTokens(change(theme.tokens, base));
  }

  #toggle(section: Section) {
    const open = new Set(this._open);
    if (!open.delete(section)) open.add(section);
    this._open = open;
  }

  /** The problem a refused save or the preview gave at a path, in the user's words. */
  #problemAt(path: string): string | undefined {
    const problem = [...this._problems, ...(this._preview?.problems ?? [])].find((one) => one.path === path);
    if (!problem) return undefined;
    const said: Record<string, string> = {
      invalid_value: 'token',
      unknown_property: 'unknown',
      property_not_supported: 'darkOnly',
      limit_exceeded: 'long',
    };
    return this.#term(`problem_${said[problem.code] ?? 'other'}`);
  }

  #section(id: Section, summary: string, body: () => TemplateResult): TemplateResult {
    const open = this._open.has(id);
    return html`<section class="section">
      <h3>
        <button
          type="button"
          aria-expanded=${open ? 'true' : 'false'}
          aria-controls=${`theme-${id}`}
          @click=${() => this.#toggle(id)}
        >
          <uui-icon name=${open ? 'icon-navigation-down' : 'icon-navigation-right'} aria-hidden="true"></uui-icon>
          <span class="name">${this.#term(`section_${id}`)}</span>
          ${open ? nothing : html`<span class="summary">${summary}</span>`}
        </button>
      </h3>
      ${open ? html`<div class="body" id=${`theme-${id}`}>${body()}</div>` : nothing}
    </section>`;
  }

  #label(name: string): string {
    return OWN.has(name) ? this.#term(`colour_${camel(name)}`) : name;
  }

  #ratio(ratio: number): string {
    return new Intl.NumberFormat(this.localize.lang(), { minimumFractionDigits: 1, maximumFractionDigits: 1 }).format(
      Math.floor(ratio * 10) / 10,
    );
  }

  #check(check: ColourCheck, scheme: Scheme, name: string, id: string): TemplateResult {
    switch (check.state) {
      case 'decoration':
        return html`<p class="help" id=${id}>${this.#term('checkDecoration')}</p>`;
      case 'unchecked':
        return html`<p class="help" id=${id}>${this.#term('checkUnchecked')}</p>`;
      case 'pass':
        return html`<p class="help" id=${id}>
          ${this.#term('checkPass', this.#ratio(check.ratio), this.#term(`against_${camel(check.against)}`))}
        </p>`;
      case 'fail': {
        const said = check.min >= 4.5 ? 'checkFailText' : 'checkFailControl';
        const better = check.suggestion;
        return html`<p class="help fail" id=${id}>
          ${this.#term(said, this.#ratio(check.ratio), this.#term(`against_${camel(check.against)}`))}
          ${
            better
              ? html`<button
                  type="button"
                  class="link"
                  @click=${() => this.#setTokens((tokens, base) => withToken(tokens, base, scheme, name, better))}
                >
                  ${this.#term('useSuggestion', better)}
                </button>`
              : nothing
          }
        </p>`;
      }
    }
  }

  #colour(base: ThemeBaseResponseModel, scheme: Scheme, name: string): TemplateResult {
    const theme = this.#shown;
    const declared = base.tokens.find((token) => token.name === name);
    const fallback = scheme === 'dark' ? (declared?.darkDefault ?? declared?.default) : declared?.default;
    const own = theme.tokens[scheme][name];
    const label = this.#label(name);
    const id = `colour-${scheme}-${name.slice(2)}`;
    const check = checkOf(name, (token) => valueIn(theme.tokens, base, scheme, token));
    const problem = this.#problemAt(`$.tokens.${scheme}['${name}']`);
    const set = (value: string | undefined) =>
      this.#setTokens((tokens, from) => withToken(tokens, from, scheme, name, value));
    return html`<div class="colour">
      <label class="label" for=${id}>${label}</label>
      <input
        type="color"
        aria-label=${this.#term('colourPicker', label)}
        .value=${hexOf(valueIn(theme.tokens, base, scheme, name)) ?? '#000000'}
        @change=${(event: Event) => set((event.target as HTMLInputElement).value)}
      />
      <input
        id=${id}
        class="code"
        .value=${own ?? ''}
        placeholder=${fallback ?? ''}
        aria-invalid=${problem ? 'true' : 'false'}
        aria-describedby=${`${id}-check`}
        @change=${(event: Event) => set((event.target as HTMLInputElement).value)}
      />
      ${
        own === undefined
          ? html`<span></span>`
          : html`<uui-button
              compact
              look="secondary"
              label=${this.#term('reset', label)}
              @click=${() => set(undefined)}
            >
              <uui-icon name="icon-undo"></uui-icon>
            </uui-button>`
      }
      ${problem ? html`<p class="why">${problem}</p>` : nothing}
      ${check ? this.#check(check, scheme, name, `${id}-check`) : html`<span id=${`${id}-check`} hidden></span>`}
    </div>`;
  }

  #colours(base: ThemeBaseResponseModel): TemplateResult {
    const scheme: Scheme = base.darkScheme ? this._scheme : 'light';
    const has = (name: string) => base.tokens.some((token) => token.name === name);
    const groups = [
      ...COLOUR_GROUPS.map((group) => ({ id: group.id, tokens: group.tokens.filter(has) })),
      { id: 'more', tokens: othersOf(base).colours.map((token) => token.name) },
    ].filter((group) => group.tokens.length > 0);
    return html`${
      base.darkScheme
        ? html`<uui-button-group role="group" aria-label=${this.#term('section_colours')}>
            ${(['light', 'dark'] as const).map(
              (one) =>
                html`<uui-button
                  look=${one === scheme ? 'primary' : 'secondary'}
                  aria-pressed=${one === scheme ? 'true' : 'false'}
                  label=${this.#term(one)}
                  @click=${() => (this._scheme = one)}
                ></uui-button>`,
            )}
          </uui-button-group>`
        : nothing
    }
    ${groups.map(
      (group) =>
        html`<div class="group" role="group" aria-labelledby=${`group-${group.id}`}>
          <h4 id=${`group-${group.id}`}>${this.#term(`group_${group.id}`)}</h4>
          ${group.tokens.map((name) => this.#colour(base, scheme, name))}
        </div>`,
    )}`;
  }

  #select(
    id: string,
    label: string,
    value: string,
    options: readonly (readonly [string, string])[],
    change: (value: string) => void,
  ) {
    return html`<div class="item">
      <label class="label" for=${id}>${label}</label>
      <select id=${id} @change=${(event: Event) => change((event.target as HTMLSelectElement).value)}>
        ${options.map(([one, said]) => html`<option value=${one} ?selected=${one === value}>${said}</option>`)}
      </select>
    </div>`;
  }

  #type(base: ThemeBaseResponseModel): TemplateResult {
    const choices = choicesOf(this.#shown.tokens, base);
    const fonts = (Object.keys(FONTS) as (keyof typeof FONTS)[]).map(
      (font) => [font, this.#term(`font_${font}`)] as const,
    );
    const scales = (Object.keys(SCALES) as (keyof typeof SCALES)[]).map(
      (scale) => [scale, this.#term(`scale_${scale}`)] as const,
    );
    const sizes = SIZES.map(
      (size) =>
        [size, size === 'inherit' ? this.#term('size_site') : this.#term('sizePx', parseInt(size, 10))] as const,
    );
    return html`${
      choices.font
        ? this.#select(
            'font',
            this.#term('font'),
            choices.font,
            choices.font === 'custom' ? [...fonts, ['custom', this.#term('font_custom')]] : fonts,
            (font) =>
              this.#setTokens((tokens, from) =>
                font === 'custom' ? tokens : withChoice(tokens, from, { font: font as keyof typeof FONTS }),
              ),
          )
        : nothing
    }
    ${
      choices.size
        ? this.#select(
            'size',
            this.#term('size'),
            choices.size,
            SIZES.includes(choices.size) ? sizes : [...sizes, [choices.size, choices.size]],
            (size) => this.#setTokens((tokens, from) => withChoice(tokens, from, { size })),
          )
        : nothing
    }
    ${
      choices.scale
        ? this.#select(
            'scale',
            this.#term('scale'),
            choices.scale,
            choices.scale === 'custom' ? [...scales, ['custom', this.#term('scale_custom')]] : scales,
            (scale) =>
              this.#setTokens((tokens, from) =>
                scale === 'custom' ? tokens : withChoice(tokens, from, { scale: scale as keyof typeof SCALES }),
              ),
          )
        : nothing
    }`;
  }

  #spacing(base: ThemeBaseResponseModel): TemplateResult {
    const choices = choicesOf(this.#shown.tokens, base);
    const spacings = (Object.keys(SPACINGS) as (keyof typeof SPACINGS)[]).map(
      (one) => [one, this.#term(`spacing_${one}`)] as const,
    );
    return html`${
      choices.spacing
        ? this.#select(
            'spacing',
            this.#term('spacing'),
            choices.spacing,
            choices.spacing === 'custom' ? [...spacings, ['custom', this.#term('spacing_custom')]] : spacings,
            (spacing) =>
              this.#setTokens((tokens, from) =>
                spacing === 'custom' ? tokens : withChoice(tokens, from, { spacing: spacing as keyof typeof SPACINGS }),
              ),
          )
        : nothing
    }
    ${
      choices.radius !== undefined
        ? this.#select(
            'corners',
            this.#term('corners'),
            String(choices.radius),
            (RADII.includes(choices.radius) ? RADII : [...RADII, choices.radius].sort((a, b) => a - b)).map(
              (radius) => [String(radius), this.#term('cornersPx', radius)] as const,
            ),
            (radius) => this.#setTokens((tokens, from) => withChoice(tokens, from, { radius: Number(radius) })),
          )
        : nothing
    }`;
  }

  #buttons(base: ThemeBaseResponseModel): TemplateResult {
    const choices = choicesOf(this.#shown.tokens, base);
    const scheme: Scheme = base.darkScheme ? this._scheme : 'light';
    return html`<div class="pair">
        ${
          choices.buttons
            ? this.#select(
                'buttons',
                this.#term('buttons'),
                choices.buttons,
                [
                  ['filled', this.#term('buttons_filled')],
                  ['outlined', this.#term('buttons_outlined')],
                ],
                (buttons) =>
                  this.#setTokens((tokens, from) =>
                    withChoice(tokens, from, { buttons: buttons as 'filled' | 'outlined' }),
                  ),
              )
            : nothing
        }
        ${
          choices.ring !== undefined
            ? this.#select(
                'ring',
                this.#term('ring'),
                String(choices.ring),
                RINGS.map((ring) => [String(ring), this.#term('ringPx', ring)] as const),
                (ring) => this.#setTokens((tokens, from) => withChoice(tokens, from, { ring: Number(ring) })),
              )
            : nothing
        }
      </div>
      ${
        choices.buttons === 'filled'
          ? html`<div class="group" role="group" aria-labelledby="group-buttons">
              <h4 id="group-buttons">${this.#term('group_buttons')}</h4>
              ${BUTTON_COLOURS.map((name) => this.#colour(base, scheme, name))}
            </div>`
          : nothing
      }`;
  }

  #others(base: ThemeBaseResponseModel): TemplateResult {
    const theme = this.#shown;
    const field = (name: string, scheme: Scheme, label: string, fallback: string | null | undefined) => {
      const id = `token-${scheme}-${name.slice(2)}`;
      const problem = this.#problemAt(`$.tokens.${scheme}['${name}']`);
      return html`<div class="item">
        <label class="label code" for=${id}>${label}</label>
        <input
          id=${id}
          class="code"
          .value=${theme.tokens[scheme][name] ?? ''}
          placeholder=${fallback ?? ''}
          aria-invalid=${problem ? 'true' : 'false'}
          @change=${(event: Event) =>
            this.#setTokens((tokens, from) =>
              withToken(tokens, from, scheme, name, (event.target as HTMLInputElement).value),
            )}
        />
        ${problem ? html`<p class="why">${problem}</p>` : nothing}
      </div>`;
    };
    return html`<p class="help">${this.#term('otherHelp')}</p>
      ${othersOf(base).tokens.map((token) => field(token.name, 'light', token.name, token.default))}`;
  }

  #more(): TemplateResult {
    const theme = this.#shown;
    const removed = this._preview?.removed ?? [];
    const problem = this.#problemAt('$.customCss');
    return html`<div class="item">
        <label class="label" for="custom-css">${this.#term('customCss')}</label>
        <p class="help" id="custom-css-help">${this.#term('customCssHelp')}</p>
        <textarea
          id="custom-css"
          class="code"
          spellcheck="false"
          maxlength=${this._editor?.maxCustomCssLength ?? 65536}
          aria-describedby="custom-css-help"
          aria-invalid=${problem ? 'true' : 'false'}
          .value=${theme.customCss}
          @input=${(event: Event) => this.#context?.setCustomCss((event.target as HTMLTextAreaElement).value)}
        ></textarea>
        ${problem ? html`<p class="why">${problem}</p>` : nothing}
      </div>
      ${
        removed.length > 0
          ? html`<div class="group" role="group" aria-labelledby="removed">
              <h4 id="removed">${this.#term('removedHeading')}</h4>
              <ul class="removed">
                ${removed.map((one) => html`<li>${this.#term(`removed_${one.code}`)}: <code>${one.text}</code></li>`)}
              </ul>
            </div>`
          : nothing
      }`;
  }

  #settings(base: ThemeBaseResponseModel): TemplateResult {
    const theme = this.#shown;
    const choices = choicesOf(theme.tokens, base);
    const colours = base.tokens.filter(
      (token) => token.name.startsWith('--jfb-color-') && !BUTTON_COLOURS.includes(token.name),
    );
    const others = othersOf(base).tokens;
    const lines = theme.customCss.trim() === '' ? 0 : theme.customCss.trim().split('\n').length;
    const font = choices.font ? this.#term(`font_${choices.font}`) : '';
    const size = choices.size === 'inherit' ? this.#term('size_site') : (choices.size ?? '');
    // What a refused save said of the name, the alias and the base theme, which the header and this line show.
    const general = this._problems.flatMap((problem) => {
      if (problem.path === '$.name') return [this.#term('problem_name')];
      if (problem.path === '$.baseTheme') return [this.#term('problem_base')];
      if (problem.path !== '$.alias') return [];
      return [this.#term(problem.code === 'alias_duplicate' ? 'problem_aliasTaken' : 'problem_alias')];
    });
    return html`<uui-box class="settings">
      <p class="base">${this.#term('buildsOn', withThemeLabel(base, (key) => this.localize.term(key)).label)}</p>
      ${
        general.length > 0
          ? html`<ul class="alert" role="alert">
              ${general.map((said) => html`<li>${said}</li>`)}
            </ul>`
          : nothing
      }
      ${
        colours.length > 0
          ? this.#section(
              'colours',
              this.#term(
                'summaryColours',
                changedOf(
                  theme.tokens,
                  colours.map((token) => token.name),
                ),
              ),
              () => this.#colours(base),
            )
          : nothing
      }
      ${choices.font || choices.size || choices.scale ? this.#section('type', this.#term('summaryType', font, size), () => this.#type(base)) : nothing}
      ${
        choices.spacing || choices.radius !== undefined
          ? this.#section(
              'spacing',
              this.#term(
                'summarySpacing',
                choices.spacing ? this.#term(`spacing_${choices.spacing}`) : '',
                choices.radius ?? 0,
              ),
              () => this.#spacing(base),
            )
          : nothing
      }
      ${
        choices.buttons || choices.ring !== undefined
          ? this.#section(
              'buttons',
              this.#term(
                'summaryButtons',
                choices.buttons ? this.#term(`buttons_${choices.buttons}`) : '',
                choices.ring ?? 0,
              ),
              () => this.#buttons(base),
            )
          : nothing
      }
      ${
        others.length > 0
          ? this.#section(
              'other',
              this.#term(
                'summaryOther',
                changedOf(
                  theme.tokens,
                  others.map((token) => token.name),
                ),
              ),
              () => this.#others(base),
            )
          : nothing
      }
      ${this.#section('more', this.#term('summaryMore', lines), () => this.#more())}
    </uui-box>`;
  }

  #frames(base: ThemeBaseResponseModel): TemplateResult {
    const sample = this._sample;
    if (sample.state === 'loading') return html`<uui-box>${skeletonLines(this.#term('preview'))}</uui-box>`;
    if (sample.state === 'failed')
      return html`<uui-box><p class="alert" role="alert">${this.#term('previewFailed')}</p></uui-box>`;
    const page = sample.sample.html;
    const schemes: Scheme[] = base.darkScheme ? ['light', 'dark'] : ['light'];
    const held = (this._preview?.problems.length ?? 0) > 0;
    return html`<div class="frames">
      ${held ? html`<p class="muted" role="status">${this.#term('previewHeld')}</p>` : nothing}
      ${schemes.map(
        (scheme) =>
          html`<iframe
            sandbox="allow-same-origin"
            data-scheme=${scheme}
            title=${this.#term(scheme === 'dark' ? 'previewDark' : 'previewLight')}
            .srcdoc=${page}
            @load=${() => this.#paint()}
          ></iframe>`,
      )}
    </div>`;
  }

  override render() {
    const theme = this._theme;
    if (!theme || this._editor === undefined) return html`<uui-box>${skeletonLines('')}</uui-box>`;
    if (this._editor === null)
      return html`<uui-box><p class="alert" role="alert">${this.#term('editorFailed')}</p></uui-box>`;
    const base = this.#base;
    if (!base) return html`<uui-box><p class="alert" role="alert">${this.#term('baseMissing')}</p></uui-box>`;
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
      <div class="page" data-side=${this._side}>
        ${this.#settings(base)}
        <section class="preview" aria-label=${this.#term('preview')}>${this.#frames(base)}</section>
      </div>`;
  }
}

if (!customElements.get('jfb-theme-view')) customElements.define('jfb-theme-view', ThemeViewElement);

export { ThemeViewElement as element };

declare global {
  interface HTMLElementTagNameMap {
    'jfb-theme-view': ThemeViewElement;
  }
}
