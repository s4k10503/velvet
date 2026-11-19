        /// <summary>
        /// Generic overload for 3 dependencies, so each dep's IEquatable&lt;T&gt; constraint is a
        /// compile-time gate that each dependency type declares value-equality semantics — the comparison itself
        /// runs through the same boxed dependency array as the params object[] overload.
        /// </summary>
        public static MemoNode Memoized<T1, T2, T3>(Func<VNode> factory, T1? dep1, T2? dep2, T3? dep3)
            where T1 : IEquatable<T1>
            where T2 : IEquatable<T2>
            where T3 : IEquatable<T3>
        {
            return new MemoNode
            {
                Factory = factory,
                Dependencies = new object?[] { dep1, dep2, dep3 },
            };
        }
