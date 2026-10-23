# One logic, two runtimes: what the sandbox spike showed

Step 1.5 wrote a minimal evaluator for conditions, rules, numbers and calculations twice: in C# (.NET 10.0.12,
NUnit 3.14.0) and in TypeScript (Node 24.21.0 with ICU 78.3 and Unicode 17.0, Vitest 5.0.3, TypeScript 6.0.3 in
strict mode). Both ran one vector file. The spike code is throwaway and git-ignored (`.local/sandbox/logic/` and
`.local/sandbox/vectors/`); the decisions are in [ADR-0005](../decisions/0005-shared-logic-and-test-vectors.md).
Measured on 2026-10-23 on the demo machine (Windows 11, the system ICU in `C:\Windows\System32\icu.dll`).

## 1. What was built

- **One vector file,** `vectors/spike.json`, written by a small script (`vectors/build.mjs`) that builds every special
  character from its code point and writes it as a JSON escape, so the file is plain ASCII and no tool can damage it.
  It holds 163 cases: text 21, choices, checkboxes and files 10, numbers 18, dates, times and "today" 22, context 5,
  operands and operators 7, patterns 53, rules 9 and calculations 18.
- **The same modules in both runtimes:** text (clean and fold), exact decimals, calendar values and "today", patterns,
  conditions and rules, calculations. C# adds a reader for the JSON wire form. TypeScript has about 1,100 lines, C#
  about 1,700 (with that reader, and braces and records spelled out).
- **Two runners:** Vitest makes one case per vector, NUnit one `TestCaseSource` case per vector. Both print the file
  they read. The C# test project gets the vector folder through an `AssemblyMetadata` attribute set at build time, so
  the file is read in place and never copied.
- **Cross-checks over every code point:** a TypeScript tool writes what fold, clean and NFC recomposition do to each of
  the 1,114,112 code points (lone surrogates included for clean), and a C# test compares its own result for each.
- **Mutation sweeps:** a script applies one change to the TypeScript evaluator at a time (74 mutants), runs Vitest and
  restores the file; a second does the same for 14 C#-only mutants with `dotnet test`. A mutant that survives marks a
  behaviour no vector pins.

## 2. The vector format

```json
{
  "format": "jfb-vectors/1",
  "suite": "spike",
  "defaults": { "context": { "now": "2026-06-15T10:00:00Z", "timeZone": "Europe/Zurich", "culture": "de-CH",
                             "member": { "signedIn": false, "groups": [] }, "query": {}, "hidden": [] } },
  "fields": { "name": "text", "amount": "number", "arrival": "date" },
  "cases": [
    { "id": "number-greater-than-equal-is-false", "values": { "amount": ["10"] },
      "condition": { "field": "amount", "op": "greaterThan", "value": "10.0" }, "expect": false }
  ]
}
```

- **A case** has an `id` (unique, kebab case), an optional `description` for anything not obvious from the id, and
  exactly one thing to run: a `condition`, a `rule`, a `number` (as a field stores it) or a `calc`.
