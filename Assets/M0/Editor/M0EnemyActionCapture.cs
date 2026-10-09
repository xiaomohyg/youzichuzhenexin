#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

namespace M0.Editor
{
    // Records the actual AI attack cycle, rather than placing the rig in invented poses.
    public static class M0EnemyActionCapture
    {
        static M0Sandbox sandbox;
        static M0Enemy enemy;
        static Camera camera;
        static RenderTexture target;
        static Texture2D pixels;
        static Mouse mouse;
        static Keyboard keyboard;
        static bool guard, autoRespawn, previousPerspective;
        static int frame;
        static float lastShot, began;
        static double deadline;
        static string prefix;
        public static bool Running { get; private set; }
        public static void Run(bool captureGuard = false)
        {
            if (!EditorApplication.isPlaying || Running) return;
            sandbox = M0Sandbox.Instance; autoRespawn = sandbox.AutoRespawn; previousPerspective = sandbox.Player.ThirdPerson;
            sandbox.AutoRespawn = false; sandbox.Restart(); foreach (var e in sandbox.Enemies) e.gameObject.SetActive(false);
            if (sandbox.InventoryOpen) sandbox.CloseInventory(); if (sandbox.MenuOpen) sandbox.CloseMenu();
            guard = captureGuard; if (sandbox.Player.ThirdPerson != guard) sandbox.Player.TogglePerspective();
            enemy = sandbox.Enemies[0]; enemy.ResetEnemy(new Vector3(0, 0.02f, -6.6f)); enemy.transform.rotation = Quaternion.Euler(0, 180, 0);
            camera = new GameObject("Temporary action-review camera").AddComponent<Camera>(); camera.enabled = false;
            camera.transform.position = guard ? new Vector3(3.3f, 2.3f, -9.7f) : new Vector3(2.9f, 2.3f, -10.8f);
            camera.transform.LookAt(new Vector3(0, 1.15f, -7)); camera.fieldOfView = 49;
            camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(0.04f, 0.07f, 0.09f);
            camera.cullingMask = guard ? ~0 : ~(1 << 2);
            target = new RenderTexture(960, 640, 24); camera.targetTexture = target;
            pixels = new Texture2D(960, 640, TextureFormat.RGB24, false);
            keyboard = InputSystem.AddDevice<Keyboard>("Guard review keyboard"); keyboard.MakeCurrent();
            mouse = InputSystem.AddDevice<Mouse>("Guard review mouse"); mouse.MakeCurrent();
            if (guard)
            {
                var path = sandbox.Input.Path(8);
                if (InputSystem.FindControl(path) is UnityEngine.InputSystem.Controls.KeyControl key)
                    InputSystem.QueueStateEvent(keyboard, new KeyboardState(key.keyCode));
                else InputSystem.QueueStateEvent(mouse, new MouseState { buttons = (ushort)(path.Contains("rightButton") ? 2 : 1) });
            }
            prefix = guard ? "guard-recoil" : "enemy-fullbody";
            Directory.CreateDirectory("Screenshots/EnemyUpgrade");
            frame = 0; lastShot = -1; began = Time.time; deadline = EditorApplication.timeSinceStartup + 18;
            Time.timeScale = 0.2f; Running = true; EditorApplication.update += Update;
        }
        static void Capture()
        {
            var old = RenderTexture.active;
            camera.Render(); RenderTexture.active = target; pixels.ReadPixels(new Rect(0, 0, 960, 640), 0, 0); pixels.Apply();
            File.WriteAllBytes("Screenshots/EnemyUpgrade/" + prefix + "-" + (frame++).ToString("00") + ".png", pixels.EncodeToPNG());
            RenderTexture.active = old; lastShot = Time.time;
        }
        static void Update()
        {
            if (!EditorApplication.isPlaying || sandbox == null) { Finish(); return; }
            EditorApplication.QueuePlayerLoopUpdate(); UnityEditorInternal.InternalEditorUtility.RepaintAllViews();
            if (EditorApplication.timeSinceStartup > deadline) { Finish(); return; }
            if (Time.time - lastShot >= (enemy.State == EnemyState.Attack ? 0.065f : 0.2f)) Capture();
            if (enemy.State == EnemyState.Recovery && Time.time - began > enemy.TelegraphTime + enemy.AttackDuration + 0.70f) Finish();
        }
        static void Finish()
        {
            EditorApplication.update -= Update; Running = false;
            if (camera != null) UnityEngine.Object.DestroyImmediate(camera.gameObject);
            if (target != null) { target.Release(); UnityEngine.Object.DestroyImmediate(target); }
            if (pixels != null) UnityEngine.Object.DestroyImmediate(pixels);
            if (keyboard != null) InputSystem.RemoveDevice(keyboard); if (mouse != null) InputSystem.RemoveDevice(mouse);
            if (sandbox != null)
            {
                if (sandbox.Player.ThirdPerson != previousPerspective) sandbox.Player.TogglePerspective();
                sandbox.AutoRespawn = autoRespawn; sandbox.Restart(); sandbox.OpenMenu(false);
            }
            Debug.Log("Actual animation review captures: Screenshots/EnemyUpgrade/" + prefix);
        }
    }
}
#endif
