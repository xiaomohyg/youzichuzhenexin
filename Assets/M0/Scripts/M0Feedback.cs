using System.Collections.Generic;
using UnityEngine;

namespace M0
{
    [RequireComponent(typeof(AudioSource))]
    public sealed class M0Feedback : MonoBehaviour
    {
        public ParticleSystem Particles;
        public Material TracerMaterial;
        public float HitMarker { get; private set; }
        public float HurtOverlay { get; private set; }
        public bool StrongHit { get; private set; }
        public float Volume = 0.55f;
        readonly Dictionary<int, AudioClip> clips = new Dictionary<int, AudioClip>();
        readonly List<FloatingNumber> numbers = new List<FloatingNumber>();
        struct FloatingNumber { public Vector3 Position; public float Damage, Born; public bool Weak; }
        AudioSource source;
        void Awake() { source = GetComponent<AudioSource>(); source.playOnAwake = false; }
        void OnEnable() { CombatEvents.Hit += OnHit; }
        void OnDisable() { CombatEvents.Hit -= OnHit; }
        void OnHit(CombatFeedback hit)
        {
            HitMarker = 0.22f; StrongHit = hit.Kind == HitKind.Weakpoint;
            Sparks(hit.Position, StrongHit ? new Color(1, 0.7f, 0.12f) : new Color(0.3f, 0.95f, 1), StrongHit ? 24 : 10);
            PlayTone(StrongHit ? 740 : 120, StrongHit ? 0.18f : 0.1f, 0.25f);
            if (numbers.Count >= 24) numbers.RemoveAt(0);
            numbers.Add(new FloatingNumber { Position = hit.Position, Damage = hit.Damage, Born = Time.time, Weak = StrongHit });
            M0Sandbox.Instance.Player.OnCombatFeedback(hit);
            M0Sandbox.Instance.ShowMessage(StrongHit ? "弱点击杀！" : "命中 " + hit.Damage.ToString("0"));
        }
        public void PlayerHurt() { HurtOverlay = 0.3f; PlayTone(65, 0.16f, 0.24f); }
        void Update() { HitMarker = Mathf.Max(0, HitMarker - Time.deltaTime); HurtOverlay = Mathf.Max(0, HurtOverlay - Time.deltaTime); numbers.RemoveAll(n => Time.time - n.Born > 0.7f); }
        public void Clear() { numbers.Clear(); HitMarker = HurtOverlay = 0; Particles.Clear(); }
        public void Sparks(Vector3 position, Color color, int count)
        {
            Sparks(position, color, count, Vector3.up, 1f);
        }
        public void Sparks(Vector3 position, Color color, int count, Vector3 normal, float intensity)
        {
            if (Particles == null || count <= 0) return;
            if (!Particles.isPlaying) Particles.Play();
            normal = normal.sqrMagnitude > 0.001f ? normal.normalized : Vector3.up;
            intensity = Mathf.Max(0.25f, intensity);
            for (int i = 0; i < count; i++)
            {
                // A biased fan keeps the burst outside the struck mesh instead of hiding inside it.
                Vector3 scatter = Random.onUnitSphere;
                Vector3 direction = (normal * Random.Range(0.72f, 1.25f) + scatter * 0.72f).normalized;
                var emit = new ParticleSystem.EmitParams
                {
                    position = position + normal * Random.Range(0.025f, 0.075f),
                    velocity = direction * Random.Range(3.4f, 7.2f) * Mathf.Lerp(0.9f, 1.18f, intensity - 1f),
                    startColor = Color.Lerp(color, Color.white, Random.Range(0f, 0.38f)),
                    startLifetime = Random.Range(0.26f, 0.46f) * Mathf.Lerp(0.9f, 1.2f, intensity - 1f),
                    startSize = Random.Range(0.085f, 0.145f) * intensity
                };
                Particles.Emit(emit, 1);
            }
            // Perfect impacts get a few white-hot, slower cores that remain readable through shake.
            if (intensity < 1.25f) return;
            for (int i = 0; i < 6; i++)
            {
                var core = new ParticleSystem.EmitParams
                {
                    position = position + normal * 0.055f,
                    velocity = (normal + Random.onUnitSphere * 0.3f).normalized * Random.Range(1.4f, 3f),
                    startColor = Color.white,
                    startLifetime = Random.Range(0.16f, 0.28f),
                    startSize = Random.Range(0.16f, 0.23f) * intensity
                };
                Particles.Emit(core, 1);
            }
        }
        public void Tracer(Vector3 from, Vector3 to)
        {
            var go = new GameObject("Shot tracer"); var line = go.AddComponent<LineRenderer>();
            line.material = TracerMaterial; line.positionCount = 2; line.SetPosition(0, from); line.SetPosition(1, to);
            line.startWidth = 0.025f; line.endWidth = 0.008f; line.startColor = new Color(0.3f, 1, 1); line.endColor = Color.white;
            Destroy(go, 0.065f);
        }
        // Small synthesized placeholder sounds; no downloads or generated art assets are needed.
        public void PlayTone(int frequency, float duration, float volume)
        {
            if (!clips.TryGetValue(frequency, out var clip))
            {
                const int rate = 22050; int samples = (int)(rate * duration); var data = new float[samples];
                for (int i = 0; i < samples; i++)
                {
                    float t = i / (float)rate, envelope = Mathf.Pow(1 - i / (float)samples, 2);
                    data[i] = (Mathf.Sin(2 * Mathf.PI * frequency * t * (1 - t * 1.6f)) * 0.7f + Random.Range(-0.25f, 0.25f)) * envelope;
                }
                clip = AudioClip.Create("M0 placeholder " + frequency, samples, 1, rate, false); clip.SetData(data, 0); clips.Add(frequency, clip);
            }
            source.PlayOneShot(clip, volume * Volume);
        }
        public void DrawNumbers(Camera camera, GUIStyle style, float scale)
        {
            foreach (var n in numbers)
            {
                float age = Time.time - n.Born;
                var point = camera.WorldToScreenPoint(n.Position + Vector3.up * age);
                if (point.z <= 0) continue;
                GUI.color = n.Weak ? new Color(1, 0.8f, 0.3f, 1 - age) : new Color(0.6f, 1, 1, 1 - age);
                GUI.Label(new Rect(point.x / scale - 50, (Screen.height - point.y) / scale, 100, 30), n.Weak ? "弱点!" : n.Damage.ToString("0"), style);
            }
            GUI.color = Color.white;
        }
        void OnDestroy() { foreach (var clip in clips.Values) Destroy(clip); }
    }
}
