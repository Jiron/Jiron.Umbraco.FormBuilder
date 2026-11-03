// A draft's fields as the designer draws them (ADR-0168): the view model the server builds for a first render, built
// here from the draft, which the headless element's views then write as the markup visitors get. Each family of types
// has a small builder that follows the server's (FormModels and its parts) for what shows: the label and its mark, the
// help, the control with its words, choices, parts and rows. What only a visitor's answers, rules or the server can
// give (answers, errors, places, options from a source, a media item's address) is left out; the views draw the field
// as a first render without them does.
import type {
  ChoiceModel,
  ChoicesControl,
  CounterModel,
  FieldViewModel,
  GroupViewModel,
  PageViewModel,
  PartModel,
  RepeaterRowModel,
} from '@jfb/element/answer';
import type { FieldSettingResponseModel, FieldTypeResponseModel } from '../../api';
import type { DesignDefinition, DesignField, DesignGroup, DesignPage, LocalizedText } from '../definition';
import { word, type CanvasLook, type Marker } from './look';
import { escapeHtml, safeHtml, showsText } from './rich-text';

/** What a field's view model is built with besides the field. */
export interface PreviewContext {
  readonly look: CanvasLook;
  /** The field types by alias, with their settings' defaults. */
  readonly types: Readonly<Record<string, FieldTypeResponseModel>>;
  /** The culture whose texts show: the culture, its language, then the default text. */
  readonly culture: string;
  /** The form's marker, inherit taking the site's. */
  readonly marker: Marker;
  /** The form's layout of choices for a field whose own is inherit. */
  readonly layout: string;
  /** The form's captcha provider; the captcha's place shows only with one. */
  readonly captcha: string | null;
  /** What starts every id, so that two previews on a screen never share one. */
  readonly prefix: string;
  /** The address of a media item an image or a card shows, when it is known. */
  readonly mediaUrl?: (key: string) => string | undefined;
  /**
   * The options of a field that takes them from elsewhere, as the server resolved the draft's (ADR-0172); undefined
   * while they are read, or where nothing resolves them, and the field shows none.
   */
  readonly resolved?: (field: DesignField) => readonly ResolvedOption[] | undefined;
  /**
   * Whether a repeatable group shows a row even when a first render has none, as the canvas does, so that its fields
   * can be seen and chosen.
   */
  readonly showRows?: boolean;
}

/**
 * Which of a text's cultures is read in a culture, as the server resolves it: the culture itself, then its more general
 * cultures (`de-CH`, then `de`), then the default (`''`); any letter case. Another culture of the same language
 * (`de-DE` for `de-CH`) never stands in. A change made to what was read goes back there.
 */
export function keyIn(texts: LocalizedText | null | undefined, culture: string): string {
  if (!texts) return '';
  const keys = Object.keys(texts);
  for (let candidate = culture.toLowerCase(); candidate !== '';) {
    const found = keys.find((key) => key.toLowerCase() === candidate);
    if (found !== undefined) return found;
    const dash = candidate.lastIndexOf('-');
    candidate = dash < 0 ? '' : candidate.slice(0, dash);
  }
  return '';
}

/** A text in a culture: the culture itself, its more general cultures, then the default; any letter case. */
export function textIn(texts: LocalizedText | null | undefined, culture: string): string {
  return texts ? (texts[keyIn(texts, culture)] ?? '') : '';
}

/** A field's settings with the type's defaults and the host's locked values, read as the server reads them. */
class Settings {
  constructor(
    private readonly given: Readonly<Record<string, unknown>>,
    private readonly schema: readonly FieldSettingResponseModel[],
    private readonly culture: string,
  ) {}

  value(name: string): unknown {
    const declared = this.schema.find((setting) => setting.name === name);
    if (declared?.lock && declared.lock.value !== undefined && declared.lock.value !== null) return declared.lock.value;
    const own = this.given[name];
    return own === undefined || own === null ? declared?.default : own;
  }

  /** A text: as it is, a localised one in the culture, a number as its digits; none when empty. */
  text(name: string): string | undefined {
    const value = this.value(name);
    const text =
      typeof value === 'string'
        ? value
        : typeof value === 'number'
          ? String(value)
          : value && typeof value === 'object' && !Array.isArray(value)
            ? textIn(value as LocalizedText, this.culture)
            : '';
    return text.length > 0 ? text : undefined;
  }

  number(name: string, fallback: number): number {
    const value = Number(this.value(name));
    return this.value(name) === undefined || this.value(name) === null || !Number.isFinite(value) ? fallback : value;
  }

  bool(name: string, fallback = false): boolean {
    const value = this.value(name);
    return typeof value === 'boolean' ? value : fallback;
  }

  texts(name: string): string[] {
    const value = this.value(name);
    return Array.isArray(value) ? value.filter((item): item is string => typeof item === 'string') : [];
  }

