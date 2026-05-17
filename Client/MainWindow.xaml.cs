#nullable disable
using System;
using System.Windows;
using System.Windows.Input;

namespace Client
{
    public partial class MainWindow : Window
    {
        // Экземпляр клиента
        private TcpChatClient _client;

        public MainWindow()
        {
            InitializeComponent();
        }

        // Кнопка Подключиться — создаём клиент и подключаемся к серверу
        private void BtnConnect_Click(object sender, RoutedEventArgs e)
        {
            if (!int.TryParse(txtPort.Text, out int port)) return;
            string nick = txtNick.Text.Trim();
            if (string.IsNullOrEmpty(nick)) return;

            _client = new TcpChatClient();

            // Подписываемся на обычные сообщения — добавляем в ленту
            _client.OnMessage += (from, text) => Dispatcher.Invoke(() =>
                AddLine($"{from}: {text}", false));

            // Подписываемся на системные сообщения — добавляем в ленту
            _client.OnSystem += msg => Dispatcher.Invoke(() =>
                AddLine($"*** {msg} ***", true));

            // Подписываемся на список пользователей — обновляем панель онлайн
            _client.OnUsers += users => Dispatcher.Invoke(() =>
            {
                lstUsers.Items.Clear();
                foreach (var u in users) lstUsers.Items.Add(u);
            });

            // Подписываемся на отключение — уведомляем пользователя
            _client.OnDisconnected += () => Dispatcher.Invoke(() =>
            {
                AddLine("*** Соединение потеряно ***", true);
                SetConnected(false);
            });

            _client.Connect(txtIp.Text.Trim(), port, nick);
            SetConnected(true);
        }

        // Кнопка Отключиться
        private void BtnDisconnect_Click(object sender, RoutedEventArgs e)
        {
            _client?.Disconnect();
            SetConnected(false);
        }

        // Кнопка Отправить
        private void BtnSend_Click(object sender, RoutedEventArgs e) => Send();

        // Отправка по клавише Enter
        private void TxtMsg_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter) Send();
        }

        // Отправка сообщения на сервер
        private void Send()
        {
            string text = txtMsg.Text.Trim();
            if (string.IsNullOrEmpty(text)) return;
            _client?.Send(text);
            txtMsg.Clear();
        }

        // Добавление строки в ленту сообщений с временной меткой
        private void AddLine(string text, bool system)
        {
            string time = DateTime.Now.ToString("HH:mm");
            lstChat.Items.Add($"[{time}] {text}");
            lstChat.ScrollIntoView(lstChat.Items[lstChat.Items.Count - 1]);
        }

        // Включение/выключение элементов интерфейса
        private void SetConnected(bool on)
        {
            btnConnect.IsEnabled = !on;
            btnDisconnect.IsEnabled = on;
            btnSend.IsEnabled = on;
            txtMsg.IsEnabled = on;
        }

        // При закрытии окна отключаемся от сервера
        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            _client?.Disconnect();
        }
    }
}