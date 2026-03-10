using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Onepunch.Auth.Domain;

public enum TokenStatus
{
    Active,
    Expired,
    Revoke,
}

public enum TokenType
{
    Api,
    Resource
}

public enum TokenExpirationType
{
    None,
    X1Mos,
    X6Mos,
    X12Mos,
}
