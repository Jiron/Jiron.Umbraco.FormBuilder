// Email me a summary... (ROADMAP 11.1, IA 6.4, ADR-0209): a sidebar where the user turns the summary email off, or on
// daily or weekly on Mondays, sees the mail as it would go out for the period due now, and sends that mail to their own
// address at once, marked as a test. Off shows the daily mail, which is what turning it on would send.
import { css, html, nothing } from '@umbraco-cms/backoffice/external/lit';
import { UmbModalBaseElement } from '@umbraco-cms/backoffice/modal';
import { UMB_NOTIFICATION_CONTEXT } from '@umbraco-cms/backoffice/notification';
import type { EntryDigestResponseModel } from '../api';
import { loadActionWords } from '../designer/actions/action-words';
import '../designer/actions/email-preview.element';
import type { EmailPreview } from '../designer/actions/email-preview';
import { NO_PREVIEW, type PreviewState } from '../designer/preview/draft-previewer';
import { jfbBackofficeTokens } from '../styles/tokens';
import { skeletonLines, jfbSkeleton } from '../ui/skeleton';
import { previewDigest, readDigest, saveDigest, testDigest, type DigestFrequency } from './entries-api';
import type { EntryDigestModalValue } from './entry-modals';

/** How often, in the order the sidebar offers them. */
export const DIGEST_FREQUENCIES: readonly DigestFrequency[] = ['off', 'daily', 'weekly'];

/** The mail a frequency shows and tests: off shows the daily one. */
export const mailOf = (frequency: DigestFrequency): 'daily' | 'weekly' => (frequency === 'weekly' ? 'weekly' : 'daily');

/**
 * The place a time zone is named after, as the sentence says it: Berlin for Europe/Berlin, New York for
 * America/New_York; '' for UTC and its other names, which the sentence names as UTC.
 */
export function placeOf(zone: string): string {
  if (/^(Etc\/)?(UTC|UCT|GMT|Zulu|Universal|Greenwich)$/i.test(zone)) return '';
  return zone.slice(zone.lastIndexOf('/') + 1).replaceAll('_', ' ');
}

export class EntryDigestModalElement extends UmbModalBaseElement<never, EntryDigestModalValue> {
  static override properties = {
    ...UmbModalBaseElement.properties,
    _choice: { state: true },
    _failed: { state: true },
    _frequency: { state: true },
    _preview: { state: true },
    _saving: { state: true },
    _testing: { state: true },
    _saveFailed: { state: true },
  };

