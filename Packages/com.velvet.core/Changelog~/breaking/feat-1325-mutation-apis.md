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

### Changed

- `Hooks.UseMutation` reads a `QueryClient` from `QueryClientContext.Ref`, or takes one as its new second
  argument, and throws when it finds none, as `useMutation` does without a `QueryClientProvider`. A component
  calling it now needs a client provided above it.
- `QueryClient.Clear` empties the client's mutation cache too.
