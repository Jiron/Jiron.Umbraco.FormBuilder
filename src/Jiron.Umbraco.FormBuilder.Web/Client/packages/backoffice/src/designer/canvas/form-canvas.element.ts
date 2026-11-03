// The canvas (ROADMAP 9.3b, docs/ux/information-architecture.md section 5.1, ADR-0168): the form as visitors see it, in
// the default theme, one page at a time at the chosen width, with the designer's controls laid over it. Above it sit
// the width switch with Undo and Redo in its menu, and the pages as steps with Add page and After sending. Every page, group
// and field selects itself; an insert point between fields and the Add field at the end of a page open the palette
// for that place. The canvas changes nothing itself: it says what was asked in a request, which the Design view does.
import { html, nothing, repeat, type PropertyValues, type TemplateResult } from '@umbraco-cms/backoffice/external/lit';
import { UmbLitElement } from '@umbraco-cms/backoffice/lit-element';
import type { FieldViewModel } from '@jfb/element/answer';
import { writeNavigation, writeOutcome } from '@jfb/element/views';
import type { FieldTypeResponseModel } from '../../api';
import { jfbBackofficeTokens } from '../../styles/tokens';
import type { DesignDefinition, DesignField, DesignGroup, DesignPage, LocalizedText } from '../definition';
import type { Place } from '../editor-model';
import { fieldModel, formSettings, groupClasses, textIn, type PreviewContext } from '../preview/field-models';
import type { CanvasLook } from '../preview/look';
import { fragmentOf, safeHtml, showsText } from '../preview/rich-text';
import { FIELD_REQUEST, type FieldRequest, type FieldState, type FieldWords } from './canvas-field.element';
import './canvas-field.element';
import { canvasChrome } from './canvas.styles';
import { CANVAS_WIDTHS, type CanvasWidth } from './widths';

/** What the canvas shows: a page by its place, or what visitors get after sending. */
export type CanvasShown = number | 'after';

/** What the canvas asks the Design view to do. */
export type CanvasRequest =
  | FieldRequest
  | { readonly kind: 'addField'; readonly page: string }
  | { readonly kind: 'show'; readonly shown: CanvasShown }
  | { readonly kind: 'addPage' }
  | { readonly kind: 'width'; readonly width: CanvasWidth }
  | { readonly kind: 'undo' }
  | { readonly kind: 'redo' }
  | { readonly kind: 'paste' }
  | { readonly kind: 'changeOutcome' };

/** The event a request travels in. */
export const CANVAS_REQUEST = 'jfb-canvas-request';

export class FormCanvasElement extends UmbLitElement {
  static override properties = {
    definition: { attribute: false },
    selection: { attribute: false },
    shown: { attribute: false },
    look: { attribute: false },
    sheets: { attribute: false },
    types: { attribute: false },
    mediaUrls: { attribute: false },
    width: { attribute: false },
    insertAt: { attribute: false },
    editing: { attribute: false },
    canUndo: { attribute: false },
    canRedo: { attribute: false },
    _narrowed: { state: true },
  };

  static override styles = [jfbBackofficeTokens, canvasChrome];

  declare definition: DesignDefinition;
  declare selection: string | null;
  declare shown: CanvasShown;
  declare look: CanvasLook | undefined;
  /** The default theme's stylesheets, adopted below the canvas's own. */
  declare sheets: readonly CSSStyleSheet[];
  declare types: Readonly<Record<string, FieldTypeResponseModel>>;
  /** The addresses of the media items the form's images show, by key. */
  declare mediaUrls: Readonly<Record<string, string>>;
  declare width: CanvasWidth;
  /** The insert point the palette is open for, which stays in sight. */
  declare insertAt: Place | null;
  /** The field whose label is edited in place. */
  declare editing: string | null;
  declare canUndo: boolean;
  declare canRedo: boolean;
  declare private _narrowed: boolean;

