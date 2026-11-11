// SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
/**
 * The licence lists of CLAUDE.md rule 4 and ADR-0027, and an SPDX expression reader, shared by the npm and NuGet
 * licence checks and the third-party notices (ROADMAP 3.5).
 *
 *   - shipped: what reaches a site may only be MIT, Apache-2.0, BSD-2-Clause, BSD-3-Clause, ISC or 0BSD;
 *   - dev: tools that never ship may also be MPL-2.0, BlueOak-1.0.0, CC-BY-4.0 (data) or Python-2.0, and the
 *     public-domain dedications MIT-0, Unlicense and CC0-1.0 (ADR-0027);
 *   - forbidden everywhere: the GPL family, SSPL, and source-available or non-commercial licences.
 */

/** Rule 4's list for anything that ships to sites. */
export const SHIPPED = Object.freeze(['MIT', 'Apache-2.0', 'BSD-2-Clause', 'BSD-3-Clause', 'ISC', '0BSD']);

/** The public-domain dedications that ADR-0027 accepts in development tools. */
export const PUBLIC_DOMAIN = Object.freeze(['MIT-0', 'Unlicense', 'CC0-1.0']);

/** Everything a development tool may use: rule 4's two lists plus ADR-0027. */
export const DEV = Object.freeze([...SHIPPED, 'MPL-2.0', 'BlueOak-1.0.0', 'CC-BY-4.0', 'Python-2.0', ...PUBLIC_DOMAIN]);

/** The licence lists by tier. */
export const ALLOWED = Object.freeze({ shipped: new Set(SHIPPED), dev: new Set(DEV) });

/** Spellings seen in package metadata, read as the SPDX id they mean (ADR-0027). */
export const ALIASES = new Map([
  ['BSD 3-Clause', 'BSD-3-Clause'],
  ['BSD 2-Clause', 'BSD-2-Clause'],
  ['Apache 2.0', 'Apache-2.0'],
  ['Apache License 2.0', 'Apache-2.0'],
  // The address of tv4's statement that its code is in the public domain, which uri-templates 0.2.0 (brought by the
  // storage emulator Azurite, a development tool, ADR-0234) gives as its licence: a dedication ADR-0027 accepts.
  ['http://geraintluff.github.io/tv4/LICENSE.txt', 'Unlicense'],
]);

/**
 * Licence ids that rule 4 forbids anywhere: the GPL family, SSPL, and source-available or non-commercial terms.
 * npm's UNLICENSED (all rights reserved) is not the Unlicense.
 */
const FORBIDDEN_ID =
  /^(?:A?GPL|LGPL|SSPL|BUSL|Elastic|PolyForm|Commons-Clause|CC-BY-NC|CC-BY-ND|CC-BY-SA|CPAL|OSL|EUPL|RPL|Sleepycat|UNLICENSED$)/i;

/** True when a single licence id is one rule 4 forbids anywhere. */
export function isForbiddenId(id) {
  return FORBIDDEN_ID.test(id);
}

/** Text that marks a licence file as one of the forbidden families. "Commercial" alone is not one: Apache-2.0 says it. */
const FORBIDDEN_TEXT = [
  /GNU (?:AFFERO |LESSER |LIBRARY )?GENERAL PUBLIC LICEN[CS]E/i,
  /SERVER SIDE PUBLIC LICEN[CS]E/i,
  /BUSINESS SOURCE LICEN[CS]E/i,
  /ELASTIC LICEN[CS]E/i,
  /POLYFORM/i,
  /COMMONS CLAUSE/i,
];

/** The forbidden family a licence text names, or undefined. */
export function forbiddenText(text) {
  return FORBIDDEN_TEXT.find((pattern) => pattern.test(text))?.source;
}

/** Licence URLs that point at a forbidden family. */
const FORBIDDEN_URL = [/gnu\.org\/licenses/i, /opensource\.org\/licenses\/(?:A?GPL|LGPL)/i, /server-side-public-license/i, /\/BUSL/i];

/** True when a licence URL points at a forbidden family. */
export function isForbiddenUrl(url) {
  return FORBIDDEN_URL.some((pattern) => pattern.test(url));
}

/**
 * Parses an SPDX licence expression into a tree: { id } | { and: [..] } | { or: [..] }. AND binds tighter than OR,
 * parentheses group, and `X WITH exception` counts as X (an exception only adds permissions). A whole expression
 * spelled as an alias reads as its SPDX id. Throws on a malformed expression.
 */
export function parseExpression(expression) {
  const whole = String(expression ?? '').trim();
  if (!whole) throw new Error('empty licence expression');
  if (ALIASES.has(whole)) return { id: ALIASES.get(whole) };
  const tokens = whole.match(/\(|\)|[^\s()]+/g) ?? [];
  let at = 0;
  const peek = () => tokens[at];
  const next = () => tokens[at++];
  const keyword = (word) => peek()?.toUpperCase() === word;

  function primary() {
    const token = next();
    if (token === undefined) throw new Error(`licence expression "${whole}" ends early`);
    if (token === '(') {
      const inner = or();
      if (next() !== ')') throw new Error(`licence expression "${whole}" lacks a closing parenthesis`);
      return inner;
    }
    if (token === ')' || ['AND', 'OR', 'WITH'].includes(token.toUpperCase())) {
      throw new Error(`licence expression "${whole}" has "${token}" where a licence belongs`);
    }
    if (keyword('WITH')) {
      next();
      if (peek() === undefined || peek() === '(' || peek() === ')') throw new Error(`licence expression "${whole}" lacks its exception`);
      next();
    }
    return { id: token };
  }
  function and() {
    const parts = [primary()];
    while (keyword('AND')) {
      next();
      parts.push(primary());
    }
    return parts.length === 1 ? parts[0] : { and: parts };
  }
  function or() {
    const parts = [and()];
    while (keyword('OR')) {
      next();
      parts.push(and());
    }
    return parts.length === 1 ? parts[0] : { or: parts };
  }

  const tree = or();
  if (at < tokens.length) throw new Error(`licence expression "${whole}" has "${tokens[at]}" after its end`);
  return tree;
}

/** Every licence id an expression names. */
export function expressionIds(expression) {
  const ids = [];
  const walk = (node) => (node.id ? ids.push(node.id) : (node.and ?? node.or).forEach(walk));
  walk(parseExpression(expression));
  return ids;
}

/**
 * True when an expression is acceptable with a set of allowed ids: an OR needs one acceptable side, an AND needs
 * every side. A missing or malformed expression is not acceptable.
 */
export function satisfies(expression, allowed) {
  let tree;
  try {
    tree = parseExpression(expression);
  } catch {
    return false;
  }
  const ok = (node) => (node.id ? allowed.has(ALIASES.get(node.id) ?? node.id) : node.and ? node.and.every(ok) : node.or.some(ok));
  return ok(tree);
}
