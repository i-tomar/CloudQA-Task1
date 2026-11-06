namespace SelfHealing.Core;

public static class ReportPrinter
{
    public static string Format(ExecutionReport report)
    {
        var writer = new StringWriter();
        Write(writer, report);
        return writer.ToString();
    }

    public static void Write(TextWriter writer, ExecutionReport report)
    {
        writer.WriteLine(report.Success ? "PASS" : "FAIL " + report.Status);
        writer.WriteLine("URL: " + report.Url);
        writer.WriteLine(report.ScopeNote);
        writer.WriteLine(
            $"Rules: threshold {report.Threshold}, margin {report.Margin}, weights " +
            string.Join(", ", report.Weights.Select(pair => pair.Key + "=" + pair.Value.ToString(CultureInfo.InvariantCulture))));
        writer.WriteLine(
            $"Seen but not entered: {report.IframesNotEntered} iframe(s), {report.ShadowHostsNotEntered} shadow host(s). Hidden forms skipped: {report.HiddenFormsSkipped}.");

        if (report.ChosenForm == null)
        {
            writer.WriteLine("Form: none");
        }
        else
        {
            var ties = report.Forms.Count(form => form.Score == report.ChosenForm.Score);
            writer.WriteLine(
                $"Form: index {report.ChosenForm.Index} id={report.ChosenForm.Id} score {report.ChosenForm.Score} " +
                $"(text={report.ChosenForm.HasTextInput}, radio={report.ChosenForm.HasRadio}, select={report.ChosenForm.HasSelect}; " +
                $"{ties} form(s) share that score, document order breaks the tie)");
        }

        foreach (var form in report.Forms.Where(form => !form.Chosen))
        {
            writer.WriteLine($"  other form index {form.Index} id={form.Id} score {form.Score}");
        }

        foreach (var field in report.Fields)
        {
            writer.WriteLine();
            writer.WriteLine($"{field.Field}: {field.Status}  score={field.Score} runner-up={field.RunnerUpScore}  {field.Matched}");
            if (!string.IsNullOrEmpty(field.Reason))
            {
                writer.WriteLine("  " + field.Reason);
            }

            if (field.DecidingSignals.Count > 0)
            {
                writer.WriteLine("  signals: " + string.Join("; ", field.DecidingSignals.Select(FormatSignal)));
            }

            if (!string.IsNullOrEmpty(field.FilledValue))
            {
                writer.WriteLine("  filled: " + field.FilledValue);
            }

            if (!string.IsNullOrEmpty(field.Detail))
            {
                writer.WriteLine("  " + field.Detail);
            }

            if (field.RejectedByHardFilter > 0)
            {
                writer.WriteLine($"  hard filter rejected {field.RejectedByHardFilter} control(s) (wrong type, or not visible and enabled)");
            }

            foreach (var candidate in field.Candidates)
            {
                var mark = candidate.Winner ? "*" : " ";
                writer.WriteLine($"  {mark} {candidate.Score,4}  {candidate.Description}");
                foreach (var signal in candidate.Signals)
                {
                    writer.WriteLine($"       {FormatSignal(signal)}");
                }
            }
        }

        if (!string.IsNullOrEmpty(report.Error))
        {
            writer.WriteLine();
            writer.WriteLine(report.Error);
        }
    }

    private static string FormatSignal(SignalReport signal)
    {
        var points = signal.Points > 0 ? "+" + signal.Points.ToString(CultureInfo.InvariantCulture) : signal.Points.ToString(CultureInfo.InvariantCulture);
        return string.IsNullOrEmpty(signal.Evidence)
            ? $"{signal.Name} {points}"
            : $"{signal.Name} {points} \"{signal.Evidence}\"";
    }
}
