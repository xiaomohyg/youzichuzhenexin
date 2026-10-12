using System;
using UnityEngine;
using UnityEngine.InputSystem;

namespace M0
{
    // One action per direction lets keyboard movement be rebound without breaking a composite.
    public sealed class M0Input : IDisposable
    {
        public static readonly string[] Names = { "前进", "后退", "向左", "向右", "奔跑", "跳跃", "闪避", "普通攻击 / 射击", "格挡", "重击", "切换武器", "重置沙盒", "下蹲（按住）", "第一 / 第三人称", "物品栏", "快捷栏 1", "快捷栏 2", "快捷栏 3", "快捷栏 4", "快捷栏 5", "手枪换弹" };
        public static readonly string[] Defaults = { "<Keyboard>/w", "<Keyboard>/s", "<Keyboard>/a", "<Keyboard>/d", "<Keyboard>/leftShift", "<Keyboard>/space", "<Keyboard>/q", "<Mouse>/leftButton", "<Mouse>/rightButton", "<Keyboard>/e", "<Keyboard>/tab", "<Keyboard>/f10", "<Keyboard>/leftCtrl", "<Keyboard>/v", "<Keyboard>/i", "<Keyboard>/1", "<Keyboard>/2", "<Keyboard>/3", "<Keyboard>/4", "<Keyboard>/5", "<Keyboard>/r" };
        const string SaveKey = "M0.Bindings.v1";
        [Serializable] class Saved { public string[] paths; }
        readonly InputAction[] actions = new InputAction[Defaults.Length];
        InputActionRebindingExtensions.RebindingOperation operation;
        public int Rebinding { get; private set; } = -1;
        public string Status { get; private set; } = "点击按键即可修改；Esc 取消。重复按键会交换。";
        public bool Blocked { get; set; }
        public M0Input()
        {
            for (int i = 0; i < actions.Length; i++) { actions[i] = new InputAction("Action" + i, InputActionType.Button, Defaults[i]); actions[i].Enable(); }
            try
            {
                var saved = JsonUtility.FromJson<Saved>(PlayerPrefs.GetString(SaveKey, ""));
                // Append new actions without discarding the player's original twelve bindings.
                if (saved?.paths != null)
                    for (int i = 0; i < Mathf.Min(saved.paths.Length, actions.Length); i++)
                        if (Allowed(saved.paths[i])) actions[i].ApplyBindingOverride(saved.paths[i]);
                if (saved?.paths != null && saved.paths.Length <= 20 &&
                    string.Equals(Path(11), "<Keyboard>/r", StringComparison.OrdinalIgnoreCase))
                {
                    actions[11].ApplyBindingOverride("<Keyboard>/f10");
                    actions[20].ApplyBindingOverride("<Keyboard>/r");
                }
                if (saved?.paths != null)
                    for (int i = saved.paths.Length; i < actions.Length; i++)
                        for (int j = 0; j < i; j++)
                            if (string.Equals(Path(i), Path(j), StringComparison.OrdinalIgnoreCase))
                            {
                                for (int key = 1; key <= 12; key++)
                                {
                                    string candidate = "<Keyboard>/f" + key; bool used = false;
                                    for (int k = 0; k < actions.Length; k++) if (k != i && string.Equals(Path(k), candidate, StringComparison.OrdinalIgnoreCase)) used = true;
                                    if (!used) { actions[i].ApplyBindingOverride(candidate); break; }
                                }
                                break;
                            }
            }
            catch (Exception) { Status = "已恢复默认按键。"; }
        }
        static bool Allowed(string path) => !string.IsNullOrEmpty(path) && path != "<Keyboard>/escape" && (path.StartsWith("<Keyboard>/") || path.StartsWith("<Mouse>/") && path.EndsWith("Button"));
        public bool Held(int i) => !Blocked && actions[i].IsPressed();
        public bool Pressed(int i) => !Blocked && actions[i].WasPressedThisFrame();
        public Vector2 Move => Vector2.ClampMagnitude(new Vector2((Held(3) ? 1 : 0) - (Held(2) ? 1 : 0), (Held(0) ? 1 : 0) - (Held(1) ? 1 : 0)), 1);
        public string Label(int i) => actions[i].GetBindingDisplayString();
        public string Path(int i) => actions[i].bindings[0].effectivePath;
        public void BeginRebind(int index)
        {
            Cancel(); Rebinding = index; var old = Path(index); actions[index].Disable();
            operation = actions[index].PerformInteractiveRebinding(0)
                .WithControlsHavingToMatchPath("<Keyboard>/*")
                .WithControlsHavingToMatchPath("<Mouse>/*Button")
                .WithControlsExcluding("<Keyboard>/escape")
                .WithCancelingThrough("<Keyboard>/escape")
                .OnCancel(op => Finish("已取消修改。"))
                .OnComplete(op =>
                {
                    var path = Path(index);
                    if (!Allowed(path)) actions[index].ApplyBindingOverride(old);
                    else for (int j = 0; j < actions.Length; j++)
                        if (j != index && string.Equals(Path(j), path, StringComparison.OrdinalIgnoreCase)) actions[j].ApplyBindingOverride(old);
                    Save(); Finish("按键已保存；游戏提示已同步更新。");
                });
            operation.Start();
        }
        void Finish(string status)
        {
            var op = operation; operation = null; op?.Dispose();
            if (Rebinding >= 0) actions[Rebinding].Enable();
            Rebinding = -1; Status = status;
        }
        public void Cancel() { operation?.Cancel(); }
        public void Reset()
        {
            Cancel(); foreach (var action in actions) action.RemoveAllBindingOverrides(); Save(); Status = "已恢复默认按键。";
        }
        void Save()
        {
            var saved = new Saved { paths = new string[actions.Length] };
            for (int i = 0; i < actions.Length; i++) saved.paths[i] = Path(i);
            PlayerPrefs.SetString(SaveKey, JsonUtility.ToJson(saved)); PlayerPrefs.Save();
        }
        public void Dispose() { Cancel(); foreach (var action in actions) action.Dispose(); }
    }
}
