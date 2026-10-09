### Changed

- A `TransitionType.Spring` play now runs and ends as Framer Motion's spring animation does. Each channel is
  sampled from Framer's spring by the time since the play started, and the play ends when its slowest channel's
  spring has run the duration Framer measures for it. An opacity, a translate, scale or rotate, and a background
  color — the values Framer hands to the browser — are measured over a travel of 100 and cut at 20 s, so an
  opacity on the default spring ends at 1.05 s. Any other channel, and any channel under a mirrored or waiting
  repeat, runs as on Framer's main thread, a length over its own travel and a color over 100, and never ends
  if it has not rested within 20 s. It ended
  before once every channel came within Velvet's own epsilons, checked on every tick. A frame that arrives late
  now moves a spring as far as the time it covers, where an integrator clamped such a frame to 1/30 s.
- Interrupting a spring — a label change mid-spring, or an exit cancelled mid-flight — releases a new spring
  from the current value and velocity, as Framer does. A label change started the new spring at rest before.
- A `layoutId` move on a spring springs its progress over Framer's travel of 1000 and ends when that spring's
  duration has run, where it ended once its edges had come to rest within 0.1 px of their layout.
