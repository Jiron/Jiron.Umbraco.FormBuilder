// SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
/**
 * The one commit identity this repository accepts (CLAUDE.md rule 9, ROADMAP 0.1): GitHub's noreply address
 * for the owner's account, so no personal address ever reaches a repository that becomes public.
 */
export const REQUIRED_NAME = 'Jiron';
export const REQUIRED_EMAIL = '59139789+Jiron@users.noreply.github.com';
export const REQUIRED_IDENT = `${REQUIRED_NAME} <${REQUIRED_EMAIL}>`;

/** Parses the output of `git var GIT_AUTHOR_IDENT` ("Name <email> 1700000000 +0100") into `{ name, email }`. */
export function parseIdent(ident) {
  const match = /^(.*?) <([^>]*)>/.exec(ident.trim());
  return match ? { name: match[1], email: match[2] } : null;
}

/**
 * The problems with an author and a committer ident, as sentences. An empty list means both are the required
 * identity. The sentences name the role but never the offending address.
 */
export function identityProblems({ author, committer }) {
  const problems = [];
  for (const [role, ident] of [
    ['author', author],
    ['committer', committer],
  ]) {
    const parsed = parseIdent(ident ?? '');
    if (!parsed || parsed.name !== REQUIRED_NAME || parsed.email !== REQUIRED_EMAIL) {
      problems.push(`the ${role} is not ${REQUIRED_IDENT}`);
    }
  }
  return problems;
}
