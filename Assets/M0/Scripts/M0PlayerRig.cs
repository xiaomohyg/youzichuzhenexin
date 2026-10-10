using UnityEngine;
using UnityEngine.Rendering;
using System.Collections.Generic;

namespace M0
{
    // Presentation only: the combat controller owns timings and damage.
    public sealed class M0PlayerRig : MonoBehaviour
    {
        public Transform Root, RightHand, LeftHand, RightUpper, RightForearm, LeftUpper, LeftForearm;
        public Transform Body, Torso, Head, LeftLeg, RightLeg, BodyLeftArm, BodyRightArm;
        public Transform LeftKnee, RightKnee;
        public TrailRenderer SwordTrail;
        public Transform BodySword, BodyGun;
        float guardBlend;
        Renderer[] firstPersonRenderers;
        Renderer[] bodyRenderers, leftHandRenderers;
        readonly Dictionary<Renderer, ShadowCastingMode> originalShadowModes = new Dictionary<Renderer, ShadowCastingMode>();
        readonly Dictionary<Renderer, bool> originalEnabled = new Dictionary<Renderer, bool>();
        static readonly Vector3 Rest = new Vector3(0.36f, -0.38f, 0.62f);
        static readonly Quaternion RestRotation = Quaternion.Euler(-12, 0, -18);
        public void Present(M0Player p)
        {
            CacheRenderers();
            Body.gameObject.SetActive(true);
            Root.gameObject.SetActive(!p.ThirdPerson);
            foreach (var renderer in firstPersonRenderers)
            {
                renderer.enabled = originalEnabled[renderer];
                renderer.shadowCastingMode = originalShadowModes[renderer];
            }
            foreach (var renderer in bodyRenderers)
            {
                renderer.enabled = originalEnabled[renderer];
                renderer.shadowCastingMode = originalShadowModes[renderer];
                bool firstPersonOnlyPart = renderer.transform.IsChildOf(Head) ||
                    renderer.transform.IsChildOf(BodyLeftArm) || renderer.transform.IsChildOf(BodyRightArm);
                if (firstPersonOnlyPart) renderer.enabled = p.ThirdPerson;
            }
            if (!p.ThirdPerson)
                foreach (var renderer in leftHandRenderers) renderer.shadowCastingMode = ShadowCastingMode.ShadowsOnly;
            BodySword.gameObject.SetActive(p.ThirdPerson && p.Armed && !p.Ranged);
            BodyGun.gameObject.SetActive(p.ThirdPerson && p.Ranged);
            Root.SetPositionAndRotation(p.ThirdPerson ? p.EyePosition : p.View.transform.position,
                p.ThirdPerson ? p.transform.rotation * Quaternion.Euler(p.LookAngles.y * 0.4f, 0, 0) : p.View.transform.rotation);
            guardBlend = Mathf.MoveTowards(guardBlend, p.Blocking ? 1 : 0, Time.deltaTime * (p.Blocking ? 12 : 7));
            Vector3 position = Rest; Quaternion rotation = RestRotation;
            if (p.CounterAttacking)
            {
                float t = p.CounterProgress;
                if (p.CounterRanged)
                {
                    Vector3 charge = new Vector3(0.02f, -0.30f, 0.34f), release = new Vector3(-0.04f, -0.12f, 0.86f);
                    Quaternion chargeRot = Quaternion.Euler(-42, 12, -8), releaseRot = Quaternion.Euler(-4, 4, -5);
                    if (t < 0.62f)
                    { float u = Mathf.SmoothStep(0, 1, t / 0.62f); position = Vector3.Lerp(Rest, charge, u); rotation = Quaternion.Slerp(RestRotation, chargeRot, u); }
                    else
                    { float u = Mathf.SmoothStep(0, 1, (t - 0.62f) / 0.38f); position = Vector3.Lerp(charge, release, u); rotation = Quaternion.Slerp(chargeRot, releaseRot, u); }
                }
                else
                {
                    Vector3 wind = new Vector3(-0.42f, -0.08f, 0.30f), strike = new Vector3(0.08f, -0.10f, 1.03f);
                    Quaternion windRot = Quaternion.Euler(-112, -55, 88), strikeRot = Quaternion.Euler(42, 10, -28);
                    if (t < 0.62f)
                    { float u = Mathf.SmoothStep(0, 1, t / 0.62f); position = Vector3.Lerp(wind, strike, u); rotation = Quaternion.Slerp(windRot, strikeRot, u); }
                    else
                    { float u = Mathf.SmoothStep(0, 1, (t - 0.62f) / 0.38f); position = Vector3.Lerp(strike, Rest, u); rotation = Quaternion.Slerp(strikeRot, RestRotation, u); }
                }
            }
            else if (p.Attacking)
            {
                Vector3 wind, strike; Vector3 windRot, strikeRot;
                if (p.HeavyAttack) { wind = new Vector3(0.18f, 0.05f, 0.48f); strike = new Vector3(0.03f, -0.55f, 0.85f); windRot = new Vector3(-85, 10, 15); strikeRot = new Vector3(70, 0, -15); }
                else if (p.Combo == 2) { wind = new Vector3(-0.30f, -0.28f, 0.57f); strike = new Vector3(0.48f, -0.24f, 0.77f); windRot = new Vector3(-20, -45, 60); strikeRot = new Vector3(25, 55, -60); }
                else if (p.Combo == 3) { wind = new Vector3(0.35f, -0.57f, 0.42f); strike = new Vector3(0.02f, -0.08f, 0.85f); windRot = new Vector3(70, 0, -20); strikeRot = new Vector3(-30, -20, 25); }
                else { wind = new Vector3(0.50f, -0.18f, 0.42f); strike = new Vector3(-0.28f, -0.4f, 0.80f); windRot = new Vector3(-45, 40, -65); strikeRot = new Vector3(35, -45, 70); }
                float t = p.AttackProgress, peak = p.StrikePhase, strikeEnd = Mathf.Min(0.72f, peak + 0.24f);
                if (t < peak)
                { float u = Mathf.SmoothStep(0, 1, t / peak); position = Vector3.Lerp(Rest, wind, u); rotation = Quaternion.Slerp(RestRotation, Quaternion.Euler(windRot), u); }
                else if (t < strikeEnd)
                { float u = Mathf.Sin((t - peak) / (strikeEnd - peak) * Mathf.PI * 0.5f); position = Vector3.Lerp(wind, strike, u); rotation = Quaternion.Slerp(Quaternion.Euler(windRot), Quaternion.Euler(strikeRot), u); }
                else
                { float u = Mathf.SmoothStep(0, 1, (t - strikeEnd) / (1 - strikeEnd)); position = Vector3.Lerp(strike, Rest, u); rotation = Quaternion.Slerp(Quaternion.Euler(strikeRot), RestRotation, u); }
            }
            // Keep the visible blade in front of the body capsule so the model, not a view cone, catches strikes.
            position = Vector3.Lerp(position, new Vector3(0.30f, -0.15f, 0.80f), guardBlend);
            rotation = Quaternion.Slerp(rotation, Quaternion.Euler(-12, -12, 62), guardBlend);
            position += new Vector3(0.04f, -0.07f, -0.16f) * p.GuardImpact;
            rotation *= Quaternion.Euler(-22 * p.GuardImpact, 0, 12 * p.GuardImpact);
            if (p.IsDodging)
            {
                float brace = Mathf.Sin(p.DodgeProgress * Mathf.PI);
                position += new Vector3(-p.DodgeLocalDirection.x * 0.12f, -0.22f, -0.18f) * brace;
                rotation = Quaternion.Slerp(rotation, Quaternion.Euler(25, 0, -35), brace);
            }
            float sway = Mathf.Sin(Time.time * 7) * 0.009f * M0Sandbox.Instance.Input.Move.magnitude;
            RightHand.localPosition = position + Vector3.up * sway; RightHand.localRotation = rotation;
            LeftHand.localPosition = Vector3.Lerp(new Vector3(-0.36f, -0.42f + sway, 0.55f), new Vector3(-0.21f, -0.20f, 0.59f), guardBlend);
            LeftHand.localRotation = Quaternion.Euler(-10, 0, guardBlend * -22);
            LeftHand.localPosition += new Vector3(0.02f, -0.045f, -0.13f) * p.GuardImpact;
            LeftHand.localRotation *= Quaternion.Euler(-18 * p.GuardImpact, 0, -9 * p.GuardImpact);
            if (p.CounterAttacking && !p.CounterRanged)
            {
                float counter = Mathf.Sin(p.CounterProgress * Mathf.PI);
                LeftHand.localPosition = Vector3.Lerp(LeftHand.localPosition, RightHand.localPosition + new Vector3(-0.17f, -0.10f, -0.02f), counter);
                LeftHand.localRotation = Quaternion.Slerp(LeftHand.localRotation, Quaternion.Euler(-38, -18, -22), counter);
            }
            else if (p.Attacking && p.HeavyAttack)
                LeftHand.localPosition = Vector3.Lerp(LeftHand.localPosition, RightHand.localPosition + new Vector3(-0.13f, -0.09f, -0.015f), Mathf.Sin(p.AttackProgress * Mathf.PI));
            if (p.IsDodging) LeftHand.localPosition += new Vector3(0.06f, -0.16f, -0.12f) * Mathf.Sin(p.DodgeProgress * Mathf.PI);
            Arms(RightUpper, RightForearm, RightHand.localPosition, 1);
            Arms(LeftUpper, LeftForearm, LeftHand.localPosition, -1);
            p.Blade.gameObject.SetActive(p.Armed && !p.Ranged); p.Gun.gameObject.SetActive(p.Ranged);
            bool trail = p.Armed && !p.Ranged && p.Attacking && p.AttackProgress >= p.StrikePhase && p.AttackProgress < p.StrikePhase + 0.24f;
            SwordTrail.emitting = trail;
            if (!p.Armed || p.Ranged || !p.Attacking) SwordTrail.Clear();
            p.SampleMeleeSweep();
            BodyPose(p);
        }
        void CacheRenderers()
        {
            if (firstPersonRenderers != null && bodyRenderers != null && leftHandRenderers != null &&
                originalEnabled.Count == firstPersonRenderers.Length + bodyRenderers.Length &&
                originalShadowModes.Count == originalEnabled.Count) return;
            originalEnabled.Clear(); originalShadowModes.Clear();
            firstPersonRenderers = Root.GetComponentsInChildren<Renderer>(true);
            bodyRenderers = Body.GetComponentsInChildren<Renderer>(true);
            leftHandRenderers = LeftHand.GetComponentsInChildren<Renderer>(true);
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
        static void Arms(Transform upper, Transform fore, Vector3 wrist, float side)
        {
            Vector3 shoulder = new Vector3(side * 0.38f, -0.35f, -0.08f);
            Vector3 elbow = Vector3.Lerp(shoulder, wrist, 0.48f) + new Vector3(side * 0.09f, -0.16f, -0.04f);
            Segment(upper, shoulder, elbow, 0.17f); Segment(fore, elbow, wrist, 0.14f);
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
            if (p.Attacking)
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
            Head.localRotation = Quaternion.Euler(p.LookAngles.y, 0, 0);
        }
    }
}