  child(name: string): Settings | undefined {
    const value = this.value(name);
    if (!value || typeof value !== 'object' || Array.isArray(value)) return undefined;
    const declared = this.schema.find((setting) => setting.name === name);
    return new Settings(value as Record<string, unknown>, declared?.settings ?? [], this.culture);
  }
}

const clamp = (value: number, low: number, high: number) => Math.min(high, Math.max(low, Math.trunc(value)));
const number = (context: PreviewContext, value: number) => new Intl.NumberFormat(context.look.culture).format(value);

/** The texts around a value, as the server joins them: each one there, with a space between. */
const around = (prefix: string, text: string, suffix: string) =>
  [prefix.trim(), text, suffix.trim()].filter((piece) => piece.length > 0).join(' ');

// The types whose label is a legend, as their controls are a group of inputs.
const LEGEND = new Set([
  'radio',
  'checkboxes',
  'grid',
  'ranking',
  'scale',
  'rating',
  'name',
  'address',
  'signature',
  'repeater',
]);
const SPEAKS_MARK = new Set(['checkboxes', 'ranking']);

const DEFAULT_AUTOCOMPLETE: Readonly<Record<string, string>> = {
  email: 'email',
  phone: 'tel',
  url: 'url',
  country: 'country',
};

/**
 * A form's settings as a preview reads them: how it marks fields (inherit taking the site's way), lays out choices,
 * names its captcha, and labels its buttons.
 */
export function formSettings(definition: DesignDefinition, look: CanvasLook, culture: string) {
  const settings = (definition['settings'] ?? {}) as {
    appearance?: { requiredMarker?: string; choiceLayout?: string };
    protection?: { captcha?: { provider?: string } };
    submission?: { submitLabel?: LocalizedText; nextLabel?: LocalizedText; backLabel?: LocalizedText };
  };
  const own = settings.appearance?.requiredMarker;
  return {
    marker: (own === 'optional' || own === 'required' || own === 'none' ? own : look.marker) as Marker,
    layout: settings.appearance?.choiceLayout ?? 'vertical',
    captcha: settings.protection?.captcha?.provider ?? null,
    send: textIn(settings.submission?.submitLabel, culture),
    next: textIn(settings.submission?.nextLabel, culture),
    back: textIn(settings.submission?.backLabel, culture),
  };
}

/** The view model of a page as a first render shows it, its groups' fields built with the field's builder. */
export function pageModel(page: DesignPage, context: PreviewContext): PageViewModel {
  const title = textIn(page.title, context.culture);
  const description = safeHtml(textIn(page['description'] as LocalizedText | undefined, context.culture));
  return {
    key: page.key,
    ...(title ? { title } : {}),
    ...(showsText(description) ? { description } : {}),
    recalling: [],
    isCurrent: true,
    isHidden: false,
    groups: page.groups.map((group) => groupModel(group, context)),
  };
}

/** The view model of a group: its title, its text, its columns and style, and its fields. */
export function groupModel(group: DesignGroup, context: PreviewContext): GroupViewModel {
  const title = textIn(group.title, context.culture);
  const description = safeHtml(textIn(group['description'] as LocalizedText | undefined, context.culture));
  const columns = clamp(Number(group['columns'] ?? 1) || 1, 1, 4);
  return {
    key: group.key,
    ...(title ? { title } : {}),
    ...(showsText(description) ? { description } : {}),
    descriptionId: `${context.prefix}-${group.key}-help`,
    recalling: [],
    cssClass: groupClasses(group),
    isHidden: false,
    fields: group.fields.flatMap((field) => fieldModel(field, context, columns) ?? []),
  };
}

/** A group's classes, as the server writes them: its columns and its style. */
export function groupClasses(group: DesignGroup): string {
  const columns = clamp(Number(group['columns'] ?? 1) || 1, 1, 4);
  return ['jfb-group', `jfb-columns-${columns}`, group['style'] === 'card' ? 'jfb-group-card' : null]
    .filter(Boolean)
    .join(' ');
}

/**
 * Marks what the website's runtime takes over as taken over, so a drawing shows what visitors with JavaScript see
 * before they touch it: an upload as its drop frame, and a signature as its empty pad without its Clear.
 */
export function asEnhanced(root: ParentNode): void {
  for (const zone of root.querySelectorAll('.jfb-upload')) zone.setAttribute('data-jfb-enhanced', '');
  for (const box of root.querySelectorAll('.jfb-signature')) {
    box.setAttribute('data-jfb-enhanced', '');
    for (const part of box.querySelectorAll('[data-jfb-typed], [data-jfb-clear]')) part.setAttribute('hidden', '');
  }
}

/**
 * A field's view model, or null for a field a first render doesn't write: a hidden field, a calculation that isn't
 * shown, a captcha on a form without a provider, and a type the site no longer has. `columns` is its group's, which a
 * repeatable group's rows take.
 */
