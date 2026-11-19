using System;
using System.Collections.Generic;
using UnityEngine;

namespace MetaDisplay
{
    /// <summary>
    /// Small procedural robot used to test every 3D strategy without importing assets. Bright colors on purpose:
    /// the glasses display only adds light, so dark colors look transparent.
    /// Replace it with your avatar: implement <see cref="IGlasses3DPoseSource"/> or use Animator states.
    /// </summary>
    [AddComponentMenu("Meta Display/Example - Avatar (robot)")]
    public class SampleAvatar : MonoBehaviour, IGlasses3DPoseSource, IGlasses3DLookTarget
    {
        static readonly string[] s_Actions = { "idle", "wave", "talk", "happy", "think", "nod", "listen", "spin" };

        public Color bodyColor = new Color(0.25f, 0.8f, 1f);
        public Color headColor = new Color(0.95f, 0.97f, 1f);
        public Color eyeColor = new Color(0.15f, 0.45f, 1f);
        public Color mouthColor = new Color(1f, 0.45f, 0.65f);
        public Color accentColor = new Color(1f, 0.85f, 0.25f);

        Transform m_Rig, m_HeadPivot, m_ArmL, m_ArmR, m_EyeL, m_EyeR, m_Mouth, m_AntennaTip;
        Vector3 m_EyeScale, m_MouthScale, m_TipScale;
        string m_Action = "idle";
        float m_Start, m_Until;
        bool m_Frozen;
        float m_LookYaw, m_LookPitch, m_TargetYaw, m_TargetPitch;

        public IReadOnlyList<string> Actions => s_Actions;
        public string CurrentAction => m_Action;

        void Awake() => EnsureBuilt();

        // ───────────────────────── IGlasses3DPoseSource ─────────────────────────

        public void Play(string action)
        {
            if (string.IsNullOrEmpty(action) || Array.IndexOf(s_Actions, action) < 0) return;
            m_Action = action;
            m_Start = Time.time;
            m_Until = 0f;
        }

        /// <summary>Plays a looping action (e.g. talk) for a while, then goes back to idle.</summary>
        public void PlayFor(string action, float seconds)
        {
            Play(action);
            m_Until = Time.time + seconds;
        }

        public void ApplyPose(string action, float normalizedTime)
        {
            EnsureBuilt();
            Evaluate(action, normalizedTime, 0f, 0f, blinkAt: action == "idle" ? 0.5f : -1f);
        }

        public void SetFrozen(bool frozen) => m_Frozen = frozen;

        public void SetLook(float yaw, float pitch)
        {
            m_TargetYaw = Mathf.Clamp(yaw, -40f, 40f);
            m_TargetPitch = Mathf.Clamp(pitch, -25f, 25f);
        }

        public static float DurationOf(string action)
        {
            switch (action)
            {
                case "wave": return 1.8f;
                case "talk": return 0.6f;
                case "happy": return 1.2f;
                case "think": return 2f;
                case "nod": return 1.2f;
                case "listen": return 1.6f;
                case "spin": return 1.6f;
                default: return 2.4f;
            }
        }

        public static bool IsLoop(string action) => action == "idle" || action == "talk" || action == "listen";

        // ───────────────────────── Animation ─────────────────────────

        void Update()
        {
            if (m_Frozen || m_Rig == null) return;
            float elapsed = Time.time - m_Start;
            float t = elapsed / DurationOf(m_Action);
            bool loop = IsLoop(m_Action);
            if ((!loop && t >= 1f) || (m_Until > 0f && Time.time >= m_Until))
            {
                Play("idle");
                t = 0f;
            }
            float smoothing = 1f - Mathf.Exp(-8f * Time.deltaTime);
            m_LookYaw = Mathf.Lerp(m_LookYaw, m_TargetYaw, smoothing);
            m_LookPitch = Mathf.Lerp(m_LookPitch, m_TargetPitch, smoothing);
            // Blink every ~3.7 s, whatever the action.
            bool blink = Time.time % 3.7f < 0.12f;
            Evaluate(m_Action, loop ? t % 1f : Mathf.Clamp01(t), m_LookYaw, m_LookPitch, blink ? 0f : -1f, blinkNow: blink);
        }

