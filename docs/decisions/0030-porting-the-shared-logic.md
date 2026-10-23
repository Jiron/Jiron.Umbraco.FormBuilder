# 0030. Porting the shared logic: where it lives, and what the port changed

- Status: accepted
- Date: 2026-10-23
- Roadmap step: 3.4b (decided and built); 4.4 stores the time zone as the server spells it; 5.2 and 5.3 complete the
  engines on these modules

## Context

Step 3.4b gives the vector runners the code they test: it ports the spike's modules of 1.5 (text, exact decimals,
calendar values, patterns, conditions and rules, calculations; ADR-0005) into Core and `packages/logic`. The port read
every line of each runtime against its twin, and every finding became a vector in a second file, `port.json`, next to
the spike's own. Run against the spike's two evaluators, its TypeScript fails 24 of those 48 cases and its C# 12. One
more case was not run against the spike's C#, because it would allocate without bound (the first finding below). The
other cases pin the edges next to the findings.

What the review found:

1. **Patterns had no bound on the automaton.** The weight limit counts matched characters, so a repeat of something that
   matches none (an anchor, an empty group) weighs nothing: `((^){1000}){1000}` compiled to a million instructions in
   both runtimes, and each further level multiplies that by a thousand. The C# weight was also summed in 32-bit
   integers, which wrap: `((((a{1000}){1000}){1000}){1000})` weighed -727,379,968 there, passed the check and would have
   compiled about 10^12 instructions.
2. **TypeScript read data and tables from plain objects,** so a lookup by an editor's name could answer with a property
   of `Object.prototype`. An operator "constructor" crashed the check, and a field "toString" was reported as
   `operator_not_for_kind` instead of `unknown_field`. An operand naming the field "valueOf" passed the check and
   crashed the evaluation, as did a field "hasOwnProperty" without answers and a query value "constructor". A
   calculation "constructor" returned its first argument, and one named "toString" was refused as `invalid_operand`
   instead of `unknown_operator`.
3. **Null:** a null operand crashed the TypeScript check (`"field" in null`), and a null context name counted as a name
   in TypeScript but not in C#.
4. **The instant of evaluation** was read by `DateTimeOffset.TryParse` and by `new Date`, which take different forms,
   and both read a form without a zone as the local time of their own machine; `new Date` also rolls 30 February
   over into March.
5. **Time zone names:** on Windows, .NET takes Windows names ("W. Europe Standard Time"), which no browser takes, and
   browsers take offsets ("+01:00"), which .NET does not. Browsers, and .NET on Windows, take an IANA name in any letter
   case; .NET on Linux reads the names from the file system, which may not.
6. **Trailing zeros** in a decimal's fraction were removed by dividing the big integer by ten once per zero, which takes
   quadratic time for an answer such as "1." followed by ten thousand zeros.
7. The spike's TypeScript used `String.prototype.toWellFormed` (ES2024); the client workspace compiles against ES2022.

## Options

The fixes themselves have one obvious form each. What needed deciding:

- **Where the code lives:** public types in Core now, or internal types until 5.2 and 5.3 give the engines their
  interfaces.
- **The tables file:** written by Vitest before every `dotnet test`, or written once by a tool and kept in the
  repository, with a test that keeps it current.
- **The second limit for patterns:** a cap on nesting, or a cap on the instructions the compiler would emit.
- **The instant's range:** any year 1 to 9999, or the span in which time zone data agrees between platforms.

## Decision

- **Where:** Core's `Logic/` folder (namespace `Jiron.Umbraco.FormBuilder.Logic`), internal and visible to Web and the
  unit tests, until 5.2 and 5.3 give the engines public interfaces; nothing public has to be kept for a full major
  before it is designed (D21). `packages/logic/src` holds the same modules (`errors`, `text`, `decimal`, `dates`,
  `pattern`, `rules`, `calc`), exported from `index.ts`. They import each other with the `.ts` extension
  (`allowImportingTsExtensions` in the workspace's base configuration) and use erasable syntax only, so Node runs the
  tables tool on the sources as they are.
- **Vector files:** each module in `tests/vectors/suites/` becomes `<suite>.json` through `tests/vectors/build.mjs`,
  in the `jfb-vectors/1` format, as ASCII; `--check` and a script test fail when a file differs from its suite.
  `spike.json` is the file of 1.5 byte for byte (163 cases), and `port.json` holds the 48 cases of this review. Both
  runners run every `*.json` in `tests/vectors/`, one test case per vector: `VectorTests` in the unit tests (named
  `Vector(<suite>/<id>)`) and `packages/logic/test/vectors.test.ts`.
- **The tables check:** `npm run tables` in `packages/logic` writes `tests/vectors/tables/typescript.json`
  (`jfb-unicode-tables/1`, the versions of Node, ICU and Unicode, and one entry per line, so a change in Unicode data is
  a short diff). A Vitest test fails when the file differs from what the running Node does, and `UnicodeTablesTests`
  compares Core with it for every code point, with the 20 known gaps of ADR-0005 listed exactly. Keeping the file lets
  `dotnet test` run without Node, and each runtime is still compared live, against the same file.
- **The fixes, the same in both runtimes:**
  1. **A second pattern limit:** at most 10,000 instructions, counted as the compiler emits them, beside the weight
     limit of 2,000 characters, which stays. Both counts are capped just past their limit as they are added up, so no
     nesting can overflow them. The instruction count is the true cost per character, and it cannot be dodged by
     nesting, which a nesting cap would allow inside a flat pattern.
  2. **Own properties only:** TypeScript keeps its operator and calculation tables in `Map`s and reads fields, answers
     and query values through `own()`, so "constructor" is a field name like any other.
  3. **Null:** a null operand is `invalid_operand`; a null field or context name reads as no name, as the C# reader
     reads it.
  4. **One form for the instant:** `YYYY-MM-DDTHH:mm:ss`, an optional fraction of 1 to 9 digits, and `Z` or
     `+HH:mm`/`-HH:mm`, from 1970-01-01T00:00:00Z to 9998-12-31T23:59:59Z; anything else is `invalid_context`. Before
     1970 time zone data differs between platforms, and after 9998 a local date could reach year 10000. The instant is
     read only when a condition reads "today". The server writes seven places of fraction, the browser three.
  5. **IANA names only:** a time zone must start with an ASCII letter, be known to the runtime, and on the server have
     an IANA id (`TimeZoneInfo.HasIanaId`); Windows names and offsets are `unknown_time_zone`.
  6. **Trailing zeros** are dropped from the text before it becomes a number.
  7. TypeScript replaces lone surrogates without `toWellFormed`.
- Everything else ADR-0005 decided stands, and the 163 vectors of 1.5 pass unchanged in both runtimes.

## Consequences

- ADR-0005 and §4.3.3 gain the instruction limit, the instant's form and the IANA-only names; §3.4 gains the platform
  facts.
- 4.4's time zone setting stores the zone as the server spells it (`TimeZoneInfo.Id`), so the browser always gets an
  IANA name in its canonical letter case, whatever case an editor typed and whichever platform the server runs on.
- 5.2's mutation sweep covers the code the port added: the instruction count, `own()`, the instant parser and the time
  zone check. Both evaluators recurse through nested groups, so 5.2 limits how deeply groups nest, and 5.3's expression
  language limits how deeply calculations nest.
- The vector reader in the unit tests is test code: the product reads rules from the definition model (4.x), whose
  schema refuses the shapes the reader turns into `InvalidOperand`.
- Revisit the instruction limit if real patterns reach it: it is five times what the weight limit allows for plain
  characters.
