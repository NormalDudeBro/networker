using System;
using System.Collections.Generic;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace networker.Controls
{
    /// <summary>
    /// Recent-prompt chips shown above the composer. Clicking a chip raises
    /// <see cref="PromptSelected"/> so the page can fill the input. Purely
    /// presentational — the page owns the history store and visibility.
    /// </summary>
    public sealed partial class PromptHistoryControl : UserControl
    {
        public event EventHandler<string>? PromptSelected;

        public PromptHistoryControl()
        {
            this.InitializeComponent();
        }

        public void SetItems(IReadOnlyList<string> items)
        {
            Chips.ItemsSource = items;
        }

        public void Clear() => Chips.ItemsSource = null;

        private void Chip_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement element && element.DataContext is string prompt)
            {
                PromptSelected?.Invoke(this, prompt);
            }
        }
    }
}
