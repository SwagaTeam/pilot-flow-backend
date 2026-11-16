using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using PilotFlow.Application.Analyses;
using PilotFlow.Domain.Analyses;

namespace PilotFlow.Infrastructure.Persistence.Analyses;

internal sealed class AnalysisRow
{
    public required Guid Id { get; init; }
    public required string OwnerSubject { get; init; }
    public DateTimeOffset CreatedAt { get; init; }

    public AnalysisDetails ToDetails(AnalysisVersionRow version) =>
        new(Id, OwnerSubject, CreatedAt, version.ToDetails());
}

internal sealed class AnalysisVersionRow
{
    private const string InitialRules = "fck-2026-tz-v1";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        TypeInfoResolver = new DefaultJsonTypeInfoResolver
        {
            Modifiers = { AllowLegacyEvidence },
        },
    };

    public Guid Id { get; init; }
    public Guid AnalysisId { get; init; }
    public int VersionNumber { get; init; }
    public long Revision { get; init; }
    public required string State { get; init; }
    public required string RulesVersion { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset UpdatedAt { get; init; }
    public required string Title { get; init; }
    public string? EnterpriseName { get; init; }
    public string? Region { get; init; }
    public string? Activity { get; init; }
    public DateOnly? ProjectStartDate { get; init; }
    public DateOnly? FinancialStart { get; init; }
    public DateOnly? FinancialEnd { get; init; }
    public DateOnly? LaborStart { get; init; }
    public DateOnly? LaborEnd { get; init; }
    public DateOnly? ActivitiesStart { get; init; }
    public DateOnly? ActivitiesEnd { get; init; }
    public DateOnly? ForecastStart { get; init; }
    public DateOnly? ForecastEnd { get; init; }
    public decimal? TotalRevenue { get; init; }
    public required string Currency { get; init; }
    public string? AccountingBasis { get; init; }
    public required string RevenueEvidence { get; init; }
    public string? RemainderExplanation { get; init; }
    public bool NomenclatureComplete { get; init; }
    public required string CompletenessEvidence { get; init; }
    public required string Participants { get; init; }
    public string? EnterpriseInn { get; init; }
    public string? FamilyConclusions { get; init; }
    public string? RankingConclusions { get; init; }

    public static AnalysisVersionRow Create(Guid analysisId, AnalysisData data) => new()
    {
        Id = Guid.NewGuid(),
        AnalysisId = analysisId,
        VersionNumber = 1,
        Revision = 1,
        State = "draft",
        RulesVersion = InitialRules,
        Title = data.Title,
        EnterpriseName = data.EnterpriseName,
        Region = data.Region,
        Activity = data.Activity,
        ProjectStartDate = data.ProjectStartDate,
        FinancialStart = data.FinancialPeriod?.Start,
        FinancialEnd = data.FinancialPeriod?.End,
        LaborStart = data.LaborPeriod?.Start,
        LaborEnd = data.LaborPeriod?.End,
        ActivitiesStart = data.ActivitiesPeriod?.Start,
        ActivitiesEnd = data.ActivitiesPeriod?.End,
        ForecastStart = data.ForecastPeriod?.Start,
        ForecastEnd = data.ForecastPeriod?.End,
        TotalRevenue = data.TotalRevenue,
        Currency = data.Currency,
        AccountingBasis = data.AccountingBasis,
        RevenueEvidence = WriteJson(data.RevenueEvidence),
        RemainderExplanation = data.RemainderExplanation,
        NomenclatureComplete = data.NomenclatureComplete,
        CompletenessEvidence = WriteJson(data.CompletenessEvidence),
        Participants = WriteJson(data.Participants),
        EnterpriseInn = data.EnterpriseInn,
        FamilyConclusions = data.FamilyConclusions,
        RankingConclusions = data.RankingConclusions,
    };

    public AnalysisVersionDetails ToDetails() => new(
        Id, AnalysisId, VersionNumber, Revision, State, RulesVersion, CreatedAt, UpdatedAt,
        new AnalysisData
        {
            Title = Title,
            EnterpriseName = EnterpriseName,
            Region = Region,
            Activity = Activity,
            ProjectStartDate = ProjectStartDate,
            TotalRevenue = TotalRevenue,
            Currency = Currency,
            AccountingBasis = AccountingBasis,
            RevenueEvidence = ReadJson<AnalysisEvidence>(RevenueEvidence),
            RemainderExplanation = RemainderExplanation,
            NomenclatureComplete = NomenclatureComplete,
            CompletenessEvidence = ReadJson<AnalysisEvidence>(CompletenessEvidence),
            Participants = ReadJson<AnalysisParticipant[]>(Participants),
            EnterpriseInn = EnterpriseInn,
            FamilyConclusions = FamilyConclusions,
            RankingConclusions = RankingConclusions,
            FinancialPeriod = ReadPeriod(FinancialStart, FinancialEnd),
            LaborPeriod = ReadPeriod(LaborStart, LaborEnd),
            ActivitiesPeriod = ReadPeriod(ActivitiesStart, ActivitiesEnd),
            ForecastPeriod = ReadPeriod(ForecastStart, ForecastEnd),
        });

    internal static string WriteJson<T>(T value) => JsonSerializer.Serialize(value, JsonOptions);

    private static T ReadJson<T>(string value) =>
        JsonSerializer.Deserialize<T>(value, JsonOptions)
        ?? throw new InvalidDataException("Invalid null analysis metadata in PostgreSQL.");

    private static void AllowLegacyEvidence(JsonTypeInfo type)
    {
        // Older rows can omit nullable evidence fields. Require them on HTTP input only.
        if (type.Type == typeof(AnalysisEvidence))
        {
            foreach (var field in type.Properties)
            {
                field.IsRequired = false;
            }
        }
    }

    private static AnalysisPeriod? ReadPeriod(DateOnly? start, DateOnly? end) =>
        start.HasValue && end.HasValue ? new AnalysisPeriod { Start = start.Value, End = end.Value } : null;
}
