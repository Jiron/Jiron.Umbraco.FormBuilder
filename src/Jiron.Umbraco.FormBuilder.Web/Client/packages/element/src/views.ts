/**
 * The built-in views of the default folder (`Views/JironFormBuilder/default`), written from the element endpoint's
 * view model (ADR-0128): each function follows its Razor view element for element and attribute for attribute, so the
 * element's markup is the server's (ADR-0015). What only a post without JavaScript uses (the action, the encoding, the
 * route token, the post values, a button's `formaction`) is not in the model, and the form posts through the runtime.
 * A first render has no outcome but the one in its templates, which the runtime shows after a send, and no summary page; a field of a site's own type gets its frame, its control from the
 * site (15.1).
 */
import type {
  ChoiceModel,
  ChoicesControl,
  CustomControl,
  CounterModel,
  ErrorSummaryViewModel,
  FieldViewModel,
  FileUploadWordsModel,
  FormResumeModel,
  FormViewModel,
  GroupViewModel,
  HoneypotModel,
  NavigationViewModel,
  NoticeViewModel,
  OtherChoiceModel,
  OutcomeViewModel,
  PageViewModel,
  PartsControl,
  ProgressViewModel,
  RankingMoveModel,
  RepeaterControl,
  RepeaterRowModel,
  RowButtonModel,
  SelectControl,
  ThemeClassesModel,
} from './answer';
import { append, el, FLAG, flag, hookOf, html, labelText, mark, type Child } from './dom';

/** The version of the markup contract the views write (`FormViewModel.ContractVersion`, ADR-0015). */
export const CONTRACT_VERSION = 1;

/**
 * Writes a site's own field type's control from the field's view model (ADR-0233): the controls a visitor answers
 * with, named, described and classed by the theme as the type's Razor view does; null for none.
 */
export type ControlWriter = (field: CustomField, document: Document, classes: ThemeClassesModel) => Node | null;

/** A field of a site's own type: its control is the type's part of the answer, or none. */
export type CustomField = Omit<FieldViewModel, 'control'> & { readonly control?: CustomControl };

/** What the views write with besides the model. */
export interface ViewContext {
  /** The document the markup is made in. */
  document: Document;
  /** The public API's base the runtime calls, absolute when the API is on another origin (`data-jfb-api`). */
  api: string;
  /** The writer of a site's own field type's control, by the type's alias; undefined for a type without one. */
  control?: (type: string) => ControlWriter | undefined;
  /** Told of a field whose type the element has no control for, once per type. */
  onCustom?: (type: string) => void;
}

/** The form's root, as `Form.cshtml` writes it: the form with its pages, or the notice in its place. */
export function writeForm(context: ViewContext, model: FormViewModel): HTMLElement {
  const d = context.document;
  const classes = model.classes;
  if (model.display !== 'form') {
    return el(
      d,
      'div',
      {
        class: model.cssClass,
        id: model.id,
        'data-jfb-form': model.key,
        'data-jfb-contract': CONTRACT_VERSION,
        lang: model.culture,
        dir: model.direction,
      },
      model.notice && notice(d, model.notice),
    );
  }

  const script = el(d, 'script', { type: 'application/json', 'data-jfb-model': FLAG });
  script.textContent = model.modelData;
  const templates = el(d, 'template', {
    'data-jfb-templates': FLAG,
    'data-jfb-sending': model.sending?.sendingText,
    'data-jfb-thanks': model.sending?.thanksText,
  }) as HTMLTemplateElement;
  append(templates.content, [
    model.errorSummaryTemplate && errorSummary(d, classes, model.errorSummaryTemplate),
    el(d, 'p', { class: classes.error, 'data-jfb-error': FLAG }),
    model.sending?.notices.map((each) => notice(d, each)),
    model.sending && writeOutcome(d, classes, model.sending.outcome),
    model.sending?.moves && writeNavigation(d, classes, model.sending.moves),
    model.resume && [notice(d, model.resume.saved), notice(d, model.resume.restored), notice(d, model.resume.expired)],
  ]);
  const first = model.defaultButton;
  return el(
    d,
    'form',
    {
      class: model.cssClass,
      id: model.id,
      'data-jfb-form': model.key,
      'data-jfb-revision': model.revision,
      'data-jfb-api': context.api,
      'data-jfb-content': model.contentKey,
      'data-jfb-placement': model.placement,
      'data-jfb-asks': flag(model.asksAtOnce),
      'data-jfb-insights': model.insights ? `${context.api}/forms/${model.key}/events` : undefined,
      'data-jfb-contract': CONTRACT_VERSION,
      lang: model.culture,
      dir: model.direction,
      method: 'post',
      novalidate: FLAG,
    },
    script,
    templates,
    model.resume && writeResume(d, classes, model.id, model.resume),
    first &&
      el(
        d,
        'button',
        {
          type: 'submit',
          class: 'jfb-visually-hidden',
          name: 'jfb-nav',
          value: first.value,
          tabindex: '-1',
          'aria-hidden': 'true',
        },
        first.label,
      ),
    model.progress && progress(d, classes, model.progress),
    model.notice && notice(d, model.notice),
    model.errorSummary && errorSummary(d, classes, model.errorSummary),
    model.requiredNote !== undefined &&
      el(
        d,
        'p',
        { class: classes.help, hidden: model.isRequiredNoteHidden, 'data-jfb-required-note': flag(model.hooks) },
        html(d, model.requiredNote),
      ),
    model.pages.map((page) => writePage(context, classes, page)),
    model.navigation && writeNavigation(d, classes, model.navigation),
    model.contextValues.map((input) => el(d, 'input', { type: 'hidden', name: input.name, value: input.value })),
    model.honeypot && honeypot(d, model.honeypot),
    el(d, 'div', { 'data-jfb-live': FLAG, 'aria-live': 'polite' }),
  );
}

/**
 * `Page.cshtml`: one page, hidden unless it is the visitor's, with its title, its text and its groups. The template
 * gallery's preview draws each page of a template with it (ADR-0168).
 */
