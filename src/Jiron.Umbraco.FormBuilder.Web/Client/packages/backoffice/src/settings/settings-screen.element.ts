// The global settings screen (ROADMAP 12.4a, IA 8 and 9, ADR-0218): one element for every section, Settings > General,
// Email, Spam protection, Files and Integrations, and Privacy > Data collection. It draws the fields the section serves
// with the inspector's editors, box by box in the section's order, each box with at most seven settings before More
// options and each setting with its help below it; a field the host locks reads only, with its key, and a section the
// host locks whole says so once and has no Save. A section a site can set starts with the site switch, set to All sites:
// with a site chosen, each field shows what that site gets, a field it sets itself says so with "Use the value for all
// sites", and the fields every site shares stay under All sites. Save in the footer sends the fields changed, checked
// before they go; the server's refusal is said under the field it names, and a save made meanwhile is said with the
// values now saved. What a section says besides its fields comes with it: Email's line while Umbraco can't send mail and
// Send test email once it can, the hosts webhooks may call, Files' checks and storage, and the form block type. The
// sidebar names the screen, so it has no title of its own (ADR-0021).
import { css, html, live, nothing, type TemplateResult } from '@umbraco-cms/backoffice/external/lit';
import { UmbLitElement } from '@umbraco-cms/backoffice/lit-element';
import { umbOpenModal } from '@umbraco-cms/backoffice/modal';
import { UMB_NOTIFICATION_CONTEXT } from '@umbraco-cms/backoffice/notification';
import type { FieldSettingResponseModel, SettingsSectionResponseModel } from '../api';
import type { LocalizedText } from '../designer/definition';
import { controlStyles } from '../designer/inspector/control.styles';
import { focusableIn } from '../designer/inspector/item-focus';
import {
  boxId,
  inspectorWords,
  problemText,
  settingEditor,
  type EditorContext,
} from '../designer/inspector/setting-editor';
import { withText, type Parsed } from '../designer/inspector/setting-values';
import { readLook, type SiteLanguage } from '../designer/preview/look';
import { choiceStyles } from '../designer/settings/choice.styles';
import { loadWorkspaceWords } from '../designer/workspace-words';
import { FORM_BLOCK_MODAL } from '../overview/form-block-modal.token';
import { jfbBackofficeTokens, jfbVisuallyHidden } from '../styles/tokens';
import { confirmText } from '../ui/confirm';
import { jfbSkeleton, skeletonLines } from '../ui/skeleton';
import { readSection, saveSection, sendTestMail, type Refused } from './settings-api';
import { consentLabelOf, settingsEditors } from './settings-editors';
import { megabytesOf } from './settings-values';
import { loadSettingsWords } from './settings-words';

/** The most settings a box shows before More options (IA 8). */
export const MOST_IN_A_BOX = 7;

/** The boxes whose headline has a sentence under it, by section. */
export const LEADS: Readonly<Record<string, readonly string[]>> = {
  general: ['newForms', 'history'],
  protection: ['content'],
  integrations: ['chat'],
  privacy: ['ip', 'insights', 'members'],
};

type Values = Readonly<Record<string, unknown>>;

/**
 * The fields shown only with another's value (ADR-0218): the captcha is chosen in the level's list, the puzzle's
 * strength matters with proof of work, and the Cookiebot category with Cookiebot.
 */
const SHOWN_WITH: Readonly<Record<string, Readonly<Record<string, (values: Values) => boolean>>>> = {
  protection: {
    captchaProvider: () => false,
    proofBits: (values) => values['level'] === 'proofOfWork',
    cookiebotCategory: (values) => values['captchaConsent'] === 'cookiebot',
  },
};

/** What the screen says once, at its top, about the last thing that happened. */
type Alert = '' | 'conflict' | 'lockedNow' | 'siteGone' | 'invalid';

// The value at a dotted path.
function at(values: Values, path: string): unknown {
  let current: unknown = values;
  for (const step of path.split('.')) {
    if (!current || typeof current !== 'object') return undefined;
    current = (current as Values)[step];
  }
  return current;
}

// The values with one at a dotted path, the objects on the way copied.
function withAt(values: Values, steps: readonly string[], value: unknown): Values {
  const [step, ...rest] = steps;
  if (step === undefined) return values;
  const inner = values[step];
  return {
    ...values,
    [step]:
      rest.length === 0 ? value : withAt(inner && typeof inner === 'object' ? (inner as Values) : {}, rest, value),
  };
}

