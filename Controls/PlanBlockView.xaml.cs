using Microsoft.UI.Xaml.Controls;

namespace networker.Controls
{
    /// <summary>
    /// Compact agent todo list. Rows bind straight to <see cref="Models.PlanItem"/>
    /// (status glyph, temperature brush, running weight); no code-behind needed.
    /// </summary>
    public sealed partial class PlanBlockView : UserControl
    {
        public PlanBlockView()
        {
            this.InitializeComponent();
        }
    }
}
