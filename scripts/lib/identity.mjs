// SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
/**
 * The commit identities this repository accepts (ADR-0265). Its history is public and cannot be rewritten, so by
 * default the author and the committer need a name and one of GitHub's private commit addresses
 * (`...@users.noreply.github.com`): nobody publishes a personal address by accident, the maintainer included. Whoever
 * wants their own address in the history says so once in their clone: `git config jfb.ownEmail true`.
 */
export const NOREPLY_HOST = 'users.noreply.github.com';

/** The maintainer's identity (CLAUDE.md rule 9, ROADMAP 0.1): GitHub's noreply address for the owner's account. */
export const MAINTAINER_NAME = 'Jiron';
export const MAINTAINER_EMAIL = `59139789+Jiron@${NOREPLY_HOST}`;
export const MAINTAINER_IDENT = `${MAINTAINER_NAME} <${MAINTAINER_EMAIL}>`;

/** Parses the output of `git var GIT_AUTHOR_IDENT` ("Name <email> 1700000000 +0100") into `{ name, email }`. */
export function parseIdent(ident) {
  const match = /^(.*?) <([^>]*)>/.exec(ident.trim());
  return match ? { name: match[1], email: match[2] } : null;
}

/** Whether an address is one GitHub gives an account to commit with: `name@...` or `id+name@...` at the noreply host. */
export function isNoreplyAddress(email) {
  const at = email.lastIndexOf('@');
  return at > 0 && !/\s/.test(email) && email.slice(at + 1).toLowerCase() === NOREPLY_HOST;
}

/**
 * The problems with an author and a committer ident, as sentences; an empty list means both may commit. With
 * `ownEmail` any address passes, as long as there is one. The sentences name the role but never the address.
 */
export function identityProblems({ author, committer, ownEmail = false }) {
  const problems = [];
  for (const [role, ident] of [
    ['author', author],
    ['committer', committer],
  ]) {
    const parsed = parseIdent(ident ?? '');
    if (!parsed || parsed.name.trim() === '' || parsed.email.trim() === '') {
      problems.push(`the ${role} has no name or no address`);
    } else if (!ownEmail && !isNoreplyAddress(parsed.email)) {
      problems.push(`the ${role}'s address is not one of GitHub's private commit addresses`);
    }
  }
  return problems;
}
