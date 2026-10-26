#nullable disable
using System;
using System.Net.Sockets;
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

        // Кнопка «Запустить»: создаём сервер, подписываемся на события и запускаем
        private void BtnStart_Click(object sender, RoutedEventArgs e)
        {
            if (!int.TryParse(txtPort.Text, out int port) || port < 1 || port > 65535)
            {
                MessageBox.Show("Введите корректный порт (1–65535)", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            lstLog.Items.Clear();
            lstUsers.Items.Clear();

            _server = new ChatServer(port);

            // События приходят из фоновых потоков, поэтому UI обновляем только через Dispatcher.Invoke
            _server.OnLog += msg => Dispatcher.Invoke(() => AddLog(msg));
            _server.OnCountChanged += count => Dispatcher.Invoke(() => lblCount.Content = count);
            _server.OnClientConnected += nick => Dispatcher.Invoke(RefreshUsers);
            _server.OnClientDisconnected += nick => Dispatcher.Invoke(RefreshUsers);
            _server.OnMessageReceived += (from, text) =>
                Dispatcher.Invoke(() => txtStatus.Text = $"Последнее сообщение: {from} в {DateTime.Now:HH:mm:ss}");

            try
            {
                _server.Start();
            }
            catch (SocketException ex)
            {
                // Например, порт уже занят другой программой
                MessageBox.Show($"Не удалось запустить сервер: {ex.Message}", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
                _server = null;
                return;
            }

            btnStart.IsEnabled = false;
            btnStop.IsEnabled = true;
            txtPort.IsEnabled = false;
            txtStatus.Text = $"Сервер работает на порту {port}";
        }

        // Кнопка «Остановить»
        private void BtnStop_Click(object sender, RoutedEventArgs e)
        {
            _server?.Stop();
            lstUsers.Items.Clear();
            btnStart.IsEnabled = true;
            btnStop.IsEnabled = false;
            txtPort.IsEnabled = true;
            txtStatus.Text = "Сервер остановлен";
        }

        // Добавление строки в лог с автопрокруткой
        private void AddLog(string msg)
        {
            lstLog.Items.Add(msg);
            lstLog.ScrollIntoView(lstLog.Items[lstLog.Items.Count - 1]);
        }

        // Обновление списка подключённых клиентов
        private void RefreshUsers()
        {
            lstUsers.Items.Clear();
            if (_server == null) return;
            foreach (var u in _server.GetUsers())
                lstUsers.Items.Add(u);
        }

        // При закрытии окна останавливаем сервер
        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            _server?.Stop();
        }
    }
}
