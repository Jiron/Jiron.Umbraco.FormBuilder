# 0130. The form block type

- Status: accepted; extends [ADR-0050](0050-management-api-base.md) (an access declaration may name a section of
  Umbraco's own)
- Date: 2026-10-31
- Roadmap step: 6.7b (decided and built); the Overview's checklist and Settings › General show its button (Phase 9
  and 10)

## Context

Editors put a form on a page with a block: an element type with a form picker property, allowed in a block list or
grid. "Create a form block type for me" makes it for a site that has none (section 4.7, "Setup and health"). It
creates Umbraco types, so it needs `manageSettings` and Umbraco's Settings section, and it must never change a type
an editor or a package made.

## Options

- **What it creates:** (a) a whole block list data type with the block in it: the site's own block lists, their
  other blocks and their settings are the editor's, and a second list beside them helps no one; (b) the element
  type and the picker's data type, which the editor then allows in their own lists. (b) is chosen.
- **How it finds what is there:** the element type by its alias, as Umbraco's types are found; the data type by a
  key fixed for good, since data type names repeat.
- **Where Umbraco's Settings section is checked:** (a) Umbraco's own authorization policy on the action, which runs
  before the access filter and so answers a user without the Forms section with Umbraco's refusal instead of
  `403 no_section`; (b) the access declaration names the section and the access filter checks it after the
  permissions, with the same refusal body as a missing permission. (b) is chosen.

## Decision

- `POST setup/form-block` creates the data type "Jiron Form Builder form picker" (key
  `c4f1a9e2-6b3d-4e58-9a07-2d8c5f1e3b64`, one form, no limits) and the element type `jfbFormBlock` ("Form", an
  element, with the mandatory property `form` of that data type) where each is missing, as the user who asked. A
  content type with the alias, of any kind, and the data type with the key stay as they are, however they were
  changed. The answer names both with their key, alias or name, and whether this call created each.
- An access declaration's `UmbracoSection` names a section the user needs besides the permissions. The access filter
  checks it last before the request's values; a user without it gets `403 forbidden` with `section` naming it, as a
  missing permission gets `permission`.

## Consequences

- An editor allows the block in a block list or grid themselves; the checklist item says how (Phase 9).
- The permission matrix holds the endpoint with `manageSettings`; the Settings section is checked by the endpoint's
  own test.
