// The form workspace's words (ADR-0201): they load for a user's language, German with English beside it, so a word
// German lacks falls back as Umbraco's own do.
import { umbLocalizationManager } from '@umbraco-cms/backoffice/localization-api';
import { describe, expect, it } from 'vitest';
import de from '../lang/workspace.de';
import en from '../lang/workspace.en';
import { loadWorkspaceWords } from './workspace-words';

describe("the form workspace's words", () => {
  it('load for a user of German in German, with English beside it', async () => {
    await loadWorkspaceWords('de-DE');
    const wordIn = (code: string) =>
      (umbLocalizationManager.localizations.get(code) as Record<string, unknown> | undefined)?.['jfbCheck_fix'];
    expect([wordIn('de'), wordIn('en')]).toEqual([de.jfbCheck.fix, en.jfbCheck.fix]);
    expect(de.jfbCheck.fix).not.toBe(en.jfbCheck.fix);
  });
});
