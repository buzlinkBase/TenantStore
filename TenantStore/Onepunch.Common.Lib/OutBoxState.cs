namespace Onepunch.Common.Lib;

public enum OutBoxState
{
    PENDING,      // Newly created, awaiting processing
    PROCESSING,   // Currently being handled (optional, useful for concurrency)
    PROCESSED,    // Successfully published
    FAILED,       // Permanent failure, no more retries
    RETRY,        // Temporary failure, will retry
    EXPIRED,      // Timed out, no longer valid (e.g., confirmation tokens)
    CANCELLED,    // Explicitly cancelled/rolled back,
    INVALID//UNKNOWN STATUS
}