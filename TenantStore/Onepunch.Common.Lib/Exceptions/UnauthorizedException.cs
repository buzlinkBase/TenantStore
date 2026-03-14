using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Onepunch.Common.Lib;

public class UnauthorizedException : Exception
{
    public UnauthorizedException(string? message ="Unauthorized") : base(message) { }
}
 