using System.Net.Mail;

namespace Onepunch.Common.Lib.DTO;

/// <summary>
/// "Send this email" for services whose message is composed by the sender, such as the Client Portal's invoices, payment reminders and
/// document-expiry digests. Notification Service delivers it from its own sender address, so a service never holds mail credentials.
/// Publishing is asynchronous: the sender learns the message was handed over, not that it was delivered.
/// </summary>
public record SendEmailRequested
{
    public const int MaxSubjectLength = 200;
    public const int MaxBodyLength = 200_000;

    /// <summary>One recipient address. Send a message per recipient.</summary>
    public string To { get; set; } = string.Empty;

    public string Subject { get; set; } = string.Empty;
    public string HtmlBody { get; set; } = string.Empty;
    public string? TextBody { get; set; }

    /// <summary>The name shown as the sender (the address is always Notification Service's). Optional.</summary>
    public string? FromName { get; set; }

    /// <summary>Where a reply goes, if not to the sender. Optional.</summary>
    public string? ReplyTo { get; set; }

    /// <summary>Which service sent it, for the log.</summary>
    public string Source { get; set; } = string.Empty;

    /// <summary>
    /// Checks and tidies a message before it is sent: a single valid address, a subject without line breaks, and sane sizes. A message that
    /// fails is a permanent problem with the content, so the caller should drop it rather than retry.
    /// </summary>
    public static bool TryNormalize(SendEmailRequested message, out SendEmailRequested clean, out string? problem)
    {
        clean = message;
        problem = null;

        var to = (message.To ?? string.Empty).Trim();
        if (!IsSingleAddress(to))
        {
            problem = "The recipient is not a single valid email address.";
            return false;
        }

        var subject = new string((message.Subject ?? string.Empty).Where(c => c != '\r' && c != '\n').ToArray()).Trim();
        if (subject.Length == 0 || subject.Length > MaxSubjectLength)
        {
            problem = $"The subject must be 1-{MaxSubjectLength} characters.";
            return false;
        }

        var html = message.HtmlBody ?? string.Empty;
        if (string.IsNullOrWhiteSpace(html) || html.Length > MaxBodyLength || (message.TextBody?.Length ?? 0) > MaxBodyLength)
        {
            problem = "The message body is empty or too large.";
            return false;
        }

        string? replyTo = null;
        if (!string.IsNullOrWhiteSpace(message.ReplyTo))
        {
            replyTo = message.ReplyTo.Trim();
            if (!IsSingleAddress(replyTo))
            {
                problem = "The reply-to address is not a single valid email address.";
                return false;
            }
        }

        var fromName = new string((message.FromName ?? string.Empty).Where(c => !char.IsControl(c) && c is not ('<' or '>' or '"')).ToArray()).Trim();
        clean = message with { To = to, Subject = subject, ReplyTo = replyTo, FromName = fromName.Length == 0 ? null : fromName[..Math.Min(fromName.Length, 80)] };
        return true;
    }

    private static bool IsSingleAddress(string value) =>
        value.Length is > 0 and <= 254
        && value.IndexOfAny([',', ';', '\r', '\n', '<', '>']) < 0
        && MailAddress.TryCreate(value, out var address)
        && address.Address == value;
}
