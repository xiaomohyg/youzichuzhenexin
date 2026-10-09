using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace M0
{
    // Uses unscaled time, but stops the effect clock inside either pause UI.
    public sealed class M0DodgeEffect : MonoBehaviour
    {
        public float Duration = 0.72f, MinimumTimeScale = 0.25f;
        public bool Active => remaining > 0;
        public float Saturation => color == null ? 0 : color.saturation.value;
        public float GameplayScale { get; private set; } = 1;
        Volume volume;
        VolumeProfile profile;
        ColorAdjustments color;
        float remaining, originalFixedStep;
        bool originalPostProcessing;
        Camera camera;
        void Awake()
        {
            originalFixedStep = Time.fixedDeltaTime;
            var fx = new GameObject("Successful dodge / grayscale volume"); fx.transform.SetParent(transform, false);
            volume = fx.AddComponent<Volume>(); volume.isGlobal = true; volume.priority = 100; volume.weight = 0;
            profile = ScriptableObject.CreateInstance<VolumeProfile>(); volume.sharedProfile = profile;
            color = profile.Add<ColorAdjustments>(true); color.saturation.Override(-100);
            camera = GetComponent<M0Sandbox>().Player.View;
            var data = camera.GetUniversalAdditionalCameraData(); originalPostProcessing = data.renderPostProcessing; data.renderPostProcessing = true;
        }
        public void Trigger()
        {
            if (Active) return;
            remaining = Duration; GameplayScale = MinimumTimeScale;
            volume.weight = 1; ApplyClock();
        }
        void Update()
        {
            if (!Active) return;
            var sandbox = GetComponent<M0Sandbox>();
            if (sandbox.MenuOpen || sandbox.InventoryOpen) { Time.timeScale = 0; return; }
            remaining = Mathf.Max(0, remaining - Time.unscaledDeltaTime);
            float fade = Mathf.SmoothStep(0, 1, Mathf.Clamp01(remaining / 0.28f));
            volume.weight = fade; GameplayScale = Mathf.Lerp(1, MinimumTimeScale, fade);
            ApplyClock();
        }
        public void ApplyClock()
        {
            var sandbox = GetComponent<M0Sandbox>();
            Time.timeScale = sandbox.MenuOpen || sandbox.InventoryOpen ? 0 : GameplayScale;
            Time.fixedDeltaTime = originalFixedStep * GameplayScale;
        }
        public void Clear()
        {
            remaining = 0; GameplayScale = 1;
            if (volume != null) volume.weight = 0;
            if (originalFixedStep > 0) ApplyClock();
        }
        void OnDisable() { Clear(); }
        void OnDestroy()
        {
            Time.timeScale = 1; if (originalFixedStep > 0) Time.fixedDeltaTime = originalFixedStep;
            if (camera != null) camera.GetUniversalAdditionalCameraData().renderPostProcessing = originalPostProcessing;
            if (profile != null) Destroy(profile);
        }
    }
}
