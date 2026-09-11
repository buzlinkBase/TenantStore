using Grpc.Core;
using OnePunch.Auth.Core.Services;

namespace Onepunch.Auth.Core.Protos;

public class UserInfoHandler : GetUserInfoService.GetUserInfoServiceBase
{
    private readonly UserService _userService;
    public UserInfoHandler(UserService userService)
    {
        _userService = userService;
    }

    public override async Task<UserInfosResponse> GetUsersByIds(UserIdsRequest request, ServerCallContext context)
    {
        var response = new UserInfosResponse();
        // 1. Parse string IDs into valid Guids
        var validIds = request.UserIds
            .Select(id => Guid.TryParse(id, out var parsedGuid) ? parsedGuid : (Guid?)null)
            .Where(id => id.HasValue)
            .Select(id => id!.Value)
            .ToList();

        if (validIds.Count == 0)
        {
            return response;
        }

        // 2. Fetch all matching users in a single database query
        var users = await _userService.Context.Users
            .Where(x => validIds.Contains(x.Id))
            .ToListAsync(context.CancellationToken);

        // 3. Populate response
        foreach (var user in users)
        {
            response.Users.Add(new UserInfo
            {
                Id = user.Id.ToString(),
                Email = user.Email ?? string.Empty,
                FullName = user.FullName ?? string.Empty,
                Status = user.Status ?? "Inactive"
            });
        }
        return response;
    }
}