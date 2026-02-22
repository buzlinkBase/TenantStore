using Grpc.Core;
using Microsoft.Extensions.DependencyInjection;
using OnePunch.Auth.Core.Services;

namespace Onepunch.Auth.Core.Protos;

public class CheckEmailHandler : CheckEmailService.CheckEmailServiceBase
{
    private readonly IServiceScopeFactory _factory;
    public CheckEmailHandler(IServiceScopeFactory factory)
    {
        _factory = factory;
    }
    public override async Task<CheckEmailResponse> Check(EmailPayload request, ServerCallContext context)
    {
        var email = request.Email;
        using (var scope = _factory.CreateScope())
        {
            var userService = scope.ServiceProvider.GetRequiredService<UserService>();
            var user = await userService.GetByEmailAsync(email);
            var response = new CheckEmailResponse
            {
                Message = user != null ? "Exists" : "email available",
                Exists = user != null
            };
            return response;
        }
    }
}
