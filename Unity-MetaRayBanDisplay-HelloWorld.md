# Unity → Android → Meta Ray-Ban Display: Hello World

> Study guide based on the official **Meta Wearables Device Access Toolkit (DAT) v1.0.1** documentation
> (checked on 2026-11-19). Main source: <https://wearables.developer.meta.com/docs/develop/dat/display-android>

> **Shortcut:** the [MetaDisplayToolkit/](MetaDisplayToolkit/) folder has the source of a ready-made package
> (`MetaDisplayToolkit.unitypackage`, in the repository Releases) with everything in this guide and more: every SDK
> capability (display, camera, IMU, inputs, speech, "Hey Meta", simulated glasses), glasses UI drawn on the **Canvas**
> and automatic Gradle/manifest configuration. See its [README](MetaDisplayToolkit/Assets/MetaDisplayToolkit/README.md).
> This guide is still useful to understand what happens under the hood.

---

## 1. Short answer: can you use Unity?

**Yes, but only as the "phone app".** Unity builds the Android APK and runs the logic in C#. To talk to the glasses you need a **native Kotlin bridge** (an `.aar`) that calls Meta's official SDK.

What you need to understand before starting:

| Topic | Situation |
|---|---|
| Official Unity SDK | **There is none.** DAT only has native Android (Kotlin) and iOS (Swift) SDKs. The Meta XR/OpenXR Unity packages are for Quest and do not work with the Ray-Ban Display. |
| Where the app runs | **On the phone.** The app sends a ready-made interface to the glasses over Bluetooth, through the Meta AI app. The glasses keep no app state. |
| What appears on the glasses | **Declarative UI:** `flexBox`, `text`, `image`, `button`, `icon` and `video` (MP4 by URL). **600×600** screen. |
| Does the Unity 3D scene appear on the glasses? | **Not directly.** There is no API to send pixels or frames in real time. You can send **snapshots** (render to texture → PNG → `image(bitmap)`), but with Bluetooth latency. It is not suitable for real-time games. |
| Glasses input | Taps on the touchpad or the Neural Band reach the phone as `onClick` callbacks. |

