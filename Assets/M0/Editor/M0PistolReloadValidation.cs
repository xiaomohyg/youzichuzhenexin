#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace M0.Editor
{
    public static class M0PistolReloadValidation
    {
        static readonly List<string> lines = new List<string>();
        static M0Sandbox sandbox;
        static M0Enemy enemy;
        static int step, failures;
        static double deadline, pauseAt;
        static int pausedStage;
        static float pausedProgress, activeReloadTime;
        static bool dodgePauseChecked;
        static bool originalRunInBackground, originalAutoRespawn, originalPauseWhenUnfocused;
        public static bool Running { get; private set; }

        [MenuItem("M0/Validate Pistol Magazine and Reload")]
        public static void Run()
        {
            if (!EditorApplication.isPlaying || M0Sandbox.Instance == null)
                throw new InvalidOperationException("Open M0Sandbox and enter Play Mode first.");
            if (Running) return;
            sandbox = M0Sandbox.Instance; enemy = sandbox.Enemies[0];
            originalAutoRespawn = sandbox.AutoRespawn; originalPauseWhenUnfocused = sandbox.PauseWhenUnfocused;
            originalRunInBackground = Application.runInBackground;
            lines.Clear(); failures = step = 0; activeReloadTime = 0;
            sandbox.PauseWhenUnfocused = false; sandbox.AutoRespawn = false; Application.runInBackground = true;
            sandbox.Restart(); sandbox.CloseMenu(); sandbox.CloseInventory(); sandbox.Inventory.Select(1);
            foreach (var candidate in sandbox.Enemies) candidate.gameObject.SetActive(false);
            sandbox.Player.ResetPlayer(new Vector3(0, 0.1f, -8));
            enemy.gameObject.SetActive(true); enemy.ResetEnemy(new Vector3(0, 0.02f, -2));
            sandbox.Player.View.transform.LookAt(enemy.Head.position); Physics.SyncTransforms();
            Check(Mathf.Approximately(sandbox.Player.ShotCooldown, 0.7f), "Pistol fire interval is 0.7 seconds");
            Check(Mathf.Approximately(sandbox.Player.ShotDamage, 10) && Mathf.Approximately(sandbox.Player.HeadshotDamage, 25),
                "Pistol body and head damage are 10 and 25");
            Check(sandbox.Player.MagazineAmmo == 13, "Sidearm initializes with a 13-round magazine");
            sandbox.Player.View.transform.LookAt(enemy.transform.position + Vector3.up * 0.95f); Physics.SyncTransforms();
            sandbox.Player.Shoot();
            Check(enemy.Alive && Mathf.Approximately(enemy.Health, enemy.MaxHealth - 10), "Pistol body hit deals exactly 10 damage");
            enemy.ResetEnemy(new Vector3(0, 0.02f, -2));
            sandbox.Player.View.transform.LookAt(enemy.Head.position); Physics.SyncTransforms();
            sandbox.Player.Shoot();
            Check(enemy.Alive && enemy.Health == enemy.MaxHealth - sandbox.Player.HeadshotDamage,
                "A pistol headshot deals its configured damage without a weakpoint instant kill");
            while (sandbox.Player.MagazineAmmo > 0) sandbox.Player.Shoot();
            Check(sandbox.Player.MagazineAmmo == 0, "Thirteen shots empty the magazine");
            sandbox.Player.RequestReload();
            Check(sandbox.Player.Reloading && sandbox.Player.ReloadStage == 0 && sandbox.Player.MagazineAmmo == 0,
                "Reload starts with the discard-magazine stage and no loaded rounds");
            Running = true; deadline = EditorApplication.timeSinceStartup + 12; EditorApplication.update += Update;
        }

        static void Check(bool okay, string message)
        {
            lines.Add((okay ? "PASS " : "FAIL ") + message);
            if (!okay) failures++;
        }

        static void Update()
        {
            if (!Running || !EditorApplication.isPlaying || sandbox == null) { Finish(); return; }
            EditorApplication.QueuePlayerLoopUpdate();
            try
            {
                if (EditorApplication.timeSinceStartup > deadline)
                { Check(false, "Reload validation completed before timeout; stage=" + sandbox.Player.ReloadStage); Finish(); return; }
                var player = sandbox.Player;
                if (player.Reloading && player.Ranged && !player.IsDodging) activeReloadTime += Time.deltaTime;
            if (step == 0 && player.ReloadStage == 0 && player.ReloadStageProgress >= 0.25f)
            {
                Check(player.StartDodge(Vector2.right), "Player can dodge during reload");
                Check(player.Gun.Find("Reload magazine") != null &&
                    (player.Gun.Find("Slide") != null || player.Gun.Find("Reload slide") != null),
                    "Reload rig has visible magazine and slide pieces to animate");
                float before = player.ReloadStageProgress;
                    pauseAt = EditorApplication.timeSinceStartup; pausedProgress = before; step = 1;
                }
                else if (step == 1)
                {
                    if (player.IsDodging)
                    {
                        if (!dodgePauseChecked)
                        {
                            Check(player.MagazineAmmo == 0 && Mathf.Abs(player.ReloadStageProgress - pausedProgress) < 0.08f,
                                "Dodging pauses reload and never grants ammo early");
                            dodgePauseChecked = true;
                        }
                        return;
                    }
                    if (EditorApplication.timeSinceStartup - pauseAt < 0.08) return;
                    step = 2;
                }
                if (step == 2)
                {
                    if (player.ReloadStage == 1)
                    {
                        pausedStage = player.ReloadStage; pausedProgress = player.ReloadStageProgress;
                        sandbox.Inventory.Select(0); pauseAt = EditorApplication.timeSinceStartup; step = 3;
                    }
                }
                else if (step == 3)
                {
                    if (EditorApplication.timeSinceStartup - pauseAt < 0.20) return;
                    Check(player.ReloadStage == pausedStage && Mathf.Abs(player.ReloadStageProgress - pausedProgress) < 0.08f,
                        "Switching to the blade interrupts and preserves the current reload action");
                    sandbox.Inventory.Select(1); step = 4;
                }
                else if (step == 4)
                {
                    if (!player.Reloading)
                    {
                        Check(player.MagazineAmmo == 13, "Reload completes with a full 13-round magazine");
                        Check(Mathf.Abs(activeReloadTime - 1.5f) < 0.12f,
                            "Three active reload stages total 1.5 seconds; dodge and weapon-switch pauses are excluded");
                        Finish();
                    }
                }
            }
            catch (Exception exception) { Check(false, exception.ToString()); Finish(); }
        }

        static void Finish()
        {
            if (!Running) return;
            Running = false; EditorApplication.update -= Update;
            Application.runInBackground = originalRunInBackground;
            if (sandbox != null)
            {
                sandbox.AutoRespawn = originalAutoRespawn; sandbox.PauseWhenUnfocused = originalPauseWhenUnfocused;
                sandbox.Restart(); sandbox.OpenMenu(false);
            }
            Directory.CreateDirectory("Validation");
            lines.Add("RESULT: " + (failures == 0 ? "PASS" : "FAIL") + "; failures=" + failures);
            File.WriteAllLines("Validation/M0-pistol-reload-validation.txt", lines);
            Debug.Log("Pistol reload validation finished: " + failures + " failures");
        }
    }
}
#endif
