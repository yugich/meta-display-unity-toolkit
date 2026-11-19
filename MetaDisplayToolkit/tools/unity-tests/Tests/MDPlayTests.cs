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
    public IEnumerator DemoScene_FluxoCompletoNaSimulacao()
    {
        SceneManager.LoadScene("MetaDisplayDemo");
        yield return null;
        yield return WaitUntil(() => MetaGlassesDisplay.IsReady && GlassesScreen.Current != null);

        Debug.Log($"MDTEST play: registro={MetaWearables.Registration} sessao={MetaWearables.Session} display={MetaGlassesDisplay.State} tela={GlassesScreen.Current?.name}");
        Assert.AreEqual(RegistrationState.Registered, MetaWearables.Registration);
        Assert.AreEqual(SessionState.Started, MetaWearables.Session);
        Assert.AreEqual(DisplayState.Started, MetaGlassesDisplay.State);
        Assert.AreEqual("Início", GlassesScreen.Current.name);
        StringAssert.Contains("Olá, óculos!", Json);
        Assert.IsTrue(MetaWearables.Devices.ContainsKey("editor-glasses"));

        // Espelho no celular: só a tela atual visível
        var screens = GlassesScreen.All.ToList();
        foreach (var s in screens)
        {
            var group = s.GetComponent<CanvasGroup>();
            Assert.IsNotNull(group, s.name + " sem CanvasGroup");
            Assert.AreEqual(s.IsShowing ? 1f : 0f, group.alpha, "visibilidade de " + s.name);
        }

        // Toque em "Contador" (c0 na tela Início) -> navega
        MetaGlassesDisplay.SimulateClick("c0");
        yield return null;
        Assert.AreEqual("Contador", GlassesScreen.Current.name);
        StringAssert.Contains("Toques: 0", Json);

        // Toque em "+1" (c0 na tela Contador) -> contador e reenvio
        MetaGlassesDisplay.SimulateClick("c0");
        yield return null;
        StringAssert.Contains("Toques: 1", Json);
        MetaGlassesDisplay.SimulateClick("c0");
        yield return null;
        StringAssert.Contains("Toques: 2", Json);

        // "Voltar" (c1) -> Início
        MetaGlassesDisplay.SimulateClick("c1");
        yield return null;
        Assert.AreEqual("Início", GlassesScreen.Current.name);

        // Por código + fala simulada
        int sent = 0;
        MetaGlassesDisplay.Sent += _ => sent++;
        MetaGlassesDisplay.Show(Glasses.Card().Add(Glasses.Heading("Código")));
        yield return null;
        yield return null;
        Assert.GreaterOrEqual(sent, 1);
        string heard = null;
        MetaSpeech.FinalTranscription += t => heard = t;
        MetaEditorSimulation.SimulateTranscription("olá mundo");
        yield return null;
        yield return null;
        Assert.AreEqual("olá mundo", heard);

        MetaWearables.StopSession();
        yield return null;
        yield return null;
        Assert.AreEqual(SessionState.Stopped, MetaWearables.Session);
        Debug.Log("MDTEST play: OK");
    }
}
