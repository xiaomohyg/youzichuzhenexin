using UnityEngine;
using UnityEngine.InputSystem;

public sealed class M0CombatPrototype : MonoBehaviour
{
    [Header("Combat")]
    [SerializeField] private float meleeRange = 2.4f;
    [SerializeField] private float meleeDamage = 55f;
    [SerializeField] private float shotDamage = 34f;
    [SerializeField] private float dodgeDistance = 3.4f;
    [SerializeField] private float dodgeDuration = 0.52f;
    [SerializeField] private float dodgeInvulnerability = 0.2f;
    [SerializeField] private float blockWindup = 0.1f;
    [SerializeField] private float perfectBlockWindow = 0.13f;
    [SerializeField] private float playerHealth = 100f;
    [SerializeField] private LayerMask hitMask = ~0;

    private Camera viewCamera;
    private CharacterController character;
    private float dodgeTimer;
    private float dodgeElapsed;
    private Vector3 dodgeDirection;
    private float blockStarted = -10f;
    private float nextMelee;
    private float nextShot;
    private float damageCooldownUntil;
    private float feedbackTimer;
    private string feedback = "";
    private bool blocking;
    private Transform weaponRoot;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Install()
    {
        BlockStickmanController mover = FindAnyObjectByType<BlockStickmanController>();
        if (mover != null && mover.GetComponent<M0CombatPrototype>() == null)
        {
            mover.gameObject.AddComponent<M0CombatPrototype>();
        }
    }

    private void Awake()
    {
        character = GetComponent<CharacterController>();
        viewCamera = GetComponentInChildren<Camera>();
        if (viewCamera != null)
        {
            viewCamera.fieldOfView = 94f;
            BuildFirstPersonArms();
        }

        PrototypeEnemy.CreateWave(transform.position);
    }

