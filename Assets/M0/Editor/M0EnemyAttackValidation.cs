#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace M0.Editor
{
    public static class M0EnemyAttackValidation
    {
        static readonly List<string> lines = new List<string>();
        static readonly List<float> windups = new List<float>();
        static readonly List<float> dashSpeeds = new List<float>();
        static M0Sandbox sandbox;
        static M0Enemy enemy;
        static int sample, failures;
        static double deadline, attackStartedAt, scenarioStartedAt;
        static float originalSlamChance;
        static bool originalRunInBackground, originalAutoRespawn, originalPauseWhenUnfocused;
        static bool measuringDash, dashSpeedChecked, trackingChecked, movingPlayerHit;
        static bool measuringRecovery, windupMovementChecked;
        static bool sawLeap, sawSlam, slamLocked;
        static Vector3 dashOrigin, dashPrevious, playerAtDashStart, initialDashForward;
        static Vector3 recoveryOrigin, windupOrigin;
        static double recoveryStartedAt, windupStartedAt;
        static float maxDashSpeed, maxDashTurn, maxLeapHeight, leapPlanarTravel;
        static Vector3 leapOrigin, slamOrigin, slamPrevious;
        static Vector3 slamStartForward;
        static float maxSlamTurn;

        public static bool Running { get; private set; }

        [MenuItem("M0/Validate Enemy Attack Variants")]
        public static void Run()
        {
            if (!EditorApplication.isPlaying || M0Sandbox.Instance == null)
                throw new InvalidOperationException("Open M0Sandbox and enter Play Mode first.");
            if (Running) return;
            sandbox = M0Sandbox.Instance;
            originalSlamChance = sandbox.Enemies[0].SlamAttackChance;
            originalAutoRespawn = sandbox.AutoRespawn;
            originalPauseWhenUnfocused = sandbox.PauseWhenUnfocused;
            originalRunInBackground = Application.runInBackground;
            lines.Clear(); windups.Clear(); dashSpeeds.Clear();
            sample = failures = 0;
            measuringDash = measuringRecovery = windupMovementChecked = false;
            sandbox.PauseWhenUnfocused = false; sandbox.AutoRespawn = false; Application.runInBackground = true;
            sandbox.Restart(); sandbox.CloseMenu(); sandbox.CloseInventory(); sandbox.Inventory.Select(0);
            foreach (var candidate in sandbox.Enemies) candidate.gameObject.SetActive(false);
            sandbox.Player.ResetPlayer(new Vector3(0, 0.1f, -8));
            enemy = sandbox.Enemies[0]; enemy.SlamAttackChance = 0;
            ResetEnemyAndFacePlayer();
            Running = true; scenarioStartedAt = EditorApplication.timeSinceStartup; deadline = scenarioStartedAt + 60;
            EditorApplication.update += Update;
        }

        static void ResetEnemyAndFacePlayer()
        {
            sandbox.Player.ResetPlayer(new Vector3(0, 0.1f, -8));
            enemy.ResetEnemy(new Vector3(0, 0.02f, -1));
            enemy.transform.rotation = Quaternion.LookRotation(sandbox.Player.transform.position - enemy.transform.position);
            Physics.SyncTransforms();
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
            double now = EditorApplication.timeSinceStartup;
            try
            {
                if (now > deadline) { Check(false, "Validation completed before timeout; state=" + enemy.State); Finish(); return; }
                if (sample < 4) { ValidateDash(now); return; }
                ValidateSlam(now);
            }
            catch (Exception exception) { Check(false, exception.ToString()); Finish(); }
        }

        static void ValidateDash(double now)
        {
            if (enemy.State == EnemyState.Windup && windups.Count == sample)
            {
                windups.Add(enemy.TelegraphTime); dashSpeeds.Add(enemy.DashSpeed);
                if (sample == 0) { windupOrigin = enemy.transform.position; windupStartedAt = now; }
                Check(enemy.TelegraphTime >= enemy.WindupRange.x && enemy.TelegraphTime <= enemy.WindupRange.y &&
                    enemy.DashSpeed >= enemy.DashSpeedRange.x && enemy.DashSpeed <= enemy.DashSpeedRange.y,
                    "Random windup and high dash speed are within configured bounds");
            }
            if (sample == 0 && enemy.State == EnemyState.Windup && !windupMovementChecked && now - windupStartedAt >= 0.2)
            {
                float drift = Vector3.ProjectOnPlane(enemy.transform.position - windupOrigin, Vector3.up).magnitude;
                Check(drift > 0.15f, "Enemy moves laterally during its charge instead of standing still");
                windupMovementChecked = true;
            }
            if (enemy.State == EnemyState.Attack)
            {
                if (!measuringDash)
                {
                    measuringDash = true; dashSpeedChecked = trackingChecked = false;
                    attackStartedAt = now; dashOrigin = dashPrevious = enemy.transform.position;
                    playerAtDashStart = sandbox.Player.transform.position; initialDashForward = enemy.transform.forward;
                    maxDashSpeed = maxDashTurn = 0;
                }
                else
                {
                    float elapsed = Mathf.Max(0.001f, Time.deltaTime);
                    maxDashSpeed = Mathf.Max(maxDashSpeed, Vector3.Distance(dashPrevious, enemy.transform.position) / elapsed);
                    maxDashTurn = Mathf.Max(maxDashTurn, Vector3.Angle(initialDashForward, enemy.transform.forward));
                    dashPrevious = enemy.transform.position;
                    float attackElapsed = (float)(now - attackStartedAt);
                    sandbox.Player.transform.position = playerAtDashStart + Vector3.right * Mathf.Min(3.2f, attackElapsed * 5f);
                    Physics.SyncTransforms();
                    if (!dashSpeedChecked && attackElapsed >= 0.09f)
                    {
                        Check(maxDashSpeed >= enemy.DashSpeedRange.x * 0.75f,
                            "Randomized dash speed is applied to measured enemy displacement");
                        dashSpeedChecked = true;
                    }
                    if (!trackingChecked && attackElapsed >= 0.11f)
                    {
                        Check(maxDashTurn >= 4f, "Enemy turns during the dash to track the moving player");
                        trackingChecked = true;
                    }
                }
            }
            if (enemy.State == EnemyState.Recovery && measuringDash)
            {
                if (!measuringRecovery)
                {
                    measuringRecovery = true; recoveryStartedAt = now; recoveryOrigin = enemy.transform.position;
                }
                if (now - recoveryStartedAt < 0.18) return;
                float recoveryTravel = Vector3.ProjectOnPlane(enemy.transform.position - recoveryOrigin, Vector3.up).magnitude;
                Check(recoveryTravel > 0.06f, "Enemy keeps moving during post-attack recovery");
                float travel = Vector3.ProjectOnPlane(enemy.transform.position - dashOrigin, Vector3.up).magnitude;
                lines.Add("TRACE dash=" + sample + " configured=" + enemy.DashSpeed.ToString("F1") + " measured=" + maxDashSpeed.ToString("F1") + " turn=" + maxDashTurn.ToString("F1") + " travel=" + travel.ToString("F2"));
                if (sample == 0) movingPlayerHit = sandbox.Player.Health < 100;
                Check(dashSpeedChecked && trackingChecked, "Dash speed and in-dash tracking were observed");
                sample++; measuringDash = measuringRecovery = false;
                if (sample < 4) ResetEnemyAndFacePlayer();
                else
                {
                    Check(HasVariation(windups), "Windup duration varies between attacks");
                    Check(HasVariation(dashSpeeds), "Dash speed varies between attacks");
                    Check(movingPlayerHit, "Rapidly moving player was hit during the tracked dash");
                    BeginSlamScenario();
                }
            }
        }

        static void BeginSlamScenario()
        {
            enemy.SlamAttackChance = 1;
            ResetEnemyAndFacePlayer();
            sawLeap = sawSlam = slamLocked = false;
            maxLeapHeight = leapPlanarTravel = 0;
            maxSlamTurn = 0;
            leapOrigin = enemy.transform.position;
            scenarioStartedAt = EditorApplication.timeSinceStartup;
            deadline = scenarioStartedAt + 15;
            lines.Add("TRACE forced slam scenario started");
        }

        static void ValidateSlam(double now)
        {
            float phaseTime = (float)(now - scenarioStartedAt);
            if (enemy.State == EnemyState.Leap)
            {
                sawLeap = true;
                maxLeapHeight = Mathf.Max(maxLeapHeight, enemy.transform.position.y - leapOrigin.y);
                leapPlanarTravel = Mathf.Max(leapPlanarTravel,
                    Vector3.ProjectOnPlane(enemy.transform.position - leapOrigin, Vector3.up).magnitude);
                sandbox.Player.transform.position = new Vector3(Mathf.Min(1.8f, phaseTime * 3f), 0.1f, -8);
                Physics.SyncTransforms();
            }
            if (enemy.State == EnemyState.Slam && !sawSlam)
            {
                sawSlam = true; slamLocked = true;
                slamOrigin = slamPrevious = enemy.transform.position;
                slamStartForward = enemy.transform.forward;
                Vector3 toPlayer = sandbox.Player.transform.position + Vector3.up * 0.7f - enemy.transform.position;
                Check(Vector3.Dot(enemy.transform.forward, Vector3.ProjectOnPlane(toPlayer, Vector3.up).normalized) > 0.7f,
                    "Airborne strike re-locks toward the player's latest position");
            }
            if (sawSlam && enemy.State == EnemyState.Slam)
            {
                sandbox.Player.transform.position = new Vector3(Mathf.Min(3.0f, phaseTime * 3f), 0.1f, -8);
                Physics.SyncTransforms();
                Vector3 toPlayer = Vector3.ProjectOnPlane(sandbox.Player.transform.position - enemy.transform.position, Vector3.up);
                maxSlamTurn = Mathf.Max(maxSlamTurn, Vector3.Angle(slamStartForward, enemy.transform.forward));
                if (toPlayer.sqrMagnitude > 0.01f)
                    CheckDirectionOnce(Vector3.Dot(enemy.transform.forward, toPlayer.normalized) > 0.45f);
                slamPrevious = enemy.transform.position;
            }
            if (sawSlam && enemy.State == EnemyState.Recovery)
            {
                float diveTravel = Vector3.Distance(slamOrigin, enemy.transform.position);
                Check(sawLeap && maxLeapHeight > 1.4f && leapPlanarTravel > 1.5f,
                    "Slam windup produces a substantial jump with visible horizontal travel");
                Check(slamLocked && diveTravel > 1.5f && maxSlamTurn > 2f,
                    "Enemy travels through an actual airborne dive while re-locking the moving player");
                Check(sandbox.Player.Health < 100, "Airborne slam damages the moving player");
                Finish();
            }
        }

        static bool directionChecked;
        static void CheckDirectionOnce(bool okay)
        {
            if (directionChecked) return;
            directionChecked = true;
            Check(okay, "Airborne dive continues to face the moving player");
        }

        static bool HasVariation(List<float> values)
        {
            for (int i = 1; i < values.Count; i++)
                for (int j = 0; j < i; j++)
                    if (Mathf.Abs(values[i] - values[j]) > 0.005f) return true;
            return false;
        }

        static void Finish()
        {
            if (!Running) return;
            Running = false; EditorApplication.update -= Update;
            directionChecked = false;
            if (enemy != null) enemy.SlamAttackChance = originalSlamChance;
            Application.runInBackground = originalRunInBackground;
            if (sandbox != null)
            {
                sandbox.AutoRespawn = originalAutoRespawn; sandbox.PauseWhenUnfocused = originalPauseWhenUnfocused;
                foreach (var candidate in sandbox.Enemies) candidate.enabled = true;
                sandbox.Restart(); sandbox.OpenMenu(false);
            }
            Directory.CreateDirectory("Validation");
            lines.Add("RESULT: " + (failures == 0 ? "PASS" : "FAIL") + "; failures=" + failures);
            File.WriteAllLines("Validation/M0-enemy-attack-validation.txt", lines);
            Debug.Log("Enemy attack validation finished: " + failures + " failures");
        }
    }
}
#endif
