# 0063. The calculation language

- Status: accepted; amends [ADR-0005](0005-shared-logic-and-test-vectors.md) (the `formula` case of the vectors and
  the position of a formula's error), [ADR-0035](0035-structural-validation-and-public-projection.md) (calculations at
  publish), [ADR-0056](0056-field-settings-checks.md) (the formula's length) and
  [ADR-0061](0061-the-form-rules-engine.md) (calculations in the form engine)
- Date: 2026-10-26
- Roadmap step: 5.3a (decided and built); 5.4b stores the result, 5.4d recomputes it in a submission's answers, which
  6.1's pipeline runs, 6.4 shows it in the visitor's browser as they type, 9.x edits the formula and points at its
  problems

## Context

§4.3.3 gives the `calculation` field a tiny expression language: numbers, field references, `+ - * / ( )`, `min`,
`max`, `round`, `if`, and `daysBetween` and `yearsBetween` over date fields in the site's time zone, parsed into a
tree, with no evaluation of code and no reflection. The server recomputes every calculation, and the result is stored
rounded to the field's `format.decimals` on the exact decimals of ADR-0005. Both runtimes implement it (D10), so the
browser shows what the server stores. ADR-0030 asks for a limit on how deeply calculations nest, because both
evaluators recurse; ADR-0035 left publishing's checks of calculations to this step: a calculation that names no field
or depends on itself is refused.

What §4.3.3 leaves open:

1. **The syntax:** how a formula names a field, what a number looks like, and what `if` decides on.
2. **What an answer counts as:** an empty, hidden or unreadable answer, several answers, and each kind of field.
3. **The date functions:** date-times, 29 February, a later date first, and "today".
4. **Errors:** which mistakes the editor sees before publishing, and what a visitor sees when an answer makes the
   arithmetic fail (a division by zero).
5. **Where calculations sit in the form engine:** their order, repeatable groups, and what rules read.
6. **Limits** that keep both parsers and evaluators bounded.

## Options

1. **What `if` decides on.**
   - **A number:** true when it is not zero. Small, but without comparisons a formula cannot say "over 10 pieces".
   - **A boolean grammar of its own** (`if a > 10 and b`): a second set of precedence rules beside arithmetic, and a
     parenthesis that may open either kind, which a recursive parser can only tell apart by trying both.
   - **Comparisons are values:** `a > 10` is 1 or 0, so a condition is a number like any other; `and`, `or` and
     `not` are functions. One grammar, the way spreadsheets read it. Chosen.
2. **How a formula names a field.**
   - **Keys,** as rules do: exact, but unreadable in a formula an editor types.
   - **Braces around aliases** (`{quantity}`): readable, but noisier than needed, since aliases are already names.
   - **Bare aliases** (`quantity * price`): what editors see in the designer. A name followed by `(` is a function,
     so a field may even be called `min`. Chosen.
3. **An empty answer.**
   - **No value:** a total stays blank until every optional field is filled in, which no one expects of a sum.
   - **Zero,** as an empty cell in a spreadsheet. Chosen for numbers. A date has no zero, so an empty date leaves the
     calculation without a value.
4. **A failure caused by the answers** (a division by zero, an unreadable answer).
   - **An error the visitor sees:** the visitor cannot fix a formula.
   - **No value:** the field shows nothing, stores nothing, and rules read it as empty. Chosen; the editor's own
     mistakes are refused before publishing instead.

## Decision

### The grammar

```text
formula    = comparison ;
comparison = sum , [ ( "=" | "!=" | "<" | "<=" | ">" | ">=" ) , sum ] ;
sum        = product , { ( "+" | "-" ) , product } ;
product    = unary , { ( "*" | "/" ) , unary } ;
unary      = { "-" } , primary ;
primary    = number | call | alias | "(" , comparison , ")" ;
call       = name , "(" , [ comparison , { "," , comparison } ] , ")" ;
number     = "0" | nonzero digit , { digit } , [ "." , digit , { digit } ] ;   (* the wire form without a sign *)
alias      = lowercase letter , { letter | digit } ;                           (* a field's alias, ADR-0002 *)
name       = letter , { letter | digit } ;
```

- Spaces, tabs and line breaks may stand between tokens. ASCII only: any other character is a syntax error.
- A name followed by `(` calls a function; any other name is a field's alias, compared exactly.
- One comparison per level: `a < b < c` is a syntax error, `(a < b) + (b < c)` is fine. Comparisons are exact and
  give 1 or 0.
- `-` before a value negates it; several negate in turn.

### Functions

| Function | Takes | Gives |
| --- | --- | --- |
| `min(a, b, …)`, `max(a, b, …)` | two or more numbers | the smallest or the largest |
| `round(x)`, `round(x, places)` | a number, and a whole number 0 to 20 written as it is | `x` rounded half away from zero, to no places without `places` |
| `if(condition, then, else)` | three numbers | `then` when the condition is not 0, `else` when it is; only the branch taken is worked out |
| `and(a, b, …)`, `or(a, b, …)` | two or more numbers | 1 or 0, worked out left to right, stopping at the first argument that decides |
| `not(a)` | one number | 1 when `a` is 0, 0 otherwise |
| `daysBetween(from, to)` | two dates | the days from `from`'s date to `to`'s date |
| `yearsBetween(from, to)` | two dates | the whole years from `from` to `to`, as an age counts them |
| `today()` | nothing | the site's date (ADR-0005), only as an argument of the two date functions |

- A date is a date field, a date-time field or `today()`, written directly as the argument; dates take no arithmetic.
- **Days:** `daysBetween` subtracts the calendar dates, the date part of a date-time included. Date-times are
  wall-clock values without a zone, so a daylight-saving change never adds or loses a day; "today" is the local date
  of the instant in the site's time zone, as rules read it, near a change too.
- **Years:** whole years from `from` to `to`, a year being complete on the same month and day; from 29 February it
  is complete on 1 March in a common year. A later `from` gives the years from `to` to `from`, negated.
- The results are exact decimals: division keeps 20 places, `round` and the stored result round half away from zero,
  and an intermediate result past 60 digits is out of range (ADR-0005).

### What a field gives a formula

The answers as rules read them: cleaned, empty ones dropped, and none for a hidden field (hidden means gone). Then:

- number, slider, rating, scale and calculation fields (kind `number`): the number;
- dropdowns, radio buttons and checkboxes (kinds `choice` and `choices`): the chosen options' values, as numbers;
- a checkbox (kind `checkbox`): 1 when checked, 0 when not;
- several answers (several choices, or a repeatable group's child field read from outside its rows) add up; no
  answer is 0;
- date and date-time fields give their dates to the date functions, and nothing else.

Any other kind (text, files, a time) is refused (`operator_not_for_kind`), as is a date outside a date function or a
number inside one.

### No value

A calculation has no value when an answer it reads is not in its wire form, a date it needs is empty, or the arithmetic
fails: a division by zero or an intermediate result past 60 digits. A value it never works out does not count: in
`if(quantity = 0, 0, total / quantity)` an empty `quantity` gives 0. Without a value the field shows nothing, stores
nothing, and rules and other calculations read it as empty. The evaluator itself reports the arithmetic's failure
(`division_by_zero`, `out_of_range`), and the form engine turns it into no value.

### Mistakes in the formula

The parser and the check report each mistake with a code and the position of the token where it starts (a UTF-16
index), so the form check can point at it:

| Code | Mistake |
| --- | --- |
| `invalid_formula` | the formula does not follow the grammar, or is longer than 1,000 characters (new) |
| `unknown_operator` | a function that does not exist |
| `invalid_operand` | the wrong number of arguments, `places` that is not a whole number 0 to 20, `today()` outside a date function, or a date function's argument that is neither a field nor `today()` |
| `too_deep` | parentheses and calls nested deeper than 20 levels |
| `out_of_range` | a number with more than 60 digits |
| `unknown_field` | an alias the form does not have, or a field whose answers a formula cannot read (a heading, a name) |
| `operator_not_for_kind` | a field whose kind the place does not take |
| `constant_formula` | a formula that names no field, so its result never changes (new) |
| `cycle` | a calculation that depends on itself, through other calculations or through whether it is shown |

The limits (1,000 characters, 20 levels, 60 digits) keep parsing and evaluation linear in the formula's length.
Negations are counted, not recursed into. The setting itself takes at most 1,000 characters (ADR-0056).

### In the form engine

- A field may carry its alias and a calculation (its formula and its decimals, 0 to 15, 2 when unset). The form's
  check parses and checks every calculation before anything is evaluated, with the aliases of the fields the form has
  (an alias given twice is `invalid_form`).
- A calculation comes after the fields it reads in the dependency order, so their visibility is final when it is
  worked out. A shown calculation is worked out over the answers of its scope, a repeatable group's row included, as
  conditions read them, and rounded to its decimals.
- Rules, other calculations and validation read the rounded result, never what was posted for the field.
- The engine's answer gains `calculations`: the value of every shown calculation outside repeatable groups, as
  canonical text or null without a value, and each row of a repeatable group has its own.

### Publishing

Publishing runs the same check at `…fields[n].settings.formula` once the setting itself holds (ADR-0056), and its
message says at which character, counted from 1, the mistake starts. It checks against the fields the visitor's
browser has, as the engine there does: a field whose value the server sets when the entry is submitted is
`invalid_reference`, as it is for a condition (ADR-0035). A formula that names a field whose type the site lacks waits
for the type, as a condition does. A calculation that depends on itself joins the cycle check of visibility, since
both are one graph: an element waits for what holds it, for the fields its show or hide rule looks at, and, for a
calculation, for the fields its formula reads. Each cycle is reported once, at its first element that has a show or
hide rule or a formula.

### Vectors and other tests

`calculations.json` adds the `formula` case: `{ "formula": text }` with the file's `fields` (kinds by alias), the
case's `values` and `context`, and `expect` as canonical text, null without a value, or `{ "error": code, "at":
position }` for a mistake in the formula. Its `form` cases carry calculation fields, and every `form` case expects
`calculations`.

Each runtime also runs 10,000 random formulas that a seeded generator builds from the grammar and then breaks by
chance, over random answers (`Logic/FormulaFuzzTests.cs`, `packages/logic/test/formulas.test.ts`). Each formula gives
a result or a `LogicError`, nothing else, and takes at most 50 ms, measured as the fastest of three runs so that a
garbage collection or the first compilation of a path does not count against it; the formulas reach every outcome,
each at least 20 times. The mutation check of ADR-0061 covers the language and the engine's calculations in
TypeScript, and C# mutants cover where .NET's own character classes, comparers and number parsing would differ.

## Consequences

- §4.3.3 states the grammar's summary, what fields give, no value, and the codes; ADR-0005 gains the `formula` case
  and the position of a formula's error; ADR-0061 gains calculations in the engine.
- 5.4b stores a result with more than 15 digits as text only (ADR-0005); 5.4d recomputes every calculation in a
  submission's answers, which 6.1 runs, and ignores what is posted for it (ADR-0071); 6.4 shows the result as the
  visitor types; 9.x checks the formula as the editor types, with the positions, and offers the fields a formula may
  name.
- A rename of a field's alias breaks the formulas that name it, as a deleted field does; publishing names them, and the
  designer updates them when it renames (9.x).
- **Revisit** if editors need text in a calculation (a joined name, a code): that is a merge tag's job, and this
  language stays numeric.