    private void Update()
    {
        Keyboard keyboard = Keyboard.current;
        Mouse mouse = Mouse.current;
        if (keyboard == null || mouse == null || viewCamera == null)
        {
            return;
        }

        feedbackTimer = Mathf.Max(0f, feedbackTimer - Time.deltaTime);
        UpdateDodge(keyboard);
        UpdateBlock(mouse);
        UpdateWeapons(mouse, keyboard);

        if (keyboard.escapeKey.wasPressedThisFrame)
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }
        if (mouse.leftButton.wasPressedThisFrame && Cursor.lockState != CursorLockMode.Locked)
        {
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }
    }

    private void UpdateDodge(Keyboard keyboard)
    {
        if (keyboard.qKey.wasPressedThisFrame && dodgeTimer <= 0f)
        {
            Vector3 input = Vector3.zero;
            if (keyboard.wKey.isPressed) input += transform.forward;
            if (keyboard.sKey.isPressed) input -= transform.forward;
            if (keyboard.aKey.isPressed) input -= transform.right;
            if (keyboard.dKey.isPressed) input += transform.right;
            dodgeDirection = input.sqrMagnitude > 0.01f ? input.normalized : transform.forward;
            dodgeTimer = dodgeDuration;
            dodgeElapsed = 0f;
            blocking = false;
            feedback = "DODGE";
            feedbackTimer = 0.25f;
        }

        if (dodgeTimer > 0f)
        {
            float step = dodgeDistance / dodgeDuration * Time.deltaTime;
            if (character != null) character.Move(dodgeDirection * step);
            else transform.position += dodgeDirection * step;
            dodgeTimer -= Time.deltaTime;
            dodgeElapsed += Time.deltaTime;
        }
    }

    private void UpdateBlock(Mouse mouse)
    {
        bool pressed = mouse.rightButton.isPressed && dodgeTimer <= 0f;
        if (pressed && !blocking)
        {
            blocking = true;
            blockStarted = Time.time;
        }
        else if (!pressed)
        {
            blocking = false;
        }
        if (weaponRoot != null)
        {
            Vector3 target = blocking ? new Vector3(0.18f, -0.06f, 0.5f) : new Vector3(0.32f, -0.3f, 0.65f);
            weaponRoot.localPosition = Vector3.Lerp(weaponRoot.localPosition, target, 14f * Time.deltaTime);
            weaponRoot.localRotation = Quaternion.Slerp(weaponRoot.localRotation, Quaternion.identity, 16f * Time.deltaTime);
        }
    }

    private void UpdateWeapons(Mouse mouse, Keyboard keyboard)
    {
        if (mouse.leftButton.wasPressedThisFrame && Time.time >= nextMelee && dodgeTimer <= 0f)
        {
            nextMelee = Time.time + 0.42f;
            DoMelee();
        }
        if (keyboard.rKey.wasPressedThisFrame && Time.time >= nextShot && dodgeTimer <= 0f)
        {
            nextShot = Time.time + 0.22f;
            DoShot();
        }
    }

    private void DoMelee()
    {
        Collider[] hits = Physics.OverlapSphere(viewCamera.transform.position + viewCamera.transform.forward * 1.1f, meleeRange, hitMask);
        PrototypeEnemy best = null;
        float bestAngle = 75f;
        foreach (Collider hit in hits)
        {
            PrototypeEnemy enemy = hit.GetComponentInParent<PrototypeEnemy>();
            if (enemy == null || enemy.IsDead) continue;
            Vector3 direction = enemy.AimPoint - viewCamera.transform.position;
            float angle = Vector3.Angle(viewCamera.transform.forward, direction);
            if (direction.magnitude <= meleeRange && angle < bestAngle)
            {
                best = enemy;
                bestAngle = angle;
            }
        }

        if (best != null)
        {
            bool weakPoint = Vector3.Distance(best.AimPoint, viewCamera.transform.position + viewCamera.transform.forward * 1.1f) < meleeRange * 0.6f;
            best.TakeHit(meleeDamage, weakPoint, viewCamera.transform.forward, weakPoint ? 9f : 6f);
            RegisterHit(weakPoint ? "WEAKPOINT" : "HIT", weakPoint);
        }
        else
        {
            feedback = "MISS";
            feedbackTimer = 0.18f;
        }
    }

    private void DoShot()
    {
        Ray ray = new Ray(viewCamera.transform.position, viewCamera.transform.forward);
        if (Physics.Raycast(ray, out RaycastHit hit, 60f, hitMask, QueryTriggerInteraction.Ignore))
        {
            PrototypeEnemy enemy = hit.collider.GetComponentInParent<PrototypeEnemy>();
            if (enemy != null && !enemy.IsDead)
            {
                bool weakPoint = hit.collider == enemy.HeadCollider;
                enemy.TakeHit(weakPoint ? shotDamage * 4f : shotDamage, weakPoint, ray.direction, weakPoint ? 12f : 3.5f);
                RegisterHit(weakPoint ? "HEADSHOT" : "HIT", weakPoint);
                return;
            }
        }
        feedback = "SHOT";
        feedbackTimer = 0.16f;
    }

    private void RegisterHit(string message, bool strong)
    {
        feedback = message;
        feedbackTimer = strong ? 0.7f : 0.35f;
        if (weaponRoot != null)
        {
            weaponRoot.localRotation = Quaternion.Euler(-14f, 0f, 0f);
        }
    }

    public void ReceiveAttack(float damage, Vector3 source)
    {
        if (Time.time < damageCooldownUntil || IsDodging)
        {
            if (IsDodging) ShowFeedback("DODGED", 0.35f);
            return;
        }

        if (blocking && Time.time - blockStarted >= blockWindup)
        {
            bool perfect = Time.time - blockStarted <= blockWindup + perfectBlockWindow;
            if (perfect)
            {
                PrototypeEnemy attacker = source == Vector3.zero ? null : PrototypeEnemy.ClosestTo(source);
                if (attacker != null) attacker.Stagger(1.1f);
                ShowFeedback("PERFECT BLOCK", 0.6f);
            }
            else
            {
                ShowFeedback("BLOCK", 0.25f);
            }
            return;
        }

        playerHealth = Mathf.Max(0f, playerHealth - damage);
        damageCooldownUntil = Time.time + 0.45f;
        ShowFeedback("-" + damage.ToString("0") + " HP", 0.5f);
    }

    private bool IsDodging => dodgeTimer > 0f && dodgeElapsed <= dodgeInvulnerability;

    private void ShowFeedback(string message, float duration)
    {
        feedback = message;
        feedbackTimer = duration;
    }

    private void BuildFirstPersonArms()
    {
        Transform visuals = transform.Find("Visuals");
        if (visuals != null) visuals.gameObject.SetActive(false);
        weaponRoot = new GameObject("Prototype Weapon View").transform;
        weaponRoot.SetParent(viewCamera.transform, false);
        weaponRoot.localPosition = new Vector3(0.32f, -0.3f, 0.65f);
        GameObject forearm = GameObject.CreatePrimitive(PrimitiveType.Cube);
        forearm.name = "Block Arm";
        forearm.transform.SetParent(weaponRoot, false);
        forearm.transform.localPosition = new Vector3(-0.05f, -0.18f, 0f);
        forearm.transform.localScale = new Vector3(0.16f, 0.22f, 0.62f);
        forearm.GetComponent<Collider>().enabled = false;
        SetMaterial(forearm, new Color(0.2f, 0.28f, 0.31f));
        GameObject blade = GameObject.CreatePrimitive(PrimitiveType.Cube);
        blade.name = "Training Blade";
        blade.transform.SetParent(weaponRoot, false);
        blade.transform.localPosition = new Vector3(0f, 0f, 0.44f);
        blade.transform.localRotation = Quaternion.Euler(7f, 0f, 0f);
        blade.transform.localScale = new Vector3(0.1f, 0.12f, 0.58f);
        blade.GetComponent<Collider>().enabled = false;
        SetMaterial(blade, new Color(0.67f, 0.78f, 0.79f));
    }

    private static void SetMaterial(GameObject target, Color color)
    {
        Renderer renderer = target.GetComponent<Renderer>();
        Shader shader = Shader.Find("Universal Render Pipeline/Lit");
        if (shader == null) shader = Shader.Find("Standard");
        Material material = new Material(shader);
        material.color = color;
        renderer.material = material;
    }

    private void OnGUI()
    {
        GUI.color = Color.white;
        GUI.Label(new Rect(20, 16, 440, 26), "M0 // COMBAT SANDBOX     HP " + playerHealth.ToString("000"));
        GUI.Label(new Rect(20, 42, 520, 24), "WASD move   Shift run   Q dodge   RMB block   LMB melee   R shoot   Esc cursor");
        float centerX = Screen.width * 0.5f;
        float centerY = Screen.height * 0.5f;
        GUI.color = feedbackTimer > 0f && (feedback.Contains("HEAD") || feedback.Contains("PERFECT") || feedback.Contains("WEAK"))
            ? new Color(1f, 0.78f, 0.25f)
            : Color.white;
        GUI.DrawTexture(new Rect(centerX - 1f, centerY - 8f, 2f, 16f), Texture2D.whiteTexture);
        GUI.DrawTexture(new Rect(centerX - 8f, centerY - 1f, 16f, 2f), Texture2D.whiteTexture);
        if (feedbackTimer > 0f)
        {
            GUI.Label(new Rect(centerX - 90f, centerY + 24f, 180f, 30f), feedback);
        }
        GUI.color = Color.white;
    }
}

