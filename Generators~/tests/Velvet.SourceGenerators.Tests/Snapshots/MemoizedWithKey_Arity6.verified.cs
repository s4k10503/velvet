        /// <summary>
        /// Keyed variant of the 6-dependency Memoized overload, so siblings created at the same call site
        /// can be told apart by an explicit key instead of by position, with the same per-dep IEquatable&lt;T&gt;
        /// compile-time gate that each dependency type declares value-equality semantics.
        /// </summary>
        public static MemoNode MemoizedWithKey<T1, T2, T3, T4, T5, T6>(string? key, Func<VNode> factory, T1? dep1, T2? dep2, T3? dep3, T4? dep4, T5? dep5, T6? dep6)
            where T1 : IEquatable<T1>
            where T2 : IEquatable<T2>
            where T3 : IEquatable<T3>
            where T4 : IEquatable<T4>
            where T5 : IEquatable<T5>
            where T6 : IEquatable<T6>
        {
            return new MemoNode
            {
                Key = key,
                Factory = factory,
                Dependencies = new object?[] { dep1, dep2, dep3, dep4, dep5, dep6 },
            };
        }
