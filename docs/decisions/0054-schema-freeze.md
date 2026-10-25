# 0054. The schema freeze

- Status: accepted; amends [ADR-0033](0033-schema-v1.md) (step 1 is frozen)
- Date: 2026-10-25
- Roadmap step: 4.8i (decided and built); 16.5 installs the tag

## Context

Step 1 of the migration plan, `CreateSchemaV1`, creates schema v1 from the snapshot DTOs nested in it
([ADR-0033](0033-schema-v1.md)). Through Phase 4 the snapshots changed as the repositories and services needed them,
which was safe while no site had run the step. From the end of Phase 4 on, sites do. The tag `baseline/schema-v1`
marks the schema, and the upgrade test of step 16.5 installs that tag and upgrades it to the current version. A site
that ran step 1 keeps its tables as the step made them, so a later edit of the step would reach only new sites, and
two schemas would sit under one migration state. Three questions were open: what exactly is frozen, how a test holds
it, and how the schema changes from now on.

## Decision

- **Frozen:** step 1 as it shapes the tables:
  - every snapshot DTO of `CreateSchemaV1.Tables`, in its order;
  - every declaration that shapes a table: its name and primary key, its columns in their order with their types,
    names, lengths, nullability, special types and constraints, its indexes, and its foreign keys with their targets;
  - the filtered indexes;
  - the state name `jfb-001-create-schema`.
- **The pin:** `SchemaV1FreezeTests` (Tests.Unit) describes all of this from the declarations alone, by reflection:
  - one line per table and per column, with the attributes sorted and a foreign key's target named by its table;
  - so the description does not depend on the class names, the order of the attributes, the layout of the source, or
    how a version of Umbraco reads the attributes;
  - the SHA-256 of the description is a constant in the test, and the test has no switch that rewrites it;
  - further tests show that each kind of declaration changes the description, and that the class names and the
    layout do not.
- **What stays as it is:** the SQL Server statements under review (`SchemaV1DdlTests`), and the schema read back from
  a migrated database (`MigrationPlanTests` against `ExpectedSchemaV1`). Both hold what Umbraco makes of the
  snapshots on each database, beside the pin, which holds the snapshots themselves.
- **How the schema changes from here:**
  - each change is a new step at the end of `FormBuilderMigrationPlan` (`jfb-004-…`, and so on), with snapshot DTOs
    of its own nested in it;
  - the new step is idempotent as step 1 is: a table, column or index is added only when it is missing;
  - its tests run the plan on a fresh database and on one where only the steps before it ran; from step 16.5 on,
    the upgrade test also runs it on the database of `baseline/schema-v1`;
  - the repositories' product DTOs follow the current schema, and step 1's snapshots never do;
  - `ExpectedSchemaV1` stays the schema of step 1, and a later schema gets an expectation of its own, built on it.
- **Never:**
  - an edit of a frozen snapshot, a table moved in the order, or a renamed step;
  - a step that rewrites step 1's tables in place, unless a data migration that the upgrade test covers goes with it.
- **What may still change in step 1:** only the code around the snapshots, such as its logging or an API that an
  Umbraco major renames on `v18/main`, and only while the pin and the reviewed statements stay the same.

## Consequences

- A change that touches step 1's snapshots fails `SchemaV1FreezeTests`, whose message names this ADR. The fix is a
  new step, not a new constant.
- The tag `baseline/schema-v1` and the pin describe the same schema, and step 16.5 installs the tag.
- A missing index found later costs a step of its own, so the review of 4.8a read the snapshots once more before
  the freeze, and 4.8g added the one the phase review found.