public sealed class PrototypeEnemy : MonoBehaviour
{
    private static readonly System.Collections.Generic.List<PrototypeEnemy> All = new System.Collections.Generic.List<PrototypeEnemy>();
    private static Material bodyMaterial;
    private static Material headMaterial;

    [SerializeField] private float health = 100f;
    [SerializeField] private float moveSpeed = 1.8f;
    [SerializeField] private float attackRange = 2.2f;
    [SerializeField] private float attackDamage = 12f;

    private Transform player;
    private Rigidbody body;
    private Collider bodyCollider;
    private float attackCooldown = 1.5f;
    private float telegraphTimer;
    private float staggerTimer;
    private bool attacking;
    private bool dead;
    private Renderer bodyRenderer;

    public bool IsDead => dead;
    public Collider HeadCollider { get; private set; }
    public Vector3 AimPoint => transform.position + Vector3.up * 1.3f;

    public static void CreateWave(Vector3 playerPosition)
    {
        if (All.Count > 0) return;
        Create(new Vector3(-2.2f, 0f, 9f), playerPosition);
        Create(new Vector3(2.4f, 0f, 12f), playerPosition);
        Create(new Vector3(0f, 0f, 16f), playerPosition);
    }

    private static void Create(Vector3 position, Vector3 playerPosition)
    {
        GameObject root = new GameObject("M0 Training Ghoul");
        root.transform.position = position;
        root.transform.LookAt(new Vector3(playerPosition.x, position.y, playerPosition.z));
        PrototypeEnemy enemy = root.AddComponent<PrototypeEnemy>();
        enemy.BuildBody();
        All.Add(enemy);
    }

    private void Awake()
    {
        player = FindAnyObjectByType<M0CombatPrototype>()?.transform;
        body = gameObject.AddComponent<Rigidbody>();
        body.mass = 55f;
        body.constraints = RigidbodyConstraints.FreezeRotation;
        bodyCollider = gameObject.AddComponent<CapsuleCollider>();
        CapsuleCollider capsule = (CapsuleCollider)bodyCollider;
        capsule.height = 1.8f;
        capsule.radius = 0.42f;
        capsule.center = Vector3.up * 0.9f;
    }

