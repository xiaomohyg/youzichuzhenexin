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
        static bool windupChecked, autoRespawn;
        static readonly List<string> lines = new List<string>();
        static readonly List<string> trajectory = new List<string>();
        public static bool Running { get; private set; }
        [MenuItem("M0/Validate Contact Combat Upgrade")]
        public static void Run()
        {
            if (!EditorApplication.isPlaying || Running) return;
            s = M0Sandbox.Instance; autoRespawn = s.AutoRespawn; s.AutoRespawn = false;
            keyboard = InputSystem.AddDevice<Keyboard>("Contact combat validation"); keyboard.MakeCurrent();
            mouse = InputSystem.AddDevice<Mouse>("Contact combat mouse"); mouse.MakeCurrent();
            lines.Clear(); trajectory.Clear(); errors = step = capture = 0; fixedStep = Time.fixedDeltaTime;
            Directory.CreateDirectory("Screenshots/EnemyUpgrade");
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
            foreach (var e in s.Enemies) { e.enabled = true; e.gameObject.SetActive(false); }
            s.DodgeEffect.Clear(); s.Player.ResetPlayer(new Vector3(0, 0.1f, -8)); s.Inventory.Select(0);
            if (s.InventoryOpen) s.CloseInventory(); if (s.MenuOpen) s.CloseMenu();
            if (s.Player.ThirdPerson) s.Player.TogglePerspective();
            enemy = s.Enemies[0]; enemy.ResetEnemy(new Vector3(0, 0.02f, -6.6f));
            enemy.transform.rotation = Quaternion.Euler(0, 180, 0);
            Physics.SyncTransforms(); windupChecked = false;
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
                    { ScreenCapture.CaptureScreenshot("Screenshots/EnemyUpgrade/punch-" + (capture++).ToString("00") + ".png"); }
                }
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
                        if (enemy.State != EnemyState.Attack || enemy.AttackPhase < 0.4f) break;
                        Action(6, 2); Next(10); break;
                    case 10:
                        if (!s.DodgeEffect.Active) break;
                        Action(); Check(s.Player.Health == 100 && s.Player.CounterReady && Time.timeScale < 0.4f, "Actual avoided swing triggers grayscale slow motion and counter without damage");
                        Check(s.Player.View.GetComponent<UnityEngine.Rendering.Universal.UniversalAdditionalCameraData>().renderPostProcessing, "URP camera post processing enabled for real grayscale");
                        ScreenCapture.CaptureScreenshot("Screenshots/EnemyUpgrade/successful-dodge-gray.png");
                        Next(11); ready = now + 0.06; break;
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
                        s.CloseInventory(); enemy.gameObject.SetActive(false); Next(14); ready = now + 1; break;
                    case 14:
                        if (now < ready) break;
                        Check(!s.DodgeEffect.Active && Time.timeScale == 1 && Mathf.Approximately(Time.fixedDeltaTime, fixedStep), "Effect expires smoothly and restores clock and physics step");
                        Setup(); Action(8); start = s.Player.transform.position; Next(15); break;
                    case 15:
                        if (enemy.State != EnemyState.Recovery) break;
                        Check(Mathf.Abs(s.Player.Health - 98.6f) < 0.01f && s.Player.GuardImpact > 0.1f, "Animated contact into held guard reduces 90% damage and triggers recoil");
                        enemy.enabled = false; Next(16); ready = now + 0.25; break;
                    case 16:
                        if (now < ready) break;
                        Check(s.Player.transform.position.z < start.z - 0.08f, "Guard impact physically pushes player backward");
                        Setup(); enemy.enabled = false; Action(8); wall = Wall(new Vector3(0, 1.5f, -8.48f)); Next(17); ready = now + 0.4; break;
                    case 17:
                        if (now < ready) break;
                        s.Player.ReceiveAttack(14, enemy); Next(18); ready = now + 0.3; break;
                    case 18:
                        if (now < ready) break;
                        Check(s.Player.transform.position.z > -8.18f, "Guard recoil respects rear-wall collision");
                        s.DodgeEffect.Trigger(); s.Restart();
                        Check(!s.DodgeEffect.Active && Time.timeScale == 1 && Mathf.Approximately(Time.fixedDeltaTime, fixedStep), "Restart clears active grayscale and slow motion");
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
            if (s != null) { foreach (var e in s.Enemies) e.enabled = true; s.AutoRespawn = autoRespawn; s.Restart(); s.OpenMenu(false); }
            Directory.CreateDirectory("Validation"); lines.Add("RESULT: " + (errors == 0 ? "PASS" : "FAIL") + "; failures=" + errors);
            File.WriteAllLines("Validation/M0-contact-validation.txt", lines); File.WriteAllLines("Validation/M0-punch-trajectory.txt", trajectory);
            Debug.Log("Contact combat validation finished: " + errors + " failures");
        }
    }
}
#endif