**When Unity makes sense:** you already have a Unity app or game on the phone and want a HUD or "second screen" on the glasses: scoreboard, notifications, instructions, snapshots.
**When it does not:** if the app exists only for the glasses, the native path (Kotlin in Android Studio, using the `DisplayAccess` sample) is simpler. If you want something that runs *inside* the glasses with no phone app, there are **Web Apps** (HTML/CSS/JS served over HTTPS in the glasses' Chromium browser). The documentation does not cover Unity Web builds in that browser.

---

## 2. Hello World architecture

```
┌──────────────────────── Android phone ──────────────────────────┐
│                                                                 │
│  Unity (C#)  MetaDisplayHello.cs                                │
│     │  AndroidJavaClass.CallStatic(...)        ▲                │
│     ▼                                          │ AndroidJavaProxy│
│  metadisplay.aar  (Kotlin: MetaDisplayBridge)  │ (events)        │
│     │                                                           │
│     ▼                                                           │
│  com.meta.wearable:mwdat-core + mwdat-display 1.0.1             │
│     │                                                           │
│     ▼                                                           │
│  Meta AI app (registration, Developer Mode, connection)         │
└─────┼───────────────────────────────────────────────────────────┘
      │ Bluetooth
      ▼
 Meta Ray-Ban Display (the DAT app installed on the glasses renders the UI)
```

SDK flow, wrapped by the bridge:

1. `Wearables.initialize(context)`: once per process.
2. `Wearables.startRegistration(activity)`: opens Meta AI, the user approves and the state goes to `REGISTERED`.
3. `Wearables.createSession(AutoDeviceSelector(filter = { it.isDisplayCapable() }))` and then `session.start()`.
4. When `session.state == STARTED`, call `session.addDisplay()`.
5. When `display.state == STARTED`, call `display.sendContent { ... }`. **Each call replaces the whole screen.**
6. To finish: `session.removeDisplay()` and then `session.stop()`.

### 2.1 How data flows: phone app (DAT) vs Web App

They are two different platforms, and the choice depends on **where your code runs**:

```
 DAT (phone app)                               Web App
 ───────────────                               ───────
 ┌──────────── Glasses ───────────┐            ┌──────────── Glasses ───────────┐
 │ camera · mic · IMU · display   │            │ Chromium browser               │
 │ (capture and pre-process)      │            │   └─ your HTML/CSS/JS runs HERE│
 └───────────────┬────────────────┘            │ IMU · display · Neural Band    │
                 │ Bluetooth                   └───────────────┬────────────────┘
                 │ (video, audio, IMU ↑ / UI ↓)                │ location only
                 ▼                                             ▼ (the glasses have no GPS)
 ┌──────────── Phone ─────────────┐            ┌──────────── Phone ─────────────┐
 │ Meta AI (registration/perms)   │            │ Meta AI (pairing)              │
 │ YOUR APP ← the code runs HERE  │            └────────────────────────────────┘
 └────────────────────────────────┘
```

#### With DAT, the glasses are a wireless peripheral

The glasses capture the data, pre-process it and send everything to the phone over **Bluetooth**. Registration and permissions go through the Meta AI app.

| Feature | How it reaches the phone |
|---|---|
| **Camera** | The glasses crop the image to 9:16, correct the lens distortion, stabilize it and compress it as **HEVC (H.265)**. The video travels over **Bluetooth Classic**, with LOW 360×640, MEDIUM 504×896 or HIGH 720×1280 quality and 2, 7, 15, 24 or 30 fps. If bandwidth drops, the glasses automatically lower the resolution and then the fps, but **never below 15**. Asking for a lower resolution often gives a *better* image, because it is less compressed. |
| **Microphone** | Through the Bluetooth **HFP** profile, mono, 16 or 8 kHz, with beamforming that isolates the wearer's voice (ambient sound is kept low). In beta, audio can also come with the camera video, as 16-bit PCM without beamforming. The permission is requested through the regular Android dialog. |
| **Speaker** | Through the **A2DP** profile (44.1 or 48 kHz stereo), like a regular Bluetooth headset. |
| **Motion (IMU)** | `MotionSample` samples with accelerometer, gyroscope, magnetometer and orientation (quaternion), with their source: **glasses or Neural Band**. Still in beta, only in the development and beta channels. |
| **Display** | The reverse path: the interface built on the phone is serialized and sent over Bluetooth to the glasses. That is why heavy layouts and large images make the screen slow. |

All heavy processing, such as AI or computer vision, runs **on the phone or in your cloud**, not on the glasses. The documentation also says Meta does not access the photos, videos and audio that go straight from the glasses to the third-party app.

#### With a Web App, the code runs inside the glasses

A Web App is served over HTTPS and runs in the Chromium browser of the glasses themselves.

| Has access to | Does not have access to |
|---|---|
| Glasses motion and orientation (`DeviceMotionEvent` and `DeviceOrientationEvent`, with permission) | **Camera** |
| Wrist orientation from the Neural Band (`RelativeOrientationSensor`) | **Raw microphone audio.** Voice only arrives as text dictated into a field, or as Meta AI calls to tools exposed by the page (WebMCP, off by default). |
| Location (`navigator.geolocation`), but the position **comes from the paired phone** | |
| Battery, text-to-speech (TTS), `localStorage` | |

#### Why this split?

The documentation does not explain why. My guess is privacy and hardware. Camera and microphone are the most sensitive features, and with DAT they go through the Meta AI permission flow. Also, the glasses have limited battery and compute, so it makes sense to send the heavy work to the phone.

#### What this means for Unity users

With the Kotlin bridge, Unity on the phone can **receive** the camera video and motion data and process everything in C#, including ML models in Unity (Inference Engine, formerly Sentis). Then it **sends** the result back to the display. The full path looks like this:

```
glasses camera/IMU ──BT──▶ Kotlin (mwdat-camera / mwdat-motion) ──JNI──▶ C# (processes)
                                                                             │
glasses display ◀──BT── Kotlin (mwdat-display, sendContent) ◀──JNI───────────┘
```

The limit on every path is **Bluetooth bandwidth**, so think of summarized results (text, icons, small images), not mirroring frames.

---

## 3. Prerequisites

### Hardware and apps

- **Meta Ray-Ban Display** with firmware **V128 or later**. To check: Meta AI → Devices tab → ⚙️ → General → About → Version.
- **Meta AI app (Android) V292 or later.** It is the minimum version for SDK 1.0.1, according to the [Version Dependencies](https://wearables.developer.meta.com/docs/develop/dat/version-dependencies) page.
- Phone with **Android 10 or later**. The SDK declares minSdk 29, but this guide uses **minSdk 31 (Android 12)**, like the official sample.
- **Developer Mode** on. To turn it on: Meta AI → Settings → App Info → tap *App version* 5×.
- **DAT app installed on the glasses.** Without it, the display does not work:
  1. Put the glasses on. This is required.
  2. In Meta AI, with Dev Mode on, tap **Install** to install the Device Access Toolkit on the glasses.
  3. Keep Meta AI open during the installation, which takes 5 to 10 seconds.

### Tools on the PC

- A recent **Android Studio**, with **JDK 17**. It is used to build the `.aar` bridge.
- **Unity 6.3 LTS (6000.3.x)** with the **Android Build Support** module (OpenJDK, Android SDK and NDK).
  - Unity 6.0 also works from **6000.0.45f1** on.
  - ⚠️ **Do not use Unity 2022.3 or older.** `mwdat-core` pulls `androidx.core:1.16.0`, which requires **AGP ≥ 8.6.0** and **compileSdk ≥ 35**. Unity 6.3 ships AGP 8.10, 9.0 or 9.1, depending on the patch.

### Suggested folder layout

```
my-project/
├─ meta-wearables-dat-android/          (clone of the official repo)
│  └─ samples/DisplayAccess/            (Gradle project; we add the bridge module here)
│     └─ metadisplay/                   (Android Library → builds metadisplay-release.aar)
└─ unity-hello/                         (Unity 6.3 project)
   └─ Assets/
      ├─ Plugins/Android/
      │  ├─ metadisplay-release.aar
      │  ├─ AndroidManifest.xml         (Custom Main Manifest)
      │  ├─ mainTemplate.gradle         (Custom Main Gradle Template)
      │  └─ gradleTemplate.properties   (Custom Gradle Properties Template)
      └─ Scripts/MetaDisplayHello.cs
```

---

## 4. Step 0: validate the hardware with the official sample (no Unity)

Do this before touching Unity. If the native sample does not work, Unity will not work either, and this way you separate hardware or configuration problems from integration problems.

```powershell
cd <your-project-folder>
git clone https://github.com/meta/meta-wearables-dat-android.git
```

1. Open `meta-wearables-dat-android/samples/DisplayAccess` in Android Studio.
2. In `local.properties`, add the credentials. In Developer Mode, the documentation says to use `0`:
   ```properties
   mwdat_application_id=0
   mwdat_client_token=0
   ```
3. Run it on the phone, register the app in Meta AI, connect and open one of the examples. The content should appear on the glasses.

This sample uses **AGP 8.11.1, Kotlin 2.2.21, compileSdk 36, minSdk 31 and Java 17**. That makes it a great base to build the bridge too.

---

## 5. Step 1: the Kotlin bridge (`metadisplay.aar`)

### 5.1 Create the module

In Android Studio, with the `DisplayAccess` project open:

1. **File → New → New Module… → Android Library**
   - Module name: `metadisplay`
   - Package name: `com.example.metadisplay`
   - Language: Kotlin · Minimum SDK: API 31
2. Check that the wizard did these three things. If it did not, do them by hand:
   - `settings.gradle.kts` has `include(":metadisplay")`.
   - `gradle/libs.versions.toml`, under `[plugins]`, has:
     ```toml
     android-library = { id = "com.android.library", version.ref = "agp" }
     ```
   - The root `build.gradle.kts`, inside `plugins { }`, has:
     ```kotlin
     alias(libs.plugins.android.library) apply false
     ```

> If the wizard creates aliases with different names, use the names it created. What matters is that the library plugin uses the **same AGP version** as the sample and that **Kotlin is 2.2.x**.

### 5.2 `metadisplay/build.gradle.kts`

Replace the whole file with:

```kotlin
import org.jetbrains.kotlin.gradle.dsl.JvmTarget

plugins {
  alias(libs.plugins.android.library)
  alias(libs.plugins.jetbrains.kotlin.android)
}

android {
  namespace = "com.example.metadisplay"
  compileSdk = 36

  defaultConfig {
    minSdk = 31
    // Shipped inside the .aar: prevents Unity's R8 from removing the bridge (called through JNI/reflection)
    consumerProguardFiles("consumer-rules.pro")
  }

  compileOptions {
    sourceCompatibility = JavaVersion.VERSION_17
    targetCompatibility = JavaVersion.VERSION_17
  }
}

kotlin { compilerOptions { jvmTarget = JvmTarget.JVM_17 } }

dependencies {
  // Used only to compile. Unity downloads these libraries again (see mainTemplate.gradle),
  // because a local .aar does NOT carry its transitive dependencies.
  implementation(libs.mwdat.core)
  implementation(libs.mwdat.display)
}
```

### 5.3 `metadisplay/consumer-rules.pro`

```proguard
-keep class com.example.metadisplay.** { *; }
```

### 5.4 `metadisplay/src/main/java/com/example/metadisplay/MetaDisplayBridge.kt`

```kotlin
package com.example.metadisplay

import android.app.Activity
import android.content.Context
import android.graphics.BitmapFactory
import android.util.Log
import com.meta.wearable.dat.core.Wearables
import com.meta.wearable.dat.core.selectors.AutoDeviceSelector
import com.meta.wearable.dat.core.session.DeviceSession
import com.meta.wearable.dat.core.session.DeviceSessionState
import com.meta.wearable.dat.core.types.DeviceSessionError
import com.meta.wearable.dat.core.types.RegistrationState
import com.meta.wearable.dat.display.Display
import com.meta.wearable.dat.display.addDisplay
import com.meta.wearable.dat.display.removeDisplay
import com.meta.wearable.dat.display.types.DisplayState
import com.meta.wearable.dat.display.views.ActionRole
import com.meta.wearable.dat.display.views.ButtonStyle
import com.meta.wearable.dat.display.views.CornerRadius
import com.meta.wearable.dat.display.views.Direction
import com.meta.wearable.dat.display.views.FlexBoxBackground
import com.meta.wearable.dat.display.views.IconName
import com.meta.wearable.dat.display.views.ImageSize
import com.meta.wearable.dat.display.views.TextColor
import com.meta.wearable.dat.display.views.TextStyle
import kotlinx.coroutines.CoroutineExceptionHandler
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.Job
import kotlinx.coroutines.SupervisorJob
import kotlinx.coroutines.launch
import kotlinx.coroutines.withContext

/** Implemented in C# through AndroidJavaProxy. Receives every bridge event. */
interface MetaDisplayListener {
  fun onEvent(type: String, message: String)
}

/**
 * Unity -> DAT SDK bridge.
 * C#: new AndroidJavaClass("com.example.metadisplay.MetaDisplayBridge").CallStatic("method", ...)
 * All the work runs on Dispatchers.Main (the Android main thread), so there are no races.
 */
object MetaDisplayBridge {
  private const val TAG = "MetaDisplayBridge"

  private val crashGuard = CoroutineExceptionHandler { _, t ->
    Log.e(TAG, "Unexpected error", t)
    emit("error", t.message ?: t.toString())
  }
  private val scope = CoroutineScope(SupervisorJob() + Dispatchers.Main + crashGuard)

  private lateinit var appContext: Context
  private var listener: MetaDisplayListener? = null
  private var initialized = false
  private var session: DeviceSession? = null
  private var display: Display? = null
  private val sessionJobs = mutableListOf<Job>()

  // ───────────── API called by Unity ─────────────

  /** Call it after the BLUETOOTH_CONNECT permission is granted. */
  @JvmStatic
  fun initialize(activity: Activity, callback: MetaDisplayListener) {
    listener = callback
    scope.launch {
      if (initialized) {
        emit("registration", "${Wearables.registrationState.value}")
        return@launch
      }
      appContext = activity.applicationContext
      // The only possible failure here is ALREADY_INITIALIZED, which is harmless
      Wearables.initialize(appContext).onFailure { error, _ -> emit("info", error.description) }
      initialized = true

      launch { Wearables.registrationState.collect { emit("registration", "$it") } }
      launch {
        Wearables.registrationErrorStream.collect {
          emit("error", "Registration: ${it.getLocalizedDescription(appContext)}")
        }
      }
      launch { Wearables.devices.collect { emit("devices", "${it.size} device(s)") } }
    }
  }

  /** Opens Meta AI so the user approves the app (in Dev Mode it shows up under "Developer mode apps"). */
  @JvmStatic
  fun startRegistration(activity: Activity) {
    scope.launch {
      if (Wearables.registrationState.value == RegistrationState.REGISTERED) {
        emit("registration", "REGISTERED")
      } else {
        Wearables.startRegistration(activity)
      }
    }
  }

  /** Creates the session with the first glasses that have a display and attaches the Display. */
  @JvmStatic
  fun connect() {
    scope.launch {
      if (Wearables.registrationState.value != RegistrationState.REGISTERED) {
        emit("error", "Register the app in Meta AI before connecting")
        return@launch
      }
      if (session != null) {
        emit("info", "Session is already active")
        return@launch
      }
      Wearables.createSession(AutoDeviceSelector(filter = { it.isDisplayCapable() })).fold(
          onSuccess = { newSession -> observeAndStart(newSession) },
          onFailure = { error, _ -> reportSessionError(error) },
      )
    }
  }

  /** The Hello World: a card with a title and text, plus a button that notifies Unity when tapped. */
  @JvmStatic
  fun showHelloWorld(title: String, body: String) {
    scope.launch {
      val d = readyDisplay() ?: return@launch
      d.sendContent {
        flexBox(direction = Direction.COLUMN, gap = 12) {
          flexBox(padding = 24, background = FlexBoxBackground.CARD) {
            text(title, style = TextStyle.HEADING)
            text(body, style = TextStyle.BODY, color = TextColor.SECONDARY)
          }
          button(
              label = "Tap here",
              style = ButtonStyle.PRIMARY,
              iconName = IconName.CHECKMARK,
              // SDK 1.0+: the first PRIMARY action gets the focus when the screen appears
              actionRole = ActionRole.PRIMARY,
              onClick = { emit("click", "hello") },
          )
        }
      }.fold(
          onSuccess = { emit("sent", "Hello World sent") },
          onFailure = { error, _ -> emit("error", "sendContent: ${error.description}") },
      )
    }
  }

  /** Bonus: shows a PNG generated by Unity (e.g. a camera snapshot). */
  @JvmStatic
  fun showImage(png: ByteArray, caption: String) {
    scope.launch {
      val d = readyDisplay() ?: return@launch
      val bitmap =
          withContext(Dispatchers.Default) { BitmapFactory.decodeByteArray(png, 0, png.size) }
      if (bitmap == null) {
        emit("error", "Invalid PNG")
        return@launch
      }
      d.sendContent {
        flexBox(direction = Direction.COLUMN, gap = 8, padding = 16) {
          image(bitmap = bitmap, sizePreset = ImageSize.FILL, cornerRadius = CornerRadius.MEDIUM)
          text(caption, style = TextStyle.META, color = TextColor.SECONDARY)
        }
      }.fold(
          onSuccess = { emit("sent", "Image sent") },
          onFailure = { error, _ -> emit("error", "sendContent: ${error.description}") },
      )
    }
  }

  @JvmStatic
  fun disconnect() {
    scope.launch {
      val s = session ?: return@launch
      withContext(Dispatchers.IO) {
        s.removeDisplay()
        s.stop()
      }
      clearSession()
      emit("session", "STOPPED")
    }
  }

  @JvmStatic
  fun openDatGlassesAppUpdate(activity: Activity) {
    scope.launch { Wearables.openDATGlassesAppUpdate(activity) }
  }

  @JvmStatic
  fun openFirmwareUpdate(activity: Activity) {
    scope.launch { Wearables.openFirmwareUpdate(activity) }
  }

  // ───────────── internals ─────────────

  private fun observeAndStart(newSession: DeviceSession) {
    session = newSession
    sessionJobs += scope.launch { newSession.errors.collect { reportSessionError(it) } }
    sessionJobs +=
        scope.launch {
          newSession.state.collect { state ->
            emit("session", "$state")
            when (state) {
              // Only attach the Display after the session is STARTED
              DeviceSessionState.STARTED -> if (display == null) attachDisplay(newSession)
              // STOPPED is terminal: reconnecting requires a new session
              DeviceSessionState.STOPPED -> clearSession()
              else -> Unit
            }
          }
        }
    newSession.start() // returns immediately; progress arrives via state/errors
  }

  private fun attachDisplay(s: DeviceSession) {
    s.addDisplay().fold(
        onSuccess = { newDisplay ->
          display = newDisplay
          sessionJobs +=
              scope.launch {
                newDisplay.state.collect { state ->
                  emit("display", "$state")
                  if (state == DisplayState.STARTED) emit("ready", "Display ready")
                }
              }
        },
        onFailure = { error, _ -> emit("error", "addDisplay: ${error.description}") },
    )
  }

  private fun reportSessionError(error: DeviceSessionError) {
    if (error == DeviceSessionError.DAT_APP_ON_THE_GLASSES_UPDATE_REQUIRED) {
      emit("update_required", error.description)
    } else {
      emit("error", "Session: ${error.description}")
    }
  }

  private fun readyDisplay(): Display? {
    val d = display
    if (d == null || d.state.value != DisplayState.STARTED) {
      emit("error", "Display is not ready yet (connect first)")
      return null
    }
    return d
  }

  private fun clearSession() {
    sessionJobs.forEach { it.cancel() }
    sessionJobs.clear()
    display = null
    session = null
  }

  private fun emit(type: String, message: String) {
    Log.i(TAG, "[$type] $message")
    listener?.onEvent(type, message)
  }
}
```

### 5.5 Build the `.aar`

```powershell
cd <your-project-folder>\meta-wearables-dat-android\samples\DisplayAccess
.\gradlew.bat :metadisplay:assembleRelease
```

Output: `metadisplay\build\outputs\aar\metadisplay-release.aar`

---

## 6. Step 2: Unity project

### 6.1 Create and configure

1. In Unity Hub, create a **Unity 6.3 LTS** project (*Universal 3D* or *2D* template) named `unity-hello`.
2. **File → Build Profiles → Android → Switch Platform**.
3. Open **Edit → Project Settings → Player → Android tab → Other Settings**:
   - **Package Name:** `com.example.metadisplayhello`
   - **Minimum API Level:** Android 12 (API 31)
   - **Target API Level:** **35 or 36.** It must be ≥ 35, because Unity uses this value as `compileSdk`. If Unity says the platform is missing, let it update the SDK or point it to Android Studio's SDK in *Preferences → External Tools*.
   - **Scripting Backend:** IL2CPP · **Target Architectures:** ARM64
   - **Application Entry Point:** GameActivity (the default) or Activity. Both work.
4. Under **Publishing Settings → Build**, check:
   - ☑ Custom Main Manifest
   - ☑ Custom Main Gradle Template
   - ☑ Custom Gradle Properties Template
   - ☑ Custom Launcher Gradle Template

   Unity creates these files in `Assets/Plugins/Android/`.
5. Copy `metadisplay-release.aar` to `Assets/Plugins/Android/`. In the file's Inspector, confirm the **Android** platform is checked.

### 6.2 `Assets/Plugins/Android/mainTemplate.gradle`

Keep everything Unity generated and only add the two DAT lines to the `dependencies` block, **before** `**DEPS**`:

```groovy
dependencies {
    implementation fileTree(dir: 'libs', include: ['*.jar'])
    // Meta Wearables Device Access Toolkit (the bridge .aar does not carry its transitive dependencies)
    implementation 'com.meta.wearable:mwdat-core:1.0.1'
    implementation 'com.meta.wearable:mwdat-display:1.0.1'
**DEPS**}
```

Unity's Gradle already declares `google()` and `mavenCentral()`, and DAT is published on Maven Central. The transitive dependencies (Kotlin stdlib 2.2, coroutines, AndroidX, OkHttp, protobuf, etc.) come automatically.

> Alternative: if you already use the **External Dependency Manager (EDM4U)**, declare both artifacts in a `Dependencies.xml` instead of editing the template.

### 6.3 `Assets/Plugins/Android/launcherTemplate.gradle`

The SDK's `fbjni` dependency and Unity's IL2CPP both ship `libc++_shared.so`. Without this, the build fails with
`2 files found with path 'lib/arm64-v8a/libc++_shared.so'`. Inside the `android { }` block, add:

```groovy
    packaging {
        jniLibs { pickFirsts += ['lib/*/libc++_shared.so'] }
    }
```

### 6.4 `Assets/Plugins/Android/gradleTemplate.properties`

Make sure this line exists. If it does not, add it:

```properties
android.useAndroidX=true
```

### 6.5 `Assets/Plugins/Android/AndroidManifest.xml`

Start from the file Unity generated and add the blocks marked `DAT`. The example below uses GameActivity. If you picked *Activity*, put the second `<intent-filter>` on `com.unity3d.player.UnityPlayerActivity`.

```xml
<?xml version="1.0" encoding="utf-8"?>
<manifest xmlns:android="http://schemas.android.com/apk/res/android"
    xmlns:tools="http://schemas.android.com/tools">

    <!-- DAT: Bluetooth and Internet permissions -->
    <uses-permission android:name="android.permission.BLUETOOTH" />
    <uses-permission android:name="android.permission.BLUETOOTH_CONNECT" />
    <uses-permission android:name="android.permission.INTERNET" />

    <application>
        <!-- DAT: use 0 in Developer Mode. In production, use the values from the Wearables Developer Center -->
        <meta-data android:name="com.meta.wearable.mwdat.APPLICATION_ID" android:value="0" />
        <meta-data android:name="com.meta.wearable.mwdat.CLIENT_TOKEN" android:value="0" />

        <activity android:name="com.unity3d.player.UnityPlayerGameActivity"
                  android:theme="@style/BaseUnityGameActivityTheme"
                  android:exported="true">
            <intent-filter>
                <action android:name="android.intent.action.MAIN" />
                <category android:name="android.intent.category.LAUNCHER" />
            </intent-filter>

            <!-- DAT: callback scheme Meta AI uses to return to the app -->
            <intent-filter>
                <action android:name="android.intent.action.VIEW" />
                <category android:name="android.intent.category.DEFAULT" />
                <category android:name="android.intent.category.BROWSABLE" />
                <data android:scheme="metadisplayhello" />
            </intent-filter>

            <meta-data android:name="unityplayer.UnityActivity" android:value="true" />
            <meta-data android:name="android.app.lib_name" android:value="game" />
        </activity>
    </application>
</manifest>
```

### 6.6 `Assets/Scripts/MetaDisplayHello.cs`

Create an empty GameObject in the scene, name it `MetaDisplay` and add this script. The interface uses IMGUI so no Canvas is needed.

```csharp
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using UnityEngine;
#if UNITY_ANDROID
using UnityEngine.Android;
#endif

/// Hello World on the Meta Ray-Ban Display from Unity.
/// Works only on the Android phone: the native plugin does not run in the Editor.
public class MetaDisplayHello : MonoBehaviour
{
    const string BridgeClass = "com.example.metadisplay.MetaDisplayBridge";
    const string ListenerInterface = "com.example.metadisplay.MetaDisplayListener";
    const string BluetoothConnect = "android.permission.BLUETOOTH_CONNECT";

    readonly ConcurrentQueue<KeyValuePair<string, string>> events =
        new ConcurrentQueue<KeyValuePair<string, string>>();
    readonly List<string> log = new List<string>();

    AndroidJavaClass bridge;
    AndroidJavaObject activity;
    volatile bool permissionGranted;
    bool initialized, displayReady, updateRequired;
    string registration = "?", sessionState = "-", displayState = "-";
    int taps;

    // Receives the Kotlin events. Runs on an Android thread, NOT on the Unity main thread,
    // so it only queues them. Update() processes them.
    class Listener : AndroidJavaProxy
    {
        readonly ConcurrentQueue<KeyValuePair<string, string>> queue;

        public Listener(ConcurrentQueue<KeyValuePair<string, string>> q) : base(ListenerInterface)
        {
            queue = q;
        }

        public void onEvent(string type, string message) =>
            queue.Enqueue(new KeyValuePair<string, string>(type, message));
    }

    void Start()
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        activity = new AndroidJavaClass("com.unity3d.player.UnityPlayer")
            .GetStatic<AndroidJavaObject>("currentActivity");
        bridge = new AndroidJavaClass(BridgeClass);

        if (Permission.HasUserAuthorizedPermission(BluetoothConnect))
        {
            permissionGranted = true;
        }
        else
        {
            var callbacks = new PermissionCallbacks();
            callbacks.PermissionGranted += _ => permissionGranted = true;
            callbacks.PermissionDenied += _ =>
                events.Enqueue(new KeyValuePair<string, string>("error", "Bluetooth permission denied"));
            Permission.RequestUserPermission(BluetoothConnect, callbacks);
        }
#else
        AddLog("Run it on the Android phone: the native plugin does not work in the Editor.");
#endif
    }

    void Update()
    {
        if (permissionGranted && !initialized)
        {
            initialized = true;
            bridge.CallStatic("initialize", activity, new Listener(events));
            AddLog("DAT initialized");
        }

        while (events.TryDequeue(out var e)) HandleEvent(e.Key, e.Value);
    }

    void HandleEvent(string type, string message)
    {
        switch (type)
        {
            case "registration":
                registration = message;
                break;
            case "session":
                sessionState = message;
                if (message == "STOPPED") { displayReady = false; displayState = "-"; }
                break;
            case "display":
                displayState = message;
                displayReady = message == "STARTED";
                break;
            case "update_required":
                updateRequired = true;
                break;
            case "click":
                // Round trip: tap on the glasses -> Kotlin -> C# -> new screen on the glasses
                taps++;
                SendHello();
                break;
        }
        AddLog($"[{type}] {message}");
    }

    void SendHello() =>
        bridge.CallStatic("showHelloWorld", "Hello, World!", $"Sent from Unity - taps: {taps}");

    // Bonus: renders the Main Camera into a 400x400 texture and sends it as an image
    void SendUnitySnapshot()
    {
        const int size = 400; // smaller = faster over Bluetooth (the display is 600x600)
        var cam = Camera.main;
        var rt = RenderTexture.GetTemporary(size, size, 24,
            RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);

        var previousTarget = cam.targetTexture;
        cam.targetTexture = rt;
        cam.Render();
        cam.targetTexture = previousTarget;

        var previousActive = RenderTexture.active;
        RenderTexture.active = rt;
        var tex = new Texture2D(size, size, TextureFormat.RGB24, false);
        tex.ReadPixels(new Rect(0, 0, size, size), 0, 0);
        tex.Apply();
        RenderTexture.active = previousActive;
        RenderTexture.ReleaseTemporary(rt);

        byte[] png = tex.EncodeToPNG();
        Destroy(tex);

        // sbyte[] maps to Java byte[] without conversion ambiguity
        var data = new sbyte[png.Length];
        Buffer.BlockCopy(png, 0, data, 0, png.Length);
        bridge.CallStatic("showImage", data, "Rendered in Unity");
    }

    void OnGUI()
    {
        GUI.skin.label.fontSize = 32;
        GUI.skin.button.fontSize = 40;
        float h = Screen.height * 0.07f;

        GUILayout.BeginArea(new Rect(40, 100, Screen.width - 80, Screen.height - 140));
        GUILayout.Label($"Registration: {registration}\nSession: {sessionState}\nDisplay: {displayState}");

        if (GUILayout.Button("1. Register in Meta AI", GUILayout.Height(h)))
            bridge?.CallStatic("startRegistration", activity);
        if (GUILayout.Button("2. Connect to the Display", GUILayout.Height(h)))
            bridge?.CallStatic("connect");

        GUI.enabled = displayReady;
        if (GUILayout.Button("3. Send Hello World", GUILayout.Height(h))) SendHello();
        if (GUILayout.Button("Bonus: send a Unity frame", GUILayout.Height(h))) SendUnitySnapshot();
        GUI.enabled = true;

        if (GUILayout.Button("Disconnect", GUILayout.Height(h)))
            bridge?.CallStatic("disconnect");
        if (updateRequired && GUILayout.Button("Update the DAT app on the glasses", GUILayout.Height(h)))
            bridge?.CallStatic("openDatGlassesAppUpdate", activity);

        GUILayout.Space(20);
        foreach (var line in log) GUILayout.Label(line);
        GUILayout.EndArea();
    }

    void AddLog(string line)
    {
        log.Insert(0, line);
        if (log.Count > 12) log.RemoveAt(log.Count - 1);
    }

    void OnApplicationQuit() => bridge?.CallStatic("disconnect");
}
```

---

## 7. Step 3: build and test on the device

1. Turn on USB debugging on the phone and plug in the cable.
2. In Unity, open **File → Build Profiles → Android → Build And Run**.
3. On the phone:
   1. Accept the **Nearby devices (Bluetooth)** permission. The log should show `DAT initialized` and `[registration] AVAILABLE`.
   2. **1. Register in Meta AI**: Meta AI opens, you approve and the app comes back. The expected state is `REGISTERED`. In Dev Mode, the app appears under *Meta AI → App connections → Developer mode apps*.
   3. With the glasses **on your face and connected**, tap **2. Connect to the Display**. The expected sequence is `session: STARTING → STARTED` and then `display: STARTING → STARTED`.
   4. **3. Send Hello World**: the card appears on the glasses 🎉
   5. Tap **"Tap here"** on the glasses, with the touchpad or the Neural Band. The tap counter goes up and the screen is re-sent.
4. To see the logs on the PC:
   ```powershell
   adb logcat -s MetaDisplayBridge Unity
   ```

---

## 8. Display limitations (Unity or native)

- **600×600 screen.** Images larger than that bring nothing and make sending over Bluetooth slower.
- **Each `sendContent` replaces the whole screen.** There are no partial updates.
- Scrolling is **vertical only**.
- **The display dims after 20 s without interaction and sleeps at 25 s.** The session stays alive. When the display wakes up, you can re-send the content.
- **A two-finger tap on the glasses temple = Back.** On the root screen, this **ends the session**. Handle the `session: STOPPED` event in Unity.
- **Video:** MP4 by HTTPS URL only, up to **400 px per side and 70,000 px total**, one video at a time.
- **Images:** by HTTPS URL or `Bitmap`, which is what the snapshot bonus uses.
- Calls, notifications and other system events can interrupt your content.
- The device can refuse the display with `CAPABILITY_DENIED` even with the right versions. Treat it as an expected outcome.
- Design tip: on waveguide displays, **black is transparent**. Dark backgrounds "disappear" and bright colors show up.

---

## 9. Common problems

| Symptom | Likely cause and fix |
|---|---|
| `checkDebugAarMetadata`: *requires compileSdk 35* / *AGP 8.6.0* | Old Unity or Target API < 35. Use Unity 6.3 LTS (or 6000.0.45f1+) and Target API 35/36. |
| `2 files found with path 'lib/arm64-v8a/libc++_shared.so'` | `fbjni` (from the SDK) and IL2CPP both ship it. Add the `pickFirsts` from section 6.3 to `launcherTemplate.gradle`. |
| `Duplicate class kotlin.*` or `kotlinx.*` | Another Unity plugin ships an old Kotlin stdlib as a `.jar`. Remove the `.jar` or let Gradle resolve a single version. |
| `2 files found with path 'META-INF/...'` | In `launcherTemplate.gradle`, inside `android { }`, add `packaging { resources { excludes += ['/META-INF/{AL2.0,LGPL2.1}'] } }`. |
| `ClassNotFoundException: com.example.metadisplay...` | The `.aar` is not in `Plugins/Android` or R8 removed the class. Check the `consumer-rules.pro` inside the `.aar`. |
| `NoClassDefFoundError: com/meta/wearable/...` | The `implementation 'com.meta.wearable:...'` lines are missing from `mainTemplate.gradle`. |
| Registration does not return to the app | Check the `<intent-filter>` with `android:scheme` on the right activity (GameActivity or Activity). |
| `DAT_APP_ON_THE_GLASSES_UPDATE_REQUIRED` | Use the "Update the DAT app on the glasses" button (`Wearables.openDATGlassesAppUpdate`). |
| Firmware below V128 | `Wearables.openFirmwareUpdate(activity)` (already exposed in the bridge as `openFirmwareUpdate`). |
| `CAPABILITY_DENIED` | Dev Mode off, DAT app not installed on the glasses or the device refused. Reinstall DAT through Meta AI while wearing the glasses. |
| Session stuck at `STARTING` | The glasses are not on your face, not connected to Meta AI, or have no display. |

---

## 10. Testing without the glasses (optional)

The SDK has the **Mock Device Kit** (`com.meta.wearable:mwdat-mockdevice`). The `DisplayAccess` sample uses it for a *phone preview* and a *Chrome preview*:

- `MockDeviceKit.getInstance(context).enable()`
- `pairGlasses(GlassesModel.META_RAYBAN_DISPLAY)`, then `powerOn()` and `don()`
- `startTestServer(9000)`, then `adb forward tcp:9000 tcp:9000`, and open `http://127.0.0.1:9000/` in Chrome on the PC

Since SDK 1.0.0, the CHANGELOG also lists:

- **`MockDisplayKit`**: renders the Display session on the phone itself. `createPreviewView(context)` creates the preview view and `sendClick(identifier)` simulates taps.
- **`simulateRegistrationOutcome(Boolean)`**: simulates the Meta AI registration result, without the glasses.

To use this from Unity, add the `mwdat-mockdevice` dependency to the module and to `mainTemplate.gradle` and expose an `enableMock()` method in the bridge, copying the logic from the sample's `WearablesRepository.kt`. (The toolkit in this repository already does this through `MetaMockDevice`.)

Another sample in the repository, **BirdSpotter**, is a "real" app extended to the glasses. It uses display cards, inputs, IMU and speech, and runs entirely on MockDeviceKit. It is a good reference on how to organize a larger app.

---

## 11. Next steps

- **Inputs, voice and sensors:** DAT also offers navigation events, speech recognition, audio and IMU. Same pattern: expose them in the bridge and send events to C#.
- **Video:** create `VideoPlayer(VideoSource.Url(...), VideoCodec.MP4)`, send it as the root with `sendContent { video(player) }` and call `play()` when the send succeeds.
- **Registration started by Meta AI:** SDK 1.0+ lets Meta AI start the registration. For that, `Wearables.handleIntent(intent, ...)` must be called in the activity's `onCreate` and `onNewIntent`. In Unity, this requires a custom activity extending `UnityPlayerGameActivity`.
- **Publishing:** replace the `0`s with the real `APPLICATION_ID` and `CLIENT_TOKEN` from the Wearables Developer Center and set up *release channels*.

---

## Sources

- [Display – Android (DAT)](https://wearables.developer.meta.com/docs/develop/dat/display-android)
- [Display overview / requirements](https://wearables.developer.meta.com/docs/develop/dat/display-overview)
- [Android integration (manifest, registration, session)](https://wearables.developer.meta.com/docs/develop/dat/build-integration-android)
- [Setup / Developer Mode](https://wearables.developer.meta.com/docs/develop/dat/getting-started-toolkit/)
- [Version Dependencies](https://wearables.developer.meta.com/docs/develop/dat/version-dependencies)
- [DAT overview](https://wearables.developer.meta.com/docs/develop/dat/build-overview)
- [Camera streaming (resolution, fps, Bluetooth Classic)](https://wearables.developer.meta.com/docs/develop/dat/streaming)
- [Camera frame processing (HEVC, stabilization)](https://wearables.developer.meta.com/docs/develop/dat/camera-stream-processing)
- [Microphones and speakers (HFP/A2DP)](https://wearables.developer.meta.com/docs/develop/dat/microphones-and-speakers)
- [Motion (IMU)](https://wearables.developer.meta.com/docs/develop/dat/motion)
- [Web Apps: sensors and location](https://wearables.developer.meta.com/docs/develop/webapps/build/sensors-and-location/)
- [Android API reference](https://wearables.developer.meta.com/docs/reference/android/dat/latest)
- [Official repository + DisplayAccess sample](https://github.com/meta/meta-wearables-dat-android/tree/main/samples/DisplayAccess)
- [Maven Central: com.meta.wearable](https://central.sonatype.com/artifact/com.meta.wearable/mwdat-core/versions)
- [Unity 6.3: Gradle/AGP compatibility](https://docs.unity.com/en-us/engine/6000.3/manual/platform-specific/android/introducing/gradle-overview/version-compatibility)
- [Unity 6.0: Gradle/AGP compatibility](https://docs.unity3d.com/6000.0/Documentation/Manual/android-gradle-version-compatibility.html)