    private void BuildBody()
    {
        if (bodyMaterial == null) bodyMaterial = CreateMaterial(new Color(0.28f, 0.38f, 0.34f));
        if (headMaterial == null) headMaterial = CreateMaterial(new Color(0.8f, 0.3f, 0.2f));
        GameObject torso = GameObject.CreatePrimitive(PrimitiveType.Capsule);
        torso.name = "Body Visual";
        torso.transform.SetParent(transform, false);
        torso.transform.localPosition = Vector3.up * 0.9f;
        torso.transform.localScale = new Vector3(0.78f, 0.9f, 0.72f);
        Destroy(torso.GetComponent<Collider>());
        bodyRenderer = torso.GetComponent<Renderer>();
        bodyRenderer.material = bodyMaterial;
        GameObject head = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        head.name = "Weakpoint Head";
        head.transform.SetParent(transform, false);
        head.transform.localPosition = Vector3.up * 1.62f;
        head.transform.localScale = Vector3.one * 0.42f;
        HeadCollider = head.GetComponent<Collider>();
        head.GetComponent<Renderer>().material = headMaterial;
        head.GetComponent<Collider>().isTrigger = false;
    }

    private void Update()
    {
        if (dead || player == null) return;
        if (staggerTimer > 0f)
        {
            staggerTimer -= Time.deltaTime;
            return;
        }
        if (attacking)
        {
            telegraphTimer -= Time.deltaTime;
            if (bodyRenderer != null) bodyRenderer.material.color = Color.Lerp(new Color(0.28f, 0.38f, 0.34f), new Color(1f, 0.25f, 0.12f), 0.6f);
            if (telegraphTimer <= 0f) PerformAttack();
            return;
        }

        Vector3 delta = player.position - transform.position;
        delta.y = 0f;
        if (delta.magnitude > attackRange)
        {
            Vector3 step = delta.normalized * moveSpeed * Time.deltaTime;
            body.MovePosition(body.position + step);
            transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(delta), 8f * Time.deltaTime);
        }
        else if (attackCooldown <= 0f)
        {
            attacking = true;
            telegraphTimer = 0.62f;
            attackCooldown = 1.7f;
        }
        attackCooldown -= Time.deltaTime;
    }

    private void PerformAttack()
    {
        attacking = false;
        if (bodyRenderer != null) bodyRenderer.material.color = new Color(0.28f, 0.38f, 0.34f);
        M0CombatPrototype combat = player.GetComponent<M0CombatPrototype>();
        if (combat != null && Vector3.Distance(player.position, transform.position) <= attackRange + 0.5f)
            combat.ReceiveAttack(attackDamage, transform.position);
    }

    public void TakeHit(float damage, bool weakpoint, Vector3 direction, float force)
    {
        if (dead) return;
        health -= damage;
        if (bodyRenderer != null) bodyRenderer.material.color = Color.white;
        if (weakpoint || health <= 0f)
        {
            Die(direction, force);
            return;
        }
        Stagger(0.22f);
        body.AddForce(direction * force + Vector3.up * 1.5f, ForceMode.Impulse);
    }

    public void Stagger(float duration)
    {
        staggerTimer = Mathf.Max(staggerTimer, duration);
        attacking = false;
        if (bodyRenderer != null) bodyRenderer.material.color = Color.white;
    }

    private void Die(Vector3 direction, float force)
    {
        dead = true;
        body.constraints = RigidbodyConstraints.None;
        bodyCollider.isTrigger = true;
        body.AddForce(direction * force + Vector3.up * (force * 0.45f), ForceMode.Impulse);
        body.AddTorque(Random.insideUnitSphere * 4f, ForceMode.Impulse);
        All.Remove(this);
        Destroy(gameObject, 12f);
    }

    public static PrototypeEnemy ClosestTo(Vector3 position)
    {
        PrototypeEnemy closest = null;
        float distance = float.MaxValue;
        foreach (PrototypeEnemy enemy in All)
        {
            if (enemy == null || enemy.dead) continue;
            float candidate = Vector3.Distance(position, enemy.transform.position);
            if (candidate < distance) { distance = candidate; closest = enemy; }
        }
        return closest;
    }

    private static Material CreateMaterial(Color color)
    {
        Shader shader = Shader.Find("Universal Render Pipeline/Lit");
        if (shader == null) shader = Shader.Find("Standard");
        Material material = new Material(shader);
        material.color = color;
        return material;
    }
}
