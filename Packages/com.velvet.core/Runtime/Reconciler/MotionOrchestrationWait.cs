using System;

namespace Velvet
{
    // The readings of child orchestration that follow the children: when a Motion whose transition is AfterChildren
    // starts its own play, how a variant child reports when its play ends to the frame it claimed from, and the
    // child count a reversed stagger counts back from.
    internal static class MotionOrchestrationWait
    {
        // Seconds from render-commit time at which a Motion's own play starts. One handed an AfterChildren frame of
        // its own starts when the plays below it have ended, and never before the slot it claimed. An ambient frame
        // is no answer here: it pools the ends of the Motion's siblings as well.
        public static float PlayStartSec(MotionOrchestrationFrame? ownFrame, MotionOrchestrationFrame? ambientFrame,
            float claimedDelaySec)
            => ownFrame is { WaitsForChildren: true } && !ReferenceEquals(ownFrame, ambientFrame)
                ? Math.Max(claimedDelaySec, (float)ownFrame.ChildrenEndSec)
                : claimedDelaySec;

        // When a play that starts at startSec ends, as BeforeChildren measures it: its delay and its span, and nothing
        // for a Motion that plays none.
        public static double PlayEndSec(StyleTransitionConfig? transition, float startSec, string[]? fromClasses,
            string[]? toClasses, MotionSlotContext? context)
            => transition == null ? 0.0 : startSec + MotionPlaySpan.HeldBy(transition, fromClasses, toClasses, context);

        // A variant child's report to the frame it claimed from: the end of its own play or of anything below it,
        // whichever is later. Frames that no AfterChildren Motion sits above take no report.
        public static void ReportEnd(MotionOrchestrationFrame? ambientFrame, MotionOrchestrationFrame? ownFrame,
            double ownEndSec)
        {
            if (ambientFrame is not { TracksEnds: true })
            {
                return;
            }
            var belowEndSec = ownFrame != null && !ReferenceEquals(ownFrame, ambientFrame) ? ownFrame.ChildrenEndSec : 0.0;
            ambientFrame.RecordChildEnd(Math.Max(ownEndSec, belowEndSec));
        }

        // The variant children a Motion's declared nodes hold, which a reversed stagger counts back from. A component
        // or a memo counts as one child, since its output is not known before it renders.
        public static int CountVariantChildren(VNode?[]? children)
        {
            var count = 0;
            if (children == null)
            {
                return count;
            }
            foreach (var child in children)
            {
                count += CountOf(child);
            }
            return count;
        }

        private static int CountOf(VNode? node)
            => node switch
            {
                MotionNode motion when MotionVariantResolver.IsVariantChild(motion) => 1,
                MotionNode motion when MotionVariantResolver.IsControlling(motion) => 0,
                BaseElementNode element => CountVariantChildren(element.Children),
                FragmentNode fragment => CountVariantChildren(fragment.Children),
                ContextProviderNode provider => CountVariantChildren(provider.Children),
                SuspenseNode suspense => CountVariantChildren(suspense.Children),
                AnimatePresenceNode presence => CountVariantChildren(presence.Children),
                ComponentNode or MemoNode => 1,
                _ => 0,
            };
    }
}
