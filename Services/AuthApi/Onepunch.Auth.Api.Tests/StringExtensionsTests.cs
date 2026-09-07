using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using OnePunch.Auth.Core;
using Xunit;

namespace Onepunch.Auth.Api.Tests;

public class StringExtensionsTests
{
    [Fact]
    public void EnumerateIdentityErrors_ReturnsEmptyString_WhenNull()
    {
        IEnumerable<IdentityError>? errors = null;

        var result = errors!.EnumerateIdentityErrors();

        result.Should().BeEmpty();
    }

    [Fact]
    public void EnumerateIdentityErrors_ReturnsEmptyString_WhenEmpty()
    {
        var errors = Enumerable.Empty<IdentityError>();

        var result = errors.EnumerateIdentityErrors();

        result.Should().BeEmpty();
    }

    [Fact]
    public void EnumerateIdentityErrors_JoinsErrors_WithNewLine()
    {
        var errors = new List<IdentityError>
        {
            new() { Code = "DuplicateEmail", Description = "Email already taken." },
            new() { Code = "PasswordTooShort", Description = "Password too short." },
        };

        var result = errors.EnumerateIdentityErrors();

        result.Should().Be($"DuplicateEmail: Email already taken.{Environment.NewLine}PasswordTooShort: Password too short.");
    }
}
