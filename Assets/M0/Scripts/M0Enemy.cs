using System.Collections.Generic;
using UnityEngine;

namespace M0
{
    public enum EnemyState { Patrol, Chase, Windup, Recovery, Stagger, Dead, Attack }
    [RequireComponent(typeof(CharacterController))]
    public sealed class M0Enemy : MonoBehaviour, ICombatTarget
    {
        public int Id;
        public float MaxHealth = 80, Speed = 1.8f, SenseRange = 16, AttackRange = 1.9f, Damage = 14, TelegraphTime = 0.62f;
        public Transform Head, LeftArm, RightArm;
        public Renderer BodyRenderer;
        public float HitStopDuration = 0.083f;
        public M0EnemyRig Rig;
        public float AttackDuration = 0.46f, RecoveryDuration = 0.85f;
        public bool DodgeContactVerified { get; private set; }
        public Vector3 StrikePoint => Rig != null && Rig.StrikeShape != null ? Rig.StrikeShape.transform.position : transform.position + Vector3.up;
        public bool StrikeActive => State == EnemyState.Attack && AttackPhase >= 0.2f && AttackPhase <= 0.8f;
        public float AttackPhase => Mathf.Clamp01(1 - timer / Mathf.Max(0.01f, AttackDuration));
        public float Health { get; private set; }
        public bool Alive => gameObject.activeSelf && State != EnemyState.Dead;
        public int TargetId => Id;
        public EnemyState State { get; private set; }
        public bool CorpseSettled { get; private set; }
        public bool RagdollActive { get; private set; }
        CharacterController controller;
        Rigidbody[] bones;
        Collider[] boneColliders;
        Vector3[] restPositions;
        Quaternion[] restRotations;
        float timer, frozen, vertical, corpseAge, groundedTime;
        Vector3 spawn, knockVelocity, deathDirection, lastVelocity, planarVelocity, attackDirection;
        float lostSight, attackCooldown, strafeSign;
        float deathForce;
        MaterialPropertyBlock tint;
        bool strikeResolved;
        Vector3 previousStrike, previousPlayer;
        Quaternion previousStrikeRotation;
        float previousPhase;
        void Awake() { Initialize(); }
        void Initialize()
        {
            // Unity can deserialize an empty array during an Editor reload before Awake.
            if (controller != null && bones != null && bones.Length > 0 && tint != null) return;
            controller = GetComponent<CharacterController>(); bones = GetComponentsInChildren<Rigidbody>(true);
            var cols = new List<Collider>();
            foreach (var bone in bones) cols.Add(bone.GetComponent<Collider>());
            boneColliders = cols.ToArray(); restPositions = new Vector3[bones.Length]; restRotations = new Quaternion[bones.Length];
            for (int i = 0; i < bones.Length; i++) { restPositions[i] = bones[i].transform.localPosition; restRotations[i] = bones[i].transform.localRotation; }
            for (int i = 0; i < boneColliders.Length; i++)
            {
                Physics.IgnoreCollision(controller, boneColliders[i]);
                for (int j = i + 1; j < boneColliders.Length; j++) Physics.IgnoreCollision(boneColliders[i], boneColliders[j]);
            }
            tint = new MaterialPropertyBlock(); spawn = transform.position;
        }
        public void ResetEnemy(Vector3 position)
        {
            Initialize();
            controller.enabled = false;
            for (int i = 0; i < bones.Length; i++)
            {
                bones[i].isKinematic = true; bones[i].detectCollisions = true;
                bones[i].interpolation = RigidbodyInterpolation.None;
                bones[i].transform.localPosition = restPositions[i]; bones[i].transform.localRotation = restRotations[i];
                boneColliders[i].enabled = i == 0 || bones[i].transform == Head;
            }
            transform.SetPositionAndRotation(position, Quaternion.identity); spawn = position;
            controller.enabled = true; gameObject.SetActive(true);
            Health = MaxHealth; State = EnemyState.Patrol; timer = 0.8f; frozen = vertical = corpseAge = groundedTime = 0;
            knockVelocity = lastVelocity = planarVelocity = Vector3.zero; attackDirection = transform.forward;
            lostSight = attackCooldown = 0; strafeSign = Id % 2 == 0 ? 1 : -1;
            CorpseSettled = RagdollActive = false; SetTint(new Color(0.22f, 0.42f, 0.44f));
            strikeResolved = false; DodgeContactVerified = false; Rig?.ResetPose();
        }
        void Update()
        {
            if (State == EnemyState.Dead) { UpdateCorpse(); return; }
            var player = M0Sandbox.Instance?.Player;
            if (player == null || player.Health <= 0) return;
            if (frozen > 0) { frozen -= Time.deltaTime; return; }
            timer -= Time.deltaTime; attackCooldown = Mathf.Max(0, attackCooldown - Time.deltaTime);
            if (State == EnemyState.Stagger)
            {
                Rig?.Present(State, 0, 0);
                planarVelocity = Vector3.zero;
                Move(knockVelocity); knockVelocity = Vector3.MoveTowards(knockVelocity, Vector3.zero, Time.deltaTime * 9);
                if (timer <= 0) { State = EnemyState.Chase; SetTint(new Color(0.22f, 0.42f, 0.44f)); }
                return;
            }
            Vector3 delta = player.transform.position - transform.position; delta.y = 0;
            if (State == EnemyState.Windup)
            {
                float phase = 1 - timer / Mathf.Max(0.01f, TelegraphTime);
                if (phase < 0.58f && delta.sqrMagnitude > 0.001f)
                {
                    float tracking = Mathf.Lerp(150, 38, phase / 0.58f);
                    TurnTowards(delta, tracking);
                    attackDirection = transform.forward;
                }
                SetTint(Color.Lerp(new Color(0.55f, 0.18f, 0.08f), new Color(1, 0.12f, 0.04f), (Mathf.Sin(Time.time * 25) + 1) * 0.5f));
                Rig?.Present(State, phase, 0); MoveSteered(Vector3.zero, 8);
                if (timer <= 0)
                {
                    State = EnemyState.Attack; timer = AttackDuration; strikeResolved = false; previousPhase = 0;
                    attackDirection = transform.forward; planarVelocity = Vector3.zero;
                    previousStrike = Rig.StrikeShape.transform.position; previousStrikeRotation = Rig.StrikeShape.transform.rotation;
                    previousPlayer = player.transform.position;
                }
                return;
            }
            if (State == EnemyState.Attack)
            {
                // The planted strike keeps the late-windup direction, but eases into and out of its lunge.
                float lunge = Mathf.Sin(Mathf.Clamp01(Mathf.InverseLerp(0.06f, 0.78f, AttackPhase)) * Mathf.PI);
                MoveSteered(attackDirection * (2.25f * lunge), 24);
                Rig.Present(State, AttackPhase, 0);
                SweepStrike(player);
                if (State == EnemyState.Attack && timer <= 0)
                { State = EnemyState.Recovery; timer = RecoveryDuration; attackCooldown = 0.38f; SetTint(new Color(0.22f, 0.42f, 0.44f)); }
                return;
            }
            if (State == EnemyState.Recovery)
            {
                float phase = 1 - timer / Mathf.Max(0.01f, RecoveryDuration);
                Vector3 retreat = phase < 0.42f ? -attackDirection * 0.42f + transform.right * strafeSign * 0.22f : Vector3.zero;
                MoveSteered(retreat, 5); Rig?.Present(State, phase, planarVelocity.magnitude / Mathf.Max(0.1f, Speed));
                if (timer <= 0) State = EnemyState.Chase; return;
            }
            bool sensed = delta.magnitude < SenseRange && ClearPath(player);
            if (!sensed)
            {
                lostSight += Time.deltaTime;
                State = EnemyState.Patrol;
                Vector3 goal = spawn + new Vector3(Mathf.Sin(Time.time * 0.3f + Id) * 1.6f, 0, Mathf.Cos(Time.time * 0.3f + Id) * 1.6f);
                var patrol = goal - transform.position; patrol.y = 0;
                TurnTowards(patrol, 85); MoveSteered(patrol.sqrMagnitude > 0.04f ? patrol.normalized * Speed * 0.32f : Vector3.zero, 2.5f);
                Rig?.Present(State, 0, planarVelocity.magnitude / Mathf.Max(0.1f, Speed)); return;
            }
            lostSight = 0;
            State = EnemyState.Chase;
            float distance = delta.magnitude;
            if (delta.sqrMagnitude > 0.01f) TurnTowards(delta, distance < AttackRange * 1.4f ? 135 : 205);
            float facingAngle = delta.sqrMagnitude > 0.01f ? Vector3.Angle(transform.forward, delta) : 0;
            if (distance <= AttackRange && facingAngle < 24 && attackCooldown <= 0)
            {
                State = EnemyState.Windup; timer = TelegraphTime; attackDirection = transform.forward;
                planarVelocity *= 0.35f; M0Sandbox.Instance.Fx.PlayTone(210, 0.12f, 0.09f);
            }
            else
            {
                Vector3 separation = Vector3.zero;
                foreach (var other in M0Sandbox.Instance.Enemies)
                {
                    if (other == this || !other.Alive) continue;
                    var away = transform.position - other.transform.position; away.y = 0;
                    if (away.sqrMagnitude < 1.4f && away.sqrMagnitude > 0.01f) separation += away.normalized * 0.7f;
                }
                float approach = Mathf.InverseLerp(AttackRange * 0.82f, AttackRange * 2.25f, distance);
                Vector3 orbit = Vector3.Cross(Vector3.up, delta.normalized) * strafeSign * (distance < AttackRange * 1.35f ? 0.38f : 0.08f);
                Vector3 desired = delta.normalized * (Speed * Mathf.Lerp(0.38f, 1, approach)) + orbit + separation;
                MoveSteered(Vector3.ClampMagnitude(desired, Speed), 5.5f);
                Rig?.Present(State, 0, planarVelocity.magnitude / Mathf.Max(0.1f, Speed));
            }
        }
        void SweepStrike(M0Player player)
        {
            var shape = Rig.StrikeShape;
            Vector3 current = shape.transform.position;
            Quaternion rotation = shape.transform.rotation;
            float phase = AttackPhase;
            int samples = Mathf.Max(1, Mathf.CeilToInt(Vector3.Distance(previousStrike, current) / 0.045f),
                Mathf.CeilToInt(Quaternion.Angle(previousStrikeRotation, rotation) / 5));
            var capsule = player.GetComponent<CharacterController>();
            for (int i = 0; i <= samples && !strikeResolved; i++)
            {
                float t = (float)i / samples, samplePhase = Mathf.Lerp(previousPhase, phase, t);
                if (samplePhase < 0.2f || samplePhase > 0.8f) continue;
                Vector3 at = Vector3.Lerp(previousStrike, current, t);
                Quaternion facing = Quaternion.Slerp(previousStrikeRotation, rotation, t);
                Vector3 playerAt = Vector3.Lerp(previousPlayer, player.transform.position, t);
                if (Obstructed(at, playerAt + capsule.center)) continue;
                if (player.GuardIntersects(shape, at, facing))
                { strikeResolved = true; player.ReceiveAttack(Damage, this, true, at); }
                else if (Physics.ComputePenetration(shape, at, facing, capsule, playerAt, player.transform.rotation, out _, out _))
                { strikeResolved = true; player.ReceiveAttack(Damage, this, false, at); }
                else if (player.InvulnerableNow && Physics.ComputePenetration(shape, at, facing, capsule,
                    player.DodgeOrigin, player.transform.rotation, out _, out _) && !Obstructed(at, player.DodgeOrigin + capsule.center))
                {
                    // A dodge qualifies only if this actual swing intersects the capsule it escaped.
                    DodgeContactVerified = true; player.CheckPerfectDodge(this); DodgeContactVerified = false;
                }
            }
            previousStrike = current; previousStrikeRotation = rotation; previousPhase = phase; previousPlayer = player.transform.position;
        }
        bool Obstructed(Vector3 weapon, Vector3 victim)
        {
            Vector3 shoulder = Rig.RightShoulder.position;
            return SegmentBlocked(shoulder, weapon) || SegmentBlocked(weapon, victim);
        }
        bool SegmentBlocked(Vector3 a, Vector3 b)
        {
            Vector3 d = b - a;
            foreach (var hit in Physics.RaycastAll(a, d.normalized, d.magnitude, ~(1 << 2), QueryTriggerInteraction.Ignore))
                if (hit.collider.GetComponentInParent<M0Enemy>() != this) return true;
            return false;
        }
        bool ClearPath(M0Player player)
        {
            var origin = Head.position; var delta = player.EyePosition - origin;
            return !Physics.Raycast(origin + delta.normalized * 0.35f, delta.normalized, out var hit, Mathf.Max(0, delta.magnitude - 0.35f), ~0, QueryTriggerInteraction.Ignore) || hit.collider.GetComponentInParent<M0Player>() == player;
        }
        void Move(Vector3 velocity)
        {
            lastVelocity = velocity;
            if (controller.isGrounded && vertical < 0) vertical = -2;
            vertical -= 20 * Time.deltaTime;
            controller.Move((velocity + Vector3.up * vertical) * Time.deltaTime);
        }
        void MoveSteered(Vector3 desiredVelocity, float acceleration)
        {
            planarVelocity = Vector3.MoveTowards(planarVelocity, desiredVelocity, acceleration * Time.deltaTime);
            Move(planarVelocity);
        }
        void TurnTowards(Vector3 direction, float degreesPerSecond)
        {
            direction.y = 0;
            if (direction.sqrMagnitude < 0.001f) return;
            transform.rotation = Quaternion.RotateTowards(transform.rotation, Quaternion.LookRotation(direction), degreesPerSecond * Time.deltaTime);
        }
        public void ApplyHit(float damage, bool weakpoint, Vector3 direction, float knockback)
        {
            if (!Alive) return;
            var resolved = CombatRules.Resolve(Health, damage, weakpoint, false, false, -1, 0.1f, 0.13f, 0.9f);
            Health = Mathf.Max(0, Health - resolved.Damage);
            frozen = HitStopDuration;
            CombatEvents.Publish(new CombatFeedback(weakpoint ? Head.position : transform.position + Vector3.up, resolved.Damage, resolved.Kind, Id));
            if (resolved.Killed)
            {
                State = EnemyState.Dead; corpseAge = 0; deathDirection = direction; deathForce = knockback;
                if (Rig != null && Rig.StrikeTrail != null) Rig.StrikeTrail.emitting = false;
                M0Sandbox.Instance.EnemyKilled(this); SetTint(new Color(0.16f, 0.25f, 0.29f));
            }
            else { Stagger(0.28f); knockVelocity = direction.normalized * Mathf.Sqrt(18 * Mathf.Max(0, knockback)); SetTint(Color.white); }
        }
        public void Stagger(float duration)
        {
            if (!Alive) return; State = EnemyState.Stagger; timer = duration; SetTint(Color.white);
            if (Rig != null && Rig.StrikeTrail != null) Rig.StrikeTrail.emitting = false;
        }
        void UpdateCorpse()
        {
            if (!RagdollActive)
            {
                frozen -= Time.deltaTime; if (frozen > 0) return;
                controller.enabled = false; RagdollActive = true;
                for (int i = 0; i < bones.Length; i++)
                {
                    boneColliders[i].enabled = true; bones[i].isKinematic = false;
                    bones[i].interpolation = RigidbodyInterpolation.Interpolate;
                    bones[i].linearVelocity = lastVelocity + deathDirection.normalized * Mathf.Sqrt(18 * deathForce) + Vector3.up * 3;
                    bones[i].angularVelocity = new Vector3(1, 0.5f, 2);
                }
            }
            if (CorpseSettled) return;
            corpseAge += Time.deltaTime;
            bool touchingGround = false; float speed = 0;
            foreach (var bone in bones)
            {
                speed = Mathf.Max(speed, bone.linearVelocity.sqrMagnitude);
                if (Physics.Raycast(bone.position, Vector3.down, out var hit, 0.55f, ~0, QueryTriggerInteraction.Ignore) && hit.collider.GetComponentInParent<M0Enemy>() != this) touchingGround = true;
            }
            groundedTime = touchingGround ? groundedTime + Time.deltaTime : 0;
            if (groundedTime > 0.6f && corpseAge > 1.2f && (speed < 0.3f || corpseAge > 8))
            {
                foreach (var bone in bones) { bone.linearVelocity = Vector3.zero; bone.angularVelocity = Vector3.zero; bone.Sleep(); bone.isKinematic = true; }
                CorpseSettled = true; // Colliders remain solid: the player can stand on the corpse.
            }
            if (corpseAge > 12 && bones[0].position.y < -8) gameObject.SetActive(false);
        }
        void SetTint(Color color)
        {
            if (BodyRenderer == null) return;
            if (tint == null) tint = new MaterialPropertyBlock();
            tint.SetColor("_BaseColor", color); tint.SetColor("_Color", color); BodyRenderer.SetPropertyBlock(tint);
        }
    }
}
