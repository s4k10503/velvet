using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies which runs of a component body the weaver cuts out as scopes of their own and which it declines,
    /// by reading the <see cref="MemoizationReportAttribute"/> it leaves on each method.
    /// <list type="bullet">
    /// <item>Only what follows the last hook is cut, and a call that returns a reference is not a tree.</item>
    /// <item>A run is declined when it holds an instruction that cannot be skipped, sits in a loop, writes into an
    /// object it did not create, or is left by a branch.</item>
    /// <item>A statement that only builds a value the run uses belongs to the run, so a label assigned to a local and
    /// handed on whole is cached with the tree around it rather than beside it.</item>
    /// <item>A run is found where a branch table sends the walk through every arm, a filter holds the exception, and a
    /// merge hands on a value no single arm made.</item>
    /// </list>
    /// </summary>
    [TestFixture]
    internal sealed class ReactiveScopeRunShapeTests
    {
        private static string Describe(int slot, object? value) => slot + ":" + value;

        private static MemoizationReportAttribute ReportOf(string methodName)
            => ReactiveScopeTestSupport.ReportOf(typeof(ReactiveScopeRunShapeTests), methodName);

        #region Where the tail begins

        [Component]
        private static VNode BuildsANodeAheadOfALaterHook(int other)
        {
            var early = V.Label(text: Describe(0, other));
            var (second, _) = Hooks.UseState(0);
            return V.Div("early", early, V.Label(text: Describe(1, second)));
        }

        // GREEN_ON_BASE(characterization): the base cuts only what follows the last hook, so the label built ahead of it is not a scope.
        // Removing the `_tailStart = tailStart;` that the constructor of `ReactiveScopeRegions` writes starts the search
        // at the top of the body and cuts that label as well.
        [Test]
        public void Given_ALabelBuiltAheadOfALaterHook_When_Compiled_Then_OnlyTheLabelAfterTheHookIsCut()
        {
            // Act
            var report = ReportOf(nameof(BuildsANodeAheadOfALaterHook));

            // Assert
            Assert.That(report.Scopes, Is.EqualTo(1));
        }

        private static VNode s_shared = V.Text("shared");

        private static ref VNode SharedNode(int key) => ref s_shared;

        [Component]
        private static VNode ReturnsAReferenceToATree(int key)
        {
            var (first, _) = Hooks.UseState(0);
            return V.Div("by-ref",
                V.Label(text: Describe(0, first)),
                SharedNode(key));
        }

        // GREEN_ON_BASE(characterization): the base does not read a call that returns a reference to a tree as one that builds a tree.
        // Only the label is cut. Removing the `ByReferenceType` clause from `DerivesFromVNode` resolves the reference to
        // the tree it points at and cuts the call too.
        [Test]
        public void Given_ACallReturningAReferenceToATree_When_Compiled_Then_OnlyTheLabelIsCut()
        {
            // Act
            var report = ReportOf(nameof(ReturnsAReferenceToATree));

            // Assert
            Assert.That(report.Scopes, Is.EqualTo(1));
        }

        #endregion

        #region A run that is declined

        [Component]
        private static VNode ThrowsWhenTheTextIsMissing(string? text)
        {
            var (first, _) = Hooks.UseState(0);
            return V.Div("throws",
                V.Label(text: Describe(0, first)),
                V.Label(text: text ?? throw new InvalidOperationException()));
        }

        // GREEN_ON_BASE(characterization): the base declines a run holding a throw, which a cache hit would skip.
        // Reading `!=` as `==` where `CheckShape` tests whether `ShapeProblem` found one stops the loop at the first
        // instruction that has none, and removing the `return true` that `ForbiddenOpcode` gives `throw` lets the
        // run through. Either leaves the label cut and reddens it.
        [Test]
        public void Given_ALabelWhoseTextThrowsWhenMissing_When_Compiled_Then_TheReportSaysItCannotBeSkipped()
        {
            // Act
            var report = ReportOf(nameof(ThrowsWhenTheTextIsMissing));

            // Assert
            Assert.That(report.Bailouts.Any(note => note.Contains("it contains an instruction that cannot be skipped")), Is.True);
        }

        [Component]
        private static VNode BuildsALabelPerIteration(int count)
        {
            var (first, _) = Hooks.UseState(0);
            var labels = new List<VNode?>();
            for (var i = 0; i < count; i++)
            {
                labels.Add(V.Label(text: Describe(0, i)));
            }
            labels.Add(V.Label(text: Describe(1, first)));
            return V.Div("loop", children: labels.ToArray());
        }

        // GREEN_ON_BASE(characterization): the base declines a label built in the body of a for loop, whose one slot would serve every iteration.
        // Removing the `_loops.Add` that `Deliver` makes for a backward branch with nothing on the stack, or reading `!=`
        // as `==` where it tests for that, loses the loop and cuts the label.
        [Test]
        public void Given_ALabelBuiltInAForLoop_When_Compiled_Then_TheReportSaysItSitsInsideALoop()
        {
            // Act
            var report = ReportOf(nameof(BuildsALabelPerIteration));

            // Assert
            Assert.That(report.Bailouts.Any(note => note.Contains("it sits inside a loop")), Is.True);
        }

        private static VNode Fresh() => V.Text("fresh");

        [Component]
        private static VNode CallsAParameterlessFactoryAsALoopBody(int count)
        {
            var (first, _) = Hooks.UseState(0);
            for (var i = 0; i < count; i++) Fresh();
            return V.Div("loop-head", V.Label(text: Describe(0, first)), V.Label(text: Describe(1, count)));
        }

        // GREEN_ON_BASE(characterization): the base declines a call whose own instruction is where the loop begins.
        // The call has no operands, so the loop head and the end of the run are one instruction. Reading `head <= end`
        // as `head < end` in `CheckBranches` finds that run outside the loop.
        [Test]
        public void Given_ACallWithNoOperandsThatBeginsAForLoopBody_When_Compiled_Then_TheReportSaysItSitsInsideALoop()
        {
            // Act
            var report = ReportOf(nameof(CallsAParameterlessFactoryAsALoopBody));

            // Assert
            Assert.That(report.Bailouts.Any(note => note.Contains("ReactiveScopeRunShapeTests.Fresh") && note.Contains("it sits inside a loop")),
                Is.True);
        }

        private sealed class Slot
        {
            public string? Text;
        }

        [Component]
        private static VNode AssignsThroughAConditionalExpression(bool pick, Slot shared)
        {
            var (first, _) = Hooks.UseState(0);
            return V.Div("cond-write",
                V.Label(text: Describe(0, first)),
                V.Label(text: (pick ? shared : new Slot()).Text = "t"));
        }

        // GREEN_ON_BASE(characterization): the base declines a label that assigns through an object only one arm of a conditional made.
        // The merged value is not read as one the run made. Removing the `Before[target] = merged;` of `Join` leaves
        // the value the first arm pushed, which the run did make, and accepts the label.
        [Test]
        public void Given_AFieldAssignedThroughAConditionalOfAFreshAndAnOutsideObject_When_Compiled_Then_TheReportSaysItWritesIntoAnObjectItDidNotCreate()
        {
            // Act
            var report = ReportOf(nameof(AssignsThroughAConditionalExpression));

            // Assert
            Assert.That(report.Bailouts.Any(note => note.Contains("it writes into an object it did not create")), Is.True);
        }

        [Component]
        private static VNode ReadsALocalAssignedByBothArmsOfAnIfElse(bool flag)
        {
            var (first, _) = Hooks.UseState(0);
            Describe(8, first);
            int chosen;
            if (flag)
            {
                chosen = 1;
            }
            else
            {
                chosen = 2;
            }
            var label = V.Label(text: Describe(0, chosen));
            return V.Div("if-else", label);
        }

        // GREEN_ON_BASE(characterization): the base keeps the assignment in the else arm out of the label, because a branch from the then arm lands past it.
        // The label then depends on the local, and the tree around it on the label. Reading `!=` as `==` where
        // `CheckBranches` tests whether `BranchProblem` found a problem returns at the first instruction, which has
        // none, and takes that assignment into the label, which would then skip it on a hit and keep the then arm's value.
        // In a build without optimization, reading `!=` as `==` or `true` as `false` in `IsOnlyNops` reddens it too.
        [Test]
        public void Given_ALocalAssignedByBothArmsOfAnIfElseAndReadByALabel_When_Compiled_Then_TheLabelIsCachedBesideTheTreeAroundIt()
        {
            // Act
            var report = ReportOf(nameof(ReadsALocalAssignedByBothArmsOfAnIfElse));

            // Assert
            Assert.That((report.BodyGate, report.Scopes), Is.EqualTo((true, 1)));
        }

        [Component]
        private static VNode MountsALabelStoredInALocalIntoAPortal()
        {
            var (first, _) = Hooks.UseState(0);
            var child = V.Label(text: Describe(0, first));
            return V.Portal("target", new VNode?[] { child });
        }

        // GREEN_ON_BASE(characterization): the base declines a label that is stored in a local the portal's children are read from.
        // Reading `end + 1` as `end - 1` in `CheckHost` looks for the store at the wrong instruction and finds none.
        [Test]
        public void Given_ALabelStoredInALocalThatBecomesAPortalChild_When_Compiled_Then_TheReportSaysItIsAChildOfAPortal()
        {
            // Act
            var report = ReportOf(nameof(MountsALabelStoredInALocalIntoAPortal));

            // Assert
            Assert.That(report.Bailouts.Any(note => note.Contains("it is a child of a portal or world-space node")), Is.True);
        }

        #endregion

        #region Control flow the walk has to follow

        [Component]
        private static VNode ChoosesALabelByASwitch(int mode, int other)
        {
            var (first, _) = Hooks.UseState(0);
            VNode content;
            switch (mode)
            {
                case 0:
                    content = V.Label(text: Describe(0, first));
                    break;
                case 1:
                    content = V.Label(text: Describe(1, first + 1));
                    break;
                case 2:
                    content = V.Label(text: Describe(2, first + 2));
                    break;
                default:
                    content = V.Label(text: "default");
                    break;
            }
            return V.Div("switch", content, V.Label(text: Describe(3, other)));
        }

        // GREEN_ON_BASE(characterization): the base reaches the arms a branch table sends the walk to, and cuts each label built in one.
        // Removing the `Deliver` of the targets of a `switch` in `ReactiveScopeFlow.Send` leaves the arms unreached
        // and drops their four labels from the scopes.
        [Test]
        public void Given_ALabelBuiltInEachArmOfASwitch_When_Compiled_Then_EveryLabelIsCut()
        {
            // Act
            var report = ReportOf(nameof(ChoosesALabelByASwitch));

            // Assert
            Assert.That(report.Scopes, Is.EqualTo(5));
        }

        [Component]
        private static VNode CatchesOnlyWhenAFlagIsSet(bool flag)
        {
            var (first, _) = Hooks.UseState(0);
            var (second, _) = Hooks.UseState(0);
            try
            {
                Describe(9, first);
            }
            catch (Exception) when (flag)
            {
            }
            return V.Div("filter",
                V.Label(text: Describe(0, first)),
                V.Label(text: Describe(1, second)));
        }

        // GREEN_ON_BASE(characterization): the base follows a handler with a filter, and cuts both labels after it.
        // Reading `==` as `!=` where `SeedHandlers` decides whether a handler is entered with the exception on the stack
        // leaves the report with no scope.
        [Test]
        public void Given_ATryWithAFilteredCatchAheadOfTheTail_When_Compiled_Then_BothLabelsAreCut()
        {
            // Act
            var report = ReportOf(nameof(CatchesOnlyWhenAFlagIsSet));

            // Assert
            Assert.That((report.BodyGate, report.Scopes), Is.EqualTo((true, 2)));
        }

        #endregion

        #region A statement that belongs to the run

        [Component]
        private static VNode ReturnsALabelAssignedToALocal()
        {
            var (first, _) = Hooks.UseState(0);
            Describe(8, first);
            var label = V.Label(text: Describe(0, first));
            return V.Div("assigned", label);
        }

        // GREEN_ON_BASE(characterization): the base lets the tree around a label take in the statement that assigns the label to a local.
        // The two then depend on the same value, so only the tree is cached and the label is not cached beside it.
        // Reading `<=` as `<` where `LocalsIn` walks a statement leaves out its store, reading `writes: true` as
        // `false` counts what the statement only reads, and reading `!=` as `==` in `IsOnlyNops` takes in a statement
        // that is no nop; each keeps the statement out of the tree and caches the label too.
        [Test]
        public void Given_ALabelAssignedToALocalAndHandedOnWhole_When_Compiled_Then_OnlyTheTreeAroundItIsCached()
        {
            // Act
            var report = ReportOf(nameof(ReturnsALabelAssignedToALocal));

            // Assert
            Assert.That((report.BodyGate, report.Scopes), Is.EqualTo((true, 0)));
        }

        [Component]
        private static VNode ReturnsALabelAssignedAfterATry()
        {
            var (first, _) = Hooks.UseState(0);
            try
            {
                Describe(9, first);
            }
            catch (Exception)
            {
                Describe(8, first);
            }
            var label = V.Label(text: Describe(0, first));
            return V.Div("after-try", label);
        }

        // GREEN_ON_BASE(characterization): the base stops taking statements into the tree at the branch that ends a catch block.
        // The label is not cached. Reading `<=` as `<` where `IsOnlyNops` walks a range reads that one-instruction
        // range as holding nothing but nops, and the report then counts one scope.
        [Test]
        public void Given_ALabelAssignedAfterATryStatement_When_Compiled_Then_OnlyTheTreeAroundItIsCached()
        {
            // Act
            var report = ReportOf(nameof(ReturnsALabelAssignedAfterATry));

            // Assert
            Assert.That((report.BodyGate, report.Scopes), Is.EqualTo((true, 0)));
        }

        #endregion

        #region What the declarations over the walk rest on

        // GREEN_ON_BASE(characterization): the base has no component body in this assembly that reaches a `leave` with a value on the stack.
        // The `MUTANT_SURVIVES` over `ForbiddenOpcode`'s test for `leave` rests on it. No cut of the weaver reddens it;
        // a compiler that wrote such a `leave` into a component body does.
        [Test]
        public void Given_EveryComponentBodyThisAssemblyHolds_When_TheFlowWalkReadsIt_Then_NoLeaveHasAValueUnderIt()
        {
            // Arrange
            var codeGen = AppDomain.CurrentDomain.GetAssemblies()
                .FirstOrDefault(assembly => assembly.GetName().Name == "Unity.Velvet.CodeGen");
            Assume.That(codeGen, Is.Not.Null,
                "Precondition: the Unity.Velvet.CodeGen assembly is loaded in the editor domain");
            var flowType = codeGen!.GetType("Velvet.CodeGen.ReactiveScopeFlow", throwOnError: true)!;
            using var assembly = ReactiveScopeTestSupport.ReadWoven(typeof(ReactiveScopeRunShapeTests));
            var offenders = new List<string>();
            var followed = 0;

            // Act
            foreach (var method in assembly.MainModule.GetTypes().SelectMany(type => type.Methods)
                .Where(method => method.HasBody && method.CustomAttributes.Any(attribute => attribute.AttributeType.FullName == typeof(ComponentAttribute).FullName)))
            {
                var flow = flowType.GetMethod("Run", BindingFlags.Public | BindingFlags.Static)!.Invoke(null, new object[] { method.Body })!;
                if ((bool)flowType.GetProperty("Failed")!.GetValue(flow)!) continue;
                followed++;
                var before = (Array)flowType.GetProperty("Before")!.GetValue(flow)!;
                var instructions = method.Body.Instructions;
                for (var i = 0; i < instructions.Count; i++)
                {
                    if (!instructions[i].OpCode.Name.StartsWith("leave", StringComparison.Ordinal)) continue;
                    if (before.GetValue(i) is Array { Length: > 0 }) offenders.Add(method.FullName + ": instruction " + i);
                }
            }

            // Assert
            Assert.That($"{followed > 0}|{string.Join(", ", offenders)}", Is.EqualTo("True|"));
        }

        #endregion
    }
}
