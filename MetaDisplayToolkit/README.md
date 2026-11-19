# MetaDisplayToolkit — source code

| Folder / file | What it is |
|---|---|
| `MetaDisplayToolkit.unitypackage` (Releases) | **The file to import into Unity** (Assets → Import Package → Custom Package…). |
| `MetaDisplayDemo.apk` (Releases) | APK of the sample scene, ready to test on the glasses: `adb install -r MetaDisplayDemo.apk` |
| `MetaDisplayAvatarDemo.apk` (Releases) | APK of the 3D avatar sample (package `com.metadisplay.avatar`, register it in Meta AI too). |
| `Assets/MetaDisplayToolkit/` | Package contents (C#, editor, shader, `.aar`). Read the `README.md` inside it. |
| `android-bridge/` | Gradle project of the Kotlin bridge → builds `MetaDisplayBridge.aar`. |
| `tools/unity-tests/` | Validation scripts (sample scenes, PlayMode tests, APK builds, package export, relay check). |

## Rebuilding the Android bridge

Requires JDK 17+ (Android Studio's works) and Android SDK 36.

```powershell
cd android-bridge
$env:JAVA_HOME = "C:\Program Files\Android\Android Studio\jbr"
.\gradlew.bat :bridge:assembleRelease
copy bridge\build\outputs\aar\bridge-release.aar ..\Assets\MetaDisplayToolkit\Plugins\Android\MetaDisplayBridge.aar
```

The bridge is compiled against `com.meta.wearable:mwdat-*:1.0.1` with `compileOnly`. The real dependencies enter the
APK through `MetaDisplayGradlePostProcessor` (Editor), according to `ProjectSettings/MetaDisplaySettings.json`.

## Architecture

```
C# (MetaWearables, MetaGlassesDisplay, MetaCamera, ...)
   │  AndroidJavaClass.CallStatic  ─────────────►  Kotlin (MDCore, MDDisplay, MDCamera, MDMotion, MDInputs, MDSpeech, MDVoice, MDMock)
   │                                                    │ coroutines on the Android main thread
   │  ◄──── MetaDisplayListener.onEvent(type, JSON) ────┘ (AndroidJavaProxy → queue → Unity main thread)
   │  ◄──── polling: YUV frames (direct ByteBuffer), IMU (double[]), PCM audio (byte[])
   ▼
Glasses UI: GlassesNode → JSON → LayoutBuilder.kt → mwdat-display DSL (flexBox/text/image/button/icon/buttonGroup)

3D (Glasses3DView):
  Live/Baked frames: Camera → RenderTexture → JPEG → image node with a data: URI (or a bitmap key) → display
                     one frame in flight, next one after "display.sent" (backpressure + metrics)
  Video clips:       MP4 per action (Editor MediaEncoder + fast start) → HTTPS → VideoPlayer on the glasses
  Web App:           Three.js page on the glasses ◄── WebSocket relay (room) ──► GlassesWebAppLink (ClientWebSocket)
```

The SDK turns every bitmap into a base64 `data:` URI inside the layout (JPEG 85, or PNG when the bitmap has
transparency) and accepts `data:` URIs directly, so the *Direct JPEG* path skips one decode/encode and lets you pick
the quality.

## What was validated

| Test | Result |
|---|---|
| Kotlin bridge compiled against `mwdat-*:1.0.1` (AGP 8.11.1, Kotlin 2.2.21) | ✅ |
| Package compiles on Unity 6000.6.4f1 **without** `com.unity.ugui` (Canvas features off) | ✅ |
| `.unitypackage` imported into a clean project: Unity **6000.6.4f1** and **6000.3.19f1 (6.3 LTS)** | ✅ no errors |
| "Create Sample Scene" menu → 3 screens converted to correct JSON | ✅ |
| PlayMode test (simulation): registration → session → display → navigation → counter → speech → disconnect | ✅ |
| IL2CPP/ARM64 APK build (Unity 6.6, AGP 9.1.1): Maven Central dependencies, manifest, duplicate `libc++_shared` resolved | ✅ 38 MB |
| On real glasses (Samsung SM-S948B): registration, display, clicks, navigation, video, HQ photo | ✅ |
| Camera stop → start cycle hardening (queue, stop confirmation, retry, watchdog) | ⏳ not retested on the glasses yet |
| 3D: avatar scene in simulation on Unity 6.6 (live frames, glasses button, swipe → rotation, speech → reply, SDK bitmap, baked frames, one-shot → idle, video, Web App without relay, simulated 40 KB/s link ≈ 4 fps) | ✅ PlayMode test |
| 3D: package compiles on Unity 6000.3.19f1 | ✅ no errors or warnings |
| 3D: MP4 recorder (264×264 H.264, moov before mdat) | ✅ |
| 3D: `GlassesWebAppLink` ↔ relay ↔ Web App messages (play, say, rotate, look, hello, action) | ✅ PlayMode test + Node relay |
| 3D: exported Web App rendered in headless Chrome (robot, caption and commands from the relay) | ✅ |
| 3D on real glasses: frame rate over Bluetooth, video clips, Web App | ⏳ not tested on the glasses yet |
