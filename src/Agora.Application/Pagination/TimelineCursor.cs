using System.Buffers.Text;
using System.Globalization;
using System.Text;
using Agora.Application.Errors;

namespace Agora.Application.Pagination;

/// <summary>
/// Position in a (CreatedAtUtc DESC, Id DESC) ordering — the ordering every "newest
/// things" list in Agora uses. Serialized as an opaque base64url token.
/// </summary>
// LEARN: cursor pagination beats offset pagination for the same reason append-only
// beats update-in-place: concurrent writes can't shift what the client already saw.
// The Id tiebreak matters — CreatedAtUtc alone duplicates or skips rows on timestamp
// collisions. See docs/adr/0002-api-conventions.md.
public readonly record struct TimelineCursor(DateTimeOffset CreatedAtUtc, Guid Id)
{
    public string Encode()
    {
        string plain = string.Create(
            CultureInfo.InvariantCulture,
            $"{CreatedAtUtc.UtcTicks}:{Id:N}");

        return Base64Url.EncodeToString(Encoding.UTF8.GetBytes(plain));
    }

    public static TimelineCursor Decode(string raw)
    {
        // WHY: cursors are OURS, but they travel through THEIR query strings — treat
        // every incoming token as attacker-controlled and fail as a 400, never a 500.
        try
        {
            string plain = Encoding.UTF8.GetString(Base64Url.DecodeFromChars(raw));
            string[] parts = plain.Split(':');

            long ticks = long.Parse(parts[0], CultureInfo.InvariantCulture);
            var id = Guid.ParseExact(parts[1], "N");

            return new TimelineCursor(new DateTimeOffset(ticks, TimeSpan.Zero), id);
        }
        catch (Exception ex) when (ex is not RequestValidationException)
        {
            throw new RequestValidationException("The cursor is not valid.");
        }
    }
}
