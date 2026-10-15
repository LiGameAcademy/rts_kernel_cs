using Rts.Kernel.Numerics;

namespace NumericPrototype;

// The probe retains its test surface while the accepted input rule has one production owner.
internal static class InputQuantizer
{
    internal const int MaximumTextLength = 256;
    internal const int MaximumExponent = 256;
    internal static PrototypeScalar Parse(string text) => new(SimScalar.Parse(text).Raw);
}