const copy = (value: unknown): Values =>
  value && typeof value === 'object' ? (JSON.parse(JSON.stringify(value)) as Values) : {};

export class SettingsScreenElement extends UmbLitElement {
  static override properties = {
    _model: { state: true },
    _failed: { state: true },
    _values: { state: true },
    _changed: { state: true },
    _overrides: { state: true },
    _problems: { state: true },
    _saving: { state: true },
    _testing: { state: true },
    _alert: { state: true },
    _site: { state: true },
    _languages: { state: true },
    _culture: { state: true },
    _more: { state: true },
  };

  static override styles = [
    jfbBackofficeTokens,
    jfbVisuallyHidden,
    jfbSkeleton,
    controlStyles,
    choiceStyles,
    css`
      :host {
        display: block;
        block-size: 100%;
      }

      .page {
        display: grid;
        gap: var(--uui-size-space-5);
        max-inline-size: 760px;
        margin-inline: auto;
      }

      .lead,
      .note,
      .alert,
      .own {
        margin: 0;
      }

      .lead,
      .note {
        color: var(--jfb-bo-color-text-muted);
      }

      .alert {
        color: var(--jfb-bo-color-danger-text);
      }

      .scope {
        display: flex;
        flex-wrap: wrap;
        align-items: end;
        gap: var(--uui-size-space-5);
      }

      .scope .item {
        min-inline-size: 14rem;
      }

      .fields {
        display: grid;
        gap: var(--uui-size-space-5);
      }

      .lead + .fields {
        margin-block-start: var(--uui-size-space-5);
      }

      /* One width for every box of one line and every list, so the boxes line up; a list of lines takes the box. */
      .fields input:not([type='checkbox'], [type='radio']),
      .fields select {
        max-inline-size: 32rem;
      }

      .fields .with-unit input {
        inline-size: 9rem;
      }

      .own {
        display: flex;
        flex-wrap: wrap;
        align-items: center;
        gap: var(--uui-size-space-3);
        margin-block-start: calc(-1 * var(--uui-size-space-3));
        color: var(--jfb-bo-color-text-muted);
        font-size: var(--uui-type-small-size);
      }

      .more-toggle {
        display: inline-flex;
        align-items: center;
        gap: var(--uui-size-space-2);
        justify-self: start;
        min-block-size: 36px;
        padding: 0 var(--uui-size-space-2);
        border: 0;
        border-radius: var(--uui-border-radius);
        background: none;
        color: var(--uui-color-interactive);
        font: inherit;
        font-weight: 600;
        cursor: pointer;
      }

      @media (hover: hover) {
        .more-toggle:hover {
          color: var(--uui-color-interactive-emphasis);
        }
      }

      .more-toggle:focus-visible {
        outline: 2px solid var(--jfb-bo-color-focus);
        outline-offset: 2px;
      }

      .facts {
        display: grid;
        gap: var(--uui-size-space-4);
        margin: 0;
      }

      .lead + .facts,
      .fields + .facts {
        margin-block-start: var(--uui-size-space-5);
      }

      .facts dt {
        font-weight: 600;
      }

      .facts dd {
        margin: var(--uui-size-space-1) 0 0;
        color: var(--jfb-bo-color-text-muted);
      }

      .block {
        display: grid;
        gap: var(--uui-size-space-3);
        justify-items: start;
      }

      .block p {
        margin: 0;
      }

      .failed {
        display: flex;
        flex-wrap: wrap;
        align-items: center;
        gap: var(--uui-size-space-4);
      }
    `,
  ];

  /** The section the screen shows, as the management API names it. */
  section = '';

  /** The boxes of the section another screen shows, as Privacy's retention days have Privacy > Retention (ADR-0222). */
  omit: readonly string[] = [];

