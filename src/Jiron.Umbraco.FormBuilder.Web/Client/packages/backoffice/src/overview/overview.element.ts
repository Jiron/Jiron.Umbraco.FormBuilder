// The Forms section's start page (docs/ux/information-architecture.md, section 2): the setup checklist until it is complete, the
// site's health, and the newest entries and the forms at a glance for a user who may view them. It is for reading, so it has no primary action and no
// title: the top bar and the sidebar header already name the section. Each block loads and fails on its own, and says so in
// place. While the migrations are pending the API refuses everything, so the page says that and why, and nothing else.
import { css, html, nothing, type PropertyValues } from '@umbraco-cms/backoffice/external/lit';
import { UmbLitElement } from '@umbraco-cms/backoffice/lit-element';
import { umbOpenModal } from '@umbraco-cms/backoffice/modal';
import { tryExecute, UmbApiError } from '@umbraco-cms/backoffice/resources';
import {
  recentEntryList,
  setupChecklist,
  setupHealth,
  type ChecklistResponseModel,
  type HealthFindingResponseModel,
  type RecentEntryResponseModel,
} from '../api';
import { ENTRY_ENTITY_TYPE, FORM_ENTITY_TYPE, screenPath } from '../section/entity-types';
import { myPermissions } from '../section/my-permissions';
import { jfbBackofficeTokens, jfbVisuallyHidden } from '../styles/tokens';
import { doneCount, findingsToShow, receivedText, stepViews, type StepView } from './checklist';
import { FORM_BLOCK_MODAL } from './form-block-modal.token';
import { createFromTemplate } from '../library/actions/new-form';

/** A block that loads by itself: on its way, there, or failed. */
type Loaded<T> = { state: 'loading' } | { state: 'failed' } | { state: 'ready'; value: T };

/** The screen each finding's link opens, and the permission that opens it. */
const FINDING_LINKS: Record<string, { entityType: string; needs: string }> = {
  settingsEmail: { entityType: 'jfb-settings-email', needs: 'manageSettings' },
  settingsGeneral: { entityType: 'jfb-settings-general', needs: 'manageSettings' },
  deliveries: { entityType: 'jfb-deliveries', needs: 'manageSettings' },
  privacyDataCollection: { entityType: 'jfb-privacy-collection', needs: 'manageSettings' },
  privacyRetention: { entityType: 'jfb-privacy-retention', needs: 'manageSettings' },
  settingsProtection: { entityType: 'jfb-settings-spam', needs: 'manageSettings' },
};

/** The API's answer while the migrations are pending; any other 503 (a gateway's) is a failure like the rest. */
const isPaused = (error: unknown): boolean =>
  Boolean(error) &&
  UmbApiError.isUmbApiError(error) &&
  error.status === 503 &&
  (error.problemDetails as { code?: string } | null | undefined)?.code === 'migrations_pending';

export class OverviewElement extends UmbLitElement {
  static override properties = {
    _paused: { state: true },
    _checklist: { state: true },
    _health: { state: true },
    _recent: { state: true },
    _global: { state: true },
    _viewsEntries: { state: true },
    _checklistAsked: { state: true },
  };

