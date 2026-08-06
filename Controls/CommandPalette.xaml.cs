using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;

namespace networker.Controls
{
    public sealed partial class CommandPalette : UserControl
    {
        private List<PaletteCommand> _commands = new();
        private readonly List<PaletteCommand> _filtered = new();

        public CommandPalette()
        {
            this.InitializeComponent();
            this.Loaded += (s, e) =>
            {
                SearchBox.Text = "";
                RefreshFilter();
            };
        }

        public event EventHandler? Closed;

        public bool IsOpen => Visibility == Visibility.Visible;

        public void Open()
        {
            Visibility = Visibility.Visible;
            SearchBox.Focus(FocusState.Programmatic);
        }

        public void Close()
        {
            Visibility = Visibility.Collapsed;
            Closed?.Invoke(this, EventArgs.Empty);
        }

        public void SetCommands(IEnumerable<PaletteCommand> commands)
        {
            _commands = commands.ToList();
            RefreshFilter();
        }

        private void RefreshFilter()
        {
            string query = SearchBox.Text ?? "";
            _filtered.Clear();
            _filtered.AddRange(_commands.Where(c => c.IsMatch(query)));

            CommandList.ItemsSource = null;
            CommandList.ItemsSource = _filtered;

            if (_filtered.Count > 0)
            {
                CommandList.SelectedIndex = 0;
            }
        }

        private void RunSelected()
        {
            if (CommandList.SelectedItem is PaletteCommand command)
            {
                Close();
                command.Action();
            }
        }

        private void SearchBox_TextChanged(object sender, TextChangedEventArgs e) => RefreshFilter();

        private void SearchBox_KeyDown(object sender, KeyRoutedEventArgs e)
        {
            switch (e.Key)
            {
                case Windows.System.VirtualKey.Enter:
                    e.Handled = true;
                    RunSelected();
                    break;
                case Windows.System.VirtualKey.Down:
                    if (_filtered.Count > 0 && CommandList.SelectedIndex < _filtered.Count - 1)
                    {
                        CommandList.SelectedIndex++;
                    }
                    e.Handled = true;
                    break;
                case Windows.System.VirtualKey.Up:
                    if (CommandList.SelectedIndex > 0)
                    {
                        CommandList.SelectedIndex--;
                    }
                    e.Handled = true;
                    break;
                case Windows.System.VirtualKey.Escape:
                    e.Handled = true;
                    Close();
                    break;
            }
        }

        private void Panel_KeyDown(object sender, KeyRoutedEventArgs e)
        {
            if (e.Key == Windows.System.VirtualKey.Escape)
            {
                e.Handled = true;
                Close();
            }
        }

        private void CommandList_ItemClick(object sender, ItemClickEventArgs e)
        {
            if (e.ClickedItem is PaletteCommand command)
            {
                Close();
                command.Action();
            }
        }
    }
}
