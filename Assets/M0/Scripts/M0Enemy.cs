using System.Collections.Generic;
using UnityEngine;

namespace M0
{
    public enum EnemyState { Patrol, Chase, Windup, Leap, Recovery, Stagger, Dead, Attack, Slam }
    [RequireComponent(typeof(CharacterController))]
    public sealed class M0Enemy : MonoBehaviour, ICombatTarget
    {
        static readonly RaycastHit[] ObstructionHits = new RaycastHit[32];
        public int Id;
        public float MaxHealth = 80, Speed = 1.8f, SenseRange = 28, AttackRange = 2.0f, LockRange = 8.0f, Damage = 14, TelegraphTime = 0.62f;
        public Transform Head, LeftArm, RightArm;
        public Renderer BodyRenderer;
        public float HitStopDuration = 0.083f;
        public M0EnemyRig Rig;
        public float AttackDuration = 0.62f, DashDuration = 0.19f, DashSpeed = 4.8f, RecoveryDuration = 0.85f;
        [Header("Attack Variation")]
        public Vector2 WindupRange = new Vector2(0.65f, 1.2f), DashSpeedRange = new Vector2(24f, 32f);
        [Range(0f, 1f)] public float SlamAttackChance = 0.68f;
        public float LeapDuration = 0.58f, LeapHeight = 3.2f, LeapSpeed = 6.5f, SlamSpeed = 36f, SlamImpactRadius = 1.6f;
        public bool DodgeContactVerified { get; private set; }
        public Vector3 StrikePoint => Rig != null && Rig.StrikeShape != null ? Rig.StrikeShape.transform.position : transform.position + Vector3.up;
        public bool StrikeActive => (State == EnemyState.Attack || State == EnemyState.Slam) && AttackPhase >= DashDuration / AttackDuration + 0.04f && AttackPhase <= 0.92f;
        public bool AttackFlashActive => Rig != null && Rig.TelegraphFlashActive;
        public float EffectiveLockRange => Mathf.Max(AttackRange, LockRange > 0.1f ? LockRange : 8f);
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
        float attackCooldown, strafeSign;
        float deathForce;
        MaterialPropertyBlock tint;
        bool strikeResolved, attackCueFlashed;
        bool slamAttack;
        Vector3 initialLockPosition, slamLockPosition, leapDirection;
        Vector3 previousStrike, previousPlayer;
        Vector3 observedPlayerPosition, playerVelocity;
        bool hasObservedPlayer;
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
            transform.SetPositionAndRotation(position, Quaternion.identity); spawn = position;
            for (int i = 0; i < bones.Length; i++)
            {
                if (!bones[i].isKinematic)
                {
                    bones[i].linearVelocity = Vector3.zero; bones[i].angularVelocity = Vector3.zero;
                    bones[i].isKinematic = true;
                }
                bones[i].detectCollisions = true;
                bones[i].interpolation = RigidbodyInterpolation.None;
                bones[i].transform.localPosition = restPositions[i]; bones[i].transform.localRotation = restRotations[i];
                bones[i].position = bones[i].transform.position; bones[i].rotation = bones[i].transform.rotation;
                boneColliders[i].enabled = i == 0 || bones[i].transform == Head;
            }
            Physics.SyncTransforms();
            controller.enabled = true; gameObject.SetActive(true);
            Health = MaxHealth; State = EnemyState.Patrol; timer = 0.8f; frozen = vertical = corpseAge = groundedTime = 0;
            knockVelocity = lastVelocity = planarVelocity = Vector3.zero; attackDirection = transform.forward;
            attackCooldown = 0; strafeSign = Id % 2 == 0 ? 1 : -1;
            observedPlayerPosition = M0Sandbox.Instance != null && M0Sandbox.Instance.Player != null
                ? M0Sandbox.Instance.Player.transform.position : Vector3.zero;
            playerVelocity = Vector3.zero; hasObservedPlayer = true;
            CorpseSettled = RagdollActive = false; SetTint(new Color(0.22f, 0.42f, 0.44f));
            strikeResolved = attackCueFlashed = slamAttack = false; DodgeContactVerified = false; Rig?.ResetPose();
        }
        void Update()
        {
            if (State == EnemyState.Dead) { Rig?.SetTelegraph(-1); UpdateCorpse(); return; }
            var player = M0Sandbox.Instance?.Player;
            if (player == null || player.Health <= 0) return;
            if (hasObservedPlayer && Time.deltaTime > 0.0001f)
                playerVelocity = Vector3.ClampMagnitude(Vector3.ProjectOnPlane((player.transform.position - observedPlayerPosition) / Time.deltaTime, Vector3.up), 12f);
            observedPlayerPosition = player.transform.position; hasObservedPlayer = true;
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
                if (phase < 0.72f && delta.sqrMagnitude > 0.001f)
                {
                    float tracking = Mathf.Lerp(210, 62, phase / 0.72f);
                    TurnTowards(delta, tracking);
                    attackDirection = transform.forward;
                }
                float weave = Mathf.Sin(Time.time * 5.3f + Id * 1.7f);
                Vector3 windupDrift = transform.right * (strafeSign * 1.25f + weave * 0.35f);
                Rig?.SetTelegraph(phase * 0.82f); Rig?.Present(State, phase, 0.55f); MoveSteered(windupDrift, 9);
                if (timer <= 0)
                {
                    if (slamAttack)
                    {
                        initialLockPosition = player.transform.position;
                        leapDirection = Vector3.ProjectOnPlane(initialLockPosition + playerVelocity * 0.22f - transform.position, Vector3.up).normalized;
                        if (leapDirection.sqrMagnitude < 0.01f) leapDirection = transform.forward;
                        State = EnemyState.Leap; timer = LeapDuration;
                        vertical = Mathf.Sqrt(2f * 20f * LeapHeight);
                        planarVelocity = leapDirection * LeapSpeed;
                        M0Sandbox.Instance.Fx.PlayTone(360, 0.16f, 0.16f);
                    }
                    else BeginDash(player);
                }
                return;
            }
            if (State == EnemyState.Leap)
            {
                float phase = 1f - timer / Mathf.Max(0.01f, LeapDuration);
                Rig?.Present(State, phase, 0);
                Vector3 lockPosition = player.transform.position + playerVelocity * Mathf.Clamp(timer * 0.45f, 0.05f, 0.3f);
                slamLockPosition = lockPosition;
                attackDirection = Vector3.RotateTowards(attackDirection, (lockPosition - transform.position).normalized, 8f * Time.deltaTime, 0).normalized;
                planarVelocity = Vector3.MoveTowards(planarVelocity, leapDirection * LeapSpeed, 12f * Time.deltaTime);
                Move(planarVelocity);
                if (timer <= 0)
                {
                    BeginSlam(player);
                }
                return;
            }
            if (State == EnemyState.Attack || State == EnemyState.Slam)
            {
                float elapsed = AttackDuration - timer;
                Rig.Present(State, AttackPhase, 0);
                if (elapsed < DashDuration)
                {
                    if (State == EnemyState.Slam)
                    {
                        Vector3 target = player.transform.position + Vector3.up * 0.7f + playerVelocity * Mathf.Clamp(Vector3.Distance(transform.position, player.transform.position) / SlamSpeed, 0f, 0.22f);
                        SteerAttack(target - transform.position, 9f);
                        MoveDirect(attackDirection * SlamSpeed);
                    }
                    else
                    {
                        Vector3 target = player.transform.position + playerVelocity * Mathf.Clamp(Vector3.Distance(transform.position, player.transform.position) / DashSpeed, 0f, 0.32f);
                        SteerAttack(Vector3.ProjectOnPlane(target - transform.position, Vector3.up), 13f);
                        Move(attackDirection * DashSpeed);
                    }
                    Rig.SetTelegraph(Mathf.Lerp(0.82f, 0.995f, elapsed / Mathf.Max(0.01f, DashDuration)));
                }
                else
                {
                    if (State == EnemyState.Slam) MoveDirect(Vector3.zero);
                    else MoveSteered(Vector3.zero, 32);
                    if (!attackCueFlashed)
                    {
                        attackCueFlashed = true;
                        Rig.FlashTelegraph();
                        M0Sandbox.Instance.Fx.Sparks(StrikePoint, Color.white, 24, -attackDirection, 1.35f);
                        M0Sandbox.Instance.Fx.PlayTone(1120, 0.10f, 0.22f);
                    }
                }
                SweepStrike(player);
                if (State == EnemyState.Slam && (elapsed >= DashDuration || controller.isGrounded) && !strikeResolved)
                    ResolveSlamImpact(player);
                if ((State == EnemyState.Attack || State == EnemyState.Slam) && timer <= 0)
                { State = EnemyState.Recovery; timer = RecoveryDuration; attackCooldown = 0.38f; SetTint(new Color(0.22f, 0.42f, 0.44f)); }
                return;
            }
            if (State == EnemyState.Recovery)
            {
                float phase = 1 - timer / Mathf.Max(0.01f, RecoveryDuration);
                Vector3 toPlayer = Vector3.ProjectOnPlane(player.transform.position - transform.position, Vector3.up).normalized;
                Vector3 orbit = Vector3.Cross(Vector3.up, toPlayer) * strafeSign;
                Vector3 retreat = -toPlayer * Mathf.Lerp(0.7f, 0.2f, phase) + orbit * Mathf.Lerp(1.2f, 0.85f, phase);
                MoveSteered(retreat, 5); Rig?.Present(State, phase, planarVelocity.magnitude / Mathf.Max(0.1f, Speed));
                if (timer <= 0) State = EnemyState.Chase; return;
            }
            bool sensed = delta.magnitude < Mathf.Max(SenseRange, 28f) && ClearPath(player);
            if (!sensed)
            {
                State = EnemyState.Patrol;
                Vector3 goal = spawn + new Vector3(Mathf.Sin(Time.time * 0.3f + Id) * 1.6f, 0, Mathf.Cos(Time.time * 0.3f + Id) * 1.6f);
                var patrol = goal - transform.position; patrol.y = 0;
                TurnTowards(patrol, 85); MoveSteered(patrol.sqrMagnitude > 0.04f ? patrol.normalized * Speed * 0.32f : Vector3.zero, 2.5f);
                Rig?.Present(State, 0, planarVelocity.magnitude / Mathf.Max(0.1f, Speed)); return;
            }
            State = EnemyState.Chase;
            float distance = delta.magnitude;
            float lockRange = EffectiveLockRange;
            if (delta.sqrMagnitude > 0.01f) TurnTowards(delta, distance < lockRange * 1.4f ? 135 : 205);
            float facingAngle = delta.sqrMagnitude > 0.01f ? Vector3.Angle(transform.forward, delta) : 0;
            if (distance <= lockRange && facingAngle < 24 && attackCooldown <= 0)
            {
                slamAttack = Random.value < SlamAttackChance;
                TelegraphTime = Random.Range(WindupRange.x, WindupRange.y);
                DashSpeed = Random.Range(DashSpeedRange.x, DashSpeedRange.y);
                DashDuration = Random.Range(0.28f, 0.40f);
                AttackDuration = DashDuration + Random.Range(0.38f, 0.52f);
                Damage = Random.Range(12f, 21f);
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
                    if (away.sqrMagnitude < 1.4f)
                    {
                        if (away.sqrMagnitude < 0.01f) away = transform.right * (Id < other.Id ? -1 : 1);
                        separation += away.normalized * 0.7f;
                    }
                }
                float approach = Mathf.InverseLerp(lockRange * 0.82f, lockRange * 2.25f, distance);
                Vector3 orbit = Vector3.Cross(Vector3.up, delta.normalized) * strafeSign * (distance < lockRange * 1.35f ? 0.38f : 0.08f);
                Vector3 desired = delta.normalized * (Speed * Mathf.Lerp(0.38f, 1, approach)) + orbit + separation;
                MoveSteered(Vector3.ClampMagnitude(desired, Speed), 5.5f);
                Rig?.Present(State, 0, planarVelocity.magnitude / Mathf.Max(0.1f, Speed));
            }
        }
        void BeginDash(M0Player player)
        {
            State = EnemyState.Attack;
            Vector3 targetPosition = player.transform.position;
            float initialDistance = Vector3.ProjectOnPlane(targetPosition - transform.position, Vector3.up).magnitude;
            float predictionTime = Mathf.Clamp(initialDistance / Mathf.Max(0.1f, DashSpeed), 0f, 0.32f);
            targetPosition += playerVelocity * predictionTime;
            attackDirection = Vector3.ProjectOnPlane(targetPosition - transform.position, Vector3.up).normalized;
            float distance = Vector3.ProjectOnPlane(targetPosition - transform.position, Vector3.up).magnitude;
            float travelDistance = Mathf.Max(0, distance - 0.45f);
            DashDuration = Mathf.Clamp(travelDistance / Mathf.Max(0.1f, DashSpeed), 0.08f, 0.72f);
            AttackDuration = DashDuration + 0.12f;
            timer = AttackDuration; strikeResolved = attackCueFlashed = false; previousPhase = 0;
            attackDirection = attackDirection.sqrMagnitude > 0.01f ? attackDirection.normalized : transform.forward;
            transform.rotation = Quaternion.LookRotation(attackDirection, Vector3.up);
            planarVelocity = Vector3.zero;
            Rig.DashPhase = DashDuration / AttackDuration;
            previousStrike = Rig.StrikeShape.transform.position; previousStrikeRotation = Rig.StrikeShape.transform.rotation;
            previousPlayer = player.transform.position;
        }
        void BeginSlam(M0Player player)
        {
            State = EnemyState.Slam;
            Vector3 target = player.transform.position + Vector3.up * 0.7f;
            slamLockPosition = target + playerVelocity * 0.12f;
            attackDirection = (slamLockPosition - transform.position).normalized;
            float distance = Vector3.Distance(transform.position, slamLockPosition);
            DashDuration = Mathf.Clamp(distance / Mathf.Max(1f, SlamSpeed), 0.2f, 0.42f);
            AttackDuration = DashDuration + 0.12f;
            timer = AttackDuration; vertical = 0; strikeResolved = attackCueFlashed = false; previousPhase = 0;
            transform.rotation = Quaternion.LookRotation(Vector3.ProjectOnPlane(attackDirection, Vector3.up).normalized, Vector3.up);
            Rig.DashPhase = DashDuration / AttackDuration;
            previousStrike = Rig.StrikeShape.transform.position; previousStrikeRotation = Rig.StrikeShape.transform.rotation;
            previousPlayer = player.transform.position;
            M0Sandbox.Instance.Fx.PlayTone(520, 0.12f, 0.16f);
        }
        void SteerAttack(Vector3 desired, float turnRate)
        {
            if (desired.sqrMagnitude < 0.001f) return;
            Vector3 next = Vector3.RotateTowards(attackDirection, desired.normalized, turnRate * Time.deltaTime, 0).normalized;
            attackDirection = next.sqrMagnitude > 0.001f ? next : desired.normalized;
            Vector3 facing = State == EnemyState.Slam ? Vector3.ProjectOnPlane(attackDirection, Vector3.up) : attackDirection;
            if (facing.sqrMagnitude > 0.001f) transform.rotation = Quaternion.LookRotation(facing, Vector3.up);
        }
        void MoveDirect(Vector3 velocity)
        {
            lastVelocity = velocity;
            controller.Move(velocity * Time.deltaTime);
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
                float strikeStart = State == EnemyState.Slam ? 0.08f : DashDuration / AttackDuration + 0.04f;
                if (samplePhase < strikeStart || samplePhase > 0.92f) continue;
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
        void ResolveSlamImpact(M0Player player)
        {
            Vector3 offset = player.transform.position - transform.position;
            Vector3 planarOffset = Vector3.ProjectOnPlane(offset, Vector3.up);
            if (planarOffset.sqrMagnitude > SlamImpactRadius * SlamImpactRadius || Mathf.Abs(offset.y) > 1.25f) return;
            var capsule = player.GetComponent<CharacterController>();
            if (SegmentBlocked(transform.position + Vector3.up * 0.35f, player.transform.position + capsule.center)) return;
            strikeResolved = true;
            Vector3 impact = transform.position + Vector3.up * 0.2f;
            bool guarded = player.GuardIntersects(Rig.StrikeShape, Rig.StrikeShape.transform.position, Rig.StrikeShape.transform.rotation);
            player.ReceiveAttack(Damage, this, guarded, impact);
        }
        bool Obstructed(Vector3 weapon, Vector3 victim)
        {
            Vector3 shoulder = Rig.RightShoulder.position;
            return SegmentBlocked(shoulder, weapon) || SegmentBlocked(weapon, victim);
        }
        bool SegmentBlocked(Vector3 a, Vector3 b)
        {
            Vector3 d = b - a;
            int count = Physics.RaycastNonAlloc(a, d.normalized, ObstructionHits, d.magnitude, ~(1 << 2), QueryTriggerInteraction.Ignore);
            if (count == ObstructionHits.Length) return true;
            for (int i = 0; i < count; i++)
                if (ObstructionHits[i].collider.GetComponentInParent<M0Enemy>() != this) return true;
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