  static override styles = [
    jfbBackofficeTokens,
    jfbVisuallyHidden,
    css`
      :host {
        display: flex;
        flex-direction: column;
        gap: var(--uui-size-layout-1);
        padding: var(--uui-size-layout-1);
      }

      .count {
        color: var(--jfb-bo-color-text-muted);
      }

      ol:focus {
        outline: none;
      }

      ol,
      ul {
        display: flex;
        flex-direction: column;
        gap: var(--uui-size-space-4);
        margin: 0;
        padding: 0;
        list-style: none;
      }

      .step {
        display: flex;
        flex-wrap: wrap;
        align-items: center;
        gap: var(--uui-size-space-2) var(--uui-size-space-4);
      }

      .mark {
        flex: none;
        box-sizing: border-box;
        width: 18px;
        height: 18px;
      }

      /* Dashed: a step still to do, never a choice to make. */
      .mark--open {
        border: 1.5px dashed var(--jfb-bo-color-control-border);
        border-radius: var(--jfb-bo-radius-round);
      }

      /* The positive text's token: UUI's positive is 2.5:1 on the dark theme's surface, and the mark alone says done. */
      .mark--done {
        color: var(--jfb-bo-color-positive-text);
      }

      .done {
        color: var(--jfb-bo-color-text-muted);
      }

      /* The text keeps its width: a control that doesn't fit beside it takes a row of its own. */
      .step__text {
        flex: 1 1 280px;
        min-width: 0;
      }

      .step__help {
        margin: var(--uui-size-space-1) 0 0;
        color: var(--jfb-bo-color-text-muted);
      }

      a,
      .link {
        padding: 0;
        border: 0;
        background: none;
        color: var(--uui-color-interactive);
        font: inherit;
        text-decoration: underline;
        cursor: pointer;
      }

      a:hover,
      .link:hover {
        color: var(--uui-color-interactive-emphasis);
      }

      .step__name {
        font-weight: 700;
      }

      .title {
        margin: 0;
        font-size: var(--jfb-bo-type-title);
        font-weight: 700;
        line-height: 1.5;
      }

      .who {
        color: var(--jfb-bo-color-text-muted);
      }

      .health {
        display: flex;
        align-items: flex-start;
        gap: var(--uui-size-space-4);
      }

      .health__body {
        flex: 1;
        min-width: 0;
      }

      .finding {
        display: flex;
        gap: var(--uui-size-space-3);
      }

      .finding uui-icon,
      .passing uui-icon {
        flex: none;
        margin-top: 2px;
      }

      .passing {
        display: flex;
        gap: var(--uui-size-space-3);
      }

      .passing uui-icon {
        color: var(--jfb-bo-color-positive-text);
      }

      .skeleton {
        display: block;
        height: 12px;
        margin: var(--uui-size-space-3) 0;
        border-radius: var(--uui-border-radius);
        background: var(--uui-color-surface-alt);
      }

      .failed {
        display: flex;
        flex-wrap: wrap;
        align-items: center;
        gap: var(--uui-size-space-4);
      }

      table {
        width: 100%;
        border-collapse: collapse;
      }

      td {
        padding: var(--uui-size-space-4) var(--uui-size-space-5);
        border-top: 1px solid var(--uui-color-divider);
        vertical-align: top;
      }

      td a {
        color: var(--uui-color-text);
        text-decoration: none;
      }

      td a:hover,
      td a:focus-visible {
        color: var(--uui-color-interactive-emphasis);
        text-decoration: underline;
      }

      .box--flush {
        --uui-box-default-padding: 0;
      }

      tr:first-child td {
        border-top: 0;
      }

      .entry--unseen a {
        font-weight: 700;
      }

      .dot {
        display: inline-block;
        width: 8px;
        height: 8px;
        margin-inline-end: var(--uui-size-space-2);
        border-radius: var(--jfb-bo-radius-round);
        background: var(--uui-color-interactive);
      }

      .muted {
        color: var(--jfb-bo-color-text-muted);
      }

      .end {
        text-align: end;
        white-space: nowrap;
      }
    `,
  ];

  declare private _paused: boolean;
  declare private _checklist: Loaded<ChecklistResponseModel>;
  declare private _health: Loaded<HealthFindingResponseModel[]>;
  declare private _recent: Loaded<RecentEntryResponseModel[]>;
  /** What the user holds globally; undefined until the answer has come, or when it didn't. */
  declare private _global: readonly string[] | undefined;
  declare private _viewsEntries: boolean;
  /** The user asked to see the checklist again, once setup was complete. */
  declare private _checklistAsked: boolean;

  constructor() {
    super();
    this._paused = false;
    this._checklist = { state: 'loading' };
    this._health = { state: 'loading' };
    this._recent = { state: 'loading' };
    this._global = undefined;
    this._viewsEntries = false;
    this._checklistAsked = false;
  }

  protected override firstUpdated(changed: PropertyValues) {
    super.firstUpdated(changed);
    void this.#loadChecklist();
    void this.#loadHealth();
    void this.#loadPermissions();
  }

