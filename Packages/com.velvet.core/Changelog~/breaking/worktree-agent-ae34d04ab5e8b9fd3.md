### Changed

- A removed subtree is taken down in one walk in the order React's deletion takes it: a component's
  `UseInsertionEffect` and `UseLayoutEffect` cleanups run where the walk reaches the component, ahead of the
  components it rendered, and an element's `refCallback:` cleanup runs where the walk reaches the element, after the
  cleanups of the components above it and ahead of those inside it. A component's `UseEffect` cleanups run right
  after its layout cleanups, so they too precede those of the components it rendered, the order React's passive
  phase cleans a deleted subtree up in. The subtree's refs used to detach together, ahead of the cleanups of the
  components inside it that are not a `V.Portal`'s children, and a component cleaned up after the components it
  rendered directly, and after every component it rendered where it was removed with an element around it.
- On mount, an element with a `refCallback:` that a `V.Portal`'s child renders after a component attaches its ref
  after that component's layout effects, as React's layout phase reaches it; it used to attach first.
- A `V.VirtualList` row inside a `V.Portal` runs its effects ahead of a component after the portal when both
  re-render in one commit, as React's commit reaches the portal's children where the portal sits; it used to run
  them after.
