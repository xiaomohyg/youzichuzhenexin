using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using M0.Networking;

namespace M0
{
    [DefaultExecutionOrder(-100)]
    public sealed class M0Sandbox : MonoBehaviour
    {
        public static M0Sandbox Instance { get; private set; }
        public M0Player Player;
        public M0Feedback Fx;
        public M0NetworkSession Network;
        public M0DodgeEffect DodgeEffect { get; private set; }
        public List<M0Enemy> Enemies = new List<M0Enemy>();
        public Transform[] SpawnPoints;
        [Range(1, 10)] public int DesiredEnemies = 5;
        [Range(1, 20)] public int MaximumCorpses = 20;
        public bool AutoRespawn = true;
        public bool PauseWhenUnfocused = true;
        public M0Input Input { get; private set; }
        public bool MenuOpen { get; private set; }
        public bool InventoryOpen { get; private set; }
        public M0Inventory Inventory { get; private set; }
        public int Kills { get; private set; }
        public int AliveCount { get { int n = 0; foreach (var enemy in Enemies) if (enemy.Alive) n++; return n; } }
        public int CorpseCount => corpses.Count;
        readonly Queue<M0Enemy> corpses = new Queue<M0Enemy>();
        float nextSpawn, messageUntil;
        string message = "抬高准星瞄准橙色头部，即可弱点击杀";
        bool resumeNextFrame;
        Font font;
        [System.NonSerialized] GUIStyle small, label, title, centered, button, box;
        Vector2 scroll;
        int page;
        int pickedSlot = -1;
        Vector2 inventoryScroll;
        void Awake()
        {
            EnsureRuntimeState();
            Player.Sensitivity = PlayerPrefs.GetFloat("M0.Sensitivity", Player.Sensitivity);
            Player.FieldOfView = PlayerPrefs.GetFloat("M0.Fov", Player.FieldOfView);
            Player.CameraShake = PlayerPrefs.GetFloat("M0.Shake", Player.CameraShake);
            Player.HeadBob = PlayerPrefs.GetInt("M0.Bob", 0) == 1;
            Fx.Volume = PlayerPrefs.GetFloat("M0.Volume", 0.55f);
            font = Font.CreateDynamicFontFromOSFont(new[] { "Microsoft YaHei", "Noto Sans CJK SC", "Arial" }, 18);
            Time.timeScale = 1; Input.Blocked = false; LockCursor(true);
            DodgeEffect = GetComponent<M0DodgeEffect>(); if (DodgeEffect == null) DodgeEffect = gameObject.AddComponent<M0DodgeEffect>();
        }
        void EnsureRuntimeState()
        {
            Instance = this;
            if (Input == null) Input = new M0Input();
            if (Inventory == null)
            {
                Inventory = new M0Inventory();
                if (Player != null) Inventory.Changed += Player.EquipmentChanged;
            }
            if (DodgeEffect == null) DodgeEffect = GetComponent<M0DodgeEffect>();
        }
        void Start() { Restart(); }
        void Update()
        {
            EnsureRuntimeState();
            if (resumeNextFrame) { resumeNextFrame = false; Input.Blocked = false; }
            // Read the configured path while gameplay actions are blocked by the inventory.
            var inventoryKey = InputSystem.FindControl(Input.Path(14)) as UnityEngine.InputSystem.Controls.ButtonControl;
            if (inventoryKey?.wasPressedThisFrame == true && !MenuOpen)
            { if (InventoryOpen) CloseInventory(); else OpenInventory(); }
            if (Keyboard.current?.escapeKey.wasPressedThisFrame == true && Input.Rebinding < 0)
            {
                if (InventoryOpen) CloseInventory();
                else if (MenuOpen && Player.Health > 0) CloseMenu(); else OpenMenu(false);
            }
            if (PauseWhenUnfocused && !MenuOpen && !Application.isFocused) OpenMenu(false);
            if (Input.Pressed(11)) Restart();
            if (!MenuOpen && !InventoryOpen && AutoRespawn && Time.time >= nextSpawn && AliveCount < DesiredEnemies)
            { SpawnOne(); nextSpawn = Time.time + 3.2f; }
        }
        static void LockCursor(bool locked) { Cursor.lockState = locked ? CursorLockMode.Locked : CursorLockMode.None; Cursor.visible = !locked; }
        public void OpenMenu(bool dead)
        {
            InventoryOpen = false; pickedSlot = -1;
            MenuOpen = true; Input.Blocked = true; Time.timeScale = 0; LockCursor(false);
            if (dead) message = "训练结束 · 点击重新开始";
        }
        public void CloseMenu()
        {
            Input.Cancel(); SaveSettings(); MenuOpen = false; DodgeEffect.ApplyClock(); LockCursor(true);
            // Consume the click used to resume, so it cannot attack in the same frame.
            Input.Blocked = true; resumeNextFrame = true;
        }
        public void OpenInventory()
        {
            if (MenuOpen || Player.Health <= 0) return;
            InventoryOpen = true; pickedSlot = -1; Input.Blocked = true; Time.timeScale = 0; LockCursor(false);
        }
        public void CloseInventory()
        {
            InventoryOpen = false; pickedSlot = -1; DodgeEffect.ApplyClock(); LockCursor(true);
            Input.Blocked = true; resumeNextFrame = true;
        }
        public void Restart()
        {
            DodgeEffect.Clear();
            corpses.Clear(); foreach (var enemy in Enemies) enemy.gameObject.SetActive(false);
            Kills = 0; Inventory.Reset(); Player.ResetPlayer(new Vector3(0, 0.1f, -8)); Fx.Clear();
            for (int i = 0; i < DesiredEnemies; i++) SpawnOne();
            nextSpawn = Time.time + 4; messageUntil = Time.time + 5;
            message = "橙色头部是弱点 · 红光是攻击前摇 · 重击可击退";
        }
        void SpawnOne()
        {
            M0Enemy slot = Enemies.Find(e => !e.gameObject.activeSelf);
            if (slot == null && corpses.Count > 0) { slot = corpses.Dequeue(); slot.gameObject.SetActive(false); }
            if (slot == null || SpawnPoints.Length == 0) return;
            var spawn = SpawnPoints[Random.Range(0, SpawnPoints.Length)].position;
            if (Vector3.Distance(spawn, Player.transform.position) < 4) spawn = SpawnPoints[0].position;
            slot.ResetEnemy(spawn);
        }
        public void EnemyKilled(M0Enemy enemy)
        {
            Kills++; corpses.Enqueue(enemy);
            while (corpses.Count > MaximumCorpses) corpses.Dequeue().gameObject.SetActive(false);
        }
        public void ShowMessage(string text) { message = text; messageUntil = Time.time + 1.15f; }
        void SaveSettings()
        {
            PlayerPrefs.SetFloat("M0.Sensitivity", Player.Sensitivity); PlayerPrefs.SetFloat("M0.Fov", Player.FieldOfView);
            PlayerPrefs.SetFloat("M0.Shake", Player.CameraShake); PlayerPrefs.SetInt("M0.Bob", Player.HeadBob ? 1 : 0);
            PlayerPrefs.SetFloat("M0.Volume", Fx.Volume); PlayerPrefs.Save();
        }
        void Styles()
        {
            if (label != null && label.font == font && label.fontSize == 18) return;
            label = new GUIStyle(GUI.skin.label) { font = font, fontSize = 18, normal = { textColor = new Color(0.85f, 0.94f, 0.98f) } };
            small = new GUIStyle(label) { fontSize = 14 };
            title = new GUIStyle(label) { fontSize = 28, fontStyle = FontStyle.Bold };
            centered = new GUIStyle(label) { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold };
            button = new GUIStyle(GUI.skin.button) { font = font, fontSize = 17 };
            box = new GUIStyle(GUI.skin.box) { font = font, fontSize = 18 };
        }
        static void RectColor(Rect rect, Color color) { GUI.color = color; GUI.DrawTexture(rect, Texture2D.whiteTexture); GUI.color = Color.white; }
        void Text(float x, float y, float width, string text, GUIStyle style = null) => GUI.Label(new Rect(x, y, width, style == title ? 44 : 32), text, style ?? label);
        bool Button(Rect rect, string text)
        {
            RectColor(rect, rect.Contains(Event.current.mousePosition) ? new Color(0.12f, 0.35f, 0.43f) : new Color(0.08f, 0.17f, 0.23f));
            var buttonStyle = new GUIStyle(centered);
            return GUI.Button(rect, text, buttonStyle);
        }
        void OnGUI()
        {
            if (Player == null || Input == null) return;
            Styles(); float scale = Mathf.Min(Screen.width / 1280f, Screen.height / 720f);
            GUI.color = GUI.contentColor = Color.white;
            var oldMatrix = GUI.matrix; GUI.matrix = Matrix4x4.TRS(Vector3.zero, Quaternion.identity, Vector3.one * scale);
            float width = Screen.width / scale, height = Screen.height / scale;
            RectColor(new Rect(24, 24, 360, 102), new Color(0.02f, 0.05f, 0.08f, 0.85f));
            RectColor(new Rect(24, 24, 4, 102), new Color(0.2f, 0.85f, 0.9f));
            Text(42, 30, 330, "M0 / 战斗手感沙盒", title);
            Text(44, 78, 310, "击杀 " + Kills + "    敌人 " + AliveCount + "    尸体 " + CorpseCount, small);
            RectColor(new Rect(44, 106, 300, 5), new Color(0.15f, 0.2f, 0.25f));
            RectColor(new Rect(44, 106, Player.Health * 3, 5), new Color(0.2f, 0.9f, 0.65f));
            Text(width - 300, 30, 280, "单机训练 / 本地权威", small);
            Text(width - 300, 53, 280, "Esc  暂停 · 按键设置", small);
            if (!MenuOpen)
            {
                float cx = width * 0.5f, cy = height * 0.5f;
                GUI.color = Player.Blocking ? new Color(0.35f, 0.8f, 1) : Color.white;
                GUI.DrawTexture(new Rect(cx - 10, cy - 1, 6, 2), Texture2D.whiteTexture); GUI.DrawTexture(new Rect(cx + 4, cy - 1, 6, 2), Texture2D.whiteTexture);
                GUI.DrawTexture(new Rect(cx - 1, cy - 10, 2, 6), Texture2D.whiteTexture); GUI.DrawTexture(new Rect(cx - 1, cy + 4, 2, 6), Texture2D.whiteTexture); GUI.color = Color.white;
                if (Fx.HitMarker > 0)
                {
                    GUI.color = Fx.StrongHit ? new Color(1, 0.75f, 0.2f) : new Color(0.4f, 1, 1);
                    GUI.Label(new Rect(cx - 22, cy - 28, 44, 44), "×", title); GUI.color = Color.white;
                }
                if (Time.time < messageUntil) Text(cx - 230, cy + 42, 460, message, centered);
                RectColor(new Rect(16, height - 143, width - 32, 62), new Color(0.02f, 0.05f, 0.08f, 0.82f));
                Text(24, height - 140, width - 48, "[" + Input.Label(0) + "/" + Input.Label(1) + "/" + Input.Label(2) + "/" + Input.Label(3) + "] 移动    [" + Input.Label(4) + "] 奔跑    [" + Input.Label(5) + "] 跳跃    [" + Input.Label(6) + "] 闪避    [" + Input.Label(12) + "] 下蹲    [" + Input.Label(13) + "] 视角", small);
                Text(24, height - 113, width - 48, "[" + Input.Label(7) + "] 攻击    [" + Input.Label(8) + "] 格挡    [" + Input.Label(9) + "] 重击    [" + Input.Label(10) + "] 换武器    [" + Input.Label(14) + "] 物品栏    滚轮 / 数字键：选物品    [" + Input.Label(11) + "] 重置", small);
                DrawHotbar(width, height);
                Text(24, height - 70, 430, (Player.ThirdPerson ? "第三人称" : "第一人称") + (Player.Crouching ? " · 下蹲" : "") + (Player.CounterReady ? " · 反击就绪" : "") + (Player.Blocking ? " · 格挡" : ""), small);
                Text(width - 220, height - 70, 200, "闪避 " + (Player.DodgeReady >= 1 ? "就绪" : "冷却"), small);
                RectColor(new Rect(width - 220, height - 38, 190 * Player.DodgeReady, 4), new Color(0.3f, 0.8f, 1));
                Fx.DrawNumbers(Player.View, centered, scale);
                if (Fx.HurtOverlay > 0) RectColor(new Rect(0, 0, width, height), new Color(0.8f, 0.04f, 0.02f, Fx.HurtOverlay * 0.5f));
            }
            else DrawMenu(width, height);
            if (InventoryOpen) DrawInventory(width, height);
            GUI.matrix = oldMatrix; GUI.color = Color.white;
        }
        void DrawHotbar(float width, float height)
        {
            float left = width * 0.5f - 240;
            for (int i = 0; i < M0Inventory.HotbarSize; i++)
            {
                Rect rect = new Rect(left + i * 97, height - 80, 91, 62);
                RectColor(rect, i == Inventory.Selected ? new Color(0.19f, 0.52f, 0.58f, 0.96f) : new Color(0.035f, 0.09f, 0.13f, 0.92f));
                Text(rect.x + 7, rect.y + 1, 70, Input.Label(15 + i), small);
                DrawItemIcon(new Rect(rect.x + 37, rect.y + 5, 42, 32), Inventory[i].Item);
                GUI.Label(new Rect(rect.x, rect.y + 34, 91, 26), Inventory[i].Name, new GUIStyle(centered) { fontSize = 13 });
            }
        }
        static void DrawItemIcon(Rect rect, M0Item item)
        {
            float unit = Mathf.Min(rect.width / 7, rect.height / 9);
            if (item == M0Item.Sword)
            {
                RectColor(new Rect(rect.x + 3 * unit, rect.y, unit, 5 * unit), new Color(0.7f, 0.9f, 0.95f));
                RectColor(new Rect(rect.x + unit, rect.y + 5 * unit, 5 * unit, unit), new Color(0.9f, 0.65f, 0.2f));
                RectColor(new Rect(rect.x + 3 * unit, rect.y + 6 * unit, unit, 3 * unit), new Color(0.48f, 0.28f, 0.15f));
            }
            else if (item == M0Item.Sidearm)
            {
                RectColor(new Rect(rect.x, rect.y + 2 * unit, 7 * unit, 2 * unit), new Color(0.65f, 0.78f, 0.82f));
                RectColor(new Rect(rect.x + unit, rect.y + 4 * unit, 2 * unit, 4 * unit), new Color(0.5f, 0.30f, 0.17f));
            }
        }
        void DrawInventory(float width, float height)
        {
            RectColor(new Rect(0, 0, width, height), new Color(0.01f, 0.025f, 0.035f, 0.84f));
            float left = width * 0.5f - 340;
            RectColor(new Rect(left, 70, 680, 580), new Color(0.035f, 0.07f, 0.10f, 0.98f));
            Text(left + 28, 87, 550, "物品栏 / 装备", title);
            Text(left + 28, 138, 620, "点击两个格子交换物品；右键快捷栏格子装备。", small);
            Text(left + 28, 165, 620, "顶部 5 格为快捷栏 · 滚轮 / 数字键选择 · 空格收起武器", small);
            inventoryScroll = GUI.BeginScrollView(new Rect(left + 28, 206, 624, 350), inventoryScroll, new Rect(0, 0, 598, 344));
            for (int i = 0; i < M0Inventory.Capacity; i++)
            {
                Rect rect = new Rect(i % 5 * 119, i / 5 * 86, 110, 78);
                RectColor(rect, pickedSlot == i ? new Color(0.48f, 0.34f, 0.13f) : i == Inventory.Selected ? new Color(0.12f, 0.37f, 0.44f) : new Color(0.075f, 0.13f, 0.18f));
                DrawItemIcon(new Rect(rect.x + 42, rect.y + 10, 50, 42), Inventory[i].Item);
                GUI.Label(new Rect(rect.x + 5, rect.y + 2, 35, 23), i < 5 ? Input.Label(15 + i) : (i + 1).ToString(), small);
                GUI.Label(new Rect(rect.x, rect.y + 54, 110, 28), Inventory[i].Name, new GUIStyle(centered) { fontSize = 14 });
                if (Event.current.type == EventType.MouseDown && rect.Contains(Event.current.mousePosition))
                {
                    if (Event.current.button == 1 && i < 5) { Inventory.Select(i); pickedSlot = -1; }
                    else if (Event.current.button == 0)
                    { if (pickedSlot < 0) pickedSlot = i; else { Inventory.Swap(pickedSlot, i); pickedSlot = -1; } }
                    Event.current.Use();
                }
            }
            GUI.EndScrollView();
            if (Button(new Rect(left + 220, 588, 240, 40), "返回游戏 / " + Input.Label(14))) CloseInventory();
        }
        void DrawMenu(float width, float height)
        {
            RectColor(new Rect(0, 0, width, height), new Color(0.01f, 0.02f, 0.04f, 0.86f));
            float left = (width - 820) / 2;
            RectColor(new Rect(left, 65, 820, 580), new Color(0.035f, 0.07f, 0.1f, 0.98f));
            Text(left + 28, 82, 750, Player.Health <= 0 ? "训练结束 / " + Kills + " 次击杀" : "暂停 / 沙盒设置", title);
            if (Button(new Rect(left + 28, 134, 240, 38), "按键设置")) page = 0;
            if (Button(new Rect(left + 287, 134, 240, 38), "手感 / 舒适度")) page = 1;
            if (Button(new Rect(left + 546, 134, 240, 38), "联机模块预留")) page = 2;
            if (page == 0)
            {
                scroll = GUI.BeginScrollView(new Rect(left + 28, 188, 760, 342), scroll, new Rect(0, 0, 725, M0Input.Names.Length * 43));
                for (int i = 0; i < M0Input.Names.Length; i++)
                {
                    Text(15, i * 43 + 4, 440, M0Input.Names[i]);
                    if (Button(new Rect(450, i * 43 + 1, 255, 36), Input.Rebinding == i ? "请按新按键…" : Input.Label(i))) Input.BeginRebind(i);
                }
                GUI.EndScrollView();
                Text(left + 40, 536, 730, Input.Status, small);
                if (Button(new Rect(left + 40, 565, 200, 35), "恢复默认按键")) Input.Reset();
            }
            else if (page == 1)
            {
                Player.Sensitivity = Slider(left, 193, "鼠标灵敏度", Player.Sensitivity, 0.03f, 0.3f);
                Player.FieldOfView = Slider(left, 243, "视野 FOV", Player.FieldOfView, 90, 100);
                Player.CameraShake = Slider(left, 293, "命中震动（0 可关闭）", Player.CameraShake, 0, 0.04f);
                Fx.Volume = Slider(left, 343, "占位音效音量", Fx.Volume, 0, 1);
                Player.HeadBob = GUI.Toggle(new Rect(left + 40, 397, 500, 30), Player.HeadBob, " 轻微头晃（默认关闭）", label);
                AutoRespawn = GUI.Toggle(new Rect(left + 40, 441, 500, 30), AutoRespawn, " 自动补充敌人", label);
                Text(left + 40, 493, 740, "重击 / 闪避 / 格挡 / 敌人参数也可在 Inspector 实时调整。", small);
                Text(left + 40, 526, 740, "命中停顿冻结敌人；成功闪避触发灰度与短暂慢动作。", small);
            }
            else
            {
                Text(left + 40, 205, 730, "M2 接口模板 / 本地消息回环", title);
                Text(left + 40, 260, 730, "输入命令 → 权威逻辑 → 状态快照 / 战斗事件");
                Text(left + 40, 305, 730, "已记录命令 " + Network.CommandsObserved + "    快照 " + Network.SnapshotsObserved + "    命中 " + Network.HitsObserved);
                Text(left + 40, 353, 730, "当前为单机；此模板尚未提供真实网络连接。", small);
                Text(left + 40, 390, 730, "Networking 文件夹：IM0Transport、输入命令、ActorSnapshot。", small);
                Text(left + 40, 428, 730, "后续接入 NGO：服务端校验输入与命中，客户端插值表现。", small);
                Text(left + 40, 466, 730, "不可信客户端不能直接提交伤害。详见工程中的 README。", small);
            }
            GUI.enabled = Input.Rebinding < 0;
            if (Button(new Rect(left + 285, 586, 240, 40), "重新开始")) { Restart(); CloseMenu(); }
            GUI.enabled = Player.Health > 0 && Input.Rebinding < 0;
            if (Button(new Rect(left + 546, 586, 240, 40), "继续训练 / Esc")) CloseMenu();
            GUI.enabled = true;
        }
        float Slider(float left, float y, string text, float value, float min, float max)
        {
            Text(left + 40, y, 390, text + "  " + value.ToString(max > 10 ? "0" : "0.00"));
            return GUI.HorizontalSlider(new Rect(left + 430, y + 13, 315, 20), value, min, max);
        }
        void OnDestroy()
        {
            SaveSettings(); Input?.Dispose(); if (font != null) Destroy(font);
            if (Instance == this) Instance = null; Time.timeScale = 1; LockCursor(false);
        }
    }
}
