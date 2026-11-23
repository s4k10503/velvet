using System.Collections;
using System.Reflection;

namespace Velvet.TestUtilities
{
    /// <summary>
    /// What a mounted tree's recording pass still holds once it has ended, reached by reflection because production types
    /// carry no test-only members.
    /// </summary>
    public static class ReconcilePassProbe
    {
        /// <summary>
        /// How many anchors the pass has rented and not taken back, or -1 where it holds no such list.
        /// </summary>
        public static int AnchorsInUse(MountedTree tree)
        {
            var pass = tree.Root.Reconciler!.Context.Pass;
            var field = typeof(ReconcilePass).GetField("_anchorsInUse", BindingFlags.NonPublic | BindingFlags.Instance);
            return field == null ? -1 : ((ICollection)field.GetValue(pass)).Count;
        }
    }
}
