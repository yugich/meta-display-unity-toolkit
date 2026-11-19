# Meta Display Toolkit for Unity

Toolkit for building **Unity (Android)** apps for the **Meta Ray-Ban Display** with the
**Meta Wearables Device Access Toolkit (DAT) 1.0.1**, with the glasses UI drawn on the Unity **Canvas**.

## Downloads (Releases)

On this repository's **Releases** page:

| File | Purpose |
|---|---|
| `MetaDisplayToolkit.unitypackage` | Import into Unity (Assets → Import Package → Custom Package…) |
| `MetaDisplayDemo.apk` | Sample scene ready to test on the glasses: `adb install -r MetaDisplayDemo.apk` |
| `MetaDisplayAvatarDemo.apk` | 3D avatar sample: compare the four ways to show a 3D object on the glasses |
| `GlassesAvatarWebApp.zip` | The Three.js Web App of the avatar sample (host it on HTTPS) and its relay |

## What's here

| Path | Contents |
|---|---|
| [`MetaDisplayToolkit/Assets/MetaDisplayToolkit/`](MetaDisplayToolkit/Assets/MetaDisplayToolkit/) | Unity package source (C#, Editor, shader and the `.aar` bridge). Manual: [README.md](MetaDisplayToolkit/Assets/MetaDisplayToolkit/README.md) |
| [`MetaDisplayToolkit/android-bridge/`](MetaDisplayToolkit/android-bridge/) | Gradle project of the Kotlin bridge (Unity ↔ Meta SDK) |
| [`MetaDisplayToolkit/tools/unity-tests/`](MetaDisplayToolkit/tools/unity-tests/) | Scripts used to validate the package (APK builds, export, PlayMode tests, relay check) |
| [`Unity-MetaRayBanDisplay-HelloWorld.md`](Unity-MetaRayBanDisplay-HelloWorld.md) | Study guide: how Unity, Android and the glasses display fit together |

Architecture details, how to rebuild the bridge and what was validated: [MetaDisplayToolkit/README.md](MetaDisplayToolkit/README.md).

## Features

- Meta AI registration, devices, permissions, session and update screens
- **Display**: screens, clicks, images, 116 native icons and video, built in code (`Glasses.*`) or **on the Canvas** (`GlassesScreen`)
- Choose what appears **on the phone** and what appears **on the glasses** (mirror, thumbnail or glasses only)
- **3D objects and avatars** on the glasses in four ways: live frames, baked frames, MP4 clips or a Three.js Web App
  rendered on the glasses and driven from Unity, with the same API and live metrics to compare them
- Camera (video → `RenderTexture`, photos, audio), IMU of the glasses and the Neural Band, inputs, speech and "Hey Meta"
- Simulated glasses (MockDeviceKit) and Editor simulation
- Gradle and manifest configured automatically at build time

## Requirements

Unity 6.0.45+ or 6.3 LTS+ with Android Build Support · Meta Ray-Ban Display (firmware V128+) · Meta AI app V292+
with Developer Mode and the Device Access Toolkit installed on the glasses.

Use of the Meta SDK is subject to the [Meta Wearables Developer Terms](https://wearables.developer.meta.com/terms).
