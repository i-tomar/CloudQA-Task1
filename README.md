# CloudQA self-healing form filler

A C# / Selenium program that fills three fields on the [CloudQA practice form](https://app.cloudqa.io/home/AutomationPracticeForm): **First Name**, **Gender**, and **State**.

It does not depend on one fixed id, name, class, or XPath. The page URL is configurable, so the same program can be pointed at a modified copy of the form. It fills only those three fields. It never clicks Submit.

## Setup

1. Install the [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0).
2. Install Google Chrome. The first run uses Selenium Manager to download a matching ChromeDriver, which needs network access once. After that the driver is cached.
3. From this directory:

```bash
dotnet test
```

Chrome runs headless. The suite covers the local fixtures and the live practice form.

## Run the console program

Default URL, name, gender, and state come from `appsettings.json` (the live form, First Name `Ishan`, Gender `Female`, State `India`):

```bash
dotnet run --project src/SelfHealing.Runner
```

Point it at another page:

```bash
dotnet run --project src/SelfHealing.Runner -- --url https://example.test/my-modified-form --firstname Asha --gender Transgender --state "United States"
```

A bare URL works too:

```bash
dotnet run --project src/SelfHealing.Runner -- https://example.test/my-modified-form
```

Watch the browser instead of running headless:

```bash
dotnet run --project src/SelfHealing.Runner -- --headless false
```

### Where the URL comes from

The first value that is set wins:

1. `--url`, or a bare URL argument
2. The `CLOUDQA_URL` environment variable
3. `CloudQa:Url` in `appsettings.json`

Test data follows the same order. `--firstname`, `--gender`, and `--state` override `CLOUDQA_FIRSTNAME`, `CLOUDQA_GENDER`, and `CLOUDQA_STATE`, which override the json file.

```bash
export CLOUDQA_URL=https://example.test/changed-form
export CLOUDQA_FIRSTNAME=Asha
dotnet run --project src/SelfHealing.Runner
```

Other flags: `--timeout <seconds>` (default 30) and `--report <path>` (default `reports/execution-report.json`). `--help` prints the full list.

### Exit codes

| Code | Meaning |
|---|---|
| 0 | The three fields were filled and the values stuck |
| 2 | The form or a field was not found |
| 3 | The match was ambiguous, so nothing was typed |
| 4 | The gender or state option was missing, or a value did not stick |
| 1 | Anything else (bad arguments, the browser could not start) |

The console prints which signals won, and the same report is written as JSON.

## How it works

1. Open the URL and consider only **visible forms in the top-level document**. Iframes and shadow DOM are not entered.
2. Score each form by whether it contains a text input, a radio, and a select. The highest score wins. A tie goes to the earliest form in the document, which is the real form on the practice page. Later copies lose the tie.
3. Inside that form, keep only controls of the right type that are visible and enabled. A radio is not a candidate for First Name. A hidden or disabled control is dropped.
4. Score the remaining controls by adding up signals, heaviest first:

| Signal | Points for an exact match |
|---|---|
| Linked label, or a real `<label>` / `<legend>` beside the control | 50 |
| Accessible name (`aria-label`, `aria-labelledby`, `title`) | 34 |
| Nearby text (a span, paragraph, or heading beside the control) | 22 |
| Placeholder | 10 |
| id or name, after splitting camelCase and separators | 6 |
| Penalty for last name, surname, lname, family name | −28 |

A longer caption such as "Emergency First Name" contains the synonym but is not the synonym, so it scores 55% of those points. The exact caption "First Name" stays ahead of it.

5. Accept the top candidate only when its score is at least **14** and it leads the runner-up by at least **12**. Otherwise the run stops with `ambiguous` or `not-found` and does not type anything.
6. **Gender** is the radio group whose heading says Gender or Sex. Each option is matched on its value and the text of the element next to it. A `for` attribute on a `<span>` is ignored (the Transgender span on the live page points at Female). The radio itself is clicked, then checked.
7. **State** is the select found from its label. The option is chosen by its visible text or its value, after trimming and collapsing whitespace. It is never chosen by position. The misspelled, padded option ` Afganistan ` still matches `Afganistan`.

Weights, synonyms, the threshold, and the margin live in `appsettings.json`.

## Tests and fixtures

`dotnet test` runs 30 tests. The browser tests use one headless Chrome and the pages in `test-pages/`:

| File | Expected |
|---|---|
| `original.html` | Pass. Includes a duplicate form with the same ids, a hidden form, and an iframe |
| `reordered.html` | Pass |
| `renamed-ids-names.html` | Pass |
| `renamed-classes.html` | Pass |
| `restructured.html` | Pass. Captions are spans, and the State caption sits after the select |
| `decoys.html` | Pass. Similar fields are present, and the plain fields still win by more than the margin |
| `ambiguous-firstname.html` | Fail as ambiguous. Nothing is typed |
| `no-firstname.html` | Fail as not-found. Gender and State are left alone |

`PageMutator` (used by the tests) rewrites `original.html` with a seed: new ids and `for` values, new names, new classes, and a shuffled field order. The visible words and the option values stay. Seeds 1, 2, and 3 must still pass. The Transgender span's `for` is remapped with the Female radio, so the trap survives the rewrite.

The live test opens `https://app.cloudqa.io/home/AutomationPracticeForm`, fills the three fields inside `#automationtestform`, and checks that Submit was not clicked.

## Project layout

```
src/SelfHealing.Core/     form scoping, scoring, gender and state rules, the report
src/SelfHealing.Runner/   console program
tests/SelfHealing.Tests/  NUnit suite and the page mutator
test-pages/               local copies of the form
appsettings.json          URL, test data, weights, threshold, margin
APPROACH.md               why it is built this way, and what it will not do
```

## What this version does not do

Iframes and shadow DOM are out of scope. Labels are matched in English, using the synonym list in config. The weights are a deliberate trade-off, not a proof. The State dropdown lists countries. The live page repeats the same ids in other copies of the form; this program avoids that by choosing the form first and never calling `By.Id` on the whole page. Details and the other limits are in [APPROACH.md](APPROACH.md).
