import { readdirSync, readFileSync, statSync } from 'node:fs';
import { join, relative, sep } from 'node:path';
import { render, type TemplateResult } from '@umbraco-cms/backoffice/external/lit';
import { describe, expect, it, vi } from 'vitest';
import { confirmList } from './confirm';

// The confirm dialogs show their content as text (section 4.10): Umbraco's confirm modal renders a string as HTML, so a form
// named like <img onerror=...> would run in the backoffice. Only ui/confirm.ts may open that modal, and it passes the content
// as a Lit template; the raw modal and unsafeHTML appear nowhere else. A list of what a step sets off (ADR-0204) shows its
// items as text too.

const asked = vi.hoisted(() => ({ data: [] as { headline?: string; confirmLabel?: string; content: unknown }[] }));

vi.mock('@umbraco-cms/backoffice/modal', async (original) => ({
  ...(await original<typeof import('@umbraco-cms/backoffice/modal')>()),
  umbConfirmModal: async (_host: unknown, data: { headline?: string; confirmLabel?: string; content: unknown }) => {
    asked.data.push(data);
  },
}));

const root = join(import.meta.dirname, '..');
// The AI add-on's backoffice is held to the same (16.1's review).
const aiRoot = join(import.meta.dirname, '..', '..', '..', 'ai', 'src');

// A folder whose name starts with a dot is another test's scratch (the API check's), which may go while this one reads.
function sources(dir: string): string[] {
  return readdirSync(dir).flatMap((name) => {
    if (name.startsWith('.')) return [];
    const path = join(dir, name);
    if (statSync(path).isDirectory()) return name === 'api' ? [] : sources(path);
    return name.endsWith('.ts') && !name.endsWith('.test.ts') ? [path] : [];
  });
}

describe('confirm dialogs', () => {
  it('open only through confirmText, which passes the content as a template', () => {
    const direct = sources(root)
      .filter((path) => /\bumbConfirmModal\b|\bUMB_CONFIRM_MODAL\b/.test(readFileSync(path, 'utf8')))
      .map((path) => relative(root, path).split(sep).join('/'));
    expect(direct).toEqual(['ui/confirm.ts']);
    expect(
      sources(aiRoot).filter((path) => /\bumbConfirmModal\b|\bUMB_CONFIRM_MODAL\b/.test(readFileSync(path, 'utf8'))),
    ).toEqual([]);
    expect(readFileSync(join(root, 'ui', 'confirm.ts'), 'utf8')).toContain('content: html`${data.content}`');
  });

  it('are the only way anything reaches the page as markup: nothing uses unsafeHTML', () => {
    const unsafe = [...sources(root), ...sources(aiRoot)]
      .filter((path) => /\bunsafeHTML\b/.test(readFileSync(path, 'utf8')))
      .map((path) => relative(root, path).split(sep).join('/'));
    expect(unsafe).toEqual([]);
  });

  it('list what a step sets off, each item named with its line, all of it as text', async () => {
    await confirmList(document.body as never, {
      headline: 'Run the actions?',
      content: 'These run <b>for real</b>.',
      items: [
        { name: '<img src=x onerror=alert(1)>', detail: 'To team@example.com' },
        { name: 'Webhook', detail: '' },
      ],
      confirmLabel: 'Run 2 actions',
    });
    const data = asked.data[0];
    const shown = document.createElement('div');
    render(data?.content as TemplateResult, shown);
    expect([data?.headline, data?.confirmLabel]).toEqual(['Run the actions?', 'Run 2 actions']);
    expect(shown.querySelector('p')?.textContent).toBe('These run <b>for real</b>.');
    expect([...shown.querySelectorAll('li')].map((item) => item.textContent?.replace(/\s+/g, ' ').trim())).toEqual([
      '<img src=x onerror=alert(1)> To team@example.com',
      'Webhook',
    ]);
    expect(shown.querySelector('img, b')).toBeNull();
    expect(shown.querySelectorAll('li span')).toHaveLength(1);
  });

  it('are used where the library asks before something people named is trashed, widened or lost', () => {
    const using = sources(root)
      .filter((path) => /confirmText\((this|host),/.test(readFileSync(path, 'utf8')))
      .map((path) => relative(root, path).split(sep).join('/'));
    expect(using).toEqual(
      expect.arrayContaining([
        'library/actions/trash-form.action.ts',
        'library/actions/move.action.ts',
        'library/actions/folder-actions.ts',
        'library/actions/trash-actions.ts',
      ]),
    );
  });
});
