### Changed

- A `Hooks.UseMutation` call now pauses while the device reads as offline, as TanStack Query's mutations do
  under their default `networkMode: 'online'`: a call started offline stays pending with `IsPaused` true
  and does not call `MutationFn` until a connection is back, where it used to run at once. The reading is
  `Application.internetReachability`, which reports a server on a local network as unreachable on some
  devices; set `NetworkSignals.IsOnline` once at startup to read connectivity another way, or give the
  mutation `Retry = new RetryPolicy { Retry = false, NetworkMode = NetworkMode.Always }` to never wait.

- On Windows and macOS a retry that comes due while the window is minimized (or, on macOS, the application is
  hidden) now waits until it is shown, for every `RetryPolicy`, a `RetryPolicy.RunAsync` around a `Hooks.Use`
  loader included. A window that has merely lost focus does not wait. A mutation started under the default
  `Online` mode from another thread now continues on the main thread, where the connectivity reading is taken.
