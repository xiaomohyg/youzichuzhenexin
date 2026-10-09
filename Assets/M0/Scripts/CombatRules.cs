using System;

namespace M0
{
    // Pure C#: authority can evaluate the same rules without camera, animation or Unity objects.
    public enum HitKind { Body, Weakpoint, Block, PerfectBlock, Evade }
    public readonly struct DamageResult
    {
        public readonly float Damage;
        public readonly HitKind Kind;
        public readonly bool Killed;
        public DamageResult(float damage, HitKind kind, bool killed) { Damage = damage; Kind = kind; Killed = killed; }
    }
    public static class CombatRules
    {
        public static DamageResult Resolve(float health, float damage, bool weakpoint, bool invulnerable,
            bool facing, float blockAge, float windup, float perfectWindow, float reduction)
        {
            if (invulnerable) return new DamageResult(0, HitKind.Evade, false);
            if (facing && blockAge >= windup)
            {
                if (blockAge <= windup + perfectWindow) return new DamageResult(0, HitKind.PerfectBlock, false);
                damage *= 1 - Math.Clamp(reduction, 0, 1);
                return new DamageResult(damage, HitKind.Block, damage >= health);
            }
            return new DamageResult(weakpoint ? health : damage, weakpoint ? HitKind.Weakpoint : HitKind.Body, weakpoint || damage >= health);
        }
    }
    public interface ICombatTarget { int TargetId { get; } void ApplyHit(float damage, bool weakpoint, UnityEngine.Vector3 direction, float knockback); }
    public readonly struct CombatFeedback
    {
        public readonly UnityEngine.Vector3 Position;
        public readonly float Damage;
        public readonly HitKind Kind;
        public readonly int TargetId;
        public CombatFeedback(UnityEngine.Vector3 position, float damage, HitKind kind, int targetId = 0) { Position = position; Damage = damage; Kind = kind; TargetId = targetId; }
    }
    public static class CombatEvents
    {
        public static event Action<CombatFeedback> Hit;
        public static void Publish(CombatFeedback feedback) => Hit?.Invoke(feedback);
    }
}
