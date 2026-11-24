using System;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UIElements;
using Velvet.Editor;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies that an inherited value Velvet writes on an element in the style pass that restyles it reaches that
    /// element's children and no other element's. Each case styles two elements alike, each holding a child, lets one
    /// writer change the first alone in that pass, and reads the second's child. GWT, one assert per case.
    /// </summary>
    internal sealed class InEventInheritedWritePanelTests : PanelTestBase
    {
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
    }
}
