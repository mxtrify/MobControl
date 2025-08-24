namespace MobControlUI.Core.UI
{
    public interface IMessageService
    {
        void Info(string message, string? title = null);
        void Warn(string message, string? title = null);
    }

    public sealed class MessageBoxService : IMessageService
    {
        public void Info(string message, string? title = null) =>
            System.Windows.MessageBox.Show(message, title ?? "Info",
                System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);

        public void Warn(string message, string? title = null) =>
            System.Windows.MessageBox.Show(message, title ?? "Warning",
                System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
    }
}

