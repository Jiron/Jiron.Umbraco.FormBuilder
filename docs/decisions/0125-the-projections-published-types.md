# 0125. The projection's published types

- Status: accepted; extends [ADR-0002](0002-form-definition-model-versions-and-public-projection.md) (the public
  projection) and [ADR-0035](0035-structural-validation-and-public-projection.md) (what the projection holds)
- Date: 2026-10-31
- Roadmap step: 6.6a (decided and built); 6.6d's element reads the types

## Context

A headless site asks the definition endpoint (`GET /jfb/api/v1/forms/{keyOrAlias}`) for a form and renders it. The
answer's `form` is the visitor form: the public projection with each text in the page's culture
(`VisitorForm`, written through `VisitorJsonContext`). Step 6.6a asks for TypeScript types of the projection,
generated and tested for drift, shipped in the package and in `schemas/`.

Three things are given. The public OpenAPI document (`docs/api/jfb-public.json`) types the answer around the form but
not the form itself: `form` is an untyped JSON value there, because the endpoint writes the projection's bytes as they
are, for its `ETag`. The website runtime and the shared logic read the projection through hand-written interfaces
(`packages/runtime/src/model.ts`, `packages/logic/src/projection.ts`), each holding only what it reads. And the
projection's records use converters a schema exporter cannot look into: localized texts written as one string, the
definition's enums written by name, a rule item that is a condition or a nested group, a condition's operand, a
field's default value and its settings.

## Options

1. **Types from the OpenAPI document** (the management client's generator). The document holds no shape for `form`,
   so the types would say nothing of the projection.
2. **Hand-written types in `schemas/`.** Nothing would notice when the projection changes.
3. **The C# records, through a JSON schema, to TypeScript.** .NET's `JsonSchemaExporter` describes the records with
   the visitor context's own options (camel case, nulls left out, nullable annotations); one table describes the types
   whose converters it cannot see; a small script turns the schema into TypeScript. Chosen.

## Decision

- **The schema:** `schemas/jfb-public-form.v1.schema.json` (JSON Schema 2020-12) describes the visitor form. A unit
  test writes it from the C# records through `VisitorJsonContext`'s own metadata (`JsonTypeInfo`: the properties in
  the order they are written, those that may be null left out of `required`, a polymorphic record as one of its
  kinds), a definition named after each record, so the types read well; the converters' types are described by one
  table (`PublicFormSchema`), and a type in neither fails the test. `JsonSchemaExporter` was tried first: it writes
  references as pointers into the middle of the schema, which would make nameless types. The test compares the
  schema with the committed file, and `JFB_UPDATE_SNAPSHOTS=1` writes it for review. The full fixture's visitor form,
  and those of generated definitions, validate against it, so the schema says what the endpoint writes.
- **The types:** `schemas/jfb-public-form.v1.d.ts` holds a type per schema definition, made from the schema by
  `scripts/generate-public-types.mjs` (Node, no dependency): object types rather than interfaces, so a record fits
  where the runtime reads a map of texts. Its test fails when the committed types are not the ones the committed
  schema makes, and shows that a changed schema changes them.
- **The runtime and the logic** keep their narrower interfaces, and a type-level check
  (`packages/runtime/src/projection-types.check.ts`) asserts that the published form fits what they read, so the
  client's type check fails when the projection drifts from them.
- **Shipped:** both files are in `schemas/` and in the package's `schemas/` folder, beside the definition's schemas.
- **The chain of checks:** C# to schema (the unit test), schema to types (the script's test), types to the runtime
  and the logic (the type check), and the projection's content against its review fixture (ADR-0035), each failing on
  its own link.

## Consequences

- A change to a projection record fails the schema test until the schema, the types and, where the runtime reads the
  change, its interfaces follow.
- 6.6d's element reads the published types for what it renders.
- The first run of the check found a drift: the logic and the runtime typed a form-level validation rule as a rule
  with an `effect`, which the endpoint never writes. They now share `ProjectedValidationRule`.
- The definition endpoint's answer around the form stays described by the OpenAPI document.
