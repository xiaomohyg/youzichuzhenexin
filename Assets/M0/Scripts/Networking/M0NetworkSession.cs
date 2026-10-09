using UnityEngine;

namespace M0.Networking
{
    // Keep the MonoBehaviour in a matching file so saved scenes retain this component.
    public sealed class M0NetworkSession : MonoBehaviour
    {
        public IM0Transport Transport { get; private set; }
        public uint Tick { get; private set; }
        public int CommandsObserved { get; private set; }
        public int SnapshotsObserved { get; private set; }
        public int HitsObserved { get; private set; }
        void OnEnable()
        {
            Transport = new LocalTransport();
            Transport.CommandReceived += ObserveCommand;
            Transport.SnapshotReceived += ObserveSnapshot;
            CombatEvents.Hit += OnHit;
        }
        void ObserveCommand(PlayerCommand command) { CommandsObserved++; }
        void ObserveSnapshot(ActorSnapshot snapshot) { SnapshotsObserved++; }
        void OnHit(CombatFeedback feedback)
        {
            HitsObserved++;
            Transport.PublishHit(new HitMessage { Tick = Tick, TargetId = feedback.TargetId, Damage = feedback.Damage, Kind = feedback.Kind });
        }
        void FixedUpdate()
        {
            Tick++;
            var player = M0Sandbox.Instance?.Player;
            if (player == null) return;
            var input = M0Sandbox.Instance.Input;
            Transport.SendCommand(new PlayerCommand { Tick = Tick, PlayerId = 1, Move = input.Move, Block = input.Held(8), Attack = input.Held(7), Heavy = input.Held(9), Dodge = input.Held(6), Crouch = input.Held(12), SelectedSlot = M0Sandbox.Instance.Inventory.Selected, Look = player.LookAngles });
            Transport.Pump();
            if (Tick % 3 == 0)
            {
                Transport.PublishSnapshot(new ActorSnapshot { Tick = Tick, ActorId = 1, Position = player.transform.position, Rotation = player.transform.rotation, Health = player.Health, Crouching = player.Crouching, SelectedSlot = M0Sandbox.Instance.Inventory.Selected, InventoryRevision = M0Sandbox.Instance.Inventory.Revision });
                foreach (var enemy in M0Sandbox.Instance.Enemies)
                    if (enemy.gameObject.activeSelf) Transport.PublishSnapshot(new ActorSnapshot { Tick = Tick, ActorId = enemy.TargetId, Position = enemy.transform.position, Rotation = enemy.transform.rotation, Health = enemy.Health });
            }
        }
        void OnDisable() { CombatEvents.Hit -= OnHit; Transport?.Dispose(); Transport = null; }
    }
}
