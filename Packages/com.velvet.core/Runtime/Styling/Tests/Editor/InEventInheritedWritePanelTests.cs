using System;
using System.Reflection;
using System.Runtime.CompilerServices;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UIElements;
using Velvet.Editor;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies that an inherited value Velvet writes on an element in the style pass that restyles it reaches that
    /// element's children and no other element's: those cases style two elements alike, each holding a child, let one
    /// writer change the first alone in that pass, and read the second's child. Four more specify that the hash the pass
    /// stored is left alone where Velvet writes nothing the element's children inherit in its style event, or writes
    /// outside one, and the last that the editor refreshes it through a compiled accessor. GWT, one assert per case.
    /// </summary>
    internal sealed class InEventInheritedWritePanelTests : PanelTestBase
    {
        private static readonly FieldInfo s_storedHash = EngineMember.ElementInheritedStylesHash.ResolveField();

        private StyleSheet _appSheet;

        [TearDown]
        public void DestroyAppSheet()
        {
            if (_appSheet != null)
            {
                UnityEngine.Object.DestroyImmediate(_appSheet);
                _appSheet = null;
            }
        }

        private VisualElement Root => _window.rootVisualElement;

        // Attaches a sheet of the app's own, imported as the editor imports a .uss file.
        private void AttachAppSheet(string uss)
        {
            _appSheet = ScriptableObject.CreateInstance<StyleSheet>();
            var impl = Activator.CreateInstance(StockStyleSheetImport.Impl!, true)!;
            try
            {
                StockStyleSheetImport.Import!.Invoke(impl, new object[] { _appSheet, uss });
            }
            catch (TargetInvocationException)
            {
                // The importer reports through its context, which this instance has none of; what it stored is used.
            }
            Root.styleSheets.Add(_appSheet);
        }

        private void RunTwoUpdates()
        {
            ForcePanelUpdate(Root.panel);
            ForcePanelUpdate(Root.panel);
        }

        private static VNode[] Holding(bool child, string name) => child ? new VNode[] { V.Div(name: name) } : Array.Empty<VNode>();

        private static VNode InheritorBesideAPeer(bool children) => V.Div(name: "parent", children: new VNode[]
        {
            V.Div(name: "first", className: "restyled text-inherit", children: Holding(children, "a")),
            V.Div(name: "second", className: "restyled", children: Holding(children, "b")),
        });

        private static VNode InheritorUnder(string parentClass) => V.Div(name: "parent", className: parentClass,
            children: new VNode[] { V.Div(name: "child", className: "w-[40px] h-[40px] text-inherit") });

        private static int StoredHash(VisualElement element) => (int)s_storedHash.GetValue(element);

        // In each style event element is sent, replaces the hash the pass stored with another, and keeps the last one
        // written. Registered trickle-down, so it runs before the bubble-up callbacks Velvet registers for the event.
        private static StrongBox<int> ReplaceStoredHashInStyleEvents(VisualElement element)
        {
            var written = new StrongBox<int>();
            element.RegisterCallback<CustomStyleResolvedEvent>(_ =>
            {
                written.Value = ~StoredHash(element);
                s_storedHash.SetValue(element, written.Value);
            }, TrickleDown.TrickleDown);
            return written;
        }

        [Test]
        public void Given_TwoButtonsOfOneStyleWhoseLineBoxAlignsOnlyTheFirst_When_TheirStylePassRuns_Then_TheChildOfTheSecondKeepsItsAlignment()
        {
            // Arrange — a colour of their own, so that no earlier case has cached a style under what the buttons pass
            // down; the second's alignment is an author's, which its line box leaves as it is.
            VelvetStyleUtilities.AttachTo(Root);
            _mounted = V.Mount(Root, V.Div(children: new VNode[]
            {
                V.Button(name: "first", className: "text-top-left", text: "a", children: new VNode[] { V.Div(name: "a") }),
                V.Button(name: "second", className: "text-top-left", text: "b", children: new VNode[] { V.Div(name: "b") }),
            }));
            var colour = new Color(0.2345f, 0.6789f, 0.0123f);
            Root.Q("first").style.color = colour;
            var second = Root.Q("second");
            second.style.color = colour;
            second.style.unityTextAlign = TextAnchor.UpperLeft;

            // Act
            RunTwoUpdates();

            // Assert
            Assert.That(Root.Q("b").resolvedStyle.unityTextAlign, Is.EqualTo(TextAnchor.UpperLeft));
        }

        [Test]
        public void Given_TwoButtonsOfOneStyleOnlyTheFirstShowingNoRichText_When_TheirStylePassRuns_Then_TheChildOfTheSecondKeepsItsFont()
        {
            // Arrange — the first is drawn in a copy of its font, which its line box writes.
            VelvetStyleUtilities.AttachTo(Root);
            _mounted = V.Mount(Root, V.Div(children: new VNode[]
            {
                V.Button(name: "first", text: "a", children: new VNode[] { V.Div(name: "a") }),
                V.Button(name: "second", text: "b", children: new VNode[] { V.Div(name: "b") }),
            }));
            var colour = new Color(0.3456f, 0.7891f, 0.1235f);
            var first = Root.Q<Button>("first");
            first.style.color = colour;
            first.enableRichText = false;
            var second = Root.Q("second");
            second.style.color = colour;

            // Act
            RunTwoUpdates();

            // Assert
            Assert.That(Root.Q("b").resolvedStyle.unityFontDefinition, Is.EqualTo(second.resolvedStyle.unityFontDefinition));
        }

        [Test]
        public void Given_TwoElementsOfOneStyleOnlyTheFirstInheritingItsTextColour_When_ChildrenMountAsTheirParentsColourChanges_Then_TheChildOfTheSecondKeepsItsColour()
        {
            // Arrange — no bundled sheet, so the parent matches no rule for ColorReferenceWatch's marker and the first
            // reads the parent's new colour in its own style event; the app's variable stands in for the marker on both.
            AttachAppSheet(".restyled { --restyled: 1; }");
            _mounted = V.Mount(Root, InheritorBesideAPeer(children: false));
            var parent = Root.Q("parent");
            var held = new Color(0.4567f, 0.8912f, 0.2346f);
            parent.style.color = held;
            Root.Q("second").style.color = held;
            RunTwoUpdates();
            _mounted.Render(InheritorBesideAPeer(children: true));
            parent.style.color = new Color(0.5679f, 0.9124f, 0.3457f);

            // Act
            RunTwoUpdates();

            // Assert
            Assert.That(Root.Q("b").resolvedStyle.color, Is.EqualTo(held));
        }

        [Test]
        public void Given_TwoElementsOfOneStyleOnlyTheFirstReadingTheirVariable_When_TheirStylePassRuns_Then_TheChildOfTheSecondKeepsItsLetterSpacing()
        {
            // Arrange — the variable is declared by a rule of the app's that both match.
            AttachAppSheet(".declares { --spacing: 3.25px; }");
            _mounted = V.Mount(Root, V.Div(children: new VNode[]
            {
                V.Div(name: "first", className: "declares tracking-[var(--spacing)]", children: new VNode[] { V.Div(name: "a") }),
                V.Div(name: "second", className: "declares", children: new VNode[] { V.Div(name: "b") }),
            }));
            var colour = new Color(0.6781f, 0.0124f, 0.4568f);
            Root.Q("first").style.color = colour;
            Root.Q("second").style.color = colour;

            // Act
            RunTwoUpdates();

            // Assert
            Assert.That(Root.Q("b").resolvedStyle.letterSpacing, Is.EqualTo(Root.Q("second").resolvedStyle.letterSpacing));
        }

        // GREEN_ON_BASE(characterization): nothing on the base refreshes this element's hash, so the one this case writes stays.
        // Call `InheritedStyleHash.Refresh(declaring);` in OnCustomStyle without its `HoldsInheritedLayer` test and it replaces that hash.
        [Test]
        public void Given_AnElementDeclaringAVariableOnlyItsChildReads_When_ItsStyleEventGivesTheChildItsWidth_Then_TheElementKeepsTheHashTheEventLeft()
        {
            // Arrange
            AttachAppSheet(".declares { --w: 25px; }");
            _mounted = V.Mount(Root, V.Div(name: "outer", className: "declares", children: new VNode[]
            {
                V.Div(name: "inner", className: "w-[var(--w)]"),
            }));
            var outer = Root.Q("outer");
            var written = ReplaceStoredHashInStyleEvents(outer);

            // Act
            ForcePanelUpdate(Root.panel);

            // Assert — null where the child was not given the width, which a hash can never equal.
            Assert.That(Root.Q("inner").style.width.value.value == 25f ? StoredHash(outer) : (int?)null,
                Is.EqualTo(written.Value));
        }

        // GREEN_ON_BASE(characterization): nothing on the base refreshes this element's hash, so the one this case writes stays.
        // Drop `&& Inherits(consumer.Property.Value)` from HoldsInheritedLayer and the width the element reads makes it refresh.
        [Test]
        public void Given_AnElementReadingItsOwnVariableForAWidth_When_ItsStyleEventGivesItTheWidth_Then_ItKeepsTheHashTheEventLeft()
        {
            // Arrange
            AttachAppSheet(".declares { --w: 25px; }");
            _mounted = V.Mount(Root, V.Div(name: "self", className: "declares w-[var(--w)]"));
            var self = Root.Q("self");
            var written = ReplaceStoredHashInStyleEvents(self);

            // Act
            ForcePanelUpdate(Root.panel);

            // Assert — null where the element was not given the width, which a hash can never equal.
            Assert.That(self.style.width.value.value == 25f ? StoredHash(self) : (int?)null, Is.EqualTo(written.Value));
        }

        // GREEN_ON_BASE(characterization): nothing on the base refreshes this element's hash, so the one this case writes stays.
        // Drop `textColour && ` from ReresolveColorReferences' condition and the border it re-reads makes it refresh.
        [Test]
        public void Given_AnElementWhoseBorderReadsItsTextColour_When_ItsStyleEventReReadsTheBorder_Then_ItKeepsTheHashTheEventLeft()
        {
            // Arrange
            VelvetStyleUtilities.AttachTo(Root);
            _mounted = V.Mount(Root, V.Div(name: "box", className: "w-[40px] h-[40px] border-2 border-current text-[#ff0000]"));
            var box = Root.Q("box");
            RunTwoUpdates();
            var written = ReplaceStoredHashInStyleEvents(box);
            box.style.color = Color.blue;

            // Act
            ForcePanelUpdate(Root.panel);

            // Assert — null where the border did not follow the text colour, which a hash can never equal.
            Assert.That(box.style.borderTopColor.value == Color.blue ? StoredHash(box) : (int?)null, Is.EqualTo(written.Value));
        }

        // GREEN_ON_BASE(characterization): nothing on the base refreshes the child's hash, so the one its last pass stored stays.
        // Drop ` && ReferenceEquals(ColorReferenceWatch.Restyled, element)` from ReresolveColorReferences and it refreshes.
        [Test]
        public void Given_AnElementInheritingItsTextColour_When_ARenderWritesItsParentsColour_Then_ItKeepsTheHashItsLastStylePassStored()
        {
            // Arrange
            VelvetStyleUtilities.AttachTo(Root);
            _mounted = V.Mount(Root, InheritorUnder("text-[#ff0000]"));
            RunTwoUpdates();
            var child = Root.Q("child");
            var stored = StoredHash(child);

            // Act — no style pass follows the render, so the colour it gives the child is written outside one.
            _mounted.Render(InheritorUnder("text-[#0000ff]"));

            // Assert — null where the render gave the child no colour, which a hash can never equal.
            Assert.That(child.style.color.value == Color.blue ? StoredHash(child) : (int?)null, Is.EqualTo(stored));
        }

        [Test]
        public void Given_TheEditor_When_InheritedStyleHashLoads_Then_ItRefreshesThroughACompiledAccessor()
        {
            // Act
            var compiled = typeof(InheritedStyleHash).GetField("s_compiled", BindingFlags.NonPublic | BindingFlags.Static)
                ?.GetValue(null);

            // Assert
            Assert.That(compiled, Is.Not.Null);
        }
    }
}
