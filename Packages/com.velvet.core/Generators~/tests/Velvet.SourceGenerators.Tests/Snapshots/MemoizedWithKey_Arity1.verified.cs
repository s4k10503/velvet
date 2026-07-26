        /// <summary>
        /// Keyed variant of the 1-dependency Memoized overload, so siblings created at the same call site
        /// can be told apart by an explicit key instead of by position, with the same per-dep IEquatable&lt;T&gt;
        /// compile-time gate that each dependency type declares value-equality semantics.
        /// </summary>
        public static MemoNode MemoizedWithKey<T1>(string key, Func<VNode> factory, T1 dep1)
            where T1 : IEquatable<T1>
        {
            return new MemoNode
            {
                Key = key,
                Factory = factory,
                Dependencies = new object[] { dep1 },
            };
        }
