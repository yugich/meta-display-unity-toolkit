# Scripts de validação

Usados para testar o pacote num **projeto Unity separado** (não coloque dentro de `Assets/MetaDisplayToolkit`).

1. Crie um projeto Unity 6, adicione `com.unity.ugui` e copie `Assets/MetaDisplayToolkit` para ele.
2. Copie `Editor/` e `Tests/` desta pasta para o `Assets/` do projeto de teste.
3. Rode em batchmode (`$U` = caminho do `Unity.exe`):

```powershell
# Cena de exemplo + JSON de cada tela no log
& $U -batchmode -projectPath <proj> -buildTarget Android -executeMethod MDTestRunner.Demo -quit -logFile demo.log

# Teste PlayMode do fluxo completo (simulação no Editor)
& $U -batchmode -projectPath <proj> -buildTarget Android -runTests -testPlatform PlayMode -testResults results.xml -logFile test.log

# APK IL2CPP/ARM64 da cena de exemplo (saída em ../build/MetaDisplayTest.apk)
& $U -batchmode -projectPath <proj> -buildTarget Android -executeMethod MDTestRunner.BuildApk -logFile build.log

# .unitypackage
$env:MD_EXPORT_PATH = "C:\caminho\MetaDisplayToolkit.unitypackage"
& $U -batchmode -projectPath <proj> -buildTarget Android -executeMethod MDTestRunner.Export -quit -logFile export.log
```
