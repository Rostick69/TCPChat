#nullable disable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;

namespace Server
{
    // Одно клиентское подключение: сокет и поток записи
    public class ClientConnection
    {
        public TcpClient Tcp { get; set; }
        public StreamWriter Writer { get; set; }
    }

    // Многопоточный TCP-сервер чата.
    // Протокол (строки, заканчиваются \n):
    //   клиент -> сервер:  /join ник | /users | /pm ник текст | обычный текст
    //   сервер -> клиент:  MSG:ник:текст | SYS:текст | USERS:ник1,ник2 | PM:от:текст | PMTO:кому:текст | ERR:текст
    public class ChatServer
    {
        // Обязательные события по заданию
        public event Action<string> OnClientConnected;          // ник подключившегося
        public event Action<string> OnClientDisconnected;       // ник отключившегося
        public event Action<string, string> OnMessageReceived;  // отправитель, текст

        // Дополнительные события для интерфейса сервера
        public event Action<string> OnLog;       // строка лога
        public event Action<int> OnCountChanged; // количество клиентов

        // Ограничения протокола
        public const int MaxNickLength = 20;
        public const int MaxMessageLength = 1000;

        private readonly int _port;
        private TcpListener _listener;
        private volatile bool _running;

        // Словарь «никнейм -> подключение», общий для всех потоков
        private readonly Dictionary<string, ClientConnection> _clients = new Dictionary<string, ClientConnection>();
        // Объект блокировки для синхронизации доступа к словарю
        private readonly object _lock = new object();

        public ChatServer(int port)
        {
            _port = port;
        }

        public bool IsRunning => _running;

        // Количество подключённых клиентов (читаем под lock)
        public int Count
        {
            get { lock (_lock) return _clients.Count; }
        }

        // Список никнеймов (копия, чтобы не держать lock снаружи)
        public List<string> GetUsers()
        {
            lock (_lock) return _clients.Keys.OrderBy(k => k).ToList();
        }

        // Запуск сервера. Сам TcpListener запускается сразу (если порт занят — будет исключение),
        // а цикл приёма подключений работает в отдельном фоновом потоке
        public void Start()
        {
            _listener = new TcpListener(IPAddress.Any, _port);
            _listener.Start();
            _running = true;
            Log("СТАРТ", $"Сервер запущен на порту {_port}");

            var acceptThread = new Thread(AcceptLoop) { IsBackground = true, Name = "AcceptLoop" };
            acceptThread.Start();
        }

        // Цикл приёма подключений: на каждого клиента — отдельный поток
        private void AcceptLoop()
        {
            while (_running)
            {
                try
                {
                    TcpClient client = _listener.AcceptTcpClient();
                    var t = new Thread(() => HandleClient(client)) { IsBackground = true };
                    t.Start();
                }
                catch (SocketException)
                {
                    // Listener остановлен методом Stop() — выходим из цикла
                    break;
                }
                catch (ObjectDisposedException)
                {
                    break;
                }
            }
        }

        // Остановка сервера: закрываем listener и все клиентские соединения
        public void Stop()
        {
            if (!_running) return;
            _running = false;
            _listener?.Stop();

            List<ClientConnection> all;
            lock (_lock)
            {
                all = _clients.Values.ToList();
                _clients.Clear();
            }
            foreach (var c in all)
            {
                try { c.Writer.WriteLine("SYS:Сервер остановлен"); } catch { }
                try { c.Tcp.Close(); } catch { }
            }
            OnCountChanged?.Invoke(0);
            Log("СТОП", "Сервер остановлен");
        }

