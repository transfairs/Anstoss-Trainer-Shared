// Windows 2000 / .NET 2.0 build only: instance methods that .NET 4.0 added to existing types, as
// extension methods. The compiler only picks these when the real instance method is missing, so the
// call sites stay the same in both builds.

using System.Text;

namespace System
{
    /// <summary>.NET 4.0 instance methods rebuilt for .NET 2.0.</summary>
    public static class FrameworkExtensions
    {
        /// <summary>True if every bit set in <paramref name="flag"/> is also set in <paramref name="value"/> (Enum.HasFlag).</summary>
        public static bool HasFlag(this Enum value, Enum flag)
        {
            if (flag == null)
            {
                throw new ArgumentNullException(nameof(flag));
            }

            if (value.GetType() != flag.GetType())
            {
                throw new ArgumentException("Enum types differ: " + flag.GetType() + " vs. " + value.GetType(), nameof(flag));
            }

            ulong bits = Bits(flag);
            return (Bits(value) & bits) == bits;
        }

        /// <summary>Empties the builder (StringBuilder.Clear).</summary>
        public static StringBuilder Clear(this StringBuilder builder)
        {
            builder.Length = 0;
            return builder;
        }

        // Raw bits of an enum value; negative values of signed enums become their two's complement
        // (Convert.ToUInt64 would throw on them).
        private static ulong Bits(Enum value)
        {
            switch (Convert.GetTypeCode(value))
            {
                case TypeCode.SByte:
                case TypeCode.Int16:
                case TypeCode.Int32:
                case TypeCode.Int64:
                    return unchecked((ulong)Convert.ToInt64(value, null));
                default:
                    return Convert.ToUInt64(value, null);
            }
        }
    }
}
