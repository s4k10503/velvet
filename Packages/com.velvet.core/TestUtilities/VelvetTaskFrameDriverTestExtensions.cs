#if UNITY_EDITOR
using System;
using System.Reflection;

namespace Velvet.TestUtilities
{
    /// <summary>
    /// Drains one <c>VelvetTaskFrameDriver</c> editor-update pass synchronously, standing in for
    /// <c>EditorApplication.update</c> ticks that EditMode batchmode does not schedule on its own.
    /// </summary>
    internal static class VelvetTaskFrameDriverTestExtensions
    {
        const string DriverTypeName = "Velvet.VelvetTaskFrameDriver";
        const string DrainMethodName = "DrainScheduledYields";

        static readonly Action DrainEditorUpdate = CreateDrainDelegate();

        // Bypasses: both routes production drains through, EditorApplication.update's OnEditorUpdate and the PlayerLoop Update system's OnPlayerLoopUpdate, invoking DrainScheduledYields directly instead.
        internal static void DrainEditorUpdateForTest() => DrainEditorUpdate();

        static Action CreateDrainDelegate()
        {
            var driverType = Type.GetType($"{DriverTypeName}, Velvet");
            if (driverType == null)
            {
                throw new TypeLoadException(DriverTypeName);
            }

            var method = driverType.GetMethod(
                DrainMethodName,
                BindingFlags.Static | BindingFlags.NonPublic,
                null,
                Type.EmptyTypes,
                null);
            if (method == null)
            {
                throw new MissingMethodException(driverType.FullName, DrainMethodName);
            }

            return (Action)Delegate.CreateDelegate(typeof(Action), method);
        }
    }
}
#endif
