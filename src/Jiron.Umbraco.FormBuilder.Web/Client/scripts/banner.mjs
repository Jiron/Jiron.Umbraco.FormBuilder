// SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
// The licence notice that every emitted JS and CSS file keeps (ROADMAP 3.2, ADR-0026). The PolyForm Perimeter
// License asks that everyone who gets a copy of any part of the software also gets the terms or their URL, and the
// licensor's lines that begin with "Required Notice:". The banner carries both; the notice lines are read from
// LICENSE.md and keep a line of their own.
import { readFileSync } from 'node:fs';
import { join } from 'node:path';
import { repositoryRoot } from './version.mjs';

export const LICENCE_URL = 'https://polyformproject.org/licenses/perimeter/1.0.1';
export const licensePath = join(repositoryRoot, 'LICENSE.md');

/**
 * The lines of the licence that begin with "Required Notice:", in order.
 * @param {string} licenseText
 */
export function requiredNotices(licenseText) {
  const notices = licenseText
    .split(/\r?\n/)
    .map((line) => line.trim())
    .filter((line) => line.startsWith('Required Notice:'));
  if (notices.length === 0) throw new Error('LICENSE.md has no "Required Notice:" line');
  return notices;
}

/**
 * The banner of a build: a legal comment (kept by minifiers) with the version, the terms' URL and the notices.
 * @param {string} version
 * @param {string | undefined} [licenseText]
 */
export function licenceBanner(version, licenseText = readFileSync(licensePath, 'utf8')) {
  return [
    `/*! Jiron Form Builder ${version}. Licensed under the PolyForm Perimeter License 1.0.1: ${LICENCE_URL}`,
    ...requiredNotices(licenseText),
    '*/',
  ].join('\n');
}

/**
 * Why a file's text does not start with the banner of the version, or null when it does.
 * @param {string} text
 * @param {string} version
 * @param {string | undefined} [licenseText]
 * @returns {string | null}
 */
export function bannerProblem(text, version, licenseText = readFileSync(licensePath, 'utf8')) {
  if (text.startsWith(`${licenceBanner(version, licenseText)}\n`)) return null;
  if (!text.startsWith('/*!')) return 'has no licence banner';
  const lines = text.slice(0, text.indexOf('*/') + 2).split('\n');
  if (!requiredNotices(licenseText).every((notice) => lines.includes(notice))) return 'has no Required Notice line';
  return `has a banner that is not the one of ${version}`;
}
