using UnityEngine;
using UnityEngine.InputSystem;
using System.Collections.Generic;

namespace M0
{
    [RequireComponent(typeof(CharacterController))]
    public sealed class M0Player : MonoBehaviour
    {
        [Header("Movement / stable first person")]
        public float WalkSpeed = 4.5f, RunSpeed = 6.2f, JumpHeight = 1.1f, Gravity = 22;
        [Range(90, 100)] public float FieldOfView = 94;
        [Range(0.03f, 0.3f)] public float Sensitivity = 0.12f;
        [Range(0, 0.06f)] public float CameraShake = 0.018f;
        public bool HeadBob;
        public float CrouchHeight = 1.15f, CrouchSpeed = 2.4f;
        [Header("Combat tuning")]
        public float MeleeRange = 2.6f, LightDamage = 34, HeavyDamage = 68, ShotDamage = 34;
        public float DodgeDistance = 3.4f, DodgeDuration = 0.52f, Invulnerability = 0.2f, DodgeCooldown = 0.85f;
        public float BlockWindup = 0, PerfectWindow = 0.16f, BlockReduction = 0.9f;
        public float LightRecovery = 0.46f, HeavyRecovery = 0.78f, HitStop = 0.083f, WeakpointHitStop = 0.2f;
        public float HeavyKnockback = 2.6f, ShotCooldown = 0.22f, RecoilFov = 2.5f;
        public float CounterDuration = 0.64f, CounterMeleeRange = 9, CounterRangedRange = 22, CounterDamageMultiplier = 1.65f, CounterLungeDistance = 3.5f;
        public Camera View;
        public Transform WeaponRoot, Blade, Gun;
        public M0PlayerRig Rig;
        public float Health { get; private set; } = 100;
        public bool Ranged => M0Sandbox.Instance != null && M0Sandbox.Instance.Inventory.Equipped == M0Item.Sidearm;
        public bool Armed => M0Sandbox.Instance != null && M0Sandbox.Instance.Inventory.Equipped != M0Item.Empty;
        public bool Blocking { get; private set; }
        public bool ThirdPerson { get; private set; }
        public bool Crouching { get; private set; }
        public bool IsDodging => dodgeLeft > 0;
        public bool InvulnerableNow => IsDodging && dodgeAge <= Invulnerability;
        public float DodgeReady => Mathf.Clamp01(1 - (nextDodge - Time.time) / DodgeCooldown);
        public float DodgeProgress => Mathf.Clamp01(dodgeAge / DodgeDuration);
        public Vector3 DodgeLocalDirection => transform.InverseTransformDirection(dodgeDirection);
        public Vector2 LookAngles => new Vector2(transform.eulerAngles.y, pitch);
        public float BlockAge => Blocking ? Time.time - blockStarted : -1;
        public float GuardImpact { get; private set; }
        public Vector3 DodgeOrigin { get; private set; }
        public float GuardRecoilSpeed = 2.1f;
        public bool CounterReady => Time.time < counterUntil;
        public bool CounterAttacking => counterAttacking;
        public bool CounterRanged => counterRanged;
        public bool CounterQueued => counterQueued;
        public bool MeleeSweepActive => meleeSweepActive;
        public int LastMeleeHitCount { get; private set; }
        public Bounds LastMeleeSweepBounds { get; private set; }
        public float CounterProgress => counterAttacking && attackLength > 0 ? Mathf.Clamp01(1 - attackAnimation / attackLength) : 0;
        public float AttackProgress => attackLength > 0 ? Mathf.Clamp01(1 - attackAnimation / attackLength) : 1;
        public bool Attacking => attackAnimation > 0;
        public bool HeavyAttack => pendingHeavy;
        public int Combo => combo;
        public float StrikePhase => attackLength > 0 ? attackWindup / attackLength : 0.3f;
        public Vector3 EyePosition => transform.position + Vector3.up * (controller.height - 0.15f);
        const int WorldMask = ~(1 << 2);
        CharacterController controller;
        float pitch, vertical, dodgeLeft, dodgeAge, nextDodge, blockStarted, recovery, windup;
        float attackAnimation, attackLength, attackWindup, fovKick, shake, damageCooldown, counterUntil, bufferUntil, comboUntil;
        float standingHeight;
        Vector3 dodgeDirection;
        Vector3 counterDirection;
        float counterMoveDistance, counterTraveled;
        M0Enemy counterTarget, counterHint;
        BoxCollider bladeVolume;
        readonly HashSet<M0Enemy> meleeHitTargets = new HashSet<M0Enemy>();
        Vector3 previousBladeBase, previousBladeTip;
        bool meleeSweepActive, meleeSweepHeavy;
        Vector3 guardVelocity;
        float guardSpringVelocity;
        bool dodgeRewarded, counterAttacking, counterRanged, counterResolved, counterQueued, counterQueuedHeavy;
        bool? blockOverride;
        int combo;
        bool pendingHeavy, pendingAttack, buffered, bufferedHeavy;
        M0Input Input => M0Sandbox.Instance.Input;
        void Awake() { controller = GetComponent<CharacterController>(); standingHeight = controller.height; EnsureBladeVolume(); }
        public void ResetPlayer(Vector3 position)
        {
            controller.enabled = false; transform.SetPositionAndRotation(position, Quaternion.identity);
            controller.height = standingHeight; controller.center = Vector3.up * standingHeight * 0.5f; controller.enabled = true;
            pitch = vertical = dodgeLeft = dodgeAge = shake = fovKick = 0;
            nextDodge = damageCooldown = counterUntil = comboUntil = GuardImpact = 0;
            CancelAttack(); Blocking = Crouching = false; combo = 0; Health = 100;
            guardVelocity = Vector3.zero; guardSpringVelocity = 0; dodgeRewarded = counterAttacking = counterQueued = false;
            counterTarget = counterHint = null; counterMoveDistance = counterTraveled = 0;
            meleeSweepActive = false; meleeHitTargets.Clear(); LastMeleeHitCount = 0;
            blockOverride = null;
        }
        public void EquipmentChanged() { Blocking = false; CancelAttack(); }
        public void TogglePerspective() { ThirdPerson = !ThirdPerson; }
        void Update()
        {
            if (M0Sandbox.Instance == null || Input.Blocked || Health <= 0) return;
            var mouse = Mouse.current;
            if (mouse != null && Cursor.lockState == CursorLockMode.Locked)
            {
                var delta = mouse.delta.ReadValue() * Sensitivity;
                transform.Rotate(0, delta.x, 0); pitch = Mathf.Clamp(pitch - delta.y, -85, 85);
            }
            if (Input.Pressed(13)) TogglePerspective();
            if (Input.Pressed(10)) M0Sandbox.Instance.Inventory.CycleWeapon();
            for (int i = 0; i < M0Inventory.HotbarSize; i++) if (Input.Pressed(15 + i)) M0Sandbox.Instance.Inventory.Select(i);
            if (mouse != null) M0Sandbox.Instance.Inventory.Scroll(mouse.scroll.ReadValue().y);
            if (Input.Pressed(6)) StartDodge(Input.Move);
            bool block = Armed && !Ranged && !IsDodging && !counterAttacking && (blockOverride ?? Input.Held(8));
            if (block && !Blocking) { blockStarted = Time.time; CancelAttack(); }
            Blocking = block;
            float actionDelta = counterAttacking ? Time.unscaledDeltaTime : Time.deltaTime;
            recovery = Mathf.Max(0, recovery - actionDelta);
            attackAnimation = Mathf.Max(0, attackAnimation - actionDelta);
            bool attackPressed = Input.Pressed(7) || Input.Pressed(9);
            bool heavyPressed = Input.Pressed(9);
            if (counterAttacking)
            {
                UpdateCounterAssist();
                float strikePoint = counterRanged ? 0.44f : 0.58f;
                if (!counterResolved && CounterProgress >= strikePoint) ResolveCounter();
                if (attackAnimation <= 0) { counterAttacking = false; counterTarget = null; recovery = Mathf.Min(recovery, 0.12f); }
            }
            if (Armed && CounterReady && attackPressed && !counterAttacking)
            {
                if (IsDodging)
                {
                    if (Ranged && recovery <= 0)
                    {
                        if (StartCounter(true, heavyPressed))
                            M0Sandbox.Instance.ShowMessage("手枪闪避反击 · 滑步射击");
                    }
                    else
                    {
                        counterQueued = true; counterQueuedHeavy = heavyPressed;
                        M0Sandbox.Instance.ShowMessage("闪避反击已缓冲");
                    }
                }
                else if (recovery <= 0 && StartCounter(Ranged, heavyPressed)) Blocking = false;
            }
            if (counterQueued && !CounterReady) counterQueued = false;
            if (Armed && counterQueued && !IsDodging && !counterAttacking && recovery <= 0 && CounterReady)
            {
                bool queuedHeavy = counterQueuedHeavy; counterQueued = false;
                if (StartCounter(Ranged, queuedHeavy)) Blocking = false;
            }
            if (Armed && !Ranged && !Blocking && !IsDodging && !counterAttacking && (Input.Pressed(7) || Input.Pressed(9)))
            { buffered = true; bufferedHeavy = Input.Pressed(9); bufferUntil = Time.time + 0.2f; }
            if (!Blocking && !IsDodging && !counterAttacking && recovery <= 0 && Armed)
            {
                if (Ranged && Input.Held(7)) Shoot();
                else if (!Ranged && buffered && Time.time <= bufferUntil) StartMelee(bufferedHeavy);
            }
            if (pendingAttack)
            {
                windup -= Time.deltaTime;
                if (windup <= 0) { pendingAttack = false; Melee(pendingHeavy); }
            }
            Move();
        }
        public static float DodgeTravel(float u)
        {
            // Integral of the velocity profile: plant foot, burst, brake. Stable at any frame rate.
            u = Mathf.Clamp01(u);
            if (u < 0.12f) return 0.06f * Mathf.Pow(u / 0.12f, 2);
            if (u < 0.52f) return Mathf.Lerp(0.06f, 0.86f, (u - 0.12f) / 0.4f);
            float t = (u - 0.52f) / 0.48f;
            return 0.86f + 0.14f * (1 - (1 - t) * (1 - t));
        }
        void Move()
        {
            bool wantsCrouch = Input.Held(12);
            bool ceiling = Physics.CheckCapsule(transform.position + Vector3.up * (controller.height - controller.radius),
                transform.position + Vector3.up * (standingHeight - controller.radius), controller.radius - 0.025f, WorldMask, QueryTriggerInteraction.Ignore);
            Crouching = wantsCrouch || (controller.height < standingHeight - 0.01f && ceiling);
            float height = Mathf.MoveTowards(controller.height, Crouching ? CrouchHeight : standingHeight, Time.deltaTime * 4.5f);
            controller.height = height; controller.center = Vector3.up * height * 0.5f;
            var move = Input.Move;
            if (controller.isGrounded && vertical < 0) vertical = -2;
            if (Input.Pressed(5) && controller.isGrounded && !IsDodging && !Crouching) vertical = Mathf.Sqrt(2 * Gravity * JumpHeight);
            vertical -= Gravity * Time.deltaTime;
            var horizontal = (transform.right * move.x + transform.forward * move.y) * (Crouching ? CrouchSpeed : Input.Held(4) ? RunSpeed : WalkSpeed);
            if (Blocking) horizontal *= 0.55f;
            Vector3 dodgeStep = Vector3.zero;
            if (IsDodging)
            {
                float step = Mathf.Min(Time.unscaledDeltaTime, dodgeLeft);
                float distance = DodgeDistance * (DodgeTravel((dodgeAge + step) / DodgeDuration) - DodgeTravel(dodgeAge / DodgeDuration));
                horizontal = Vector3.zero;
                dodgeStep = dodgeDirection * distance;
                dodgeLeft -= step; dodgeAge += step;
            }
            Vector3 counterStep = Vector3.zero;
            if (counterAttacking && !counterRanged && counterMoveDistance > 0)
            {
                float travel = Mathf.SmoothStep(0, 1, Mathf.Clamp01(CounterProgress / 0.62f));
                float desired = counterMoveDistance * travel;
                float step = Mathf.Max(0, desired - counterTraveled);
                counterTraveled = desired;
                counterStep = counterDirection * step;
            }
            controller.Move((horizontal + guardVelocity + Vector3.up * vertical) * Time.deltaTime + dodgeStep + counterStep);
            guardVelocity = Vector3.MoveTowards(guardVelocity, Vector3.zero, Time.deltaTime * 12);
            if (transform.position.y < -10) ResetPlayer(new Vector3(0, 0.1f, -8));
        }
        void LateUpdate()
        {
            if (M0Sandbox.Instance == null || Input.Blocked) return;
            shake = Mathf.MoveTowards(shake, 0, Time.unscaledDeltaTime * 3);
            GuardImpact = Mathf.SmoothDamp(GuardImpact, 0, ref guardSpringVelocity, 0.12f, Mathf.Infinity, Time.deltaTime);
            fovKick = Mathf.MoveTowards(fovKick, 0, Time.deltaTime * 24);
            float bob = HeadBob && Input.Move.sqrMagnitude > 0 ? Mathf.Sin(Time.time * 10) * 0.012f : 0;
            float evadeDip = IsDodging ? Mathf.Sin(DodgeProgress * Mathf.PI) * 0.16f : 0;
            float jolt = shake * CameraShake;
            View.transform.localRotation = Quaternion.Euler(pitch + Random.Range(-jolt, jolt) * 90, 0, Random.Range(-jolt, jolt) * 70);
            Vector3 eye = EyePosition;
            if (ThirdPerson)
            {
                Vector3 offset = View.transform.rotation * new Vector3(0.55f, 0.35f, -3.4f);
                float distance = offset.magnitude;
                if (Physics.SphereCast(eye, 0.18f, offset.normalized, out var hit, distance, WorldMask, QueryTriggerInteraction.Ignore)) distance = Mathf.Max(0, hit.distance - 0.08f);
                View.transform.position = eye + offset.normalized * distance;
            }
            else View.transform.position = eye + transform.forward * 0.12f + new Vector3(0, bob - evadeDip, 0) + Random.insideUnitSphere * jolt;
            View.fieldOfView = (ThirdPerson ? 75 : FieldOfView) + fovKick;
            if (Rig != null) Rig.Present(this);
        }
        void EnsureBladeVolume()
        {
            if (Blade == null || bladeVolume != null) return;
            bladeVolume = Blade.GetComponent<BoxCollider>();
            if (bladeVolume == null) bladeVolume = Blade.gameObject.AddComponent<BoxCollider>();
            bladeVolume.center = new Vector3(0, 0.31f, 0.025f);
            bladeVolume.size = new Vector3(0.26f, 0.70f, 0.14f);
            bladeVolume.isTrigger = true;
        }
        void CancelAttack()
        {
            pendingAttack = buffered = false; counterAttacking = false; counterTarget = null;
            counterMoveDistance = counterTraveled = 0; windup = recovery = attackAnimation = 0;
            meleeSweepActive = false; meleeHitTargets.Clear();
        }
        public bool StartDodge(Vector2 move)
        {
            if (Time.time < nextDodge || Health <= 0) return false;
            dodgeDirection = move.sqrMagnitude > 0 ? (transform.right * move.x + transform.forward * move.y).normalized : transform.forward;
            dodgeLeft = DodgeDuration; dodgeAge = 0; nextDodge = Time.time + DodgeCooldown;
            DodgeOrigin = transform.position; dodgeRewarded = false;
            CancelAttack(); Blocking = false; M0Sandbox.Instance.Fx.PlayTone(280, 0.1f, 0.13f);
            return true;
        }
        // Optional external control is also used by deterministic combat validation; player input remains the default.
        public void SetBlockOverride(bool? held) { blockOverride = held; }
        public bool TryStartCounter(bool ranged, bool heavy = false)
        {
            if (!Armed || !CounterReady || counterAttacking || (IsDodging && !ranged) || recovery > 0) return false;
            bool started = StartCounter(ranged, heavy);
            if (started) Blocking = false;
            return started;
        }
        public void StartMelee(bool heavy)
        {
            buffered = false; pendingAttack = true; pendingHeavy = heavy;
            combo = Time.time > comboUntil ? 1 : combo % 3 + 1;
            attackWindup = windup = heavy ? 0.30f : combo == 3 ? 0.17f : 0.12f;
            recovery = heavy ? HeavyRecovery : LightRecovery + (combo == 3 ? 0.08f : 0);
            attackLength = attackAnimation = Mathf.Max(recovery, windup + 0.16f); comboUntil = Time.time + recovery + 0.65f;
            M0Sandbox.Instance.Fx.PlayTone(heavy ? 170 : 350, 0.14f, 0.16f);
        }
        void Aim(out Vector3 origin, out Vector3 direction)
        {
            origin = ThirdPerson ? EyePosition : View.transform.position; direction = View.transform.forward;
            if (!ThirdPerson) return;
            Vector3 target = View.transform.position + direction * 55;
            if (Physics.Raycast(View.transform.position, direction, out var hit, 55, WorldMask, QueryTriggerInteraction.Ignore)) target = hit.point;
            direction = (target - origin).normalized;
        }
        bool StartCounter(bool ranged, bool heavy)
        {
            float range = ranged ? CounterRangedRange : CounterMeleeRange;
            M0Enemy target = counterHint != null && counterHint.Alive && Vector3.Distance(EyePosition, counterHint.Head.position) <= range
                ? counterHint : FindCounterTarget(range, ranged);
            if (target == null) return false;
            counterHint = null;
            counterTarget = target; counterRanged = ranged; counterAttacking = true; counterResolved = false;
            pendingAttack = buffered = counterQueued = false; counterUntil = 0; counterMoveDistance = 0;
            Vector3 delta = target.Head.position - transform.position; delta.y = 0;
            counterDirection = delta.sqrMagnitude > 0.001f ? delta.normalized : transform.forward;
            float distance = Vector3.Distance(EyePosition, target.Head.position);
            counterMoveDistance = ranged ? 0 : Mathf.Clamp(distance - 1.45f, 0, CounterLungeDistance);
            counterTraveled = 0;
            float duration = ranged ? Mathf.Min(CounterDuration, 0.50f) : Mathf.Min(CounterDuration, 0.56f);
            attackLength = attackAnimation = duration; attackWindup = 0;
            pendingHeavy = heavy; recovery = CounterDuration + 0.12f;
            M0Sandbox.Instance.Fx.PlayTone(ranged ? 520 : 260, 0.18f, 0.24f);
            M0Sandbox.Instance.ShowMessage(ranged ? "手枪闪避反击 · 平滑锁定" : "反击突进 · 平滑锁定");
            return true;
        }
        void UpdateCounterAssist()
        {
            if (counterTarget == null || !counterTarget.Alive || counterResolved) return;
            Vector3 flat = counterTarget.Head.position - transform.position; flat.y = 0;
            if (flat.sqrMagnitude > 0.001f)
            {
                Vector3 desired = flat.normalized;
                float turnSpeed = counterRanged ? 300f : 430f;
                transform.rotation = Quaternion.RotateTowards(transform.rotation, Quaternion.LookRotation(desired), turnSpeed * Time.unscaledDeltaTime);
                counterDirection = Vector3.Slerp(counterDirection, desired, 1 - Mathf.Exp(-13f * Time.unscaledDeltaTime)).normalized;
            }
            Vector3 aim = counterTarget.Head.position - EyePosition;
            float targetPitch = Mathf.Clamp(-Mathf.Atan2(aim.y, new Vector2(aim.x, aim.z).magnitude) * Mathf.Rad2Deg, -75, 75);
            pitch = Mathf.MoveTowards(pitch, targetPitch, (counterRanged ? 210f : 150f) * Time.unscaledDeltaTime);
        }
        M0Enemy FindCounterTarget(float range, bool ranged)
        {
            Aim(out var origin, out var direction);
            M0Enemy best = null; float bestScore = float.PositiveInfinity;
            foreach (var enemy in M0Sandbox.Instance.Enemies)
            {
                if (!enemy.Alive) continue;
                Vector3 delta = enemy.Head.position - origin; float distance = delta.magnitude;
                float angle = Vector3.Angle(direction, delta);
                if (distance > range || angle > (ranged ? 125f : 100f)) continue;
                if (Physics.Raycast(origin, delta.normalized, out var hit, distance + 0.1f, WorldMask, QueryTriggerInteraction.Ignore)
                    && hit.collider.GetComponentInParent<M0Enemy>() != enemy) continue;
                float score = angle * (ranged ? 0.055f : 0.07f) + distance * 0.045f;
                if (score < bestScore) { bestScore = score; best = enemy; }
            }
            return best;
        }
        void ResolveCounter()
        {
            counterResolved = true;
            if (counterTarget == null || !counterTarget.Alive) return;
            Vector3 origin = View.transform.position;
            Vector3 aimPoint = counterTarget.Head.position;
            Vector3 direction = (aimPoint - origin).normalized;
            bool weak = false;
            if (counterRanged)
            {
                recovery = Mathf.Max(recovery, ShotCooldown + 0.25f); fovKick = RecoilFov * 1.8f;
                Vector3 end = aimPoint;
                if (Physics.Raycast(origin, direction, out var hit, CounterRangedRange, WorldMask, QueryTriggerInteraction.Ignore))
                {
                    end = hit.point;
                    var hitTarget = hit.collider.GetComponentInParent<M0Enemy>();
                    if (hitTarget != counterTarget) { counterTarget = null; return; }
                    weak = hit.collider.transform == counterTarget.Head;
                }
                counterTarget.HitStopDuration = WeakpointHitStop;
                counterTarget.ApplyHit(ShotDamage * CounterDamageMultiplier, weak, direction, 2.2f);
                M0Sandbox.Instance.Fx.Tracer(Gun.position + Gun.forward * 0.35f, end);
                M0Sandbox.Instance.Fx.Sparks(end, new Color(0.4f, 0.95f, 1), 28, -direction, 1.25f);
                M0Sandbox.Instance.Fx.PlayTone(1180, 0.18f, 0.3f);
            }
            else
            {
                direction = counterDirection;
                counterTarget.HitStopDuration = WeakpointHitStop;
                counterTarget.ApplyHit((pendingHeavy ? HeavyDamage : LightDamage) * CounterDamageMultiplier, false, direction, 4.2f);
                if (counterTarget.Alive) counterTarget.Stagger(0.55f);
                M0Sandbox.Instance.Fx.Sparks(counterTarget.Head.position, new Color(1, 0.74f, 0.24f), 22, -direction, 1.2f);
                M0Sandbox.Instance.Fx.PlayTone(980, 0.16f, 0.28f);
            }
            M0Sandbox.Instance.ShowMessage(counterRanged ? "精准反击 · 自动索敌" : "闪避反击 · 自动索敌");
            counterTarget = null;
        }
        public void Melee(bool heavy)
        {
            EnsureBladeVolume();
            if (Blade == null) return;
            meleeSweepActive = true; meleeSweepHeavy = heavy; meleeHitTargets.Clear(); LastMeleeHitCount = 0;
            GetBladeSegment(out previousBladeBase, out previousBladeTip);
            var initialBounds = new Bounds(previousBladeBase, Vector3.zero); initialBounds.Encapsulate(previousBladeTip);
            initialBounds.Expand((heavy ? 0.13f : 0.105f) * 2); LastMeleeSweepBounds = initialBounds;
        }
        void GetBladeSegment(out Vector3 bladeBase, out Vector3 bladeTip)
        {
            bladeBase = Blade.TransformPoint(new Vector3(0, 0.045f, 0.025f));
            bladeTip = Blade.TransformPoint(new Vector3(0, 0.62f, 0.025f));
        }
        public void SampleMeleeSweep()
        {
            EnsureBladeVolume();
            if (!meleeSweepActive || Blade == null) return;
            float strikeEnd = Mathf.Min(0.78f, StrikePhase + 0.29f);
            if (!Attacking || AttackProgress > strikeEnd)
            { meleeSweepActive = false; return; }
            GetBladeSegment(out var currentBase, out var currentTip);
            float travel = Mathf.Max(Vector3.Distance(previousBladeBase, currentBase), Vector3.Distance(previousBladeTip, currentTip));
            int samples = Mathf.Clamp(Mathf.CeilToInt(travel / 0.055f), 1, 20);
            float radius = meleeSweepHeavy ? 0.13f : 0.105f;
            for (int i = 1; i <= samples; i++)
            {
                float t = i / (float)samples;
                Vector3 bladeBase = Vector3.Lerp(previousBladeBase, currentBase, t);
                Vector3 bladeTip = Vector3.Lerp(previousBladeTip, currentTip, t);
                var sweepBounds = LastMeleeSweepBounds; sweepBounds.Encapsulate(bladeBase); sweepBounds.Encapsulate(bladeTip);
                LastMeleeSweepBounds = sweepBounds;
                foreach (var hit in Physics.OverlapCapsule(bladeBase, bladeTip, radius, WorldMask, QueryTriggerInteraction.Collide))
                {
                    var enemy = hit.GetComponentInParent<M0Enemy>();
                    if (enemy == null || !enemy.Alive || meleeHitTargets.Contains(enemy) || !WeaponPathClear(bladeBase, hit.bounds.center, enemy)) continue;
                    bool weak = hit.transform == enemy.Head;
                    meleeHitTargets.Add(enemy); LastMeleeHitCount++;
                    enemy.HitStopDuration = weak ? WeakpointHitStop : HitStop;
                    float damage = meleeSweepHeavy ? HeavyDamage : LightDamage + (combo == 3 ? 8 : 0);
                    Vector3 knockDirection = enemy.transform.position - transform.position; knockDirection.y = 0;
                    enemy.ApplyHit(damage, weak, knockDirection.normalized, meleeSweepHeavy ? HeavyKnockback : 0.65f);
                }
            }
            previousBladeBase = currentBase; previousBladeTip = currentTip;
        }
        bool WeaponPathClear(Vector3 from, Vector3 to, M0Enemy target)
        {
            Vector3 delta = to - from;
            if (delta.sqrMagnitude < 0.001f) return true;
            var hits = Physics.RaycastAll(from, delta.normalized, delta.magnitude + 0.05f, WorldMask, QueryTriggerInteraction.Ignore);
            System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
            foreach (var hit in hits)
            {
                var enemy = hit.collider.GetComponentInParent<M0Enemy>();
                if (enemy == target) return true;
                if (enemy == null) return false;
            }
            return true;
        }
        public bool GuardIntersects(BoxCollider attackShape, Vector3 attackPosition, Quaternion attackRotation)
        {
            EnsureBladeVolume();
            if (!Blocking || bladeVolume == null || attackShape == null) return false;
            return Physics.ComputePenetration(attackShape, attackPosition, attackRotation,
                bladeVolume, Blade.position, Blade.rotation, out _, out _);
        }
        public void Shoot()
        {
            recovery = ShotCooldown; fovKick = RecoilFov;
            M0Sandbox.Instance.Fx.PlayTone(90, 0.1f, 0.22f);
            Aim(out var origin, out var direction); Vector3 end = origin + direction * 55;
            if (Physics.Raycast(origin, direction, out var hit, 55, WorldMask, QueryTriggerInteraction.Ignore))
            {
                end = hit.point; var target = hit.collider.GetComponentInParent<M0Enemy>();
                if (target != null && target.Alive)
                {
                    bool weak = hit.collider.transform == target.Head; target.HitStopDuration = weak ? WeakpointHitStop : HitStop;
                    target.ApplyHit(ShotDamage, weak, direction, weak ? 3.6f : 0.45f);
                }
                else M0Sandbox.Instance.Fx.Sparks(hit.point, Color.gray, 5);
            }
            M0Sandbox.Instance.Fx.Tracer(Gun.position + Gun.forward * 0.35f, end);
        }
        public DamageResult ReceiveAttack(float damage, M0Enemy attacker, bool? weaponGuardContact = null, Vector3? contactPoint = null)
        {
            if (Health <= 0 || (Time.time < damageCooldown && !InvulnerableNow)) return new DamageResult(0, HitKind.Evade, false);
            Vector3 direction = attacker.transform.position - transform.position; direction.y = 0;
            bool facing = Blocking && (weaponGuardContact ?? Vector3.Angle(transform.forward, direction) <= 70);
            var result = CombatRules.Resolve(Health, damage, false, InvulnerableNow, facing, BlockAge, BlockWindup, PerfectWindow, BlockReduction);
            Health = Mathf.Max(0, Health - result.Damage);
            if (result.Damage > 0) damageCooldown = Time.time + 0.35f;
            if (result.Kind == HitKind.PerfectBlock || result.Kind == HitKind.Block)
            {
                bool perfect = result.Kind == HitKind.PerfectBlock;
                GuardImpact = perfect ? 1.35f : 0.72f; guardSpringVelocity = 0;
                guardVelocity = perfect ? Vector3.zero : -direction.normalized * GuardRecoilSpeed;
                Vector3 impact = contactPoint ?? attacker.StrikePoint;
                Vector3 sparkNormal = EyePosition - impact;
                M0Sandbox.Instance.Fx.Sparks(impact, perfect ? new Color(1, 0.72f, 0.2f) : new Color(0.45f, 0.88f, 1), perfect ? 42 : 18, sparkNormal, perfect ? 1.55f : 1.05f);
                shake = Mathf.Max(shake, perfect ? 1.65f : 0.72f);
                M0Sandbox.Instance.Fx.PlayTone(result.Kind == HitKind.PerfectBlock ? 880 : 420, 0.12f, 0.22f);
            }
            if (result.Kind == HitKind.PerfectBlock) { attacker.Stagger(1.1f); counterUntil = Time.time + 1.2f; }
            if (result.Kind == HitKind.Evade && InvulnerableNow) RewardDodge(attacker);
            if (result.Damage > 0) { shake = 0.5f; M0Sandbox.Instance.Fx.PlayerHurt(); }
            M0Sandbox.Instance.ShowMessage(result.Kind == HitKind.PerfectBlock ? "完美格挡 · 反击就绪" : result.Kind == HitKind.Block ? "格挡 · 90% 减伤" : result.Kind == HitKind.Evade ? "极限闪避 · 反击就绪" : "受击 -" + result.Damage.ToString("0"));
            if (Health <= 0) M0Sandbox.Instance.OpenMenu(true);
            return result;
        }
        public void CheckPerfectDodge(M0Enemy attacker)
        {
            if (!InvulnerableNow || !attacker.DodgeContactVerified) return;
            RewardDodge(attacker);
        }
        void RewardDodge(M0Enemy attacker)
        {
            if (dodgeRewarded) return;
            dodgeRewarded = true;
            counterHint = attacker;
            counterUntil = Time.time + 1.2f;
            M0Sandbox.Instance.DodgeEffect.Trigger();
            M0Sandbox.Instance.Fx.PlayTone(1040, 0.12f, 0.16f);
            M0Sandbox.Instance.ShowMessage(Ranged ? "极限闪避 · 左键触发手枪反击" : "极限闪避 · 左键反击（可切换手枪）");
        }
        public void OnCombatFeedback(CombatFeedback hit) { shake = hit.Kind == HitKind.Weakpoint ? 0.85f : 0.45f; }
    }
}
