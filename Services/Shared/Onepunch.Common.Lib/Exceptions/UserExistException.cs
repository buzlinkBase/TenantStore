namespace Onepunch.Common.Lib.Exceptions;

public class UserExistException :Exception
{
    public UserExistException(string? message = "User is already exist") : base(message) { } 
}
 