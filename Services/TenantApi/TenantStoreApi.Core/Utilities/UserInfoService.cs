namespace TenantStoreApi.Core.Utilities;

public class UserInfoService
{
    private readonly GetUserInfoService.GetUserInfoServiceClient _serviceClient;

    public UserInfoService(GetUserInfoService.GetUserInfoServiceClient serviceClient)
    {
        _serviceClient = serviceClient;
    }

    public async Task<List<UserInfoDto>> GetUsersByIdsAsync(IEnumerable<string> userIds, CancellationToken token)
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
}

public class UserInfoDto
{
    public string Id { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
}
