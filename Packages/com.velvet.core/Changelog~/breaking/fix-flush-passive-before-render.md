### Changed

- Pending passive effects (`Hooks.UseEffect` setups and cleanups, and the `V.AnimatePresence` `onExitComplete` calls
  that wait for the passive-effect flush) now run before each render the update scheduler drains, before each
  resumed slice of a time-sliced pass, and before a `V.VirtualList` renders a range as it scrolls or is resized, as
  React flushes them before it starts a render. Before, a drain rendered without flushing them first. The drains
  include the immediate tier's scheduled drain, the Transition tier's drain, and the follow-up pass that a layout
  effect or callback ref writing state asks for, on a mount as well. On the immediate tier, an update an effect
  schedules from that flush renders in the same pass, where it used to wait for the next drain.
  - A presence whose child left with nothing to exit, and whose key comes back before the passive flush, calls
    `onExitComplete` before the render that adds the key back; it used to be called after that render.
  - A component whose layout effect writes state on mount runs its `UseEffect` setups before the re-render that
    write asks for, as React does; before, the re-render committed first and the setups ran at the next frame.
- The passive effects of a commit React gives the sync lane now run as that commit ends instead of at the next
  frame: the commit of a discrete event (a click, a value change, a drag), the follow-up commit of a layout effect or
  callback ref writing state, and the render of a `Hooks.UseSyncExternalStore` change. An update one of those
  effects schedules waits for the next drain. A drain of ordinary updates still leaves its effects to the next frame.
- An update that a passive effect schedules while the Transition tier's drain, or the resume of a parked transition
  pass, flushes renders before that transition, as React renders the higher-priority lane first. It used to render
  after the transition.
- A passive-effect flush reached from inside a layout commit or a running passive drain, such as a click an effect
  dispatches, no longer runs the pending effects there, as React refuses to flush passive effects inside a commit.
  They wait for the passive drain under way to finish, or, from a layout commit, for the next flush.
- Pending passive effects stay per root, a deviation from React: React flushes every root's pending effects before
  any root renders, while a render of one `V.Mount` root flushes only its own.
