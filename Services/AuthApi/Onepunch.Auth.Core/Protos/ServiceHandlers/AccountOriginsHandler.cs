using System.Globalization;
using Grpc.Core;
using Onepunch.Auth.Core.Origins;

namespace Onepunch.Auth.Core.Protos;

/// <summary>Where accounts came from, for Tenant Service's Acquisition report. Internal, like UserInfoHandler.</summary>
public class AccountOriginsHandler(AccountOriginReader reader) : AccountOriginsService.AccountOriginsServiceBase
{
    public override async Task<AccountOriginsResponse> GetAccountOrigins(AccountOriginsRequest request, ServerCallContext context)
    {
        var ids = request.UserIds
            .Select(id => Guid.TryParse(id, out var parsed) ? parsed : Guid.Empty)
            .Where(id => id != Guid.Empty)
            .Distinct()
            .ToList();

        var page = await reader.ReadAsync(ids, Parse(request.CreatedFromUtc), Parse(request.CreatedToUtc), context.CancellationToken);

        var response = new AccountOriginsResponse { UndatedCount = page.UndatedCount, Truncated = page.Truncated };
        response.Accounts.AddRange(page.Accounts.Select(a => new AccountOriginRow
        {
            UserId = a.UserId.ToString(),
            Origin = a.Origin,
            CreatedAtUtc = Format(a.CreatedAtUtc),
            EmailConfirmedAtUtc = Format(a.EmailConfirmedAtUtc),
            FirstHrisSignInAtUtc = Format(a.FirstHrisSignInAtUtc),
            FirstPortalSignInAtUtc = Format(a.FirstPortalSignInAtUtc),
            OwnCompanyRequestedAtUtc = Format(a.OwnCompanyRequestedAtUtc),
        }));
        return response;
    }

    private static DateTime? Parse(string value) =>
        DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var at) ? at : null;

    private static string Format(DateTime? value) => value?.ToString("O", CultureInfo.InvariantCulture) ?? string.Empty;
}
