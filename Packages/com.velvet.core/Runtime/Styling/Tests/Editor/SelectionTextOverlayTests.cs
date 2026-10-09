using System;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies the drawing that shows a text field's selected text above the engine's selection highlight:
    /// the rich text it composes, when it exists and shows, that it follows the selection, and that a tick
    /// with nothing to follow allocates nothing. What the composed text looks like once drawn is not read back
    /// here.
    /// </summary>
    internal sealed class SelectionTextOverlayTests
    {
        private HeadlessEditorPanelHost _host;
        private MountedTree _mounted;

        private static StateUpdater<bool> s_setDeclared;
        private static StateUpdater<int> s_setStep;

        [SetUp]
        public void SetUp()
        {
            _host = new HeadlessEditorPanelHost();
            VelvetStyleUtilities.AttachTo(_host.Root);
            s_setDeclared = default;
            s_setStep = default;
        }

        private void Settle()
        {
            _mounted.FlushStateForTest();
            EditorPanelTestHelpers.ForcePanelUpdate(_host.Panel);
        }

        private void RenderStep(int step)
        {
            s_setStep.Invoke(step);
            Settle();
        }

        private static void SendGeometryChanged(VisualElement element)
        {
            using var changed = GeometryChangedEvent.GetPooled(Rect.zero, element.layout);
            changed.target = element;
            element.SendEvent(changed);
        }

        [TearDown]
        public void TearDown()
        {
            _mounted?.Dispose();
            _mounted = null;
            _host?.Dispose();
            _host = null;
        }

        private TextField Mount(Func<VNode> body)
        {
            _mounted = V.Mount(_host.Root, V.Component(body, key: "root"));
            EditorPanelTestHelpers.ForcePanelUpdate(_host.Panel);
            return _host.Root.Q<TextField>("field");
        }

        private static TextElement Input(TextField field) => (TextElement)field.textEdition;

        private static TextElement Drawing(TextField field) => field.Q<TextElement>(className: SelectionTextOverlay.ClassName);

        private static bool Showing(TextField field)
            => Drawing(field) is { } drawing && drawing.style.display != DisplayStyle.None;

        private void Select(TextField field, int start, int end)
        {
            Input(field).Focus();
            field.textSelection.SelectRange(start, end);
            EditorPanelTestHelpers.DriveSchedulerOnce(_host.Panel);
        }

        // One panel scheduler tick as a delegate resolved once, so the reflection that finds it is not
        // charged to the window that measures the tick.
        private static Action SchedulerTick(IPanel panel)
        {
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            PropertyInfo property = null;
            for (var type = panel.GetType(); type != null && property == null; type = type.BaseType)
            {
                property = type.GetProperty("scheduler", flags | BindingFlags.DeclaredOnly);
            }

            var scheduler = property!.GetValue(panel);
            var update = scheduler.GetType().GetMethod("UpdateScheduledEvents", flags);
            return (Action)Delegate.CreateDelegate(typeof(Action), scheduler, update!);
        }

        [Component]
        private static VNode TextColorHost()
        {
            var (declared, setDeclared) = Hooks.UseState(true);
            s_setDeclared = setDeclared;
            return V.TextField(name: "field", value: "hello", className: declared ? "selection:text-white" : null);
        }

        // Step 0 colours the selected text white, 1 black, 2 drops the utility and 3 brings it back.
        [Component]
        private static VNode SteppingHost()
        {
            var (step, setStep) = Hooks.UseState(0);
            s_setStep = setStep;
            var className = step switch { 0 => "selection:text-white", 1 => "selection:text-black", 2 => null, _ => "selection:text-white" };
            return V.TextField(name: "field", value: "hello", className: className);
        }

        // The overlay's scheduled poll, reached through the record StyleTextInputColors keeps per control.
        private static IVisualElementScheduledItem Poll(TextField field)
        {
            var states = typeof(StyleTextInputColors).GetField("s_states", BindingFlags.NonPublic | BindingFlags.Static)!
                .GetValue(null);
            var arguments = new object[] { field, null };
            states.GetType().GetMethod("TryGetValue")!.Invoke(states, arguments);
            var overlay = arguments[1].GetType().GetField("Overlay")!.GetValue(arguments[1]);
            return (IVisualElementScheduledItem)typeof(SelectionTextOverlay)
                .GetField("_poll", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(overlay);
        }

        [Component]
        private static VNode TranslucentTextHost() =>
            V.TextField(name: "field", value: "hello", className: "selection:bg-red-500 text-black/50");

        [Component]
        private static VNode FieldAndOtherHost() => V.Div(children: new VNode[]
        {
            V.TextField(name: "field", value: "hello", className: "selection:text-white"),
            V.TextField(name: "other"),
        });

        [Test]
        public void Given_ARunOutsideTheText_When_Composed_Then_ItIsClampedToTheText()
        {
            // Arrange
            var shown = "abc";

            // Act
            var composed = SelectionTextOverlay.Compose(shown, -2, 9, null, 1f);

            // Assert
            Assert.That(composed, Is.EqualTo("<alpha=#00><alpha=#FF>abc<alpha=#00>"));
        }

        [Test]
        public void Given_ASelectionUtility_When_TheFieldGainsAndLosesFocus_Then_ItsPollRunsOnlyWhileFocused()
        {
            // Arrange
            var field = Mount(FieldAndOtherHost);
            var beforeFocus = Poll(field).isActive;

            // Act
            Input(field).Focus();
            var whileFocused = Poll(field).isActive;
            Input(_host.Root.Q<TextField>("other")).Focus();

            // Assert
            Assert.That((beforeFocus, whileFocused, Poll(field).isActive), Is.EqualTo((false, true, false)));
        }

        [Test]
        public void Given_ASelectionUtilityOnAFieldNeverFocused_When_ItMounts_Then_ItCarriesNoDrawing()
        {
            // Arrange / Act
            var field = Mount(TextColorHost);

            // Assert
            Assert.That(Drawing(field), Is.Null);
        }

        [Test]
        public void Given_AFocusedFieldWithASelectionUtility_When_ItsSelectionIsEmptied_Then_NoDrawingShows()
        {
            // Arrange
            var field = Mount(TextColorHost);
            Select(field, 1, 3);
            var whileSelected = Showing(field);

            // Act
            field.textSelection.SelectNone();
            EditorPanelTestHelpers.DriveSchedulerOnce(_host.Panel);

            // Assert
            Assert.That((whileSelected, Showing(field)), Is.EqualTo((true, false)));
        }

        [Test]
        public void Given_AFieldShowingASelection_When_FocusLeavesIt_Then_NoDrawingShows()
        {
            // Arrange
            var field = Mount(FieldAndOtherHost);
            Select(field, 1, 3);
            var whileFocused = Showing(field);

            // Act
            Input(_host.Root.Q<TextField>("other")).Focus();

            // Assert
            Assert.That((whileFocused, Showing(field)), Is.EqualTo((true, false)));
        }

        [Test]
        public void Given_ASelection_When_TheDrawingIsBuilt_Then_ItTakesNoInputAndLiesOverTheInput()
        {
            // Arrange
            var field = Mount(TextColorHost);

            // Act
            Select(field, 1, 3);
            var drawing = Drawing(field);

            // Assert — every inline value below is one the drawing sets; an unset one reads as the Null keyword.
            Assert.That(
                (drawing.pickingMode, drawing.style.position.value,
                    drawing.style.marginLeft.keyword, drawing.style.paddingLeft.keyword, drawing.style.left.keyword,
                    drawing.style.top.keyword, drawing.style.width.keyword, drawing.style.height.keyword),
                Is.EqualTo((PickingMode.Ignore, Position.Absolute, StyleKeyword.Undefined,
                    StyleKeyword.Undefined, StyleKeyword.Undefined, StyleKeyword.Undefined, StyleKeyword.Undefined,
                    StyleKeyword.Undefined)));
        }

        [Test]
        public void Given_ADrawing_When_TheInputsGeometryChanges_Then_ItTakesTheInputsContentWidthAgain()
        {
            // Arrange
            var field = Mount(TextColorHost);
            Select(field, 1, 3);
            var drawing = Drawing(field);
            drawing.style.width = 123f;

            // Act
            SendGeometryChanged(Input(field));

            // Assert
            Assert.That(drawing.style.width.value.value, Is.EqualTo(Input(field).contentRect.width));
        }

        [Test]
        public void Given_ADrawingTheUtilityLeft_When_TheInputsGeometryChanges_Then_ItNoLongerFollows()
        {
            // Arrange
            var field = Mount(SteppingHost);
            Select(field, 1, 3);
            var drawing = Drawing(field);
            RenderStep(2);
            drawing.style.width = 123f;

            // Act
            SendGeometryChanged(Input(field));

            // Assert
            Assert.That(drawing.style.width.value.value, Is.EqualTo(123f));
        }

        [Test]
        public void Given_ASelection_When_ARenderChangesTheTextColor_Then_TheDrawingTakesItWithoutATick()
        {
            // Arrange
            var field = Mount(SteppingHost);
            Select(field, 1, 3);

            // Act
            RenderStep(1);

            // Assert
            Assert.That(Drawing(field).text, Is.EqualTo(SelectionTextOverlay.Compose("hello", 1, 3, Color.black, 1f)));
        }

        [Test]
        public void Given_ASelectionTextUtilityDroppedAndBroughtBack_When_TextIsSelected_Then_TheFieldShowsADrawingAgain()
        {
            // Arrange
            var field = Mount(SteppingHost);
            RenderStep(2);
            RenderStep(3);

            // Act
            Select(field, 1, 3);

            // Assert
            Assert.That(Showing(field), Is.True);
        }

        [Test]
        public void Given_ARunAndATextColor_When_Composed_Then_OnlyTheRunIsVisibleInThatColor()
        {
            // Arrange
            var shown = "hello";

            // Act
            var composed = SelectionTextOverlay.Compose(shown, 1, 3, Color.white, 1f);

            // Assert
            Assert.That(composed, Is.EqualTo("<alpha=#00>h<color=#FFFFFFFF>el</color><alpha=#00>lo"));
        }

        [Test]
        public void Given_NoTextColorAndATranslucentBase_When_Composed_Then_TheRunKeepsTheBaseAlpha()
        {
            // Arrange
            var shown = "hello";

            // Act
            var composed = SelectionTextOverlay.Compose(shown, 0, 2, null, 0.5f);

            // Assert
            Assert.That(composed, Is.EqualTo("<alpha=#00><alpha=#80>he<alpha=#00>llo"));
        }

        [Test]
        public void Given_NoSelectionTextUtilityAndATranslucentTextColor_When_TextIsSelected_Then_TheDrawingKeepsItsAlpha()
        {
            // Arrange
            var field = Mount(TranslucentTextHost);

            // Act
            Select(field, 1, 3);

            // Assert
            Assert.That(Drawing(field).text, Is.EqualTo(SelectionTextOverlay.Compose("hello", 1, 3, null, 0.5f)));
        }

        [Test]
        public void Given_TextHoldingATagOpener_When_Composed_Then_TheOpenerIsEscaped()
        {
            // Arrange
            var shown = "a<b";

            // Act
            var composed = SelectionTextOverlay.Compose(shown, 0, 3, null, 1f);

            // Assert
            Assert.That(composed, Is.EqualTo("<alpha=#00><alpha=#FF>a<noparse><</noparse>b<alpha=#00>"));
        }

        [Test]
        public void Given_TextHoldingANoparseCloser_When_Composed_Then_ItIsEscapedLikeAnyOtherOpener()
        {
            // Arrange — the closer a single noparse span around the run would end early on.
            var shown = "a</noparse><b>c";

            // Act
            var composed = SelectionTextOverlay.Compose(shown, 0, shown.Length, null, 1f);

            // Assert
            Assert.That(
                composed,
                Is.EqualTo("<alpha=#00><alpha=#FF>a<noparse><</noparse>/noparse><noparse><</noparse>b>c<alpha=#00>"));
        }

        [Test]
        public void Given_APasswordField_When_ItsShownTextIsRead_Then_ItIsTheMask()
        {
            // Arrange
            var field = new TextField { isPasswordField = true, maskChar = '*' };
            field.SetValueWithoutNotify("secret");

            // Act
            var shown = SelectionTextOverlay.Displayed((TextElement)field.textEdition);

            // Assert
            Assert.That(shown, Is.EqualTo("******"));
        }

        [Test]
        public void Given_ASelectionTextUtility_When_ALaterRenderDropsIt_Then_TheDrawingComesAndGoes()
        {
            // Arrange
            var field = Mount(TextColorHost);
            Select(field, 1, 3);
            var whileDeclared = Drawing(field) != null;

            // Act
            s_setDeclared.Invoke(false);
            Settle();

            // Assert
            Assert.That((whileDeclared, Drawing(field) != null), Is.EqualTo((true, false)));
        }

        [Test]
        public void Given_ASelectionTextUtility_When_ARangeIsSelectedAndTheSchedulerTicks_Then_TheDrawingShowsThatRun()
        {
            // Arrange
            var field = Mount(TextColorHost);

            // Act
            Select(field, 1, 3);

            // Assert
            Assert.That(Drawing(field).text, Is.EqualTo(SelectionTextOverlay.Compose("hello", 1, 3, Color.white, 1f)));
        }

        // The canary for the two cases below: a tick whose selection moved composes again, and the probe
        // counts that through the same tick delegate they measure.
        [Test]
        public void Given_AFocusedFieldWhoseSelectionMovesBetweenTicks_When_TheSchedulerTicks_Then_TheProbeCountsTheRecomposition()
        {
            // Arrange
            var field = Mount(TextColorHost);
            Select(field, 1, 3);
            var tick = SchedulerTick(_host.Panel);
            tick();
            var steady = GCAllocationProbe.MedianBlocksDuring(tick);
            var wide = false;
            Action moveSelection = () =>
            {
                wide = !wide;
                field.textSelection.SelectRange(1, wide ? 4 : 3);
            };

            // Act
            var moving = GCAllocationProbe.MedianBlocksDuring(moveSelection, tick);

            // Assert
            Assert.That(moving, Is.GreaterThan(steady));
        }

        [Test]
        public void Given_AnUnfocusedFieldWithASelectionUtility_When_TheSchedulerTicks_Then_ItAllocatesNoMoreThanWithoutTheUtility()
        {
            // Arrange
            Mount(SteppingHost);
            var tick = SchedulerTick(_host.Panel);
            tick();
            var withUtility = GCAllocationProbe.MedianBlocksDuring(tick);
            RenderStep(2);
            tick();

            // Act
            var withoutUtility = GCAllocationProbe.MedianBlocksDuring(tick);

            // Assert
            Assert.That(withUtility, Is.EqualTo(withoutUtility));
        }

        [Test]
        public void Given_AFocusedFieldHoldingASteadySelection_When_TheSchedulerTicks_Then_ItAllocatesNoMoreThanWithoutTheUtility()
        {
            // Arrange
            var field = Mount(SteppingHost);
            Select(field, 1, 3);
            var tick = SchedulerTick(_host.Panel);
            tick();
            var withUtility = GCAllocationProbe.MedianBlocksDuring(tick);
            RenderStep(2);
            tick();

            // Act
            var withoutUtility = GCAllocationProbe.MedianBlocksDuring(tick);

            // Assert
            Assert.That(withUtility, Is.EqualTo(withoutUtility));
        }
    }
}