- **Its inputs:** `values` (alias to a list of strings, the wire form of every answer), `context` (merged over the
  defaults one property at a time) and `fields` (merged over the file's field kinds).
- **Its expectation:** `true` or `false`, `null` for a switched-off rule, a canonical number string, or
  `{ "error": code }`. Both runners compare the result as JSON.
- **Field kinds:** `text`, `choice`, `choices`, `checkbox`, `files`, `number`, `date`, `time`, `dateTime`.

## 3. Text: what differs between the runtimes

- **Trimming.** The built-in trims use different sets: JavaScript's `trim()` removes U+FEFF but not U+0085, .NET's
  `Trim()` removes U+0085 but not U+FEFF (both remove U+00A0, U+2028 and U+3000). The whitespace classes `\s` differ
  the same way. Both runtimes therefore trim an explicit list: tab to carriage return, space, U+0085, U+00A0, U+1680,
  U+2000 to U+200A, U+2028, U+2029, U+202F, U+205F, U+3000 and U+FEFF.
- **Case.** The first fold (simple uppercase, then simple lowercase, one character at a time) differed on 55 code
  points: letters added in Unicode 16 and 17, such as U+1C89 and the Garay and Beria Erfe letters. Node maps them;
  .NET does not, although its own character data knows 27 of them as uppercase letters. On Windows .NET takes casing
  from the operating system's ICU and categories from its own tables, so a server's casing depends on its host, and a
  browser's on its version. Folding is therefore limited to blocks whose mappings every runtime has had for years:
  Latin (U+0000 to U+024F, U+1E00 to U+1EFF), Greek (U+0370 to U+03FF, U+1F00 to U+1FFF), Cyrillic (U+0400 to
  U+052F), Armenian (U+0530 to U+058F) and the full-width Latin letters. Letters of other scripts compare exactly.
  With that rule the two runtimes agree on all 728 code points that fold.
- **The Turkish i's.** .NET's invariant casing leaves U+0130 and U+0131 as they are, while JavaScript maps both. Both
  runtimes fold them to "i" explicitly (the C# mutant without the special case fails the vectors).
- **Upper, then lower.** Folding through the uppercase makes final and medial sigma, the micro sign and Greek mu, and
  the long s and "s" compare equal, as case folding should. Mappings that would expand (sharp s to "SS") are not used,
  so "Straße" does not equal "STRASSE" in either runtime.
- **NFC.** The two runtimes normalise every single code point alike, except U+FFFE: .NET's `Normalize` throws
  `ArgumentException` for it. A visitor could send it and turn a validation into a server error. Recomposing every
  canonical decomposition agrees except for 20 characters in scripts added in Unicode 16 (Todhri, Tulu-Tigalari,
  Gurung Khema, Kirat Rai), which this .NET's ICU does not decompose yet. They matter only when such text is typed
  decomposed; the test lists them exactly, so it fails when a runtime catches up as well as when a new gap appears.
- **Invalid text.** .NET's JSON reader throws `InvalidOperationException` for an escaped lone surrogate, so a server
  never sees one through JSON, while a browser's strings can hold them. Clean replaces lone surrogates and all 66
  noncharacters (U+FDD0 to U+FDEF and the last two code points of every plane) with U+FFFD in both runtimes, then
  normalises. Clean then agrees on every code point and every lone surrogate.
- **Invariant globalization.** With `DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1` the C# suite failed 13 tests: NFC does
  nothing, IANA time zones are not found on Windows, and the long s no longer folds. Jiron Form Builder needs ICU.

## 4. Numbers

- **Neither runtime's own number type will do.** In JavaScript `0.1 + 0.2` is `0.30000000000000004`,
  `(2.675).toFixed(2)` is "2.67" and `Math.round(-2.5)` is -2; a double keeps about 16 significant digits. C#
  `decimal` rounds half to even by default (`Math.Round(0.125m, 2)` is 0.12, `Math.Round(-2.5m)` is -2), keeps 28
  digits, and `1m / 3m * 3m` is 0.9999999999999999999999999999. Any calculation done with them would round differently
  in the browser and on the server.
- **Exact decimals in both:** a big integer `n` and a scale `s` for the value n / 10^s (`BigInt` and
  `System.Numerics.BigInteger`), normalised so that equal values look alike. Addition, subtraction and
  multiplication are exact. Division keeps 20 places, rounded half away from zero (1 / 3 is
  0.33333333333333333333). `round` rounds half away from zero (2.675 to 2.68, -2.5 to -3, 0.125 to 0.13). A result
  with more than 60 digits is `out_of_range`; division by zero is `division_by_zero`.
- **The wire form** is strict: an optional minus, an integer part without leading zeros, and an optional point with
  at least one digit. No plus sign, exponent, group separator or culture. "007", "5.", "1e3" and "1,5" are not
  numbers. The canonical form drops trailing fraction zeros and turns "-0" into "0".
- **Stored numbers** keep at most 15 digits in all, leading zeros not counted, and at most 15 after the point
  (`too_many_digits`). Every such value is exact as a double (ADR-0003) and below 2^53. 0.000000000000001 is accepted;
  0.0000000000000001 and 1,000,000,000,000,000 are not.
- **Comparisons** compare by value, exactly, whatever the length ("0.123456789012346" is greater than
  "0.123456789012345"; "10.5" equals "10.50"; "9" is less than "10").

## 5. Dates, times and "today"

- **Wire forms:** `YYYY-MM-DD`, `HH:mm` or `HH:mm:ss`, and `YYYY-MM-DDTHH:mm(:ss)` as local wall-clock time without a
  zone. Each is validated as a real calendar value: years 1 to 9999, the Gregorian leap rules (2100-02-29 is invalid,
  2000-02-29 is valid), hours 0 to 23. An invalid value never matches a comparison.
- **TypeScript does not use `Date` for calendar values:** `Date.UTC` maps the years 0 to 99 to the 1900s. It uses a
  civil-days algorithm instead; C# uses `DateOnly.DayNumber`.
- **"Today"** is the local date of an injected instant in the site's IANA time zone. TypeScript reads it with
  `Intl.DateTimeFormat(…, { timeZone, calendar: "gregory", numberingSystem: "latn" }).formatToParts`; C# with
  `TimeZoneInfo.FindSystemTimeZoneById`, which resolves IANA ids on Windows through ICU, and `ConvertTime`. Both agree
  on the vectors around the daylight-saving changes in Zurich (both directions), New York and Auckland. An unknown
  zone is `unknown_time_zone` in both.
- **Relative dates** (`{ "today": "+1m+1d" }`, steps of `[+-]` 1 to 4 digits and d, w, m or y) apply in written
  order; months and years clamp to the end of the month (31 March minus a month is 28 February; 29 February 2028
  plus a year is 28 February 2029). A result outside years 1 to 9999 matches nothing.
- **Local date-times are wall-clock values:** 02:30 on 29 March 2026 does not exist in Zurich, but as a value it
  compares like any other.

## 6. Patterns

- **The engines' dialects differ:** .NET's `\d` matches every Unicode digit and JavaScript's only 0 to 9; the `\s`
  sets differ (section 3); .NET's "." matches carriage return, U+2028 and U+2029; .NET's `$` also matches before a
  final line break; the `(?…)` forms, class subtraction and empty classes mean different things or are errors.
- **JavaScript has no linear-time engine.** Native `RegExp` took 5 ms for `(a+)+` on 20 a's and a "b", 21 ms on 22 and
  98 ms on 24, doubling with each character; 40 would take about two hours, and the browser tab hangs meanwhile.
  `RegexOptions.NonBacktracking` protects the server only.
- **First approach, a translator:** the spike first translated a safe subset into a source for each engine (every
  literal as an escape, `\d`, `\w`, `\s` and "." as explicit classes, `$` as `\z` for .NET), compiled with
  `NonBacktracking` on the server. It passed the vectors, but it could not protect a browser, and the C# mutation
  sweep showed that no vector noticed when `NonBacktracking` was switched off.
- **Decision, an own matcher:** both runtimes parse the subset into a tree, compile it to a Thompson automaton and run
  it over the value's code points, advancing every thread together. The same algorithm in both runtimes means the same
  answers by construction, with no engine involved. Times in Node for values of 1,000, 10,000 and 100,000 characters:

  | Pattern | 1,000 | 10,000 | 100,000 |
  | --- | --- | --- | --- |
  | `(a+)+` against a's and a "b" | 3.3 ms | 6.6 ms | 22.4 ms |
  | `(a\|aa)*c` against a's | 2.5 ms | 5.3 ms | 35.4 ms |
  | an e-mail-like pattern | 0.1 ms | 2.2 ms | 17.2 ms |
  | `(a{40}){40}` against a's | 0.4 ms | 0.5 ms | 2.2 ms |

- **Code points, not code units:** an emoji is one character (`.{3}` matches "a", an emoji and "b"), and a range of
  emoji works. The pattern itself is normalised like a value, so a pattern typed with a combining accent matches the
  composed value.
- **The subset:** literal characters; escapes `\d \w \s \D \W \S`, `\t \n \v \f \r`, `\u` with four hex digits and
  `\x` with two (not a surrogate), and any escaped ASCII punctuation; classes with ranges and the escaped sets, negated
  with `^`; "." (anything but a line end); `^` and `$` (only at the start and end of the value); groups `( )` and
  `(?: )`; `|`; the quantifiers `* + ? {n} {n,} {n,m}`, each optionally lazy (which matches the same values). A
  pattern always matches the whole value and is case-sensitive.
- **Refused** (`unsafe_pattern`): backreferences, `\b \B \p \P \c \0 \k \A \z \Z \G` and other letter escapes; any
  `(?` form but `(?:`; empty classes, `[^]`, a raw `[` inside a class, a `-` that neither starts nor ends a class nor
  joins a range, ranges out of order or from a set; a lone `]` or `}`; a quantifier with nothing to repeat (which
  also refuses stacked quantifiers); unbalanced groups; patterns over 500 code points, counts over 1,000, and patterns
  whose counted repeats unrolled exceed 2,000 characters (`(a{50}){50}` is refused, `(a{40}){40}` accepted).

## 7. Conditions and rules

- **Checked, then evaluated.** A condition or a whole rule is checked first: unknown fields and context subjects
  (`unknown_field`), unknown operators (`unknown_operator`), an operator the field's kind does not take
  (`operator_not_for_kind`, such as `greaterThan` on text), a missing or malformed operand (`invalid_operand`), and
  patterns (`unsafe_pattern`). Only then is it evaluated. An "any" rule whose first condition matches still reports a
  broken second condition, so evaluation order can never change the outcome between the runtimes.
- **Values:** answers are cleaned, and empty ones are dropped. A hidden field has none, as subject and as operand.
  Text compares folded; option keys (choice, choices, checkbox, files) compare exactly; numbers and calendar values
  compare by value.
- **Operators:**

  | Operators | Kinds | Meaning |
  | --- | --- | --- |
  | `equals`, `isOneOf` | all | some answer equals some operand value |
  | `notEquals`, `isNoneOf` | all | the exact negation (true when empty) |
  | `isEmpty`, `isNotEmpty` | all | no answer, or at least one |
  | `countAtLeast`, `countAtMost` | all | the number of answers, inclusive; the operand 0 or 1 to 6 digits without a leading zero |
  | `contains`, `notContains`, `startsWith`, `endsWith` | text | on folded text; the negation is exact |
  | `matches` | text | some answer matches the whole pattern |
  | `greaterThan`, `lessThan`, `between` | number, date, time, dateTime | the first answer against the first operand value; `between` inclusive at both ends |
  | `isChecked`, `isNotChecked` | checkbox (no operand), choice and choices (the option key) | the checkbox's value is "true", or the option is chosen |

- **Operands:** a string, a list (for `isOneOf`, `isNoneOf` and the two ends of `between`), `{ "field": alias }` for
  another answer, or `{ "today": "+2d" }`.
- **Context subjects** compare as a kind: `culture` and `query.<name>` as text, `memberSignedIn` as a checkbox,
  `memberGroup` as choices, `today` as a date.
- **Rules:** `all` of an empty list is true and `any` of an empty list is false; a switched-off rule is checked and
  then gives `null`.

## 8. Mutation sweeps

- **The broken operator of the step's check proved too weak on its own:** with `greaterThan` made inclusive, all 115
  vectors of the first file still passed, because none compared equal values.
- **The first sweep killed 37 of 61 mutants.** The survivors were mostly boundaries and grammar: inclusive
  comparisons, `isEmpty` with one answer, the start or end of `startsWith` and `endsWith`, leap centuries, hour 24,
  year 0, weeks, leading zeros, a trailing point, 16 places after the point, the operand of `contains` not folded, a
  lone `}`, counts over the limit, round's scale, a calculation's arity. Twenty-four vectors were added.
- **After the matcher:** 73 of 74 TypeScript mutants and 14 of 14 C# mutants are killed. The one survivor (no special
  case for the dotless i) is equivalent in JavaScript, which maps it anyway; in C# the same mutant fails. Three other
  checks proved redundant and were removed or left as defence in depth: `$` as `\z` (a value never ends in a line
  break once cleaned), `NonBacktracking` (gone with the matcher) and a separate check for stacked quantifiers.

## 9. The runs

| Run | Vectors | Vitest | NUnit |
| --- | --- | --- | --- |
| First file, translator patterns | 115 | 116 of 116 | 114 of 116: fold (55 code points) and NFC (U+FFFE throws) differ |
| Fold limited to stable blocks, noncharacters replaced | 115 | 116 of 116 | 118 of 119: 20 recomposition gaps, then listed exactly |
| Mutation vectors added | 139 | 140 of 140 | 145 of 145 |
| Own matcher, final file | 163 | 164 of 164 | 170 of 170 |
| `greaterThan` made inclusive in TypeScript | 163 | 1 failed (`number-greater-than-equal-is-false`), then reverted: 164 of 164 | |
| C# with invariant globalization | 163 | | 13 failed |

Both final runs read `C:\Code\Jiron.Umbraco.FormBuilder\.local\sandbox\vectors\spike.json` (SHA-256 `dd5ea105…`).
`tsc --noEmit` and the C# build with warnings as errors and the recommended analyzers were clean.

## 10. Notes for later

- **3.4** ports both runners and the code-point tables tool; the vector files of §4.3.3 all use `jfb-vectors/1`, and
  the tables check runs in CI after Vitest has written the table.
- **4.3:** `jfbEntryValue.TextKey` uses the same clean and fold, so the entries screen filters text as rules compare it.
- **4.4** refuses invariant globalization at start-up and a time zone the server cannot resolve when it is saved.
- **5.2** starts from these 163 vectors (5.2 asks for at least 300), and its mutation check covers every module, with
  C# mutants for the parts where .NET differs (the Turkish i's, noncharacters, the folded blocks).
- **5.3** builds calculations on the exact decimals; `daysBetween` and `yearsBetween` use the calendar functions.
- **5.4:** a number field's no-JS post comes from `<input type="number">`, whose value may use an exponent; the server
  converts it exactly to the wire form before the limits apply. The API and the runtime send the wire form.
- **6.1:** a JSON body with an escaped lone surrogate must answer `400`, not `500`.
- **6.4:** the runtime cleans with the same functions before sending; the matcher's size counts toward the §4.12
  budget (about 400 lines of TypeScript before minification).
- **9.x:** the designer's pattern tester runs the same matcher, so it shows exactly what the server will decide.
- **Merge-tag filters** `upper` and `lower` are for display and may use the page's culture; folding is only for
  comparisons.

## 11. Roadmap changes

- §3.4 gains the text and time zone facts: casing from the host's ICU, `Normalize` refusing U+FFFE, the JSON reader
  refusing lone surrogates, IANA ids through ICU, and invariant globalization.
- §4.3.3 states the comparison semantics, the operand shapes, the check codes, the wire forms, the pattern matcher
  (replacing "`RegexOptions.NonBacktracking` and a timeout") and the decimal rules, with the vector format.
- §4.8: the time zone is validated when saved.
- 3.4 (the runners and the tables check), 4.3 (`TextKey`), 4.4 (start-up and time zone checks), 5.2 (the spike's
  vectors, mutation in both runtimes, the tables), 5.3 (decimal rules), 5.4 (number limits and input), 6.1 (lone
  surrogates) and 6.4 (cleaning before sending) gain checks. None is weakened.
