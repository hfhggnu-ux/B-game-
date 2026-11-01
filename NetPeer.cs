using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;

namespace TomYum
{
    public enum NetState { Idle = 0, Listening = 1, Connecting = 2, Connected = 3, Failed = 4 }

    /// <summary>
    /// Tiny peer-to-peer link over plain TCP. One player "hosts" (listens on a port),
    /// the others connect straight to the host's IP. No relay or game server is involved.
    /// Messages are single text lines.
    /// </summary>
    public sealed class NetPeer
    {
        public struct Msg { public int from; public string text; }

        sealed class Conn
        {
            public int id;
            public TcpClient tcp;
            public NetworkStream ns;
            public readonly object wlock = new object();
            public volatile bool alive = true;
            public string addr = "";
        }

        public bool IsHost { get; private set; }
        public string Error = "";
        public NetState State { get { return (NetState)state; } }

        readonly Dictionary<int, Conn> conns = new Dictionary<int, Conn>();
        readonly object clock = new object();
        readonly ConcurrentQueue<Msg> inbox = new ConcurrentQueue<Msg>();
        readonly ConcurrentQueue<int> joined = new ConcurrentQueue<int>();
        readonly ConcurrentQueue<int> left = new ConcurrentQueue<int>();
        TcpListener listener;
        volatile bool running;
        int nextId = 1;
        int state;

        public bool StartHost(int port)
        {
            try
            {
                IsHost = true;
                listener = new TcpListener(IPAddress.Any, port);
                listener.Start();
                running = true;
                state = (int)NetState.Listening;
                var t = new Thread(AcceptLoop) { IsBackground = true };
                t.Start();
                return true;
            }
            catch (Exception e)
            {
                Error = e.Message;
                state = (int)NetState.Failed;
                return false;
            }
        }

        void AcceptLoop()
        {
            while (running)
            {
                try
                {
                    TcpClient c = listener.AcceptTcpClient();
                    c.NoDelay = true;
                    var conn = new Conn { tcp = c, ns = c.GetStream() };
                    try { conn.addr = c.Client.RemoteEndPoint.ToString(); } catch (Exception) { }
                    lock (clock)
                    {
                        conn.id = nextId++;
                        conns[conn.id] = conn;
                    }
                    joined.Enqueue(conn.id);
                    var rt = new Thread(() => ReadLoop(conn)) { IsBackground = true };
                    rt.Start();
                }
                catch (Exception)
                {
                    if (!running) break;
                }
            }
        }

        public void Connect(string host, int port)
        {
            IsHost = false;
            running = true;
            state = (int)NetState.Connecting;
            var t = new Thread(() =>
            {
                try
                {
                    var c = new TcpClient();
                    c.NoDelay = true;
                    IAsyncResult ar = c.BeginConnect(host, port, null, null);
                    if (!ar.AsyncWaitHandle.WaitOne(6000))
                    {
                        try { c.Close(); } catch (Exception) { }
                        throw new Exception("Connection timed out");
                    }
                    c.EndConnect(ar);
                    var conn = new Conn { id = 0, tcp = c, ns = c.GetStream() };
                    try { conn.addr = c.Client.RemoteEndPoint.ToString(); } catch (Exception) { }
                    lock (clock) { conns[0] = conn; }
                    state = (int)NetState.Connected;
                    ReadLoop(conn);
                }
                catch (Exception e)
                {
                    Error = e.Message;
                    state = (int)NetState.Failed;
                }
            }) { IsBackground = true };
            t.Start();
        }

        void ReadLoop(Conn c)
        {
            try
            {
                using (var sr = new StreamReader(c.ns, new UTF8Encoding(false)))
                {
                    string line;
                    while (running && c.alive && (line = sr.ReadLine()) != null)
                        inbox.Enqueue(new Msg { from = c.id, text = line });
                }
            }
            catch (Exception) { }
            c.alive = false;
            lock (clock) { conns.Remove(c.id); }
            try { c.tcp.Close(); } catch (Exception) { }
            if (running) left.Enqueue(c.id);
        }

        public void Send(int id, string text)
        {
            Conn c;
            lock (clock) { if (!conns.TryGetValue(id, out c)) return; }
            Write(c, text);
        }

        public void Broadcast(string text)
        {
            List<Conn> l;
            lock (clock) { l = new List<Conn>(conns.Values); }
            foreach (var c in l) Write(c, text);
        }

        void Write(Conn c, string text)
        {
            if (!c.alive) return;
            try
            {
                byte[] b = Encoding.UTF8.GetBytes(text + "\n");
                lock (c.wlock) { c.ns.Write(b, 0, b.Length); }
            }
            catch (Exception)
            {
                c.alive = false;
                try { c.tcp.Close(); } catch (Exception) { }
            }
        }

        public bool TryGetMsg(out Msg m) { return inbox.TryDequeue(out m); }
        public bool TryJoined(out int id) { return joined.TryDequeue(out id); }
        public bool TryLeft(out int id) { return left.TryDequeue(out id); }

        public string Addr(int id)
        {
            lock (clock)
            {
                Conn c;
                return conns.TryGetValue(id, out c) ? c.addr : "";
            }
        }

        public void Stop()
        {
            running = false;
            try { if (listener != null) listener.Stop(); } catch (Exception) { }
            List<Conn> l;
            lock (clock) { l = new List<Conn>(conns.Values); conns.Clear(); }
            foreach (var c in l)
            {
                c.alive = false;
                try { c.tcp.Close(); } catch (Exception) { }
            }
            state = (int)NetState.Idle;
        }
    }
}