export function fieldModel(field: DesignField, context: PreviewContext, columns = 1): FieldViewModel | null {
  const type = Object.hasOwn(context.types, field.type) ? context.types[field.type] : undefined;
  if (!type) return null;
  const settings = new Settings(
    (field['settings'] as Record<string, unknown> | undefined) ?? {},
    type.settings,
    context.culture,
  );
  if (
    field.type === 'hidden' ||
    (field.type === 'calculation' && !settings.bool('visible', true)) ||
    (field.type === 'captcha' && context.captcha === null)
  )
    return null;

  const id = `${context.prefix}-${field.alias ?? field.key.replace(/-/g, '')}`;
  const name = field.alias ?? field.key;
  const required = field['required'] === true;
  const element = !type.holdsValue
    ? 'none'
    : LEGEND.has(field.type)
      ? 'legend'
      : field.type === 'checkbox' || field.type === 'consent'
        ? 'choice'
        : field.type === 'yesno'
          ? settings.text('style') === 'switch'
            ? 'choice'
            : 'legend'
          : 'label';
  const mark = markOf(field, settings, element, required, context);
  const label = type.holdsValue ? textIn(field.label, context.culture) : '';
  const help = textIn(field['help'] as LocalizedText | undefined, context.culture);
  const placeholder = textIn(field['placeholder'] as LocalizedText | undefined, context.culture);
  const span = clamp(Number(field['span'] ?? 1) || 1, 1, 4);
  const autocomplete = (field['autocomplete'] as string | undefined) ?? DEFAULT_AUTOCOMPLETE[field.type];
  const ownClass = typeof field['cssClass'] === 'string' ? field['cssClass'] : null;
  const shown: Shown = { field, settings, context, id, name, label, required, columns };

  return {
    key: field.key,
    type: field.type,
    ...(field.alias ? { alias: field.alias } : {}),
    id,
    name,
    cssClass: ['jfb-field', `jfb-field-${field.type}`, span > 1 ? `jfb-span-${span}` : null, ownClass]
      .filter(Boolean)
      .join(' '),
    labelElement: element,
    ...(label ? { label } : {}),
    hideLabel: field['hideLabel'] === true,
    mark,
    markText: mark === 'optional' ? word(context.look, 'optional') : mark === 'required' ? '*' : '',
    ...(mark === 'required' && (SPEAKS_MARK.has(field.type) || (field.type === 'grid' && settings.bool('multiple')))
      ? { markSpoken: word(context.look, 'required') }
      : {}),
    isMarkShown: mark !== 'none',
    hooks: false,
    ...(help ? { help } : {}),
    helpId: `${id}-help`,
    errorId: `${id}-error`,
    isRequired: required,
    isHidden: false,
    isDisabled: false,
    ...(placeholder ? { placeholder } : {}),
    recalling: [],
    ...(autocomplete ? { autocomplete } : {}),
    values: [],
    fillsDefault: false,
    value: '',
    ...controlOf(shown),
  };
}

/** What a field's builder reads. */
interface Shown {
  readonly field: DesignField;
  readonly settings: Settings;
  readonly context: PreviewContext;
  readonly id: string;
  readonly name: string;
  readonly label: string;
  readonly required: boolean;
  readonly columns: number;
}

// How the form's style marks a label: one box only a required tick, a switch, a calculation and a slider never, a
// repeatable group only while it needs no row, a grid with a required row not as optional.
function markOf(
  field: DesignField,
  settings: Settings,
  element: FieldViewModel['labelElement'],
  required: boolean,
  context: PreviewContext,
): FieldViewModel['mark'] {
  if (
    element === 'none' ||
    (field.type === 'yesno' && element === 'choice') ||
    field.type === 'calculation' ||
    field.type === 'slider' ||
    (field.type === 'repeater' && settings.number('minRows', 0) > 0)
  )
    return 'none';
  if (context.marker === 'required' && required) return 'required';
  if (
    context.marker === 'optional' &&
    !required &&
    element !== 'choice' &&
    !(field.rows ?? []).some((row) => row['required'] === true)
  )
    return 'optional';
  return 'none';
}

interface Control {
  readonly control?: NonNullable<FieldViewModel['control']>;
}

