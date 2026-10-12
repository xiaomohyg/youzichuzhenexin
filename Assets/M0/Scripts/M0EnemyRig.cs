using UnityEngine;

namespace M0
{
    // Presentation stays on the existing six ragdoll bones; detail meshes follow those bones.
    public sealed class M0EnemyRig : MonoBehaviour
    {
        public Transform Torso, Head, LeftShoulder, RightShoulder, LeftElbow, RightElbow, LeftLeg, RightLeg;
        public BoxCollider StrikeShape;
        public TrailRenderer StrikeTrail;
        public LineRenderer TelegraphRing;
        public float DashPhase = 0.32f;
        public bool TelegraphFlashActive => telegraphFlash > 0;
        public float TelegraphClosure { get; private set; }
        const int TelegraphSegments = 65;
        float gait, stride, telegraphFlash;
        void Update() { telegraphFlash = Mathf.Max(0, telegraphFlash - Time.unscaledDeltaTime); }
        public void ResetPose()
        {
            gait = stride = telegraphFlash = 0; DashPhase = 0.32f; TelegraphClosure = 0;
            LeftElbow.localRotation = RightElbow.localRotation = Quaternion.identity;
            if (StrikeTrail != null) { StrikeTrail.emitting = false; StrikeTrail.Clear(); }
            if (TelegraphRing != null) TelegraphRing.enabled = false;
        }
        public void Present(EnemyState state, float phase, float movement)
        {
            PresentTelegraph(state == EnemyState.Windup ? phase * 0.82f : -1);
            gait += Time.deltaTime * 8;
            stride = Mathf.MoveTowards(stride, movement, Time.deltaTime * 5);
            float walk = Mathf.Sin(gait) * stride;
            Vector3 right = new Vector3(-walk * 22, 0, -8), left = new Vector3(walk * 22, 0, 8);
            Vector3 torso = new Vector3(stride * 6, walk * 4, walk * 3);
            float elbow = -18, brace = 0;
            if (state == EnemyState.Windup)
            {
                float t = Mathf.SmoothStep(0, 1, phase);
                float settle = Mathf.Sin(t * Mathf.PI);
                right = Vector3.Lerp(new Vector3(-8, 0, -8), new Vector3(-118, -58, -64), t);
                left = Vector3.Lerp(new Vector3(0, 0, 8), new Vector3(-72, 24, 30), t);
                torso = new Vector3(-12 * t - settle * 4, -36, -12 * t);
                elbow = Mathf.Lerp(-18, -92, t); brace = t;
            }
            else if (state == EnemyState.Leap)
            {
                float lift = Mathf.Sin(Mathf.Clamp01(phase) * Mathf.PI);
                right = Vector3.Lerp(new Vector3(-118, -58, -64), new Vector3(-150, 0, -8), lift);
                left = Vector3.Lerp(new Vector3(-72, 24, 30), new Vector3(-142, 0, 8), lift);
                torso = new Vector3(-22 * lift, 0, -8 * lift);
                elbow = Mathf.Lerp(-92, -118, lift); brace = 1;
            }
            else if (state == EnemyState.Slam)
            {
                float impact = Mathf.SmoothStep(0, 1, Mathf.InverseLerp(0, DashPhase, phase));
                right = Vector3.Lerp(new Vector3(-150, 0, -8), new Vector3(38, 0, -8), impact);
                left = Vector3.Lerp(new Vector3(-142, 0, 8), new Vector3(12, 0, 8), impact);
                torso = Vector3.Lerp(new Vector3(-8, 0, 0), new Vector3(48, 0, 0), impact);
                elbow = Mathf.Lerp(-125, -12, impact); brace = 1;
            }
            else if (state == EnemyState.Attack)
            {
                float dash = Mathf.SmoothStep(0, 1, Mathf.InverseLerp(0, DashPhase, phase));
                float strike = Mathf.SmoothStep(0, 1, Mathf.InverseLerp(DashPhase + 0.08f, 0.82f, phase));
                float follow = Mathf.SmoothStep(0, 1, Mathf.InverseLerp(0.76f, 1f, phase));
                Vector3 extended = new Vector3(-88, -8, -8), followThrough = new Vector3(-62, 26, 12);
                right = Vector3.Lerp(new Vector3(-118, -58, -64), extended, strike);
                right = Vector3.Lerp(right, followThrough, follow);
                left = Vector3.Lerp(new Vector3(-72, 24, 30), new Vector3(-38, -12, 18), strike);
                torso = Vector3.Lerp(new Vector3(-12, -36, -12), new Vector3(8, -20, -4), dash);
                torso = Vector3.Lerp(torso, new Vector3(20, 34, 12), strike);
                torso = Vector3.Lerp(torso, new Vector3(12, 42, 16), follow);
                elbow = Mathf.Lerp(-92, -4, strike); brace = 1 - follow * 0.45f;
            }
            else if (state == EnemyState.Recovery)
            {
                float recover = Mathf.SmoothStep(0, 1, phase);
                right = Vector3.Lerp(new Vector3(46, 38, 18), new Vector3(0, 0, -8), recover);
                left = Vector3.Lerp(new Vector3(-38, -12, 18), new Vector3(0, 0, 8), recover);
                torso = Vector3.Lerp(new Vector3(12, 42, 16), Vector3.zero, recover);
                elbow = Mathf.Lerp(-4, -18, recover); brace = 1 - recover;
            }
            else if (state == EnemyState.Stagger)
            { right = new Vector3(20, 0, -32); left = new Vector3(20, 0, 32); torso = new Vector3(-18, 0, 0); }
            float blendSpeed = state == EnemyState.Attack || state == EnemyState.Slam ? 38 : state == EnemyState.Windup ? 18 : 12;
            float blend = 1 - Mathf.Exp(-Time.deltaTime * blendSpeed);
            RightShoulder.localRotation = Quaternion.Slerp(RightShoulder.localRotation, Quaternion.Euler(right), blend);
            LeftShoulder.localRotation = Quaternion.Slerp(LeftShoulder.localRotation, Quaternion.Euler(left), blend);
            RightElbow.localRotation = Quaternion.Slerp(RightElbow.localRotation, Quaternion.Euler(elbow, 0, 0), blend);
            LeftElbow.localRotation = Quaternion.Slerp(LeftElbow.localRotation, Quaternion.Euler(-25 - brace * 25, 0, 0), blend);
            Torso.localRotation = Quaternion.Slerp(Torso.localRotation, Quaternion.Euler(torso), blend);
            Head.localRotation = Quaternion.Slerp(Head.localRotation, Quaternion.Euler(0, -torso.y * 0.4f, 0), blend);
            LeftLeg.localRotation = Quaternion.Slerp(LeftLeg.localRotation, Quaternion.Euler(-walk * 24 + brace * 12, 0, 0), blend);
            RightLeg.localRotation = Quaternion.Slerp(RightLeg.localRotation, Quaternion.Euler(walk * 24 - brace * 12, 0, 0), blend);
            if (StrikeTrail != null) StrikeTrail.emitting = (state == EnemyState.Attack || state == EnemyState.Slam) && phase >= DashPhase + 0.04f && phase <= 0.92f;
        }
        public void SetTelegraph(float phase) { PresentTelegraph(phase); }
        public void FlashTelegraph()
        {
            telegraphFlash = 0.14f;
            PresentTelegraph(1);
        }
        void PresentTelegraph(float phase)
        {
            if (TelegraphRing == null) return;
            bool flashing = telegraphFlash > 0;
            if (phase < 0 && !flashing)
            {
                TelegraphRing.enabled = false;
                TelegraphClosure = 0;
                return;
            }
            if (flashing) phase = 1;
            phase = Mathf.Clamp01(phase);
            TelegraphClosure = phase;
            TelegraphRing.enabled = true;
            float pulse = flashing ? 1f : 0.86f + Mathf.Sin(Time.unscaledTime * 18f) * 0.14f;
            Color color = flashing ? Color.white : Color.Lerp(new Color(1f, 0.06f, 0.015f, 0.9f), new Color(1f, 0.78f, 0.08f, 1f), phase);
            color.a *= pulse;
            TelegraphRing.startColor = TelegraphRing.endColor = color;
            TelegraphRing.widthMultiplier = (flashing ? 0.22f : Mathf.Lerp(0.075f, 0.14f, phase)) * pulse;
            float radius = (flashing ? 0.92f + Mathf.Sin(Time.unscaledTime * 70f) * 0.09f : Mathf.Lerp(1.42f, 0.92f, phase)) +
                (flashing ? 0 : Mathf.Sin(Time.unscaledTime * 18f) * 0.025f);
            TelegraphRing.transform.localScale = Vector3.one * radius;
            int visible = flashing ? TelegraphSegments : Mathf.Clamp(Mathf.CeilToInt(Mathf.Lerp(33, TelegraphSegments, phase)), 33, TelegraphSegments);
            TelegraphRing.loop = flashing || phase >= 0.999f;
            TelegraphRing.positionCount = visible;
            for (int i = 0; i < visible; i++)
            {
                float angle = -Mathf.PI * 0.5f + i * Mathf.PI * 2f / (TelegraphSegments - 1);
                TelegraphRing.SetPosition(i, new Vector3(Mathf.Cos(angle), Mathf.Sin(angle), 0));
            }
            var enemy = GetComponent<M0Enemy>();
            if (enemy != null)
            {
                Vector3 center = enemy.transform.position + Vector3.up * 1.05f;
                Vector3 towardPlayer = M0Sandbox.Instance != null && M0Sandbox.Instance.Player != null
                    ? M0Sandbox.Instance.Player.transform.position - center
                    : enemy.transform.forward;
                towardPlayer = Vector3.ProjectOnPlane(towardPlayer, Vector3.up);
                if (towardPlayer.sqrMagnitude < 0.001f) towardPlayer = enemy.transform.forward;
                TelegraphRing.transform.SetPositionAndRotation(center, Quaternion.LookRotation(towardPlayer.normalized, Vector3.up));
            }
        }
    }
}