  #resize = new ResizeObserver(() => this.#measure());
  #observed: Element | null = null;
  /** Each field's view model, kept while the field and what it is built with stay the same. */
  #models = new WeakMap<DesignField, { context: PreviewContext; columns: number; model: FieldViewModel | null }>();
  #previewContext: { key: readonly unknown[]; context: PreviewContext & ReturnType<typeof formSettings> } | undefined;

  constructor() {
    super();
    this.definition = { pages: [] };
    this.selection = null;
    this.shown = 0;
    this.sheets = [];
    this.types = {};
    this.mediaUrls = {};
    this.width = 'desktop';
    this.insertAt = null;
    this.editing = null;
    this.canUndo = false;
    this.canRedo = false;
    this._narrowed = false;
    this.addEventListener(FIELD_REQUEST, (event) => {
      event.stopPropagation();
      this.#ask((event as CustomEvent<FieldRequest>).detail);
    });
  }

  override disconnectedCallback() {
    this.#resize.disconnect();
    super.disconnectedCallback();
  }

  /** Moves the focus to an element's control: a field's or a group's button, a page's step. */
  focusElement(key: string) {
    const root = this.shadowRoot;
    const field = root?.querySelector<HTMLElement & { focusField?: () => void }>(`jfb-canvas-field[key="${key}"]`);
    // A field the canvas shows is found in its group or in its repeatable group's row.
    if (field?.focusField) {
      field.focusField();
      field.scrollIntoView({ block: 'nearest' });
      return;
    }
    root?.querySelector<HTMLElement>(`[data-select="${key}"]`)?.focus();
  }

  /** Moves the focus to the Add field of the page shown. */
  focusAddField() {
    this.shadowRoot?.querySelector<HTMLElement>('.bo-add-field')?.focus();
  }

  protected override updated(changed: PropertyValues) {
    super.updated(changed);
    const root = this.shadowRoot;
    if (changed.has('sheets') && root && 'adoptedStyleSheets' in root) {
      // The theme first, so the canvas's own rules for its controls come after it.
      const own = FormCanvasElement.elementStyles.flatMap((style) =>
        style instanceof CSSStyleSheet ? [style] : style.styleSheet ? [style.styleSheet] : [],
      );
      root.adoptedStyleSheets = [...this.sheets, ...own];
    }
    const viewport = root?.querySelector('.bo-viewport') ?? null;
    if (viewport !== this.#observed) {
      this.#resize.disconnect();
      this.#observed = viewport;
      if (viewport) this.#resize.observe(viewport);
    }
    if (changed.has('width')) this.#measure();
  }

