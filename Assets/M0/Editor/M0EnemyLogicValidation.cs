#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace M0.Editor
{
    public static class M0EnemyLogicValidation
    {
        static readonly List<string> lines = new List<string>();
        static M0Sandbox sandbox;
        static M0Enemy enemy;
        static int step, errors;
        static double deadline, ready;
        static Vector3 dashOrigin, dashForward;
        static bool sawWindup, sawOpenArc, sawFlash, blockPressedOnFlash;
        static float originalSlamChance;
        static float distanceAtWindup, distanceAtFlash;

        public static bool Running { get; private set; }

        [MenuItem("M0/Validate Enemy Lock Flash and Dodge Attack")]
        public static void Run()
        {
            if (!EditorApplication.isPlaying || M0Sandbox.Instance == null)
                throw new InvalidOperationException("Open M0Sandbox and enter Play Mode first.");
            if (Running) return;
            sandbox = M0Sandbox.Instance;
            originalSlamChance = sandbox.Enemies[0].SlamAttackChance;
            lines.Clear(); errors = step = 0; Running = true;
            sawWindup = sawOpenArc = sawFlash = blockPressedOnFlash = false;
            distanceAtWindup = distanceAtFlash = 0;
            sandbox.PauseWhenUnfocused = false; sandbox.AutoRespawn = false;
            sandbox.Restart(); sandbox.CloseMenu(); sandbox.Inventory.Select(0);
            foreach (var candidate in sandbox.Enemies) candidate.gameObject.SetActive(false);
            sandbox.Player.ResetPlayer(new Vector3(0, 0.1f, -8));
            enemy = sandbox.Enemies[0]; enemy.ResetEnemy(new Vector3(0, 0.02f, -1));
            enemy.SlamAttackChance = 0;
            enemy.transform.rotation = Quaternion.LookRotation(sandbox.Player.transform.position - enemy.transform.position);
            Physics.SyncTransforms();
            deadline = EditorApplication.timeSinceStartup + 8;
            EditorApplication.update += Update;
        }

        static void Check(bool okay, string name)
        {
            lines.Add((okay ? "PASS " : "FAIL ") + name);
            if (!okay) errors++;
        }

        static void Update()
        {
            if (!Running || !EditorApplication.isPlaying || sandbox == null) { Finish(); return; }
            EditorApplication.QueuePlayerLoopUpdate();
            double now = EditorApplication.timeSinceStartup;
            try
            {
                if (now > deadline) { Check(false, "Scenario " + step + " completed before timeout"); Finish(); return; }
                if (step == 0)
                {
                    float distance = Vector3.Distance(enemy.transform.position, sandbox.Player.transform.position);
                    if (enemy.State == EnemyState.Windup && !sawWindup)
                    {
                        sawWindup = true; distanceAtWindup = distance;
                        Check(distanceAtWindup > enemy.AttackRange + 2f && distanceAtWindup <= enemy.EffectiveLockRange + 0.25f,
                            "Enemy locks and begins telegraph well outside the old melee range");
                    }
                    if (enemy.State == EnemyState.Windup && enemy.Rig.TelegraphRing.enabled &&
                        enemy.Rig.TelegraphClosure > 0.05f && enemy.Rig.TelegraphClosure < 0.9f && !sawOpenArc)
                    {
                        sawOpenArc = true;
                        Check(!enemy.Rig.TelegraphRing.loop && enemy.Rig.TelegraphRing.positionCount < 65,
                            "Warning arc is visibly open while charging");
                    }
                    if (enemy.AttackFlashActive && !sawFlash)
                    {
                        sawFlash = true; distanceAtFlash = distance;
                        lines.Add("TRACE flash distance=" + distanceAtFlash.ToString("F2") + " phase=" + enemy.AttackPhase.ToString("F3"));
                        blockPressedOnFlash = true; sandbox.Player.SetBlockOverride(true);
                        Check(enemy.Rig.TelegraphRing.enabled && enemy.Rig.TelegraphRing.loop && enemy.Rig.TelegraphClosure >= 0.999f &&
                            enemy.Rig.TelegraphRing.widthMultiplier >= 0.2f,
                            "Warning arc closes into a bright flash at the strike cue");
                    }
                    if (!sawFlash || enemy.State != EnemyState.Stagger) return;
                    Check(sawWindup && sawOpenArc && sawFlash && distanceAtFlash < distanceAtWindup - 2f,
                        "Enemy rushes forward between distant lock and strike flash");
                    Check(blockPressedOnFlash && sandbox.Player.Health == 100 && sandbox.Player.CounterReady && sandbox.Fx.PerfectGuardMarker,
                        "Blocking on the flash produces a live perfect block");
                    sandbox.Player.SetBlockOverride(null);
                    enemy.gameObject.SetActive(false);
                    sandbox.Player.ResetPlayer(new Vector3(0, 0.1f, -8));
                    dashForward = Quaternion.Euler(0, 37, 0) * Vector3.forward;
                    sandbox.Player.transform.rotation = Quaternion.LookRotation(dashForward);
                    Check(sandbox.Player.StartDodge(new Vector2(0, -1)), "Normal dodge starts without an enemy contact reward");
                    step = 1; ready = now + sandbox.Player.DodgeDuration + 0.04; deadline = now + 4;
                    return;
                }
                if (step == 1)
                {
                    if (now < ready) return;
                    Check(sandbox.Player.DodgeDashAvailable && !sandbox.Player.CounterReady,
                        "Normal dodge opens only the 0.5 second free dash-attack window");
                    dashOrigin = sandbox.Player.transform.position;
                    Check(sandbox.Player.TryStartDodgeDashAttack(), "Left-click replacement can start the free dash attack");
                    step = 2; ready = now + 0.62; return;
                }
                if (step == 2)
                {
                    if (now < ready) return;
                    Vector3 travel = sandbox.Player.transform.position - dashOrigin; travel.y = 0;
                    Check(travel.magnitude > sandbox.Player.DodgeAttackDistance - 0.35f &&
                        Vector3.Dot(travel.normalized, dashForward) > 0.98f,
                        "Dodge attack rushes straight forward without target lock or auto-turning");
                    Check(!sandbox.Player.DodgeDashAttacking, "Dodge dash attack completes cleanly");
                    Finish();
                }
            }
            catch (Exception ex) { Check(false, ex.ToString()); Finish(); }
        }

        static void Finish()
        {
            if (!Running) return;
            Running = false; EditorApplication.update -= Update;
            if (sandbox != null)
            {
                if (enemy != null) enemy.SlamAttackChance = originalSlamChance;
                sandbox.Player.SetBlockOverride(null);
                foreach (var candidate in sandbox.Enemies) candidate.enabled = true;
                sandbox.Restart(); sandbox.OpenMenu(false);
            }
            Directory.CreateDirectory("Validation");
            lines.Add("RESULT: " + (errors == 0 ? "PASS" : "FAIL") + "; failures=" + errors);
            File.WriteAllLines("Validation/M0-enemy-logic-validation.txt", lines);
            Debug.Log("Enemy logic validation finished: " + errors + " failures");
        }
    }
}
#endif
