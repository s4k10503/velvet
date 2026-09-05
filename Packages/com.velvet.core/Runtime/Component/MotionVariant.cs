#nullable enable

namespace Velvet
{
    /// <summary>
    /// One entry of a <c>V.Motion(variants:)</c> map: the utility-class string naming a pose, plus an
    /// optional transition the swap into that pose plays on. A <c>string</c> converts implicitly, so a pose
    /// that takes the Motion's own <c>transition:</c> is still written as the class string alone.
    /// </summary>
    public readonly struct MotionVariant
    {
        /// <summary>
        /// The utility-class string applied while this variant is the active pose. Merged on top of the
        /// Motion's own <c>className</c>; null or empty applies nothing. A variant enter and a variant exit
        /// each require a class on the NON-resting pose — <see cref="MotionNode.Initial"/>'s and
        /// <see cref="MotionNode.Exit"/>'s respectively, against the resting <see cref="MotionNode.Animate"/>
        /// — so an <see cref="MotionNode.Initial"/> or <see cref="MotionNode.Exit"/> pose leaving this empty
        /// is no variant swap at all: the mount enter is skipped and the element rests at
        /// <see cref="MotionNode.Animate"/>'s pose from the start, and the removal plays the classic exit.
        /// </summary>
        public string? ClassName { get; }

        /// <summary>
        /// The transition a swap INTO this variant plays on: a mount enter whose
        /// <see cref="MotionNode.Animate"/> names it, a runtime label change to it, and a removal whose
        /// <see cref="MotionNode.Exit"/> names it. The destination pose supplies it whether or not it applies
        /// a class — except at a removal, where the destination is also the non-resting pose
        /// <see cref="ClassName"/> requires a class on, so an exit pose leaving that empty is no variant exit
        /// and the classic exit plays on the Motion's own transition instead.
        /// Null falls back to the enclosing <c>V.Motion</c>'s own
        /// <c>transition:</c> — itself <see cref="StyleTransition"/>'s <c>Fade</c> preset when the call site
        /// left that out. Same delegate-outward shape as <see cref="AnimationSequenceStep.Transition"/>,
        /// without its carry-forward from the previous step: a variant map has no order to carry along.
        /// The child-orchestration knobs (<see cref="StyleTransitionConfig.StaggerChildrenSec"/>,
        /// <see cref="StyleTransitionConfig.DelayChildrenSec"/>, <see cref="StyleTransitionConfig.When"/>)
        /// are read from whichever config drives the swap, so declaring them here orchestrates the
        /// inheriting descendants of a swap into this pose.
        /// </summary>
        public StyleTransitionConfig? Transition { get; }

        /// <param name="className">See <see cref="ClassName"/>.</param>
        /// <param name="transition">See <see cref="Transition"/>.</param>
        public MotionVariant(string? className, StyleTransitionConfig? transition = null)
        {
            ClassName = className;
            Transition = transition;
        }

        /// <summary>A pose taking the Motion's own <c>transition:</c>.</summary>
        /// <param name="className">See <see cref="ClassName"/>.</param>
        public static implicit operator MotionVariant(string? className) => new(className);
    }
}
