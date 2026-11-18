using System.Collections;
using System.Collections.Generic;
using System.Reflection;

namespace Velvet.TestUtilities
{
    /// <summary>
    /// The walks <c>DeletionCommit</c> keeps in its pool for the next deletion, reached by reflection because production
    /// types carry no test-only members.
    /// </summary>
    public static class DeletionCommitPool
    {
        /// <summary>The walks the pool holds, as objects, since the walk's type is internal to the runtime.</summary>
        public static IReadOnlyList<object> Walks()
        {
            var pool = (IEnumerable)typeof(DeletionCommit)
                .GetField("s_pool", BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null);
            var walks = new List<object>();
            foreach (var walk in pool) walks.Add(walk);
            return walks;
        }

        /// <summary>
        /// How many walks the pool holds whose field, or field of one ("outer.inner"), is not null. A walk with no such
        /// field counts as -1 once, so a rename reads as a value the expected count is not.
        /// </summary>
        public static int WalksHolding(string path)
        {
            var holding = 0;
            foreach (var walk in Walks())
            {
                var value = FieldPathValue(walk, path);
                if (value == Missing) return -1;
                if (value != null) holding++;
            }
            return holding;
        }

        /// <summary>Empties the pool, so a case counts the walks a deletion returns and not what earlier ones left.</summary>
        public static void Clear()
        {
            var pool = typeof(DeletionCommit).GetField("s_pool", BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null);
            pool.GetType().GetMethod("Clear")!.Invoke(pool, null);
        }

        /// <summary>
        /// How many entries the lists held in a field of the walks the pool holds keep, or -1 where a walk has no such field.
        /// </summary>
        public static int EntriesInWalks(string field)
        {
            var entries = 0;
            foreach (var walk in Walks())
            {
                var value = FieldPathValue(walk, field);
                if (value == Missing) return -1;
                if (value is ICollection collection) entries += collection.Count;
            }
            return entries;
        }

        private static readonly object Missing = new();

        private static object FieldPathValue(object target, string path)
        {
            foreach (var name in path.Split('.'))
            {
                FieldInfo info = null;
                for (var type = target.GetType(); info == null && type != null; type = type.BaseType)
                {
                    info = type.GetField(name, BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance);
                }
                if (info == null) return Missing;
                target = info.GetValue(target);
                if (target == null) return null;
            }
            return target;
        }
    }
}
