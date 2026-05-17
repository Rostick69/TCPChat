#nullable disable
using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;

namespace Server
{
    public class ChatServer
    {
        // Событие — отправляет строку лога в интерфейс сервера
        public event Action<string> OnLog;

        // Событие — отправляет количество клиентов в интерфейс
        public event Action<int> OnCountChanged;

        // Порт на котором слушает сервер
        private readonly int _port;

        // TCP слушатель — ожидает входящие подключения
        private TcpListener _listener;

        // Флаг работы сервера
        private bool _running;

        // Словарь: никнейм → StreamWriter для отправки сообщений клиенту
        private readonly Dictionary<string, StreamWriter> _clients = new();

        // Объект синхронизации — защищает словарь от одновременного доступа из разных потоков
        private readonly object _lock = new object();

        // Количество подключённых клиентов
        public int Count => _clients.Count;
        // Список никнеймов подключённых клиентов
        public List<string> GetUsers() => new List<string>(_clients.Keys);

        public ChatServer(int port)
        {
            _port = port;
        }

        // Запуск сервера — начинаем слушать порт и принимать клиентов
        public void Start()
        {
            _running = true;
            _listener = new TcpListener(IPAddress.Any, _port);
            _listener.Start();
            Log($"Сервер запущен на порту {_port}");

            // Бесконечный цикл приёма новых подключений
            while (_running)
            {
                try
                {
                    // Ждём нового клиента — блокирует поток до подключения
                    var client = _listener.AcceptTcpClient();

                    // Каждый клиент обслуживается в отдельном фоновом потоке
                    var t = new Thread(() => Handle(client)) { IsBackground = true };
                    t.Start();
                }
                catch { break; }
            }
        }

        // Остановка сервера
        public void Stop()
        {
            _running = false;
            _listener?.Stop();
            // Очищаем список клиентов при остановке сервера
            lock (_lock) _clients.Clear();
            Log("Сервер остановлен");
        }

        // Обработчик одного клиентского соединения — выполняется в отдельном потоке
        private void Handle(TcpClient tcp)
        {
            string nick = null;
            StreamWriter writer = null;

            try
            {
                var stream = tcp.GetStream();
                var reader = new StreamReader(stream);
                writer = new StreamWriter(stream) { AutoFlush = true };

                // Первое сообщение — команда /join с никнеймом
                string first = reader.ReadLine();
                if (first == null || !first.StartsWith("/join ")) return;

                nick = first.Substring(6).Trim();
                if (string.IsNullOrEmpty(nick)) return;

                // Добавляем клиента в словарь под защитой lock
                // Проверяем что никнейм не занят
                lock (_lock)
                {
                    if (_clients.ContainsKey(nick))
                    {
                        writer.WriteLine("SYS:Ник занят");
                        return;
                    }
                    _clients[nick] = writer;
                }

                Log($"[+] {nick}");
                OnCountChanged?.Invoke(Count);

                // Уведомляем всех о новом участнике
                Broadcast($"SYS:{nick} вошёл в чат", null);

                // Отправляем новому клиенту список пользователей онлайн
                SendUsers(nick);

                // Основной цикл чтения сообщений от клиента
                string line;
                while ((line = reader.ReadLine()) != null)
                {
                    if (string.IsNullOrWhiteSpace(line)) continue;
                    Log($"{nick}: {line}");

                    // Рассылаем сообщение всем клиентам
                    Broadcast($"MSG:{nick}:{line}", null);
                }
            }
            catch { }
            finally
            {
                // Клиент отключился — убираем из словаря и уведомляем остальных
                if (nick != null)
                {
                    lock (_lock) _clients.Remove(nick);
                    Log($"[-] {nick}");
                    OnCountChanged?.Invoke(Count);
                    Broadcast($"SYS:{nick} покинул чат", null);
                }
                tcp.Close();
            }
        }

        // Рассылка сообщения всем клиентам кроме указанного
        // Если клиент недоступен — удаляем его из словаря
        private void Broadcast(string msg, string skip)
        {
            lock (_lock)
            {
                var dead = new List<string>();
                foreach (var kv in _clients)
                {
                    if (kv.Key == skip) continue;
                    try { kv.Value.WriteLine(msg); }
                    catch { dead.Add(kv.Key); }
                }

                // Удаляем отключившихся клиентов
                foreach (var k in dead) _clients.Remove(k);
            }
        }

        // Отправка списка пользователей онлайн конкретному клиенту
        private void SendUsers(string to)
        {
            string list;
            lock (_lock) list = "USERS:" + string.Join(",", _clients.Keys);
            lock (_lock)
            {
                if (_clients.TryGetValue(to, out var w))
                    try { w.WriteLine(list); } catch { }
            }
        }

        // Запись в лог с временной меткой
        private void Log(string msg) =>
            OnLog?.Invoke($"[{DateTime.Now:HH:mm:ss}] {msg}");
    }
}