using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

namespace MetaDisplay
{
    /// <summary>How a 3D object (e.g. an avatar) reaches the glasses. See the README, "3D objects and avatars".</summary>
    public enum Glasses3DStrategy
    {
        /// <summary>A Unity camera renders the object; each frame is sent as a JPEG image. Interactive, few frames per second.</summary>
        [InspectorName("Live frames (render → JPEG over Bluetooth)")] LiveFrames,
        /// <summary>Each action is pre-rendered once into a few JPEG frames and played as a flipbook. No render cost per frame.</summary>
        [InspectorName("Baked frames (pre-rendered flipbook)")] BakedFrames,
        /// <summary>Each action is an MP4 clip (≤ 400 px per side, ≤ 70,000 px) on an HTTPS URL. Smooth, not real-time.</summary>
        [InspectorName("Video clips (MP4 over HTTPS)")] VideoClips,
        /// <summary>The object is rendered on the glasses by a Three.js Web App; Unity sends commands through a relay.</summary>
        [InspectorName("Web App (Three.js on the glasses)")] WebApp,
    }

    public enum Glasses3DUpdateMode
    {
        [InspectorName("Continuous (every interval)")] Continuous,
        [InspectorName("Only when the image changes")] OnChange,
        [InspectorName("Manual (SendFrameNow)")] Manual,
    }

    public enum Glasses3DEncoding
    {
        /// <summary>JPEG encoded in Unity and sent as a data URI: one encode, quality under your control.</summary>
        [InspectorName("Direct JPEG (data URI, recommended)")] DirectJpeg,
        /// <summary>Bitmap handed to the SDK, which re-encodes it (JPEG 85, or PNG with transparency).</summary>
        [InspectorName("SDK bitmap (SDK re-encodes)")] SdkBitmap,
    }

    /// <summary>An action of the object (animation, expression) that can be baked into frames or recorded as a clip.</summary>
    [Serializable]
    public class Glasses3DPose
    {
        [Tooltip("Action name, e.g. idle, wave, talk. Play(\"wave\") uses it in every strategy.")]
        public string name = "idle";
        [Tooltip("Frames rendered for this action (Baked frames strategy).")]
        [Min(1)] public int frames = 8;
        [Tooltip("Playback rate of the baked frames. The link may deliver fewer; frames are picked by time.")]
        [Min(0.5f)] public float fps = 6f;
        public bool loop = true;
        [Tooltip("Optional: clip sampled on the target (Generic/Legacy). Leave empty to use the Animator state or IGlasses3DPoseSource.")]
        public AnimationClip clip;
        [Tooltip("Optional: Animator state played on the target's Animator.")]
        public string animatorState = "";
        [Tooltip("Optional: MP4 URL (HTTPS) for the Video clips strategy. Empty = Video base URL + name + .mp4")]
        public string videoUrl = "";
    }

    /// <summary>A button shown below the object on the glasses (Live/Baked strategies).</summary>
    [Serializable]
    public class Glasses3DButton
    {
        public string label = "Wave";
        [Tooltip("Action played when tapped (e.g. wave, rotate:30). Empty = only the event.")]
        public string action = "wave";
        public GlassesButtonStyle style = GlassesButtonStyle.Secondary;
        public UnityEvent onClick = new UnityEvent();
    }

    /// <summary>Delivery statistics of the current strategy (shown in the debug panel and the Inspector).</summary>
    public struct Glasses3DMetrics
    {
        public Glasses3DStrategy Strategy;
        /// <summary>Frames confirmed by the glasses.</summary>
        public int FramesSent;
        /// <summary>Captures not sent because the image did not change.</summary>
        public int FramesSkipped;
        public int FramesFailed;
        public int InFlight;
        /// <summary>Confirmed frames per second (last 3 s).</summary>
        public float Fps;
        /// <summary>Send → confirmation time of the last frame.</summary>
        public float LastLatencyMs;
        public float AvgLatencyMs;
        /// <summary>Encoded image size of the last frame.</summary>
        public int LastBytes;
        public float AvgBytes;
        /// <summary>Phone time spent reading the render and encoding it.</summary>
        public float CaptureMs;

        public override string ToString() =>
            $"{Strategy}: {Fps:0.0} fps · latency {LastLatencyMs:0} ms (avg {AvgLatencyMs:0}) · {LastBytes / 1024f:0.0} KB/frame · " +
            $"capture {CaptureMs:0.0} ms · sent {FramesSent} · skipped {FramesSkipped} · failed {FramesFailed} · in flight {InFlight}";
    }

    /// <summary>
    /// Implement on the object (e.g. the avatar) to drive its actions from the toolkit. Optional: without it,
    /// <see cref="Glasses3DView"/> uses the pose clip, the Animator state or only the onAction event.
    /// </summary>
    public interface IGlasses3DPoseSource
    {
        /// <summary>Actions this object knows (shown as buttons in the debug panel).</summary>
        IReadOnlyList<string> Actions { get; }
        /// <summary>Starts an action in real time (Live frames, phone view).</summary>
        void Play(string action);
        /// <summary>Puts the object in the exact pose of an action at a normalized time (0..1), for baking and recording.</summary>
        void ApplyPose(string action, float normalizedTime);
        /// <summary>Stops/resumes the object's own animation while frames are being baked.</summary>
        void SetFrozen(bool frozen);
    }

    /// <summary>Optional: the object can look somewhere (e.g. follow the wearer's head). Angles in degrees.</summary>
    public interface IGlasses3DLookTarget
    {
        void SetLook(float yaw, float pitch);
    }
}
