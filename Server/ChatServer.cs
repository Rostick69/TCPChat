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
        public event Action<string> OnLog;
        public event Action<int> OnCountChanged;

        private readonly int _port;
        private TcpListener _listener;
        private bool _running;
        private readonly Dictionary<string, StreamWriter> _clients = new();
        private readonly object _lock = new object();

        public int Count => _clients.Count;

        public ChatServer(int port)
        {
            _port = port;
        }

        public void Start()
        {
            _running = true;
            _listener = new TcpListener(IPAddress.Any, _port);
            _listener.Start();
            Log($"Сервер запущен на порту {_port}");

            while (_running)
            {
                try
                {
                    var client = _listener.AcceptTcpClient();
                    var t = new Thread(() => Handle(client)) { IsBackground = true };
                    t.Start();
                }
                catch { break; }
            }
        }

        public void Stop()
        {
            _running = false;
            _listener?.Stop();
            Log("Сервер остановлен");
        }

        private void Handle(TcpClient tcp)
        {
            string nick = null;
            StreamWriter writer = null;

            try
            {
                var stream = tcp.GetStream();
                var reader = new StreamReader(stream);
                writer = new StreamWriter(stream) { AutoFlush = true };

                string first = reader.ReadLine();
                if (first == null || !first.StartsWith("/join ")) return;

                nick = first.Substring(6).Trim();
                if (string.IsNullOrEmpty(nick)) return;

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
                Broadcast($"SYS:{nick} вошёл в чат", null);
                SendUsers(nick);

                string line;
                while ((line = reader.ReadLine()) != null)
                {
                    if (string.IsNullOrWhiteSpace(line)) continue;
                    Log($"{nick}: {line}");
                    Broadcast($"MSG:{nick}:{line}", null);
                }
            }
            catch { }
            finally
            {
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
                foreach (var k in dead) _clients.Remove(k);
            }
        }

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

        private void Log(string msg) =>
            OnLog?.Invoke($"[{DateTime.Now:HH:mm:ss}] {msg}");
    }
}