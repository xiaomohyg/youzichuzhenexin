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
                float settle = Mathf.Sin(t * Mathf.PI);
                right = Vector3.Lerp(new Vector3(-8, 0, -8), new Vector3(-118, -58, -64), t);
                left = Vector3.Lerp(new Vector3(0, 0, 8), new Vector3(-72, 24, 30), t);
                torso = new Vector3(-12 * t - settle * 4, -36 * t, -12 * t);
                elbow = Mathf.Lerp(-18, -92, t); brace = t;
            }
            else if (state == EnemyState.Attack)
            {
                float strike = Mathf.SmoothStep(0, 1, Mathf.InverseLerp(0.03f, 0.48f, phase));
                float follow = Mathf.SmoothStep(0, 1, Mathf.InverseLerp(0.58f, 1f, phase));
                Vector3 extended = new Vector3(-88, -8, -8), followThrough = new Vector3(-62, 26, 12);
                right = Vector3.Lerp(new Vector3(-118, -58, -64), extended, strike);
                right = Vector3.Lerp(right, followThrough, follow);
                left = Vector3.Lerp(new Vector3(-72, 24, 30), new Vector3(-38, -12, 18), strike);
                torso = Vector3.Lerp(new Vector3(-12, -36, -12), new Vector3(20, 34, 12), strike);
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
            float blendSpeed = state == EnemyState.Attack ? 38 : state == EnemyState.Windup ? 18 : 12;
            float blend = 1 - Mathf.Exp(-Time.deltaTime * blendSpeed);
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