export function writePage(context: ViewContext, classes: ThemeClassesModel, page: PageViewModel): HTMLElement {
  const d = context.document;
  return el(
    d,
    'section',
    {
      class: classes.page,
      'data-jfb-page': page.key,
      hidden: !page.isCurrent || page.isHidden,
      'data-jfb-hidden': flag(page.isHidden),
    },
    (page.title !== undefined || page.recalling.includes('title')) &&
      el(
        d,
        'h2',
        {
          class: 'jfb-title',
          'data-jfb-text': hookOf(page.key, 'title', page.recalling),
          hidden: page.title === undefined,
        },
        page.title,
      ),
    (page.description !== undefined || page.recalling.includes('description')) &&
      el(
        d,
        'div',
        {
          class: 'jfb-intro',
          'data-jfb-text': hookOf(page.key, 'description', page.recalling),
          hidden: page.description === undefined,
        },
        html(d, page.description),
      ),
    page.groups.map((group) => writeGroup(context, classes, group)),
  );
}

/** `Group.cshtml`: a fieldset with its legend when the group has a title, otherwise a div, with its text and fields. */
function writeGroup(context: ViewContext, classes: ThemeClassesModel, group: GroupViewModel): HTMLElement {
  const d = context.document;
  const describes = group.description !== undefined || group.recalling.includes('description');
  const description =
    describes &&
    el(
      d,
      'div',
      {
        class: classes.help,
        id: group.descriptionId,
        'data-jfb-text': hookOf(group.key, 'description', group.recalling),
        hidden: group.description === undefined,
      },
      html(d, group.description),
    );
  const fields = el(
    d,
    'div',
    { class: 'jfb-fields' },
    group.fields.map((field) => writeField(context, classes, field)),
  );
  if (group.title !== undefined || group.recalling.includes('title')) {
    return el(
      d,
      'fieldset',
      {
        class: group.cssClass,
        'data-jfb-group': group.key,
        hidden: group.isHidden,
        'data-jfb-hidden': flag(group.isHidden),
        'aria-describedby': describes ? group.descriptionId : null,
      },
      el(
        d,
        'legend',
        { 'data-jfb-text': hookOf(group.key, 'title', group.recalling), hidden: group.title === undefined },
        group.title,
      ),
      description,
      fields,
    );
  }
  return el(
    d,
    'div',
    {
      class: group.cssClass,
      'data-jfb-group': group.key,
      hidden: group.isHidden,
      'data-jfb-hidden': flag(group.isHidden),
    },
    description,
    fields,
  );
}

/**
 * `Field.cshtml`: the wrapper of every field, with its label or legend, its help, its control and its error. The
 * designer's canvas draws each field with it too, from a view model it builds from the draft (ADR-0168).
 */
export function writeField(context: ViewContext, classes: ThemeClassesModel, field: FieldViewModel): HTMLElement {
  const d = context.document;
  const labelClass = field.hideLabel ? `${classes.label} jfb-visually-hidden` : classes.label;
  const help =
    (field.help !== undefined || field.recalling.includes('help')) &&
    el(
      d,
      'p',
      {
        class: classes.help,
        id: field.helpId,
        'data-jfb-text': hookOf(field.key, 'help', field.recalling),
        hidden: field.help === undefined,
      },
      field.help,
    );
  const error =
    field.error !== undefined &&
    el(d, 'p', { class: classes.error, id: field.errorId, 'data-jfb-error': FLAG }, field.error);
  const label = (): Child => [labelText(d, field.key, field.label, field.recalling.includes('label')), mark(d, field)];
  const control = writeControl(context, classes, field);
  const wrapper = el(d, 'div', {
    class: field.cssClass,
    'data-jfb-field': field.key,
    'data-jfb-alias': field.alias,
    'data-jfb-type': field.type,
    hidden: field.isHidden,
    'data-jfb-hidden': flag(field.isHidden),
    'data-jfb-default': flag(field.fillsDefault),
  });
  switch (field.labelElement) {
    case 'legend':
      append(wrapper, [
        el(
          d,
          'fieldset',
          { 'aria-describedby': field.describedBy },
          el(d, 'legend', { class: labelClass }, label()),
          help,
          control,
          error,
        ),
      ]);
      break;
    case 'choice':
      append(wrapper, [control, help, error]);
      break;
    default:
      append(wrapper, [
        field.labelElement === 'label' && el(d, 'label', { class: labelClass, for: field.id }, label()),
        help,
        control,
        error,
      ]);
  }
  return wrapper;
}

// The attributes every input of a field shares, in the order the views write them.
const invalid = (field: FieldViewModel): string | null => (field.error === undefined ? null : 'true');
const shown = (value: string): string | null => (value.length === 0 ? null : value);

