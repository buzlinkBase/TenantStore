namespace Onepunch.AuditEngine.Integrity;

/// <summary>
/// Checks a set of stored events for tampering:
/// - <see cref="AuditIntegrityIssue.ContentAltered"/>: an event's stored hash no longer matches
///   its content (a field was edited after the fact);
/// - <see cref="AuditIntegrityIssue.PredecessorMissing"/>: an event points at a predecessor hash
///   that no event in its chain has (an earlier event was deleted or altered).
/// Concurrent saves can legitimately link two events to the same predecessor; that fork is not
/// reported, since every link still resolves.
/// </summary>
public static class AuditChainVerifier
{
    public static IReadOnlyList<AuditIntegrityFinding> Verify(IEnumerable<AuditEvent> events)
    {
        var findings = new List<AuditIntegrityFinding>();

        // One chain per organization per recording service (see HashChainingAuditWriter).
        foreach (var chain in events.GroupBy(e => (e.OrganizationId ?? string.Empty, e.Source ?? string.Empty)))
        {
            var hashes = chain.Where(e => e.Hash != null).Select(e => e.Hash!).ToHashSet(StringComparer.Ordinal);

            foreach (var e in chain)
            {
                if (e.Hash == null || !string.Equals(e.Hash, AuditHashCalculator.Compute(e), StringComparison.Ordinal))
                    findings.Add(new AuditIntegrityFinding(e.Id, AuditIntegrityIssue.ContentAltered));
                else if (e.PreviousEventHash != null && !hashes.Contains(e.PreviousEventHash))
                    findings.Add(new AuditIntegrityFinding(e.Id, AuditIntegrityIssue.PredecessorMissing));
            }
        }

        return findings;
    }
}

public enum AuditIntegrityIssue
{
    ContentAltered,
    PredecessorMissing,
}

public sealed record AuditIntegrityFinding(Guid EventId, AuditIntegrityIssue Issue);
