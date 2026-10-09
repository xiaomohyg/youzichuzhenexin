using System;
using System.Collections.Generic;
using UnityEngine;

namespace M0.Networking
{
    [Serializable] public struct PlayerCommand
    {
        public uint Tick;
        public int PlayerId;
        public Vector2 Move;
        public Vector2 Look;
        public bool Attack, Heavy, Block, Dodge, Crouch;
        public int SelectedSlot;
    }
    [Serializable] public struct ActorSnapshot
    {
        public uint Tick;
        public int ActorId;
        public Vector3 Position;
        public Quaternion Rotation;
        public float Health;
        public bool Crouching;
        public int SelectedSlot;
        public uint InventoryRevision;
    }
    [Serializable] public struct HitMessage
    {
        public uint Tick;
        public int TargetId;
        public float Damage;
        public HitKind Kind;
    }
    // M2 integration region: replace the local adapter with NGO RPC/NetworkVariable transport.
    // Remote clients must send commands, never authoritative damage or positions.
    public interface IM0Transport : IDisposable
    {
        bool IsAuthority { get; }
        event Action<PlayerCommand> CommandReceived;
        event Action<ActorSnapshot> SnapshotReceived;
        event Action<HitMessage> HitReceived;
        void SendCommand(PlayerCommand command);
        void PublishSnapshot(ActorSnapshot snapshot);
        void PublishHit(HitMessage hit);
        void Pump();
    }
    // Offline authority with queued delivery; supports a smoke test without a network dependency.
    public sealed class LocalTransport : IM0Transport
    {
        readonly Queue<PlayerCommand> commands = new Queue<PlayerCommand>();
        public bool IsAuthority => true;
        public event Action<PlayerCommand> CommandReceived;
        public event Action<ActorSnapshot> SnapshotReceived;
        public event Action<HitMessage> HitReceived;
        public void SendCommand(PlayerCommand command) => commands.Enqueue(command);
        public void PublishSnapshot(ActorSnapshot snapshot) => SnapshotReceived?.Invoke(snapshot);
        public void PublishHit(HitMessage hit) => HitReceived?.Invoke(hit);
        public void Pump() { while (commands.Count > 0) CommandReceived?.Invoke(commands.Dequeue()); }
        public void Dispose() { commands.Clear(); CommandReceived = null; SnapshotReceived = null; HitReceived = null; }
    }
}