/** A field's control, as the view `fields/<type>` writes it. */
function writeControl(context: ViewContext, classes: ThemeClassesModel, field: FieldViewModel): Child {
  const d = context.document;
  const control = field.control;
  const placeholder = hookOf(field.key, 'placeholder', field.recalling);
  switch (field.type) {
    case 'text':
    case 'phone':
    case 'url': {
      if (control?.kind !== 'text') break;
      const input = el(d, 'input', {
        class: classes.control,
        type: control.inputType,
        id: field.id,
        name: field.name,
        value: shown(field.value),
        placeholder: field.placeholder,
        'data-jfb-text': placeholder,
        autocomplete: field.autocomplete,
        spellcheck: control.spellcheck === undefined ? null : String(control.spellcheck),
        required: field.isRequired,
        'data-jfb-requires': flag(field.hooks),
        disabled: field.isDisabled,
        'aria-invalid': invalid(field),
        'aria-describedby': field.describedBy,
      });
      if (field.type !== 'text') return input;
      return [
        affixed(d, classes, input, control.prefix, control.suffix),
        control.counter && counterOf(d, classes, control.counter),
      ];
    }
    case 'email': {
      if (control?.kind !== 'text') break;
      const confirm = control.confirm;
      return [
        el(d, 'input', {
          class: classes.control,
          type: 'email',
          id: field.id,
          name: field.name,
          value: shown(field.value),
          placeholder: field.placeholder,
          'data-jfb-text': placeholder,
          autocomplete: field.autocomplete,
          spellcheck: 'false',
          required: field.isRequired,
          'data-jfb-requires': flag(field.hooks),
          disabled: field.isDisabled,
          'aria-invalid': invalid(field),
          'aria-describedby': field.describedBy,
        }),
        confirm && [
          el(d, 'label', { class: classes.label, for: confirm.id }, confirm.label, mark(d, field)),
          el(d, 'input', {
            class: classes.control,
            type: 'email',
            id: confirm.id,
            name: confirm.name,
            value: shown(confirm.value),
            autocomplete: field.autocomplete,
            spellcheck: 'false',
            required: field.isRequired,
            'data-jfb-requires': flag(field.hooks),
            disabled: field.isDisabled,
            'aria-invalid': invalid(field),
            'aria-describedby': field.error === undefined ? null : field.errorId,
          }),
        ],
      ];
    }
    case 'textarea': {
      if (control?.kind !== 'textArea') break;
      const area = el(d, 'textarea', {
        class: classes.control,
        id: field.id,
        name: field.name,
        rows: control.rows,
        'data-jfb-grow': flag(control.autoGrow),
        placeholder: field.placeholder,
        'data-jfb-text': placeholder,
        autocomplete: field.autocomplete,
        required: field.isRequired,
        'data-jfb-requires': flag(field.hooks),
        disabled: field.isDisabled,
        'aria-invalid': invalid(field),
        'aria-describedby': field.describedBy,
      });
      area.textContent = field.value;
      return [area, control.counter && counterOf(d, classes, control.counter)];
    }
    case 'number': {
      if (control?.kind !== 'number') break;
      const input = el(d, 'input', {
        class: classes.control,
        type: control.inputType,
        id: field.id,
        name: field.name,
        value: shown(field.value),
        inputmode: control.inputMode,
        min: control.min,
        max: control.max,
        step: control.step,
        placeholder: field.placeholder,
        'data-jfb-text': placeholder,
        autocomplete: field.autocomplete,
        required: field.isRequired,
        'data-jfb-requires': flag(field.hooks),
        disabled: field.isDisabled,
        'aria-invalid': invalid(field),
        'aria-describedby': field.describedBy,
      });
      return affixed(d, classes, input, control.prefix, control.suffix);
    }
    case 'date':
    case 'datetime':
    case 'time':
      if (control?.kind !== 'date') break;
      return el(d, 'input', {
        class: classes.control,
        type: control.inputType,
        id: field.id,
        name: field.name,
        value: shown(field.value),
        min: control.min,
        max: control.max,
        step: control.step,
        autocomplete: field.autocomplete,
        required: field.isRequired,
        'data-jfb-requires': flag(field.hooks),
        disabled: field.isDisabled,
        'aria-invalid': invalid(field),
        'aria-describedby': field.describedBy,
      });
    case 'heading': {
      if (control?.kind !== 'heading') break;
      const tag = control.level === 3 ? 'h3' : control.level === 4 ? 'h4' : 'h2';
      return el(
        d,
        tag,
        {
          class: 'jfb-heading',
          'data-jfb-text': hookOf(field.key, 'settings.text', field.recalling),
          hidden: control.text.length === 0,
        },
        control.text,
      );
    }
    case 'paragraph':
      if (control?.kind !== 'textBlock') break;
      return el(
        d,
        'div',
        {
          class: 'jfb-text',
          'data-jfb-text': hookOf(field.key, 'settings.html', field.recalling),
          hidden: control.isEmpty,
        },
        html(d, control.html),
      );
    case 'divider':
      return el(d, 'hr', { class: 'jfb-divider' });
    case 'image':
      if (control?.kind !== 'image') break;
      if (control.url === undefined) return null;
      return el(
        d,
        'figure',
        { class: `jfb-image jfb-image-${control.size}` },
        el(d, 'img', {
          src: control.url,
          alt: control.alt,
          width: control.width,
          height: control.height,
          loading: 'lazy',
          decoding: 'async',
        }),
      );
    case 'calculation':
      if (control?.kind !== 'calculation') break;
      return el(
        d,
        'output',
        {
          class: 'jfb-output',
          id: field.id,
          name: field.name,
          'aria-live': 'off',
          'aria-describedby': field.describedBy,
        },
        control.text,
      );
    case 'captcha':
      if (control?.kind !== 'captcha') break;
      return el(d, 'div', { class: 'jfb-captcha', id: field.id, 'data-jfb-captcha': control.provider });
    case 'radio':
    case 'checkboxes':
      if (control?.kind !== 'choices') break;
      return choices(d, classes, field, control, field.type === 'radio');
    case 'yesno':
      if (control?.kind === 'singleChoice') {
        return el(
          d,
          'div',
          { class: classes.switchChoice },
          el(d, 'input', {
            type: 'checkbox',
            role: 'switch',
            class: classes.switchInput,
            id: field.id,
            name: field.name,
            value: control.value,
            checked: control.isChecked,
            disabled: field.isDisabled,
            'aria-invalid': invalid(field),
            'aria-describedby': field.describedBy,
          }),
          el(d, 'label', { class: classes.choiceLabel, for: field.id }, html(d, control.label)),
        );
      }
      if (control?.kind !== 'choices') break;
      return el(
        d,
        'div',
        { class: control.cssClass },
        control.choices.map((choice) =>
          el(
            d,
            'div',
            { class: classes.choice },
            radio(d, classes, field, choice),
            el(d, 'label', { class: classes.choiceLabel, for: choice.id }, choice.label),
          ),
        ),
      );
    case 'scale':
      if (control?.kind !== 'choices') break;
      return el(
        d,
        'div',
        { class: 'jfb-scale' },
        el(
          d,
          'div',
          { class: control.cssClass },
          control.choices.map((choice) =>
            el(
              d,
              'div',
              { class: classes.choice },
              radio(d, classes, field, choice),
              choice.hiddenText !== undefined
                ? el(
                    d,
                    'label',
                    { class: classes.choiceLabel, for: choice.id },
                    el(d, 'span', { 'aria-hidden': 'true' }, choice.label),
                    el(d, 'span', { class: 'jfb-visually-hidden' }, choice.label, choice.hiddenText),
                  )
                : el(d, 'label', { class: classes.choiceLabel, for: choice.id }, choice.label),
            ),
          ),
        ),
        control.ends && ends(d, control.ends.low, control.ends.high),
      );
    case 'rating': {
      if (control?.kind !== 'choices') break;
      const points = el(
        d,
        'div',
        { class: control.cssClass },
        control.choices.map((choice) =>
          el(
            d,
            'div',
            { class: classes.choice },
            radio(d, classes, field, choice),
            choice.hiddenText !== undefined
              ? el(
                  d,
                  'label',
                  { class: classes.choiceLabel, for: choice.id },
                  el(d, 'span', { class: 'jfb-rating-value', 'aria-hidden': 'true' }, choice.label),
                  el(d, 'span', { class: 'jfb-visually-hidden' }, choice.label, choice.hiddenText),
                )
              : el(
                  d,
                  'label',
                  { class: classes.choiceLabel, for: choice.id },
                  el(d, 'span', { class: 'jfb-rating-value' }, choice.label),
                ),
          ),
        ),
      );
      return control.ends
        ? el(d, 'div', { class: 'jfb-scale' }, points, ends(d, control.ends.low, control.ends.high))
        : points;
    }
    case 'dropdown':
      if (control?.kind !== 'select') break;
      return dropdown(d, classes, field, control);
    case 'country':
      if (control?.kind !== 'select') break;
      return el(
        d,
        'div',
        { class: 'jfb-select' },
        el(
          d,
          'select',
          {
            class: classes.select,
            id: field.id,
            name: field.name,
            autocomplete: field.autocomplete,
            required: field.isRequired,
            'data-jfb-requires': flag(field.hooks),
            disabled: field.isDisabled,
            'aria-invalid': invalid(field),
            'aria-describedby': field.describedBy,
          },
          el(d, 'option', { value: '' }, control.prompt),
          control.options.map((option) =>
            el(d, 'option', { value: option.value, selected: option.isSelected }, option.label),
          ),
        ),
      );
    case 'grid': {
      if (control?.kind !== 'grid') break;
      if (control.empty !== undefined) return el(d, 'p', { class: classes.help, id: field.id }, control.empty);
      return el(
        d,
        'div',
        { class: 'jfb-matrix' },
        el(
          d,
          'div',
          { class: 'jfb-matrix-head', 'aria-hidden': 'true' },
          el(d, 'span'),
          el(
            d,
            'div',
            {},
            control.columns.map((column) => el(d, 'span', {}, column)),
          ),
        ),
        control.rows.map((row) =>
          el(
            d,
            'fieldset',
            {},
            el(d, 'legend', {}, row.label, mark(d, row)),
            el(
              d,
              'div',
              { class: classes.choices },
              row.choices.map((choice) =>
                el(
                  d,
                  'div',
                  { class: classes.choice },
                  el(d, 'input', {
                    class: classes.choiceInput,
                    type: control.inputType,
                    id: choice.id,
                    name: row.name,
                    value: choice.value,
                    checked: choice.isChecked,
                    required: row.isRequired && control.inputType === 'radio',
                    'data-jfb-requires': flag(row.followsRequired),
                    disabled: field.isDisabled,
                    'aria-invalid': row.errorId === undefined ? null : 'true',
                    'aria-describedby': row.errorId,
                  }),
                  el(d, 'label', { class: classes.choiceLabel, for: choice.id }, el(d, 'span', {}, choice.label)),
                ),
              ),
            ),
          ),
        ),
      );
    }
    case 'ranking': {
      if (control?.kind !== 'ranking') break;
      if (control.empty !== undefined) return el(d, 'p', { class: classes.help, id: field.id }, control.empty);
      const move = (button: RankingMoveModel, icon: string): HTMLElement =>
        el(
          d,
          'button',
          {
            type: 'submit',
            class: classes.buttonIcon,
            id: button.id,
            name: 'jfb-nav',
            value: button.value,
            'aria-disabled': button.isDisabled ? 'true' : null,
            disabled: field.isDisabled,
          },
          el(d, 'span', { class: `jfb-icon ${icon}`, 'aria-hidden': 'true' }),
          el(d, 'span', { class: 'jfb-visually-hidden' }, button.label),
        );
      return el(
        d,
        'ol',
        {
          class: 'jfb-ranking',
          role: 'list',
          'data-jfb-name': control.name,
          'data-jfb-ranked': control.ranked,
          'data-jfb-moved': control.moved,
        },
        control.items.map((item) =>
          el(
            d,
            'li',
            { id: item.id },
            el(d, 'span', {}, item.position === undefined ? '' : String(item.position)),
            el(d, 'span', {}, item.label),
            el(d, 'input', {
              type: 'hidden',
              name: item.order.name,
              value: item.order.value,
              disabled: field.isDisabled,
            }),
            item.input &&
              el(d, 'input', {
                type: 'hidden',
                name: item.input.name,
                value: item.input.value,
                disabled: field.isDisabled,
              }),
            move(item.up, 'jfb-icon-up'),
            move(item.down, 'jfb-icon-down'),
          ),
        ),
      );
    }
    case 'checkbox':
    case 'consent': {
      if (control?.kind !== 'singleChoice') break;
      return [
        el(
          d,
          'div',
          { class: classes.choice },
          el(d, 'input', {
            class: classes.choiceInput,
            type: 'checkbox',
            id: field.id,
            name: field.name,
            value: control.value,
            checked: control.isChecked,
            required: field.isRequired,
            'data-jfb-requires': flag(field.hooks),
            disabled: field.isDisabled,
            'aria-invalid': invalid(field),
            'aria-describedby': field.describedBy,
          }),
          el(d, 'label', { class: classes.choiceLabel, for: field.id }, html(d, control.label), mark(d, field)),
        ),
        field.type === 'consent' &&
          control.wording &&
          el(d, 'input', {
            type: 'hidden',
            name: control.wording.name,
            value: control.wording.value,
            disabled: field.isDisabled,
          }),
      ];
    }
    case 'slider':
      if (control?.kind !== 'slider') break;
      return el(
        d,
        'div',
        { class: 'jfb-range' },
        el(d, 'input', {
          class: classes.range,
          type: 'range',
          id: field.id,
          name: field.name,
          min: control.min,
          max: control.max,
          step: control.step,
          value: control.value,
          'aria-valuetext': control.valueText,
          disabled: field.isDisabled,
          'aria-invalid': invalid(field),
          'aria-describedby': field.describedBy,
        }),
        control.shown !== undefined && el(d, 'output', { for: field.id, 'aria-hidden': 'true' }, control.shown),
      );
    case 'signature':
      if (control?.kind !== 'signature') break;
      return el(
        d,
        'div',
        { class: 'jfb-signature' },
        el(
          d,
          'div',
          { class: 'jfb-signature-way', 'data-jfb-drawn': flag(true) },
          el(d, 'p', { class: classes.help }, control.pad.help),
          el(d, 'canvas', {
            class: 'jfb-signature-pad',
            width: control.pad.width,
            height: control.pad.height,
            role: 'img',
            'aria-label': control.pad.drawn,
          }),
          el(
            d,
            'div',
            { class: 'jfb-signature-tools' },
            el(
              d,
              'button',
              {
                type: 'button',
                class: classes.buttonLink,
                disabled: field.isDisabled,
                'data-jfb-clear': flag(true),
                'data-jfb-undo': control.pad.undoClear,
              },
              control.pad.clear,
            ),
            el(
              d,
              'button',
              {
                type: 'button',
                class: classes.buttonLink,
                disabled: field.isDisabled,
                'data-jfb-to-typed': flag(true),
              },
              control.pad.typeInstead,
            ),
          ),
        ),
        el(
          d,
          'div',
          { class: 'jfb-signature-way', 'data-jfb-typed': flag(true) },
          el(d, 'p', { class: classes.help, id: control.helpId }, control.help),
          el(d, 'label', { class: 'jfb-visually-hidden', for: field.id }, control.label),
          el(d, 'input', {
            class: classes.control,
            id: field.id,
            name: control.name,
            value: shown(control.value),
            maxlength: control.maxLength,
            autocomplete: 'name',
            autocapitalize: 'words',
            spellcheck: 'false',
            required: field.isRequired,
            'data-jfb-requires': flag(field.hooks),
            disabled: field.isDisabled,
            'aria-invalid': invalid(field),
            'aria-describedby': control.helpId,
          }),
          el(
            d,
            'div',
            { class: 'jfb-signature-tools' },
            el(
              d,
              'button',
              {
                type: 'button',
                class: classes.buttonLink,
                disabled: field.isDisabled,
                'data-jfb-to-drawn': flag(true),
              },
              control.pad.byHand,
            ),
          ),
        ),
      );
    case 'name':
    case 'address':
      if (control?.kind !== 'parts') break;
      return parts(d, classes, field, control);
    case 'file':
      if (control?.kind !== 'file') break;
      return [
        control.kept.length > 0 &&
          el(
            d,
            'div',
            { class: `${classes.choices} jfb-files` },
            control.kept.map((kept) =>
              el(
                d,
                'div',
                { class: classes.choice },
                el(d, 'input', {
                  class: classes.choiceInput,
                  type: 'checkbox',
                  id: kept.id,
                  name: field.name,
                  value: kept.token,
                  checked: FLAG,
                  disabled: field.isDisabled,
                }),
                el(d, 'label', { class: classes.choiceLabel, for: kept.id }, kept.label),
              ),
            ),
          ),
        el(
          d,
          'div',
          {
            class: 'jfb-upload',
            'data-jfb-dropzone': flag(control.dropZone),
            'data-jfb-thumbnails': flag(control.thumbnails),
          },
          el(d, 'span', { class: 'jfb-icon jfb-icon-upload', 'aria-hidden': 'true' }),
          control.upload.drag !== undefined && el(d, 'p', {}, control.upload.drag),
          el(
            d,
            'div',
            { class: 'jfb-upload-actions' },
            el(d, 'input', {
              class: classes.fileInput,
              type: 'file',
              id: field.id,
              name: field.name,
              accept: control.accept,
              multiple: control.multiple,
              required: field.isRequired,
              'data-jfb-requires': flag(field.hooks),
              disabled: field.isDisabled,
              'aria-invalid': invalid(field),
              'aria-describedby': field.describedBy,
            }),
            el(d, 'label', { class: classes.button, for: field.id }, control.upload.choose),
            control.camera && [
              el(d, 'input', {
                type: 'file',
                class: 'jfb-upload-camera',
                id: control.camera.id,
                accept: 'image/*',
                capture: 'environment',
                disabled: field.isDisabled,
                'aria-describedby': field.describedBy,
              }),
              el(
                d,
                'label',
                { class: `${classes.button} jfb-upload-camera`, for: control.camera.id },
                control.camera.text,
              ),
            ],
          ),
        ),
        fileTemplate(d, classes, control.upload),
      ];
    case 'repeater':
      if (control?.kind !== 'repeater') break;
      return repeater(context, classes, field, control);
  }
  if (control?.kind === 'custom' || (control === undefined && !BUILT_IN.has(field.type))) {
    const write = context.control?.(field.type);
    if (write !== undefined) return write(field as CustomField, d, classes);
  }
  if (control !== undefined || !BUILT_IN.has(field.type)) context.onCustom?.(field.type);
  return null;
}

