#nullable disable
using System;
using System.IO;
using System.Net.Sockets;
using System.Threading;

namespace Client
{
    // Сетевая часть клиента: подключение, поток чтения, разбор протокола и события для интерфейса
    public class TcpChatClient
    {
        // Обычное сообщение в общий чат (отправитель, текст)
        public event Action<string, string> OnMessage;
        // Личное сообщение: входящее (от кого, текст)
        public event Action<string, string> OnPrivateMessage;
        // Личное сообщение: подтверждение отправки (кому, текст)
        public event Action<string, string> OnPrivateSent;
        // Системное сообщение (вход/выход участников и т. п.)
        public event Action<string> OnSystem;
        // Ошибка от сервера (например, ник занят)
        public event Action<string> OnError;
        // Список пользователей онлайн
        public event Action<string[]> OnUsers;
        // Потеря соединения с сервером
        public event Action OnDisconnected;

        private TcpClient _tcp;
        private StreamWriter _writer;
        private volatile bool _connected;

        public bool IsConnected => _connected;

        // Подключение к серверу. Если сервер недоступен — бросает исключение,
        // которое обрабатывается в интерфейсе
        public void Connect(string host, int port, string nick)
        {
            _tcp = new TcpClient();

            // Ждём подключения не дольше 5 секунд, чтобы интерфейс не зависал надолго
            var connectTask = _tcp.ConnectAsync(host, port);
            bool completed;
            try
            {
                completed = connectTask.Wait(5000);
            }
            catch (AggregateException ae)
            {
                _tcp.Close();
                throw ae.GetBaseException(); // например, SocketException «подключение не установлено»
            }
            if (!completed)
            {
                _tcp.Close();
                throw new TimeoutException("Сервер не ответил за 5 секунд");
            }

            var stream = _tcp.GetStream();
            _writer = new StreamWriter(stream) { AutoFlush = true };
            var reader = new StreamReader(stream);
            _connected = true;

            // Первая команда протокола — /join с никнеймом
            _writer.WriteLine($"/join {nick}");

            // Фоновый поток постоянно читает строки от сервера
            var t = new Thread(() => ReadLoop(reader)) { IsBackground = true, Name = "ReadLoop" };
            t.Start();
        }

        private void ReadLoop(StreamReader reader)
        {
            try
            {
                string line;
                while (_connected && (line = reader.ReadLine()) != null)
                    Parse(line);
            }
            catch (IOException) { }
            catch (ObjectDisposedException) { }
            finally
            {
                // Если отключение произошло не по кнопке — сообщаем интерфейсу
                if (_connected)
                {
                    _connected = false;
                    OnDisconnected?.Invoke();
                }
            }
        }

        // Отправка строки на сервер (обычное сообщение или команда)
        public bool Send(string text)
        {
            if (!_connected) return false;
            try
            {
                _writer.WriteLine(text);
                return true;
            }
            catch (IOException)
            {
                return false;
            }
        }

        // Отключение по кнопке
        public void Disconnect()
        {
            _connected = false;
            try { _tcp?.Close(); } catch { }
        }

        // Разбор входящей строки по префиксу протокола
        private void Parse(string line)
        {
            if (line.StartsWith("MSG:"))
            {
                // MSG:ник:текст (текст может содержать двоеточия, поэтому делим только на 2 части)
                var parts = line.Substring(4).Split(new[] { ':' }, 2);
                if (parts.Length == 2) OnMessage?.Invoke(parts[0], parts[1]);
            }
            else if (line.StartsWith("PM:"))
            {
                var parts = line.Substring(3).Split(new[] { ':' }, 2);
                if (parts.Length == 2) OnPrivateMessage?.Invoke(parts[0], parts[1]);
            }
            else if (line.StartsWith("PMTO:"))
            {
                var parts = line.Substring(5).Split(new[] { ':' }, 2);
                if (parts.Length == 2) OnPrivateSent?.Invoke(parts[0], parts[1]);
            }
            else if (line.StartsWith("USERS:"))
            {
                OnUsers?.Invoke(line.Substring(6).Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries));
            }
            else if (line.StartsWith("SYS:"))
            {
                OnSystem?.Invoke(line.Substring(4));
            }
            else if (line.StartsWith("ERR:"))
            {
                OnError?.Invoke(line.Substring(4));
            }
            else
            {
                OnSystem?.Invoke(line);
            }
        }
    }
}
