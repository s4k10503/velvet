using System;
using System.Reflection;
using UnityEngine.UIElements;

namespace Velvet.Tests
{
    /// <summary>
    /// The row model's readers, which no shipped code calls until a step moves a reader onto the model, reached by
    /// reflection from the cases that pin them.
    /// </summary>
    internal static class RowModelAccess
    {
        private const BindingFlags Private = BindingFlags.NonPublic | BindingFlags.Instance;

        private static readonly Func<VisualElement, ContainerRows?> PeekDelegate =
            (Func<VisualElement, ContainerRows?>)Delegate.CreateDelegate(
                typeof(Func<VisualElement, ContainerRows?>),
                typeof(ChildListWrites).GetMethod("Peek", BindingFlags.NonPublic | BindingFlags.Static)!);

        private static readonly Func<ContainerRows, int, int> ToPhysicalDelegate = Reader<int, int>("ToPhysical");
        private static readonly Func<ContainerRows, int, int> ToLogicalDelegate = Reader<int, int>("ToLogical");
        private static readonly Func<ContainerRows, VisualElement, int> PhysicalIndexOfDelegate =
            Reader<VisualElement, int>("PhysicalIndexOf");
        private static readonly Func<ContainerRows, VisualElement, int> SlotOfDelegate =
            Reader<VisualElement, int>("SlotOf");
        private static readonly MethodInfo TryGetPhysicalMethod =
            typeof(ContainerRows).GetMethod("TryGetPhysical", Private)!;

        private static Func<ContainerRows, TArg, TResult> Reader<TArg, TResult>(string name)
            => (Func<ContainerRows, TArg, TResult>)Delegate.CreateDelegate(
                typeof(Func<ContainerRows, TArg, TResult>), typeof(ContainerRows).GetMethod(name, Private)!);

        internal static ContainerRows? Peek(VisualElement container) => PeekDelegate(container);

        internal static int ToPhysical(ContainerRows rows, int logical) => ToPhysicalDelegate(rows, logical);

        internal static int ToLogical(ContainerRows rows, int physical) => ToLogicalDelegate(rows, physical);

        internal static int PhysicalIndexOf(ContainerRows rows, VisualElement element)
            => PhysicalIndexOfDelegate(rows, element);

        internal static int SlotOf(ContainerRows rows, VisualElement element) => SlotOfDelegate(rows, element);

        internal static bool TryGetPhysical(ContainerRows rows, int logical, out int physical)
        {
            var arguments = new object[] { logical, 0 };
            var found = (bool)TryGetPhysicalMethod.Invoke(rows, arguments)!;
            physical = (int)arguments[1];
            return found;
        }
    }
}
