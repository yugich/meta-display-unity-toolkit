# Meta Display Toolkit for Unity

Toolkit for building Unity (Android) apps that use the **Meta Ray-Ban Display** and other Meta glasses through the
**Meta Wearables Device Access Toolkit (DAT) 1.0.1**. It covers:

| Feature | C# class | Notes |
|---|---|---|
| Meta AI registration, devices, permissions, updates | `MetaWearables` | |
| Session with the glasses | `MetaWearables.StartSession` / `StopSession` | |
| **Display**: screens, clicks, images, icons, video | `MetaGlassesDisplay`, `Glasses`, `GlassesScreen` | 600×600 |
| **Glasses UI drawn on the Canvas** | `GlassesScreen` + `Glasses*` components | automatic conversion |
| Camera (video → `RenderTexture`, photos, audio) | `MetaCamera` | audio and HQ photo are experimental |
| IMU of the glasses and the Neural Band | `MetaMotion` | experimental |
| Inputs (navigation, select, back, capture, drag) | `MetaInputs` | experimental |
| Speech recognition on the glasses | `MetaSpeech` | experimental |
| "Hey Meta, start {app}" | `MetaVoiceInvocations` | experimental |
| Simulated glasses (no hardware) + display preview | `MetaMockDevice` | |
| Editor simulation (Play Mode) | automatic | |