// The built-in types whose view writes no control or nothing at all where the model has none.
const BUILT_IN = new Set(['divider', 'image', 'hidden']);

/** A counter's line with what the runtime says as the text changes (ADR-0154). */
const counterOf = (d: Document, classes: ThemeClassesModel, counter: CounterModel): HTMLElement =>
  el(
    d,
    'p',
    {
      class: classes.counter,
      id: counter.id,
      'data-jfb-limit': counter.limit,
      'data-jfb-left': counter.left,
      'data-jfb-left-one': counter.leftOne,
      'data-jfb-too-many': counter.over,
      'data-jfb-too-many-one': counter.overOne,
    },
    counter.text,
  );

/** An input with its prefix and its suffix, as the text and number views write them. */
function affixed(
  d: Document,
  classes: ThemeClassesModel,
  input: HTMLElement,
  prefix: { text: string; id: string } | undefined,
  suffix: { text: string; id: string } | undefined,
): Child {
  if (prefix === undefined && suffix === undefined) return input;
  const text = (affix: { text: string; id: string }): HTMLElement =>
    el(d, 'span', { class: classes.affixText, id: affix.id }, affix.text);
  return el(d, 'div', { class: classes.affix }, prefix && text(prefix), input, suffix && text(suffix));
}

/** A radio button of a group of choices, as the choice views write it. */
const radio = (d: Document, classes: ThemeClassesModel, field: FieldViewModel, choice: ChoiceModel): HTMLElement =>
  el(d, 'input', {
    class: classes.choiceInput,
    type: 'radio',
    id: choice.id,
    name: field.name,
    value: choice.value,
    checked: choice.isChecked,
    required: field.isRequired,
    'data-jfb-requires': flag(field.hooks),
    disabled: field.isDisabled,
    ...choiceMarks(field),
  });

