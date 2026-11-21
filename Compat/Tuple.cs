// Windows 2000 / .NET 2.0 build only: the .NET 4.0 Tuple types the trainers use, with value equality
// like the originals (some serve as dictionary values or are compared).

using System.Collections.Generic;

namespace System
{
    /// <summary>Factory methods for tuples.</summary>
    public static class Tuple
    {
        /// <summary>Creates a pair.</summary>
        public static Tuple<T1, T2> Create<T1, T2>(T1 item1, T2 item2) => new Tuple<T1, T2>(item1, item2);

        /// <summary>Creates a triple.</summary>
        public static Tuple<T1, T2, T3> Create<T1, T2, T3>(T1 item1, T2 item2, T3 item3) => new Tuple<T1, T2, T3>(item1, item2, item3);

        internal static int Combine(int h1, int h2) => ((h1 << 5) + h1) ^ h2;
    }

    /// <summary>Immutable pair.</summary>
    [Serializable]
    public class Tuple<T1, T2>
    {
        /// <summary>Creates the pair.</summary>
        public Tuple(T1 item1, T2 item2)
        {
            this.Item1 = item1;
            this.Item2 = item2;
        }

        /// <summary>First component.</summary>
        public T1 Item1 { get; }

        /// <summary>Second component.</summary>
        public T2 Item2 { get; }

        /// <inheritdoc/>
        public override bool Equals(object obj) =>
            obj is Tuple<T1, T2> other
            && EqualityComparer<T1>.Default.Equals(this.Item1, other.Item1)
            && EqualityComparer<T2>.Default.Equals(this.Item2, other.Item2);

        /// <inheritdoc/>
        public override int GetHashCode() =>
            Tuple.Combine(EqualityComparer<T1>.Default.GetHashCode(this.Item1), EqualityComparer<T2>.Default.GetHashCode(this.Item2));

        /// <inheritdoc/>
        public override string ToString() => "(" + this.Item1 + ", " + this.Item2 + ")";
    }

    /// <summary>Immutable triple.</summary>
    [Serializable]
    public class Tuple<T1, T2, T3>
    {
        /// <summary>Creates the triple.</summary>
        public Tuple(T1 item1, T2 item2, T3 item3)
        {
            this.Item1 = item1;
            this.Item2 = item2;
            this.Item3 = item3;
        }

        /// <summary>First component.</summary>
        public T1 Item1 { get; }

        /// <summary>Second component.</summary>
        public T2 Item2 { get; }

        /// <summary>Third component.</summary>
        public T3 Item3 { get; }

        /// <inheritdoc/>
        public override bool Equals(object obj) =>
            obj is Tuple<T1, T2, T3> other
            && EqualityComparer<T1>.Default.Equals(this.Item1, other.Item1)
            && EqualityComparer<T2>.Default.Equals(this.Item2, other.Item2)
            && EqualityComparer<T3>.Default.Equals(this.Item3, other.Item3);

        /// <inheritdoc/>
        public override int GetHashCode() =>
            Tuple.Combine(Tuple.Combine(EqualityComparer<T1>.Default.GetHashCode(this.Item1), EqualityComparer<T2>.Default.GetHashCode(this.Item2)),
                EqualityComparer<T3>.Default.GetHashCode(this.Item3));

        /// <inheritdoc/>
        public override string ToString() => "(" + this.Item1 + ", " + this.Item2 + ", " + this.Item3 + ")";
    }
}