  declare private _model: SettingsSectionResponseModel | undefined;
  /** Why the section couldn't be read: no answer, which Try again may fix, or a missing permission, which it won't. */
  declare private _failed: false | 'failed' | 'forbidden';
  /** The values the boxes hold, changes not yet saved among them. */
  declare private _values: Values;
  /** The fields changed since the section was read, by their names. */
  declare private _changed: ReadonlySet<string>;
  /** With a site chosen, the fields it sets of its own. */
  declare private _overrides: ReadonlySet<string>;
  /** Why a box's value can't be kept, or why the server refused it, by the box's id. */
  declare private _problems: ReadonlyMap<string, string>;
  declare private _saving: boolean;
  declare private _testing: boolean;
  declare private _alert: Alert;
  /** The site chosen; null for all sites. */
  declare private _site: string | null;
  /** The site's languages, for a section with texts in each. */
  declare private _languages: readonly SiteLanguage[];
  /** The language the texts are edited in. */
  declare private _culture: string;
  /** The boxes whose More options are open. */
  declare private _more: ReadonlySet<string>;

  constructor() {
    super();
    this._model = undefined;
    this._failed = false;
    this._values = {};
    this._changed = new Set();
    this._overrides = new Set();
    this._problems = new Map();
    this._saving = false;
    this._testing = false;
    this._alert = '';
    this._site = null;
    this._languages = [];
    this._culture = '';
    this._more = new Set();
  }

  override connectedCallback() {
    super.connectedCallback();
    void this.#read();
  }

  #words = (key: string, ...args: unknown[]) => this.localize.term(`jfbGlobalSettings_${key}`, ...args);

  #say = (text: string | null | undefined) => (text ? this.localize.string(text) : '');

  // ---- Reading ------------------------------------------------------------------------------------------------------

  async #read() {
    this._failed = false;
    const site = this._site;
    const lang = this.localize.lang();
    const [words, model] = await Promise.all([
      Promise.all([loadSettingsWords(lang), loadWorkspaceWords(lang)]).then(
        () => true,
        () => false,
      ),
      readSection(this, this.section, site),
    ]);
    // Another site chosen meanwhile wins over the answer for the one before it.
    if (site !== this._site) return;
    if (!words || model === undefined || model === 'forbidden') {
      this._failed = model === 'forbidden' ? 'forbidden' : 'failed';
      return;
    }
    if (this._languages.length === 0 && model.fields.some((field) => this.#holdsTexts(field))) {
      const look = await readLook(this);
      if (look) {
        this._languages = look.languages;
        this._culture = look.languages.find((language) => language.isDefault)?.culture ?? look.culture;
      }
    }
    this.#show(model);
  }

