// SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
/**
 * Jiron Form Builder's migration plan as the install test reads it (ROADMAP 16.5): the states its steps reach, read
 * from the plan's source, so the test never names a final state that a later step has moved on from (one source for
 * every value, ROADMAP "How to use").
 */
import { readFileSync } from 'node:fs';
import { join, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';

const ROOT = resolve(fileURLToPath(new URL('../..', import.meta.url)));

/** The constants that name the plan Umbraco stores its state under. */
export const CONSTANTS = join(ROOT, 'src', 'Jiron.Umbraco.FormBuilder.Core', 'FormBuilderConstants.cs');

export const PLAN = join(
  ROOT,
  'src',
  'Jiron.Umbraco.FormBuilder.Core',
  'Persistence',
  'Migrations',
  'FormBuilderMigrationPlan.cs',
);

/** The plan's steps in order, each with its state's constant and value: `(StateX, typeof(StepY))` in `Steps`. */
export function steps(planText = readFileSync(PLAN, 'utf8')) {
  const list = planText.match(/Steps\s*=\s*\[([\s\S]*?)\];/)?.[1];
  if (!list) throw new Error('the migration plan has no Steps list');
  const found = [...list.matchAll(/\((\w+),\s*typeof\((\w+)\)\)/g)].map(([, constant, step]) => {
    const value = planText.match(new RegExp(`const string ${constant} = "([^"]+)"`))?.[1];
    if (!value) throw new Error(`the migration plan's constant ${constant} has no value`);
    return { constant, step, state: value };
  });
  if (found.length === 0) throw new Error('the migration plan has no steps');
  return found;
}

/** The state the plan's last step reaches. */
export const finalState = (planText) => steps(planText).at(-1).state;

/** The name Umbraco keeps the plan's state under (FormBuilderConstants.MigrationPlanName). */
export function planName(constantsText = readFileSync(CONSTANTS, 'utf8')) {
  const name = constantsText.match(/MigrationPlanName = "([^"]+)"/)?.[1];
  if (!name) throw new Error('FormBuilderConstants has no MigrationPlanName');
  return name;
}

// `node migration-plan.mjs [plan.cs]` prints the last state of a plan's source: the repository's, or another
// checkout's (the baseline's worktree).
if (resolve(process.argv[1] ?? '') === fileURLToPath(import.meta.url)) {
  console.log(finalState(process.argv[2] ? readFileSync(process.argv[2], 'utf8') : undefined));
}
