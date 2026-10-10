#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using M0.Networking;

namespace M0.Editor
{
    // Run in Play Mode. Real Input System events exercise movement, cancellation and rebinding.
    public static class M0Validation
    {
        static readonly List<string> lines = new List<string>();
        static Keyboard keyboard;
        static Mouse mouse;
        static GameObject ceiling;
        static M0Sandbox sandbox;
        static M0Enemy target;
        static int step;
        static double until;
        static Vector3 origin;
        static string savedBindings;
        static string[] livePaths;
        static bool savedExists;
        static bool savedAutoRespawn, savedPauseWhenUnfocused, savedRunInBackground;
        static int errors;
        static readonly string Report = "Validation/M0-validation-optimized.txt";
        static readonly string SaveKey = "M0.Bindings.v1";
        public static bool Running { get; private set; }
        [MenuItem("M0/Validate Running Sandbox")]
        public static void Run()
        {
            if (!EditorApplication.isPlaying || M0Sandbox.Instance == null) throw new InvalidOperationException("Open M0Sandbox and enter Play Mode first.");
            if (Running) return;
            sandbox = M0Sandbox.Instance; lines.Clear(); errors = 0; step = 0; Running = true;
            savedAutoRespawn = sandbox.AutoRespawn; savedPauseWhenUnfocused = sandbox.PauseWhenUnfocused;
            savedRunInBackground = Application.runInBackground;
            sandbox.PauseWhenUnfocused = false; Application.runInBackground = true;
            savedExists = PlayerPrefs.HasKey(SaveKey); savedBindings = PlayerPrefs.GetString(SaveKey, "");
            livePaths = new string[M0Input.Defaults.Length]; for (int i = 0; i < livePaths.Length; i++) livePaths[i] = sandbox.Input.Path(i);
            keyboard = InputSystem.AddDevice<Keyboard>("M0 validation keyboard"); keyboard.MakeCurrent();
            mouse = InputSystem.AddDevice<Mouse>("M0 validation mouse"); mouse.MakeCurrent();
            sandbox.Input.Reset(); sandbox.AutoRespawn = false; sandbox.Restart(); sandbox.CloseMenu();
            foreach (var enemy in sandbox.Enemies) enemy.gameObject.SetActive(false);
            Application.logMessageReceived += OnLog;
            EditorApplication.update += Update;
            until = EditorApplication.timeSinceStartup + 0.2;
        }
        static void OnLog(string message, string stack, LogType type) { if (type == LogType.Error || type == LogType.Exception) { errors++; lines.Add("ERROR: " + message); } }
        static void Check(bool passed, string name) { lines.Add((passed ? "PASS " : "FAIL ") + name); if (!passed) errors++; }
        static void Keys(params Key[] keys) => InputSystem.QueueStateEvent(keyboard, new KeyboardState(keys));
        static void Wait(double seconds) { until = EditorApplication.timeSinceStartup + seconds; step++; }
        static void Update()
        {
            if (!EditorApplication.isPlaying || sandbox == null) { Finish(); return; }
            EditorApplication.QueuePlayerLoopUpdate();
            UnityEditorInternal.InternalEditorUtility.RepaintAllViews();
            if (EditorApplication.timeSinceStartup < until) return;
            try
            {
                switch (step)
                {
                    case 0:
                        Check(CombatRules.Resolve(100, 14, false, true, false, -1, 0.1f, 0.13f, 0.9f).Damage == 0, "Dodge invulnerability");
                        Check(CombatRules.Resolve(100, 14, false, false, true, 0.08f, 0, 0.16f, 0.9f).Kind == HitKind.PerfectBlock, "Perfect block is immediate and time-limited");
                        Check(Mathf.Approximately(CombatRules.Resolve(100, 14, false, false, true, 0.4f, 0, 0.16f, 0.9f).Damage, 1.4f), "Held normal block reduces damage 90%");
                        Check(CombatRules.Resolve(80, 1, true, false, false, -1, 0.1f, 0.13f, 0.9f).Killed, "Weakpoint kills regardless of health");
                        Check(CombatRules.Resolve(100, 14, false, false, false, 0.08f, 0, 0.16f, 0.9f).Damage == 14, "Rear attack bypasses block");
                        origin = sandbox.Player.transform.position; Keys(Key.W); Wait(0.4); break;
                    case 1:
                        Check(sandbox.Player.transform.position.z > origin.z + 0.7f, "Input W moves player");
                        Keys(); sandbox.Player.ResetPlayer(new Vector3(0, 0.1f, -8)); Wait(0.15); break;
                    case 2:
                        origin = sandbox.Player.transform.position; Keys(Key.Q); Wait(0.15); break;
                    case 3:
                        Check(sandbox.Player.InvulnerableNow, "Dodge enters live invulnerability window"); Keys(); Wait(0.45); break;
                    case 4:
                        Check(Mathf.Abs(sandbox.Player.transform.position.z - origin.z - sandbox.Player.DodgeDistance) < 0.25f, "Dodge distance is 3.4m without extra movement");
                        Check(Mathf.Abs(sandbox.Player.View.transform.eulerAngles.z) < 0.01f, "Dodge camera does not roll");
                        sandbox.OpenMenu(false); origin = sandbox.Player.transform.position; Keys(Key.W); Wait(0.2); break;
                    case 5:
                        Check(Vector3.Distance(origin, sandbox.Player.transform.position) < 0.01f, "Menu blocks player movement");
                        Keys(); sandbox.Input.BeginRebind(0); Wait(0.1); break;
                    case 6: Keys(Key.F12); Wait(0.15); break;
                    case 7:
                        Keys(); Check(sandbox.Input.Path(0) == "<Keyboard>/f12", "Interactive rebinding accepts new key");
                        using (var restored = new M0Input()) Check(restored.Path(0) == "<Keyboard>/f12", "New input instance reloads saved binding");
                        sandbox.CloseMenu(); Wait(0.15); break;
                    case 8: origin = sandbox.Player.transform.position; Keys(Key.W); Wait(0.2); break;
                    case 9:
                        Check(Vector3.Distance(origin, sandbox.Player.transform.position) < 0.03f, "Old W key no longer moves after rebind");
                        Keys(Key.F12); Wait(0.3); break;
                    case 10:
                        Check(sandbox.Player.transform.position.z > origin.z + 0.7f, "Rebound F12 moves actual player");
                        Keys(); sandbox.OpenMenu(false); sandbox.Input.BeginRebind(0); Wait(0.1); break;
                    case 11: Keys(Key.S); Wait(0.15); break;
                    case 12:
                        Keys(); Check(sandbox.Input.Path(0) == "<Keyboard>/s" && sandbox.Input.Path(1) == "<Keyboard>/f12", "Duplicate binding swaps instead of losing action");
                        sandbox.Input.Reset(); sandbox.CloseMenu(); sandbox.Player.ResetPlayer(new Vector3(0, 0.1f, -8));
                        target = sandbox.Enemies[0]; target.ResetEnemy(new Vector3(0, 0.02f, -5.8f)); target.enabled = false;
                        Wait(0.1); break;
                    case 13:
                        sandbox.Player.View.transform.LookAt(target.transform.position + Vector3.up * 0.94f); Physics.SyncTransforms(); sandbox.Player.Shoot();
                        Check(target.Health == target.MaxHealth - sandbox.Player.ShotDamage && target.Alive, "Body raycast shot damages without instant death"); Wait(0.1); break;
                    case 14:
                        sandbox.Player.View.transform.LookAt(target.Head); Physics.SyncTransforms(); sandbox.Player.Shoot();
                        Check(!target.Alive && sandbox.Kills == 1, "Head collider raycast kills enemy");
                        Check(sandbox.Fx.HitMarker > 0 && sandbox.Network.HitsObserved >= 2, "Hit events drive HUD and network template");
                        target.enabled = true; Wait(9); break;
                    case 15:
                        Check(target.RagdollActive && target.CorpseSettled, "Ragdoll falls and freezes after ground contact");
                        bool solid = true; foreach (var body in target.GetComponentsInChildren<Rigidbody>()) solid &= body.isKinematic && body.GetComponent<Collider>().enabled;
                        Check(solid, "Settled corpse keeps solid colliders");
                        sandbox.Player.ResetPlayer(new Vector3(0, 0.1f, -8)); target.ResetEnemy(new Vector3(0, 0.02f, -6.7f)); target.enabled = false;
                        Wait(0.1); break;
                    case 16:
                        sandbox.Player.View.transform.LookAt(target.transform.position + Vector3.up * 0.94f); Physics.SyncTransforms();
                        sandbox.Player.StartMelee(true); step = 160; until = EditorApplication.timeSinceStartup + 0.85; break;
                    case 160:
                        lines.Add("TRACE sword sweep health=" + target.Health.ToString("F1") + "/" + target.MaxHealth + " hits=" + sandbox.Player.LastMeleeHitCount + " bounds=" + sandbox.Player.LastMeleeSweepBounds);
                        Check(target.Health < target.MaxHealth && sandbox.Player.LastMeleeHitCount == 1, "Animated sword volume sweeps through and hits nearby enemy once");
                        target.ResetEnemy(new Vector3(0, 0.02f, -6));
                        Keys(); sandbox.Input.BeginRebind(8); step = 17; until = EditorApplication.timeSinceStartup + 0.1; break;
                    case 17: Keys(Key.B); Wait(0.15); break;
                    case 18: Keys(); Wait(0.1); break;
                    case 19: Keys(Key.B); Wait(0.14); break;
                    case 20:
                        var block = sandbox.Player.ReceiveAttack(14, target);
                        Check(block.Kind == HitKind.PerfectBlock && sandbox.Player.Health == 100, "Live perfect block staggers attacker");
                        Keys(); Wait(0.1); break;
                    case 21:
                        var raw = sandbox.Player.ReceiveAttack(14, target);
                        Check(raw.Damage == 14 && sandbox.Player.Health == 86, "Unblocked enemy attack reduces player health");
                        Check(sandbox.Network.CommandsObserved > 0 && sandbox.Network.SnapshotsObserved > 0, "Local transport publishes commands and actor snapshots");
                        sandbox.Player.ResetPlayer(new Vector3(0, 0.1f, -8));
                        target.ResetEnemy(new Vector3(0, 0.02f, -6)); target.enabled = false; Keys(); Wait(0.1); break;
                    case 22: Keys(Key.E); Wait(0.08); break;
                    case 23: Keys(Key.Q); Wait(0.55); break;
                    case 24:
                        Check(target.Health == target.MaxHealth, "Dodge cancels pending heavy attack before damage");
                        Keys(); sandbox.Player.ResetPlayer(new Vector3(0, 0.1f, -8)); target.ResetEnemy(new Vector3(0, 0.02f, -6));
                        Wait(0.15); break;
                    case 25: Keys(Key.E); Wait(0.08); break;
                    case 26: Keys(Key.B); Wait(0.3); break;
                    case 27:
                        Check(target.Health == target.MaxHealth && sandbox.Player.Blocking, "Block cancels pending heavy attack before damage");
                        Keys();
                        for (int i = 0; i < 23; i++)
                        { var e = sandbox.Enemies[i]; e.ResetEnemy(new Vector3(i % 7 - 3, 0.1f, 8 + i / 7 * 2)); e.ApplyHit(100, false, Vector3.forward, 1); }
                        Check(sandbox.CorpseCount == 20 && !sandbox.Enemies[0].gameObject.activeSelf, "Corpse pool retains at most 20, recycles oldest");
                        sandbox.Restart(); Check(sandbox.AliveCount == sandbox.DesiredEnemies && sandbox.Player.Health == 100, "Restart restores pool and player");
                        Check(sandbox.Player.Rig.Body.gameObject.activeSelf && sandbox.Player.Rig.Eyes.parent == sandbox.Player.Rig.Body,
                            "Restart keeps the player body visible and eye anchor independent of head pitch");
                        float nearestEnemy = float.MaxValue;
                        for (int i = 0; i < sandbox.Enemies.Count; i++)
                            if (sandbox.Enemies[i].gameObject.activeSelf)
                                for (int j = i + 1; j < sandbox.Enemies.Count; j++)
                                    if (sandbox.Enemies[j].gameObject.activeSelf)
                                    {
                                        Vector3 delta = sandbox.Enemies[i].transform.position - sandbox.Enemies[j].transform.position;
                                        delta.y = 0; nearestEnemy = Mathf.Min(nearestEnemy, delta.magnitude);
                                    }
                        Check(nearestEnemy >= 2.25f, "Restart assigns active enemies to separated spawn positions");
                        foreach (var e in sandbox.Enemies) e.gameObject.SetActive(false);
                        sandbox.Input.Reset(); sandbox.CloseMenu(); Keys(Key.LeftCtrl); Wait(0.3); break;
                    case 28:
                        Check(sandbox.Player.Crouching && sandbox.Player.GetComponent<CharacterController>().height < 1.2f, "Crouch input lowers collider and eye");
                        ceiling = GameObject.CreatePrimitive(PrimitiveType.Cube); ceiling.name = "Validation low ceiling";
                        ceiling.transform.position = sandbox.Player.transform.position + Vector3.up * 1.45f; ceiling.transform.localScale = new Vector3(2, 0.25f, 2);
                        Keys(); Wait(0.25); break;
                    case 29:
                        Check(sandbox.Player.Crouching, "Low ceiling prevents standing through geometry");
                        UnityEngine.Object.DestroyImmediate(ceiling); ceiling = null; Keys(Key.V); Wait(0.3); break;
                    case 30:
                        Check(sandbox.Player.ThirdPerson && sandbox.Player.Rig.Body.gameObject.activeSelf && Vector3.Distance(sandbox.Player.View.transform.position, sandbox.Player.EyePosition) > 2, "View key reveals body and moves camera behind player");
                        Check(!sandbox.Player.Crouching && sandbox.Player.GetComponent<CharacterController>().height > 1.7f, "Crouch release restores standing height with clearance");
                        ceiling = GameObject.CreatePrimitive(PrimitiveType.Cube); ceiling.name = "Validation camera wall";
                        ceiling.transform.position = sandbox.Player.EyePosition + new Vector3(0, 0, -1.5f); ceiling.transform.localScale = new Vector3(4, 4, 0.2f);
                        Keys(); Wait(0.08); break;
                    case 31:
                        Check(Vector3.Distance(sandbox.Player.View.transform.position, sandbox.Player.EyePosition) < 1.5f, "Third-person camera retracts before a wall");
                        UnityEngine.Object.DestroyImmediate(ceiling); ceiling = null; Keys(Key.V); Wait(0.12); break;
                    case 32:
                        bool firstPersonBody = sandbox.Player.Rig.Body.gameObject.activeSelf;
                        var torsoRenderer = sandbox.Player.Rig.Torso.GetComponent<Renderer>();
                        var leftArmRenderer = sandbox.Player.Rig.BodyLeftArm.GetComponentInChildren<Renderer>(true);
                        var leftLegRenderer = sandbox.Player.Rig.LeftLeg.GetComponentInChildren<Renderer>(true);
                        bool headVisible = true;
                        foreach (var renderer in sandbox.Player.Rig.Head.GetComponentsInChildren<Renderer>(true))
                            headVisible &= renderer.enabled && renderer.shadowCastingMode != UnityEngine.Rendering.ShadowCastingMode.ShadowsOnly;
                        Check(!sandbox.Player.ThirdPerson && firstPersonBody && torsoRenderer.enabled &&
                            torsoRenderer.shadowCastingMode != UnityEngine.Rendering.ShadowCastingMode.ShadowsOnly &&
                            leftArmRenderer.enabled && leftLegRenderer.enabled && headVisible &&
                            sandbox.Player.Rig.Eyes.parent == sandbox.Player.Rig.Body &&
                            !sandbox.Player.Rig.LeftHand.gameObject.activeSelf &&
                            !sandbox.Player.Rig.LeftUpper.gameObject.activeSelf &&
                            !sandbox.Player.Rig.LeftForearm.gameObject.activeSelf,
                            "First-person view keeps the head and body visible with an eye anchor independent of head pitch and no screen-space left hand");
                        Keys(Key.Digit2); Wait(0.12); break;
                    case 33:
                        Check(sandbox.Inventory.Selected == 1 && sandbox.Player.Ranged, "Hotbar key equips sidearm"); Keys();
                        InputSystem.QueueDeltaStateEvent(mouse.scroll, new Vector2(0, -120)); Wait(0.12); break;
                    case 34:
                        Check(sandbox.Inventory.Selected == 2 && !sandbox.Player.Armed, "Actual mouse wheel selects empty slot and holsters weapon");
                        InputSystem.QueueDeltaStateEvent(mouse.scroll, new Vector2(0, 120)); Wait(0.12); break;
                    case 35:
                        Check(sandbox.Inventory.Selected == 1 && sandbox.Player.Ranged, "Reverse wheel equips previous item");
                        sandbox.Inventory.Select(0); sandbox.OpenInventory(); origin = sandbox.Player.transform.position;
                        Keys(Key.W); InputSystem.QueueDeltaStateEvent(mouse.scroll, new Vector2(0, -120)); Wait(0.2); break;
                    case 36:
                        Check(sandbox.InventoryOpen && sandbox.Inventory.Selected == 0 && Vector3.Distance(origin, sandbox.Player.transform.position) < 0.01f, "Inventory blocks combat movement and wheel equipment changes");
                        sandbox.Inventory.Swap(0, 5); Check(!sandbox.Player.Armed && sandbox.Inventory[5].Item == M0Item.Sword, "Moving equipped item into backpack holsters it");
                        sandbox.Inventory.Swap(0, 5); sandbox.CloseInventory(); Keys(); Wait(0.12); break;
                    case 37:
                        sandbox.Inventory.Select(4); sandbox.Inventory.Scroll(-120);
                        Check(sandbox.Inventory.Selected == 0, "Wheel wraps last slot to first");
                        sandbox.Inventory.Scroll(120); Check(sandbox.Inventory.Selected == 4, "Wheel wraps first slot to last"); sandbox.Inventory.Select(0);
                        Check(M0Player.DodgeTravel(0.52f) > 0.8f && M0Player.DodgeTravel(0.9f) - M0Player.DodgeTravel(0.8f) < 0.04f, "Dodge burst and braking have distinct speed phases");
                        // Old saved files remain compatible with appended actions.
                        PlayerPrefs.SetString(SaveKey, "{\"paths\":[\"<Keyboard>/f12\",\"<Keyboard>/s\",\"<Keyboard>/a\",\"<Keyboard>/d\",\"<Keyboard>/leftShift\",\"<Keyboard>/space\",\"<Keyboard>/q\",\"<Mouse>/leftButton\",\"<Mouse>/rightButton\",\"<Keyboard>/e\",\"<Keyboard>/tab\",\"<Keyboard>/r\"]}");
                        using (var migrated = new M0Input()) Check(migrated.Path(0) == "<Keyboard>/f12" && migrated.Path(12) == "<Keyboard>/leftCtrl", "Existing twelve-action bindings migrate without loss");
                        Finish(); break;
                }
            }
            catch (Exception ex) { lines.Add("FAIL exception " + ex); errors++; Finish(); }
        }
        static void Finish()
        {
            EditorApplication.update -= Update; Application.logMessageReceived -= OnLog; Running = false;
            Application.runInBackground = savedRunInBackground;
            if (keyboard != null) InputSystem.RemoveDevice(keyboard);
            if (mouse != null) InputSystem.RemoveDevice(mouse);
            if (ceiling != null) UnityEngine.Object.DestroyImmediate(ceiling);
            if (savedExists) PlayerPrefs.SetString(SaveKey, savedBindings); else PlayerPrefs.DeleteKey(SaveKey);
            if (sandbox != null)
            {
                sandbox.Input.Cancel();
                // Restore the real user's binding paths without writing their preferences again.
                var field = typeof(M0Input).GetField("actions", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                var actions = (InputAction[])field.GetValue(sandbox.Input);
                for (int i = 0; i < actions.Length; i++) actions[i].ApplyBindingOverride(livePaths[i]);
                foreach (var enemy in sandbox.Enemies) enemy.enabled = true;
                sandbox.AutoRespawn = savedAutoRespawn; sandbox.PauseWhenUnfocused = savedPauseWhenUnfocused;
                sandbox.Restart(); sandbox.OpenMenu(false);
            }
            PlayerPrefs.Save(); Directory.CreateDirectory("Validation");
            lines.Add("RESULT: " + (errors == 0 ? "PASS" : "FAIL") + "; failures/errors=" + errors);
            File.WriteAllLines(Report, lines); Debug.Log("M0 validation " + (errors == 0 ? "PASS" : "FAIL") + ": " + Report);
        }
    }
}
#endif
