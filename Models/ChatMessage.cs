using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace networker.Models
{
    public enum ChatRole
    {
        User,
        Assistant,
        Error
    }

    /// <summary>
    /// A single message rendered in the chat workspace. Notifies the UI of
    /// streaming text updates so the bound template redraws incrementally.
    /// </summary>
    public sealed class ChatMessage : INotifyPropertyChanged
    {
        public string Id { get; } = Guid.NewGuid().ToString("N");

        public ChatRole Role { get; init; }

        public DateTime Timestamp { get; init; } = DateTime.Now;

        public string? Provider { get; set; }

        public string? Model { get; set; }

        public bool IsCode { get; init; }

        public string? CodeTitle { get; init; }

        public string? ValidationBadge { get; set; }

        public string ValidationSeverity { get; set; } = "info";

        private string _text = "";

        public string Text
        {
            get => _text;
            set
            {
                if (_text != value)
                {
                    _text = value;
                    OnPropertyChanged();
                }
            }
        }

        private bool _isStreaming;

        public bool IsStreaming
        {
            get => _isStreaming;
            set
            {
                if (_isStreaming != value)
                {
                    _isStreaming = value;
                    OnPropertyChanged();
                }
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        private void OnPropertyChanged([CallerMemberName] string? name = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }
}
