using UnityEngine;

namespace M0
{
    // Presentation stays on the existing six ragdoll bones; detail meshes follow those bones.
    public sealed class M0EnemyRig : MonoBehaviour
    {
        public Transform Torso, Head, LeftShoulder, RightShoulder, LeftElbow, RightElbow, LeftLeg, RightLeg;
        public BoxCollider StrikeShape;
        public TrailRenderer StrikeTrail;
        float gait, stride;
        public void ResetPose()
        {
            gait = stride = 0;
            LeftElbow.localRotation = RightElbow.localRotation = Quaternion.identity;
            if (StrikeTrail != null) { StrikeTrail.emitting = false; StrikeTrail.Clear(); }
        }
        public void Present(EnemyState state, float phase, float movement)
        {
            gait += Time.deltaTime * 8;
            stride = Mathf.MoveTowards(stride, movement, Time.deltaTime * 5);
            float walk = Mathf.Sin(gait) * stride;
            Vector3 right = new Vector3(-walk * 22, 0, -8), left = new Vector3(walk * 22, 0, 8);
            Vector3 torso = new Vector3(stride * 6, walk * 4, walk * 3);
            float elbow = -18, brace = 0;
            if (state == EnemyState.Windup)
            {
                float t = Mathf.SmoothStep(0, 1, phase);
                right = Vector3.Lerp(new Vector3(0, 0, -8), new Vector3(-115, -42, -50), t);
                left = Vector3.Lerp(new Vector3(0, 0, 8), new Vector3(-65, 15, 22), t);
                torso = new Vector3(-8 * t, -28 * t, -8 * t); elbow = Mathf.Lerp(-18, -78, t); brace = t;
            }
            else if (state == EnemyState.Attack || state == EnemyState.Recovery)
            {
                float t = Mathf.SmoothStep(0, 1, Mathf.InverseLerp(0.12f, 0.72f, phase));
                right = Vector3.Lerp(new Vector3(-115, -42, -50), new Vector3(-80, -20, -10), t);
                left = new Vector3(-65 + t * 25, 15, 22);
                torso = Vector3.Lerp(new Vector3(-8, -28, -8), new Vector3(16, 26, 9), t);
                elbow = Mathf.Lerp(-78, -10, t); brace = 1;
                if (state == EnemyState.Recovery)
                {
                    float recover = Mathf.SmoothStep(0, 1, phase);
                    right = Vector3.Lerp(new Vector3(-80, -20, -10), new Vector3(0, 0, -8), recover);
                    left = Vector3.Lerp(new Vector3(-40, 15, 22), new Vector3(0, 0, 8), recover);
                    torso = Vector3.Lerp(new Vector3(16, 26, 9), Vector3.zero, recover);
                    elbow = Mathf.Lerp(-10, -18, recover); brace = 1 - recover;
                }
            }
            else if (state == EnemyState.Stagger)
            { right = new Vector3(20, 0, -32); left = new Vector3(20, 0, 32); torso = new Vector3(-18, 0, 0); }
            float blend = 1 - Mathf.Exp(-Time.deltaTime * 25);
            RightShoulder.localRotation = Quaternion.Slerp(RightShoulder.localRotation, Quaternion.Euler(right), blend);
            LeftShoulder.localRotation = Quaternion.Slerp(LeftShoulder.localRotation, Quaternion.Euler(left), blend);
            RightElbow.localRotation = Quaternion.Slerp(RightElbow.localRotation, Quaternion.Euler(elbow, 0, 0), blend);
            LeftElbow.localRotation = Quaternion.Slerp(LeftElbow.localRotation, Quaternion.Euler(-25 - brace * 25, 0, 0), blend);
            Torso.localRotation = Quaternion.Slerp(Torso.localRotation, Quaternion.Euler(torso), blend);
            Head.localRotation = Quaternion.Slerp(Head.localRotation, Quaternion.Euler(0, -torso.y * 0.4f, 0), blend);
            LeftLeg.localRotation = Quaternion.Slerp(LeftLeg.localRotation, Quaternion.Euler(-walk * 24 + brace * 12, 0, 0), blend);
            RightLeg.localRotation = Quaternion.Slerp(RightLeg.localRotation, Quaternion.Euler(walk * 24 - brace * 12, 0, 0), blend);
            if (StrikeTrail != null) StrikeTrail.emitting = state == EnemyState.Attack && phase >= 0.2f && phase <= 0.8f;
        }
    }
}