/** A refused group of choices' marks on each choice, beside the ids it names already (6.8c). */
const choiceMarks = (field: FieldViewModel, ...ids: (string | null)[]): Record<string, string | null> => {
  const named = [...ids, field.choiceErrorId ?? null].filter((id) => id !== null);
  return {
    'aria-invalid': field.choiceErrorId === undefined ? null : 'true',
    'aria-describedby': named.length === 0 ? null : named.join(' '),
  };
};

/** The words at a scale's ends, hidden from assistive technology. */
const ends = (d: Document, low: string, high: string): HTMLElement =>
  el(d, 'div', { class: 'jfb-ends', 'aria-hidden': 'true' }, el(d, 'span', {}, low), ' ', el(d, 'span', {}, high));

/** `fields/radio` and `fields/checkboxes`: the choices, with their images and descriptions, and the choice of one's own. */
function choices(
  d: Document,
  classes: ThemeClassesModel,
  field: FieldViewModel,
  control: ChoicesControl,
  single: boolean,
): Child {
  const choice = (each: ChoiceModel, sample = false): HTMLElement =>
    el(
      d,
      'div',
      { class: classes.choice, 'data-jfb-sample': flag(sample) },
      each.image && el(d, 'img', { src: each.image.url, alt: '', width: each.image.width, height: each.image.height }),
      el(d, 'input', {
        class: classes.choiceInput,
        type: single ? 'radio' : 'checkbox',
        id: each.id,
        name: field.name,
        value: each.value,
        checked: each.isChecked,
        required: single && field.isRequired,
        'data-jfb-requires': flag(single && field.hooks),
        disabled: field.isDisabled,
        ...choiceMarks(field, each.description === undefined ? null : each.descriptionId),
      }),
      el(d, 'label', { class: classes.choiceLabel, for: each.id }, each.label),
      each.description !== undefined && el(d, 'p', { class: classes.help, id: each.descriptionId }, each.description),
    );
  const other = control.other;
  const otherOf = (given: OtherChoiceModel, text: string | null): Child => [
    choice(given.choice),
    el(
      d,
      'div',
      { class: 'jfb-other' },
      el(d, 'label', { class: 'jfb-visually-hidden', for: given.textId }, given.textLabel),
      el(d, 'input', {
        class: classes.control,
        id: given.textId,
        name: given.textName,
        value: text,
        disabled: field.isDisabled,
      }),
    ),
  ];
  const cascade = control.cascade;
  let template: HTMLTemplateElement | undefined;
  if (cascade !== undefined) {
    template = el(d, 'template', {
      'data-jfb-cascade': flag(true),
      'data-jfb-none': cascade.none,
      'data-jfb-unavailable': cascade.unavailable,
    }) as HTMLTemplateElement;
    template.content.append(
      el(d, 'div', { class: control.cssClass }, choice(cascade.sample, true), other && otherOf(other, null)),
      el(d, 'p', { class: classes.help, id: field.id }),
    );
  }
  return [
    control.empty !== undefined
      ? el(d, 'p', { class: classes.help, id: field.id }, control.empty)
      : el(
          d,
          'div',
          { class: control.cssClass },
          control.choices.map((each) => choice(each)),
          other && otherOf(other, shown(other.text)),
        ),
    template,
  ];
}