function controlOf(shown: Shown): Control {
  switch (shown.field.type) {
    case 'text':
    case 'email':
    case 'phone':
    case 'url':
      return text(shown);
    case 'textarea': {
      const counter = shown.settings.bool('counter', true)
        ? counterOf(shown, shown.settings.number('maxLength', 4000))
        : undefined;
      return {
        control: {
          kind: 'textArea',
          rows: clamp(shown.settings.number('rows', 4), 1, 50),
          autoGrow: shown.settings.bool('autoGrow', true),
          ...(counter ? { counter } : {}),
        },
      };
    }
    case 'number':
      return numeric(shown);
    case 'date':
      return { control: { kind: 'date', inputType: 'date' } };
    case 'datetime': {
      const minutes = shown.settings.number('stepMinutes', 0);
      return {
        control: {
          kind: 'date',
          inputType: 'datetime-local',
          ...(minutes > 0 && 1440 % minutes === 0 ? { step: String(minutes * 60) } : {}),
        },
      };
    }
    case 'time': {
      const minutes = shown.settings.number('stepMinutes', 0);
      const min = shown.settings.text('min');
      const max = shown.settings.text('max');
      return {
        control: {
          kind: 'date',
          inputType: 'time',
          ...(min ? { min } : {}),
          ...(max ? { max } : {}),
          ...(minutes > 0 ? { step: String(minutes * 60) } : {}),
        },
      };
    }
    case 'heading':
      return {
        control: {
          kind: 'heading',
          text: shown.settings.text('text') ?? '',
          level: clamp(shown.settings.number('level', 2), 2, 4),
        },
      };
    case 'paragraph': {
      const html = safeHtml(shown.settings.text('html'));
      return { control: { kind: 'textBlock', html, isEmpty: html.length === 0 } };
    }
    case 'image': {
      const size = shown.settings.text('size') ?? 'medium';
      const media = shown.settings.text('media');
      const url = media ? shown.context.mediaUrl?.(media) : undefined;
      return {
        control: {
          kind: 'image',
          ...(url ? { url } : {}),
          alt: shown.settings.text('alt') ?? '',
          size: ['small', 'medium', 'large', 'full'].includes(size) ? size : 'medium',
        },
      };
    }
    case 'calculation':
      return { control: { kind: 'calculation', text: calculated(shown) } };
    case 'captcha':
      return { control: { kind: 'captcha', provider: shown.context.captcha ?? '' } };
    case 'dropdown':
      return dropdown(shown);
    case 'country':
      return country(shown);
    case 'radio':
    case 'checkboxes':
      return list(shown, shown.field.type === 'checkboxes');
    case 'yesno':
      return yesNo(shown);
    case 'checkbox':
    case 'consent': {
      const html = safeHtml(shown.settings.text('text'));
      return {
        control: {
          kind: 'singleChoice',
          value: 'true',
          label: showsText(html) ? html : escapeHtml(shown.label),
          isChecked: false,
        },
      };
    }
    case 'scale':
      return numbers(
        shown,
        clamp(shown.settings.number('from', 0), 0, 1),
        clamp(shown.settings.number('to', 10), 5, 10),
        'jfb-choices jfb-choices-scale',
      );
    case 'rating': {
      const symbol = shown.settings.text('symbol');
      return numbers(
        shown,
        1,
        clamp(shown.settings.number('count', 5), 3, 10),
        `jfb-choices jfb-choices-rating jfb-choices-${symbol === 'heart' || symbol === 'circle' ? symbol : 'star'}`,
      );
    }
    case 'slider':
      return slider(shown);
    case 'grid':
      return grid(shown);
    case 'ranking':
      return ranking(shown);
    case 'name':
      return parts(
        shown,
        NAME_PARTS,
        shown.settings.texts('parts'),
        (_part, asked) => asked,
        () => false,
      );
    case 'address':
      return parts(
        shown,
        ADDRESS_PARTS,
        ['street', 'postcode', 'city', ...shown.settings.texts('parts')],
        () => ['street', 'postcode', 'city'],
        (part, asked) => part === 'street' || part === 'street2' || (part === 'country' && !asked.includes('region')),
      );
    case 'file':
      return file(shown);
    case 'signature':
      return signature(shown);
    case 'repeater':
      return repeater(shown);
    default:
      // A divider adds nothing to its wrapper's line; a site's own type is the site's to draw (15.1).
      return {};
  }
}

function text(shown: Shown): Control {
  const { field, settings, context, id } = shown;
  const plain = field.type === 'text';
  const prefix = plain ? settings.text('prefix') : undefined;
  const suffix = plain ? settings.text('suffix') : undefined;
  const counter = plain && settings.bool('counter') ? counterOf(shown, settings.number('maxLength', 255)) : undefined;
  return {
    control: {
      kind: 'text',
      inputType:
        field.type === 'email' ? 'email' : field.type === 'phone' ? 'tel' : field.type === 'url' ? 'url' : 'text',
      ...(field.type === 'email' || field.type === 'url' ? { spellcheck: false } : {}),
      ...(prefix ? { prefix: { text: prefix, id: `${id}-prefix` } } : {}),
      ...(suffix ? { suffix: { text: suffix, id: `${id}-suffix` } } : {}),
      ...(counter ? { counter } : {}),
      ...(field.type === 'email' && settings.bool('confirm')
        ? {
            confirm: {
              label: settings.text('confirmLabel') ?? word(context.look, 'confirmEmail'),
              id: `${id}-confirm`,
              name: `${shown.name}.confirm`,
              value: '',
            },
          }
        : {}),
    },
  };
}

