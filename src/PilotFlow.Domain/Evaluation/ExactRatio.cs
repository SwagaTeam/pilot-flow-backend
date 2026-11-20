using System.Numerics;

namespace PilotFlow.Domain.Evaluation;

public readonly record struct ExactRatio : IComparable<ExactRatio>
{
    private const int WireScale = 1_000_000;
    private static readonly BigInteger WireExclusiveLimit = BigInteger.Pow(10, 24);

    private ExactRatio(BigInteger numerator, BigInteger denominator)
    {
        var divisor = BigInteger.GreatestCommonDivisor(numerator, denominator);
        Numerator = numerator / divisor;
        Denominator = denominator / divisor;
    }

    private BigInteger Numerator { get; }
    private BigInteger Denominator { get; }

    public static ExactRatio From(decimal value)
    {
        var (coefficient, scale) = DecimalParts(value);
        return new(coefficient, BigInteger.Pow(10, scale));
    }

    public static ExactRatio? From(decimal? value) => value is { } amount ? From(amount) : null;

    public static ExactRatio? Divide(decimal? amount, decimal? total, int multiplier = 1)
    {
        if (amount is not { } numerator || total is not > 0)
        {
            return null;
        }

        var (left, leftScale) = DecimalParts(numerator);
        var (right, rightScale) = DecimalParts(total.Value);
        return new(left * multiplier * BigInteger.Pow(10, rightScale), right * BigInteger.Pow(10, leftScale));
    }

    public int CompareTo(ExactRatio other) =>
        (Numerator * other.Denominator).CompareTo(other.Numerator * Denominator);

    public static bool operator <(ExactRatio left, ExactRatio right) => left.CompareTo(right) < 0;
    public static bool operator <=(ExactRatio left, ExactRatio right) => left.CompareTo(right) <= 0;
    public static bool operator >(ExactRatio left, ExactRatio right) => left.CompareTo(right) > 0;
    public static bool operator >=(ExactRatio left, ExactRatio right) => left.CompareTo(right) >= 0;

    public bool TryRoundForWire(out decimal value)
    {
        var scaled = BigInteger.DivRem(BigInteger.Abs(Numerator) * WireScale, Denominator, out var remainder);
        if (remainder * 2 >= Denominator)
        {
            scaled++;
        }

        if (scaled >= WireExclusiveLimit)
        {
            value = default;
            return false;
        }

        value = (decimal)scaled / WireScale * Numerator.Sign;
        return true;
    }

    private static (BigInteger Coefficient, int Scale) DecimalParts(decimal value)
    {
        Span<int> parts = stackalloc int[4];
        decimal.GetBits(value, parts);
        var coefficient = ((BigInteger)(uint)parts[2] << 64) | ((BigInteger)(uint)parts[1] << 32) | (uint)parts[0];
        return (parts[3] < 0 ? -coefficient : coefficient, (parts[3] >> 16) & 0xff);
    }
}
