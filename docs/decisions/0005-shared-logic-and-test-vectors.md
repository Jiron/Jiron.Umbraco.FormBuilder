# 0005. Shared logic and test vectors

- Status: accepted; unsafe patterns at publish and stored calculation results amended in 1.8; the instruction limit
  for patterns, the instant's form and IANA-only time zones amended in 3.4b (ADR-0030); comparisons over the wire
  form's digits amended in 4.8f (ADR-0030); folding and compiling once amended in 4.8h (ADR-0030); whole forms in the
  vectors and the mutation script amended in 5.2a ([ADR-0061](0061-the-form-rules-engine.md)); the `formula` case and
  the position of a formula's mistake amended in 5.3a ([ADR-0063](0063-the-calculation-language.md))
- Date: 2026-10-23
- Roadmap step: 1.5 (decided); built in 3.4 (the vector runners), 4.4 (the time zone and start-up checks), 5.2 (rules
  and validation), 5.3 (merge tags and calculations), 5.4 (number fields), 6.1 and 6.4 (the wire); reviewed in 5.5

## Context

D10 implements rules, validation, merge tags and calculations twice, in C# (authoritative) and in TypeScript (instant
feedback), and requires one shared set of JSON test vectors so that the browser and the server never disagree about
a hidden field or a validation message. Step 1.5 asked for the vector format, the wire formats, time zones and
"today", number semantics and rounding, with stored numbers limited to 15 significant digits (ADR-0003).

The spike wrote a minimal evaluator in both runtimes and ran both against one file of 163 vectors, compared text
handling for every code point, and swept both evaluators with mutants
([research](../research/spike-logic.md)). What it found that shapes the decision:

- **Built-in text handling differs.** The trims and whitespace classes use different sets (U+FEFF, U+0085). Case
  mappings differed on 55 letters added in Unicode 16 and 17: on Windows .NET takes casing from the host's ICU, so a
  server's casing depends on its operating system, and a browser's on its version. .NET's invariant casing leaves the
  Turkish i's alone, JavaScript maps them. .NET's `Normalize` throws for U+FFFE, and its JSON reader throws for an
  escaped lone surrogate. Normalisation otherwise agrees, apart from 20 compositions in scripts added in Unicode 16.
- **Regular-expression dialects differ** (`\d`, `\s`, ".", `$`, the `(?` forms, classes), and JavaScript has no
  linear-time engine: `(a+)+` against 24 characters took 98 ms in Node and doubles with each further character, so
  an editor's pattern can hang a visitor's browser.
- **Neither number type agrees with the other:** doubles round 2.675 to 2.67 and add 0.1 and 0.2 inexactly; C#
  `decimal` rounds half to even by default and has 28 digits.
- **IANA time zones** resolve in .NET on Windows through ICU, and in the browser through `Intl`; both agree around
  daylight-saving changes. Invariant globalization breaks normalisation and time zones.
- **A single broken operator proves little:** making `greaterThan` inclusive passed all 115 vectors of the first file.
  A sweep of 61 mutants found 24 behaviours no vector pinned.

## Options

**Comparing text without case:**

1. **Each runtime's built-in casing.** Agrees for old letters only; differs by host and browser version for new ones.
2. **A shipped table of case mappings,** identical everywhere. It would ship Unicode data, which falls outside the
   licences CLAUDE.md rule 4 allows for what ships to sites, and it grows with every Unicode version.
3. **Built-in casing, limited to blocks whose mappings every supported runtime has had for years** (Latin, Greek,
   Cyrillic, Armenian, full-width Latin); other scripts compare exactly. No data shipped, and deterministic.

**Patterns:**

1. **Each runtime's engine with a translator** to one safe subset, `NonBacktracking` on the server. Agrees on meaning,
   but cannot protect a browser.
2. **Jiron Form Builder's own matcher** in both runtimes: the subset parsed into a tree, compiled to a Thompson
   automaton, run over code points in linear time. Identical by construction; no engine involved.

**Numbers:** C# `decimal` with a JavaScript emulation of it; doubles in both; or **an exact decimal** (a big integer
and a scale) in both, with written rounding rules.

**Vectors:** files per area of one format, or test code per runtime; and expected values as JSON values, or as
strings per runtime.

## Decision

Option 3 for text, option 2 for patterns, exact decimals for numbers, and one JSON vector format for every area.

- **The vector format `jfb-vectors/1`:** `{ format, suite, defaults: { context }, fields: { alias: kind }, cases }`. A
  case has an `id`, an optional `description`, its `values` (alias to a list of wire strings), `context` and `fields`
  overrides, exactly one thing to run (`condition`, `rule`, `number`, `calc`; 5.2a adds `form`, with the `rows` of
  its repeatable groups; 5.2b `field`, ADR-0062; 5.3a `formula`, ADR-0063; 5.3b `template`), and `expect`: `true`,
  `false`, `null` for a switched-off rule or a calculation without a value, a canonical string, a form's
  `FormState`, or `{ "error": code }`, which for a mistake in a formula also holds `at`, the UTF-16 index where it
  starts (5.3a). Both runners compare the result as JSON. Vector files are written by scripts from code points, as ASCII with JSON escapes. Each runner makes
  one test case per vector and reads the files in place.
- **Wire forms,** invariant and strict:
  - numbers: an optional minus, an integer part without leading zeros, an optional point with at least one digit,
    canonical without trailing fraction zeros or "-0";
  - dates `YYYY-MM-DD`; times `HH:mm` or `HH:mm:ss`; date-times `YYYY-MM-DDTHH:mm(:ss)` as local wall-clock time
    without a zone; all validated as real calendar values in years 1 to 9999;
  - a checked checkbox is "true"; choices and files are their option keys or tokens; text is any string;
  - the instant of evaluation is ISO 8601 with Z or an offset, injected (the server's `TimeProvider`, the browser's
    clock), in one form: `YYYY-MM-DDTHH:mm:ss`, an optional fraction of 1 to 9 digits, and `Z` or `+HH:mm`/`-HH:mm`,
    from 1970 to the end of 9998 in UTC; anything else is `invalid_context` (ADR-0030).
