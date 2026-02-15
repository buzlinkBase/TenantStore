namespace Onepunch.Common.Lib.DTO;
public record MessagePayload<T> where T : class, new()
{
    public T Data { get; set; }
}
