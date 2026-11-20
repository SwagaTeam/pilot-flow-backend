using System.Text;
using PilotFlow.Domain.Analyses;

namespace PilotFlow.Domain.Decisions;

internal static class DecisionInputValidation
{
    internal static AnalysisEvidence EmptyEvidence { get; } = new()
    {
        Source = null, Period = null, Basis = null, AssessedOn = null, MissingReason = null,
    };

    internal static void Text(List<AnalysisIssue> issues, string? value, int maxLength, string path)
    {
        if (value is not null && (value.EnumerateRunes().Count() > maxLength || value.Contains('\0', StringComparison.Ordinal)))
        {
            issues.Add(new("INVALID_TEXT", path, "Недопустимое текстовое значение.",
                FormattableString.Invariant($"Используйте до {maxLength} символов без нулевых символов.")));
        }
    }

    internal static void Evidence(List<AnalysisIssue> issues, AnalysisEvidence evidence, string path)
    {
        Text(issues, evidence.Source, 2000, path + "/source");
        Text(issues, evidence.Basis, 2000, path + "/basis");
        Text(issues, evidence.MissingReason, 2000, path + "/missingReason");
        if (evidence.Period is { } period && period.Start > period.End)
        {
            issues.Add(new("INVALID_PERIOD", path + "/period", "Начало периода позже его окончания.", "Исправьте даты периода."));
        }
    }
}
