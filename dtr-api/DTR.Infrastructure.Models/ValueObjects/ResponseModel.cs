namespace DTR.Models;

public class ResponseModel<T>
{
    public string? Message { get; set; } = "Success";
    public int? Status { get; set; } = 200;
    public T? Data { get; set; }
    //public string? CorrelationId { get; set; }
    //public DateTime Timestamp { get; set; } = DateTime.UtcNow;
}