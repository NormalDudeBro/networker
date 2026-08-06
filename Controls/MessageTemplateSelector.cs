using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using networker.Models;

namespace networker.Controls
{
    /// <summary>
    /// Picks a data template for a <see cref="ChatMessage"/> based on its role
    /// and content type (user bubble, assistant prose, code block, error).
    /// </summary>
    public sealed class MessageTemplateSelector : DataTemplateSelector
    {
        public DataTemplate? UserTemplate { get; set; }

        public DataTemplate? AssistantTemplate { get; set; }

        public DataTemplate? CodeTemplate { get; set; }

        public DataTemplate? ErrorTemplate { get; set; }

        protected override DataTemplate SelectTemplateCore(object item)
        {
            return SelectTemplateCore(item, null);
        }

        protected override DataTemplate SelectTemplateCore(object item, DependencyObject? container)
        {
            if (item is ChatMessage message)
            {
                if (message.Role == ChatRole.User) return UserTemplate ?? AssistantTemplate!;
                if (message.Role == ChatRole.Error) return ErrorTemplate ?? AssistantTemplate!;
                if (message.IsCode) return CodeTemplate ?? AssistantTemplate!;
            }
            return AssistantTemplate ?? UserTemplate!;
        }
    }
}