  #measure() {
    const viewport = this.shadowRoot?.querySelector<HTMLElement>('.bo-viewport');
    if (!viewport) return;
    const style = getComputedStyle(viewport);
    const room = viewport.clientWidth - parseFloat(style.paddingInlineStart) - parseFloat(style.paddingInlineEnd);
    // The sheet's own padding on each side, as canvas.styles.ts sets it.
    this._narrowed = room - 2 * 32 < CANVAS_WIDTHS[this.width];
  }

  #ask(request: CanvasRequest) {
    this.dispatchEvent(
      new CustomEvent<CanvasRequest>(CANVAS_REQUEST, { detail: request, bubbles: true, composed: true }),
    );
  }

  // What the fields are built with, the same object while nothing it is made of changes, so their models are kept.
  #context(look: CanvasLook): PreviewContext & ReturnType<typeof formSettings> {
    const key = [look, this.types, this.mediaUrls, this.definition['settings']];
    const kept = this.#previewContext;
    if (kept && kept.key.every((part, at) => part === key[at])) return kept.context;
    const culture = look.culture;
    const urls = this.mediaUrls;
    const context = {
      look,
      types: this.types,
      culture,
      prefix: 'jfb-canvas',
      showRows: true,
      mediaUrl: (media: string) => (Object.hasOwn(urls, media) ? urls[media] : undefined),
      ...formSettings(this.definition, look, culture),
    };
    this.#previewContext = { key, context };
    return context;
  }

  #model(field: DesignField, context: PreviewContext, columns: number): FieldViewModel | null {
    const kept = this.#models.get(field);
    if (kept && kept.context === context && kept.columns === columns) return kept.model;
    const model = fieldModel(field, context, columns);
    this.#models.set(field, { context, columns, model });
    return model;
  }

  #typeName(type: string): string {
    const found = Object.hasOwn(this.types, type) ? this.types[type] : undefined;
    return found ? this.localize.string(found.name) : type;
  }

  #fieldLabel(field: DesignField, culture: string): string {
    const type = field.type === 'heading' ? textIn((field['settings'] as { text?: LocalizedText })?.text, culture) : '';
    return textIn(field.label, culture) || type || field.alias || this.localize.term('jfbDesigner_untitledField');
  }

  #groupName(group: DesignGroup, index: number, culture: string): string {
    return textIn(group.title, culture) || this.localize.term('jfbDesigner_groupNumber', index + 1);
  }

  #pageName(page: DesignPage, index: number, culture: string): string {
    return textIn(page.title, culture) || this.localize.term('jfbDesigner_pageNumber', index + 1);
  }

  // Why a field visitors don't see shows a note in its place.
  #unseen(field: DesignField, model: FieldViewModel | null): string {
    const type = this.#typeName(field.type);
    if (!Object.hasOwn(this.types, field.type)) return this.localize.term('jfbDesigner_unseenGone', field.type);
    if (field.type === 'hidden') return this.localize.term('jfbDesigner_unseenHidden', type);
    if (field.type === 'captcha' && !model) return this.localize.term('jfbDesigner_unseenCaptcha', type);
    if (field.type === 'image') {
      // A picture chosen that the user may not open, or that is gone, is still not "no picture".
      const media = (field['settings'] as { media?: unknown } | undefined)?.media;
      const chosen = typeof media === 'string' && media !== '';
      return this.localize.term(chosen ? 'jfbDesigner_unseenPicture' : 'jfbDesigner_unseenImage', type);
    }
    if (!model) return this.localize.term('jfbDesigner_unseenOff', type);
    return this.localize.term('jfbDesigner_unseenEmpty', type);
  }

  #field(field: DesignField, context: PreviewContext, columns: number): TemplateResult {
    return html`<jfb-canvas-field
      .state=${this.#state(field, context, columns, null)}
      .classes=${context.look.classes}
    ></jfb-canvas-field>`;
  }

  // A field's state on the canvas, a repeatable group's with its own fields'; `container` is the repeatable group a
  // field is in, whose insert points count within it.
  #state(field: DesignField, context: PreviewContext, columns: number, container: DesignField | null): FieldState {
    const model = this.#model(field, context, columns);
    const label = this.#fieldLabel(field, context.culture);
    const words: FieldWords = {
      name: this.localize.term('jfbDesigner_fieldName', label, this.#typeName(field.type)),
      insertHere: this.localize.term('jfbDesigner_insertHere'),
      insertAfter: this.localize.term('jfbDesigner_insertAfter', label),
      duplicate: this.localize.term('jfbDesigner_duplicate'),
      duplicateNamed: this.localize.term('jfbDesigner_duplicateNamed', label),
      delete: this.localize.term('jfbDesigner_delete'),
      deleteNamed: this.localize.term('jfbDesigner_deleteNamed', label),
      labelBox: this.localize.term('jfbDesigner_labelBox'),
      unseen: this.#unseen(field, model),
      ...(field.fields
        ? {
            insertFirst: field.fields[0]
              ? this.localize.term('jfbDesigner_insertBefore', this.#fieldLabel(field.fields[0], context.culture))
              : this.localize.term('jfbDesigner_insertInto', label),
          }
        : {}),
    };
    const children = field.fields?.map((child) => this.#state(child, context, columns, field));
    return {
      key: field.key,
      model,
      words,
      selected: this.selection === field.key,
      editing: this.editing === field.key,
      labelText: field.type === 'heading' ? label : textIn(field.label, ''),
      insertActive: this.#insertsAfter(field, container),
      ...(children
        ? { children, firstActive: this.insertAt?.container === field.key && this.insertAt.index === 0 }
        : {}),
    };
  }

  // Whether the open insert point is the one below a field, in its group or its repeatable group.
  #insertsAfter(field: DesignField, container: DesignField | null): boolean {
    const at = this.insertAt;
    if (!at) return false;
    if (container) {
      const index = (container.fields ?? []).findIndex((each) => each.key === field.key);
      return at.container === container.key && at.index === index + 1;
    }
    for (const page of this.definition.pages)
      for (const group of page.groups) {
        const index = group.fields.findIndex((each) => each.key === field.key);
        if (index >= 0) return at.container === group.key && at.index === index + 1;
      }
    return false;
  }

  #tools(key: string, name: string): TemplateResult {
    return html`<div class="bo-tools">
      <button
        type="button"
        aria-label=${this.localize.term('jfbDesigner_duplicateNamed', name)}
        @click=${() => this.#ask({ kind: 'duplicate', key })}
      >
        <uui-icon name="icon-documents" aria-hidden="true"></uui-icon>${this.localize.term('jfbDesigner_duplicate')}
      </button>
      <button
        type="button"
        aria-label=${this.localize.term('jfbDesigner_deleteNamed', name)}
        @click=${() => this.#ask({ kind: 'delete', key })}
      >
        <uui-icon name="icon-trash" aria-hidden="true"></uui-icon>${this.localize.term('jfbDesigner_delete')}
      </button>
    </div>`;
  }

  #group(group: DesignGroup, index: number, context: PreviewContext): TemplateResult {
    const culture = context.culture;
    const title = textIn(group.title, culture);
    const description = safeHtml(textIn(group['description'] as LocalizedText | undefined, culture));
    const name = this.#groupName(group, index, culture);
    const columns = Math.min(4, Math.max(1, Math.trunc(Number(group['columns'] ?? 1)) || 1));
    const selected = this.selection === group.key;
    const first = group.fields[0];
    const firstActive = this.insertAt?.container === group.key && this.insertAt.index === 0;
    const select = html`<button
        type="button"
        class="bo-select bo-select-group"
        data-select=${group.key}
        aria-label=${this.localize.term('jfbDesigner_groupName', name)}
        aria-current=${selected ? 'true' : 'false'}
        @click=${() => this.#ask({ kind: 'select', key: group.key })}
      ></button>
      ${selected ? this.#tools(group.key, name) : nothing}`;
    const help = showsText(description) ? html`<div class="jfb-help" inert>${fragmentOf(description)}</div>` : nothing;
    const fields = html`<div class="jfb-fields">
      ${
        first
          ? html`<button
              type="button"
              class="bo-insert bo-insert-first"
              ?data-active=${firstActive}
              aria-label=${this.localize.term('jfbDesigner_insertBefore', this.#fieldLabel(first, culture))}
              @click=${() => this.#ask({ kind: 'insertFirst', container: group.key })}
            >
              <span class="bo-pill"
                ><uui-icon name="icon-add" aria-hidden="true"></uui-icon
                >${this.localize.term('jfbDesigner_insertHere')}</span
              >
            </button>`
          : html`<button
              type="button"
              class="bo-empty-group"
              ?data-active=${firstActive}
              aria-label=${this.localize.term('jfbDesigner_insertInto', name)}
              @click=${() => this.#ask({ kind: 'insertFirst', container: group.key })}
            >
              <uui-icon name="icon-add" aria-hidden="true"></uui-icon>${this.localize.term('jfbDesigner_insertHere')}
            </button>`
      }
      ${repeat(
        group.fields,
        (field) => field.key,
        (field) => this.#field(field, context, columns),
      )}
    </div>`;
    const classes = `${groupClasses(group)} bo-group${selected ? ' bo-selected' : ''}`;
    return title
      ? html`<fieldset class=${classes}>
          ${select}
          <legend inert>${title}</legend>
          ${help}${fields}
        </fieldset>`
      : html`<div class=${classes}>${select}${help}${fields}</div>`;
  }

  #page(page: DesignPage, index: number, context: PreviewContext & ReturnType<typeof formSettings>): TemplateResult {
    const culture = context.culture;
    const title = textIn(page.title, culture);
    const description = safeHtml(textIn(page['description'] as LocalizedText | undefined, culture));
    const name = this.#pageName(page, index, culture);
    const selected = this.selection === page.key;
    const noFields = page.groups.every((group) => group.fields.length === 0);
    const formEmpty = this.definition.pages.every((each) => each.groups.every((group) => group.fields.length === 0));
    const last = index === this.definition.pages.length - 1;
    const navigation = writeNavigation(document, context.look.classes, {
      ...(index > 0 ? { back: { label: context.back || this.#word('back'), value: 'back' } } : {}),
      ...(last
        ? { submit: { label: context.send || this.#word('send'), value: 'submit' } }
        : { next: { label: context.next || this.#word('next'), value: 'next' } }),
    });
    navigation.inert = true;
    return html`<section class="jfb-page bo-page${selected ? ' bo-selected' : ''}">
        <button
          type="button"
          class="bo-select bo-select-page"
          data-select=${page.key}
          aria-label=${this.localize.term('jfbDesigner_pageName', name)}
          aria-current=${selected ? 'true' : 'false'}
          @click=${() => this.#ask({ kind: 'select', key: page.key })}
        ></button>
        ${selected ? this.#tools(page.key, name) : nothing}
        ${title ? html`<h2 class="jfb-title" inert>${title}</h2>` : nothing}
        ${showsText(description) ? html`<div class="jfb-intro" inert>${fragmentOf(description)}</div>` : nothing}
        ${
          noFields
            ? html`<p class="bo-empty">
                ${
                  formEmpty
                    ? html`${this.localize.term('jfbDesigner_emptyFormBefore')}
                        <button type="button" class="bo-link" @click=${() => this.#ask({ kind: 'paste' })}>
                          ${this.localize.term('jfbDesigner_emptyFormLink')}</button
                        >${this.localize.term('jfbDesigner_emptyFormAfter')}`
                    : this.localize.term('jfbDesigner_emptyPage')
                }
              </p>`
            : nothing
        }
        ${noFields ? nothing : page.groups.map((group, at) => this.#group(group, at, context))}
        <button type="button" class="bo-add-field" @click=${() => this.#ask({ kind: 'addField', page: page.key })}>
          <uui-icon name="icon-add" aria-hidden="true"></uui-icon>${this.localize.term('jfbDesigner_addField')}
        </button>
      </section>
      ${navigation}`;
  }

  #word(name: string): string {
    return this.look?.words[name] ?? name;
  }

  // What visitors get after sending: the outcome without rules, else the package's thanks.
  #after(look: CanvasLook): TemplateResult {
    const outcomes = (this.definition['outcomes'] ?? []) as readonly {
      kind?: string;
      message?: LocalizedText;
      rules?: readonly unknown[];
    }[];
    const outcome = outcomes.find((each) => (each.rules ?? []).length === 0) ?? outcomes[0];
    let shown: Node | TemplateResult;
    if (outcome?.kind === 'redirect')
      shown = html`<p class="bo-empty">${this.localize.term('jfbDesigner_afterRedirect')}</p>`;
    else {
      const message = safeHtml(textIn(outcome?.message, look.culture));
      const element = writeOutcome(document, look.classes, {
        message: showsText(message) ? message : `<p>${safeHtml(this.#word('thankYou'))}</p>`,
        isPending: false,
      });
      element.removeAttribute('tabindex');
      element.inert = true;
      shown = element;
    }
    return html`<div class="bo-after">
      ${shown}
      <uui-button
        look="outline"
        label=${this.localize.term('jfbDesigner_changeOutcome')}
        @click=${() => this.#ask({ kind: 'changeOutcome' })}
      ></uui-button>
    </div>`;
  }

  #toolbar(): TemplateResult {
    return html`<div class="bo-bar">
      <div class="bo-widths" role="group" aria-label=${this.localize.term('jfbDesigner_canvasWidth')}>
        ${(Object.keys(CANVAS_WIDTHS) as CanvasWidth[]).map(
          (width) =>
            html`<button
              type="button"
              aria-pressed=${this.width === width ? 'true' : 'false'}
              @click=${() => this.#ask({ kind: 'width', width })}
            >
              ${this.localize.term(`jfbDesigner_width_${width}`)}
            </button>`,
        )}
      </div>
      ${this._narrowed ? html`<span class="bo-narrowed">${this.localize.term('jfbDesigner_narrowed')}</span>` : nothing}
      <uui-button
        class="bo-more"
        compact
        popovertarget="jfb-canvas-more"
        label=${this.localize.term('jfbDesigner_moreCanvas')}
      >
        <uui-symbol-more></uui-symbol-more>
      </uui-button>
      <uui-popover-container id="jfb-canvas-more" placement="bottom-end">
        <div class="bo-menu">
          <button type="button" ?disabled=${!this.canUndo} @click=${() => this.#menu('undo')}>
            <span>${this.localize.term('jfbDesigner_undo')}</span><kbd>Ctrl+Z</kbd>
          </button>
          <button type="button" ?disabled=${!this.canRedo} @click=${() => this.#menu('redo')}>
            <span>${this.localize.term('jfbDesigner_redo')}</span><kbd>Ctrl+Y</kbd>
          </button>
        </div>
      </uui-popover-container>
    </div>`;
  }

  #menu(kind: 'undo' | 'redo') {
    this.shadowRoot?.querySelector<HTMLElement & { hidePopover?: () => void }>('#jfb-canvas-more')?.hidePopover?.();
    this.#ask({ kind });
  }

  #steps(culture: string): TemplateResult {
    return html`<nav class="bo-steps" aria-label=${this.localize.term('jfbDesigner_pages')}>
      ${this.definition.pages.map(
        (page, index) =>
          html`<button
            type="button"
            class="bo-step"
            aria-current=${this.shown === index ? 'step' : 'false'}
            @click=${() => this.#ask({ kind: 'show', shown: index })}
          >
            ${this.#pageName(page, index, culture)}
          </button>`,
      )}
      <button type="button" class="bo-step bo-step-add" @click=${() => this.#ask({ kind: 'addPage' })}>
        <uui-icon name="icon-add" aria-hidden="true"></uui-icon>${this.localize.term('jfbDesigner_addPage')}
      </button>
      <button
        type="button"
        class="bo-step"
        aria-current=${this.shown === 'after' ? 'step' : 'false'}
        @click=${() => this.#ask({ kind: 'show', shown: 'after' })}
      >
        <uui-icon name="icon-flag" aria-hidden="true"></uui-icon>${this.localize.term('jfbDesigner_afterSending')}
      </button>
    </nav>`;
  }

  override render() {
    const look = this.look;
    if (!look) return html`<div class="bo-loading"><uui-loader></uui-loader></div>`;
    const context = this.#context(look);
    const pages = this.definition.pages;
    const index = this.shown === 'after' ? -1 : Math.min(this.shown, pages.length - 1);
    const page = index >= 0 ? pages[index] : undefined;
    const width = CANVAS_WIDTHS[this.width];
    return html`${this.#toolbar()} ${this.#steps(look.culture)}
      <div class="bo-viewport" id="jfb-canvas-form" tabindex="-1">
        <div class="bo-sheet" style="--bo-width: ${width}px">
          <div class="jfb-form jfb-theme-default" style="--jfb-size-form: ${width}px" lang=${look.culture}>
            ${page ? this.#page(page, index, context) : this.#after(look)}
          </div>
        </div>
      </div>`;
  }
}

if (!customElements.get('jfb-form-canvas')) customElements.define('jfb-form-canvas', FormCanvasElement);

declare global {
  interface HTMLElementTagNameMap {
    'jfb-form-canvas': FormCanvasElement;
  }
}
