using Microsoft.EntityFrameworkCore;
using PilotFlow.Application.Analyses;
using PilotFlow.Application.Catalogs;
using PilotFlow.Application.Decisions;
using PilotFlow.Domain.Analyses;
using PilotFlow.Domain.Decisions;
using PilotFlow.Infrastructure.Persistence.Analyses;
using PilotFlow.Infrastructure.Persistence.Catalogs;
using PilotFlow.Infrastructure.Persistence.Evaluation;

namespace PilotFlow.Infrastructure.Persistence.Decisions;

internal sealed class PostgresDecisionSettingsStore(
    AnalysisDbContext db, VersionCatalogTransactions transactions,
    DecisionSettingsReader reader, EvaluationAggregateReader evaluation) : IDecisionSettingsStore
{
    public Task<Versioned<ProtocolData>?> GetProtocolAsync(VersionScope scope, CancellationToken cancellationToken) =>
        transactions.ReadAsync<ProtocolData>(scope, async token => await reader.GetProtocolAsync(scope.VersionId, token), cancellationToken);

    public Task<Versioned<SelectionData>?> GetSelectionAsync(VersionScope scope, CancellationToken cancellationToken) =>
        transactions.ReadAsync(scope, token => reader.GetSelectionAsync(scope.VersionId, token), cancellationToken);

    public Task<AnalysisWriteResult<Versioned<ProtocolData>>> ReplaceProtocolAsync(
        CatalogChange change, ProtocolData data, CancellationToken cancellationToken) =>
        transactions.WriteAsync<ProtocolData>(change, async token =>
        {
            var issues = data.ValidateInput();
            if (issues.Count > 0)
            {
                return new(AnalysisWriteStatus.ValidationFailed, Issues: issues);
            }

            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO pilot.protocol (
                    version_id, protocol_number, agreement_number, agreement_date, enterprise_party, partner_party,
                    signing_place, protocol_date, prepared_by_name, prepared_by_position, approved_by_name,
                    approved_by_position, conclusions
                ) VALUES (
                    {change.Scope.VersionId}, {data.ProtocolNumber}, {data.AgreementNumber}, {data.AgreementDate},
                    {data.EnterpriseParty}, {data.PartnerParty}, {data.SigningPlace}, {data.ProtocolDate},
                    {data.PreparedBy.FullName}, {data.PreparedBy.Position}, {data.ApprovedBy.FullName},
                    {data.ApprovedBy.Position}, {data.Conclusions}
                )
                ON CONFLICT (version_id) DO UPDATE SET
                    protocol_number = excluded.protocol_number, agreement_number = excluded.agreement_number,
                    agreement_date = excluded.agreement_date, enterprise_party = excluded.enterprise_party,
                    partner_party = excluded.partner_party, signing_place = excluded.signing_place,
                    protocol_date = excluded.protocol_date, prepared_by_name = excluded.prepared_by_name,
                    prepared_by_position = excluded.prepared_by_position, approved_by_name = excluded.approved_by_name,
                    approved_by_position = excluded.approved_by_position, conclusions = excluded.conclusions
                """, token);
            return new(AnalysisWriteStatus.Success, data);
        }, clearSelection: false, cancellationToken);

    public Task<AnalysisWriteResult<Versioned<SelectionData>>> ReplaceSelectionAsync(
        CatalogChange change, SelectionData data, CancellationToken cancellationToken) =>
        transactions.WriteAsync<SelectionData>(change, async token =>
        {
            var versionId = change.Scope.VersionId;
            var exists = await db.Database.SqlQuery<bool>($"""
                SELECT EXISTS(SELECT 1 FROM pilot.family WHERE version_id = {versionId} AND id = {data.FamilyId})
                    AND ({data.DominantProductId}::uuid IS NULL OR EXISTS(
                        SELECT 1 FROM pilot.product WHERE version_id = {versionId} AND id = {data.DominantProductId}
                    )) AS "Value"
                """).SingleAsync(token);
            if (!exists)
            {
                return new(AnalysisWriteStatus.NotFound);
            }

            var inputIssues = data.ValidateInput();
            if (inputIssues.Count > 0)
            {
                return new(AnalysisWriteStatus.ValidationFailed, Issues: inputIssues);
            }

            var aggregate = (await evaluation.ReadAsync(change.Scope, token))!;
            var issues = aggregate.SelectionIssues(data, forSave: true);
            if (issues.Count > 0)
            {
                return new(AnalysisWriteStatus.ValidationFailed, Issues: issues.Select(issue =>
                    new AnalysisIssue(issue.Code, issue.Path, issue.Message, issue.Suggestion, issue.EntityId)).ToArray());
            }

            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO pilot.selection (
                    version_id, family_id, mode, dominant_product_id, justification, direct_basis_evidence, manager_decision_reference
                ) VALUES (
                    {versionId}, {data.FamilyId}, {data.Mode}, {data.DominantProductId}, {data.Justification},
                    {StoredJson.Write(data.DirectBasisEvidence)}::jsonb, {data.ManagerDecisionReference}
                )
                ON CONFLICT (version_id) DO UPDATE SET
                    family_id = excluded.family_id, mode = excluded.mode, dominant_product_id = excluded.dominant_product_id,
                    justification = excluded.justification, direct_basis_evidence = excluded.direct_basis_evidence,
                    manager_decision_reference = excluded.manager_decision_reference
                """, token);
            return new(AnalysisWriteStatus.Success, data);
        }, clearSelection: false, cancellationToken);
}
