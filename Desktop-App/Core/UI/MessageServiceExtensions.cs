using System.Windows;

namespace MobControlUI.Core.UI
{
    public static class MessageServiceExtensions
    {
        /// <summary>
        /// Shows a Yes/No confirmation dialog and returns true if the user chose Yes.
        /// </summary>
        public static bool Confirm(this IMessageService _, string message, string title = "Confirm")
        {
            var result = MessageBox.Show(
                message,
                title,
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            return result == MessageBoxResult.Yes;
        }
    }
}