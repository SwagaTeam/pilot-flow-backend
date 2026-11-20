using PilotFlow.Domain.Analyses;
using static PilotFlow.Domain.Decisions.DecisionInputValidation;

namespace PilotFlow.Domain.Decisions;

public sealed record PersonData
{
    public required string? FullName { get; init; }
    public required string? Position { get; init; }
}

public sealed record ProtocolData
{
    public required string? ProtocolNumber { get; init; }
    public required string? AgreementNumber { get; init; }
    public required DateOnly? AgreementDate { get; init; }
    public required string? EnterpriseParty { get; init; }
    public required string? PartnerParty { get; init; }
    public required string? SigningPlace { get; init; }
    public required DateOnly? ProtocolDate { get; init; }
    public required PersonData PreparedBy { get; init; }
    public required PersonData ApprovedBy { get; init; }
    public required string? Conclusions { get; init; }

    public static ProtocolData Empty { get; } = new()
    {
        ProtocolNumber = null,
        AgreementNumber = null,
        AgreementDate = null,
        EnterpriseParty = null,
        PartnerParty = null,
        SigningPlace = null,
        ProtocolDate = null,
        Conclusions = null,
        PreparedBy = new() { FullName = null, Position = null },
        ApprovedBy = new() { FullName = null, Position = null },
    };

    public IReadOnlyList<AnalysisIssue> ValidateInput()
    {
        var issues = new List<AnalysisIssue>();
        Text(issues, ProtocolNumber, 100, "/protocolNumber");
        Text(issues, AgreementNumber, 100, "/agreementNumber");
        Text(issues, EnterpriseParty, 500, "/enterpriseParty");
        Text(issues, PartnerParty, 500, "/partnerParty");
        Text(issues, SigningPlace, 300, "/signingPlace");
        Text(issues, PreparedBy.FullName, 300, "/preparedBy/fullName");
        Text(issues, PreparedBy.Position, 300, "/preparedBy/position");
        Text(issues, ApprovedBy.FullName, 300, "/approvedBy/fullName");
        Text(issues, ApprovedBy.Position, 300, "/approvedBy/position");
        Text(issues, Conclusions, 10000, "/conclusions");
        return issues;
    }
}

public sealed record SelectionData
{
    public required Guid FamilyId { get; init; }
    public required string Mode { get; init; }
    public required Guid? DominantProductId { get; init; }
    public required string? Justification { get; init; }
    public required AnalysisEvidence DirectBasisEvidence { get; init; }
    public required string? ManagerDecisionReference { get; init; }

    public IReadOnlyList<AnalysisIssue> ValidateInput()
    {
        var issues = new List<AnalysisIssue>();
        Text(issues, Justification, 10000, "/justification");
        Text(issues, ManagerDecisionReference, 2000, "/managerDecisionReference");
        Evidence(issues, DirectBasisEvidence, "/directBasisEvidence");
        if (Mode is not ("ranked" or "monoproduct" or "dominantProduct"))
        {
            issues.Add(new("INVALID_SELECTION_MODE", "/mode", "Неизвестный режим выбора.", "Укажите режим из справочника."));
        }
        else if ((Mode == "dominantProduct") != DominantProductId.HasValue)
        {
            issues.Add(new("INVALID_DOMINANT_PRODUCT", "/dominantProductId", "Идентификатор продукта нужен только для режима доминирования.",
                "Укажите продукт для этого режима или оставьте поле пустым."));
        }

        return issues;
    }
}
