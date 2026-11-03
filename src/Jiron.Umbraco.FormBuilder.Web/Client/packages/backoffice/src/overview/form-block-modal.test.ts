// "Create a form block type..." (ADR-0130): the dialog says what it creates, and once it has, the one step left. A failure is said in
// place, with the dialog still open. Playwright presses the real button (ROADMAP 9.1).
import { afterEach, beforeAll, beforeEach, describe, expect, it, vi } from 'vitest';
import { all, must, registerEnglish, settle, shadow, testHost, text } from '../testing';
import type { FormBlockModalElement } from './form-block-modal.element';

const api = vi.hoisted(() => ({ create: vi.fn() }));

vi.mock('../api', async (original) => ({
  ...(await original<typeof import('../api')>()),
  setupCreateFormBlock: () => api.create(),
}));

vi.mock('@umbraco-cms/backoffice/resources', async (original) => ({
  ...(await original<typeof import('@umbraco-cms/backoffice/resources')>()),
  tryExecute: async (_host: unknown, promise: Promise<unknown>) => promise,
}));

async function open(): Promise<FormBlockModalElement> {
  await import('./form-block-modal.element');
  const element = document.createElement('jfb-form-block-modal');
  testHost().append(element);
  await settle(element);
  return element;
}

const button = (element: Element, label: string) =>
  must(
    all(element, 'uui-button').find((each) => each.getAttribute('label') === label),
    label,
  );

describe('the form block dialog', () => {
  beforeAll(registerEnglish);

  beforeEach(() => {
    api.create.mockImplementation(() => Promise.resolve({ data: {} }));
  });

  afterEach(() => {
    document.body.replaceChildren();
    vi.clearAllMocks();
  });

  it('says what it creates and that it changes nothing that exists, and calls nothing until asked', async () => {
    const element = await open();

    expect(text(must(shadow(element).querySelector('p'), 'the text'))).toContain(
      'It never changes types that exist already.',
    );
    expect(api.create).not.toHaveBeenCalled();
  });

  it('once created, says the one step left, with where to do it', async () => {
    const element = await open();

    button(element, 'Create').click();
    await settle(element);

    expect(api.create).toHaveBeenCalledTimes(1);
    expect(text(must(shadow(element).querySelector('[role="status"]'), 'the result'))).toContain('One step is left');
    expect(button(element, 'Open document types').getAttribute('href')).toBe(
      'section/settings/workspace/document-type-root',
    );
    expect(all(element, 'uui-button').map((each) => each.getAttribute('label'))).not.toContain('Create');
  });

  it('says so when it failed, and can be tried again', async () => {
    api.create.mockImplementationOnce(() => Promise.resolve({ error: new Error('403') }));
    const element = await open();

    button(element, 'Create').click();
    await settle(element);
    expect(text(must(shadow(element).querySelector('[role="alert"]'), 'the failure'))).toContain("couldn't be created");

    button(element, 'Create').click();
    await settle(element);
    expect(shadow(element).querySelector('[role="alert"]')).toBeNull();
    expect(shadow(element).querySelector('[role="status"]')).not.toBeNull();
  });
});
