using Refit; 

namespace Onepunch.Auth.Core.Interfaces;

public interface IAccountMembershipClient 
{
    [Get("/api/v1/members/account-tenants")]
    [Headers("Accept: application/x-msgpack")]
    Task<ResponseModel<List<AccountMemberShipQuery>>> FindTenants(
         [Query][AliasAs("user-id")] Guid userId,
        [Header("Authorization")] string authorization);
}
