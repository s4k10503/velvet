### Changed

- A commit attaches each `refCallback:` as it reaches the element in the walk React's layout phase makes,
  rather than all of them ahead of its first layout effect. An element's ref attaches after everything inside
  it and after the layout effects of the components below it, and a component's layout effects run after the
  refs of the elements it rendered. A `Hooks.UseLayoutEffect` on mount therefore no longer finds an ancestor
  element's ref set, nor a later sibling's, where it used to find both; it still finds the refs of its own
  component's elements, of its descendants' and of its earlier siblings'. A layout effect that needs an
  element above it reads it from an effect (`Hooks.UseEffect`) or from a layout effect of the component that
  rendered the element.
- The layout cleanups of a commit run after the refs it replaces detach and before the new ones attach, where
  the new refs used to attach first.
- The refs of every component a batched drain re-renders attach at the end of the drain, interleaved with the
  drain's layout effects, where each component's refs used to attach as its own pass ended. A render that reads
  a `Ref<T>` another component in the same drain sets now finds the previous commit's value, as a render does in
  React.
- The refs of a `V.Portal`'s elements attach where the portal sits among its neighbours, not after every ref
  of the pass that rendered it.
