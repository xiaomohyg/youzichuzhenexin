#if UNITY_EDITOR
using System;
using System.IO;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEditor;
namespace M0.Editor
{
    // Visual review uses actual input and gameplay timings, slowed only during capture.
    public static class M0ActionCapture
    {
        static Keyboard keyboard;
        static Mouse mouse;
        static M0Sandbox sandbox;
        static int shot, stage;
        static double until;
        static string prefix;
        static bool autoRespawn;
        public static bool Running { get; private set; }
        public static void Run(string action)
        {
            if (!EditorApplication.isPlaying || Running) return;
            sandbox = M0Sandbox.Instance; prefix = action; shot = stage = 0;
            autoRespawn = sandbox.AutoRespawn; sandbox.AutoRespawn = false;
            sandbox.Restart(); foreach (var enemy in sandbox.Enemies) enemy.gameObject.SetActive(false);
            if (sandbox.InventoryOpen) sandbox.CloseInventory(); if (sandbox.MenuOpen) sandbox.CloseMenu();
            if (sandbox.Player.ThirdPerson && action != "dodge-body") sandbox.Player.TogglePerspective();
            if (!sandbox.Player.ThirdPerson && action == "dodge-body") sandbox.Player.TogglePerspective();
            keyboard = InputSystem.AddDevice<Keyboard>("M0 action capture"); keyboard.MakeCurrent();
            mouse = InputSystem.AddDevice<Mouse>("M0 action capture mouse"); mouse.MakeCurrent();
            Directory.CreateDirectory("Screenshots/Actions"); Time.timeScale = 0.2f;
            until = EditorApplication.timeSinceStartup + 0.25; Running = true; EditorApplication.update += Update;
        }
        static void Press(int index)
        {
            var control = InputSystem.FindControl(sandbox.Input.Path(index));
            if (control is UnityEngine.InputSystem.Controls.KeyControl key) InputSystem.QueueStateEvent(keyboard, new KeyboardState(key.keyCode));
            else if (sandbox.Input.Path(index).StartsWith("<Mouse>"))
                InputSystem.QueueStateEvent(mouse, new MouseState { buttons = (ushort)(sandbox.Input.Path(index).Contains("rightButton") ? 2 : 1) });
        }
        static void Update()
        {
            if (!EditorApplication.isPlaying || sandbox == null) { Finish(); return; }
            if (EditorApplication.timeSinceStartup < until) return;
            EditorApplication.QueuePlayerLoopUpdate();
            UnityEditorInternal.InternalEditorUtility.RepaintAllViews();
            if (stage == 0)
            {
                Press(prefix.StartsWith("dodge") ? 6 : prefix == "guard" ? 8 : prefix == "heavy" ? 9 : 7);
                stage = 1; until = EditorApplication.timeSinceStartup + 0.12; return;
            }
            if (shot < 8)
            {
                ScreenCapture.CaptureScreenshot("Screenshots/Actions/" + prefix + "-" + shot.ToString("00") + ".png");
                shot++; until = EditorApplication.timeSinceStartup + (prefix == "heavy" ? 0.48 : 0.32);
                if (prefix != "guard") { InputSystem.QueueStateEvent(keyboard, new KeyboardState()); InputSystem.QueueStateEvent(mouse, new MouseState()); }
                return;
            }
            Finish();
        }
        static void Finish()
        {
            EditorApplication.update -= Update; Running = false;
            if (keyboard != null) InputSystem.RemoveDevice(keyboard); if (mouse != null) InputSystem.RemoveDevice(mouse);
            if (sandbox != null) { sandbox.AutoRespawn = autoRespawn; sandbox.Restart(); sandbox.OpenMenu(false); }
            else Time.timeScale = 1;
            Debug.Log("Action captures ready: Screenshots/Actions/" + prefix);
        }
    }
}
#endif