        static float Ease(float x) => x * x * (3f - 2f * x);

        void Evaluate(string action, float t, float lookYaw, float lookPitch, float blinkAt, bool blinkNow = false)
        {
            if (m_Rig == null) return;
            float wave = Mathf.Sin(t * Mathf.PI * 2f);
            var rigPos = Vector3.zero;
            float rigYaw = 0f;
            var head = new Vector3(-lookPitch, lookYaw, 0f);
            float armL = -10f, armR = 10f, armRx = 0f;
            float mouthOpen = 0f, tipPulse = 0f;

            switch (action)
            {
                case "idle":
                    rigPos.y = 0.02f * wave;
                    head.z += 3f * wave;
                    break;
                case "wave":
                {
                    float up = Ease(Mathf.Clamp01(t * 5f)) * (1f - Ease(Mathf.Clamp01((t - 0.8f) * 5f)));
                    armR = Mathf.Lerp(10f, 150f, up) + 22f * Mathf.Sin(t * Mathf.PI * 2f * 4f) * up;
                    head.z -= 8f * up;
                    mouthOpen = 0.6f * up;
                    break;
                }
                case "talk":
                    mouthOpen = Mathf.Abs(Mathf.Sin(t * Mathf.PI * 2f));
                    head.x += 3f * wave;
                    break;
                case "happy":
                {
                    float jump = Mathf.Sin(Mathf.PI * t);
                    rigPos.y = 0.28f * jump;
                    armL = -(10f + 140f * jump);
                    armR = 10f + 140f * jump;
                    mouthOpen = 0.8f;
                    break;
                }
                case "think":
                {
                    float k = Ease(Mathf.Clamp01(t * 4f)) * (1f - Ease(Mathf.Clamp01((t - 0.85f) * 6.6f)));
                    head.z += 14f * k;
                    head.x -= 8f * k;
                    // Hand to the chin: arm forward, up and inward.
                    armR = Mathf.Lerp(10f, -30f, k);
                    armRx = -110f * k;
                    tipPulse = 0.5f * k * (0.5f + 0.5f * Mathf.Sin(t * Mathf.PI * 8f));
                    break;
                }
                case "nod":
                    head.x += 16f * Mathf.Sin(t * Mathf.PI * 4f) * (1f - t);
                    break;
                case "listen":
                    head.z += 10f;
                    tipPulse = 0.5f + 0.5f * Mathf.Sin(t * Mathf.PI * 4f);
                    break;
                case "spin":
                    rigYaw = 360f * Ease(t);
                    break;
            }

            m_Rig.localPosition = rigPos;
            m_Rig.localRotation = Quaternion.Euler(0f, rigYaw, 0f);
            m_HeadPivot.localRotation = Quaternion.Euler(head);
            m_ArmL.localRotation = Quaternion.Euler(0f, 0f, armL);
            m_ArmR.localRotation = Quaternion.Euler(armRx, 0f, armR);
            m_Mouth.localScale = new Vector3(m_MouthScale.x, m_MouthScale.y * (1f + 3.5f * mouthOpen), m_MouthScale.z);
            m_AntennaTip.localScale = m_TipScale * (1f + 0.5f * tipPulse);
            bool closed = blinkNow || (blinkAt >= 0f && Mathf.Abs(t - blinkAt) < 0.04f);
            var eye = closed ? new Vector3(m_EyeScale.x, m_EyeScale.y * 0.12f, m_EyeScale.z) : m_EyeScale;
            m_EyeL.localScale = eye;
            m_EyeR.localScale = eye;
        }

        // ───────────────────────── Build ─────────────────────────

