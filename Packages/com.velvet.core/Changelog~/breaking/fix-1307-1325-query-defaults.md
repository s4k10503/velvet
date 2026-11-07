### Changed

- A `Hooks.UseQuery` request now pauses while the device reads as offline, as TanStack Query's queries do
  under their default `networkMode: 'online'`: a query mounted offline does not call its query function,
  reports `FetchStatus` `Paused` with `IsPaused` true and `IsFetching` false, and fetches once a connection
  is back, where it used to call the function at once and fail or hang. The reading is
  `Application.internetReachability`, which reports a server on a local network as unreachable on some
  devices; set `NetworkSignals.IsOnline` once at startup to read connectivity another way, or give a query
  (`QueryOptions.NetworkMode`) or every query (`QueryClientOptions.NetworkMode`) `NetworkMode.Always` to never
  wait, or `OfflineFirst` to make the first request whatever the connection.
- A query's retry now waits until the application is visible, and outside `NetworkMode.Always` until the
  device is online, as TanStack Query's retryer does. `QueryResult.IsFetching`, `QueryInfo.IsFetching` and
  `QueryState<T>.IsFetching` are false while a request is paused, and `QueryFilters.FetchStatus` `Fetching`
  does not select a paused entry; an entry with a paused request is not collected.
- A `QueryKey` part that is a tuple, a record or an anonymous type now compares by its members, each compared
  as a part is, as TanStack Query's `hashKey` serialises an object whole: a key holding a record that holds a
  list, an array or a dictionary is equal to another key holding an equal record, where it used to be equal
  only when the collection was the same instance, and keys holding the same instance are still equal. A record
  that declares its own `Equals`, and a class that declares none, compare as before. A filter key leads an entry's record the
  way it leads an array. `QueryKey.ToString` prints such a part's members rather than the part's own
  `ToString`.
