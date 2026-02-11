using Grpc.Core;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Onepunch.Auth.Core.Protos;

public class CheckEmailHandler : CheckEmailService.CheckEmailServiceBase
{
    public override async Task<CheckEmailResponse> Check(EmailPayload request, ServerCallContext context)
    { 
        var email=request.Email;

        var response = new CheckEmailResponse
        {
            Message = "",
            Valid = false
        };
        return response;
    }
}
