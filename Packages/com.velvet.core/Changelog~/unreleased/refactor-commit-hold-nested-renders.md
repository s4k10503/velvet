### Changed

- `V.Mount` called while a pass of another root records or applies its writes, from an `onCreated` callback for
  example, returns its handle at once and renders the root once the commit that follows that pass has ended. A discrete
  event's handler fired inside such a pass no longer renders its own root inside the pass: the root's pending passive
  effects, its flush and the first render of a mounted root run once that commit has ended, in that order, with its
  layout effects and ref setups behind them.