function counterOf(shown: Shown, limit: number): CounterModel {
  const look = shown.context.look;
  return {
    text: word(look, 'counter', number(shown.context, limit)),
    id: `${shown.id}-count`,
    limit,
    left: word(look, 'counterLeft'),
    leftOne: word(look, 'counterLeftOne'),
    over: word(look, 'counterOver'),
    overOne: word(look, 'counterOverOne'),
  };
}

function numeric(shown: Shown): Control {
  const { settings, id } = shown;
  const places = settings.number('decimals', 0) > 0;
  const min = settings.text('min');
  const max = settings.text('max');
  const step = settings.text('step');
  const negative = min === undefined || Number(min) < 0;
  const prefix = settings.text('prefix');
  const suffix = settings.text('suffix');
  return {
    control: {
      kind: 'number',
      inputType: places ? 'text' : 'number',
      ...(negative ? {} : { inputMode: places ? 'decimal' : 'numeric' }),
      ...(!places && min !== undefined ? { min } : {}),
      ...(!places && max !== undefined ? { max } : {}),
      ...(!places && step !== undefined ? { step } : {}),
      ...(prefix ? { prefix: { text: prefix, id: `${id}-prefix` } } : {}),
      ...(suffix ? { suffix: { text: suffix, id: `${id}-suffix` } } : {}),
    },
  };
}

// A calculation before any answer: nought in the field's places, between its prefix and suffix.
function calculated(shown: Shown): string {
  const format = shown.settings.child('format');
  const places = clamp(format?.number('decimals', 2) ?? 2, 0, 15);
  const value = new Intl.NumberFormat(shown.context.look.culture, {
    minimumFractionDigits: places,
    maximumFractionDigits: places,
  }).format(0);
  return around(format?.text('prefix') ?? '', value, format?.text('suffix') ?? '');
}

/** One option a source gives, as the server resolved it. */
export interface ResolvedOption {
  readonly value: string;
  readonly label?: string | null;
  readonly description?: string | null;
  readonly image?: string | null;
}

/**
 * A field's options, as the page shows them: its own, or those its source resolved (ADR-0172); a blank label shows
 * the value.
 */
function optionsOf(shown: Shown): { value: string; label: string; description?: string; image?: string }[] {
  const options = shown.field['options'] as
    | {
        source?: string;
        items?: readonly { value?: string; label?: LocalizedText; description?: LocalizedText; image?: string }[];
      }
    | undefined;
  if (options?.source !== undefined && options.source !== 'items') {
    return (shown.context.resolved?.(shown.field) ?? []).map((option) => {
      const label = (option.label ?? '').trim();
      const description = (option.description ?? '').trim();
      return {
        value: option.value,
        label: label || option.value,
        ...(description ? { description } : {}),
        ...(option.image ? { image: option.image } : {}),
      };
    });
  }
  return (options?.items ?? []).map((item) => {
    const value = item.value ?? '';
    const label = textIn(item.label, shown.context.culture).trim();
    const description = textIn(item.description, shown.context.culture).trim();
    return {
      value,
      label: label || value,
      ...(description ? { description } : {}),
      ...(item.image ? { image: item.image } : {}),
    };
  });
}

/** Whether a field's options come from elsewhere than itself, which only the server resolves. */
function fromElsewhere(shown: Shown): boolean {
  const options = shown.field['options'] as { source?: string; parent?: string | null } | undefined;
  return (options?.source !== undefined && options.source !== 'items') || Boolean(options?.parent);
}

const choiceId = (id: string, index: number) => (index === 0 ? id : `${id}-${index + 1}`);

function dropdown(shown: Shown): Control {
  const { settings, context, id } = shown;
  const options = optionsOf(shown);
  const multiple = settings.bool('multiple');
  const elsewhere = fromElsewhere(shown);
  return {
    control: {
      kind: 'select',
      multiple,
      ...(multiple ? { size: clamp(options.length, 2, 6) } : {}),
      ...(multiple ? {} : { prompt: settings.text('prompt') ?? word(context.look, 'choose') }),
      cascades: false,
      options: options.map((option) => ({ value: option.value, label: option.label, isSelected: false })),
      ...(multiple && options.length > 0
        ? { hint: { text: word(context.look, 'severalHint'), id: `${id}-hint` } }
        : {}),
      ...(options.length === 0 && !elsewhere ? { empty: word(context.look, 'noChoices') } : {}),
    },
  };
}

function country(shown: Shown): Control {
  const chosen = shown.settings.text('default');
  const name = chosen ? countryName(chosen, shown.context.look.culture) : '';
  return {
    control: {
      kind: 'select',
      multiple: false,
      prompt: word(shown.context.look, 'chooseCountry'),
      cascades: false,
      options: chosen ? [{ value: chosen, label: name ?? chosen, isSelected: true }] : [],
    },
  };
}

