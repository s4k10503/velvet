#nullable enable
namespace Velvet
{
    // Type-erased abstraction over Ref<T>.
    // Allows ComponentNode to accept the ref passed by the parent and store the handle produced
    // by the child's UseImperativeHandle without knowing the concrete THandle.
    // internal: consumers only need to pass a Ref<T> to V.Component<TRef>(componentRef:).
    // Code that directly implements or accepts IHookRefSetter exists only inside the Velvet core.
    internal interface IHookRefSetter
    {
        void Set(object? value);

        // The held value when it is a VNode root the recycle sweep must spare (element-in-ref
        // caching) — see HookSlotRecycleProbe.
        object? RecycleMarkRoot { get; }
    }
}