  static override styles = [
    jfbBackofficeTokens,
    jfbSkeleton,
    css`
      .stack {
        display: grid;
        gap: var(--uui-size-space-5);
      }

      fieldset {
        margin: 0;
        padding: 0;
        border: 0;
      }

      legend {
        margin-block-end: var(--uui-size-space-2);
        padding: 0;
        font-weight: 700;
      }

      .help {
        margin: var(--uui-size-space-3) 0 0;
        color: var(--jfb-bo-color-text-muted);
        font-size: var(--uui-type-small-size);
      }

      .note {
        display: grid;
        justify-items: start;
        gap: var(--uui-size-space-3);
      }

      .note p {
        margin: 0;
        color: var(--jfb-bo-color-text-muted);
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

  declare private _choice: EntryDigestResponseModel | undefined;
  declare private _failed: boolean;
  declare private _frequency: DigestFrequency;
  declare private _preview: PreviewState<EmailPreview>;
  declare private _saving: boolean;
  declare private _testing: boolean;
  declare private _saveFailed: boolean;

  /** The mail the preview last asked for, so a late answer for another one is dropped. */
  #asked: 'daily' | 'weekly' | undefined;

  constructor() {
    super();
    this._failed = false;
    this._frequency = 'off';
    this._preview = NO_PREVIEW;
    this._saving = false;
    this._testing = false;
    this._saveFailed = false;
  }

  override connectedCallback() {
    super.connectedCallback();
    void this.#read();
  }

  #term = (key: string, ...args: unknown[]) => this.localize.term(`jfbEntries_${key}`, ...args);

  async #read() {
    this._failed = false;
    // The preview speaks the Actions area's words, which load with it rather than with the backoffice's start.
    const [choice] = await Promise.all([
      readDigest(this),
      loadActionWords(this.localize.lang()).catch(() => undefined),
    ]);
    this._failed = !choice;
    this._choice = choice;
    if (!choice) return;
    this._frequency = choice.frequency as DigestFrequency;
    void this.#ask();
  }

  async #ask(again = false) {
    const mail = mailOf(this._frequency);
    if (!again && this.#asked === mail && this._preview.status !== 'failed') return;
    this.#asked = mail;
    this._preview = { status: 'asking', preview: this._preview.preview };
    const preview = await previewDigest(this, mail);
    if (this.#asked !== mail) return;
    this._preview = preview ? { status: 'shown', preview } : { status: 'failed', preview: null };
  }

  #choose(frequency: DigestFrequency) {
    this._frequency = frequency;
    this._saveFailed = false;
    void this.#ask();
  }

  async #save() {
    if (this._saving) return;
    this._saving = true;
    this._saveFailed = false;
    const saved = await saveDigest(this, this._frequency);
    this._saving = false;
    if (!saved) {
      this._saveFailed = true;
      return;
    }

    this.value = { frequency: this._frequency };
    this._submitModal();
  }

  async #test() {
    if (this._testing) return;
    this._testing = true;
    const test = await testDigest(this, mailOf(this._frequency));
    this._testing = false;
    const notifications = await this.getContext(UMB_NOTIFICATION_CONTEXT);
    const message =
      'to' in test
        ? this.#term('digest_testSent', test.to)
        : test.refused === 'limited'
          ? this.localize.term('jfbLibrary_testsLimited')
          : this.#term('digest_testFailed');
    notifications?.peek('to' in test ? 'positive' : 'danger', { data: { message } });
  }

  #when(choice: EntryDigestResponseModel): string {
    const place = placeOf(choice.timeZone);
    return place ? this.#term('digest_when', choice.sendsAt, place) : this.#term('digest_whenUtc', choice.sendsAt);
  }

  #body() {
    const choice = this._choice;
    if (!choice) {
      return this._failed
        ? html`<uui-box>
            <div class="failed" role="alert">
              <span>${this.#term('digest_failed')}</span>
              <uui-button look="outline" label=${this.#term('retry')} @click=${() => void this.#read()}></uui-button>
            </div>
          </uui-box>`
        : html`<uui-box>${skeletonLines(this.#term('loading'), [60, 80, 40])}</uui-box>`;
    }

    return html`<div class="stack">
      <uui-box>
        <fieldset aria-describedby="when">
          <legend>${this.#term('digest_often')}</legend>
          <uui-radio-group
            .value=${this._frequency}
            @change=${(event: Event) => this.#choose((event.target as HTMLInputElement).value as DigestFrequency)}
          >
            ${DIGEST_FREQUENCIES.map(
              (frequency) =>
                html`<uui-radio value=${frequency} label=${this.#term(`digest_${frequency}`)}></uui-radio>`,
            )}
          </uui-radio-group>
          <p class="help" id="when">${this.#when(choice)}</p>
        </fieldset>
      </uui-box>
      <uui-box>
        <jfb-email-preview
          .state=${this._preview}
          heading=${this.#term('digest_preview')}
          help=""
          hides-to
          .retry=${() => void this.#ask(true)}
        ></jfb-email-preview>
      </uui-box>
      <div class="note">
        <p>${choice.address ? this.#term('digest_note', choice.address) : this.#term('digest_noAddress')}</p>
        ${
          // The test goes to the address the note names, so it sits under it rather than crowding the footer.
          choice.address
            ? html`<uui-button
                look="secondary"
                label=${this.#term('digest_test')}
                .state=${this._testing ? 'waiting' : undefined}
                @click=${() => void this.#test()}
              ></uui-button>`
            : nothing
        }
      </div>
      ${this._saveFailed ? html`<p class="failed" role="alert">${this.#term('digest_saveFailed')}</p>` : nothing}
    </div>`;
  }

  override render() {
    const ready = this._choice !== undefined;
    return html`<umb-body-layout headline=${this.#term('digest_headline')}>
      ${this.#body()}
      <div slot="actions" class="actions">
        <uui-button label=${this.#term('digest_cancel')} @click=${this._rejectModal}></uui-button>
        <uui-button
          look="primary"
          color="positive"
          label=${this.#term('digest_save')}
          ?disabled=${!ready}
          .state=${this._saving ? 'waiting' : undefined}
          @click=${() => void this.#save()}
        ></uui-button>
      </div>
    </umb-body-layout>`;
  }
}

if (!customElements.get('jfb-entry-digest-modal'))
  customElements.define('jfb-entry-digest-modal', EntryDigestModalElement);

export { EntryDigestModalElement as element };

declare global {
  interface HTMLElementTagNameMap {
    'jfb-entry-digest-modal': EntryDigestModalElement;
  }
}
