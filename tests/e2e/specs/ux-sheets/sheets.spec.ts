import { existsSync, mkdirSync, readFileSync, writeFileSync } from 'node:fs';
import { join } from 'node:path';
import { expect, test } from '../../fixtures.ts';
import {
  COMBOS,
  comboName,
  sheetHtml,
  walkDir,
  wordChanges,
  WORDS_NOISE,
  type Combo,
  type SheetCell,
  type ShotReport,
  type WalkSummary,
} from '../../support/ux-walk.ts';

// The walk's contact sheets (ROADMAP 9.6a): after the ux project's walks and journeys (16.6), one sheet per screen with its twelve shots,
// rows the widths and columns the looks, and summary.json with what each shot found, screen by screen, for the review.

test("the walk's contact sheets", async ({ page }) => {
  test.setTimeout(10 * 60_000);
  const dir = walkDir();
  // The walk's shots, then the journeys' (journeys.spec.ts), each look in its own folder.
  const reports = COMBOS.map((combo) => {
    const screens = ['report.json', 'journeys.json'].flatMap((name) => {
      const file = join(dir, comboName(combo), name);
      return existsSync(file) ? (JSON.parse(readFileSync(file, 'utf8')) as { screens: ShotReport[] }).screens : [];
    });
    return { combo, screens };
  });
  const screens = [...new Set(reports.flatMap((report) => report.screens.map((shot) => shot.screen)))];
  const sheets = join(dir, 'sheets');
  mkdirSync(sheets, { recursive: true });
  const summary: Record<string, Record<string, Omit<ShotReport, 'screen' | 'file'>>> = {};
  await page.setViewportSize({ width: 2000, height: 1400 });
  for (const screen of screens) {
    const cells: SheetCell[] = reports.map(({ combo, screens: shots }: { combo: Combo; screens: ShotReport[] }) => {
      const shot = shots.find((each) => each.screen === screen);
      const image =
        shot?.file && existsSync(join(dir, comboName(combo), shot.file))
          ? `data:image/png;base64,${readFileSync(join(dir, comboName(combo), shot.file)).toString('base64')}`
          : null;
      if (shot) {
        const { screen: _screen, file: _file, ...found } = shot;
        (summary[screen] ??= {})[comboName(combo)] = found;
      }
      const counts = shot && !shot.error ? { words: shot.words, navigation: shot.navigation } : {};
      return {
        combo,
        image,
        ...counts,
        ...(shot?.error ? { note: shot.error } : shot ? {} : { note: 'not walked' }),
      };
    });
    await page.setContent(sheetHtml(screen, cells));
    await page.screenshot({ path: join(sheets, `${screen}.png`), fullPage: true });
  }
  writeFileSync(join(dir, 'summary.json'), JSON.stringify(summary, null, 2));

  // With an earlier walk to compare with (JFB_UX_BEFORE names its folder), every screen's words in both: words.json
  // holds them all, and no screen may have grown by more than the few words a time of day or a count changes. A list's
  // rows are data, though, and two looks share one site: a screen that grew by a row someone has looked at is named in
  // JFB_UX_GROWN (comma-separated), and the review says why.
  const earlier = process.env.JFB_UX_BEFORE ? join(process.env.JFB_UX_BEFORE, 'summary.json') : null;
  if (earlier && existsSync(earlier)) {
    const changes = wordChanges(JSON.parse(readFileSync(earlier, 'utf8')) as WalkSummary, summary);
    writeFileSync(join(dir, 'words.json'), JSON.stringify(changes, null, 2));
    expect(changes.length, 'screens that both walks reached').toBeGreaterThan(0);
    const explained = new Set((process.env.JFB_UX_GROWN ?? '').split(',').map((name) => name.trim()));
    expect(
      changes
        .filter((change) => change.after - change.before > WORDS_NOISE && !explained.has(change.screen))
        .map((change) => `${change.screen} (${change.look}): ${change.before} to ${change.after}`),
      'screens that show more words than before',
    ).toEqual([]);
  }
});
