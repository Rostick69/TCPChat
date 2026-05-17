#nullable disable
using System;
using System.IO;
using System.Net.Sockets;
using System.Threading;

namespace Client
{
    public class TcpChatClient
    {
        // Событие — срабатывает при получении обычного сообщения (отправитель, текст)
        public event Action<string, string> OnMessage;

        // Событие — срабатывает при получении системного сообщения
        public event Action<string> OnSystem;

        // Событие — срабатывает при получении списка пользователей онлайн
        public event Action<string[]> OnUsers;

        // Событие — срабатывает при потере соединения с сервером
        public event Action OnDisconnected;

        // TCP клиент — соединение с сервером
        private TcpClient _tcp;

        // Поток записи — отправка сообщений серверу
        private StreamWriter _writer;

        // Флаг активного соединения
        private bool _connected;
        // Свойство для проверки состояния подключения из интерфейса
        public bool IsConnected => _connected;

        // Подключение к серверу по адресу, порту и никнейму
        public void Connect(string host, int port, string nick)
        {
            _tcp = new TcpClient();
            _tcp.Connect(host, port);

            var stream = _tcp.GetStream();
            _writer = new StreamWriter(stream) { AutoFlush = true };
            var reader = new StreamReader(stream);

            _connected = true;

            // Отправляем никнейм серверу командой /join
            _writer.WriteLine($"/join {nick}");

            // Запускаем фоновый поток чтения входящих сообщений
            var t = new Thread(() =>
            {
                try
                {
                    string line;

                    // Читаем строки пока соединение активно
                    while (_connected && (line = reader.ReadLine()) != null)
                        Parse(line);
                }
                catch { }
                finally
                {
                    // Соединение разорвано — уведомляем интерфейс
                    if (_connected) { _connected = false; OnDisconnected?.Invoke(); }
                }
            })
            { IsBackground = true };
            t.Start();
        }

        // Отправка сообщения на сервер с проверкой подключения
        public void Send(string text)
        {
            if (!_connected) return;
            try { _writer?.WriteLine(text); }
            catch { }
        }

        // Отключение от сервера
        public void Disconnect()
        {
            _connected = false;
            try { _tcp?.Close(); } catch { }
        }

        // Разбор входящей строки по префиксу протокола
        private void Parse(string line)
        {
            // Системное сообщение: SYS:текст
            if (line.StartsWith("SYS:"))
                OnSystem?.Invoke(line.Substring(4));

            // Список пользователей: USERS:ник1,ник2,ник3
            else if (line.StartsWith("USERS:"))
                OnUsers?.Invoke(line.Substring(6).Split(',', StringSplitOptions.RemoveEmptyEntries));

            // Обычное сообщение: MSG:ник:текст
            else if (line.StartsWith("MSG:"))
            {
                var parts = line.Substring(4).Split(':', 2);
                if (parts.Length == 2) OnMessage?.Invoke(parts[0], parts[1]);
            }
            else
                OnSystem?.Invoke(line);
        }
    }
}