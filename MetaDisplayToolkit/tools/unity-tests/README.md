# Validation scripts

Used to test the package in a **separate Unity project** (do not put them inside `Assets/MetaDisplayToolkit`).

1. Create a Unity 6 project, add `com.unity.ugui` and copy `Assets/MetaDisplayToolkit` into it.
2. Copy `Editor/` and `Tests/` from this folder into the test project's `Assets/`.
3. Run in batchmode (`$U` = path to `Unity.exe`):

```powershell
# Sample scene + JSON of each screen in the log
& $U -batchmode -projectPath <proj> -buildTarget Android -executeMethod MDTestRunner.Demo -quit -logFile demo.log

# PlayMode test of the full flow (Editor simulation)
& $U -batchmode -projectPath <proj> -buildTarget Android -runTests -testPlatform PlayMode -testResults results.xml -logFile test.log

# Avatar sample scene (3D) + Web App export in ../build/WebApp
& $U -batchmode -projectPath <proj> -buildTarget Android -executeMethod MDTestRunner.Avatar -quit -logFile avatar.log

# PNG/JPG frames of the avatar as the glasses receive them, in ../build/snapshots
& $U -batchmode -projectPath <proj> -buildTarget Android -executeMethod MDTestRunner.Snapshot -quit -logFile snapshot.log

# IL2CPP/ARM64 APK of the sample scene (output in ../build/MetaDisplayDemo.apk)
& $U -batchmode -projectPath <proj> -buildTarget Android -executeMethod MDTestRunner.BuildApk -logFile build.log

# IL2CPP/ARM64 APK of the avatar sample (output in ../build/MetaDisplayAvatarDemo.apk)
& $U -batchmode -projectPath <proj> -buildTarget Android -executeMethod MDTestRunner.BuildAvatarApk -logFile build-avatar.log

# .unitypackage
$env:MD_EXPORT_PATH = "C:\path\MetaDisplayToolkit.unitypackage"
& $U -batchmode -projectPath <proj> -buildTarget Android -executeMethod MDTestRunner.Export -quit -logFile export.log
```

Run `MDTestRunner.Avatar` (and `MDTestRunner.Demo`) before the PlayMode tests: they create the scenes the tests load.

## Relay check (Web App link)

`relay-check/` holds two Node.js scripts (Node 22+, no dependencies) used with the relay exported by
**Meta Display > 3D > Export Web App** (`relay/server.js`, needs `npm install`):

```powershell
cd <exported web app>\relay; npm install; $env:PORT = 8099; node server.js       # terminal 1
node relay-check\test.mjs ws://localhost:8099                                     # forwarding between rooms
node relay-check\fake-glasses.mjs ws://localhost:8099 unitytest                   # terminal 2: pretends to be the Web App
$env:MD_RELAY_URL = "ws://localhost:8099"                                         # then run the PlayMode tests:
                                                                                  # WebAppLink_TalksThroughTheRelay runs only with it
```
