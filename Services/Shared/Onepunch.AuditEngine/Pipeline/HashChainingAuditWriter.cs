using Onepunch.AuditEngine.Integrity;

namespace Onepunch.AuditEngine.Pipeline;

/// <summary>
/// Decorator that links each event to the previous one in its organization's chain before
/// handing it to the inner (storage) writer. It depends only on <see cref="IAuditWriter"/> and
/// <see cref="IAuditChainHead"/>, so every storage adapter gets tamper-evidence without
/// implementing any hashing itself.
/// Scoped: it remembers the head of each chain it has extended, so several events staged in one
/// unit of work (not yet saved, so not yet visible to the store) chain onto each other.
/// </summary>
public sealed class HashChainingAuditWriter(IAuditWriter inner, IAuditChainHead chainHead) : IAuditWriter
{
    private readonly Dictionary<string, string?> _heads = new(StringComparer.Ordinal);

    public async Task WriteAsync(AuditEvent auditEvent, CancellationToken token = default)
    {
        var chainKey = auditEvent.OrganizationId ?? string.Empty;
        if (!_heads.TryGetValue(chainKey, out var previous))
            previous = await chainHead.GetHeadHashAsync(auditEvent.OrganizationId, token);

        var linked = auditEvent with { PreviousEventHash = previous };
        var hashed = linked with { Hash = AuditHashCalculator.Compute(linked) };

        await inner.WriteAsync(hashed, token);
        _heads[chainKey] = hashed.Hash;
    }
}
