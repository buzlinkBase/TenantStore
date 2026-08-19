using Grpc.Core;
using Microsoft.AspNetCore.Identity;
using OnePunch.Auth.Domain.Entities;

namespace Onepunch.Auth.Core.Protos;

public class UserInfoHandler : GetUserInfoService.GetUserInfoServiceBase
{
    private readonly UserManager<User> _userManager;

    public UserInfoHandler(UserManager<User> userManager)
    {
        _userManager = userManager;
    }

    public override async Task<UserInfosResponse> GetUsersByIds(UserIdsRequest request, ServerCallContext context)
    {
        var response = new UserInfosResponse();

        foreach (var userId in request.UserIds)
        {
            if (Guid.TryParse(userId, out var id))
            {
                var user = await _userManager.FindByIdAsync(userId);
                if (user != null)
                {
                    response.Users.Add(new UserInfo
                    {
                        Id = user.Id.ToString(),
                        Email = user.Email ?? string.Empty,
                        FullName = user.FullName ?? string.Empty,
                        Status = user.Status ?? "Active"
                    });
                }
            }
        }

        return response;
    }
}
