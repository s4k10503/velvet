### Changed

- `Hooks.UseSyncExternalStore` and `Hooks.UseStore` subscribe in the passive effects of the commit rather than
  during render, as React subscribes in an effect. A render that fails or is discarded subscribes and
  unsubscribes nothing, so an unsubscribe error no longer reaches `OnCaughtError` for a subscription the
  failed output made. A store change between the render and the subscription, or while `subscribe` runs,
  re-renders the component once it is subscribed; a store change after a first render but before the passive
  effects flush is no longer notified until then.