        /// <summary>Creates the robot's parts if they do not exist yet (also works in Edit Mode).</summary>
        public void EnsureBuilt()
        {
            if (m_Rig != null) return;
            var existing = transform.Find("Rig");
            if (existing != null)
            {
                Bind(existing);
                return;
            }

            var rig = new GameObject("Rig").transform;
            rig.SetParent(transform, false);
            Part("Body", PrimitiveType.Capsule, rig, new Vector3(0f, 0.75f, 0f), new Vector3(0.7f, 0.55f, 0.55f), bodyColor);
            var headPivot = Pivot("HeadPivot", rig, new Vector3(0f, 1.42f, 0f));
            Part("Head", PrimitiveType.Sphere, headPivot, new Vector3(0f, 0.08f, 0f), new Vector3(0.62f, 0.58f, 0.58f), headColor);
            Part("EyeL", PrimitiveType.Sphere, headPivot, new Vector3(-0.12f, 0.12f, 0.27f), new Vector3(0.11f, 0.13f, 0.06f), eyeColor);
            Part("EyeR", PrimitiveType.Sphere, headPivot, new Vector3(0.12f, 0.12f, 0.27f), new Vector3(0.11f, 0.13f, 0.06f), eyeColor);
            Part("Mouth", PrimitiveType.Cube, headPivot, new Vector3(0f, -0.06f, 0.28f), new Vector3(0.16f, 0.035f, 0.03f), mouthColor);
            Part("AntennaStem", PrimitiveType.Cylinder, headPivot, new Vector3(0f, 0.42f, 0f), new Vector3(0.03f, 0.08f, 0.03f), headColor);
            Part("AntennaTip", PrimitiveType.Sphere, headPivot, new Vector3(0f, 0.53f, 0f), Vector3.one * 0.09f, accentColor);
            var armL = Pivot("ArmL", rig, new Vector3(-0.42f, 1.05f, 0f));
            Part("ArmLMesh", PrimitiveType.Capsule, armL, new Vector3(0f, -0.25f, 0f), new Vector3(0.14f, 0.25f, 0.14f), bodyColor);
            var armR = Pivot("ArmR", rig, new Vector3(0.42f, 1.05f, 0f));
            Part("ArmRMesh", PrimitiveType.Capsule, armR, new Vector3(0f, -0.25f, 0f), new Vector3(0.14f, 0.25f, 0.14f), bodyColor);
            Bind(rig);
        }

        void Bind(Transform rig)
        {
            m_Rig = rig;
            m_HeadPivot = rig.Find("HeadPivot");
            m_ArmL = rig.Find("ArmL");
            m_ArmR = rig.Find("ArmR");
            m_EyeL = m_HeadPivot.Find("EyeL");
            m_EyeR = m_HeadPivot.Find("EyeR");
            m_Mouth = m_HeadPivot.Find("Mouth");
            m_AntennaTip = m_HeadPivot.Find("AntennaTip");
            m_EyeScale = m_EyeL.localScale;
            m_MouthScale = m_Mouth.localScale;
            m_TipScale = m_AntennaTip.localScale;
        }

        static Transform Pivot(string name, Transform parent, Vector3 position)
        {
            var t = new GameObject(name).transform;
            t.SetParent(parent, false);
            t.localPosition = position;
            return t;
        }

        static void Part(string name, PrimitiveType type, Transform parent, Vector3 position, Vector3 scale, Color color)
        {
            var go = GameObject.CreatePrimitive(type);
            go.name = name;
            var collider = go.GetComponent<Collider>();
            if (collider != null)
            {
                if (Application.isPlaying) Destroy(collider);
                else DestroyImmediate(collider);
            }
            go.transform.SetParent(parent, false);
            go.transform.localPosition = position;
            go.transform.localScale = scale;
            var renderer = go.GetComponent<Renderer>();
            // Copy of the pipeline's default material (Standard or URP Lit), so the shader is always in the build.
            var material = new Material(renderer.sharedMaterial) { name = "SampleAvatar " + name, color = color };
            renderer.sharedMaterial = material;
        }
    }
}
