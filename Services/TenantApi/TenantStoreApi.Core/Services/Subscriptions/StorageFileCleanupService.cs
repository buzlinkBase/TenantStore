using System.Text.Json;
using Refit;
using Serilog;

namespace TenantStoreApi.Core.Services.Subscriptions;

// What hrms-api's Storage Manager answers (see hrms-api InternalStorageManagerController).
public sealed record StorageYearView(int Year, int FileCount, long Bytes);

public sealed record StorageCategoryView(string Category, string Label, int FileCount, long Bytes, List<StorageYearView> Years);

public sealed record StorageBreakdownView(int FileCount, long Bytes, List<StorageCategoryView> Categories);

public sealed record StoredFileView(
    string Key, string FileName, long SizeBytes, DateTime StoredAtUtc,
    string? RecordLabel, bool Locked, bool Deletable, Guid? AttachmentId);

public sealed record StoredFilePageView(List<StoredFileView> Items, int Total, int Page, int PageSize);

public sealed record StorageDeletePreviewView(int FileCount, long Bytes, int LockedCount);

public sealed record StorageDeleteResultView(int DeletedCount, long FreedBytes, int FailedCount);

/// <summary>Which files to delete: a category + year, or picked keys. A reason is required from an operator.</summary>
public sealed record StorageFileDeleteCommand(string? Category, int? Year, List<string>? Keys, bool ConfirmLocked, string? Reason);

/// <summary>hrms-api's internal Storage Manager endpoints (Api-key protected), one company per call.</summary>
public interface IStorageManagerApi
{
    [Get("/api/v1/internal/storage-manager/{tenantId}/breakdown")]
    Task<ResponseModel<StorageBreakdownView>> GetBreakdownAsync(Guid tenantId, CancellationToken token);

    [Get("/api/v1/internal/storage-manager/{tenantId}/files")]
    Task<ResponseModel<StoredFilePageView>> GetFilesAsync(
        Guid tenantId, string category, int year, int page, int pageSize, CancellationToken token);

    [Post("/api/v1/internal/storage-manager/{tenantId}/delete/preview")]
    Task<ResponseModel<StorageDeletePreviewView>> PreviewDeleteAsync(Guid tenantId, [Body] object request, CancellationToken token);

    [Post("/api/v1/internal/storage-manager/{tenantId}/delete")]
    Task<ResponseModel<StorageDeleteResultView>> DeleteAsync(Guid tenantId, [Body] object request, CancellationToken token);
}

/// <summary>
/// The Platform Console's view of one company's stored files, for helping an account owner free
/// space instead of buying more. The files live in hrms-api; this checks the company is one of the
/// account's own, passes the call on, and records an operator's delete in the account's billing
/// history (the platform audit trail) -- hrms-api records it in the company's audit trail too.
/// </summary>
public class StorageFileCleanupService
{
    private readonly SubscriptionUsageService _usage;
    private readonly BillingEventService _events;
    private readonly IUnitOfWorkService _uow;
    private readonly IStorageManagerApi? _api;

    public StorageFileCleanupService(
        SubscriptionUsageService usage, BillingEventService events, IUnitOfWorkService uow, IStorageManagerApi? api = null)
    {
        _usage = usage;
        _events = events;
        _uow = uow;
        _api = api;
    }

    private IStorageManagerApi Api =>
        _api ?? throw new SubscriptionRuleException("File storage isn't reachable from here (Subscriptions:Usage:HrmsApiUrl isn't set).");

    public async Task<StorageBreakdownView> GetBreakdownAsync(Guid ownerUserId, Guid tenantId, CancellationToken token)
    {
        await RequireOwnedAsync(ownerUserId, tenantId, token);
        return await CallAsync(() => Api.GetBreakdownAsync(tenantId, token));
    }

    public async Task<StoredFilePageView> GetFilesAsync(
        Guid ownerUserId, Guid tenantId, string category, int year, int page, int pageSize, CancellationToken token)
    {
        await RequireOwnedAsync(ownerUserId, tenantId, token);
        return await CallAsync(() => Api.GetFilesAsync(tenantId, category, year, page, pageSize, token));
    }

    public async Task<StorageDeletePreviewView> PreviewDeleteAsync(
        Guid ownerUserId, Guid tenantId, StorageFileDeleteCommand command, CancellationToken token)
    {
        await RequireOwnedAsync(ownerUserId, tenantId, token);
        return await CallAsync(() => Api.PreviewDeleteAsync(
            tenantId, new { command.Category, command.Year, command.Keys, command.ConfirmLocked }, token));
    }

    public async Task<StorageDeleteResultView> DeleteAsync(
        Guid ownerUserId, Guid tenantId, StorageFileDeleteCommand command, Platform.PlatformOperator actor, CancellationToken token)
    {
        if (string.IsNullOrWhiteSpace(command.Reason)) throw new SubscriptionRuleException("A reason is required.");
        var companyName = await RequireOwnedAsync(ownerUserId, tenantId, token);
        var reason = command.Reason.Trim();

        var result = await CallAsync(() => Api.DeleteAsync(tenantId, new
        {
            command.Category,
            command.Year,
            command.Keys,
            command.ConfirmLocked,
            OperatorId = actor.Id.ToString(),
            OperatorName = actor.Actor,
            Reason = reason,
        }, token));

        await _events.AddAsync(null, BillingEventTypes.StorageFilesDeleted, new
        {
            tenantId,
            company = companyName,
            command.Category,
            command.Year,
            selectedFiles = command.Keys?.Count,
            result.DeletedCount,
            result.FreedBytes,
            result.FailedCount,
            reason,
        }, actor.Actor, ownerUserId: ownerUserId, token: token);
        await _uow.CommitChangesAsync("", token);
        return result;
    }

    // A company id from the console is only trusted once it is one of the account's own companies.
    private async Task<string> RequireOwnedAsync(Guid ownerUserId, Guid tenantId, CancellationToken token)
    {
        var companies = await _usage.OwnedCompaniesAsync(ownerUserId, token);
        var company = companies.FirstOrDefault(c => c.Id == tenantId);
        return company.Id == tenantId && tenantId != Guid.Empty
            ? company.Name
            : throw new KeyNotFoundException("This company isn't one of the account's.");
    }

    private static async Task<T> CallAsync<T>(Func<Task<ResponseModel<T>>> call)
    {
        try
        {
            var response = await call();
            return response.Data ?? throw new SubscriptionRuleException("File storage returned no answer.");
        }
        catch (ApiException ex) when ((int)ex.StatusCode is >= 400 and < 500)
        {
            throw new SubscriptionRuleException(DetailOf(ex.Content) ?? "File storage refused the request.");
        }
        catch (Exception ex) when (ex is ApiException or HttpRequestException or TaskCanceledException)
        {
            Log.Logger.Warning(ex, "Storage Manager call to hrms-api failed");
            throw new SubscriptionRuleException("File storage isn't reachable right now. Try again in a moment.");
        }
    }

    // hrms-api wraps its ProblemDetails: { "data": { "detail": "..." } }.
    private static string? DetailOf(string? content)
    {
        if (string.IsNullOrWhiteSpace(content)) return null;
        try
        {
            using var json = JsonDocument.Parse(content);
            var root = json.RootElement;
            if (root.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Object) root = data;
            return root.TryGetProperty("detail", out var detail) && detail.ValueKind == JsonValueKind.String
                ? detail.GetString()
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
