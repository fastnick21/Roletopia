using System;
using System.Collections.Concurrent;

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
        private readonly ConcurrentQueue<SyncPacket> _outboundQueue = new ConcurrentQueue<SyncPacket>();

        public NetworkingService(INetworkTransport transport)
        {
            _transport = transport ?? throw new ArgumentNullException(nameof(transport));
        }

        public int PendingPacketCount => _outboundQueue.Count;

        public bool QueueStateSync(string payload)
        {
            if (payload == null) return false;
            _outboundQueue.Enqueue(new SyncPacket { Type = "state-sync", Payload = payload });
            return true;
        }

        public bool QueuePacket(string type, string payload)
        {
            if (string.IsNullOrWhiteSpace(type) || payload == null) return false;
            _outboundQueue.Enqueue(new SyncPacket { Type = type.Trim(), Payload = payload });
            return true;
        }

        public int FlushBroadcastQueue()
        {
            var sent = 0;
            while (_outboundQueue.TryDequeue(out var packet))
            {
                _transport.Broadcast(packet);
                sent++;
            }
            return sent;
        }
    }
}
