using UnityEngine;
using UnityEngine.InputSystem;

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
        public float BlockWindup = 0.1f, PerfectWindow = 0.13f, BlockReduction = 0.9f;
        public float LightRecovery = 0.46f, HeavyRecovery = 0.78f, HitStop = 0.083f, WeakpointHitStop = 0.2f;
        public float HeavyKnockback = 2.6f, ShotCooldown = 0.22f, RecoilFov = 2.5f;
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
        Vector3 guardVelocity;
        float guardSpringVelocity;
        bool dodgeRewarded;
        int combo;
        bool pendingHeavy, pendingAttack, buffered, bufferedHeavy;
        M0Input Input => M0Sandbox.Instance.Input;
        void Awake() { controller = GetComponent<CharacterController>(); standingHeight = controller.height; }
        public void ResetPlayer(Vector3 position)
        {
            controller.enabled = false; transform.SetPositionAndRotation(position, Quaternion.identity);
            controller.height = standingHeight; controller.center = Vector3.up * standingHeight * 0.5f; controller.enabled = true;
            pitch = vertical = dodgeLeft = dodgeAge = shake = fovKick = 0;
            nextDodge = damageCooldown = counterUntil = comboUntil = GuardImpact = 0;
            CancelAttack(); Blocking = Crouching = false; combo = 0; Health = 100;
            guardVelocity = Vector3.zero; guardSpringVelocity = 0; dodgeRewarded = false;
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
            if (Input.Pressed(6) && Time.time >= nextDodge)
            {
                Vector2 move = Input.Move;
                dodgeDirection = move.sqrMagnitude > 0 ? (transform.right * move.x + transform.forward * move.y).normalized : transform.forward;
                dodgeLeft = DodgeDuration; dodgeAge = 0; nextDodge = Time.time + DodgeCooldown;
                DodgeOrigin = transform.position; dodgeRewarded = false;
                CancelAttack(); Blocking = false; M0Sandbox.Instance.Fx.PlayTone(280, 0.1f, 0.13f);
            }
            bool block = Armed && !Ranged && !IsDodging && Input.Held(8);
            if (block && !Blocking) { blockStarted = Time.time; CancelAttack(); }
            Blocking = block;
            recovery = Mathf.Max(0, recovery - Time.deltaTime);
            attackAnimation = Mathf.Max(0, attackAnimation - Time.deltaTime);
            if (Armed && !Ranged && !Blocking && !IsDodging && (Input.Pressed(7) || Input.Pressed(9)))
            { buffered = true; bufferedHeavy = Input.Pressed(9); bufferUntil = Time.time + 0.2f; }
            if (!Blocking && !IsDodging && recovery <= 0 && Armed)
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
            if (IsDodging)
            {
                float step = Mathf.Min(Time.deltaTime, dodgeLeft);
                float distance = DodgeDistance * (DodgeTravel((dodgeAge + step) / DodgeDuration) - DodgeTravel(dodgeAge / DodgeDuration));
                horizontal = dodgeDirection * distance / Mathf.Max(Time.deltaTime, 0.0001f);
                dodgeLeft -= step; dodgeAge += step;
            }
            controller.Move((horizontal + guardVelocity + Vector3.up * vertical) * Time.deltaTime);
            guardVelocity = Vector3.MoveTowards(guardVelocity, Vector3.zero, Time.deltaTime * 12);
            if (transform.position.y < -10) ResetPlayer(new Vector3(0, 0.1f, -8));
        }
        void LateUpdate()
        {
            if (M0Sandbox.Instance == null || Input.Blocked) return;
            shake = Mathf.MoveTowards(shake, 0, Time.deltaTime * 3);
            GuardImpact = Mathf.SmoothDamp(GuardImpact, 0, ref guardSpringVelocity, 0.12f, Mathf.Infinity, Time.deltaTime);
            fovKick = Mathf.MoveTowards(fovKick, 0, Time.deltaTime * 24);
            float bob = HeadBob && Input.Move.sqrMagnitude > 0 ? Mathf.Sin(Time.time * 10) * 0.012f : 0;
            float evadeDip = IsDodging ? Mathf.Sin(DodgeProgress * Mathf.PI) * 0.16f : 0;
            View.transform.localRotation = Quaternion.Euler(pitch, 0, 0);
            Vector3 eye = EyePosition;
            if (ThirdPerson)
            {
                Vector3 offset = View.transform.rotation * new Vector3(0.55f, 0.35f, -3.4f);
                float distance = offset.magnitude;
                if (Physics.SphereCast(eye, 0.18f, offset.normalized, out var hit, distance, WorldMask, QueryTriggerInteraction.Ignore)) distance = Mathf.Max(0, hit.distance - 0.08f);
                View.transform.position = eye + offset.normalized * distance;
            }
            else View.transform.position = eye + new Vector3(0, bob - evadeDip, 0) + Random.insideUnitSphere * CameraShake * shake;
            View.fieldOfView = (ThirdPerson ? 75 : FieldOfView) + fovKick;
            if (Rig != null) Rig.Present(this);
        }
        void CancelAttack() { pendingAttack = buffered = false; windup = recovery = attackAnimation = 0; }
        void StartMelee(bool heavy)
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
        public void Melee(bool heavy)
        {
            Aim(out var origin, out var direction);
            M0Enemy best = null; float bestDistance = MeleeRange;
            foreach (var enemy in M0Sandbox.Instance.Enemies)
            {
                if (!enemy.Alive) continue;
                Vector3 delta = enemy.Head.position - origin; float distance = delta.magnitude;
                if (distance > bestDistance || Vector3.Angle(direction, delta) > (heavy ? 48 : 38)) continue;
                if (Physics.Raycast(origin, delta.normalized, out var hit, distance + 0.1f, WorldMask, QueryTriggerInteraction.Ignore)
                    && hit.collider.GetComponentInParent<M0Enemy>() != enemy) continue;
                best = enemy; bestDistance = distance;
            }
            if (best == null) return;
            Vector3 headDelta = best.Head.position - origin;
            bool weak = Vector3.Angle(direction, headDelta) < 7 &&
                Physics.SphereCast(origin, 0.14f, direction, out var headHit, MeleeRange, WorldMask, QueryTriggerInteraction.Ignore) && headHit.collider.transform == best.Head;
            best.HitStopDuration = weak ? WeakpointHitStop : HitStop;
            bool counter = CounterReady; counterUntil = 0;
            best.ApplyHit((heavy ? HeavyDamage : LightDamage + (combo == 3 ? 8 : 0)) * (counter ? 1.25f : 1), weak, transform.forward, heavy ? HeavyKnockback : 0.65f);
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
        public DamageResult ReceiveAttack(float damage, M0Enemy attacker)
        {
            if (Health <= 0 || (Time.time < damageCooldown && !InvulnerableNow)) return new DamageResult(0, HitKind.Evade, false);
            Vector3 direction = attacker.transform.position - transform.position; direction.y = 0;
            bool facing = Blocking && Vector3.Angle(transform.forward, direction) <= 70;
            var result = CombatRules.Resolve(Health, damage, false, InvulnerableNow, facing, BlockAge, BlockWindup, PerfectWindow, BlockReduction);
            Health = Mathf.Max(0, Health - result.Damage);
            if (result.Damage > 0) damageCooldown = Time.time + 0.35f;
            if (result.Kind == HitKind.PerfectBlock || result.Kind == HitKind.Block)
            {
                GuardImpact = 1; guardSpringVelocity = 0;
                guardVelocity = -direction.normalized * GuardRecoilSpeed * (result.Kind == HitKind.PerfectBlock ? 0.35f : 1);
                M0Sandbox.Instance.Fx.Sparks(EyePosition + transform.forward * 0.65f, result.Kind == HitKind.PerfectBlock ? new Color(1, 0.8f, 0.3f) : Color.cyan, 18);
                M0Sandbox.Instance.Fx.PlayTone(result.Kind == HitKind.PerfectBlock ? 880 : 420, 0.12f, 0.22f);
            }
            if (result.Kind == HitKind.PerfectBlock) { attacker.Stagger(1.1f); counterUntil = Time.time + 1.2f; }
            if (result.Kind == HitKind.Evade && InvulnerableNow) RewardDodge();
            if (result.Damage > 0) { shake = 0.5f; M0Sandbox.Instance.Fx.PlayerHurt(); }
            M0Sandbox.Instance.ShowMessage(result.Kind == HitKind.PerfectBlock ? "完美格挡 · 反击就绪" : result.Kind == HitKind.Block ? "格挡 · 90% 减伤" : result.Kind == HitKind.Evade ? "极限闪避 · 反击就绪" : "受击 -" + result.Damage.ToString("0"));
            if (Health <= 0) M0Sandbox.Instance.OpenMenu(true);
            return result;
        }
        public void CheckPerfectDodge(M0Enemy attacker)
        {
            if (!InvulnerableNow || !attacker.DodgeContactVerified) return;
            RewardDodge();
        }
        void RewardDodge()
        {
            if (dodgeRewarded) return;
            dodgeRewarded = true;
            counterUntil = Time.time + 1.2f;
            M0Sandbox.Instance.DodgeEffect.Trigger();
            M0Sandbox.Instance.Fx.PlayTone(1040, 0.12f, 0.16f);
            M0Sandbox.Instance.ShowMessage("极限闪避 · 时流减缓 · 反击就绪");
        }
        public void OnCombatFeedback(CombatFeedback hit) { shake = hit.Kind == HitKind.Weakpoint ? 0.85f : 0.45f; }
    }
}
