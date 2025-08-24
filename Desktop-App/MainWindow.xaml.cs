using Microsoft.Extensions.DependencyInjection;
using MobControlUI.Core.Logging;
using MobControlUI.Core.Sync;
using System;
using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;

namespace MobControlUI
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
        }

        private void Root_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            // Drag the borderless window from any non-interactive area
            if (e.ButtonState == MouseButtonState.Pressed)
                DragMove();
        }

        private void Min_Click(object sender, RoutedEventArgs e)
            => WindowState = WindowState.Minimized;

        private void Close_Click(object sender, RoutedEventArgs e)
            => Close();

        /// <summary>
        /// Ensure RTDB upload completes before the window fully closes.
        /// Runs the async upload on a background thread and blocks until done,
        /// avoiding UI-thread deadlock.
        /// </summary>
        protected override void OnClosing(CancelEventArgs e)
        {
            var services = ((App)Application.Current).Services;
            var log = services.GetRequiredService<ILogService>();
            var sync = services.GetRequiredService<IFirebaseRtdbFolderSync>();

            log.Add("UI: OnClosing → best-effort RTDB flush (10s)");

            try
            {
                Task.Run(() => sync.UploadAllBestEffortAsync(TimeSpan.FromSeconds(10)))
                    .Wait(TimeSpan.FromSeconds(11)); // tiny cushion
            }
            catch (Exception ex)
            {
                log.Add($"UI: OnClosing flush error: {ex.Message}", "Warn");
            }

            base.OnClosing(e);
        }
    }
}