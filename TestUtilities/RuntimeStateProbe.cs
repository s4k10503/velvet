using System;
using System.Reflection;

namespace Velvet.TestUtilities
{
    /// <summary>
    /// Reaches runtime state a test needs and production does not, so no seam for it is declared under
    /// <c>Runtime/</c>. A member kept there for a test is one production code can start leaning on, at
    /// which point removing it stops being a refactor — which is why
    /// <c>TestOnlyMemberConventionTests</c> fails on one.
    /// </summary>
    public static class RuntimeStateProbe
    {
        private const BindingFlags AnyInstance =
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        private const BindingFlags AnyStatic =
            BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

        /// <summary>Cancels an async resource's token without releasing its source, as a consumer's own
        /// cancellation does.</summary>
        /// <remarks>
        /// Production reaches this only through Dispose, which cancels, releases the source and clears the
        /// completion callback together. Cancelling alone is the state a test needs to observe and the one
        /// a production caller must not be able to reach, since it leaks the source.
        /// </remarks>
        public static void CancelAsyncResource(object resource)
        {
            if (resource == null) throw new ArgumentNullException(nameof(resource));
            var field = Field(resource.GetType(), "_cts", AnyInstance);
            var source = field.GetValue(resource);
            var requested = (bool)Property(source.GetType(), "IsCancellationRequested").GetValue(source);
            if (!requested)
            {
                Method(source.GetType(), "Cancel", Type.EmptyTypes).Invoke(source, null);
            }
        }

        /// <summary>Empties the portal registry, which persists across mounts within a domain.</summary>
        public static void ClearPortalRegistry()
        {
            var registry = typeof(V).Assembly.GetType("Velvet.FiberPortalRegistry", throwOnError: true);
            var targets = Field(registry, "_targets", AnyStatic).GetValue(null);
            Method(targets.GetType(), "Clear", Type.EmptyTypes).Invoke(targets, null);
        }

        // Each lookup reports the name it could not find rather than a NullReferenceException one frame
        // later, because a rename here fails in a fixture's setup where the cause is least visible.
        private static FieldInfo Field(Type type, string name, BindingFlags flags) =>
            type.GetField(name, flags) ?? throw new MissingMemberException(type.FullName, name);

        private static PropertyInfo Property(Type type, string name) =>
            type.GetProperty(name, AnyInstance) ?? throw new MissingMemberException(type.FullName, name);

        private static MethodInfo Method(Type type, string name, Type[] parameters) =>
            type.GetMethod(name, AnyInstance | BindingFlags.Static, null, parameters, null)
            ?? throw new MissingMemberException(type.FullName, name);
    }
}
