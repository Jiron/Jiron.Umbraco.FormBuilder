/**
 * The one audited way markup from a string reaches the website code (ROADMAP section 4.10): the server's sanitised
 * editor HTML, parsed in an inert `<template>`, where nothing runs or loads while it is parsed. Its callers are the
 * rich texts that recall answers, which copy texts and attribute values over and never a node (ADR-0115), the
 * outcome's message, the entries endpoint's sanitised HTML, whose nodes take the form's place (ADR-0120), and the form
 * as it is now, the `render` endpoint's markup of the same views, after `form_changed` (ADR-0004).
 */
export function parseSanitised(document: Document, html: string): DocumentFragment {
  const parsed = document.createElement('template');
  // eslint-disable-next-line no-restricted-properties -- the one audited helper of section 4.10
  parsed.innerHTML = html;
  return parsed.content;
}
