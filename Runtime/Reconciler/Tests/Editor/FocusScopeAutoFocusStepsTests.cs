using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Holds each step of a focus scope's autoFocus on its own, below the Suspense reveal that drives them
    /// together in <see cref="SuspenseHiddenPortalFocusTests"/>: the deferral entry a hidden scope leaves and its
    /// release, the style pass a release applies before it looks for a focusable, and the one retry a scheduler
    /// tick gives it.
    /// </summary>
    internal sealed class FocusScopeAutoFocusStepsTests
    {
        private const string HiddenSheet =
            "Packages/com.velvet.core/Runtime/Reconciler/Tests/Editor/SuspenseHiddenPortalFocus.uss";

        private HeadlessEditorPanelHost _host;
        private ReconcilerScope _scope;
        private VisualElement _target;
        private Button _inner;
        private Button _outside;

        [SetUp]
        public void SetUp()
        {
            _host = new HeadlessEditorPanelHost();
            _scope = new ReconcilerScope();
            _target = new VisualElement { name = "scope" };
            _inner = new Button { name = "inner" };
            _outside = new Button { name = "outside" };
            _target.Add(_inner);
            _host.Root.Add(_target);
            _host.Root.Add(_outside);
            EditorPanelTestHelpers.ForcePanelUpdate(_host.Panel);
        }

        [TearDown]
        public void TearDown()
        {
            SuspenseHiddenElements.Release(_target);
            _scope?.Dispose();
            _host?.Dispose();
        }

        private ReconcilerContext Context => ReconcilerContextProbe.Of(_scope);

        private Focusable Focused => _host.Panel.focusController.focusedElement;

        private FocusScopeBinding Binding(bool autoFocus) => new(new FocusScopeSettings(AutoFocus: autoFocus))
        {
            Context = Context,
            OnAttach = _ => { },
        };

        private static object Call(string method, params object[] arguments)
            => typeof(FocusScopeDriver)
                .GetMethod(method, BindingFlags.NonPublic | BindingFlags.Static)!
                .Invoke(null, arguments)!;

        // GREEN_ON_BASE(characterization): the base moves a replaced host root's chained-ring ownership to its successor.
        // Drop `ctx.ChainedHostRoots[root] = owner;` from RekeyChainedHost and the successor holds no owner.
        [Test]
        public void Given_APortalChainedHostRootOwnedByAPlaceholder_When_TheRootIsReplaced_Then_TheSuccessorHoldsTheOwner()
        {
            // Arrange
            var previousRoot = new VisualElement();
            var successor = new VisualElement();
            var owner = new VisualElement();
            Context.ChainedHostRoots[previousRoot] = owner;

            // Act
            FiberFocusNavigator.RekeyChainedHost(previousRoot, successor, Context);

            // Assert
            Assert.That(Context.ChainedHostRoots.TryGetValue(successor, out var held) ? held : null, Is.SameAs(owner));
        }

        // GREEN_ON_BASE(characterization): the base reports focus already inside a scope as taken.
        // Change `return true;` to `return false;` in the focus-inside branch of AutoFocusFirst and it reports false.
        [Test]
        public void Given_FocusAlreadyInsideTheScope_When_AutoFocusFirstRuns_Then_ItReportsTheFocusAsTaken()
        {
            // Arrange — tabIndex -1 keeps the button out of the first-focusable search, so a run that did not
            // find focus inside the scope has nothing left to report as found.
            _inner.tabIndex = -1;
            _inner.Focus();

            // Act
            var reported = (bool)Call("AutoFocusFirst", _target);

            // Assert
            Assert.IsTrue(reported);
        }

        // GREEN_ON_BASE(characterization): the base retries an after-styles autoFocus on the next tick of the scheduler when it found no focusable.
        // Drop the `scope.schedule.Execute(...)` call from TakeAutoFocus and nothing takes focus.
        [Test]
        public void Given_AnAfterStylesAttemptThatFoundNoFocusable_When_AFocusableArrivesBeforeTheNextTick_Then_TheTickFocusesIt()
        {
            // Arrange
            _target.Remove(_inner);
            Call("TakeAutoFocus", _target, Binding(autoFocus: true), true);
            _target.Add(_inner);
            EditorPanelTestHelpers.ForcePanelUpdate(_host.Panel);

            // Act
            EditorPanelTestHelpers.DriveSchedulerOnce(_host.Panel);

            // Assert
            Assert.That(Focused, Is.SameAs(_inner));
        }

        // GREEN_ON_BASE(characterization): the base schedules no retry for an attach-time autoFocus that found no focusable.
        // Drop `|| !afterStyles` from the condition in TakeAutoFocus and the attempt schedules a retry that takes
        // focus on the tick.
        [Test]
        public void Given_AnAttachTimeAttemptThatFoundNoFocusable_When_AFocusableArrivesBeforeTheNextTick_Then_NothingTakesFocus()
        {
            // Arrange
            _target.Remove(_inner);
            Call("TakeAutoFocus", _target, Binding(autoFocus: true), false);
            _target.Add(_inner);
            EditorPanelTestHelpers.ForcePanelUpdate(_host.Panel);

            // Act
            EditorPanelTestHelpers.DriveSchedulerOnce(_host.Panel);

            // Assert
            Assert.That(Focused, Is.Null);
        }

        // GREEN_ON_BASE(characterization): the base defers one entry for a hidden scope however often its autoFocus is attempted.
        // Drop the `!ctx.DeferredAutoFocus.Exists(...)` term from FireAutoFocusOnce and the second attempt adds a
        // second entry.
        [Test]
        public void Given_AHiddenScopeWhoseAutoFocusIsAttemptedTwice_When_BothAttemptsRun_Then_OneEntryIsDeferred()
        {
            // Arrange
            var binding = Binding(autoFocus: true);
            SuspenseHiddenElements.Hide(_target);

            // Act
            Call("FireAutoFocusOnce", _target, binding);
            Call("FireAutoFocusOnce", _target, binding);

            // Assert
            Assert.AreEqual(1, Context.DeferredAutoFocus.Count);
        }

        // GREEN_ON_BASE(characterization): the base applies the panel's pending styles before a released deferral looks for a focusable.
        // Drop `FiberLayoutReflow.LayOutFor(ctx, scope, null);` from TakeAutoFocus and the candidate still reads as
        // hidden, so nothing takes focus.
        [Test]
        public void Given_ADeferredScopeWhoseCandidateWaitsOnAStylePass_When_TheDeferralIsReleased_Then_TheCandidateHoldsFocus()
        {
            // Arrange — the rule hid the button at the last style pass and its class is gone since, so the
            // button reads as hidden until the release applies the styles.
            _host.Root.styleSheets.Add(AssetDatabase.LoadAssetAtPath<StyleSheet>(HiddenSheet));
            _inner.AddToClassList("portal-focus-stylesheet-hidden");
            EditorPanelTestHelpers.ForcePanelUpdate(_host.Panel);
            _inner.RemoveFromClassList("portal-focus-stylesheet-hidden");
            Context.DeferredAutoFocus.Add(new DeferredAutoFocus(_target, Binding(autoFocus: true), null));

            // Act
            FocusScopeDriver.FireDeferredAutoFocus(Context);

            // Assert
            Assert.That(Focused, Is.SameAs(_inner));
        }

        // GREEN_ON_BASE(characterization): the base removes a deferral once it is released.
        // Drop `ctx.DeferredAutoFocus.Remove(entry);` from FireDeferredAutoFocus and the next release takes focus
        // back into the scope.
        [Test]
        public void Given_AReleasedDeferral_When_FocusLeftTheScopeAndAnotherCommitReleases_Then_FocusStaysOutside()
        {
            // Arrange
            Context.DeferredAutoFocus.Add(new DeferredAutoFocus(_target, Binding(autoFocus: true), null));
            FocusScopeDriver.FireDeferredAutoFocus(Context);
            _outside.Focus();

            // Act
            FocusScopeDriver.FireDeferredAutoFocus(Context);

            // Assert
            Assert.That(Focused, Is.SameAs(_outside));
        }
    }
}
