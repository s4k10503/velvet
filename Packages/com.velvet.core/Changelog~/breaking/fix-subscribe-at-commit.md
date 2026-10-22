### Changed

- `Hooks.UseSyncExternalStore` and `Hooks.UseStore` subscribe in the passive effects of the commit rather than
  during render, as React subscribes in an effect. A render that throws or suspends, and an attempt a render-phase
  update re-runs, subscribe and unsubscribe nothing, so an unsubscribe error no longer reaches `OnCaughtError` for
  a subscription such a render made. A store change between the render and the subscription re-renders the
  component once it is subscribed; a store change after a first render but before the passive effects flush is no
  longer notified until then.
- What a render reads through either hook reaches a store-change notification and the passive effects only once
  that render settles, so a later render of the component that does not settle no longer changes the store,
  `subscribe` or selector an earlier commit's pending effects use.
- A `UseSyncExternalStore` re-render that a passive effect asks for as a sync-lane commit ends renders in that same
  drain and counts toward its update-depth limit, as React renders a sync-lane update before it yields. It used to
  wait for the next drain.
- Under StrictMode in the Editor, `subscribe` is called, unsubscribed and called again when a component mounts, as
  React double-invokes a mount effect.
