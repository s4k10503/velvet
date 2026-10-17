### Changed

- Pending passive effects (`Hooks.UseEffect` setups and cleanups, and the `V.AnimatePresence` `onExitComplete` calls
  that wait for the passive-effect flush) now run before each render the update scheduler drains and before each
  resumed slice of a time-sliced pass, as React flushes them before it starts a render. Before, a drain rendered
  without flushing them first. The drains include the immediate tier's scheduled drain, the Transition tier's drain, and
  the follow-up pass that a layout effect or callback ref writing state asks for, on a mount as well. On the
  immediate tier, an update an effect schedules from that flush renders in the same pass, where it used to wait for
  the next drain.
  - A presence whose child left with nothing to exit, and whose key comes back before the passive flush, calls
    `onExitComplete` before the render that adds the key back; it used to be called after that render.
  - A component whose layout effect writes state on mount runs its `UseEffect` setups before the re-render that
    write asks for, as React does; before, the re-render committed first and the setups ran at the next frame.
