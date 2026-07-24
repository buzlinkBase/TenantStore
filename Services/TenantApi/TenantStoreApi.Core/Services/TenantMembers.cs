namespace TenantStoreApi.Core.Services;

public class TenantMembers
{
    public TenantMembers()
    {
    }

    //public async Task<(User user, IdentityResult result)> RegisterInvitesAsync(CreateInvitedUser payload, CancellationToken ctoken)
    //{
    //    var token = Encoding.UTF8.GetString(TokenEncodingHelper.FromBase64Url(payload.Token));
    //    var userToken = ObjectSerializer.Deserialize<EmailTokenInfo>(token);
    //    var emailInfoDb = await FindToken(payload.Token);
    //    if (emailInfoDb == null) throw new Exception("Unverified token");
    //    if (emailInfoDb.TenantId != userToken.TenantId) throw new Exception("Invalid payload");
    //    if (emailInfoDb.Email != userToken.Email) throw new Exception("Invalid payload");
    //    if (IsTokenExpired(emailInfoDb) || emailInfoDb.IsUsed)
    //    {
    //        await RemoveAsync(emailInfoDb.Id);
    //        await CommitChangesAsync(ctoken);
    //        throw new Exception("Token expired");
    //    }

    //    var user = new User
    //    {
    //        TenantId = userToken.TenantId,
    //        UserName = userToken.Email,
    //        Email = userToken.Email,
    //        Name = payload.Name ?? userToken.Name,
    //        Status = "Active",
    //        EmailConfirmed = true
    //    };
    //    await RemoveAsync(emailInfoDb.Id);
    //    var result = await _manager.CreateAsync(user, payload.Password);
    //    if (result.Succeeded)
    //    {
    //        await CommitChangesAsync(ctoken);
    //        return (user, result);
    //    }
    //    return (user, result);
    //}
}
