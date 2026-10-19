using MassTransit;
using Onepunch.Auth.Core.Platform;
using Onepunch.Common.Lib.DTO;

namespace OnePunch.Auth.Core.Messaging;

/// <summary>
/// An operator-approved account erasure reached its last step (AccountHardDeleted, published by
/// Tenant Service after the companies and its own rows are gone): deletes the account's logins and
/// writes the operator log line. Idempotent, so a redelivery is harmless.
/// </summary>
public class AccountHardDeletedWorker : IConsumer<AccountHardDeleted>, IConsumer<CompanyHardDeleted>
{
    private readonly AccountEraserService _eraser;

    public AccountHardDeletedWorker(AccountEraserService eraser) => _eraser = eraser;

    public Task Consume(ConsumeContext<AccountHardDeleted> context) =>
        context.Message is { OwnerUserId: var owner } message && owner != Guid.Empty
            ? _eraser.EraseAsync(message, context.CancellationToken)
            : Task.CompletedTask;

    /// <summary>One company of an account was erased (the account stays): its logins go the same way.</summary>
    public Task Consume(ConsumeContext<CompanyHardDeleted> context) =>
        context.Message is { TenantId: var tenantId } message && tenantId != Guid.Empty
            ? _eraser.EraseAsync(message, context.CancellationToken)
            : Task.CompletedTask;
}
