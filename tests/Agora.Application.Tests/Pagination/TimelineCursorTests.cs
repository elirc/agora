using Agora.Application.Errors;
using Agora.Application.Pagination;

namespace Agora.Application.Tests.Pagination;

public class TimelineCursorTests
{
    [Fact]
    public void Encode_then_decode_is_identity()
    {
        var cursor = new TimelineCursor(
            new DateTimeOffset(2026, 7, 16, 9, 30, 15, TimeSpan.Zero),
            Guid.NewGuid());

        Assert.Equal(cursor, TimelineCursor.Decode(cursor.Encode()));
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-base64!!!")]
    [InlineData("aGVsbG8")]                    // valid base64url, wrong shape
    [InlineData("MTIzNDU2Nzg5MDo=")]           // ticks but no guid
    public void Decode_rejects_garbage_as_a_request_error(string raw)
    {
        // WHY: a cursor arrives from the network. Garbage must become 400
        // (RequestValidationException), never a 500 from an unhandled FormatException.
        Assert.Throws<RequestValidationException>(() => TimelineCursor.Decode(raw));
    }

    [Fact]
    public void Tokens_are_opaque_but_stable()
    {
        var cursor = new TimelineCursor(
            new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
            Guid.Parse("11111111-1111-1111-1111-111111111111"));

        // Same cursor → same token: clients may cache them.
        Assert.Equal(cursor.Encode(), cursor.Encode());
    }
}
