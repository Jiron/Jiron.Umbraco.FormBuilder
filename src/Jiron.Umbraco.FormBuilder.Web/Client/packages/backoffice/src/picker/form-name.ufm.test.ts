// The block label's form name (ADR-0134): `{jfbFormName: form}` names the picked forms, marks one that is gone and
// shows nothing for no value; the component renders its element even when Umbraco calls it without the instance.
import { UmbContextBase } from '@umbraco-cms/backoffice/class-api';
import type { UmbControllerHost } from '@umbraco-cms/backoffice/controller-api';
import { UmbObjectState } from '@umbraco-cms/backoffice/observable-api';
import { UMB_UFM_RENDER_CONTEXT } from '@umbraco-cms/backoffice/ufm';
import { afterEach, beforeAll, beforeEach, describe, expect, it, vi } from 'vitest';
import { registerEnglish, settle, testHost } from '../testing';
import type { FormNameUfmElement } from './form-name.ufm';
import type { PickerItem } from './picker-data';

const reads = vi.hoisted(() => ({
  forms: vi.fn<(keys: readonly string[]) => Promise<PickerItem[] | undefined>>(),
  made: [] as (string | undefined)[],
}));

vi.mock('./picker-data', async (original) => ({
  ...(await original<typeof import('./picker-data')>()),
  formPickerData: (_host: unknown, dataTypeKey: string | undefined) => {
    reads.made.push(dataTypeKey);
    return reads;
  },
}));

const { FormNameUfmComponent } = await import('./form-name.ufm');

const form = (key: string, name: string): PickerItem => ({ key, kind: 'form', name, alias: name, hasChildren: false });
const known = [form('a-contact', 'Contact us'), form('b-news', 'Newsletter')];

/** The block's content as a label's render context provides it to the components inside the label. */
class RenderContext extends UmbContextBase {
  #content = new UmbObjectState<unknown>(undefined);
  readonly value = this.#content.asObservable();

  constructor(host: UmbControllerHost) {
    super(host, UMB_UFM_RENDER_CONTEXT as never);
  }

  setValue(content: unknown) {
    this.#content.setValue(content);
  }
}

/** The label's element for the block content given, under a render context. */
async function label(content: unknown, alias = 'form') {
  const host = testHost();
  const context = new RenderContext(host);
  context.setValue(content);
  const element = document.createElement('ufm-jfb-form-name') as FormNameUfmElement;
  element.alias = alias;
  host.append(element);
  await settle(element);
  return { element, context };
}

beforeAll(registerEnglish);

beforeEach(() => {
  reads.forms.mockReset();
  reads.forms.mockImplementation((keys) => Promise.resolve(known.filter((item) => keys.includes(item.key))));
  reads.made.length = 0;
});

afterEach(() => document.body.replaceChildren());

describe("the block label's form name", () => {
  it("names the form the block's property picks, without a data type's limits", async () => {
    const { element } = await label({ form: 'a-contact', $settings: {} });

    expect(element.value).toBe('Contact us');
    expect(element.shadowRoot?.textContent).toContain('Contact us');
    expect(reads.made).toEqual([undefined]);
    expect(reads.forms).toHaveBeenCalledWith(['a-contact']);
  });

  it('joins several names as the language joins a list, and marks a form that is gone', async () => {
    const { element } = await label({ form: 'a-contact,gone,b-news' });

    expect(element.value).toBe('Contact us, (not available), and Newsletter');
  });

  it('follows the block as its value changes', async () => {
    const { element, context } = await label({ form: 'a-contact' });

    context.setValue({ form: 'b-news' });
    await settle(element);
    expect(element.value).toBe('Newsletter');
  });

  it('shows nothing for no value, and nothing when the names cannot be read', async () => {
    const empty = await label({ form: '' });
    expect(empty.element.value).toBe('');
    expect(reads.forms).not.toHaveBeenCalled();

    reads.forms.mockResolvedValueOnce(undefined);
    const failed = await label({ form: 'a-contact' });
    expect(failed.element.value).toBe('');
  });

  it("renders its element with the token's alias, also when Umbraco calls render without the component", () => {
    const component = new FormNameUfmComponent();
    const render = component.render;

    expect(render({ text: 'form' } as never)).toBe('<ufm-jfb-form-name alias="form"></ufm-jfb-form-name>');
    expect(render({ text: '' } as never)).toBeUndefined();
  });
});
