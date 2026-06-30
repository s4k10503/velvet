using System;

namespace Velvet
{
    /// <summary>
    /// A type with a single value. Used in generic positions that stand in for "no value"
    /// (e.g. <see cref="MutationResult{TVariables, TData}"/> with no variables or no return value).
    /// </summary>
    public readonly struct Unit : IEquatable<Unit>
    {
        /// <summary>The single <see cref="Unit"/> value, passed where a "no value" argument is required.</summary>
        public static readonly Unit Default = default;

        public bool Equals(Unit other) => true;
        public override bool Equals(object? obj) => obj is Unit;
        public override int GetHashCode() => 0;
        public override string ToString() => "()";
        public static bool operator ==(Unit left, Unit right) => true;
        public static bool operator !=(Unit left, Unit right) => false;
    }
}
