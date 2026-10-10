#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEditor;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

namespace M0.Editor
{
    public static class M0CombatUpgradeValidation
    {
        static M0Sandbox s;
        static M0Enemy enemy;
        static Keyboard keyboard;
        static Mouse mouse;
        static GameObject wall;
        static int step, errors, capture;
        static double deadline, ready;
        static float pausedScale, fixedStep;
        static Vector3 start;
        static bool windupChecked, dodgeObserved, guardImpactObserved, autoRespawn;
        static bool pauseWhenUnfocused;
        static bool runInBackground;
        static readonly List<string> lines = new List<string>();
        static readonly List<string> trajectory = new List<string>();
        public static bool Running { get; private set; }
        [MenuItem("M0/Validate Contact Combat Upgrade")]
        public static void Run()
        {
            if (!EditorApplication.isPlaying || Running) return;
            s = M0Sandbox.Instance; autoRespawn = s.AutoRespawn; pauseWhenUnfocused = s.PauseWhenUnfocused; runInBackground = Application.runInBackground;
            s.AutoRespawn = false; s.PauseWhenUnfocused = false;
            Application.runInBackground = true;
            keyboard = InputSystem.AddDevice<Keyboard>("Contact combat validation"); keyboard.MakeCurrent();
            mouse = InputSystem.AddDevice<Mouse>("Contact combat mouse"); mouse.MakeCurrent();
            lines.Clear(); trajectory.Clear(); errors = step = capture = 0; fixedStep = Time.fixedDeltaTime;
            Directory.CreateDirectory("Screenshots/CombatTune");
            Check(CombatRules.Resolve(100, 14, false, false, true, 0.08f, 0, 0.16f, 0.9f).Kind == HitKind.PerfectBlock,
                "Perfect block opens immediately on guard input");
            Check(Mathf.Approximately(CombatRules.Resolve(100, 14, false, false, true, 0.4f, 0, 0.16f, 0.9f).Damage, 1.4f),
                "Holding guard transitions to normal 90 percent mitigation");
            Setup(); Running = true; EditorApplication.update += Update;
        }
        static void Action(params int[] actions)
        {
            var keys = new List<Key>(); ushort buttons = 0;
            foreach (int index in actions)
            {
                string path = s.Input.Path(index);
                if (InputSystem.FindControl(path) is UnityEngine.InputSystem.Controls.KeyControl key) keys.Add(key.keyCode);
                else if (path.Contains("rightButton")) buttons |= 2;
                else if (path.Contains("leftButton")) buttons |= 1;
            }
            InputSystem.QueueStateEvent(keyboard, new KeyboardState(keys.ToArray()));
            InputSystem.QueueStateEvent(mouse, new MouseState { buttons = buttons });
        }
        static void Setup()
        {
            Action(); if (wall != null) UnityEngine.Object.DestroyImmediate(wall);
            // Close pause surfaces before resetting the time effect so a warm rerun cannot inherit timeScale=0.
            if (s.InventoryOpen) s.CloseInventory(); if (s.MenuOpen) s.CloseMenu();
            s.DodgeEffect.Clear(); s.DodgeEffect.ApplyClock();
            Time.timeScale = 1; Time.fixedDeltaTime = fixedStep;
            s.Player.SetBlockOverride(null);
            foreach (var e in s.Enemies) { e.enabled = true; e.gameObject.SetActive(false); }
            s.Player.ResetPlayer(new Vector3(0, 0.1f, -8)); s.Inventory.Select(0);
            if (s.Player.ThirdPerson) s.Player.TogglePerspective();
            enemy = s.Enemies[0]; enemy.ResetEnemy(new Vector3(0, 0.02f, -6.6f));
            enemy.enabled = true;
            enemy.transform.rotation = Quaternion.Euler(0, 180, 0);
            Physics.SyncTransforms(); windupChecked = dodgeObserved = guardImpactObserved = false;
            deadline = EditorApplication.timeSinceStartup + 5; ready = 0;
        }
        static void Check(bool okay, string name) { lines.Add((okay ? "PASS " : "FAIL ") + name); if (!okay) errors++; }
        static void Next(int next) { step = next; deadline = EditorApplication.timeSinceStartup + 5; ready = 0; }
        static GameObject Wall(Vector3 at)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube); go.name = "Contact validation temporary cover";
            go.transform.position = at; go.transform.localScale = new Vector3(4, 3, 0.1f); Physics.SyncTransforms(); return go;
        }
        static void Update()
        {
            if (!EditorApplication.isPlaying || s == null) { Finish(); return; }
            EditorApplication.QueuePlayerLoopUpdate(); UnityEditorInternal.InternalEditorUtility.RepaintAllViews();
            double now = EditorApplication.timeSinceStartup;
            try
            {
                if (now > deadline) { Check(false, "Scenario " + step + " reached timeout; state=" + enemy.State + " hp=" + s.Player.Health); Finish(); return; }
                if (step == 0 && (enemy.State == EnemyState.Windup || enemy.State == EnemyState.Attack))
                {
                    trajectory.Add(Time.time.ToString("F3") + " " + enemy.State + " phase=" + enemy.AttackPhase.ToString("F3") + " fist=" + enemy.Rig.StrikeShape.transform.position.ToString("F3") + " hp=" + s.Player.Health);
                    if (capture < 8 && (enemy.State == EnemyState.Attack || capture == 0))
                    { ScreenCapture.CaptureScreenshot("Screenshots/CombatTune/punch-" + (capture++).ToString("00") + ".png"); }
                }
                if (step == 15 && s.Player.GuardImpact > 0.1f) guardImpactObserved = true;
                switch (step)
                {
                    case 0:
                        if (enemy.State == EnemyState.Windup && !windupChecked)
                        { Check(s.Player.Health == 100, "Windup cannot damage player"); windupChecked = true; }
                        if (enemy.State != EnemyState.Recovery) break;
                        Check(s.Player.Health == 86, "Actual animated gauntlet contacts player and causes exactly one 14-damage hit");
                        Check(enemy.Rig.StrikeShape.size == Vector3.one && enemy.Rig.StrikeShape.transform.localScale == new Vector3(0.34f, 0.30f, 0.36f), "Damage box matches visible gauntlet size");
                        enemy.enabled = false; ready = now + 0.35; Next(1); ready = now + 0.35; break;
                    case 1:
                        if (now < ready) break;
                        Check(s.Player.Health == 86, "Recovery and persistent model overlap do not repeat damage"); Setup(); Next(2); break;
                    case 2:
                        if (enemy.State != EnemyState.Attack) break;
                        s.Player.ResetPlayer(new Vector3(1.15f, 0.1f, -8)); Physics.SyncTransforms(); Next(3); break;
                    case 3:
                        if (enemy.State != EnemyState.Recovery) break;
                        Check(s.Player.Health == 100, "Side-step out of actual swing misses despite nearby player"); Setup(); Next(4); break;
                    case 4:
                        if (enemy.State != EnemyState.Windup) break;
                        wall = Wall(new Vector3(0, 1.5f, -7.32f)); Next(5); break;
                    case 5:
                        if (enemy.State != EnemyState.Recovery) break;
                        Check(s.Player.Health == 100, "Cover placed during windup blocks attack contact through wall"); Setup(); Next(6); break;
                    case 6:
                        if (enemy.State != EnemyState.Windup) break;
                        enemy.Stagger(0.8f); Next(7); ready = now + 0.55; break;
                    case 7:
                        if (now < ready) break;
                        Check(s.Player.Health == 100 && enemy.State == EnemyState.Stagger, "Stagger interrupts pending attack and cancels damage");
                        Setup(); enemy.gameObject.SetActive(false); Action(6, 2); Next(8); ready = now + 0.65; break;
                    case 8:
                        if (now < ready) break;
                        Check(!s.DodgeEffect.Active && Time.timeScale == 1 && !s.Player.CounterReady, "Empty dodge has no slow motion or counter reward");
                        Setup(); Next(9); break;
                    case 9:
                        if (enemy.State != EnemyState.Attack || enemy.AttackPhase < 0.25f) break;
                        trajectory.Add("DODGE INPUT phase=" + enemy.AttackPhase.ToString("F3") + " player=" + s.Player.transform.position);
                        s.Player.StartDodge(Vector2.left); Next(10); break;
                    case 10:
                        if (s.Player.IsDodging && !dodgeObserved)
                        { dodgeObserved = true; trajectory.Add("DODGE OBSERVED progress=" + s.Player.DodgeProgress.ToString("F3") + " origin=" + s.Player.DodgeOrigin + " player=" + s.Player.transform.position); }
                        if (!s.DodgeEffect.Active) break;
                        Check(s.Player.Health == 100 && s.Player.CounterReady && Time.timeScale < 0.4f, "Actual avoided swing triggers grayscale slow motion and counter without damage");
                        Check(s.Player.View.GetComponent<UnityEngine.Rendering.Universal.UniversalAdditionalCameraData>().renderPostProcessing, "URP camera post processing enabled for real grayscale");
                        Check(s.DodgeEffect.Duration >= 1.1f, "Successful-dodge time dilation lasts longer than one second");
                        ScreenCapture.CaptureScreenshot("Screenshots/CombatTune/successful-dodge-gray.png");
                        s.Inventory.Select(1); Check(s.Player.TryStartCounter(true), "Sidearm counter accepts immediate activation during the successful dodge"); Next(101); ready = now + 0.08; break;
                    case 101:
                        if (now < ready) break;
                        Check((s.Player.CounterQueued && s.Player.CounterReady) || (s.Player.CounterAttacking && s.Player.CounterRanged), "Sidearm counter input is buffered or starts its dedicated sliding shot immediately");
                        Next(102); ready = now + 0.62; break;
                    case 102:
                        if (now < ready) break;
                        Check(enemy.Health < enemy.MaxHealth && !s.Player.CounterAttacking, "Buffered sidearm dodge counter fires and completes in under 0.75 real seconds despite time dilation");
                        Check(s.DodgeEffect.Active, "Sidearm counter timing does not consume or cancel the remaining dodge slow motion");
                        s.Inventory.Select(0); Next(11); ready = now + 0.03; break;
                    case 11:
                        if (now < ready) break;
                        pausedScale = s.DodgeEffect.GameplayScale; s.OpenMenu(false); Next(12); ready = now + 0.9; break;
                    case 12:
                        if (now < ready) break;
                        Check(Time.timeScale == 0 && s.DodgeEffect.Active && s.DodgeEffect.GameplayScale == pausedScale, "Pause freezes effect clock beyond its normal duration");
                        s.CloseMenu(); Check(Time.timeScale == pausedScale, "Resume restores remaining slow-motion speed");
                        s.OpenInventory(); Next(13); ready = now + 0.9; break;
                    case 13:
                        if (now < ready) break;
                        Check(s.InventoryOpen && Time.timeScale == 0 && s.DodgeEffect.Active, "Inventory also freezes slow-motion clock without unpausing");
                        s.CloseInventory(); enemy.gameObject.SetActive(false); Next(14); ready = now + s.DodgeEffect.Duration + 0.25; break;
                    case 14:
                        if (now < ready) break;
                        Check(!s.DodgeEffect.Active && Time.timeScale == 1 && Mathf.Approximately(Time.fixedDeltaTime, fixedStep), "Effect expires smoothly and restores clock and physics step");
                        Setup(); enemy.enabled = false; s.Player.SetBlockOverride(true); Next(140); ready = now + 0.08; break;
                    case 140:
                        if (now < ready) break;
                        if (!s.Player.Blocking) { ready = now + 0.04; break; }
                        start = s.Player.transform.position; enemy.enabled = true; Next(15); break;
                    case 15:
                        if (enemy.State != EnemyState.Recovery) break;
                        trajectory.Add("GUARD RESULT health=" + s.Player.Health.ToString("F2") + " impact=" + s.Player.GuardImpact.ToString("F3") + " position=" + s.Player.transform.position);
                        Check(Mathf.Abs(s.Player.Health - 98.6f) < 0.01f && guardImpactObserved, "Animated fist collides with sword volume, blocks for 90% mitigation and triggers impact");
                        enemy.enabled = false; Next(16); ready = now + 0.25; break;
                    case 16:
                        if (now < ready) break;
                        Check(s.Player.transform.position.z < start.z - 0.08f, "Guard impact physically pushes player backward");
                        Setup(); enemy.enabled = false; s.Player.SetBlockOverride(true); wall = Wall(new Vector3(0, 1.5f, -8.48f)); Next(17); ready = now + 0.4; break;
                    case 17:
                        if (now < ready) break;
                        s.Player.ReceiveAttack(14, enemy); Next(18); ready = now + 0.3; break;
                    case 18:
                        if (now < ready) break;
                        Check(s.Player.transform.position.z > -8.18f, "Guard recoil respects rear-wall collision");
                        Setup(); enemy.ResetEnemy(new Vector3(0, 0.02f, -4.5f)); enemy.enabled = false; s.Player.SetBlockOverride(true); Next(19); ready = now + 0.05; break;
                    case 19:
                        if (now < ready) break;
                        var perfectPosition = s.Player.transform.position;
                        var parry = s.Player.ReceiveAttack(14, enemy);
                        Check(parry.Kind == HitKind.PerfectBlock && s.Player.Health == 100, "Live perfect block negates contact damage");
                        Check(s.Player.GuardImpact > 1.2f && s.Player.transform.position == perfectPosition, "Perfect block has stronger impact and no player knockback");
                        Check(s.Fx.Particles.particleCount >= 48, "Perfect block emits a dense contact spark burst with a white-hot core");
                        Check(s.Player.TryStartCounter(false), "Perfect-block follow-up can start melee counter without input timing dependency"); Next(20); ready = now + 0.05; break;
                    case 20:
                        if (now < ready) break;
                        Check(s.Player.CounterAttacking && !s.Player.CounterRanged, "Perfect-block follow-up starts a distinct melee counter");
                        Action(); Next(21); ready = now + 0.72; break;
                    case 21:
                        if (now < ready) break;
                        Check(enemy.Health < enemy.MaxHealth && s.Player.Health == 100, "Melee counter auto-locks and damages the visible target");
                        Setup(); enemy.ResetEnemy(new Vector3(0, 0.02f, -4.5f)); enemy.enabled = false; s.Player.SetBlockOverride(true); Next(22); ready = now + 0.05; break;
                    case 22:
                        if (now < ready) break;
                        var rangedParry = s.Player.ReceiveAttack(14, enemy);
                        Check(rangedParry.Kind == HitKind.PerfectBlock && s.Player.CounterReady, "Perfect block grants a counter before ranged weapon swap");
                        s.Inventory.Select(1); Check(s.Player.TryStartCounter(true), "Sidearm parry counter starts through the dedicated ranged action"); Next(23); ready = now + 0.05; break;
                    case 23:
                        if (now < ready) break;
                        Check(s.Player.CounterAttacking && s.Player.CounterRanged, "Sidearm counter uses its own lock-on firing action");
                        Action(); Next(24); ready = now + 0.72; break;
                    case 24:
                        if (now < ready) break;
                        Check(enemy.Health < enemy.MaxHealth && s.Player.Health == 100, "Ranged counter auto-locks and damages the visible target");
                        s.DodgeEffect.Trigger(); s.Restart();
                        Check(!s.DodgeEffect.Active && Time.timeScale == 1 && Mathf.Approximately(Time.fixedDeltaTime, fixedStep), "Restart clears active grayscale and slow motion");
                        Setup(); enemy.ResetEnemy(new Vector3(-0.28f, 0.02f, -7.05f)); enemy.enabled = false;
                        var second = s.Enemies[1]; second.ResetEnemy(new Vector3(0.28f, 0.02f, -7.05f)); second.enabled = false;
                        var outside = s.Enemies[2]; outside.ResetEnemy(new Vector3(1.45f, 0.02f, -7.05f)); outside.enabled = false;
                        s.Player.StartMelee(false); Next(25); ready = now + 0.82; break;
                    case 25:
                        if (now < ready) break;
                        var sweptSecond = s.Enemies[1]; var sweptOutside = s.Enemies[2];
                        trajectory.Add("PLAYER SWEEP bounds=" + s.Player.LastMeleeSweepBounds + " hits=" + s.Player.LastMeleeHitCount);
                        Check(s.Player.LastMeleeHitCount >= 2 && enemy.Health < enemy.MaxHealth && sweptSecond.Health < sweptSecond.MaxHealth,
                            "One animated sword arc can damage every enemy model swept by the blade");
                        Check(sweptOutside.Health == sweptOutside.MaxHealth, "Sword sweep does not damage an enemy outside the model trajectory");
                        Finish(); break;
                }
            }
            catch (Exception ex) { Check(false, ex.ToString()); Finish(); }
        }
        static void Finish()
        {
            EditorApplication.update -= Update; Running = false;
            if (keyboard != null) InputSystem.RemoveDevice(keyboard); if (mouse != null) InputSystem.RemoveDevice(mouse);
            if (wall != null) UnityEngine.Object.DestroyImmediate(wall);
            Application.runInBackground = runInBackground;
            if (s != null) { foreach (var e in s.Enemies) e.enabled = true; s.AutoRespawn = autoRespawn; s.PauseWhenUnfocused = pauseWhenUnfocused; s.Restart(); s.OpenMenu(false); }
            Directory.CreateDirectory("Validation"); lines.Add("RESULT: " + (errors == 0 ? "PASS" : "FAIL") + "; failures=" + errors);
            File.WriteAllLines("Validation/M0-combat-tune-validation.txt", lines); File.WriteAllLines("Validation/M0-combat-tune-trajectory.txt", trajectory);
            Debug.Log("Contact combat validation finished: " + errors + " failures");
        }
    }
}
#endif
