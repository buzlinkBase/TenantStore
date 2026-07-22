namespace Onepunch.Common.Lib.Exceptions;

public class DOException : Exception
{
    private readonly int _code;
    public DOException(int code, string message, Exception? ex = null) : base(message, ex)
    {
        _code = code;
    }

    public int Code => _code;
}