- **Text:**
  - **clean:** lone surrogates and noncharacters become U+FFFD, the text is normalised to NFC, and an explicit list
    of whitespace is trimmed from both ends (the research note lists it);
  - **fold:** in the folded blocks only, each character takes its simple uppercase and then its simple lowercase
    mapping; mappings that would expand are not used; U+0130 and U+0131 fold to "i";
  - text compares folded after cleaning; option keys compare exactly after cleaning; empty answers are dropped.
- **Numbers:**
  - exact decimals in both runtimes (`BigInt`, `BigInteger`); addition, subtraction and multiplication are exact;
  - division keeps 20 places, rounded half away from zero; `round(x, places)` rounds half away from zero, with 0 to
    20 places; more than 60 digits is `out_of_range`; division by zero is `division_by_zero`;
  - a stored number has at most 15 digits in all, leading zeros not counted, and at most 15 after the point
    (`too_many_digits`), so it is exact as a double (ADR-0003); comparisons are exact at any length, over the digits
    of the wire form without a number being built, so they cost what reading the answers costs (amended in 4.8f),
    and a text condition folds each answer once however many operands it has (amended in 4.8h);
  - a calculation's result is stored rounded half away from zero to its `format.decimals` (2 when unset, 0 to 15). A
    result that still has more than 15 digits is stored as text only (`Number` stays null, so it neither filters nor
    sorts) and is never refused, because the visitor did not type it. The rounded result is the field's value
    everywhere: in rules, other calculations, merge tags and the stored entry.
- **Dates and "today":**
  - "today" is the local date of the injected instant in the site's IANA time zone: `Intl.DateTimeFormat` with
    `formatToParts` in the browser, `TimeZoneInfo.FindSystemTimeZoneById` and `ConvertTime` on the server; an unknown
    zone is `unknown_time_zone`, and so are Windows names and offsets, which only one runtime takes (ADR-0030);
  - relative dates are `([+-][0-9]{1,4}[dwmy])*`, applied in written order, with months and years clamped to the end
    of the month; outside years 1 to 9999 they match nothing;
  - calendar arithmetic uses day numbers, never JavaScript `Date`;
  - the browser uses its own clock and the site's zone; the server decides, so a device with a wrong clock sees the
    server's answer as an ordinary validation message.
- **Patterns:** the subset and its limits of the research note (500 code points, counts up to 1,000, at most 2,000
  characters once counted repeats are unrolled), and at most 10,000 instructions in the automaton (ADR-0030), parsed, compiled to a Thompson automaton and run over code points,
  the same algorithm in both runtimes. A pattern matches the whole value, is case-sensitive, and is normalised like a
  value. Anything outside the subset is `unsafe_pattern`: publishing refuses it, and a draft keeps it while the form
  check lists it (ADR-0002). Neither runtime's regular-expression engine runs editors' patterns.
- **Conditions and rules** are checked as a whole before they are evaluated, with the codes `unknown_field`,
  `unknown_operator`, `operator_not_for_kind`, `invalid_operand` and `unsafe_pattern`, so evaluation order never
  changes an outcome. The operator semantics (which kinds each takes, "some answer", "the first answer", exact
  negations, inclusive bounds, hidden fields empty, empty `all` true and empty `any` false) are those of the research
  note and the vectors.
- **Cross-checks:** a TypeScript tool writes fold, clean and recomposition for every code point; the C# suite
  compares them. Known gaps (the 20 Unicode 16 compositions) are listed exactly, so the check fails both when a gap
  appears and when one closes.
- **Mutation:** the vector suites are proven by mutants in both runtimes; a mutant that survives is either an
  equivalent change, written down, or a missing vector, added. **Amended in 5.2a (ADR-0061):** the mutants are a script
  in the repository, `tests/vectors/mutants.mjs`, with the equivalent changes and their reasons in it.
- **Globalization:** Jiron Form Builder requires ICU. Start-up refuses invariant globalization, and saving refuses a
  time zone the server cannot resolve.

## Consequences

- §3.4 gains the text and time zone facts. §4.3.3 states the comparison semantics, the operands, the check codes, the
  wire forms, the pattern matcher in place of "`RegexOptions.NonBacktracking` and a timeout", the decimal rules and the
  vector format. §4.8 validates the time zone. WORKLOG records the change.
- 3.4 ports both runners and the tables check. 4.3 builds `TextKey` with the same clean and fold, so the entries
  screen filters as rules compare. 4.4 adds the start-up and time zone checks.
- 5.2 starts from the spike's 163 vectors, and its mutation check covers every operator and every module in
  TypeScript, plus C# mutants for the parts where .NET differs. 5.3 builds calculations and the date functions on
  these rules. 5.4 converts the value of `<input type="number">`, which may use an exponent, exactly to the wire form
  before the limits apply. 6.1 answers `400` for a JSON body with a lone surrogate. 6.4 cleans values before sending,
  and counts the matcher toward the §4.12 budget.
- Letters outside the folded blocks (Georgian, Cherokee, Deseret, Adlam and others) compare case-sensitively. If
  editors of such forms need case-insensitive comparisons, a later step can add a block once every supported runtime
  agrees on it, proven by the tables check.
- **Revisit** if a runtime gains a portable simple case folding, if JavaScript gains a standard linear-time
  regular-expression engine, or if ICU on the supported hosts catches up with the Unicode 16 compositions.
