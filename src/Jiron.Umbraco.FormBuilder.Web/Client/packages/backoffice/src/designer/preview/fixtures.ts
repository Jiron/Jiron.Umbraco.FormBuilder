// The fixtures the preview tests draw with (ADR-0168), one source each: the canvas endpoint's answer in
// docs/api/canvas.json, the field types in docs/api/field-types.json and some of their changes in
// docs/api/field-type-changes.json, the option sources in
// docs/api/option-sources.json, the captcha providers in docs/api/captcha-providers.json, the sink's draft in
// tests/fixtures/rendering/designer-sink.json and the server's markup of the same sink in element-en.json.
import { readFileSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';
import type {
  CanvasResponseModel,
  CaptchaProviderResponseModel,
  FieldTypeChangesResponseModel,
  FieldTypesResponseModel,
  OptionSourcesResponseModel,
} from '../../api';
import { normalise, type DesignDefinition } from '../definition';
import { lookOf, type CanvasLook } from './look';

const root = join(dirname(fileURLToPath(import.meta.url)), ...Array<string>(8).fill('..'));
const json = <T>(...path: string[]): T => JSON.parse(readFileSync(join(root, ...path), 'utf8')) as T;

/** The look in English, as the canvas endpoint answers without a culture; its snapshot leaves out the stylesheets. */
export const englishLook: CanvasLook = lookOf({
  ...json<Omit<CanvasResponseModel, 'stylesheets'>>('docs', 'api', 'canvas.json'),
  stylesheets: [],
});

/** The built-in field types by alias. */
export const builtInTypes = Object.fromEntries(
  json<FieldTypesResponseModel>('docs', 'api', 'field-types.json').types.map((type) => [type.alias, type]),
);

/** The changes of type of a text, a long text, a number, checkboxes and a single checkbox, by the type they leave. */
export const builtInChanges = json<Record<string, FieldTypeChangesResponseModel>>(
  'docs',
  'api',
  'field-type-changes.json',
);

/** The kinds a field may take its options from and the standard lists, as docs/api/option-sources.json has them. */
export const optionSources = json<OptionSourcesResponseModel>('docs', 'api', 'option-sources.json');

/** The captcha providers, as the Settings view reads them on a site with reCAPTCHA v2's keys. */
export const captchaProviders = json<CaptchaProviderResponseModel[]>('docs', 'api', 'captcha-providers.json');

/** The sink: a field of every built-in type, a repeatable group holding one of each, and a captcha. */
export const sink: DesignDefinition = normalise(
  json<{ cases: { definition: unknown }[] }>('tests', 'fixtures', 'rendering', 'designer-sink.json').cases[0]
    ?.definition,
);

/** The server's markup of the sink's first render in English, in the default theme. */
export const sinkMarkup: string =
  json<{ cases: { name: string; html: string }[] }>('tests', 'fixtures', 'rendering', 'element-en.json').cases.find(
    (each) => each.name === 'sink',
  )?.html ?? '';
