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
- The re-render `UseSyncExternalStore` or `UseStore` forces when its subscription or snapshot check finds the store
  changed takes the sync lane and renders before the passive flush that ran the check returns, the scheduled
  post-paint flush included, counting toward the drain's update-depth limit, as React renders sync-lane work before
  its passive flush returns. It used to wait for a later drain. An ordinary update the same flush schedules renders
  with it, as React's sync flush renders the default lane too; one a flush schedules without such a re-render still
  waits for the next drain.
- Under StrictMode in the Editor, `subscribe` is called, unsubscribed and called again when a component mounts, as
  React double-invokes a mount effect.
