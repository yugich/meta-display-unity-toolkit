# MetaDisplayToolkit — código-fonte

| Pasta / arquivo | O que é |
|---|---|
| `MetaDisplayToolkit.unitypackage` (Releases) | **O arquivo para importar no Unity** (Assets → Import Package → Custom Package…). |
| `MetaDisplayDemo.apk` (Releases) | APK da cena de exemplo, pronto para testar nos óculos: `adb install -r MetaDisplayDemo.apk` |
| `Assets/MetaDisplayToolkit/` | Conteúdo do pacote (C#, editor, shader, `.aar`). Leia o `LEIAME.md` dentro dele. |
| `android-bridge/` | Projeto Gradle da ponte Kotlin → gera `MetaDisplayBridge.aar`. |
| `tools/unity-tests/` | Scripts de validação (cena de exemplo, teste PlayMode, build do APK, exportação do pacote). |

## Recompilar a ponte Android

Requer JDK 17+ (o do Android Studio serve) e Android SDK 36.

```powershell
cd android-bridge
$env:JAVA_HOME = "C:\Program Files\Android\Android Studio\jbr"
.\gradlew.bat :bridge:assembleRelease
copy bridge\build\outputs\aar\bridge-release.aar ..\Assets\MetaDisplayToolkit\Plugins\Android\MetaDisplayBridge.aar
```

A ponte é compilada contra `com.meta.wearable:mwdat-*:1.0.1` com `compileOnly`. As dependências reais entram no
APK pelo `MetaDisplayGradlePostProcessor` (Editor), conforme `ProjectSettings/MetaDisplaySettings.json`.

## Arquitetura

```
C# (MetaWearables, MetaGlassesDisplay, MetaCamera, ...)
   │  AndroidJavaClass.CallStatic  ─────────────►  Kotlin (MDCore, MDDisplay, MDCamera, MDMotion, MDInputs, MDSpeech, MDVoice, MDMock)
   │                                                    │ coroutines na main thread do Android
   │  ◄──── MetaDisplayListener.onEvent(tipo, JSON) ────┘ (AndroidJavaProxy → fila → main thread do Unity)
   │  ◄──── polling: frames YUV (ByteBuffer direto), IMU (double[]), áudio PCM (byte[])
   ▼
UI dos óculos: GlassesNode → JSON → LayoutBuilder.kt → DSL do mwdat-display (flexBox/text/image/button/icon/buttonGroup)
```

## O que foi validado

| Teste | Resultado |
|---|---|
| Ponte Kotlin compilada contra `mwdat-*:1.0.1` (AGP 8.11.1, Kotlin 2.2.21) | ✅ |
| Pacote compila no Unity 6000.6.4f1 **sem** `com.unity.ugui` (recursos de Canvas desligados) | ✅ |
| `.unitypackage` importado em projeto limpo: Unity **6000.6.4f1** e **6000.3.19f1 (6.3 LTS)** | ✅ sem erros |
| Menu "Criar cena de exemplo" → 3 telas convertidas em JSON correto | ✅ |
| Teste PlayMode (simulação): registro → sessão → display → navegação → contador → fala → desconexão | ✅ |
| Build APK IL2CPP/ARM64 (Unity 6.6, AGP 9.1.1): dependências do Maven Central, manifest, `libc++_shared` duplicada resolvida | ✅ 38 MB |
| Funcionamento com os óculos físicos | ⏳ depende do seu hardware |
