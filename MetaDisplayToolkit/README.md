# MetaDisplayToolkit — source code

| Folder / file | What it is |
|---|---|
| `MetaDisplayToolkit.unitypackage` (Releases) | **The file to import into Unity** (Assets → Import Package → Custom Package…). |
| `MetaDisplayDemo.apk` (Releases) | APK of the sample scene, ready to test on the glasses: `adb install -r MetaDisplayDemo.apk` |
| `Assets/MetaDisplayToolkit/` | Package contents (C#, editor, shader, `.aar`). Read the `README.md` inside it. |
| `android-bridge/` | Gradle project of the Kotlin bridge → builds `MetaDisplayBridge.aar`. |
| `tools/unity-tests/` | Validation scripts (sample scene, PlayMode test, APK build, package export). |

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
```

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
