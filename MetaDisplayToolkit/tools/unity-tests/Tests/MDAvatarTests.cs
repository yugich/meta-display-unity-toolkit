using System.Collections;
using System.IO;
using MetaDisplay;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

/// <summary>Avatar sample scene: every 3D strategy in the Editor simulation (run MDTestRunner.Avatar first).</summary>
public class MDAvatarTests
{
    /// <summary>Waits by real time (batchmode runs thousands of frames per second).</summary>
    static IEnumerator WaitUntil(System.Func<bool> condition, float seconds = 5f)
    {
        float end = Time.realtimeSinceStartup + seconds;
        while (!condition() && Time.realtimeSinceStartup < end) yield return null;
    }

    static IEnumerator WaitSeconds(float seconds)
    {
        float end = Time.realtimeSinceStartup + seconds;
        while (Time.realtimeSinceStartup < end) yield return null;
    }

    static string Json => MetaGlassesDisplay.LastLayoutJson ?? "";

    /// <summary>
    /// Needs a relay and a Web App (or tools/unity-tests/relay-check/fake-glasses.mjs) in room "unitytest":
    /// set MD_RELAY_URL, e.g. ws://localhost:8099. Skipped otherwise.
    /// </summary>
    [UnityTest]
    public IEnumerator WebAppLink_TalksThroughTheRelay()
    {
        var relay = System.Environment.GetEnvironmentVariable("MD_RELAY_URL");
        if (string.IsNullOrEmpty(relay)) Assert.Ignore("MD_RELAY_URL not set");
        var go = new GameObject("link");
        var link = go.AddComponent<GlassesWebAppLink>();
        link.relayUrl = relay;
        link.room = "unitytest";
        link.connectOnStart = false;
        GlassesWebAppLink.Message last = null;
        link.MessageReceived += m => last = m;
        link.Connect();
        yield return WaitUntil(() => link.WebAppOnline, 10f);
        Debug.Log("MDTEST link: " + link.Status + " actions=" + string.Join(",", link.WebAppActions));
        Assert.IsTrue(link.WebAppOnline, "Web App said hello");
        link.SendPlay("wave");
        yield return WaitUntil(() => last != null && last.type == "action", 5f);
        Assert.AreEqual("wave", last?.name, "action echoed back by the glasses");
        link.SendSay("Hi \"there\"\nsecond line", 2f);
        link.SendRotate(30f);
        link.SendLook(10f, -5f);
        yield return null;
        link.Disconnect();
        Object.Destroy(go);
        Debug.Log("MDTEST link: OK");
    }

