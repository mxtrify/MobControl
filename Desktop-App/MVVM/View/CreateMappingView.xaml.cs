using System.IO;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace MobControlUI.MVVM.View
{
    public partial class CreateMappingView : UserControl
    {
        // cache invalid filename chars regex: \ / : * ? " < > | and others per platform
        private static readonly Regex InvalidNameRegex =
            new($"[{Regex.Escape(new string(Path.GetInvalidFileNameChars()))}]",
                RegexOptions.Compiled);

        public CreateMappingView()
        {
            InitializeComponent();
        }

        // Blocks typing of invalid filename characters
        private void FileName_PreviewTextInput(object sender, TextCompositionEventArgs e)
        {
            e.Handled = InvalidNameRegex.IsMatch(e.Text);
        }

        // Blocks paste that contains invalid filename characters
        private void FileName_OnPaste(object sender, DataObjectPastingEventArgs e)
        {
            if (e.DataObject.GetDataPresent(DataFormats.UnicodeText))
            {
                var text = (string)e.DataObject.GetData(DataFormats.UnicodeText);
                if (InvalidNameRegex.IsMatch(text))
                    e.CancelCommand();
            }
        }
    }
}
