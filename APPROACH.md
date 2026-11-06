# Approach and limitations

Task 1 asks for a C# Selenium program that can still fill First Name, Gender, and State when the practice form is rearranged: field order, ids, names, classes, a small HTML change, or extra fields that look similar. The URL has to be configurable because the review runs against modified copies of the page.

The approach is additive weighted scoring, with the label as the strongest signal. There is no language model. A fixed chain of locators was rejected because the page contains misleading markup, and the first locator in a chain would trust it. A model was rejected because the same page would not resolve the same way on every run.

## What the program does

**It chooses the form before it chooses a field.** The practice page repeats the form in iframes and in open shadow trees, and those copies reuse `id="fname"` and `id="state"`. `By.Id("fname")` is therefore ambiguous, and which element comes back depends on DOM order. The program lists `<form>` elements in the top-level document only, keeps the ones that are visible, and scores each one: one point for a usable text input, one for a radio, one for a select. The best score wins. When two forms tie, the earliest in the document wins. On the live page that is `#automationtestform`. The shadow-host form also scores 3, because its inputs are light-DOM children, and it loses the tie. Nested shadow forms score 0, because their controls are inside a shadow root and this version does not enter those.

**Type and visibility are filters, not points.** First Name candidates are visible, enabled text inputs or textareas (not number, file, password, radio, or checkbox). Gender candidates are visible, enabled radios. State candidates are visible, enabled selects. A hidden copy of First Name cannot tie the real one, because it never enters the score list.

**Signals are added, heaviest first.**

| Order | Signal | Exact-match points |
|---|---|---|
| 1 | A `<label for="id">`, a wrapping `<label>`, or a `<label>` / `<legend>` sitting beside the control | 50 |
| 2 | `aria-label`, `aria-labelledby`, or `title` | 34 |
| 3 | Other short text beside the control (span, paragraph, heading) | 22 |
| 4 | Placeholder | 10 |
| 5 | id or name, after camelCase and separator normalisation | 6 |

Last name, surname, lname, and family name subtract 28 once. The id/name step is only a synonym match after normalisation (`first_name` and `FirstName` both become `first name`). Edit distance is not used. One character separates `fname` from `lname`, and a fuzzy edit would treat the last-name box as the first-name box.

A caption that merely contains the synonym scores 55% of the points. "First Name" is 50. "Emergency First Name" is 28 from the label, plus the weak placeholder and name points. On the decoy fixture the real field scores 66 and the emergency field scores 41. The lead of 25 is more than the margin of 12, so the real field wins. Two fields that are both captioned exactly "First Name" score 60 and 60, and the run stops as ambiguous.

**The margin is the safety check.** The winner must score at least 14 and lead the next candidate by at least 12. Below the threshold the result is `not-found`. Above it, but too close to the runner-up, the result is `ambiguous`. Either way the page is left as it was. 14 is high enough that a placeholder of "Name" on its own (10 points) is not accepted, and low enough that a caption which is no longer a `<label>` can still win: the restructured fixture has only a span reading "First Name" (22) plus the placeholder (10), which clears 14 with room to spare. Set the threshold much higher and a page that has lost its label fails. Set it much lower and a decoy whose only clue is the word "Name" can win. The numbers are in `appsettings.json` so they can be read next to a failing report.

**Gender is a group, then an option.** Radios that share a `name` are one group. The group is scored from its heading (Gender or Sex), not from the option captions. The option is then matched by normalised `value` and by the text of the next element. The Transgender `<span for="female">` is the trap on the live page: `for` on a span does nothing, and that particular `for` points at the Female radio. The program never reads `for` on a span. It clicks the radio element and checks that it is selected.

**State is text, not position.** The select is found from its label. Options are compared after trimming and collapsing whitespace, on both the value and the visible text. `SelectByIndex` is never called. The first option is the placeholder `-- Select Country --`, and the list is countries, including the padded, misspelled ` Afganistan `. Asking for `Afganistan`, `India`, or `United States` selects that option and then reads the selection back.

**The report is the explanation.** For every run the console, and `reports/execution-report.json`, list the forms that were considered, the form that was chosen, every candidate and its score, and the signals that fired. That is the record of why a match was accepted or refused.

## Limitations

- **Iframes and shadow DOM are out of scope for v1.** A field that exists only inside an iframe or a shadow root will not be found. The live page has both. The top-level form is the one this program fills. An optional scope switch could add the others later; it is not in this version.
- **Labels are English**, or whatever synonyms are listed in `appsettings.json`. A page captioned in another language will not match until those words are added. "Full Name" is not treated as First Name. "Given Name" is a configured synonym, so a form that has both "First Name" and "Given Name" is ambiguous on purpose.
- **The weights, the 14-point threshold, and the 12-point margin are a rule of thumb.** A carefully built decoy can still land close to the real field. The margin turns that into an `ambiguous` failure instead of a silent wrong click. It cannot make the guess correct.
- **The State list is countries**, not US states. The placeholder says "Select Country", one option is misspelled Afganistan, and there is a separate Country text box. The program follows the State label. It does not guess that the Country text box is the dropdown.
- **The live page repeats ids.** `fname`, `state`, `male`, and `female` occur more than once. This program does not call `By.Id` against the whole document. That avoids the duplicate, but it also means a modified page whose only copy of the field is inside a shadow tree is out of reach.
- **A radio group needs a shared `name`.** That is how HTML defines a radio group. Radios that do not share a name are scored as separate groups.
- **There is no language model, and the program does not click Submit.** A required field elsewhere on the form is left blank. Verification checks the three values that were written, not that the form would be accepted by the server.
