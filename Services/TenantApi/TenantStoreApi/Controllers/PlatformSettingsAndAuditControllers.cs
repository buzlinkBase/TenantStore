using Microsoft.AspNetCore.RateLimiting;
using Asp.Versioning;
using Microsoft.AspNetCore.Mvc;
using Onepunch.Common.Lib.Platform;
using TenantStoreApi.Core.Services.Platform;
using TenantStoreApi.Security;

namespace TenantStoreApi.Controllers;

public record SetSettingRequest
{
    public string? Value { get; set; }
}

/// <summary>
/// Platform-wide settings an operator may change (trial length, grace period, retention, payment
/// instructions). SuperAdmin only. A change takes effect for the whole platform within seconds and is
/// recorded in the audit trail; "reset" falls back to the configured value.
/// </summary>
[Route("api/v{version:apiVersion}/platform/settings")]
[ApiVersion("1.0")]
[EnableRateLimiting(PlatformRateLimiting.PolicyName)]
[ApiController]
public class PlatformSettingsController : ControllerBase
{
    private readonly PlatformSettingsService _settings;

    public PlatformSettingsController(PlatformSettingsService settings) => _settings = settings;

    [HttpGet]
    [PlatformAuthorize(PlatformPermissions.SettingsManage)]
    [ProducesResponseType(typeof(List<PlatformSettingView>), StatusCodes.Status200OK)]
    public async Task<IActionResult> List(CancellationToken token) => Ok(await _settings.GetAllAsync(token));

    [HttpPut("{key}")]
    [PlatformAuthorize(PlatformPermissions.SettingsManage)]
    public Task<IActionResult> Set(string key, [FromBody] SetSettingRequest request, CancellationToken token) =>
        SubscriptionResults.Run(async () =>
        {
            await _settings.SetAsync(key, request.Value, PlatformActor.From(User), token);
            await _settings.CommitChangesAsync(token);
            return Ok();
        });

    [HttpDelete("{key}")]
    [PlatformAuthorize(PlatformPermissions.SettingsManage)]
    public Task<IActionResult> Reset(string key, CancellationToken token) =>
        SubscriptionResults.Run(async () =>
        {
            await _settings.ResetAsync(key, PlatformActor.From(User), token);
            await _settings.CommitChangesAsync(token);
            return Ok();
        });
}

/// <summary>The audit trail across all accounts: operator actions and system events, filterable and paged.</summary>
[Route("api/v{version:apiVersion}/platform/audit")]
[ApiVersion("1.0")]
[EnableRateLimiting(PlatformRateLimiting.PolicyName)]
[ApiController]
public class PlatformAuditController : ControllerBase
{
    private readonly PlatformAuditService _audit;

    public PlatformAuditController(PlatformAuditService audit) => _audit = audit;

    [HttpGet]
    [PlatformAuthorize(PlatformPermissions.AuditRead)]
    [ProducesResponseType(typeof(PagedResult<AuditEntry>), StatusCodes.Status200OK)]
    public async Task<IActionResult> List([FromQuery] AuditQuery query, CancellationToken token) =>
        Ok(await _audit.ListAsync(query, token));

    [HttpGet("types")]
    [PlatformAuthorize(PlatformPermissions.AuditRead)]
    [ProducesResponseType(typeof(List<string>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Types(CancellationToken token) => Ok(await _audit.ListTypesAsync(token));
}