  async #loadPermissions() {
    const permissions = await myPermissions(this);
    this._global = permissions?.global;
    this._viewsEntries = permissions?.anywhere.includes('viewEntries') ?? false;
    if (this._viewsEntries) {
      void this.#loadRecent();
      // Forms at a glance loads with its own words, not with the backoffice's start (ADR-0211).
      void import('../insights/insight-glance.element');
    }
  }

  async #loadChecklist() {
    this._checklist = { state: 'loading' };
    const { data, error } = await tryExecute(this, setupChecklist(), { disableNotifications: true });
    if (isPaused(error)) this._paused = true;
    this._checklist = data ? { state: 'ready', value: data } : { state: 'failed' };
  }

  async #loadHealth() {
    this._health = { state: 'loading' };
    const { data, error } = await tryExecute(this, setupHealth(), { disableNotifications: true });
    if (isPaused(error)) this._paused = true;
    this._health = data ? { state: 'ready', value: data.findings } : { state: 'failed' };
  }

  async #loadRecent() {
    this._recent = { state: 'loading' };
    const { data, error } = await tryExecute(this, recentEntryList(), { disableNotifications: true });
    if (isPaused(error)) this._paused = true;
    this._recent = data ? { state: 'ready', value: data.items } : { state: 'failed' };
  }

  async #openFormBlock() {
    // Closed with Escape it rejects, also after it created the types, so the checklist is read again either way.
    await umbOpenModal(this, FORM_BLOCK_MODAL).catch(() => undefined);
    void this.#loadChecklist();
  }

  /** The button that asked goes away with the request, so the focus moves to what came. */
  async #showChecklist() {
    this._checklistAsked = true;
    await this.updateComplete;
    this.renderRoot.querySelector<HTMLElement>('ol')?.focus();
  }

  get #checklistShown(): boolean {
    return (
      this._checklist.state === 'loading' ||
      this._checklist.state === 'failed' ||
      !this._checklist.value.complete ||
      this._checklistAsked
    );
  }

  /** The screen a step opens; the form's step opens the template gallery and the form block's a dialog instead (ADR-0166). */
  #stepPath(step: string, checklist: ChecklistResponseModel): string | undefined {
    switch (step) {
      case 'email':
        return screenPath('jfb-settings-email');
      case 'testEntry':
        return checklist.previewForm ? screenPath(FORM_ENTITY_TYPE, checklist.previewForm) : undefined;
      case 'access':
        return screenPath('jfb-settings-access');
      default:
        return undefined;
    }
  }

  /** What a step without a screen does: the form's opens the gallery, whose form then opens; the form block's its dialog. */
  #stepAction(step: string) {
    if (step === 'form')
      void createFromTemplate(this, null).then(
        () => this.#loadChecklist(),
        () => undefined,
      );
    else void this.#openFormBlock();
  }

  #renderStepName(view: StepView, name: string, path: string | undefined) {
    if (view.state !== 'open') return html`<span class="step__name">${name}</span>`;
    if (path) return html`<a class="step__name" href=${path}>${name}</a>`;
    const act = () => this.#stepAction(view.step);
    return html`<button type="button" class="link step__name" @click=${act}>${name}</button>`;
  }

  /**
   * The email step says what is wrong where Health leaves it out; the test entry's waits for a form until there is
   * one; any other says what it is for.
   */
  #renderStepHelp(view: StepView, checklist: ChecklistResponseModel) {
    const covered =
      view.step === 'email' && this._health.state === 'ready'
        ? this._health.value.filter((finding) => finding.coversEmail)
        : [];
    return covered.length > 0
      ? covered.map((finding) => html`<p class="step__help">${finding.message}</p>`)
      : html`<p class="step__help">
          ${this.localize.term(
            view.step === 'testEntry' && !checklist.previewForm
              ? 'jfbOverview_help_testEntryWaiting'
              : `jfbOverview_help_${view.step}`,
          )}
        </p>`;
  }

  #renderStep(view: StepView, checklist: ChecklistResponseModel) {
    const name = this.localize.term(`jfbOverview_step_${view.step}`);
    if (view.state === 'done') {
      return html`<li class="step">
        <uui-icon class="mark mark--done" name="icon-check"></uui-icon>
        <span class="done"
          >${name}<span class="visually-hidden">, ${this.localize.term('jfbOverview_done')}</span></span
        >
      </li>`;
    }

    const path = this.#stepPath(view.step, checklist);
    const action = this.localize.term(`jfbOverview_action_${view.step}`);
    return html`<li class="step">
      <span class="mark mark--open"></span>
      <div class="step__text">${this.#renderStepName(view, name, path)} ${this.#renderStepHelp(view, checklist)}</div>
      ${
        view.state === 'next'
          ? html`<uui-button
              look="outline"
              label=${action}
              href=${path ?? nothing}
              @click=${path ? nothing : () => this.#stepAction(view.step)}
            ></uui-button>`
          : nothing
      }
      ${view.state === 'needsSomeone' ? html`<span class="who">${this.localize.term('jfbOverview_needsAdministrator')}</span>` : nothing}
    </li>`;
  }

  #renderChecklist() {
    if (!this.#checklistShown) return nothing;
    const headline = this.localize.term('jfbOverview_checklist');
    if (this._checklist.state === 'failed')
      return this.#renderFailed(headline, 'jfbOverview_checklistFailed', () => void this.#loadChecklist());
    if (this._checklist.state === 'loading') return this.#renderSkeleton(headline, 'jfbOverview_checklistLoading');
    const checklist = this._checklist.value;
    return html`<uui-box headline-variant="h2">
      <span slot="headline" class="title">${headline}</span>
      <span slot="header-actions" class="count"
        >${this.localize.term('jfbOverview_doneCount', doneCount(checklist.items), checklist.items.length)}</span
      >
      <ol tabindex="-1">
        ${stepViews(checklist.items).map((view) => this.#renderStep(view, checklist))}
      </ol>
    </uui-box>`;
  }

  #renderFinding(finding: HealthFindingResponseModel) {
    const link = finding.link ? FINDING_LINKS[finding.link] : undefined;
    const needs = link?.needs ?? 'manageSettings';
    return html`<li class="finding">
      <uui-icon name="icon-alert"></uui-icon>
      <span>
        ${finding.message}
        ${link && this._global?.includes(needs) ? html`<a href=${screenPath(link.entityType)}>${this.localize.term(`jfbOverview_open_${finding.link}`)}</a>` : nothing}
        ${this._global && !this._global.includes(needs) ? html`<span class="who">${this.localize.term('jfbOverview_needsAdministrator')}</span>` : nothing}
      </span>
    </li>`;
  }

  /** Once setup is complete, the checklist can be asked for from here. */
  #renderMore() {
    if (this.#checklistShown) return nothing;
    return html`<umb-dropdown compact hide-expand look="secondary" label=${this.localize.term('jfbOverview_more')}>
      <uui-symbol-more slot="label"></uui-symbol-more>
      <uui-menu-item
        label=${this.localize.term('jfbOverview_showChecklist')}
        @click-label=${this.#showChecklist}
      ></uui-menu-item>
    </umb-dropdown>`;
  }

  #renderHealth() {
    if (this._health.state === 'loading') return this.#renderSkeleton(undefined, 'jfbOverview_healthLoading');
    if (this._health.state === 'failed')
      return this.#renderFailed(undefined, 'jfbOverview_healthFailed', () => void this.#loadHealth());
    const items = this._checklist.state === 'ready' ? this._checklist.value.items : [];
    const findings = findingsToShow(this._health.value, this.#checklistShown, items);
    return html`<uui-box aria-label=${this.localize.term('jfbOverview_health')}>
      <div class="health">
        <div class="health__body">
          ${
            findings.length === 0
              ? html`<div class="passing" role="status">
                  <uui-icon name="icon-check"></uui-icon><span>${this.localize.term('jfbOverview_allPass')}</span>
                </div>`
              : html`<ul>
                  ${findings.map((finding) => this.#renderFinding(finding))}
                </ul>`
          }
        </div>
        ${this.#renderMore()}
      </div>
    </uui-box>`;
  }

  #renderEntry(entry: RecentEntryResponseModel) {
    const name = entry.title ?? this.localize.term('jfbOverview_entryNumber', entry.number);
    return html`<tr class=${entry.unseen ? 'entry--unseen' : ''}>
      <td>
        ${entry.unseen ? html`<span class="dot"></span>` : nothing}
        <a href=${screenPath(ENTRY_ENTITY_TYPE, entry.key)}>${name}</a>
        ${entry.unseen ? html`<span class="visually-hidden">, ${this.localize.term('jfbOverview_unseen')}</span>` : nothing}
      </td>
      <td class="muted">${entry.formName}</td>
      <td class="muted end">
        ${receivedText(new Date(entry.submitted), new Date(), this.localize.lang() || 'en', {
          justNow: this.localize.term('jfbOverview_justNow'),
          minutes: (count) => this.localize.term('jfbOverview_minutesAgo', count),
          hours: (count) => this.localize.term('jfbOverview_hoursAgo', count),
          today: (time) => this.localize.term('jfbOverview_todayAt', time),
          yesterday: (time) => this.localize.term('jfbOverview_yesterdayAt', time),
        })}
      </td>
    </tr>`;
  }

  #renderRecent() {
    if (!this._viewsEntries) return nothing;
    const headline = this.localize.term('jfbOverview_recent');
    if (this._recent.state === 'loading') return this.#renderSkeleton(headline, 'jfbOverview_recentLoading');
    if (this._recent.state === 'failed')
      return this.#renderFailed(headline, 'jfbOverview_recentFailed', () => void this.#loadRecent());
    if (this._recent.value.length === 0)
      return html`<uui-box headline-variant="h2">
        <span slot="headline" class="title">${headline}</span>
        <p class="muted">${this.localize.term('jfbOverview_recentEmpty')}</p></uui-box
      >`;
    return html`<uui-box headline-variant="h2" class="box--flush">
      <span slot="headline" class="title">${headline}</span>
      <table>
        <thead class="visually-hidden">
          <tr>
            <th scope="col">${this.localize.term('jfbOverview_columnEntry')}</th>
            <th scope="col">${this.localize.term('jfbOverview_columnForm')}</th>
            <th scope="col">${this.localize.term('jfbOverview_columnReceived')}</th>
          </tr>
        </thead>
        <tbody>
          ${this._recent.value.map((entry) => this.#renderEntry(entry))}
        </tbody>
      </table>
    </uui-box>`;
  }

  #renderSkeleton(headline: string | undefined, text: string) {
    return html`<uui-box headline-variant="h2" aria-busy="true">
      ${headline ? html`<span slot="headline" class="title">${headline}</span>` : nothing}
      <span class="skeleton" style="width: 62%"></span>
      <span class="skeleton" style="width: 41%"></span>
      <span class="visually-hidden">${this.localize.term(text)}</span>
    </uui-box>`;
  }

  #renderFailed(headline: string | undefined, text: string, retry: () => void) {
    return html`<uui-box headline-variant="h2">
      ${headline ? html`<span slot="headline" class="title">${headline}</span>` : nothing}
      <div class="failed" role="alert">
        <span>${this.localize.term(text)}</span>
        <uui-button look="outline" label=${this.localize.term('jfbOverview_tryAgain')} @click=${retry}></uui-button>
      </div>
    </uui-box>`;
  }

  override render() {
    if (this._paused) {
      return html`<uui-box><p role="alert">${this.localize.term('jfbOverview_paused')}</p></uui-box>`;
    }

    return html`${this.#renderChecklist()}${this.#renderHealth()}${this.#renderRecent()}${
      this._viewsEntries ? html`<jfb-insight-glance></jfb-insight-glance>` : nothing
    }`;
  }
}

if (!customElements.get('jfb-overview')) customElements.define('jfb-overview', OverviewElement);

export { OverviewElement as element };

declare global {
  interface HTMLElementTagNameMap {
    'jfb-overview': OverviewElement;
  }
}
