using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.Media;
using UnityEngine;

namespace MetaDisplay.Editor
{
    /// <summary>
    /// Records each pose of a <see cref="Glasses3DView"/> as an MP4 clip the glasses can play
    /// (≤ 400 px per side, ≤ 70,000 px in total, H.264, fast start). Upload the folder to any HTTPS host and set
    /// "Video base URL". Uses UnityEditor.Media.MediaEncoder (Windows and macOS Editor).
    /// </summary>
    public static class Glasses3DVideoRecorder
    {
        public const int MaxSide = 400;
        public const int MaxPixels = 70000;

        /// <summary>Largest square that fits the glasses video limits, rounded to an even number (H.264).</summary>
        public static int DefaultSize => 264;

        public static string Validate(int width, int height)
        {
            if (width > MaxSide || height > MaxSide) return $"Each side must be ≤ {MaxSide} px.";
            if (width * height > MaxPixels) return $"{width}x{height} = {width * height} px; the glasses accept up to {MaxPixels} px.";
            if (width % 2 != 0 || height % 2 != 0) return "Width and height must be even (H.264).";
            return null;
        }

        /// <summary>Records every pose to &lt;folder&gt;/&lt;pose&gt;.mp4. Returns the files written.</summary>
        public static List<string> RecordAll(Glasses3DView view, string folder, int width, int height, int fps, bool fastStart = true)
        {
            var error = Validate(width, height);
            if (error != null) throw new ArgumentException(error);
            Directory.CreateDirectory(folder);
            var written = new List<string>();
            try
            {
                for (int i = 0; i < view.poses.Count; i++)
                {
                    var pose = view.poses[i];
                    if (pose == null || string.IsNullOrEmpty(pose.name)) continue;
                    EditorUtility.DisplayProgressBar("Recording glasses clips", pose.name, (float)i / view.poses.Count);
                    var path = Path.Combine(folder, SafeName(pose.name) + ".mp4");
                    Record(view, pose, path, width, height, fps, fastStart);
                    written.Add(path);
                }
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }
            return written;
        }

        /// <summary>Records one pose. Its length is the pose's frames / fps (at least 1 s).</summary>
        public static void Record(Glasses3DView view, Glasses3DPose pose, string path, int width, int height, int fps, bool fastStart = true)
        {
            float seconds = Mathf.Max(1f, pose.frames / Mathf.Max(0.5f, pose.fps));
            int frameCount = Mathf.Max(2, Mathf.RoundToInt(seconds * fps));
            var frames = view.RenderFrames(pose, width, height, frameCount);
            try
            {
                var attributes = new VideoTrackAttributes
                {
                    frameRate = new MediaRational(fps),
                    width = (uint)width,
                    height = (uint)height,
                    includeAlpha = false,
                    bitRateMode = VideoBitrateMode.Medium,
                };
                if (File.Exists(path)) File.Delete(path);
                using (var encoder = new MediaEncoder(path, attributes))
                {
                    foreach (var frame in frames) encoder.AddFrame(frame);
                }
                if (fastStart) Mp4FastStart.Process(path);
            }
            finally
            {
                foreach (var frame in frames) UnityEngine.Object.DestroyImmediate(frame);
            }
        }

        static string SafeName(string name)
        {
            foreach (var c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
            return name.Replace(' ', '_');
        }
    }
}
