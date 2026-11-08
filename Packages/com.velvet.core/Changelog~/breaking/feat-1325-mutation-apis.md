### Added

- A `MutationCache` on each `QueryClient`, TanStack Query's: every call a `Hooks.UseMutation` handle makes is
  a `Mutation` in it, with a `MutationState`, found through `Find` / `FindAll` / `GetAll` and `MutationFilters`
  (`MutationKey`, `Exact`, `Status`, `Predicate`), and removed `GcTime` after no handle shows it, or later
  while it is still pending. The cache's `OnMutate`, `OnSuccess`, `OnError` and `OnSettled` run for every mutation before
  the options' own, and `Subscribe` hears mutations added, updated, observed and removed. Hand one in through
  `QueryClientOptions.MutationCache`; `QueryClient.GetMutationCache` and `QueryClient.IsMutating` read it.
- `MutationKey`, `Scope`, `Meta` and `GcTime` on every mutation option record, through their new base
  `MutationOptionsBase`. Mutations sharing a `MutationScope` id run one after another; `Meta` reaches the cache
  callbacks as `Mutation.Meta`; a `MutationKey` replaced by an unequal one between commits leaves the handle
  idle.
- `Hooks.UseMutationState` and `Hooks.UseIsMutating`, TanStack Query's `useMutationState` and `useIsMutating`.
- Mutation defaults: `QueryClient.SetMutationDefaults`, `GetMutationDefaults` and `DefaultMutationOptions`, and
  `QueryClientOptions.Mutations`, filling what a mutation's options leave null, a `MutationFn` included.
- `MutationOptionsBase.ThrowOnError` (`true` or `ThrowOnErrorRule.When(...)`) throws the failure a
  `Hooks.UseMutation` handle shows to the nearest error boundary.
- `QueryClient.ResumePausedMutations` and `MutationCache.ResumePausedMutations`, and `MutationCacheEvent.Observer`.

### Changed

- `Hooks.UseMutation` reads a `QueryClient` from `QueryClientContext.Ref`, or takes one as its new second
  argument, and throws when it finds none, as `useMutation` does without a QueryClientProvider. A component
  calling it now needs a client provided above it.
- `QueryClient.Clear` empties the client's mutation cache too.
- A mutation's `MutationFn` and callbacks, its cache callbacks and its per-call `MutateOptions` callbacks are
  handed a `MutationFunctionContext` (client, meta, mutation key) last, as v5's are. `MutationFn` no longer
  takes a `CancellationToken` of its own: the token is `MutationFunctionContext.CancellationToken`.
  `Retry` moves to `MutationOptionsBase`, and `MutationFn` may be null for the client's defaults to supply.
- A call keeps the options it was built with once a newer call supersedes it, where it used to call the
  latest render's function and callbacks; a later commit's options reach only the pending call the handle
  shows. A call is built from the options the component last committed.
- A call made after the component unmounts stays in the mutation cache until it is removed, as v5 keeps its
  observer on it.
- The first attempt of a `RetryPolicy` run, a mutation's included, waits for the connection its
  `NetworkMode` needs, and a mutation's for its scope, but not for the application to be visible, as v5's
  retryer starts.
