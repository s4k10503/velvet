using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine.UIElements;

namespace Velvet.Tests
{
    // The class-list restore names the classes a constructor chain adds, which is a hand-written mirror in
    // the same way the property scrub was — and it had already drifted: a composite field constructed
    // without a label carries a variant class that ClearClassList drops and nothing put back. Comparing
    // against a freshly constructed instance is what says the list is whole, rather than a longer list of
    // names to keep in step.
    internal sealed class PooledElementClassListResetTests
    {
        [TestCase("Button")]
        [TestCase("Label")]
        [TestCase("Toggle")]
        [TestCase("Slider")]
        [TestCase("TextField")]
        public void Given_a_pooled_widget_When_its_reset_helper_runs_Then_its_class_list_is_the_one_a_fresh_instance_carries(string widget)
        {
            // Arrange
            var element = Construct(widget);

            // Act
            Reset(widget, element);

            // Assert
            Assert.That(Classes(element), Is.EqualTo(Classes(Construct(widget))));
        }

        private static string Classes(VisualElement element) =>
            string.Join(" ", element.GetClasses().OrderBy(c => c, StringComparer.Ordinal));

        private static VisualElement Construct(string widget) => widget switch
        {
            "Button" => new Button(),
            "Label" => new Label(),
            "Toggle" => new Toggle(),
            "Slider" => new Slider(),
            "TextField" => new TextField(),
            _ => throw new ArgumentOutOfRangeException(nameof(widget), widget, null),
        };

        private static void Reset(string widget, VisualElement element)
        {
            switch (widget)
            {
                case "Button": FiberButtonPoolHelper.ResetButtonForReuse((Button)element); break;
                case "Label": FiberLabelPoolHelper.ResetLabelForReuse((Label)element); break;
                case "Toggle": FiberTogglePoolHelper.ResetToggleForReuse((Toggle)element); break;
                case "Slider": FiberSliderPoolHelper.ResetSliderForReuse((Slider)element); break;
                case "TextField": FiberTextFieldPoolHelper.ResetTextFieldForReuse((TextField)element); break;
                default: throw new ArgumentOutOfRangeException(nameof(widget), widget, null);
            }
        }
    }
}
