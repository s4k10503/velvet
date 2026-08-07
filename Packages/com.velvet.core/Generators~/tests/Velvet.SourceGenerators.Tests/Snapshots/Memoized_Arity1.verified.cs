        /// <summary>
        /// Generic overload for 1 dependency, so each dep's IEquatable&lt;T&gt; constraint is a
        /// compile-time gate that each dependency type declares value-equality semantics — the comparison itself
        /// runs through the same boxed dependency array as the params object[] overload.
        /// </summary>
        public static MemoNode Memoized<T1>(Func<VNode> factory, T1? dep1)
            where T1 : IEquatable<T1>
        {
            return new MemoNode
            {
                Factory = factory,
                Dependencies = new object?[] { dep1 },
            };
        }
