using System;
using Microsoft.UI.Xaml;

namespace networker.Controls
{
    /// <summary>
    /// Activates a visual state from the available width of a specific element,
    /// rather than the width of the application window.
    /// </summary>
    public sealed class ElementWidthTrigger : StateTriggerBase
    {
        public static readonly DependencyProperty SourceProperty = DependencyProperty.Register(
            nameof(Source),
            typeof(FrameworkElement),
            typeof(ElementWidthTrigger),
            new PropertyMetadata(null, OnSourceChanged));

        public static readonly DependencyProperty MinWidthProperty = DependencyProperty.Register(
            nameof(MinWidth),
            typeof(double),
            typeof(ElementWidthTrigger),
            new PropertyMetadata(0d, OnConstraintChanged));

        public static readonly DependencyProperty MaxWidthProperty = DependencyProperty.Register(
            nameof(MaxWidth),
            typeof(double),
            typeof(ElementWidthTrigger),
            new PropertyMetadata(double.MaxValue, OnConstraintChanged));

        public FrameworkElement? Source
        {
            get => (FrameworkElement?)GetValue(SourceProperty);
            set => SetValue(SourceProperty, value);
        }

        public double MinWidth
        {
            get => (double)GetValue(MinWidthProperty);
            set => SetValue(MinWidthProperty, value);
        }

        public double MaxWidth
        {
            get => (double)GetValue(MaxWidthProperty);
            set => SetValue(MaxWidthProperty, value);
        }

        private static void OnSourceChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs args)
        {
            var trigger = (ElementWidthTrigger)dependencyObject;
            if (args.OldValue is FrameworkElement oldSource)
            {
                oldSource.SizeChanged -= trigger.Source_SizeChanged;
            }

            if (args.NewValue is FrameworkElement newSource)
            {
                newSource.SizeChanged += trigger.Source_SizeChanged;
            }

            trigger.UpdateState();
        }

        private static void OnConstraintChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs args)
            => ((ElementWidthTrigger)dependencyObject).UpdateState();

        private void Source_SizeChanged(object sender, SizeChangedEventArgs e) => UpdateState();

        private void UpdateState()
        {
            double width = Source?.ActualWidth ?? 0;
            SetActive(width >= MinWidth && width <= MaxWidth);
        }
    }
}
