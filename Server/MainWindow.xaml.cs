#nullable disable
using System;
using System.Threading;
using System.Windows;

namespace Server
{
    public partial class MainWindow : Window
    {
        // Экземпляр сервера
        private ChatServer _server;

        public MainWindow()
        {
            InitializeComponent();
        }

        // Кнопка Запустить — создаём сервер и запускаем в отдельном потоке
        private void BtnStart_Click(object sender, RoutedEventArgs e)
        {
            if (!int.TryParse(txtPort.Text, out int port)) return;

            _server = new ChatServer(port);

            // Подписываемся на лог — добавляем запись в список через Dispatcher
            _server.OnLog += msg => Dispatcher.Invoke(() =>
            {
                lstLog.Items.Add(msg);
                lstLog.ScrollIntoView(lstLog.Items[lstLog.Items.Count - 1]);
            });

            // Подписываемся на изменение количества клиентов
            _server.OnCountChanged += count => Dispatcher.Invoke(() => lblCount.Content = count);

            // Запускаем сервер в отдельном фоновом потоке чтобы не блокировать UI
            var t = new Thread(_server.Start) { IsBackground = true };
            t.Start();

            btnStart.IsEnabled = false;
            btnStop.IsEnabled = true;
        }

        // Кнопка Остановить — останавливаем сервер
        private void BtnStop_Click(object sender, RoutedEventArgs e)
        {
            _server?.Stop();
            btnStart.IsEnabled = true;
            btnStop.IsEnabled = false;
        }

        // При закрытии окна останавливаем сервер
        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            _server?.Stop();
        }
    }
}