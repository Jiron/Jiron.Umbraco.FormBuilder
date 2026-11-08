// A template's name and description (ROADMAP 12.1, ADR-0217): the name it starts with, refused when blank with the reason
// at the field, the description optional, and a note on what the template leaves out; what it gives back is trimmed, a
// blank description as none.
import { UmbObjectState } from '@umbraco-cms/backoffice/observable-api';
import { afterEach, beforeAll, describe, expect, it, vi } from 'vitest';
import { must, registerEnglish, settle, shadow, text } from '../../testing';
import { TemplateNameModalElement } from './template-name.modal.element';
import type { TemplateNameModalData } from './tokens';

async function dialogOf(data: TemplateNameModalData) {
  const element = new TemplateNameModalElement();
  const updateValue = vi.fn();
  const submit = vi.fn();
  element.modalContext = {
    value: new UmbObjectState({}).asObservable(),
    getValue: () => ({}),
    setValue: () => undefined,
    updateValue,
    submit,
    reject: vi.fn(),
  } as never;
  element.data = data;
  document.body.append(element);
  await settle(element);
  return { element, root: shadow(element), updateValue, submit };
}

const confirmButton = (root: ShadowRoot) =>
  must(root.querySelector<HTMLElement>('uui-button[look="primary"]'), 'the confirm button');

beforeAll(registerEnglish);

afterEach(() => {
  document.body.replaceChildren();
});

describe("a template's name and description", () => {
  it('start from what they are given, with the note on what the template leaves out', async () => {
    const { root } = await dialogOf({
      headline: 'Save Contact as a template',
      confirmLabel: 'Save template',
      name: 'Contact',
      note: 'Changes not saved yet stay out.',
    });
    expect(root.querySelector<HTMLInputElement>('#name')?.value).toBe('Contact');
    expect(root.querySelector<HTMLTextAreaElement>('#description')?.value).toBe('');
    expect(text(root.querySelector('.note'))).toBe('Changes not saved yet stay out.');
    expect(confirmButton(root).getAttribute('label')).toBe('Save template');
  });

  it('refuse a blank name with the reason at the field, and give back what was typed, trimmed', async () => {
    const { element, root, updateValue, submit } = await dialogOf({ headline: 'Rename', confirmLabel: 'Rename' });
    confirmButton(root).dispatchEvent(new Event('click'));
    await settle(element);
    expect(text(root.querySelector('#blank'))).toBe('Enter a name.');
    expect(submit).not.toHaveBeenCalled();
    const name = must(root.querySelector<HTMLInputElement>('#name'), 'the name');
    name.value = '  Address block ';
    name.dispatchEvent(new Event('input'));
    const description = must(root.querySelector<HTMLTextAreaElement>('#description'), 'the description');
    description.value = '   ';
    description.dispatchEvent(new Event('input'));
    await settle(element);
    expect(root.querySelector('#blank')).toBeNull();
    confirmButton(root).dispatchEvent(new Event('click'));
    expect(updateValue).toHaveBeenLastCalledWith({ name: 'Address block', description: null });
    expect(submit).toHaveBeenCalledOnce();
  });
});
