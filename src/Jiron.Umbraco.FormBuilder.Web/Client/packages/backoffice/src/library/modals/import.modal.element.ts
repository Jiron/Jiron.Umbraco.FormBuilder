// "Import a form..." (docs/ux/information-architecture.md, section 4; ADR-0046, ADR-0166): choose a JSON file, see what it holds and
// what it can't import, as the server's check says without writing anything, then import it as a new draft copy.
import { css, html, nothing } from '@umbraco-cms/backoffice/external/lit';
import { UmbModalBaseElement } from '@umbraco-cms/backoffice/modal';
import { tryExecute, UmbApiError } from '@umbraco-cms/backoffice/resources';
import { folderGet, formExchangeCheckImport, formExchangeImport, type ImportCheckResponseModel } from '../../api';
import { jfbBackofficeTokens, jfbVisuallyHidden } from '../../styles/tokens';
import { codeOf, permissionOf } from '../library-changes';
import { dayText, isGoodNews, noteTexts, type NoteWords } from '../rows';
import { ImportStore, type ChosenFile, type ImportRefusal } from '../stores';
import type { NewFormModalData, NewFormModalValue } from './tokens';

/** The first problem a refused file names, or the refusal's own words. */
function problemOf(error: unknown): string | undefined {
  if (!error || !UmbApiError.isUmbApiError(error)) return undefined;
  const details = error.problemDetails as { problems?: { message?: string }[]; detail?: string } | null | undefined;
  return details?.problems?.[0]?.message ?? details?.detail ?? undefined;
}

/** How a refused check or import reads. */
export function refusalOf(error: unknown): ImportRefusal {
  if (codeOf(error) === 'forbidden') return { kind: 'forbidden', permission: permissionOf(error) };
  if (UmbApiError.isUmbApiError(error) && error.status === 400)
    return { kind: 'unreadable', message: problemOf(error) };
  return { kind: 'failed' };
}

export class ImportModalElement extends UmbModalBaseElement<NewFormModalData, NewFormModalValue> {
  static override properties = {
    ...UmbModalBaseElement.properties,
    _folderName: { state: true },
  };

  static override styles = [
    jfbBackofficeTokens,
    jfbVisuallyHidden,
    css`
      uui-box + uui-box,
      uui-box + p {
        margin-top: var(--uui-size-space-5);
      }

      p {
        margin: 0;
      }

      .file {
        display: flex;
        flex-wrap: wrap;
        align-items: center;
        gap: var(--uui-size-space-4);
      }

      .file__text {
        flex: 1 1 12rem;
        min-width: 0;
      }

      .file__name {
        font-weight: 700;
        overflow-wrap: anywhere;
      }

      .muted {
        color: var(--jfb-bo-color-text-muted);
      }

      dl {
        display: grid;
        grid-template-columns: max-content 1fr;
        gap: var(--uui-size-space-3) var(--uui-size-space-6);
        margin: 0 0 var(--uui-size-space-4);
      }

      dt {
        color: var(--jfb-bo-color-text-muted);
      }

      dd {
        margin: 0;
        overflow-wrap: anywhere;
      }

      ul {
        margin: 0;
        padding: 0;
        list-style: none;
      }

      li {
        display: flex;
        gap: var(--uui-size-space-3);
      }

      li + li {
        margin-top: var(--uui-size-space-3);
      }

      li uui-icon {
        flex: none;
        margin-top: 2px;
      }

      .choose {
        margin-top: var(--uui-size-space-4);
      }

      .alert {
        color: var(--jfb-bo-color-danger-text);
      }
    `,
  ];

  declare private _folderName: string | null;

