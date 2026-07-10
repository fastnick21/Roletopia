using System;
using System.Collections.Generic;

namespace Roletopia.Networking
{
    public sealed class SyncPacket
    {
        public string Type { get; set; }
        public string Payload { get; set; }
    }

    public interface INetworkTransport
    {
        void SendToClient(string clientId, SyncPacket packet);
        void Broadcast(SyncPacket packet);
    }

    public sealed class NetworkingService
    {
        private readonly INetworkTransport _transport;
        private readonly Queue<SyncPacket> _outboundQueue = new Queue<SyncPacket>();

        public NetworkingService(INetworkTransport transport)
        {
            _transport = transport ?? throw new ArgumentNullException(nameof(transport));
        }

        public void QueueStateSync(string payload)
        {
            _outboundQueue.Enqueue(new SyncPacket
            {
                Type = "state-sync",
                Payload = payload ?? string.Empty
            });
        }

        public void FlushBroadcastQueue()
        {
            while (_outboundQueue.Count > 0)
            {
                _transport.Broadcast(_outboundQueue.Dequeue());
            }
        }
    }
}
