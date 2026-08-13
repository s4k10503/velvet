using System.Reflection;

namespace Velvet.TestUtilities
{
    /// <summary>
    /// Reaches a <see cref="Reconciler"/>'s <c>ReconcilerContext</c>, which is private and stays private —
    /// the side tables a test wants to read are reached from here rather than through a member added to the
    /// production type for the purpose.
    /// </summary>
    public static class ReconcilerContextProbe
    {
        private static readonly FieldInfo ContextField =
            typeof(Reconciler).GetField("_ctx", BindingFlags.NonPublic | BindingFlags.Instance)!;

        internal static ReconcilerContext Of(Reconciler reconciler)
            => (ReconcilerContext)ContextField.GetValue(reconciler)!;

        internal static ReconcilerContext Of(ReconcilerScope scope) => Of(scope.Reconciler);
    }
}
