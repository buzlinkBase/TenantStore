namespace Onepunch.Common.Lib;

public class UnauthorizedException : Exception
{
    public UnauthorizedException(string? message = "Unauthorized") : base(message) { }
}