// A country's name in a culture's language; its code where the browser names none.
function countryName(code: string, culture: string): string {
  try {
    return new Intl.DisplayNames([culture], { type: 'region' }).of(code) ?? code;
  } catch {
    return code;
  }
}

function list(shown: Shown, several: boolean): Control {
  const { settings, context, id } = shown;
  const own = settings.text('layout');
  const layout = own && own !== 'inherit' ? own : context.layout;
  const choices: ChoiceModel[] = optionsOf(shown).map((option, index) => {
    const url = layout === 'cards' && option.image ? context.mediaUrl?.(option.image) : undefined;
    return {
      id: choiceId(id, index),
      value: option.value,
      label: option.label,
      ...(option.description ? { description: option.description } : {}),
      ...(url ? { image: { url } } : {}),
      descriptionId: `${choiceId(id, index)}-description`,
      isChecked: false,
    };
  });
  const other = settings.bool('other')
    ? {
        choice: {
          id: choiceId(id, choices.length),
          value: '',
          label: settings.text('otherLabel') ?? word(context.look, 'other'),
          descriptionId: `${choiceId(id, choices.length)}-description`,
          isChecked: false,
        },
        textLabel: word(context.look, 'otherText'),
        textId: `${id}-other`,
        textName: `${shown.name}.other`,
        text: '',
      }
    : undefined;
  const control: ChoicesControl = {
    kind: 'choices',
    inputType: several ? 'checkbox' : 'radio',
    cssClass: [
      'jfb-choices',
      layout === 'horizontal' ? 'jfb-choices-horizontal' : layout === 'cards' ? 'jfb-choices-cards' : null,
    ]
      .filter(Boolean)
      .join(' '),
    choices,
    ...(other ? { other } : {}),
    ...(choices.length === 0 && !other && !fromElsewhere(shown) ? { empty: word(context.look, 'noChoices') } : {}),
  };
  return { control };
}

function yesNo(shown: Shown): Control {
  const { settings, context, id } = shown;
  if (settings.text('style') === 'switch')
    return { control: { kind: 'singleChoice', value: 'yes', label: escapeHtml(shown.label), isChecked: false } };
  const answer = (value: string, index: number, label: string): ChoiceModel => ({
    id: choiceId(id, index),
    value,
    label,
    descriptionId: `${choiceId(id, index)}-description`,
    isChecked: false,
  });
  return {
    control: {
      kind: 'choices',
      inputType: 'radio',
      cssClass: 'jfb-choices jfb-choices-buttons',
      choices: [
        answer('yes', 0, settings.text('yesLabel') ?? word(context.look, 'yes')),
        answer('no', 1, settings.text('noLabel') ?? word(context.look, 'no')),
      ],
    },
  };
}

// A scale's or a rating's whole numbers, the words at its ends heard with the first and the last.
function numbers(shown: Shown, from: number, to: number, cssClass: string): Control {
  const low = shown.settings.text('lowLabel');
  const high = shown.settings.text('highLabel');
  const choices: ChoiceModel[] = [];
  for (let value = from; value <= to; value++) {
    const hidden = value === from && low ? `, ${low}` : value === to && high ? `, ${high}` : undefined;
    choices.push({
      id: choiceId(shown.id, choices.length),
      value: String(value),
      label: String(value),
      ...(hidden ? { hiddenText: hidden } : {}),
      descriptionId: `${choiceId(shown.id, choices.length)}-description`,
      isChecked: false,
    });
  }
  return {
    control: {
      kind: 'choices',
      inputType: 'radio',
      cssClass,
      choices,
      ...(low || high ? { ends: { low: low ?? '', high: high ?? '' } } : {}),
    },
  };
}

// The decimal places of a number written in the wire form.
const placesOf = (text: string) => (text.includes('.') ? (text.split('.')[1] ?? '').length : 0);

/**
 * Where a slider starts: the middle of its range on its step, the higher of two steps as near, as a browser puts a
 * range input without a value; worked in whole units of the finest place, so no binary fraction creeps in.
 */
export function sliderStart(min: string, max: string, step: string): string {
  const places = Math.max(placesOf(min), placesOf(max), placesOf(step));
  const scale = 10 ** places;
  const [low, high, by] = [min, max, step].map((text) => Math.round(Number(text) * scale)) as [number, number, number];
  if (!Number.isFinite(low) || !Number.isFinite(high) || !(by > 0)) return min;
  const value = low + Math.floor((high - low) / 2 / by + 0.5) * by;
  return (value / scale).toFixed(places);
}