  #show(model: SettingsSectionResponseModel) {
    this._model = model;
    this._values = copy(model.values);
    this._changed = new Set();
    this._overrides = new Set(model.overrides);
    this._problems = new Map();
  }

  #holdsTexts = (field: FieldSettingResponseModel): boolean =>
    field.kind === 'localizedText' ||
    field.kind === 'localizedHtml' ||
    field.editor === 'newFormConsent' ||
    field.settings.some(this.#holdsTexts);

  #defaultCulture(): string {
    return this._languages.find((language) => language.isDefault)?.culture ?? this._culture;
  }

  /** Chooses a site, or all sites with null; changes not saved are given up only once the user says so. */
  async #choose(site: string | null) {
    if (site === this._site) return;
    if (this._changed.size > 0) {
      const given = await confirmText(this, {
        headline: this.#words('discardHeadline'),
        content: this.#words('discardText'),
        confirmLabel: this.#words('discardConfirm'),
        color: 'danger',
      }).then(
        () => true,
        () => false,
      );
      if (!given) {
        this.requestUpdate();
        return;
      }
    }
    this._site = site;
    this._alert = '';
    this._model = undefined;
    await this.#read();
  }

  // ---- Changes ------------------------------------------------------------------------------------------------------

  #problem(id: string, text: string | null) {
    if ((this._problems.get(id) ?? null) === text) return;
    const next = new Map(this._problems);
    if (text === null) next.delete(id);
    else next.set(id, text);
    this._problems = next;
  }

  /** A value that can be kept, at a dotted path: the field it is in counts as changed, and as the site's own. */
  #put(path: string, value: unknown) {
    const top = path.split('.')[0] ?? path;
    this._values = withAt(this._values, path.split('.'), value);
    this._changed = new Set([...this._changed, top]);
    if (this._site) this._overrides = new Set([...this._overrides, top]);
  }

  /** What an editor hands on: a value, which a text keeps in the language edited, or why it can't be kept. */
  #set(path: string, setting: FieldSettingResponseModel, parsed: Parsed, done: boolean) {
    const id = boxId(path);
    if (!parsed.ok) {
      if (done) this.#problem(id, problemText(inspectorWords(this.localize), parsed.problem));
      return;
    }
    this.#problem(id, null);
    let value = parsed.value;
    if (setting.kind === 'localizedText' || setting.kind === 'localizedHtml')
      value =
        withText(
          at(this._values, path) as LocalizedText | undefined,
          this._culture,
          typeof value === 'string' ? value : '',
          this._culture === this.#defaultCulture(),
        ) ?? null;
    // An emptied box leaves the setting to its default.
    else if (value === undefined) value = setting.default ?? null;
    this.#put(path, value);
  }

  /** A site's field back on the value of all sites. */
  #useAllSites(field: FieldSettingResponseModel) {
    const all = (this._model?.globalValues ?? {}) as Values;
    this._values = { ...this._values, [field.name]: all[field.name] ?? null };
    const overrides = new Set(this._overrides);
    overrides.delete(field.name);
    this._overrides = overrides;
    this._changed = new Set([...this._changed, field.name]);
    for (const id of this._problems.keys())
      if (id === boxId(field.name) || id.startsWith(`${boxId(field.name)}-`)) this.#problem(id, null);
  }

  // ---- Saving -------------------------------------------------------------------------------------------------------

  /** The setting at a dotted path, nested ones too. */
  #settingAt(path: string): FieldSettingResponseModel | undefined {
    let fields = this._model?.fields ?? [];
    let found: FieldSettingResponseModel | undefined;
    for (const step of path.split('.')) {
      found = fields.find((field) => field.name === step);
      if (!found) return undefined;
      fields = found.settings;
    }
    return found;
  }

  /** The server's refusal of a field in the screen's words where it has them, otherwise in the server's. */
  #refusedWords(path: string, refused: Refused): string {
    const setting = this.#settingAt(path);
    if (refused.code === 'range' && setting?.editor === 'fileSize')
      return this.#words('sizeInvalid', megabytesOf(this._model?.facts.files?.hostMaxFileBytes ?? 0));
    if (refused.code === 'range' && setting?.kind === 'wholeNumber' && setting.rules.minimum && setting.rules.maximum)
      return this.#words('range', setting.rules.minimum, setting.rules.maximum);
    return refused.message;
  }

  /** What can be checked before a save: a new form's consent field needs a label. */
  #checked(): boolean {
    const consent = at(this._values, 'newForms.consent');
    if (this.section === 'general' && consent && !consentLabelOf(consent))
      this.#problem(boxId('newForms.consent.label'), this.#words('consentNeedsLabel'));
    return this._problems.size === 0;
  }

  async #focusProblem() {
    await this.updateComplete;
    const id = [...this._problems.keys()][0];
    const box = id ? this.shadowRoot?.getElementById(id) : null;
    const focus = box ? (box.localName === 'fieldset' ? focusableIn(box) : box) : null;
    focus?.focus();
    focus?.scrollIntoView?.({ block: 'center' });
  }

  /** Saves the fields changed in the scope shown; true once they are saved. */
  async #save(): Promise<boolean> {
    const model = this._model;
    if (!model || this._saving) return false;
    if (!this.#checked()) {
      this._alert = 'invalid';
      await this.#focusProblem();
      return false;
    }
    this._alert = '';
    this._saving = true;
    const site = this._site;
    const values = Object.fromEntries(
      [...(site ? this._overrides : this._changed)].map((name) => [name, this._values[name] ?? null]),
    );
    const answer = await saveSection(this, this.section, {
      version: model.version,
      site,
      values,
      ...(site ? { overrides: [...this._overrides] } : {}),
    });
    this._saving = false;
    const notifications = await this.getContext(UMB_NOTIFICATION_CONTEXT);
    switch (answer.kind) {
      case 'saved':
        this.#show(answer.section);
        notifications?.peek('positive', { data: { message: this.#words('saved') } });
        return true;
      case 'invalid': {
        const problems = new Map(this._problems);
        for (const [path, refused] of answer.fields) problems.set(boxId(path), this.#refusedWords(path, refused));
        this._problems = problems;
        this._alert = 'invalid';
        await this.#focusProblem();
        return false;
      }
      case 'conflict':
        // The values saved meanwhile replace the ones written, which are said to be made again.
        this._alert = 'conflict';
        await this.#read();
        return false;
      case 'locked':
        this._alert = 'lockedNow';
        await this.#read();
        return false;
      case 'siteUnknown':
        this._alert = 'siteGone';
        this._site = null;
        await this.#read();
        return false;
      default:
        notifications?.peek('danger', { data: { message: this.#words('saveFailed') } });
        return false;
    }
  }

  /** Sends the scope's test mail to the user, saving first what the screen has changed. */
  async #test() {
    if (this._testing || this._saving) return;
    if (this._changed.size > 0 && !(await this.#save())) return;
    this._testing = true;
    const answer = await sendTestMail(this, this._site);
    this._testing = false;
    const notifications = await this.getContext(UMB_NOTIFICATION_CONTEXT);
    if (answer.kind === 'sent')
      notifications?.peek('positive', { data: { message: this.#words('testSent', answer.to) } });
    else
      notifications?.peek('danger', {
        data: { message: answer.limited ? this.localize.term('jfbLibrary_testsLimited') : this.#words('testFailed') },
      });
  }

  async #createBlock() {
    await umbOpenModal(this, FORM_BLOCK_MODAL).catch(() => undefined);
    await this.#read();
  }

  // ---- Drawing ------------------------------------------------------------------------------------------------------

  #context(): EditorContext {
    const context: EditorContext = {
      say: this.#say,
      term: inspectorWords(this.localize),
      culture: this._culture,
      defaultCulture: this.#defaultCulture(),
      problems: this._problems,
      set: (path, setting, parsed, done) => this.#set(path, setting, parsed, done),
    };
    const model = this._model;
    if (!model) return context;
    return {
      ...context,
      editors: settingsEditors({
        section: model,
        values: this._values,
        site: this._site !== null,
        context,
        words: this.#words,
        put: (path, value) => this.#put(path, value),
        problem: (id, text) => this.#problem(id, text),
      }),
    };
  }

  #shown(field: FieldSettingResponseModel): boolean {
    const model = this._model;
    if (!model) return false;
    if (this._site && !model.siteFields.includes(field.name)) return false;
    return SHOWN_WITH[this.section]?.[field.name]?.(this._values) ?? true;
  }

  /** A field with what a site's own value says under it; a locked field shows the host's value. */
  #field(context: EditorContext, field: FieldSettingResponseModel, path: string): TemplateResult {
    const value =
      field.lock?.value !== undefined && field.lock?.value !== null ? field.lock.value : at(this._values, path);
    const own = this._site !== null && path === field.name && this._overrides.has(field.name) && !field.lock;
    return html`${settingEditor(context, field, value, path)}
    ${
      own
        ? html`<p class="own">
            <span>${this.#words('siteOwn')}</span>
            <uui-button
              look="secondary"
              compact
              label=${this.#words('siteUseAll')}
              @click=${() => this.#useAllSites(field)}
            ></uui-button>
          </p>`
        : nothing
    }`;
  }

  #box(
    context: EditorContext,
    box: string,
    fields: readonly FieldSettingResponseModel[],
  ): TemplateResult | typeof nothing {
    const shown = fields.filter((field) => this.#shown(field));
    const extra = this.#addOn(box);
    if (shown.length === 0 && extra === nothing) return nothing;
    // A box of one object, such as History, holds the object's settings: its name is the box's headline.
    const only = shown.length === 1 && shown[0]?.kind === 'nested' ? shown[0] : undefined;
    const items = only
      ? only.settings.map((inner) => ({ field: inner, path: `${only.name}.${inner.name}` }))
      : shown.map((field) => ({ field, path: field.name }));
    const first = items.slice(0, MOST_IN_A_BOX);
    const rest = items.slice(MOST_IN_A_BOX);
    const open = this._more.has(box);
    const lead = LEADS[this.section]?.includes(box);
    return html`<uui-box headline-variant="h2" headline=${this.#words(`${this.section}Box_${box}`)}>
      ${lead ? html`<p class="lead">${this.#words(`${this.section}Box_${box}Lead`)}</p>` : nothing}
      ${
        items.length > 0
          ? html`<div class="fields">
              ${first.map((item) => this.#field(context, item.field, item.path))}
              ${
                rest.length > 0
                  ? html`<button
                      type="button"
                      class="more-toggle"
                      aria-expanded=${open ? 'true' : 'false'}
                      aria-controls=${open ? `more-${box}` : nothing}
                      @click=${() => {
                        const next = new Set(this._more);
                        if (open) next.delete(box);
                        else next.add(box);
                        this._more = next;
                      }}
                    >
                      <uui-icon
                        name=${open ? 'icon-navigation-down' : 'icon-navigation-right'}
                        aria-hidden="true"
                      ></uui-icon>
                      <span>${open ? this.#words('fewerOptions') : this.#words('moreOptions', rest.length)}</span>
                    </button>`
                  : nothing
              }
              ${
                rest.length > 0 && open
                  ? html`<div class="fields" id=${`more-${box}`}>
                      ${rest.map((item) => this.#field(context, item.field, item.path))}
                    </div>`
                  : nothing
              }
            </div>`
          : nothing
      }
      ${extra}
    </uui-box>`;
  }

  /** What a box says besides its fields: Integrations' hosts that webhooks may call. */
  #addOn(box: string): TemplateResult | typeof nothing {
    const integrations = this._model?.facts.integrations;
    if (this.section !== 'integrations' || box !== 'webhooks' || !integrations || this._site) return nothing;
    const hosts = integrations.allowedHosts;
    return html`<dl class="facts">
      <div>
        <dt>${this.#words('hosts')}</dt>
        <dd>${hosts.length === 0 ? this.#words('hostsNone') : hosts.join(', ')}</dd>
        <dd>
          ${this.#words('hostsKey', 'JironFormBuilder:Webhooks:AllowedHosts')}
          ${integrations.allowHttp ? this.#words('hostsHttp') : nothing}
        </dd>
      </div>
    </dl>`;
  }

  /** The boxes a section has besides its fields' own: Files' checks and storage, General's form block type. */
  #closing(): TemplateResult | typeof nothing {
    const model = this._model;
    if (!model || this._site) return nothing;
    const files = model.facts.files;
    if (this.section === 'files' && files)
      return html`<uui-box headline-variant="h2" headline=${this.#words('filesChecks')}>
        <dl class="facts">
          <div>
            <dt>${this.#words('typeCheck')}</dt>
            <dd>${this.#words('typeCheckLine')}</dd>
          </div>
          <div>
            <dt>${this.#words('scanner')}</dt>
            <dd>${files.scanners === 0 ? this.#words('scannerNone') : this.#words('scannerSome', files.scanners)}</dd>
          </div>
          <div>
            <dt>${this.#words('storedIn')}</dt>
            <dd>${files.folder ? this.#words('storedFolder', files.rootKey) : this.#words('storedCustom')}</dd>
          </div>
        </dl>
      </uui-box>`;
    const block = model.facts.general?.formBlock;
    if (this.section === 'general' && block)
      return html`<uui-box headline-variant="h2" headline=${this.#words('formBlock')}>
        <div class="block">
          ${
            block.exists
              ? html`<p>
                    ${this.#words('formBlockThere', block.createdAt ? this.localize.date(block.createdAt, { dateStyle: 'medium' }) : '')}
                  </p>
                  <p class="note">${this.#words('formBlockHow')}</p>`
              : html`<p>${this.#words('formBlockMissing')}</p>
                  ${
                    block.canCreate
                      ? html`<uui-button
                          look="outline"
                          label=${this.#words('formBlockCreate')}
                          @click=${() => void this.#createBlock()}
                        ></uui-button>`
                      : html`<p class="note">${this.#words('formBlockAsk')}</p>`
                  }`
          }
        </div>
      </uui-box>`;
    return nothing;
  }

  /** The site switch, and the language switch for texts in each of the site's languages. */
  #scope(model: SettingsSectionResponseModel): TemplateResult | typeof nothing {
    const sites = model.sites.length > 0 && model.siteFields.length > 0;
    const languages =
      this._languages.length > 1 && model.fields.some((field) => this.#shown(field) && this.#holdsTexts(field));
    if (!sites && !languages) return nothing;
    return html`<div class="scope">
      ${
        sites
          ? html`<div class="item">
              <label class="label" for="site">${this.#words('site')}</label>
              <select
                id="site"
                .value=${live(this._site ?? '')}
                @change=${(event: Event) => void this.#choose((event.target as HTMLSelectElement).value || null)}
              >
                <option value="" ?selected=${this._site === null}>${this.#words('allSites')}</option>
                ${model.sites.map(
                  (site) => html`<option value=${site.key} ?selected=${site.key === this._site}>${site.name}</option>`,
                )}
              </select>
            </div>`
          : nothing
      }
      ${
        languages
          ? html`<div class="item">
              <label class="label" for="language">${this.#words('language')}</label>
              <select
                id="language"
                .value=${live(this._culture)}
                @change=${(event: Event) => {
                  this._culture = (event.target as HTMLSelectElement).value;
                  this._problems = new Map();
                }}
              >
                ${this._languages.map(
                  (language) =>
                    html`<option value=${language.culture} ?selected=${language.culture === this._culture}>
                      ${language.name}
                    </option>`,
                )}
              </select>
            </div>`
          : nothing
      }
    </div>`;
  }

  /** The lines at the top: what last happened, a section the host locks, Email while it can't send, a site's scope. */
  #notes(model: SettingsSectionResponseModel): TemplateResult {
    const locked = model.locks.find((lock) => lock.field === '*');
    const email = model.facts.email;
    const shared = this._site !== null && model.fields.some((field) => !model.siteFields.includes(field.name));
    const alert =
      this._alert === 'conflict'
        ? this.#words('conflict')
        : this._alert === 'lockedNow'
          ? this.#words('lockedNow')
          : this._alert === 'siteGone'
            ? this.#words('siteGone')
            : this._alert === 'invalid'
              ? this.#words('invalid')
              : '';
    return html`${alert ? html`<p class="alert" role="alert">${alert}</p>` : nothing}
    ${locked ? html`<p class="note" role="note">${this.#words('locked', locked.configurationKey)}</p>` : nothing}
    ${
      email && !email.canSend
        ? html`<p class="note" role="note">
            ${this.#words('cannotSend')} ${this.#words('cannotSendKey', email.sendingKey)}
          </p>`
        : nothing
    }
    ${shared ? html`<p class="note">${this.#words('siteShared')}</p>` : nothing}`;
  }

  #body(): TemplateResult {
    if (this._failed === 'forbidden')
      return html`<uui-box>
        <p class="lead" role="alert">
          ${this.localize.term('jfbLibrary_missingPermission', this.localize.term('jfbLibrary_permission_manageSettings'))}
        </p>
      </uui-box>`;
    if (this._failed)
      return html`<uui-box>
        <div class="failed" role="alert">
          <span>${this.#words('failed')}</span>
          <uui-button look="outline" label=${this.#words('retry')} @click=${() => void this.#read()}></uui-button>
        </div>
      </uui-box>`;
    const model = this._model;
    if (!model) return html`<uui-box>${skeletonLines(this.#words('loading'), [64, 48, 56])}</uui-box>`;
    const context = this.#context();
    const boxes = [...new Set(model.fields.map((field) => field.group))].filter((box) => !this.omit.includes(box));
    return html`${this.#scope(model)} ${this.#notes(model)}
    ${boxes.map((box) =>
      this.#box(
        context,
        box,
        model.fields.filter((field) => field.group === box),
      ),
    )}
    ${this.#closing()}`;
  }

  override render() {
    const model = this._model;
    const ready = model !== undefined && !this._failed;
    const savable = ready && !model.locks.some((lock) => lock.field === '*');
    const testable = ready && this.section === 'email' && (model.facts.email?.canSend ?? false);
    return html`<umb-body-layout>
      <div class="page">${this.#body()}</div>
      ${
        testable
          ? html`<uui-button
              slot="actions"
              look="secondary"
              label=${this.#words('sendTest')}
              .state=${this._testing ? 'waiting' : undefined}
              @click=${() => void this.#test()}
            ></uui-button>`
          : nothing
      }
      ${
        savable
          ? html`<uui-button
              slot="actions"
              look="primary"
              color="positive"
              label=${this.#words('save')}
              .state=${this._saving ? 'waiting' : undefined}
              @click=${() => void this.#save()}
            ></uui-button>`
          : nothing
      }
    </umb-body-layout>`;
  }
}

/** A section's screen: the settings screen for that section without the boxes it omits, defined once under its own tag. */
export function settingsScreen(
  tag: string,
  section: string,
  omit: readonly string[] = [],
): typeof SettingsScreenElement {
  const known = customElements.get(tag);
  if (known) return known as typeof SettingsScreenElement;
  class SectionScreen extends SettingsScreenElement {
    constructor() {
      super();
      this.section = section;
      this.omit = omit;
    }
  }
  customElements.define(tag, SectionScreen);
  return SectionScreen;
}
