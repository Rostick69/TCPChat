#nullable disable
using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace Client
{
    public partial class MainWindow : Window
    {
        private TcpChatClient _client;

        // Цвета строк ленты
        private static readonly Brush NormalBrush = new SolidColorBrush(Color.FromRgb(0xcd, 0xd6, 0xf4));
        private static readonly Brush SystemBrush = new SolidColorBrush(Color.FromRgb(0xa6, 0xad, 0xc8));
        private static readonly Brush PrivateBrush = new SolidColorBrush(Color.FromRgb(0xf9, 0xe2, 0xaf));
        private static readonly Brush ErrorBrush = new SolidColorBrush(Color.FromRgb(0xf3, 0x8b, 0xa8));

        public MainWindow()
        {
            InitializeComponent();
        }

        // Кнопка «Подключиться»
        private void BtnConnect_Click(object sender, RoutedEventArgs e)
        {
            if (!int.TryParse(txtPort.Text, out int port) || port < 1 || port > 65535)
            {
                MessageBox.Show("Введите корректный порт", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            string nick = txtNick.Text.Trim();
            if (string.IsNullOrEmpty(nick))
            {
                MessageBox.Show("Введите никнейм", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            _client = new TcpChatClient();

            // Подписка на события клиента. Они вызываются из фонового потока чтения,
            // поэтому элементы интерфейса меняем только через Dispatcher.Invoke
            _client.OnMessage += (from, text) => Dispatcher.Invoke(() => AddLine($"{from}: {text}", NormalBrush));
            _client.OnPrivateMessage += (from, text) => Dispatcher.Invoke(() => AddLine($"[ЛС от {from}] {text}", PrivateBrush));
            _client.OnPrivateSent += (to, text) => Dispatcher.Invoke(() => AddLine($"[ЛС для {to}] {text}", PrivateBrush));
            _client.OnSystem += msg => Dispatcher.Invoke(() => AddLine($"*** {msg} ***", SystemBrush));
            _client.OnError += msg => Dispatcher.Invoke(() =>
            {
                AddLine($"Ошибка: {msg}", ErrorBrush);
                MessageBox.Show(msg, "Сервер отклонил подключение", MessageBoxButton.OK, MessageBoxImage.Warning);
            });
            _client.OnUsers += users => Dispatcher.Invoke(() =>
            {
                lstUsers.Items.Clear();
                foreach (var u in users) lstUsers.Items.Add(u);
            });
            _client.OnDisconnected += () => Dispatcher.Invoke(() =>
            {
                AddLine("*** Соединение с сервером потеряно ***", ErrorBrush);
                lstUsers.Items.Clear();
                SetConnected(false);
            });

            try
            {
                _client.Connect(txtIp.Text.Trim(), port, nick);
            }
            catch (Exception ex)
            {
                // Сервер не запущен, неверный адрес или таймаут — программа не падает, а сообщает об ошибке
                AddLine($"Не удалось подключиться: {ex.Message}", ErrorBrush);
                MessageBox.Show($"Не удалось подключиться к серверу:\n{ex.Message}", "Ошибка подключения",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                _client = null;
                return;
            }

            Title = $"TCP Чат — Клиент ({nick})";
            SetConnected(true);
            txtMsg.Focus();
        }

        // Кнопка «Отключиться»
        private void BtnDisconnect_Click(object sender, RoutedEventArgs e)
        {
            _client?.Disconnect();
            lstUsers.Items.Clear();
            AddLine("*** Вы отключились ***", SystemBrush);
            SetConnected(false);
        }

        private void BtnSend_Click(object sender, RoutedEventArgs e) => Send();

        // Отправка по клавише Enter
        private void TxtMsg_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter) Send();
        }

        // Двойной щелчок по пользователю — подставляем команду личного сообщения
        private void LstUsers_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (lstUsers.SelectedItem is string user && txtMsg.IsEnabled)
            {
                txtMsg.Text = $"/pm {user} ";
                txtMsg.CaretIndex = txtMsg.Text.Length;
                txtMsg.Focus();
            }
        }

        private void Send()
        {
            string text = txtMsg.Text.Trim();
            if (string.IsNullOrEmpty(text)) return; // пустые сообщения не отправляем
            if (_client == null || !_client.Send(text))
            {
                AddLine("Сообщение не отправлено: нет соединения", ErrorBrush);
                return;
            }
            txtMsg.Clear();
        }

        // Добавление строки в ленту с временем и цветом
        private void AddLine(string text, Brush color)
        {
            var item = new ListBoxItem
            {
                Content = new TextBlock { Text = $"[{DateTime.Now:HH:mm}] {text}", TextWrapping = TextWrapping.Wrap },
                Foreground = color
            };
            lstChat.Items.Add(item);
            lstChat.ScrollIntoView(item);
        }

        private void SetConnected(bool on)
        {
            btnConnect.IsEnabled = !on;
            btnDisconnect.IsEnabled = on;
            btnSend.IsEnabled = on;
            txtMsg.IsEnabled = on;
            txtIp.IsEnabled = !on;
            txtPort.IsEnabled = !on;
            txtNick.IsEnabled = !on;
            if (!on) Title = "TCP Чат — Клиент";
        }

        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            _client?.Disconnect();
        }
    }
}
