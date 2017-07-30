/* (c) 2017 James Renwick */
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace cci
{
    public class Connection
    {
        private Socket socket;
        private BuildServer server;

        private Task handler;

        public Connection(BuildServer server, Socket socket)
        {
            this.server = server;
            this.socket = socket;
            this.handler = Task.Run((Action)handleConnection);
        }

        private async void handleConnection()
        {
            var array = new byte[1024];
            ArraySegment<byte> buffer = new ArraySegment<byte>(array);

            while (true)
            {
                int len = await socket.ReceiveAsync(buffer, SocketFlags.None);
                if (len == 0) return;

                var request = JsonConvert.DeserializeObject<Request>(
                    System.Text.ASCIIEncoding.ASCII.GetString(buffer.Array, 0, len));

                request.Sender = this;
                server.NotifyRequest(request);
            }
        }

        public void SendResponse(string data)
        {
            var buffer = System.Text.ASCIIEncoding.ASCII.GetBytes(data);
            this.socket.Send(buffer, 0, buffer.Length, SocketFlags.None);
        }
    }

    public class Request
    {
        [JsonProperty, JsonRequired]
        public string Action { get; private set; }
        [JsonProperty]
        public int BuildReference { get; private set; }
        [JsonProperty]
        public string TaskName { get; private set; }
        [JsonIgnore]
        public Connection Sender { get; internal set; }
    }

    public class RequestReceivedEventArgs : EventArgs
    {
        public Request Request { get; private set; }
        public RequestReceivedEventArgs(Request request)
        {
            Request = request;
        }

        public void RespondWith<Response>(Response response)
        {
            Request.Sender.SendResponse(JsonConvert.SerializeObject(response));
        }
    }

    public class BuildServer : IDisposable
    {
        private bool running;
        private Task acceptTask;
        private Task[] requestTasks;
        private Socket listener;
        private AutoResetEvent queueCV = new AutoResetEvent(false);
        private List<Connection> connections = new List<Connection>();
        private Queue<Request> requests = new Queue<Request>();

        public int MaxConnections { get; private set; }

        public event EventHandler<RequestReceivedEventArgs> RequestReceived;

        public BuildServer()
        {
            this.listener = new Socket(AddressFamily.InterNetwork,
                SocketType.Stream, ProtocolType.Tcp);
        }

        public void Start(EndPoint endpoint, int maxConnections, int connectBacklog)
        {
            this.MaxConnections = maxConnections;
            this.listener.Bind(endpoint);
            this.listener.Listen(connectBacklog);

            this.running = true;
            this.acceptTask = Task.Run((Action)this.AcceptIncoming);

            this.requestTasks = new Task[maxConnections];
            for (int i = 0; i < maxConnections; i++) {
                this.requestTasks[i] = Task.Run((Action)this.RequestHandler);
            }
        }

        public void Stop()
        {
            this.running = false;
            try { 
                this.listener.Shutdown(SocketShutdown.Both);
            } catch { }
            try {
                Task.WaitAll(this.requestTasks, 3000);
            } catch { }
            try {
                this.acceptTask.Wait(3000);
            } catch { }
        }

        private void AcceptIncoming()
        {
            while (running)
            {
                var socket = this.listener.Accept();
                this.connections.Add(new Connection(this, socket));

                // TODO: Condition variable!
                while (this.connections.Count >= this.MaxConnections) { }
            }
        }

        internal void NotifyRequest(Request req)
        {
            lock (queueCV)
            {
                this.requests.Enqueue(req);
                queueCV.Set();
            }
        }

        private void RequestHandler()
        {
            while (running)
            {
                if (this.requests.Count != 0)
                {
                    Request request = null;
                    lock (queueCV)
                    {
                        if (this.requests.Count == 0) continue;
                        request = this.requests.Dequeue();
                    }

                    if (request != null && this.RequestReceived != null)
                    {
                        try
                        {
                            this.RequestReceived(this,
                                new RequestReceivedEventArgs(request));
                        }
                        catch { }
                    }
                }
                else queueCV.WaitOne();
            }
        }

        public void Dispose()
        {
            this.listener.Dispose();
        }
    }
}