function slider(shown: Shown): Control {
  const { settings, context } = shown;
  const min = settings.text('min') ?? '0';
  const max = settings.text('max') ?? '100';
  const step = settings.text('step') ?? '1';
  const value = sliderStart(min, max, step);
  const valueText = around(
    '',
    new Intl.NumberFormat(context.look.culture, { maximumFractionDigits: 15 }).format(Number(value)),
    settings.text('unit') ?? '',
  );
  return {
    control: {
      kind: 'slider',
      min,
      max,
      step,
      value,
      ...(valueText === value ? {} : { valueText }),
      ...(settings.bool('showValue', true) ? { shown: valueText } : {}),
    },
  };
}

function grid(shown: Shown): Control {
  const { field, settings, context, id } = shown;
  const scale = settings.child('scale');
  let columns: { value: string; label: string }[];
  if (scale) {
    const from = clamp(scale.number('from', 1), 0, 1);
    const to = clamp(scale.number('to', 5), 2, 10);
    columns = Array.from({ length: to - from + 1 }, (_, at) => ({
      value: String(from + at),
      label: String(from + at),
    }));
  } else columns = optionsOf(shown);
  const multiple = settings.bool('multiple');
  const rows =
    columns.length === 0
      ? []
      : (field.rows ?? []).map((row, index) => {
          const marked = context.marker === 'required' && !shown.required && row['required'] === true;
          return {
            key: row.key,
            label: textIn(row.label, context.culture) || word(context.look, 'row', String(index + 1)),
            name: `${shown.name}.${row.key}`,
            isRequired: shown.required || row['required'] === true,
            followsRequired: false,
            mark: marked ? ('required' as const) : ('none' as const),
            markText: marked ? '*' : '',
            ...(marked && multiple ? { markSpoken: word(context.look, 'required') } : {}),
            isMarkShown: marked,
            choices: columns.map((column, at) => ({
              id: index === 0 && at === 0 ? id : `${id}-${index + 1}-${at + 1}`,
              value: column.value,
              label: column.label,
              descriptionId: `${id}-${index + 1}-${at + 1}-description`,
              isChecked: false,
            })),
          };
        });
  return {
    control: {
      kind: 'grid',
      inputType: multiple ? 'checkbox' : 'radio',
      columns: columns.map((column) => column.label),
      rows,
      ...(rows.length === 0 && !fromElsewhere(shown) ? { empty: word(context.look, 'noChoices') } : {}),
    },
  };
}

function ranking(shown: Shown): Control {
  const { settings, context, id } = shown;
  const options = optionsOf(shown);
  const top = settings.number('top', 0);
  const ranked = top > 0 ? Math.min(top, options.length) : options.length;
  const place = (at: number) => `${id}-place-${at + 1}`;
  return {
    control: {
      kind: 'ranking',
      items: options.map((option, at) => ({
        id: place(at),
        label: option.label,
        ...(at < ranked ? { position: at + 1 } : {}),
        ...(at < ranked ? { input: { name: `${shown.name}.${option.value}`, value: String(at + 1) } } : {}),
        order: { name: `jfb-order-${shown.name}`, value: option.value },
        up: { label: word(context.look, 'moveUp', option.label), value: 'up', isDisabled: at === 0 },
        down: {
          label: word(context.look, 'moveDown', option.label),
          value: 'down',
          isDisabled: at === options.length - 1,
        },
      })),
      name: shown.name,
      ranked,
      moved: word(context.look, 'rankingMoved'),
      ...(options.length === 0 && !fromElsewhere(shown) ? { empty: word(context.look, 'noChoices') } : {}),
    },
  };
}

const NAME_PARTS: readonly [string, string, string][] = [
  ['title', 'honorific-prefix', 'nameTitle'],
  ['first', 'given-name', 'nameFirst'],
  ['middle', 'additional-name', 'nameMiddle'],
  ['last', 'family-name', 'nameLast'],
];

const ADDRESS_PARTS: readonly [string, string, string][] = [
  ['street', 'address-line1', 'street'],
  ['street2', 'address-line2', 'street2'],
  ['postcode', 'postal-code', 'postcode'],
  ['city', 'address-level2', 'city'],
  ['region', 'address-level1', 'region'],
  ['country', 'country', 'country'],
];

