using FluentAssertions;
using Onepunch.Common.Lib.DTO;
using Xunit;

namespace Onepunch.Auth.Api.Tests.Messaging;

/// <summary>
/// The rules Notification Service applies to an email another service composed. They live next to the message so every sender and the
/// consumer share one definition. (NotificationApi has no test project of its own.)
/// </summary>
public class SendEmailRequestedTests
{
    private static SendEmailRequested Valid() => new()
    {
        To = " ap@client.test ",
        Subject = "Invoice INV-1",
        HtmlBody = "<p>Hello</p>",
        FromName = "Agency Billing",
        ReplyTo = "billing@agency.test",
        Source = "client-portal",
    };

    [Fact]
    public void A_good_message_is_accepted_and_tidied()
    {
        var ok = SendEmailRequested.TryNormalize(Valid(), out var clean, out var problem);

        ok.Should().BeTrue();
        problem.Should().BeNull();
        clean.To.Should().Be("ap@client.test");
        clean.Subject.Should().Be("Invoice INV-1");
        clean.ReplyTo.Should().Be("billing@agency.test");
        clean.FromName.Should().Be("Agency Billing");
    }

    [Theory]
    [InlineData("a@client.test, b@client.test")]
    [InlineData("a@client.test;b@client.test")]
    [InlineData("Name <a@client.test>")]
    [InlineData("a@client.test\r\nBcc: x@y.test")]
    [InlineData("not an address")]
    [InlineData("")]
    public void Anything_but_one_plain_address_is_refused(string to)
    {
        var message = Valid();
        message.To = to;

        SendEmailRequested.TryNormalize(message, out _, out var problem).Should().BeFalse();
        problem.Should().Contain("recipient");
    }

    [Fact]
    public void Line_breaks_cannot_be_smuggled_into_the_subject()
    {
        var message = Valid();
        message.Subject = "Invoice\r\nBcc: attacker@evil.test";

        SendEmailRequested.TryNormalize(message, out var clean, out _).Should().BeTrue();
        clean.Subject.Should().Be("InvoiceBcc: attacker@evil.test");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void An_empty_subject_is_refused(string subject)
    {
        var message = Valid();
        message.Subject = subject;

        SendEmailRequested.TryNormalize(message, out _, out _).Should().BeFalse();
    }

    [Fact]
    public void An_overlong_subject_or_oversized_body_is_refused()
    {
        var longSubject = Valid();
        longSubject.Subject = new string('s', SendEmailRequested.MaxSubjectLength + 1);
        var hugeBody = Valid();
        hugeBody.HtmlBody = new string('x', SendEmailRequested.MaxBodyLength + 1);
        var emptyBody = Valid();
        emptyBody.HtmlBody = "  ";

        foreach (var message in new[] { longSubject, hugeBody, emptyBody })
            SendEmailRequested.TryNormalize(message, out _, out _).Should().BeFalse();
    }

    [Fact]
    public void A_bad_reply_to_is_refused_but_a_missing_one_is_fine()
    {
        var bad = Valid();
        bad.ReplyTo = "a@x.test, b@x.test";
        var none = Valid();
        none.ReplyTo = null;

        SendEmailRequested.TryNormalize(bad, out _, out var problem).Should().BeFalse();
        problem.Should().Contain("reply-to");
        SendEmailRequested.TryNormalize(none, out var clean, out _).Should().BeTrue();
        clean.ReplyTo.Should().BeNull();
    }

    [Fact]
    public void The_sender_name_cannot_carry_control_characters_or_address_syntax()
    {
        var message = Valid();
        message.FromName = "Agency\r\n <evil@x.test> \"Billing\"";

        SendEmailRequested.TryNormalize(message, out var clean, out _).Should().BeTrue();
        clean.FromName.Should().NotContainAny("<", ">", "\"", "\r", "\n");
    }

    [Fact]
    public void A_blank_sender_name_falls_back_to_the_default()
    {
        var message = Valid();
        message.FromName = "  ";

        SendEmailRequested.TryNormalize(message, out var clean, out _).Should().BeTrue();
        clean.FromName.Should().BeNull();
    }

    [Fact]
    public void The_message_type_has_the_name_other_services_publish()
    {
        // MassTransit routes by full name. The Client Portal keeps its own copy under exactly this name.
        typeof(SendEmailRequested).FullName.Should().Be("Onepunch.Common.Lib.DTO.SendEmailRequested");
    }
}
