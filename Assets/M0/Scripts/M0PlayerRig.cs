using UnityEngine;
using UnityEngine.Rendering;
using System.Collections.Generic;

namespace M0
{
    // Presentation only: the combat controller owns timings and damage.
    public sealed class M0PlayerRig : MonoBehaviour
    {
        public Transform Root, RightHand, LeftHand, RightUpper, RightForearm, LeftUpper, LeftForearm;
        public Transform Body, Torso, Head, Eyes, LeftLeg, RightLeg, BodyLeftArm, BodyRightArm;
        public Transform LeftKnee, RightKnee;
        public TrailRenderer SwordTrail;
        public Transform BodySword, BodyGun;
        Renderer[] firstPersonRenderers;
        Renderer[] bodyRenderers;
        readonly Dictionary<Renderer, ShadowCastingMode> originalShadowModes = new Dictionary<Renderer, ShadowCastingMode>();
        readonly Dictionary<Renderer, bool> originalEnabled = new Dictionary<Renderer, bool>();
        public void EnsureEyeAnchor()
        {
            if (Eyes == null || Body == null) return;
            if (Eyes.parent != Body) Eyes.SetParent(Body, false);
            Eyes.localPosition = new Vector3(0, 1.54f, 0.17f);
            Eyes.localRotation = Quaternion.identity;
        }
        public void Present(M0Player p)
        {
            EnsureEyeAnchor();
            CacheRenderers();
            if (!Body.gameObject.activeSelf) Body.gameObject.SetActive(true);
            Root.gameObject.SetActive(!p.ThirdPerson);
            foreach (var renderer in firstPersonRenderers)
            {
                renderer.enabled = originalEnabled[renderer];
                renderer.shadowCastingMode = p.ThirdPerson
                    ? originalShadowModes[renderer]
                    : ShadowCastingMode.Off;
            }
            foreach (var renderer in bodyRenderers)
            {
                renderer.enabled = true;
                renderer.shadowCastingMode = ShadowCastingMode.On;
            }
            LeftHand.gameObject.SetActive(false);
            LeftUpper.gameObject.SetActive(false);
            LeftForearm.gameObject.SetActive(false);
            BodySword.gameObject.SetActive(p.ThirdPerson && p.Armed && !p.Ranged);
            BodyGun.gameObject.SetActive(p.ThirdPerson && p.Ranged);
            Root.SetPositionAndRotation(p.ThirdPerson ? p.EyePosition : p.View.transform.position,
                p.ThirdPerson ? p.transform.rotation * Quaternion.Euler(p.LookAngles.y * 0.4f, 0, 0) : p.View.transform.rotation);
            BodyPose(p);
            RightHand.position = BodyRightArm.TransformPoint(new Vector3(0, -0.59f, 0));
            RightHand.rotation = BodyRightArm.rotation * Quaternion.Euler(90, 0, 0);
            RightHand.localPosition = Root.InverseTransformPoint(RightHand.position);
            RightHand.localRotation = Quaternion.Inverse(Root.rotation) * RightHand.rotation;
            Vector3 shoulder = Root.InverseTransformPoint(BodyRightArm.position);
            Vector3 elbow = Root.InverseTransformPoint(BodyRightArm.TransformPoint(Vector3.down * 0.30f));
            Vector3 wrist = Root.InverseTransformPoint(RightHand.position);
            Segment(RightUpper, shoulder, elbow, 0.17f);
            Segment(RightForearm, elbow, wrist, 0.14f);
            p.Blade.gameObject.SetActive(p.Armed && !p.Ranged); p.Gun.gameObject.SetActive(p.Ranged);
            bool trail = p.Armed && !p.Ranged && p.Attacking && p.AttackProgress >= p.StrikePhase && p.AttackProgress < p.StrikePhase + 0.24f;
            SwordTrail.emitting = trail;
            if (!p.Armed || p.Ranged || !p.Attacking) SwordTrail.Clear();
            p.SampleMeleeSweep();
        }
        void CacheRenderers()
        {
            if (firstPersonRenderers != null && bodyRenderers != null &&
                originalEnabled.Count == firstPersonRenderers.Length + bodyRenderers.Length &&
                originalShadowModes.Count == originalEnabled.Count) return;
            originalEnabled.Clear(); originalShadowModes.Clear();
            firstPersonRenderers = Root.GetComponentsInChildren<Renderer>(true);
            bodyRenderers = Body.GetComponentsInChildren<Renderer>(true);
            var bodyArmRenderer = BodyRightArm.GetComponentInChildren<Renderer>(true);
            var firstPersonForearmRenderer = RightForearm.GetComponent<Renderer>();
            if (bodyArmRenderer != null && firstPersonForearmRenderer != null)
                firstPersonForearmRenderer.sharedMaterial = bodyArmRenderer.sharedMaterial;
            Cache(firstPersonRenderers); Cache(bodyRenderers);
        }
        void Cache(Renderer[] renderers)
        {
            foreach (var renderer in renderers)
            {
                originalEnabled[renderer] = renderer.enabled;
                originalShadowModes[renderer] = renderer.shadowCastingMode;
            }
        }
        static void Segment(Transform part, Vector3 a, Vector3 b, float width)
        {
            part.localPosition = (a + b) * 0.5f;
            part.localRotation = Quaternion.FromToRotation(Vector3.up, b - a);
            part.localScale = new Vector3(width, (b - a).magnitude, width);
        }
        void BodyPose(M0Player p)
        {
            float crouch = Mathf.Clamp01((1.8f - p.GetComponent<CharacterController>().height) / 0.65f);
            float dodge = p.IsDodging ? Mathf.Sin(p.DodgeProgress * Mathf.PI) : 0;
            Body.localPosition = Vector3.down * (crouch * 0.44f + dodge * 0.14f);
            Body.localRotation = Quaternion.Euler(dodge * 24 + crouch * 12 - p.GuardImpact * 14, 0, -p.DodgeLocalDirection.x * dodge * 18);
            float stride = Mathf.Sin(Time.time * (p.Crouching ? 6 : 10)) * (1 - crouch * 0.65f) * 28 * M0Sandbox.Instance.Input.Move.magnitude;
            float bend = crouch * 62 + dodge * 24;
            LeftLeg.localRotation = Quaternion.Euler(stride - bend - dodge * 20, 0, 0);
            RightLeg.localRotation = Quaternion.Euler(-stride - bend + dodge * 16, 0, 0);
            LeftKnee.localRotation = Quaternion.Euler(bend * 2 + dodge * 10, 0, 0);
            RightKnee.localRotation = Quaternion.Euler(bend * 2 + dodge * 10, 0, 0);
            BodyLeftArm.localRotation = Quaternion.Euler(-stride * 0.6f, 0, -5);
            BodyRightArm.localRotation = Quaternion.Euler(stride * 0.6f, 0, 5);
            if (p.CounterAttacking)
            {
                float t = p.CounterProgress;
                Quaternion wind = p.CounterDodge
                    ? Quaternion.Euler(-116, -58, -48)
                    : Quaternion.Euler(-112, -56, 66);
                Quaternion strike = p.CounterDodge
                    ? Quaternion.Euler(-24, 4, 10)
                    : Quaternion.Euler(24, 58, -62);
                BodyRightArm.localRotation = Quaternion.Slerp(wind, strike, Mathf.SmoothStep(0, 1, t));
            }
            else if (p.Attacking)
            {
                float swing = Mathf.Sin(Mathf.Clamp01((p.AttackProgress - p.StrikePhase) / 0.24f) * Mathf.PI * 0.5f);
                BodyRightArm.localRotation = Quaternion.Euler(Mathf.Lerp(-100, 30, swing), (p.Combo == 2 ? -1 : 1) * (40 - swing * 80), p.Combo == 3 ? 0 : 25 - swing * 50);
            }
            if (p.Blocking) BodyRightArm.localRotation = Quaternion.Euler(-72 - p.GuardImpact * 20, -25, -35);
            if (p.Blocking) BodyLeftArm.localRotation = Quaternion.Euler(-66 - p.GuardImpact * 16, 20, 28);
            if (p.Ranged) BodyRightArm.localRotation = Quaternion.Euler(-86, 0, 0);
            if (p.IsDodging)
            {
                BodyLeftArm.localRotation = Quaternion.Euler(-60 * dodge, 0, -20 * dodge);
                BodyRightArm.localRotation = Quaternion.Euler(-55 * dodge, 10 * dodge, 20 * dodge);
            }
            if (!p.ThirdPerson) BodyLeftArm.localRotation = Quaternion.Euler(stride * 0.16f, 0, -5);
            Head.localRotation = Quaternion.Euler(p.LookAngles.y, 0, 0);
        }
    }
}