// A name's or an address's parts in its type's order, marked in the form's style when it needs some but not all.
function parts(
  shown: Shown,
  order: readonly [string, string, string][],
  wanted: readonly string[],
  unlistedOf: (part: string, asked: string[]) => string[],
  wide: (part: string, asked: string[]) => boolean,
): Control {
  const { settings, context, id } = shown;
  const asked = order.map(([part]) => part).filter((part) => wanted.includes(part));
  const listed = settings.texts('requiredParts');
  const ifRequired = (listed.length > 0 ? listed : unlistedOf('', asked)).filter((part) => asked.includes(part));
  const some = ifRequired.length < asked.length;
  const models: PartModel[] = asked.map((part, at) => {
    const [, token, label] = order.find(([name]) => name === part) ?? [part, '', part];
    const markIfRequired = !some
      ? 'none'
      : context.marker === 'required' && ifRequired.includes(part)
        ? 'required'
        : context.marker === 'optional' && !ifRequired.includes(part)
          ? 'optional'
          : 'none';
    const mark = shown.required ? markIfRequired : 'none';
    return {
      part,
      label: word(context.look, label),
      id: at === 0 ? id : `${id}-${part}`,
      name: `${shown.name}.${part}`,
      value: '',
      autocomplete: token,
      isWide: wide(part, asked),
      isRequired: shown.required && ifRequired.includes(part),
      followsRequired: false,
      isInvalid: false,
      mark,
      markText: mark === 'optional' ? word(context.look, 'optional') : mark === 'required' ? '*' : '',
      isMarkShown: mark !== 'none',
      ...(part === 'country'
        ? { countries: { multiple: false, prompt: word(context.look, 'chooseCountry'), cascades: false, options: [] } }
        : {}),
    };
  });
  return { control: { kind: 'parts', parts: models } };
}

function file(shown: Shown): Control {
  const { settings, context, id } = shown;
  const look = context.look;
  const several = settings.number('maxFiles', 1) > 1;
  const drop = settings.bool('dropZone', true);
  return {
    control: {
      kind: 'file',
      accept: '',
      multiple: several,
      dropZone: drop,
      thumbnails: settings.bool('thumbnails', true),
      upload: {
        ...(drop ? { drag: word(look, several ? 'dragFiles' : 'dragFile') } : {}),
        choose: word(look, several ? 'chooseFiles' : 'chooseFile'),
        list: word(look, 'yourFiles'),
        uploading: word(look, 'fileUploading'),
        uploaded: word(look, 'fileUploaded'),
        remove: word(look, 'removeFile'),
        stop: word(look, 'stopUploading'),
        tryAgain: word(look, 'tryAgain'),
        tryAgainFor: word(look, 'tryAgainFor'),
        stopped: word(look, 'uploadStopped'),
        later: word(look, 'uploadLater'),
        kilobytes: word(look, 'kilobytes'),
        megabytes: word(look, 'megabytes'),
      },
      ...(settings.bool('camera') ? { camera: { id: `${id}-camera`, text: word(look, 'takePhoto') } } : {}),
      kept: [],
    },
  };
}

// The pad's size when the field gives none, as the server's typed signature keeps it.
const PAD_WIDTH = 900;
const PAD_HEIGHT = 300;

function signature(shown: Shown): Control {
  const { settings, context, id } = shown;
  const look = context.look;
  return {
    control: {
      kind: 'signature',
      label: word(look, 'signatureName'),
      name: `${shown.name}.typed`,
      value: '',
      maxLength: 100,
      help: word(look, 'signatureTyped'),
      helpId: `${id}-typed-help`,
      pad: {
        width: settings.number('width', PAD_WIDTH),
        height: settings.number('height', PAD_HEIGHT),
        help: word(look, 'signatureHand'),
        drawn: word(look, 'signatureDrawn'),
        clear: word(look, 'signatureClear'),
        undoClear: word(look, 'signatureUndoClear'),
        typeInstead: word(look, 'signatureTypeInstead'),
        byHand: word(look, 'signatureByHand'),
      },
    },
  };
}

// A repeatable group's least rows, at least the one a visitor starts with once they add a row, each its fields.
function repeater(shown: Shown): Control {
  const { field, settings, context, id } = shown;
  const least = Math.max(0, settings.number('minRows', 0));
  const most = settings.number('maxRows', 10);
  const label = settings.text('rowLabel');
  const remove = settings.text('removeLabel') ?? word(context.look, 'removeRow');
  const rowOf = (index: number): RepeaterRowModel => {
    const legend = label ? `${label} ${index + 1}` : word(context.look, 'repeaterRow', String(index + 1));
    const prefix = `${id}-${index}`;
    return {
      index,
      id: prefix,
      legend,
      // A first render has its least rows, which can't be removed.
      remove: { value: 'remove', text: remove, isHidden: true, context: legend },
      fields: (field.fields ?? []).flatMap((child) => fieldModel(child, { ...context, prefix }, shown.columns) ?? []),
    };
  };
  return {
    control: {
      kind: 'repeater',
      rows: Array.from({ length: context.showRows ? Math.max(least, 1) : least }, (_, index) => rowOf(index)),
      template: rowOf(least),
      least,
      most,
      legend: label ? `${label} {0}` : word(context.look, 'repeaterRow'),
      removed: word(context.look, 'rowRemoved'),
      add: { value: 'add', text: settings.text('addLabel') ?? word(context.look, 'addRow'), isHidden: least >= most },
      rowCount: { name: `jfb-rows-${shown.name}`, value: String(least) },
      fieldsCssClass: `jfb-fields jfb-columns-${shown.columns}`,
    },
  };
}
