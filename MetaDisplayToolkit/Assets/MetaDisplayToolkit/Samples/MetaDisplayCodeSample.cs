using MetaDisplay;
using UnityEngine;

/// <summary>
/// Exemplo 100% por código (sem Canvas): monta a UI dos óculos com <see cref="Glasses"/>,
/// mostra a câmera dos óculos num material e escreve no display o que o usuário fala.
/// Coloque num GameObject. Pode conviver com o MetaDisplayManager (ele cuida da conexão).
/// </summary>
public class MetaDisplayCodeSample : MonoBehaviour
{
    [Tooltip("Opcional: Renderer que vai mostrar o vídeo da câmera dos óculos.")]
    public Renderer cameraPreview;
    public bool connectHere = true;

    int m_Taps;

    void OnEnable()
    {
        MetaWearables.RegistrationChanged += OnRegistration;
        MetaGlassesDisplay.StateChanged += OnDisplay;
        MetaSpeech.FinalTranscription += OnSpeech;
        MetaCamera.FrameReady += OnFrame;
    }

    void OnDisable()
    {
        MetaWearables.RegistrationChanged -= OnRegistration;
        MetaGlassesDisplay.StateChanged -= OnDisplay;
        MetaSpeech.FinalTranscription -= OnSpeech;
        MetaCamera.FrameReady -= OnFrame;
    }

    void Start()
    {
        if (MetaDisplayManager.Instance == null) MetaWearables.Initialize();
    }

    void OnRegistration(RegistrationState state)
    {
        if (state != RegistrationState.Registered || !connectHere || MetaDisplayManager.Instance != null) return;
        MetaGlassesDisplay.Enable();
        MetaWearables.StartSession();
    }

    void OnDisplay(DisplayState state)
    {
        if (state == DisplayState.Started) ShowHome();
    }

    void ShowHome()
    {
        MetaGlassesDisplay.Show(
            Glasses.Column(gap: 12, padding: 24).Add(
                Glasses.Row(gap: 12).Add(
                    Glasses.Icon(GlassesIconName.SmartGlasses),
                    Glasses.Heading("Feito por código")),
                Glasses.Card().Add(
                    Glasses.Text($"Toques: {m_Taps}"),
                    Glasses.Meta("Fale algo para aparecer aqui")),
                Glasses.ButtonGroup(
                    Glasses.Button("+1", () => { m_Taps++; ShowHome(); }, icon: GlassesIconName.Plus, primary: true),
                    Glasses.Button("Câmera", ToggleCamera, GlassesButtonStyle.Secondary, GlassesIconName.VideoCamera))));
    }

    void ToggleCamera()
    {
        if (MetaCamera.State == CameraStreamState.Stopped) MetaCamera.Start(CameraQuality.Low, CameraFps.Fps15);
        else MetaCamera.Stop();
    }

    void OnSpeech(string text)
    {
        MetaGlassesDisplay.Show(Glasses.Card().Add(Glasses.Meta("Você disse:"), Glasses.Heading(text),
            Glasses.Button("Voltar", ShowHome, GlassesButtonStyle.Outline, GlassesIconName.ArrowLeft, primary: true)));
    }

    void OnFrame(RenderTexture frame)
    {
        if (cameraPreview != null) cameraPreview.material.mainTexture = frame;
    }
}