    [UnityTest]
    public IEnumerator AvatarScene_AllStrategiesInSimulation()
    {
        SceneManager.LoadScene("MetaDisplayAvatarDemo");
        yield return null;
        yield return WaitUntil(() => MetaGlassesDisplay.IsReady);
        var view = Object.FindAnyObjectByType<Glasses3DView>();
        Assert.IsNotNull(view, "Glasses3DView");
        Assert.IsNotNull(view.PoseSource, "SampleAvatar as pose source");

        // ── Live frames ──
        yield return WaitUntil(() => view.Metrics.FramesSent > 0);
        Debug.Log("MDTEST 3d live: " + view.Metrics);
        Assert.Greater(view.Metrics.FramesSent, 0, "live frames sent");
        StringAssert.Contains("data:image/jpeg;base64,", Json);
        StringAssert.Contains("Swipe to rotate me", Json);
        Assert.Greater(view.Metrics.LastBytes, 1000, "JPEG size");

        // Button on the glasses ("Wave" is the first click id)
        string played = null;
        view.onAction.AddListener(a => played = a);
        MetaGlassesDisplay.SimulateClick("c0");
        yield return null;
        Assert.AreEqual("wave", played, "glasses button plays its action");

        // Swipe right on the touchpad rotates the avatar
        float yaw0 = view.target.eulerAngles.y;
        MetaEditorSimulation.SimulateInput(InputKind.Nav, NavDirection.Right);
        yield return WaitSeconds(0.3f);
        Assert.AreEqual(30f, Mathf.DeltaAngle(yaw0, view.target.eulerAngles.y), 0.5f, "swipe rotates 30 degrees");

        // Speech -> chat sample reply
        MetaEditorSimulation.SimulateTranscription("hello there");
        yield return null;
        yield return null;
        Assert.AreEqual("wave", view.CurrentAction, "speech reply action");
        StringAssert.Contains("Hi!", view.CurrentCaption);

        // SDK bitmap encoding uses an image key instead of a data URI
        view.encoding = Glasses3DEncoding.SdkBitmap;
        view.ApplySettings();
        int before = view.Metrics.FramesSent;
        yield return WaitUntil(() => view.Metrics.FramesSent > before);
        StringAssert.Contains("\"imageKey\":\"g3d_live\"", Json);
        view.encoding = Glasses3DEncoding.DirectJpeg;

        // ── Baked frames ──
        view.SetStrategy(Glasses3DStrategy.BakedFrames);
        yield return WaitUntil(() => !view.IsBaking && view.HasBaked("idle") && view.HasBaked("spin"), 20f);
        Assert.IsTrue(view.HasBaked("spin"), "baked");
        yield return WaitUntil(() => view.Metrics.FramesSent > 2);
        Debug.Log("MDTEST 3d baked: " + view.Metrics);
        Assert.Greater(view.Metrics.FramesSent, 2, "baked frames sent");
        view.Play("spin");
        yield return WaitSeconds(2.2f);
        Assert.AreEqual("idle", view.CurrentAction, "one-shot returns to idle");

        // ── Video clips ──
        view.videoBaseUrl = "https://example.com/clips";
        view.SetStrategy(Glasses3DStrategy.VideoClips);
        yield return null;
        yield return null;
        Assert.AreEqual(VideoState.Playing, MetaGlassesDisplay.Video, "video plays");

        // ── Web App (no relay: works alone, no errors) ──
        view.SetStrategy(Glasses3DStrategy.WebApp);
        yield return null;
        view.Play("happy");
        Assert.IsFalse(view.webAppLink.IsConnected);

        // ── Simulated Bluetooth link: live frames slow down ──
        view.SetStrategy(Glasses3DStrategy.LiveFrames);
        view.updateMode = Glasses3DUpdateMode.Continuous;
        view.editorLinkKBps = 40f;
        view.editorLinkLatencyMs = 100f;
        view.ApplySettings();
        view.ResetMetrics();
        yield return WaitSeconds(3.5f);
        Debug.Log("MDTEST 3d simulated link 40 KB/s: " + view.Metrics);
        Assert.Greater(view.Metrics.FramesSent, 0);
        Assert.Greater(view.Metrics.AvgLatencyMs, 100f, "latency includes the simulated link");
        view.editorLinkKBps = 0f;
        view.editorLinkLatencyMs = 0f;
        view.ApplySettings();

#if UNITY_EDITOR
        // ── MP4 clips (Editor recorder, through reflection: the test assembly is not an Editor assembly) ──
        var recorder = System.Type.GetType("MetaDisplay.Editor.Glasses3DVideoRecorder, MetaDisplay.Editor");
        Assert.IsNotNull(recorder, "recorder type");
        var folder = Path.Combine(Path.GetTempPath(), "md-clips-test");
        if (Directory.Exists(folder)) Directory.Delete(folder, true);
        var pose = view.FindPose("wave");
        var record = recorder.GetMethod("Record");
        var path = Path.Combine(folder, "wave.mp4");
        Directory.CreateDirectory(folder);
        record.Invoke(null, new object[] { view, pose, path, 264, 264, 24, true });
        var bytes = File.ReadAllBytes(path);
        string head = System.Text.Encoding.ASCII.GetString(bytes, 0, Mathf.Min(bytes.Length, 4096));
        Debug.Log($"MDTEST 3d mp4: {bytes.Length} bytes, moov at {head.IndexOf("moov")}, mdat at {head.IndexOf("mdat")}");
        Assert.Greater(bytes.Length, 1000);
        int moov = head.IndexOf("moov");
        Assert.GreaterOrEqual(moov, 0, "moov in the first 4 KB (fast start)");
        int mdat = head.IndexOf("mdat");
        Assert.IsTrue(mdat < 0 || moov < mdat, "moov before mdat");
#endif
        MetaWearables.StopSession();
        yield return null;
        yield return null;
        Debug.Log("MDTEST 3d: OK");
    }
}