/** `fields/dropdown`: a dropdown in its wrapper for the theme's chevron, or a list box with how to choose several. */
function dropdown(d: Document, classes: ThemeClassesModel, field: FieldViewModel, control: SelectControl): Child {
  const select = el(
    d,
    'select',
    {
      class: classes.select,
      id: field.id,
      name: field.name,
      multiple: control.multiple,
      size: control.size,
      'data-jfb-cascade': flag(control.cascades === true),
      'data-jfb-prompt': control.cascades === true ? control.prompt : null,
      'data-jfb-none': control.cascades === true ? control.none : null,
      'data-jfb-unavailable': control.cascades === true ? control.unavailable : null,
      autocomplete: field.autocomplete,
      required: field.isRequired,
      'data-jfb-requires': flag(field.hooks),
      disabled: field.isDisabled || control.empty !== undefined,
      'data-jfb-disabled': flag(field.hooks && control.empty !== undefined),
      'aria-invalid': invalid(field),
      'aria-describedby': field.describedBy,
    },
    control.empty !== undefined
      ? el(d, 'option', { value: '' }, control.empty)
      : [
          control.prompt !== undefined && el(d, 'option', { value: '' }, control.prompt),
          control.options.map((option) =>
            el(d, 'option', { value: option.value, selected: option.isSelected }, option.label),
          ),
        ],
  );
  if (control.multiple)
    return [control.hint && el(d, 'p', { class: classes.help, id: control.hint.id }, control.hint.text), select];
  return el(d, 'div', { class: 'jfb-select' }, select);
}

/** `fields/name` and `fields/address`: each part with its own label and input, a country as a dropdown. */
function parts(d: Document, classes: ThemeClassesModel, field: FieldViewModel, control: PartsControl): HTMLElement {
  return el(
    d,
    'div',
    { class: 'jfb-parts' },
    control.parts.map((part) =>
      el(
        d,
        'div',
        { class: part.isWide ? 'jfb-part jfb-part-wide' : 'jfb-part' },
        el(d, 'label', { class: classes.partLabel, for: part.id }, part.label, mark(d, part)),
        part.countries
          ? el(
              d,
              'div',
              { class: 'jfb-select' },
              el(
                d,
                'select',
                {
                  class: classes.select,
                  id: part.id,
                  name: part.name,
                  autocomplete: part.autocomplete,
                  required: part.isRequired,
                  'data-jfb-requires': flag(part.followsRequired),
                  disabled: field.isDisabled,
                  'aria-invalid': part.isInvalid ? 'true' : null,
                },
                el(d, 'option', { value: '' }, part.countries.prompt),
                part.countries.options.map((option) =>
                  el(d, 'option', { value: option.value, selected: option.isSelected }, option.label),
                ),
              ),
            )
          : el(d, 'input', {
              class: classes.control,
              id: part.id,
              name: part.name,
              value: shown(part.value),
              autocomplete: part.autocomplete,
              autocapitalize: part.part === 'postcode' ? null : 'words',
              spellcheck: 'false',
              required: part.isRequired,
              'data-jfb-requires': flag(part.followsRequired),
              disabled: field.isDisabled,
              'aria-invalid': part.isInvalid ? 'true' : null,
            }),
      ),
    ),
  );
}

