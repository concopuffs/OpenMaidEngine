namespace Ome.Engine.Vm;

/// <summary>
/// Float arithmetic as native AGE's x87 does it: the exact result of two exact operands, rounded once to a float.
/// Native creates its Direct3D 9 device without <c>D3DCREATE_FPU_PRESERVE</c> (4.38D gfx_create_d3d9_device
/// @0x484690), so Direct3D sets the x87 of that thread, which also runs the script VM, to single precision with
/// round to nearest; the float opcodes' sums, differences, products and quotients are rounded to 24 bits at once
/// (docs/engine-re.md, "The x87 runs at single precision"). Computing in double and then rounding to a float rounds
/// twice, which differs only when an operand is an integer of more than 24 significant bits and the double lands on
/// the midpoint of two floats while the exact result does not; each method here detects that case from the double's
/// exact error and rounds to the side the exact result lies on.
/// </summary>
public static class SinglePrecision
{
    public static float Add(double a, double b)
    {
        double sum = a + b;
        if (!double.IsFinite(sum)) return (float)sum;
        // Knuth's two-sum: what the double sum leaves out, exactly.
        double bPart = sum - a;
        double error = (a - (sum - bPart)) + (b - bPart);
        return Round(sum, error);
    }

    public static float Subtract(double a, double b) => Add(a, -b);

    public static float Multiply(double a, double b)
    {
        double product = a * b;
        if (!double.IsFinite(product)) return (float)product;
        return Round(product, System.Math.FusedMultiplyAdd(a, b, -product));
    }

    /// <summary>A zero divisor gives the IEEE infinity or NaN, as native's (it does not test the divisor).</summary>
    public static float Divide(double a, double b)
    {
        double quotient = a / b;
        if (!double.IsFinite(quotient) || quotient == 0) return (float)quotient;
        // a - quotient * b, exact for a correctly rounded quotient: the exact quotient is above the double when the
        // remainder and the divisor share a sign.
        double remainder = System.Math.FusedMultiplyAdd(-quotient, b, a);
        double direction = remainder == 0 ? 0 : (remainder > 0) == (b > 0) ? 1 : -1;
        return Round(quotient, direction);
    }

    // value: the double nearest the exact result; error: the exact result minus value, or only its sign. A double
    // rounds to the right float unless it is the midpoint of two floats while the exact result is not.
    private static float Round(double value, double error)
    {
        float rounded = (float)value;
        if (error == 0 || !float.IsFinite(rounded) || (double)rounded == value) return rounded;
        float other = value > rounded ? System.MathF.BitIncrement(rounded) : System.MathF.BitDecrement(rounded);
        // Two neighbouring floats and their mean are exact in a double.
        if (value != ((double)rounded + other) / 2) return rounded;
        return error > 0 ? System.Math.Max(rounded, other) : System.Math.Min(rounded, other);
    }
}
