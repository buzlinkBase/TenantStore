using Grpc.Core;
using Serilog;

namespace TenantStoreApi.Core.Utilities;

public class UserInfoService
{
    private readonly GetUserInfoService.GetUserInfoServiceClient _serviceClient;

    public UserInfoService(GetUserInfoService.GetUserInfoServiceClient serviceClient)
    {
        _serviceClient = serviceClient;
    }

    // AuthApi being unreachable (e.g. a transport/TLS failure on the gRPC channel) must not take
    // down the whole Members list -- degrade to an empty lookup so MembersController still
    // returns members (just without resolved email/full name) instead of a 500. Mirrors
    // MembershipGrpcClient's catch-and-degrade pattern on the AuthApi side of this same call.
    public async Task<List<UserInfoDto>> GetUsersByIdsAsync(IEnumerable<string> userIds, CancellationToken token)
    {
        try
        {
            var request = new UserIdsRequest();
            request.UserIds.AddRange(userIds);

            var response = await _serviceClient.GetUsersByIdsAsync(request, cancellationToken: token);

            return response.Users.Select(u => new UserInfoDto
            {
                Id = u.Id,
                Email = u.Email,
                FullName = u.FullName,
                Status = u.Status
            }).ToList();
        }
        catch (RpcException ex)
        {
            Log.Logger.Warning(ex, "UserInfoService.GetUsersByIdsAsync failed: {Status}", ex.StatusCode);
            return [];
        }
    }
}

public class UserInfoDto
{
    public string Id { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
}