  readonly #store = new ImportStore(
    {
      check: (json) =>
        tryExecute(this, formExchangeCheckImport({ body: { json, folderKey: this.#folderKey() } }), {
          disableNotifications: true,
        }),
      import: (json) =>
        tryExecute(this, formExchangeImport({ body: { json, folderKey: this.#folderKey() } }), {
          disableNotifications: true,
        }),
      refusal: refusalOf,
    },
    () => this.#changed(),
  );

  override connectedCallback() {
    super.connectedCallback();
    this._folderName = null;
    const key = this.#folderKey();
    if (key)
      void tryExecute(this, folderGet({ path: { key } }), { disableNotifications: true }).then(
        ({ data }) => (this._folderName = data?.name ?? null),
      );
  }

  #folderKey(): string | null {
    return this.data?.folderKey ?? null;
  }

  #changed() {
    const state = this.#store.state;
    if (state.step === 'imported') {
      this.updateValue({ formKey: state.formKey });
      this._submitModal();
      return;
    }

    this.requestUpdate();
  }

  #choose() {
    this.shadowRoot?.querySelector<HTMLInputElement>('input[type=file]')?.click();
  }

  #onFile(event: Event) {
    const input = event.target as HTMLInputElement;
    const file = input.files?.[0];
    input.value = '';
    if (file) void this.#store.choose(file);
  }

  #noteWords(): NoteWords {
    const term = this.localize.term.bind(this.localize);
    return {
      themeMissing: (theme) => term('jfbLibrary_noteThemeMissing', theme),
      optionListReused: (list) => term('jfbLibrary_noteListReused', list),
      optionListCreated: (list) => term('jfbLibrary_noteListCreated', list),
      optionListMissing: (list) => term('jfbLibrary_noteListMissing', list),
      optionListUnnamed: term('jfbLibrary_noteListUnnamed'),
      actionConfigurationMissing: (key) => term('jfbLibrary_noteActionConfiguration', key),
      actionTypeMissing: (type) => term('jfbLibrary_noteActionType', type),
      pageMissing: term('jfbLibrary_notePageMissing'),
      other: (code) => term('jfbLibrary_noteOther', code),
    };
  }

  #size(bytes: number): string {
    return new Intl.NumberFormat(this.localize.lang() || 'en', {
      style: 'unit',
      unit: bytes < 1024 * 1024 ? 'kilobyte' : 'megabyte',
      maximumFractionDigits: 1,
    }).format(bytes < 1024 * 1024 ? Math.max(1, Math.round(bytes / 1024)) : bytes / (1024 * 1024));
  }

  #file(file: ChosenFile, check?: ImportCheckResponseModel) {
    const culture = this.localize.lang() || 'en';
    const exported = check
      ? this.localize.term(
          'jfbLibrary_fileExported',
          this.#size(file.size),
          dayText(new Date(check.exportedUtc), new Date(), culture, {
            today: this.localize.term('jfbLibrary_today'),
            yesterday: this.localize.term('jfbLibrary_yesterday'),
          }),
        )
      : this.#size(file.size);
    return html`<uui-box>
      <div class="file">
        <uui-icon name="icon-document"></uui-icon>
        <div class="file__text">
          <div class="file__name">${file.name}</div>
          <div class="muted">${exported}</div>
        </div>
        <uui-button
          look="outline"
          label=${this.localize.term('jfbLibrary_chooseAnotherFile')}
          @click=${this.#choose}
        ></uui-button>
      </div>
    </uui-box>`;
  }

  #holds(check: ImportCheckResponseModel) {
    const holds = check.holds;
    return html`<uui-box headline-variant="h4" headline=${this.localize.term('jfbLibrary_whatItHolds')}>
      <dl>
        <dt>${this.localize.term('jfbLibrary_holdsForm')}</dt>
        <dd>${check.name}</dd>
        <dt>${this.localize.term('jfbLibrary_holdsPages')}</dt>
        <dd>${this.localize.term('jfbLibrary_pagesWithFields', holds.pages, holds.fields)}</dd>
        <dt>${this.localize.term('jfbLibrary_holdsRules')}</dt>
        <dd>${holds.rules === 0 ? this.localize.term('jfbLibrary_none') : holds.rules}</dd>
        <dt>${this.localize.term('jfbLibrary_holdsOutcomes')}</dt>
        <dd>${this.localize.term('jfbLibrary_outcomes', holds.outcomes)}</dd>
        <dt>${this.localize.term('jfbLibrary_holdsActions')}</dt>
        <dd>${holds.actions === 0 ? this.localize.term('jfbLibrary_none') : holds.actions}</dd>
      </dl>
      <p class="muted">${this.localize.term('jfbLibrary_exportsNeverHold')}</p>
    </uui-box>`;
  }

  #notes(check: ImportCheckResponseModel) {
    const words = this.#noteWords();
    const missing = noteTexts(
      check.notes.filter((note) => !isGoodNews(note)),
      words,
    );
    const along = noteTexts(check.notes.filter(isGoodNews), words);
    const list = (texts: string[], icon: string) =>
      html`<ul>
        ${texts.map((text) => html`<li><uui-icon name=${icon}></uui-icon><span>${text}</span></li>`)}
      </ul>`;
    return html`${
      missing.length > 0
        ? html`<uui-box headline-variant="h4" headline=${this.localize.term('jfbLibrary_whatItCannotImport')}
            >${list(missing, 'icon-info')}</uui-box
          >`
        : nothing
    }
    ${along.length > 0 ? html`<uui-box headline-variant="h4" headline=${this.localize.term('jfbLibrary_whatComesAlong')}>${list(along, 'icon-check')}</uui-box>` : nothing}`;
  }

  #refusal(reason: ImportRefusal) {
    const text =
      reason.kind === 'tooLarge'
        ? this.localize.term('jfbLibrary_importTooLarge')
        : reason.kind === 'unreadable'
          ? this.localize.term('jfbLibrary_importUnreadable', reason.message ?? '')
          : reason.kind === 'forbidden'
            ? this.localize.term(
                'jfbLibrary_missingPermission',
                this.localize.term(`jfbLibrary_permission_${reason.permission ?? 'design'}`),
              )
            : this.localize.term('jfbLibrary_failed');
    return html`<uui-box><p class="alert" role="alert">${text}</p></uui-box>`;
  }

  #body() {
    const state = this.#store.state;
    switch (state.step) {
      case 'choose':
        return html`<uui-box>
          <p>${this.localize.term('jfbLibrary_importIntro')}</p>
          <p class="choose">
            <uui-button
              look="outline"
              label=${this.localize.term('jfbLibrary_chooseFile')}
              @click=${this.#choose}
            ></uui-button>
          </p>
        </uui-box>`;
      case 'checking':
        return html`${this.#file(state.file)}
          <uui-box><p aria-busy="true">${this.localize.term('jfbLibrary_readingFile')}</p></uui-box>`;
      case 'refused':
        return html`${this.#file(state.file)}${this.#refusal(state.reason)}`;
      case 'checked':
      case 'importing':
        return html`${this.#file(state.file, state.check)}${this.#holds(state.check)}${this.#notes(state.check)}
          <p class="muted">
            ${this.localize.term('jfbLibrary_arrivesAsDraft', this._folderName ?? this.localize.term('jfbLibrary_topOfForms'))}
          </p>`;
      default:
        return nothing;
    }
  }

  override render() {
    const state = this.#store.state;
    const canImport = state.step === 'checked' || state.step === 'importing';
    return html`<umb-body-layout headline=${this.localize.term('jfbLibrary_importHeadline')}>
      <input
        class="visually-hidden"
        type="file"
        accept=".json,application/json"
        tabindex="-1"
        aria-hidden="true"
        @change=${this.#onFile}
      />
      ${this.#body()}
      <div slot="actions">
        <uui-button label=${this.localize.term('general_cancel')} @click=${this._rejectModal}></uui-button>
        <uui-button
          look="primary"
          color="positive"
          label=${this.localize.term('jfbLibrary_importAsNewCopy')}
          ?disabled=${!canImport}
          .state=${state.step === 'importing' ? 'waiting' : undefined}
          @click=${() => void this.#store.import()}
        ></uui-button>
      </div>
    </umb-body-layout>`;
  }
}

if (!customElements.get('jfb-import-modal')) customElements.define('jfb-import-modal', ImportModalElement);

export { ImportModalElement as element };

declare global {
  interface HTMLElementTagNameMap {
    'jfb-import-modal': ImportModalElement;
  }
}
