using Microsoft.UI.Xaml;

namespace networker.Controls;

/// <summary>Activates a visual state from an element's available logical height.</summary>
public sealed class ElementHeightTrigger : StateTriggerBase
{
    public static readonly DependencyProperty SourceProperty = DependencyProperty.Register(
        nameof(Source), typeof(FrameworkElement), typeof(ElementHeightTrigger), new PropertyMetadata(null, OnChanged));

    public static readonly DependencyProperty MinHeightProperty = DependencyProperty.Register(
        nameof(MinHeight), typeof(double), typeof(ElementHeightTrigger), new PropertyMetadata(0d, OnChanged));

    public static readonly DependencyProperty MaxHeightProperty = DependencyProperty.Register(
        nameof(MaxHeight), typeof(double), typeof(ElementHeightTrigger), new PropertyMetadata(double.MaxValue, OnChanged));

    public FrameworkElement? Source
    {
        get => (FrameworkElement?)GetValue(SourceProperty);
        set => SetValue(SourceProperty, value);
    }

    public double MinHeight
    {
        get => (double)GetValue(MinHeightProperty);
        set => SetValue(MinHeightProperty, value);
    }

    public double MaxHeight
    {
        get => (double)GetValue(MaxHeightProperty);
        set => SetValue(MaxHeightProperty, value);
    }

    private static void OnChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
    {
        var trigger = (ElementHeightTrigger)sender;
        if (args.OldValue is FrameworkElement oldSource) oldSource.SizeChanged -= trigger.Source_SizeChanged;
        if (args.NewValue is FrameworkElement newSource) newSource.SizeChanged += trigger.Source_SizeChanged;
        trigger.UpdateState();
    }

    private void Source_SizeChanged(object sender, SizeChangedEventArgs args) => UpdateState();

    private void UpdateState()
    {
        double height = Source?.ActualHeight ?? 0;
        SetActive(height >= MinHeight && height <= MaxHeight);
    }
}
