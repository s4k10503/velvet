### Changed

- A Motion transition's `EaseIn`, `EaseOut` and `EaseInOut` now run on Framer Motion's `easeIn`, `easeOut` and
  `easeInOut`, `cubic-bezier(0.42, 0, 1, 1)`, `cubic-bezier(0, 0, 0.58, 1)` and `cubic-bezier(0.42, 0, 0.58, 1)`,
  where they ran on UI Toolkit's quadratics, up to 0.069 of the way apart for the first two. `EaseInBack`,
  `EaseOutBack` and `EaseInOutBack` run on Framer Motion's `backIn`, `backOut` and `backInOut` — `backOut` being
  `cubic-bezier(0.33, 1.53, 0.69, 0.99)`, `backIn` its reverse and `backInOut` the mirror of `backIn` — where they ran
  on UI Toolkit's own back curves, up to 0.024 of the way apart. That covers a `Tween` UI Toolkit's own transition
  plays, a driven `Tween`, a `PropertyOverrides` entry, a `layoutId` move, `UseAnimate`, a timeline's step, the
  ease-out a transition naming none takes, and the transitions Velvet runs in the engine's place while a Motion's
  tween holds the element's timing; `EasingMode.EaseOut` is the default `StyleTransitionConfig.Easing`, so a Motion
  that names no easing is retimed too.
- `UseAnimate`'s single call eases `Opacity` keyframes on a `Tween` or a `Bezier` along one easing over the whole
  duration, as Framer Motion runs opacity through the Web Animations API, where it eased each pair of keyframes;
  three keyframes with no transition now run 0.8 s on one ease-out. A `RepeatDelaySec` or a `Mirror` `RepeatType`
  keeps the pair-by-pair easing, as either keeps Framer on its keyframes generator. `X`, `Y`, `Scale` and
  `RotateDeg`, and every value of a sequence segment, still ease pair by pair.
