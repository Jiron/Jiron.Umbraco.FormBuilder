import { existsSync, mkdirSync, readFileSync, writeFileSync } from 'node:fs';
import { join } from 'node:path';
import { test } from '../../fixtures.ts';
import {
  COMBOS,
  comboName,
  sheetHtml,
  walkDir,
  type Combo,
  type SheetCell,
  type ShotReport,
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
      return { combo, image, ...(shot?.error ? { note: shot.error } : shot ? {} : { note: 'not walked' }) };
    });
    await page.setContent(sheetHtml(screen, cells));
    await page.screenshot({ path: join(sheets, `${screen}.png`), fullPage: true });
  }
  writeFileSync(join(dir, 'summary.json'), JSON.stringify(summary, null, 2));
});
