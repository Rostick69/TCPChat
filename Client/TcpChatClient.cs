#nullable disable
using System;
using System.IO;
using System.Net.Sockets;
using System.Threading;

namespace Client
{
    public class TcpChatClient
    {
        public event Action<string, string> OnMessage;
        public event Action<string> OnSystem;
        public event Action<string[]> OnUsers;
        public event Action OnDisconnected;

        private TcpClient _tcp;
        private StreamWriter _writer;
        private bool _connected;

        public void Connect(string host, int port, string nick)
        {
            _tcp = new TcpClient();
            _tcp.Connect(host, port);

            var stream = _tcp.GetStream();
            _writer = new StreamWriter(stream) { AutoFlush = true };
            var reader = new StreamReader(stream);

            _connected = true;
            _writer.WriteLine($"/join {nick}");

            var t = new Thread(() =>
            {
                try
                {
                    string line;
                    while (_connected && (line = reader.ReadLine()) != null)
                        Parse(line);
                }
                catch { }
                finally
                {
                    if (_connected) { _connected = false; OnDisconnected?.Invoke(); }
                }
            })
            { IsBackground = true };
            t.Start();
        }

        public void Send(string text)
        {
            try { _writer?.WriteLine(text); }
            catch { }
        }

        public void Disconnect()
        {
            _connected = false;
            try { _tcp?.Close(); } catch { }
        }

        private void Parse(string line)
        {
            if (line.StartsWith("SYS:"))
                OnSystem?.Invoke(line.Substring(4));
            else if (line.StartsWith("USERS:"))
                OnUsers?.Invoke(line.Substring(6).Split(',', StringSplitOptions.RemoveEmptyEntries));
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