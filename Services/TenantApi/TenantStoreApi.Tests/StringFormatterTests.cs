using FluentAssertions;
using TenantStoreApi.Core;
using Xunit;

namespace TenantStoreApi.Tests;

public class StringFormatterTests
{
    [Theory]
    [InlineData(0, "000000")]
    [InlineData(5, "000005")]
    [InlineData(42, "000042")]
    [InlineData(123456, "123456")]
    public void FormatCode_PadsToSixDigits(int count, string expected)
    {
        count.FormatCode().Should().Be(expected);
    }

    [Fact]
    public void FormatCode_DoesNotTruncate_WhenLongerThanSixDigits()
    {
        1234567.FormatCode().Should().Be("1234567");
    }
}
