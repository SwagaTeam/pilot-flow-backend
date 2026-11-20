using Microsoft.EntityFrameworkCore;
using PilotFlow.Domain.Decisions;
using PilotFlow.Infrastructure.Persistence.Analyses;

namespace PilotFlow.Infrastructure.Persistence.Decisions;

internal sealed class DecisionSettingsReader(AnalysisDbContext db)
{
    internal async Task<ProtocolData> GetProtocolAsync(Guid versionId, CancellationToken cancellationToken)
    {
        var json = await db.Database.SqlQuery<string>($"""
            SELECT jsonb_build_object(
                'protocolNumber', protocol_number, 'agreementNumber', agreement_number, 'agreementDate', agreement_date,
                'enterpriseParty', enterprise_party, 'partnerParty', partner_party, 'signingPlace', signing_place,
                'protocolDate', protocol_date, 'conclusions', conclusions,
                'preparedBy', jsonb_build_object('fullName', prepared_by_name, 'position', prepared_by_position),
                'approvedBy', jsonb_build_object('fullName', approved_by_name, 'position', approved_by_position)
            )::text AS "Value"
            FROM pilot.protocol WHERE version_id = {versionId}
            """).SingleOrDefaultAsync(cancellationToken);
        return json is null ? ProtocolData.Empty : StoredJson.Read<ProtocolData>(json);
    }

    internal async Task<SelectionData?> GetSelectionAsync(Guid versionId, CancellationToken cancellationToken)
    {
        var json = await db.Database.SqlQuery<string>($"""
            SELECT jsonb_build_object(
                'familyId', family_id, 'mode', mode, 'dominantProductId', dominant_product_id, 'justification', justification,
                'directBasisEvidence', direct_basis_evidence, 'managerDecisionReference', manager_decision_reference
            )::text AS "Value"
            FROM pilot.selection WHERE version_id = {versionId}
            """).SingleOrDefaultAsync(cancellationToken);
        return json is null ? null : StoredJson.Read<SelectionData>(json);
    }
}
