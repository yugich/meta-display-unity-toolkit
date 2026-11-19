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

# IL2CPP/ARM64 APK of the sample scene (output in ../build/MetaDisplayDemo.apk)
& $U -batchmode -projectPath <proj> -buildTarget Android -executeMethod MDTestRunner.BuildApk -logFile build.log

# .unitypackage
$env:MD_EXPORT_PATH = "C:\path\MetaDisplayToolkit.unitypackage"
& $U -batchmode -projectPath <proj> -buildTarget Android -executeMethod MDTestRunner.Export -quit -logFile export.log
```