/** `fields/repeater`: the rows, each a fieldset with its legend, its Remove button and its fields, and Add. */
function repeater(
  context: ViewContext,
  classes: ThemeClassesModel,
  field: FieldViewModel,
  control: RepeaterControl,
): HTMLElement {
  const d = context.document;
  const remove = (button: RowButtonModel): HTMLElement =>
    el(
      d,
      'button',
      {
        type: 'submit',
        class: classes.buttonLink,
        name: 'jfb-nav',
        value: button.value,
        disabled: field.isDisabled,
        hidden: button.isHidden,
      },
      button.text,
      button.context !== undefined && el(d, 'span', { class: 'jfb-visually-hidden' }, ` ${button.context}`),
    );
  const rowOf = (row: RepeaterRowModel): HTMLElement =>
    el(
      d,
      'fieldset',
      { class: 'jfb-row', id: row.id, 'data-jfb-row': row.index },
      el(d, 'legend', {}, row.legend),
      remove(row.remove),
      el(
        d,
        'div',
        { class: control.fieldsCssClass },
        row.fields.map((child) => writeField(context, classes, child)),
      ),
    );
  const template = d.createElement('template');
  template.content.append(rowOf(control.template));
  return el(
    d,
    'div',
    {
      class: 'jfb-repeater',
      id: field.id,
      tabindex: '-1',
      'data-jfb-least': control.least,
      'data-jfb-most': control.most,
      'data-jfb-legend': control.legend,
      'data-jfb-removed': control.removed,
    },
    el(d, 'input', {
      type: 'hidden',
      name: control.rowCount.name,
      value: control.rowCount.value,
      disabled: field.isDisabled,
    }),
    control.rows.map(rowOf),
    template,
    el(
      d,
      'button',
      {
        type: 'submit',
        class: classes.button,
        name: 'jfb-nav',
        value: control.add.value,
        disabled: field.isDisabled,
        hidden: control.add.isHidden,
      },
      el(d, 'span', { class: 'jfb-icon jfb-icon-add', 'aria-hidden': 'true' }),
      control.add.text,
    ),
  );
}

/** `Progress.cshtml`: the bar with its text, or the steps, the current one marked. */
function progress(d: Document, classes: ThemeClassesModel, model: ProgressViewModel): HTMLElement {
  const stepOf = (step: ProgressViewModel['steps'][number]): HTMLElement =>
    el(
      d,
      'li',
      {
        'data-jfb-step': step.page,
        'aria-current': step.isCurrent ? 'step' : null,
        'data-jfb-done': flag(step.isDone),
      },
      el(
        d,
        'span',
        {
          ...(step.recallsTitle ? { 'data-jfb-text': `${step.page} title`, 'data-jfb-fallback': step.fallback } : {}),
          'data-jfb-numbered': flag(step.isUntitled === true),
        },
        step.title,
      ),
    );
  const hidden = model.steps.filter((step) => step.isHidden === true);
  let waiting: HTMLTemplateElement | undefined;
  if (hidden.length > 0) {
    waiting = el(d, 'template', { 'data-jfb-steps': FLAG }) as HTMLTemplateElement;
    waiting.content.append(...hidden.map(stepOf));
  }
  if (model.style === 'bar') {
    return el(
      d,
      'div',
      { class: classes.progressBar, hidden: flag(model.isHidden === true) },
      el(d, 'p', { class: 'jfb-progress-text', id: model.textId, 'data-jfb-words': model.words }, model.text),
      el(d, 'progress', { max: model.total, value: model.current, 'aria-labelledby': model.textId }, model.text),
    );
  }
  return el(
    d,
    'div',
    { class: classes.progressSteps, hidden: flag(model.isHidden === true) },
    el(d, 'p', { class: 'jfb-progress-text', 'data-jfb-words': model.words }, model.text),
    el(
      d,
      'ol',
      { role: 'list', 'aria-label': model.label, 'data-jfb-untitled': model.untitledWords },
      model.steps.filter((step) => step.isHidden !== true).map(stepOf),
    ),
    waiting,
  );
}

/** `Notice.cshtml`: a notice after a post is an alert named by its heading; another is its heading and its text. */
function notice(d: Document, model: NoticeViewModel): HTMLElement {
  if (model.code !== undefined) {
    return el(
      d,
      'div',
      {
        class: model.cssClass,
        'data-jfb-notice': model.code,
        role: 'alert',
        tabindex: '-1',
        'aria-labelledby': model.headingId,
      },
      el(d, 'h2', { id: model.headingId }, model.heading),
      html(d, model.text),
    );
  }
  return el(
    d,
    'div',
    { class: model.cssClass },
    model.heading !== undefined && el(d, 'h2', {}, model.heading),
    html(d, model.text),
  );
}

