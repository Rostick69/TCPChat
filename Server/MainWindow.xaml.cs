#nullable disable
using System;
using System.Threading;
using System.Windows;

namespace Server
{
    public partial class MainWindow : Window
    {
        private ChatServer _server;

        public MainWindow()
        {
            InitializeComponent();
        }

        private void BtnStart_Click(object sender, RoutedEventArgs e)
        {
            if (!int.TryParse(txtPort.Text, out int port)) return;

            _server = new ChatServer(port);
            _server.OnLog += msg => Dispatcher.Invoke(() =>
            {
                lstLog.Items.Add(msg);
                lstLog.ScrollIntoView(lstLog.Items[lstLog.Items.Count - 1]);
            });
            _server.OnCountChanged += count => Dispatcher.Invoke(() => lblCount.Content = count);

            var t = new Thread(_server.Start) { IsBackground = true };
            t.Start();

            btnStart.IsEnabled = false;
            btnStop.IsEnabled = true;
        }

        private void BtnStop_Click(object sender, RoutedEventArgs e)
        {
            _server?.Stop();
            btnStart.IsEnabled = true;
            btnStop.IsEnabled = false;
        }

        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            _server?.Stop();
        }
    }
}