        // Обработчик одного клиента (выполняется в отдельном потоке)
        private void HandleClient(TcpClient tcp)
        {
            string nick = null;
            try
            {
                var stream = tcp.GetStream();
                var reader = new StreamReader(stream);
                var writer = new StreamWriter(stream) { AutoFlush = true };

                // Первая строка должна быть командой /join
                string first = reader.ReadLine();
                if (first == null || !first.StartsWith("/join "))
                {
                    writer.WriteLine("ERR:Первой должна быть команда /join ник");
                    return;
                }

                string candidate = first.Substring(6).Trim();
                string error = ValidateNick(candidate);
                if (error != null)
                {
                    writer.WriteLine("ERR:" + error);
                    return;
                }

                // Добавляем клиента в словарь под защитой lock
                lock (_lock)
                {
                    if (_clients.ContainsKey(candidate))
                    {
                        writer.WriteLine("ERR:Ник занят, выберите другой");
                        return;
                    }
                    _clients[candidate] = new ClientConnection { Tcp = tcp, Writer = writer };
                }
                nick = candidate;

                Log("ВХОД", nick);
                OnClientConnected?.Invoke(nick);
                OnCountChanged?.Invoke(Count);

                Broadcast($"SYS:{nick} вошёл в чат", null);
                BroadcastUsers();

                // Основной цикл чтения строк от клиента
                string line;
                while ((line = reader.ReadLine()) != null)
                {
                    if (string.IsNullOrWhiteSpace(line)) continue; // пустые строки игнорируем

                    if (line.StartsWith("/"))
                        HandleCommand(nick, line);
                    else
                        HandleMessage(nick, line);
                }
            }
            catch (IOException)
            {
                // Клиент оборвал соединение (закрыл программу без команды) — это нормальная ситуация
            }
            catch (ObjectDisposedException)
            {
            }
            finally
            {
                if (nick != null)
                {
                    bool removed;
                    lock (_lock) removed = _clients.Remove(nick);

                    // Если сервер уже остановлен, словарь очищен в Stop() — повторно не уведомляем
                    if (removed)
                    {
                        Log("ВЫХОД", nick);
                        OnClientDisconnected?.Invoke(nick);
                        OnCountChanged?.Invoke(Count);
                        Broadcast($"SYS:{nick} покинул чат", null);
                        BroadcastUsers();
                    }
                }
                tcp.Close();
            }
        }

        // Обычное сообщение — рассылаем всем
        private void HandleMessage(string nick, string text)
        {
            if (text.Length > MaxMessageLength)
                text = text.Substring(0, MaxMessageLength) + "…";

            Log("СООБЩЕНИЕ", $"{nick}: {text}");
            OnMessageReceived?.Invoke(nick, text);
            Broadcast($"MSG:{nick}:{text}", null);
        }

        // Обработка команд, начинающихся с «/»
        private void HandleCommand(string nick, string line)
        {
            if (line == "/users")
            {
                SendTo(nick, "USERS:" + string.Join(",", GetUsers()));
                return;
            }

            if (line.StartsWith("/pm "))
            {
                // Формат: /pm ник текст
                string rest = line.Substring(4).Trim();
                int space = rest.IndexOf(' ');
                if (space <= 0)
                {
                    SendTo(nick, "SYS:Формат личного сообщения: /pm ник текст");
                    return;
                }
                string to = rest.Substring(0, space);
                string text = rest.Substring(space + 1).Trim();
                if (text.Length > MaxMessageLength)
                    text = text.Substring(0, MaxMessageLength) + "…";

                if (to == nick)
                {
                    SendTo(nick, "SYS:Нельзя отправить личное сообщение самому себе");
                }
                else if (SendTo(to, $"PM:{nick}:{text}"))
                {
                    SendTo(nick, $"PMTO:{to}:{text}");
                    Log("ЛИЧНОЕ", $"{nick} -> {to}: {text}");
                }
                else
                {
                    SendTo(nick, $"SYS:Пользователь {to} не найден");
                }
                return;
            }

            SendTo(nick, "SYS:Неизвестная команда. Доступно: /users, /pm ник текст");
        }

        // Проверка никнейма: не пустой, без пробелов и служебных символов протокола
        private static string ValidateNick(string nick)
        {
            if (string.IsNullOrWhiteSpace(nick)) return "Ник не может быть пустым";
            if (nick.Length > MaxNickLength) return $"Ник длиннее {MaxNickLength} символов";
            if (nick.IndexOfAny(new[] { ' ', ':', ',', '/' }) >= 0) return "Ник не должен содержать пробелы и символы : , /";
            return null;
        }

        // Отправка строки одному клиенту. Возвращает false, если такого клиента нет
        private bool SendTo(string nick, string msg)
        {
            lock (_lock)
            {
                if (!_clients.TryGetValue(nick, out var c)) return false;
                try { c.Writer.WriteLine(msg); return true; }
                catch { return false; }
            }
        }

        // Рассылка всем клиентам (кроме skip). Весь перебор — под lock
        private void Broadcast(string msg, string skip)
        {
            lock (_lock)
            {
                foreach (var kv in _clients)
                {
                    if (kv.Key == skip) continue;
                    try { kv.Value.Writer.WriteLine(msg); }
                    catch
                    {
                        // Ошибка записи значит, что клиент уже отключился.
                        // Удалять его здесь не нужно: его поток HandleClient получит ошибку чтения
                        // и сам корректно удалит клиента и уведомит остальных
                    }
                }
            }
        }

        // Рассылка актуального списка пользователей всем клиентам
        private void BroadcastUsers()
        {
            Broadcast("USERS:" + string.Join(",", GetUsers()), null);
        }

        // Запись в лог: время, тип события, текст
        private void Log(string type, string text) =>
            OnLog?.Invoke($"[{DateTime.Now:HH:mm:ss}] {type,-10} {text}");
    }
}