/** The template of `fields/file.cshtml`: the list, a file's row and the words of its states (ADR-0152). */
function fileTemplate(d: Document, classes: ThemeClassesModel, words: FileUploadWordsModel): HTMLElement {
  const template = el(d, 'template', {
    'data-jfb-file': FLAG,
    'data-jfb-list': words.list,
    'data-jfb-uploading': words.uploading,
    'data-jfb-uploaded': words.uploaded,
    'data-jfb-remove': words.remove,
    'data-jfb-stop': words.stop,
    'data-jfb-again': words.tryAgainFor,
    'data-jfb-stopped': words.stopped,
    'data-jfb-later': words.later,
    'data-jfb-kb': words.kilobytes,
    'data-jfb-mb': words.megabytes,
  }) as HTMLTemplateElement;
  append(template.content, [
    el(d, 'ul', { class: 'jfb-files', 'aria-label': words.list }),
    el(
      d,
      'li',
      { class: 'jfb-file' },
      el(d, 'span', { class: 'jfb-icon jfb-icon-file', 'aria-hidden': 'true' }),
      el(d, 'strong', {}),
      el(d, 'p', { class: classes.help }),
      el(d, 'p', { class: classes.error }),
      el(d, 'progress', { max: '100', value: '0', 'aria-hidden': 'true' }),
      el(
        d,
        'button',
        { type: 'button', class: classes.buttonLink, 'data-jfb-retry': FLAG },
        words.tryAgain,
        el(d, 'span', { class: 'jfb-visually-hidden' }),
      ),
      el(
        d,
        'button',
        { type: 'button', class: classes.buttonIcon, 'data-jfb-drop-file': FLAG },
        el(d, 'span', { class: 'jfb-icon jfb-icon-remove', 'aria-hidden': 'true' }),
        el(d, 'span', { class: 'jfb-visually-hidden' }),
      ),
    ),
  ]);
  return template;
}

/**
 * `Outcome.cshtml`, as the form's templates hold it: without its message, for the runtime to fill in after a send. The
 * designer shows a form's outcome after sending with it, its message in place (ADR-0168).
 */
export function writeOutcome(d: Document, classes: ThemeClassesModel, model: OutcomeViewModel): HTMLElement {
  return el(
    d,
    'div',
    { class: classes.outcome, 'data-jfb-outcome': FLAG, 'data-jfb-pending': flag(model.isPending), tabindex: '-1' },
    html(d, model.message),
    model.reference !== undefined &&
      el(d, 'p', { class: 'jfb-reference', 'data-jfb-reference': FLAG }, html(d, model.reference)),
    model.countdown &&
      el(d, 'p', {
        'data-jfb-countdown': FLAG,
        'data-jfb-one': model.countdown.one,
        'data-jfb-other': model.countdown.other,
      }),
    model.continue &&
      el(
        d,
        'div',
        { class: classes.navigation },
        el(d, 'a', { class: classes.button, href: model.continue.url, 'data-jfb-continue': FLAG }, model.continue.text),
        model.countdown &&
          el(d, 'button', { type: 'button', class: classes.buttonLink, 'data-jfb-stay': FLAG }, model.countdown.stay),
      ),
  );
}

/** `ErrorSummary.cshtml`: the alert with its heading and a link to each error's control. */
function errorSummary(d: Document, classes: ThemeClassesModel, model: ErrorSummaryViewModel): HTMLElement {
  return el(
    d,
    'div',
    {
      class: classes.errorSummary,
      'data-jfb-summary': FLAG,
      role: 'alert',
      tabindex: '-1',
      'aria-labelledby': model.headingId,
    },
    el(d, 'h2', { id: model.headingId }, model.heading),
    el(
      d,
      'ul',
      { role: 'list' },
      model.errors.map((error) => el(d, 'li', {}, el(d, 'a', { href: `#${error.target}` }, error.message))),
    ),
  );
}

/**
 * `Navigation.cshtml`: Next or Send first, so Enter in a field means it, then Back with its icon. The designer's canvas
 * ends each page with it (ADR-0168).
 */
export function writeNavigation(d: Document, classes: ThemeClassesModel, model: NavigationViewModel): HTMLElement {
  const button = (each: { label: string; value: string }, primary: boolean): HTMLElement =>
    el(
      d,
      'button',
      {
        type: 'submit',
        class: primary ? classes.buttonPrimary : classes.button,
        name: 'jfb-nav',
        value: each.value,
        'data-jfb-nav': each.value,
      },
      !primary && el(d, 'span', { class: 'jfb-icon jfb-icon-back', 'aria-hidden': 'true' }),
      each.label,
    );
  return el(
    d,
    'div',
    { class: classes.navigation },
    model.next && button(model.next, true),
    model.submit && button(model.submit, true),
    model.back && button(model.back, false),
  );
}

/** The honeypot, out of view and out of the accessibility tree (ADR-0012). */
const honeypot = (d: Document, model: HoneypotModel): HTMLElement =>
  el(
    d,
    'div',
    { class: 'jfb-hp', 'aria-hidden': 'true' },
    el(d, 'label', { for: model.id }, model.label),
    el(d, 'input', { type: 'text', id: model.id, name: model.name, tabindex: '-1', autocomplete: 'off' }),
  );

/**
 * `Form.cshtml`'s template of save and continue later (ADR-0163): the link button the runtime puts with the form's
 * buttons and the panel it shows in their place, with what the saving says, which the runtime reads from the template.
 */
function writeResume(d: Document, classes: ThemeClassesModel, id: string, model: FormResumeModel): HTMLTemplateElement {
  const template = el(d, 'template', {
    'data-jfb-resume': FLAG,
    'data-jfb-saving': model.savingText,
    'data-jfb-invalid': model.invalidText,
    'data-jfb-failed': model.failedText,
  }) as HTMLTemplateElement;
  append(template.content, [
    el(d, 'button', { type: 'button', class: classes.buttonLink, 'data-jfb-save-open': FLAG }, model.openLabel),
    el(
      d,
      'div',
      { class: model.panelClass, role: 'group', 'aria-labelledby': `${id}-save-title`, 'data-jfb-save': FLAG },
      el(d, 'h2', { id: `${id}-save-title` }, model.openLabel),
      el(d, 'p', {}, model.intro),
      el(
        d,
        'div',
        { class: model.fieldClass },
        el(d, 'label', { class: classes.label, for: `${id}-save` }, model.emailLabel),
        el(d, 'input', {
          class: classes.control,
          type: 'email',
          id: `${id}-save`,
          autocomplete: 'email',
          spellcheck: 'false',
          'aria-describedby': `${id}-save-error`,
        }),
        el(d, 'p', { class: classes.error, id: `${id}-save-error`, hidden: FLAG }),
      ),
      el(
        d,
        'div',
        { class: classes.navigation },
        el(d, 'button', { type: 'button', class: classes.buttonPrimary, 'data-jfb-save-send': FLAG }, model.sendLabel),
        el(d, 'button', { type: 'button', class: classes.button, 'data-jfb-save-close': FLAG }, model.cancelLabel),
      ),
    ),
  ]);
  return template;
}
