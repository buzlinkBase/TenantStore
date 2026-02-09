namespace DTR.Models;

public class OutboxMessage
{
    public Guid Id { get; set; } = Guid.NewGuid();

    // Timestamp when the message was created
    public DateTime OccurredOnUtc { get; set; } = DateTime.UtcNow;

    // Timestamp when the message was processed (null if pending)
    public DateTime? ProcessedOnUtc { get; set; }

    // Optional field for tracking retries or failures
    public int AttemptCount { get; set; } = 0;

    // Message type (e.g., event name, domain type)
    public string Type { get; set; } = string.Empty;

    // Serialized payload (JSON, XML, etc.)
    public string Payload { get; set; } = string.Empty;

    // Optional metadata for routing, correlation, etc.
    public string? Metadata { get; set; }

    // Optional error message if processing failed
    public string? Error { get; set; }
}