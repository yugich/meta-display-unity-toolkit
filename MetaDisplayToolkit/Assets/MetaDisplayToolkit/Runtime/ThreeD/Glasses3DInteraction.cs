using System;
using UnityEngine;
using UnityEngine.Events;

namespace MetaDisplay
{
    /// <summary>
    /// Lets the wearer interact with the object shown by a <see cref="Glasses3DView"/> directly on the glasses:
    /// touchpad / Neural Band gestures, speech and head movement become actions (Play, Rotate, Look).
    /// Buttons below the object are configured in the view itself.
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("Meta Display/Glasses 3D Interaction")]
    public class Glasses3DInteraction : MonoBehaviour
    {
        [Serializable] public class TextEvent : UnityEvent<string> { }

        public enum Axis { X, Y, Z, MinusX, MinusY, MinusZ }

        [Tooltip("Empty = the Glasses3DView on this object.")]
        public Glasses3DView view;

        // ── Gestures (Inputs capability) ──
        [Tooltip("Starts the Inputs capability (touchpad and Neural Band events).")]
        public bool startInputs = true;
        [Tooltip("Action for each gesture. 'rotate:30' rotates the object; empty = nothing.")]
        public string swipeLeft = "rotate:-30";
        public string swipeRight = "rotate:30";
        public string swipeUp = "";
        public string swipeDown = "";
        public string select = "wave";
        public string captureButton = "";
        [Tooltip("Degrees of rotation per unit of drag (Neural Band / touchpad drag). 0 = off.")]
        public float dragDegreesPerUnit = 0.5f;

        // ── Speech (Speech capability) ──
        [Tooltip("Starts the Speech capability: what the wearer says arrives in onHeard.")]
        public bool startSpeech;
        [Tooltip("Action while the wearer is speaking (partial transcriptions).")]
        public string listeningAction = "listen";
        [Tooltip("Action after a sentence is heard (e.g. while your code prepares a reply).")]
        public string heardAction = "think";
        [Tooltip("Shows \"You said: …\" below the object (for testing).")]
        public bool echoSpeech = true;
        public TextEvent onHeard = new TextEvent();

        // ── Head (Motion capability) ──
        [Tooltip("Starts the Motion capability and makes the object look where the wearer's head turns (IGlasses3DLookTarget).")]
        public bool followHead;
        [Tooltip("Device axes used for yaw and pitch. The glasses IMU frame is not documented: adjust while testing.")]
        public Axis yawAxis = Axis.Y;
        public Axis pitchAxis = Axis.X;
        [Range(0f, 2f)] public float headScale = 1f;
        [Tooltip("Look commands per second sent to the Web App.")]
        [Range(1f, 30f)] public float lookRate = 10f;

        Quaternion m_Reference = Quaternion.identity;
        bool m_HasReference;
        float m_NextLook;
        bool m_Listening;

        void Reset() => view = GetComponent<Glasses3DView>();

        void Awake()
        {
            if (view == null) view = GetComponent<Glasses3DView>();
        }

        void OnEnable()
        {
            MetaInputs.InputReceived += OnInput;
            MetaSpeech.TranscriptionReceived += OnSpeech;
            MetaMotion.SampleReceived += OnMotion;
            MetaWearables.SessionStateChanged += OnSession;
        }

        void OnDisable()
        {
            MetaInputs.InputReceived -= OnInput;
            MetaSpeech.TranscriptionReceived -= OnSpeech;
            MetaMotion.SampleReceived -= OnMotion;
            MetaWearables.SessionStateChanged -= OnSession;
        }

        void Start()
        {
            if (MetaWearables.Session == SessionState.Started) StartCapabilities();
        }

        void OnSession(SessionState state)
        {
            if (state != SessionState.Started) return;
            m_HasReference = false;
            // Only once connected: speech asks for the microphone permission in Meta AI, which needs a registered app.
            StartCapabilities();
        }

        void StartCapabilities()
        {
            if (startInputs) MetaInputs.Start(false);
            if (startSpeech) MetaSpeech.Start();
            if (followHead) MetaMotion.Start(MotionRate.Hz30);
        }

        /// <summary>Uses the current head orientation as "looking straight ahead".</summary>
        public void Recenter() => m_HasReference = false;

        void Act(string action)
        {
            if (view != null && !string.IsNullOrEmpty(action)) view.Play(action);
        }

        void OnInput(GlassesInputEvent e)
        {
            switch (e.Kind)
            {
                case InputKind.Nav:
                    switch (e.Direction)
                    {
                        case NavDirection.Left: Act(swipeLeft); break;
                        case NavDirection.Right: Act(swipeRight); break;
                        case NavDirection.Up: Act(swipeUp); break;
                        case NavDirection.Down: Act(swipeDown); break;
                    }
                    break;
                case InputKind.Select: Act(select); break;
                case InputKind.Capture: Act(captureButton); break;
                case InputKind.Drag:
                    if (view != null && dragDegreesPerUnit != 0f && e.DragAction == DragAction.Move)
                        view.Rotate(e.Delta.x * dragDegreesPerUnit);
                    break;
            }
        }

        void OnSpeech(SpeechResult result)
        {
            if (view == null || string.IsNullOrEmpty(result.Text)) return;
            if (!result.IsFinal)
            {
                if (!m_Listening)
                {
                    m_Listening = true;
                    Act(listeningAction);
                }
                return;
            }
            m_Listening = false;
            if (echoSpeech) view.Say("You said: " + result.Text, 4f);
            Act(heardAction);
            onHeard?.Invoke(result.Text);
        }

        void OnMotion(MotionSample sample)
        {
            if (!followHead || view == null || sample.Source != MotionSource.Glasses || !sample.HasOrientation) return;
            var q = sample.Orientation;
            if (!m_HasReference)
            {
                m_Reference = q;
                m_HasReference = true;
            }
            var euler = (Quaternion.Inverse(m_Reference) * q).eulerAngles;
            float yaw = Pick(euler, yawAxis) * headScale;
            float pitch = Pick(euler, pitchAxis) * headScale;
            if (Time.unscaledTime < m_NextLook) return;
            m_NextLook = Time.unscaledTime + 1f / lookRate;
            view.Look(yaw, pitch);
        }

        static float Pick(Vector3 euler, Axis axis)
        {
            float v;
            switch (axis)
            {
                case Axis.X: case Axis.MinusX: v = euler.x; break;
                case Axis.Y: case Axis.MinusY: v = euler.y; break;
                default: v = euler.z; break;
            }
            v = Mathf.DeltaAngle(0f, v);
            return axis >= Axis.MinusX ? -v : v;
        }
    }
}
