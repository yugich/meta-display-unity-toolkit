using MetaDisplay;
using UnityEngine;

/// <summary>
/// 100% code example (no Canvas): builds the glasses UI with <see cref="Glasses"/>,
/// shows the glasses camera on a material and writes what the user says on the display.
/// Put it on a GameObject. It can live alongside the MetaDisplayManager (which handles the connection).
/// </summary>
public class MetaDisplayCodeSample : MonoBehaviour
{
    [Tooltip("Optional: Renderer that shows the glasses camera video.")]
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
                    Glasses.Heading("Built in code")),
                Glasses.Card().Add(
                    Glasses.Text($"Taps: {m_Taps}"),
                    Glasses.Meta("Say something to show it here")),
                Glasses.ButtonGroup(
                    Glasses.Button("+1", () => { m_Taps++; ShowHome(); }, icon: GlassesIconName.Plus, primary: true),
                    Glasses.Button("Camera", ToggleCamera, GlassesButtonStyle.Secondary, GlassesIconName.VideoCamera))));
    }

    void ToggleCamera()
    {
        if (MetaCamera.State == CameraStreamState.Stopped) MetaCamera.Start(CameraQuality.Low, CameraFps.Fps15);
        else MetaCamera.Stop();
    }

    void OnSpeech(string text)
    {
        MetaGlassesDisplay.Show(Glasses.Card().Add(Glasses.Meta("You said:"), Glasses.Heading(text),
            Glasses.Button("Back", ShowHome, GlassesButtonStyle.Outline, GlassesIconName.ArrowLeft, primary: true)));
    }

    void OnFrame(RenderTexture frame)
    {
        if (cameraPreview != null) cameraPreview.material.mainTexture = frame;
    }
}
