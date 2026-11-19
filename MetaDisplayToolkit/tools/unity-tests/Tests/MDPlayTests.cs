using System.Collections;
using System.Linq;
using MetaDisplay;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

public class MDPlayTests
{
    static IEnumerator WaitUntil(System.Func<bool> condition, int frames = 120)
    {
        for (int i = 0; i < frames && !condition(); i++) yield return null;
    }

    static string Json => MetaGlassesDisplay.LastLayoutJson ?? "";

    [UnityTest]
    public IEnumerator SampleScene_FullFlowInSimulation()
    {
        SceneManager.LoadScene("MetaDisplayDemo");
        yield return null;
        yield return WaitUntil(() => MetaGlassesDisplay.IsReady && GlassesScreen.Current != null);

        Debug.Log($"MDTEST play: registration={MetaWearables.Registration} session={MetaWearables.Session} display={MetaGlassesDisplay.State} screen={GlassesScreen.Current?.name}");
        Assert.AreEqual(RegistrationState.Registered, MetaWearables.Registration);
        Assert.AreEqual(SessionState.Started, MetaWearables.Session);
        Assert.AreEqual(DisplayState.Started, MetaGlassesDisplay.State);
        Assert.AreEqual("Home", GlassesScreen.Current.name);
        StringAssert.Contains("Hello, glasses!", Json);
        Assert.IsTrue(MetaWearables.Devices.ContainsKey("editor-glasses"));

        // Phone mirror: only the current screen is visible
        var screens = GlassesScreen.All.ToList();
        foreach (var s in screens)
        {
            var group = s.GetComponent<CanvasGroup>();
            Assert.IsNotNull(group, s.name + " has no CanvasGroup");
            Assert.AreEqual(s.IsShowing ? 1f : 0f, group.alpha, "visibility of " + s.name);
        }

        // Tap "Counter" (c0 on Home) -> navigates
        MetaGlassesDisplay.SimulateClick("c0");
        yield return null;
        Assert.AreEqual("Counter", GlassesScreen.Current.name);
        StringAssert.Contains("Taps: 0", Json);

        // Tap "+1" (c0 on Counter) -> counter and re-send
        MetaGlassesDisplay.SimulateClick("c0");
        yield return null;
        StringAssert.Contains("Taps: 1", Json);
        MetaGlassesDisplay.SimulateClick("c0");
        yield return null;
        StringAssert.Contains("Taps: 2", Json);

        // "Back" (c1) -> Home
        MetaGlassesDisplay.SimulateClick("c1");
        yield return null;
        Assert.AreEqual("Home", GlassesScreen.Current.name);

        // Code-built UI + simulated speech
        int sent = 0;
        MetaGlassesDisplay.Sent += _ => sent++;
        MetaGlassesDisplay.Show(Glasses.Card().Add(Glasses.Heading("Code")));
        yield return null;
        yield return null;
        Assert.GreaterOrEqual(sent, 1);
        string heard = null;
        MetaSpeech.FinalTranscription += t => heard = t;
        MetaEditorSimulation.SimulateTranscription("hello world");
        yield return null;
        yield return null;
        Assert.AreEqual("hello world", heard);

        MetaWearables.StopSession();
        yield return null;
        yield return null;
        Assert.AreEqual(SessionState.Stopped, MetaWearables.Session);
        Debug.Log("MDTEST play: OK");
    }
}
