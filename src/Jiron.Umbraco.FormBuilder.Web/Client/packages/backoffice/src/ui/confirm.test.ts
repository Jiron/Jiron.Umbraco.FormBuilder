import { readdirSync, readFileSync, statSync } from 'node:fs';
import { join, relative, sep } from 'node:path';
import { describe, expect, it } from 'vitest';

// The confirm dialogs show their content as text (section 4.10): Umbraco's confirm modal renders a string as HTML, so a form
// named like <img onerror=...> would run in the backoffice. Only ui/confirm.ts may open that modal, and it passes the content
// as a Lit template; the raw modal and unsafeHTML appear nowhere else.

const root = join(import.meta.dirname, '..');

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
    expect(readFileSync(join(root, 'ui', 'confirm.ts'), 'utf8')).toContain('content: html`${data.content}`');
  });

  it('are the only way anything reaches the page as markup: nothing uses unsafeHTML', () => {
    const unsafe = sources(root)
      .filter((path) => /\bunsafeHTML\b/.test(readFileSync(path, 'utf8')))
      .map((path) => relative(root, path).split(sep).join('/'));
    expect(unsafe).toEqual([]);
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