> "Experimental" = works in **Developer Mode** and beta channels, but cannot ship to production yet (Meta's rule).

---

## 1. Quick start (5 minutes)

1. **Requirements:** Unity **6.0.45+ or 6.3 LTS+** with Android Build Support; glasses firmware V128+; Meta AI app V292+;
   **Developer Mode** on and the **Device Access Toolkit installed on the glasses** (Meta AI → Settings → App Info → tap the version 5× → Install, while wearing the glasses).
2. Import the package: **Assets → Import Package → Custom Package… → `MetaDisplayToolkit.unitypackage`**.
3. Open **Meta Display → Project Setup** and click **Fix all**
   (Android, uGUI/TextMeshPro, API 31/35+, IL2CPP ARM64, Internet).
4. **Meta Display → Create Sample Scene**. Press **Play**: the Editor simulates the glasses and you can navigate the
   screens by clicking the mirror in the middle of the screen.
5. **File → Build Profiles → Android → Build And Run**. On the phone, use the **Meta Display** panel:
   *1. Register in Meta AI* → *2. Connect to the glasses*. The "Home" screen appears on the glasses.

There is no need to edit `AndroidManifest.xml` or `mainTemplate.gradle`: `MetaDisplayGradlePostProcessor` adds the
Maven Central dependencies, permissions, `APPLICATION_ID`/`CLIENT_TOKEN` and the Meta AI callback scheme.

---

## 2. What goes to the glasses and what stays on the phone

- **Phone:** any regular Unity Canvas.
- **Glasses:** objects with the **`GlassesScreen`** component (a 600×600 area). Each screen has a
  **Phone visibility** option:
  - *Glasses only*: hidden on the phone;
  - *Glasses + mirror on the phone*: the current screen appears centered on the phone (great for debugging);
  - *Glasses + thumbnail*: appears small in a corner.
- In the **`MetaDisplayManager`** Inspector, the **UI map** section lists every Canvas in the scene and lets you switch
  its destination (Phone ↔ Glasses) with one click.
- An object with **`GlassesIgnore`** only appears on the phone, even inside a glasses screen.

## 3. Drawing the glasses UI on the Canvas

Use **GameObject → Meta Display →** *Glasses Screen, Container (Column/Row/Card), Text (Heading/Body/Caption),
Button, Button Group, Image, Icon*. The Canvas is the **preview**: the `Glasses*` components adjust layout, colors
and sizes so it looks like the glasses.

| On the Canvas | On the glasses |
|---|---|
| `GlassesFlexBox` or Horizontal/Vertical/Grid **LayoutGroup** | `flexBox` (direction, gap, padding, alignments, *Card* background, click) |
| `GlassesText`, **TextMeshPro** or **Text** | `text` (Heading/Body/Meta; primary/secondary color) |
| `GlassesButton` or **Button** | `button` (style, icon, primary action, **Navigate To** another screen) |
| `GlassesImage`, **RawImage** or **Image** with a sprite | `image` (texture sent as PNG once, or an HTTPS URL) |
| `GlassesIcon` | one of the 116 native icons (`GlassesIconName`) |
| `GlassesButtonGroup` | `buttonGroup` |

Tips:
- **Black = transparent** on the glasses. The screen's black background is only for the preview.
- Content is converted **when it is sent**. With *Auto Refresh* on, changing a text re-sends the screen.
- Toggle, Slider, InputField and Dropdown do not exist on the glasses (the Inspector warns you).
- The `GlassesScreen` Inspector shows **the JSON sent** and the validation warnings.
- No-code navigation: on `GlassesButton`, set **Navigate To** to another `GlassesScreen`.

## 4. From code

```csharp
using MetaDisplay;

MetaGlassesDisplay.Show(
    Glasses.Column(gap: 12, padding: 24).Add(
        Glasses.Row(gap: 12).Add(Glasses.Icon(GlassesIconName.SmartGlasses), Glasses.Heading("Hello!")),
        Glasses.Card().Add(Glasses.Text("Body text"), Glasses.Meta("caption")),
        Glasses.Image(myTexture),
        Glasses.ButtonGroup(
            Glasses.Button("OK", () => Debug.Log("tap"), icon: GlassesIconName.Checkmark, primary: true),
            Glasses.Button("Cancel", null, GlassesButtonStyle.Secondary))));

GlassesScreen.Find("home").Show();            // screen drawn on the Canvas
MetaGlassesDisplay.PlayVideo("https://.../clip.mp4");

MetaCamera.Start(CameraQuality.Low, CameraFps.Fps15);
rawImage.texture = MetaCamera.Texture;        // RenderTexture updated every frame
MetaCamera.CapturePhoto(tex => photo = tex);

MetaMotion.SampleReceived += s => Debug.Log(s.Orientation);
MetaInputs.InputReceived += e => Debug.Log(e);
MetaSpeech.FinalTranscription += text => Debug.Log(text);
```

See `Samples/MetaDisplayCodeSample.cs` for a complete code-only example.

## 5. Testing without the glasses

- **Editor (Play Mode):** automatic simulation of registration → session → display. Click the screen mirror to test the buttons.
- **Phone, no glasses:** check *Simulated glasses on the phone* on the `MetaDisplayManager` (or use the panel's *Mock* tab).
  MockDeviceKit draws the **simulated display on top of the app** and lets you inject inputs, speech and "Hey Meta".
  *Preview in the PC's Chrome*: *Preview in the PC's Chrome* button → `adb forward tcp:9000 tcp:9000` → open `http://127.0.0.1:9000/`.

## 6. Hardware/SDK limitations

- 600×600 screen. Each send replaces the whole screen (there are no partial updates).
- Everything goes over Bluetooth: large and many images make sending slow.
- The display dims after 20 s and sleeps after 25 s without interaction (the session stays alive).
- A two-finger Back on the temple, on the root screen, **ends the session**.
- Video: MP4 over HTTPS only, up to 400 px per side and 70,000 px total.
- The Unity 3D scene is **not** mirrored to the glasses. You can send *snapshots* as images (`Glasses.Image(renderTexture)`), but not in real time.

## 7. Troubleshooting

| Symptom | Fix |
|---|---|
| `NO_ELIGIBLE_DEVICE` when connecting | Glasses off, not being worn, or without a display. Check in Meta AI. |
| `DAT_APP_ON_THE_GLASSES_UPDATE_REQUIRED` | *Update the DAT app on the glasses* button on the panel (or `MetaWearables.OpenGlassesAppUpdate()`). |
| `CAPABILITY_DENIED` | Developer Mode off or DAT not installed on the glasses; experimental features must be enabled in the Wearables Developer Center for production builds. |
| Registration does not return to the app | Check the *Callback scheme* in Project Setup (the build injects the intent-filter). |
| Camera colors swapped | `MetaCamera.LayoutOverride = YuvLayout.NV21` (or the button on the panel). |
| Video does not come back after stopping | Check the panel log: if it says *Permission Camera: Denied*, the user picked "Allow once" in Meta AI (each start asks again). If the camera sends no frames within 12 s, the toolkit restarts the stream once on its own. |
| HQ photo is slow | The photo comes over Bluetooth: at full resolution (~1.6 MB) it takes ~1 min, plus a few seconds of HEIC → JPEG conversion. The video pauses during the capture and comes back on its own. Use `PhotoResolution.Medium` for tests. |
| "Start" during an HQ photo | The request is queued and the video starts when the photo is done. |
| Gradle error `minCompileSdk 35` / AGP | Old Unity. Use 6.0.45+ or 6.3+ and Target API ≥ 35. |
| `ClassNotFoundException com.metadisplay.bridge` | `MetaDisplayBridge.aar` must have the Android platform enabled. |

Logs on the PC: `adb logcat -s MetaDisplayUnity Unity DAT:*`
(Android drops old logs of "chatty" apps; to investigate something that already happened, enlarge the buffer before testing with `adb logcat -G 16M`).

## 8. Structure

```
MetaDisplayToolkit/
├─ Plugins/Android/MetaDisplayBridge.aar   Kotlin bridge (source in android-bridge/)
├─ Runtime/Core          MetaWearables, events, Editor simulation
├─ Runtime/Display       GlassesNode (UI model), MetaGlassesDisplay, icons
├─ Runtime/Canvas        GlassesScreen, Glasses* components, Canvas → glasses converter
├─ Runtime/Capabilities  camera, IMU, inputs, speech, voice, MockDevice
├─ Runtime/Components    MetaDisplayManager, debug panel, sample counter
├─ Resources/MetaDisplay YUV → RGB camera shader
├─ Editor                Setup, Gradle post-processor, menus, inspectors
└─ Samples               code-only example
```

Project settings live in `ProjectSettings/MetaDisplaySettings.json` (credentials, modules included in the APK, SDK version).
For production: replace `Application ID`/`Client Token` (Wearables Developer Center) and uncheck *Mock Device*.
