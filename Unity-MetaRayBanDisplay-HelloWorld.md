# Unity → Android → Meta Ray-Ban Display: Hello World

> Guia de estudo baseado na documentação oficial do **Meta Wearables Device Access Toolkit (DAT) v1.0.1**
> (consultada em 19/11/2026). Fonte principal: <https://wearables.developer.meta.com/docs/develop/dat/display-android>

> **Atalho:** a pasta [MetaDisplayToolkit/](MetaDisplayToolkit/) tem o código de um pacote pronto
> (`MetaDisplayToolkit.unitypackage`, nas Releases do repositório) com tudo deste guia e mais: todas as capacidades do SDK (display, câmera, IMU,
> inputs, fala, "Hey Meta", óculos simulados), UI dos óculos desenhada no **Canvas** e configuração automática do
> Gradle/manifest. Veja o [LEIAME](MetaDisplayToolkit/Assets/MetaDisplayToolkit/LEIAME.md). Este guia continua útil
> para entender o que acontece por baixo.

---

## 1. Resposta curta: dá para usar Unity?

**Sim, mas só como "app do celular".** O Unity compila o APK Android e roda a lógica em C#. Para falar com os óculos você precisa de uma **ponte nativa em Kotlin** (um `.aar`) que chama o SDK oficial da Meta.

O que é preciso entender antes de começar:

| Ponto | Situação |
|---|---|
| SDK oficial para Unity | **Não existe.** O DAT só tem SDK nativo Android (Kotlin) e iOS (Swift). Os pacotes Meta XR/OpenXR para Unity são para Quest e não servem para o Ray-Ban Display. |
| Onde o app roda | **No celular.** O app manda a interface pronta para os óculos por Bluetooth, via app Meta AI. Os óculos não guardam estado do app. |
| O que aparece nos óculos | **UI declarativa:** `flexBox`, `text`, `image`, `button`, `icon` e `video` (MP4 por URL). Tela de **600×600**. |
| A cena 3D do Unity aparece nos óculos? | **Não diretamente.** Não há API para mandar pixels ou frames em tempo real. Dá para enviar **snapshots** (renderizar para textura → PNG → `image(bitmap)`), mas com latência de Bluetooth. Não serve para jogo em tempo real. |
| Input dos óculos | Toques no touchpad ou Neural Band chegam ao celular como callbacks `onClick`. |

**Quando Unity faz sentido:** você já tem um app ou jogo Unity no celular e quer um HUD ou "segunda tela" nos óculos: placar, notificações, instruções, snapshots.
**Quando não faz:** se o app existe só para os óculos, o caminho nativo (Kotlin no Android Studio, usando o sample `DisplayAccess`) é mais simples. Se você quer algo que rode *dentro* dos óculos sem app no celular, existem os **Web Apps** (HTML/CSS/JS servidos por HTTPS no navegador Chromium dos óculos). A documentação não cobre build Web do Unity nesse navegador.

---

## 2. Arquitetura do Hello World

```
┌──────────────────────── Celular Android ────────────────────────┐
│                                                                 │
│  Unity (C#)  MetaDisplayHello.cs                                │
│     │  AndroidJavaClass.CallStatic(...)        ▲                │
│     ▼                                          │ AndroidJavaProxy│
│  metadisplay.aar  (Kotlin: MetaDisplayBridge)  │ (eventos)       │
│     │                                                           │
│     ▼                                                           │
│  com.meta.wearable:mwdat-core + mwdat-display 1.0.1             │
│     │                                                           │
│     ▼                                                           │
│  App Meta AI (registro, Developer Mode, conexão)                │
└─────┼───────────────────────────────────────────────────────────┘
      │ Bluetooth
      ▼
 Meta Ray-Ban Display (app DAT instalado nos óculos renderiza a UI)
```

Fluxo do SDK, que a ponte encapsula:

1. `Wearables.initialize(context)`: uma vez por processo.
2. `Wearables.startRegistration(activity)`: abre o Meta AI, o usuário aprova e o estado vai para `REGISTERED`.
3. `Wearables.createSession(AutoDeviceSelector(filter = { it.isDisplayCapable() }))` e depois `session.start()`.
4. Quando `session.state == STARTED`, chama `session.addDisplay()`.
5. Quando `display.state == STARTED`, chama `display.sendContent { ... }`. **Cada chamada substitui a tela inteira.**
6. Para encerrar: `session.removeDisplay()` e depois `session.stop()`.

### 2.1 Como os dados trafegam: app no celular (DAT) vs Web App

São duas plataformas diferentes, e a escolha depende de **onde o seu código roda**:

```
 DAT (app no celular)                          Web App
 ────────────────────                          ───────
 ┌──────────── Óculos ────────────┐            ┌──────────── Óculos ────────────┐
 │ câmera · mic · IMU · display   │            │ navegador Chromium             │
 │ (capturam e pré-processam)     │            │   └─ seu HTML/CSS/JS roda AQUI │
 └───────────────┬────────────────┘            │ IMU · display · Neural Band    │
                 │ Bluetooth                   └───────────────┬────────────────┘
                 │ (vídeo, áudio, IMU ↑ / UI ↓)                │ só localização
                 ▼                                             ▼ (óculos não têm GPS)
 ┌──────────── Celular ───────────┐            ┌──────────── Celular ───────────┐
 │ Meta AI (registro/permissões)  │            │ Meta AI (pareamento)           │
 │ SEU APP ← o código roda AQUI   │            └────────────────────────────────┘
 └────────────────────────────────┘
```

#### No DAT, os óculos são um periférico sem fio

Os óculos capturam os dados, fazem um pré-processamento e mandam tudo para o celular por **Bluetooth**. Registro e permissões passam pelo app Meta AI.

| Recurso | Como chega ao celular |
|---|---|
| **Câmera** | Os óculos cortam a imagem para 9:16, corrigem a distorção da lente, estabilizam e comprimem em **HEVC (H.265)**. O vídeo vai por **Bluetooth Classic**, com qualidade LOW 360×640, MEDIUM 504×896 ou HIGH 720×1280 e fps de 2, 7, 15, 24 ou 30. Se a banda cair, os óculos baixam a resolução automaticamente e depois o fps, mas **nunca abaixo de 15**. Pedir menos resolução costuma dar uma imagem *melhor*, porque ela sai menos comprimida. |
| **Microfone** | Pelo perfil **HFP** do Bluetooth, mono, 16 ou 8 kHz, com beamforming que isola a voz de quem usa (o som ambiente fica baixo). Em beta, o áudio também pode vir junto com o vídeo da câmera, em PCM de 16 bits e sem beamforming. A permissão é pedida pelo diálogo normal do Android. |
| **Alto-falante** | Pelo perfil **A2DP** (estéreo de 44,1 ou 48 kHz), como um fone Bluetooth comum. |
| **Movimento (IMU)** | Amostras `MotionSample` com acelerômetro, giroscópio, magnetômetro e orientação (quaternion), com a origem indicada: **óculos ou Neural Band**. Ainda em beta, só nos canais de desenvolvimento e beta. |
| **Display** | O caminho inverso: a interface montada no celular é serializada e vai pelo Bluetooth até os óculos. Por isso layouts pesados e imagens grandes deixam a tela lenta. |

Todo o processamento pesado, como IA ou visão computacional, roda **no celular ou na sua nuvem**, e não nos óculos. A documentação também diz que a Meta não acessa as fotos, vídeos e áudios que vão direto dos óculos para o app de terceiros.

#### No Web App, o código roda dentro dos óculos

O Web App é servido por HTTPS e roda no navegador Chromium dos próprios óculos.

| Tem acesso | Não tem acesso |
|---|---|
| Movimento e orientação dos óculos (`DeviceMotionEvent` e `DeviceOrientationEvent`, com permissão) | **Câmera** |
| Orientação do pulso pela Neural Band (`RelativeOrientationSensor`) | **Áudio bruto do microfone.** A voz só chega como texto ditado num campo, ou por chamadas do Meta AI a ferramentas da página (WebMCP, que vem desligado por padrão). |
| Localização (`navigator.geolocation`), mas a posição **vem do celular pareado** | |
| Bateria, síntese de voz (TTS), `localStorage` | |

#### Por que essa divisão?

A documentação não explica o motivo. Meu palpite é privacidade e hardware. Câmera e microfone são os recursos mais sensíveis, e no DAT eles passam pelo fluxo de permissão do Meta AI. Além disso, os óculos têm bateria e processamento limitados, então faz sentido mandar o trabalho pesado para o celular.

#### O que isso significa para quem usa Unity

Com a ponte Kotlin, o Unity no celular pode **receber** o vídeo da câmera e os dados de movimento e processar tudo em C#, inclusive com modelos de ML no Unity (Inference Engine, antigo Sentis). Depois ele **devolve** o resultado para o display. O caminho completo fica assim:

```
câmera/IMU dos óculos ──BT──▶ Kotlin (mwdat-camera / mwdat-motion) ──JNI──▶ C# (processa)
                                                                              │
display dos óculos ◀──BT── Kotlin (mwdat-display, sendContent) ◀──JNI─────────┘
```

O limite em todos esses caminhos é a **banda do Bluetooth**, então pense em resultados resumidos (texto, ícones, imagens pequenas) e não em espelhar frames.

---

## 3. Pré-requisitos

### Hardware e apps

- **Meta Ray-Ban Display** com firmware **V128 ou superior**. Para conferir: Meta AI → aba Devices → ⚙️ → General → About → Version.
- **App Meta AI (Android) V292 ou superior.** É a versão mínima do SDK 1.0.1, segundo a página [Version Dependencies](https://wearables.developer.meta.com/docs/develop/dat/version-dependencies).
- Celular **Android 10 ou superior**. O SDK declara minSdk 29, mas este guia usa **minSdk 31 (Android 12)**, como o sample oficial.
- **Developer Mode** ✅ (já ativado). Para ativar: Meta AI → Settings → App Info → tocar 5× em *App version*.
- **App DAT instalado nos óculos.** Sem ele, o display não funciona:
  1. Coloque os óculos no rosto. É obrigatório.
  2. No Meta AI, com o Dev Mode ligado, toque em **Install** para instalar o Device Access Toolkit nos óculos.
  3. Mantenha o Meta AI aberto durante a instalação, que leva de 5 a 10 segundos.

### Ferramentas no PC

- **Android Studio** recente, com **JDK 17**. Ele serve para compilar a ponte `.aar`.
- **Unity 6.3 LTS (6000.3.x)** com o módulo **Android Build Support** (OpenJDK, Android SDK e NDK).
  - Unity 6.0 também funciona a partir da **6000.0.45f1**.
  - ⚠️ **Não use Unity 2022.3 nem versões anteriores.** O `mwdat-core` puxa `androidx.core:1.16.0`, que exige **AGP ≥ 8.6.0** e **compileSdk ≥ 35**. O Unity 6.3 já traz AGP 8.10, 9.0 ou 9.1, conforme o patch.

### Estrutura de pastas sugerida

```
Meta-display/
├─ meta-wearables-dat-android/          (clone do repo oficial)
│  └─ samples/DisplayAccess/            (projeto Gradle; vamos adicionar o módulo da ponte aqui)
│     └─ metadisplay/                   (Android Library → gera metadisplay-release.aar)
└─ unity-hello/                         (projeto Unity 6.3)
   └─ Assets/
      ├─ Plugins/Android/
      │  ├─ metadisplay-release.aar
      │  ├─ AndroidManifest.xml         (Custom Main Manifest)
      │  ├─ mainTemplate.gradle         (Custom Main Gradle Template)
      │  └─ gradleTemplate.properties   (Custom Gradle Properties Template)
      └─ Scripts/MetaDisplayHello.cs
```

---

## 4. Passo 0: validar o hardware com o sample oficial (sem Unity)

Faça isso antes de mexer no Unity. Se o sample nativo não funcionar, o Unity também não vai funcionar, e assim você separa problema de hardware ou configuração de problema de integração.

```powershell
cd E:\Projetos\Estudos\Meta-display
git clone https://github.com/meta/meta-wearables-dat-android.git
```

1. Abra `meta-wearables-dat-android/samples/DisplayAccess` no Android Studio.
2. Em `local.properties`, adicione as credenciais. Em Developer Mode, a documentação manda usar `0`:
   ```properties
   mwdat_application_id=0
   mwdat_client_token=0
   ```
3. Rode no celular, registre o app no Meta AI, conecte e abra um dos exemplos. O conteúdo deve aparecer nos óculos.

Esse sample usa **AGP 8.11.1, Kotlin 2.2.21, compileSdk 36, minSdk 31 e Java 17**. Por isso ele também é uma ótima base para compilar a ponte.

---

## 5. Passo 1: a ponte Kotlin (`metadisplay.aar`)

### 5.1 Criar o módulo

No Android Studio, com o projeto `DisplayAccess` aberto:

1. **File → New → New Module… → Android Library**
   - Module name: `metadisplay`
   - Package name: `com.estudos.metadisplay`
   - Language: Kotlin · Minimum SDK: API 31
2. Confira se o assistente fez estas três coisas. Se não fez, faça à mão:
   - `settings.gradle.kts` tem `include(":metadisplay")`.
   - `gradle/libs.versions.toml`, em `[plugins]`, tem:
     ```toml
     android-library = { id = "com.android.library", version.ref = "agp" }
     ```
   - O `build.gradle.kts` da raiz, dentro de `plugins { }`, tem:
     ```kotlin
     alias(libs.plugins.android.library) apply false
     ```

> Se o assistente criar aliases com nomes diferentes, use os nomes que ele criou. O importante é que o plugin de library use a **mesma versão de AGP** do sample e o **Kotlin seja 2.2.x**.

### 5.2 `metadisplay/build.gradle.kts`

Substitua todo o conteúdo do arquivo por:

```kotlin
import org.jetbrains.kotlin.gradle.dsl.JvmTarget

plugins {
  alias(libs.plugins.android.library)
  alias(libs.plugins.jetbrains.kotlin.android)
}

android {
  namespace = "com.estudos.metadisplay"
  compileSdk = 36

  defaultConfig {
    minSdk = 31
    // Vai dentro do .aar: impede que o R8 do Unity remova a ponte (chamada via JNI/reflexão)
    consumerProguardFiles("consumer-rules.pro")
  }

  compileOptions {
    sourceCompatibility = JavaVersion.VERSION_17
    targetCompatibility = JavaVersion.VERSION_17
  }
}

kotlin { compilerOptions { jvmTarget = JvmTarget.JVM_17 } }

dependencies {
  // Usados só para compilar. O Unity vai baixar essas libs de novo (ver mainTemplate.gradle),
  // porque um .aar local NÃO leva as dependências transitivas junto.
  implementation(libs.mwdat.core)
  implementation(libs.mwdat.display)
}
```

### 5.3 `metadisplay/consumer-rules.pro`

```proguard
-keep class com.estudos.metadisplay.** { *; }
```

### 5.4 `metadisplay/src/main/java/com/estudos/metadisplay/MetaDisplayBridge.kt`

```kotlin
package com.estudos.metadisplay

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

/** Implementada no C# via AndroidJavaProxy. Recebe todos os eventos da ponte. */
interface MetaDisplayListener {
  fun onEvent(type: String, message: String)
}

/**
 * Ponte Unity -> DAT SDK.
 * C#: new AndroidJavaClass("com.estudos.metadisplay.MetaDisplayBridge").CallStatic("metodo", ...)
 * Todo o trabalho roda em Dispatchers.Main (thread principal do Android), então não há corrida.
 */
object MetaDisplayBridge {
  private const val TAG = "MetaDisplayBridge"

  private val crashGuard = CoroutineExceptionHandler { _, t ->
    Log.e(TAG, "Erro inesperado", t)
    emit("error", t.message ?: t.toString())
  }
  private val scope = CoroutineScope(SupervisorJob() + Dispatchers.Main + crashGuard)

  private lateinit var appContext: Context
  private var listener: MetaDisplayListener? = null
  private var initialized = false
  private var session: DeviceSession? = null
  private var display: Display? = null
  private val sessionJobs = mutableListOf<Job>()

  // ───────────── API chamada pelo Unity ─────────────

  /** Chame depois que a permissão BLUETOOTH_CONNECT for concedida. */
  @JvmStatic
  fun initialize(activity: Activity, callback: MetaDisplayListener) {
    listener = callback
    scope.launch {
      if (initialized) {
        emit("registration", "${Wearables.registrationState.value}")
        return@launch
      }
      appContext = activity.applicationContext
      // A única falha possível aqui é ALREADY_INITIALIZED, que é inofensiva
      Wearables.initialize(appContext).onFailure { error, _ -> emit("info", error.description) }
      initialized = true

      launch { Wearables.registrationState.collect { emit("registration", "$it") } }
      launch {
        Wearables.registrationErrorStream.collect {
          emit("error", "Registro: ${it.getLocalizedDescription(appContext)}")
        }
      }
      launch { Wearables.devices.collect { emit("devices", "${it.size} dispositivo(s)") } }
    }
  }

  /** Abre o Meta AI para o usuário aprovar o app (em Dev Mode aparece em "Developer mode apps"). */
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

  /** Cria a sessão com os primeiros óculos que tenham display e anexa o Display. */
  @JvmStatic
  fun connect() {
    scope.launch {
      if (Wearables.registrationState.value != RegistrationState.REGISTERED) {
        emit("error", "Registre o app no Meta AI antes de conectar")
        return@launch
      }
      if (session != null) {
        emit("info", "Sessão já está ativa")
        return@launch
      }
      Wearables.createSession(AutoDeviceSelector(filter = { it.isDisplayCapable() })).fold(
          onSuccess = { newSession -> observeAndStart(newSession) },
          onFailure = { error, _ -> reportSessionError(error) },
      )
    }
  }

  /** O Hello World: um card com título e texto, mais um botão que avisa o Unity quando é tocado. */
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
              label = "Toque aqui",
              style = ButtonStyle.PRIMARY,
              iconName = IconName.CHECKMARK,
              // SDK 1.0+: a primeira ação PRIMARY já recebe o foco quando a tela aparece
              actionRole = ActionRole.PRIMARY,
              onClick = { emit("click", "hello") },
          )
        }
      }.fold(
          onSuccess = { emit("sent", "Hello World enviado") },
          onFailure = { error, _ -> emit("error", "sendContent: ${error.description}") },
      )
    }
  }

  /** Bônus: mostra um PNG gerado pelo Unity (ex.: snapshot de uma câmera). */
  @JvmStatic
  fun showImage(png: ByteArray, caption: String) {
    scope.launch {
      val d = readyDisplay() ?: return@launch
      val bitmap =
          withContext(Dispatchers.Default) { BitmapFactory.decodeByteArray(png, 0, png.size) }
      if (bitmap == null) {
        emit("error", "PNG inválido")
        return@launch
      }
      d.sendContent {
        flexBox(direction = Direction.COLUMN, gap = 8, padding = 16) {
          image(bitmap = bitmap, sizePreset = ImageSize.FILL, cornerRadius = CornerRadius.MEDIUM)
          text(caption, style = TextStyle.META, color = TextColor.SECONDARY)
        }
      }.fold(
          onSuccess = { emit("sent", "Imagem enviada") },
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

  // ───────────── internos ─────────────

  private fun observeAndStart(newSession: DeviceSession) {
    session = newSession
    sessionJobs += scope.launch { newSession.errors.collect { reportSessionError(it) } }
    sessionJobs +=
        scope.launch {
          newSession.state.collect { state ->
            emit("session", "$state")
            when (state) {
              // Só anexe o Display depois que a sessão estiver STARTED
              DeviceSessionState.STARTED -> if (display == null) attachDisplay(newSession)
              // STOPPED é terminal: para reconectar é preciso criar outra sessão
              DeviceSessionState.STOPPED -> clearSession()
              else -> Unit
            }
          }
        }
    newSession.start() // retorna na hora; o progresso chega por state/errors
  }

  private fun attachDisplay(s: DeviceSession) {
    s.addDisplay().fold(
        onSuccess = { newDisplay ->
          display = newDisplay
          sessionJobs +=
              scope.launch {
                newDisplay.state.collect { state ->
                  emit("display", "$state")
                  if (state == DisplayState.STARTED) emit("ready", "Display pronto")
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
      emit("error", "Sessão: ${error.description}")
    }
  }

  private fun readyDisplay(): Display? {
    val d = display
    if (d == null || d.state.value != DisplayState.STARTED) {
      emit("error", "Display ainda não está pronto (conecte primeiro)")
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

### 5.5 Gerar o `.aar`

```powershell
cd E:\Projetos\Estudos\Meta-display\meta-wearables-dat-android\samples\DisplayAccess
.\gradlew.bat :metadisplay:assembleRelease
```

Saída: `metadisplay\build\outputs\aar\metadisplay-release.aar`

---

## 6. Passo 2: projeto Unity

### 6.1 Criar e configurar

1. No Unity Hub, crie um projeto **Unity 6.3 LTS** (template *Universal 3D* ou *2D*) chamado `unity-hello`.
2. **File → Build Profiles → Android → Switch Platform**.
3. Abra **Edit → Project Settings → Player → aba Android → Other Settings**:
   - **Package Name:** `com.estudos.metadisplayhello`
   - **Minimum API Level:** Android 12 (API 31)
   - **Target API Level:** **35 ou 36.** Precisa ser ≥ 35, porque o Unity usa esse valor como `compileSdk`. Se o Unity disser que falta a plataforma, deixe-o atualizar o SDK ou aponte para o SDK do Android Studio em *Preferences → External Tools*.
   - **Scripting Backend:** IL2CPP · **Target Architectures:** ARM64
   - **Application Entry Point:** GameActivity (o padrão) ou Activity. Os dois funcionam.
4. Em **Publishing Settings → Build**, marque:
   - ☑ Custom Main Manifest
   - ☑ Custom Main Gradle Template
   - ☑ Custom Gradle Properties Template

   O Unity cria esses arquivos em `Assets/Plugins/Android/`.
5. Copie `metadisplay-release.aar` para `Assets/Plugins/Android/`. No Inspector do arquivo, confirme que a plataforma **Android** está marcada.

### 6.2 `Assets/Plugins/Android/mainTemplate.gradle`

Mantenha tudo o que o Unity gerou e adicione só as duas linhas do DAT no bloco `dependencies`, **antes** de `**DEPS**`:

```groovy
dependencies {
    implementation fileTree(dir: 'libs', include: ['*.jar'])
    // Meta Wearables Device Access Toolkit (o .aar da ponte não traz as dependências transitivas)
    implementation 'com.meta.wearable:mwdat-core:1.0.1'
    implementation 'com.meta.wearable:mwdat-display:1.0.1'
**DEPS**}
```

O Gradle do Unity já declara `google()` e `mavenCentral()`, e o DAT é publicado no Maven Central. As dependências transitivas (Kotlin stdlib 2.2, coroutines, AndroidX, OkHttp, protobuf etc.) vêm automaticamente.

> Alternativa: se você já usa o **External Dependency Manager (EDM4U)**, declare os dois artefatos num `Dependencies.xml` em vez de editar o template.

### 6.3 `Assets/Plugins/Android/gradleTemplate.properties`

Confirme que esta linha existe. Se não existir, adicione:

```properties
android.useAndroidX=true
```

### 6.4 `Assets/Plugins/Android/AndroidManifest.xml`

Parta do arquivo gerado pelo Unity e acrescente os blocos marcados com `DAT`. O exemplo abaixo usa a GameActivity. Se você escolheu *Activity*, coloque o segundo `<intent-filter>` na `com.unity3d.player.UnityPlayerActivity`.

```xml
<?xml version="1.0" encoding="utf-8"?>
<manifest xmlns:android="http://schemas.android.com/apk/res/android"
    xmlns:tools="http://schemas.android.com/tools">

    <!-- DAT: permissões de Bluetooth e Internet -->
    <uses-permission android:name="android.permission.BLUETOOTH" />
    <uses-permission android:name="android.permission.BLUETOOTH_CONNECT" />
    <uses-permission android:name="android.permission.INTERNET" />

    <application>
        <!-- DAT: em Developer Mode use 0. Em produção, use os valores do Wearables Developer Center -->
        <meta-data android:name="com.meta.wearable.mwdat.APPLICATION_ID" android:value="0" />
        <meta-data android:name="com.meta.wearable.mwdat.CLIENT_TOKEN" android:value="0" />

        <activity android:name="com.unity3d.player.UnityPlayerGameActivity"
                  android:theme="@style/BaseUnityGameActivityTheme"
                  android:exported="true">
            <intent-filter>
                <action android:name="android.intent.action.MAIN" />
                <category android:name="android.intent.category.LAUNCHER" />
            </intent-filter>

            <!-- DAT: esquema de callback que o Meta AI usa para voltar ao app -->
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

### 6.5 `Assets/Scripts/MetaDisplayHello.cs`

Crie um GameObject vazio na cena, chame de `MetaDisplay` e adicione este script. A interface usa IMGUI para não precisar montar Canvas.

```csharp
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using UnityEngine;
#if UNITY_ANDROID
using UnityEngine.Android;
#endif

/// Hello World no Meta Ray-Ban Display a partir do Unity.
/// Funciona só no celular Android: o plugin nativo não roda no Editor.
public class MetaDisplayHello : MonoBehaviour
{
    const string BridgeClass = "com.estudos.metadisplay.MetaDisplayBridge";
    const string ListenerInterface = "com.estudos.metadisplay.MetaDisplayListener";
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

    // Recebe os eventos do Kotlin. Roda numa thread do Android, NÃO na main thread do Unity,
    // então só enfileira. Quem processa é o Update().
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
                events.Enqueue(new KeyValuePair<string, string>("error", "Permissão Bluetooth negada"));
            Permission.RequestUserPermission(BluetoothConnect, callbacks);
        }
#else
        AddLog("Rode no celular Android: o plugin nativo não funciona no Editor.");
#endif
    }

    void Update()
    {
        if (permissionGranted && !initialized)
        {
            initialized = true;
            bridge.CallStatic("initialize", activity, new Listener(events));
            AddLog("DAT inicializado");
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
                // Ida e volta: toque nos óculos -> Kotlin -> C# -> nova tela nos óculos
                taps++;
                SendHello();
                break;
        }
        AddLog($"[{type}] {message}");
    }

    void SendHello() =>
        bridge.CallStatic("showHelloWorld", "Hello, World!", $"Enviado pelo Unity - toques: {taps}");

    // Bônus: renderiza a Main Camera numa textura 400x400 e manda como imagem
    void SendUnitySnapshot()
    {
        const int size = 400; // menor = mais rápido pelo Bluetooth (o display tem 600x600)
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

        // sbyte[] vira byte[] no Java sem ambiguidade de conversão
        var data = new sbyte[png.Length];
        Buffer.BlockCopy(png, 0, data, 0, png.Length);
        bridge.CallStatic("showImage", data, "Renderizado no Unity");
    }

    void OnGUI()
    {
        GUI.skin.label.fontSize = 32;
        GUI.skin.button.fontSize = 40;
        float h = Screen.height * 0.07f;

        GUILayout.BeginArea(new Rect(40, 100, Screen.width - 80, Screen.height - 140));
        GUILayout.Label($"Registro: {registration}\nSessão: {sessionState}\nDisplay: {displayState}");

        if (GUILayout.Button("1. Registrar no Meta AI", GUILayout.Height(h)))
            bridge?.CallStatic("startRegistration", activity);
        if (GUILayout.Button("2. Conectar ao Display", GUILayout.Height(h)))
            bridge?.CallStatic("connect");

        GUI.enabled = displayReady;
        if (GUILayout.Button("3. Enviar Hello World", GUILayout.Height(h))) SendHello();
        if (GUILayout.Button("Bônus: enviar frame do Unity", GUILayout.Height(h))) SendUnitySnapshot();
        GUI.enabled = true;

        if (GUILayout.Button("Desconectar", GUILayout.Height(h)))
            bridge?.CallStatic("disconnect");
        if (updateRequired && GUILayout.Button("Atualizar app DAT nos óculos", GUILayout.Height(h)))
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

## 7. Passo 3: build e teste no aparelho

1. Ative a depuração USB no celular e conecte o cabo.
2. No Unity, abra **File → Build Profiles → Android → Build And Run**.
3. No celular:
   1. Aceite a permissão de **Dispositivos próximos (Bluetooth)**. O log deve mostrar `DAT inicializado` e `[registration] AVAILABLE`.
   2. **1. Registrar no Meta AI**: o Meta AI abre, você aprova e o app volta. O estado esperado é `REGISTERED`. Em Dev Mode, o app aparece em *Meta AI → App connections → Developer mode apps*.
   3. Com os óculos **no rosto e conectados**, toque em **2. Conectar ao Display**. A sequência esperada é `session: STARTING → STARTED` e depois `display: STARTING → STARTED`.
   4. **3. Enviar Hello World**: o card aparece nos óculos 🎉
   5. Toque em **"Toque aqui"** nos óculos, pelo touchpad ou pela Neural Band. O contador de toques sobe e a tela é reenviada.
4. Para ver os logs no PC:
   ```powershell
   adb logcat -s MetaDisplayBridge Unity
   ```

---

## 8. Limitações do Display (vale para Unity ou nativo)

- **Tela de 600×600.** Imagens maiores que isso não melhoram nada e deixam o envio por Bluetooth mais lento.
- **Cada `sendContent` substitui a tela inteira.** Não existe atualização parcial.
- A rolagem é **só vertical**.
- **O display escurece após 20 s sem interação e dorme aos 25 s.** A sessão continua viva. Quando o display acorda, você pode reenviar o conteúdo.
- **Tocar com dois dedos na haste dos óculos = Back.** Na tela raiz, isso **encerra a sessão**. Trate o evento `session: STOPPED` no Unity.
- **Vídeo:** só MP4 por URL HTTPS, até **400 px por lado e 70.000 px no total**, um vídeo por vez.
- **Imagens:** por URL HTTPS ou `Bitmap`, que é o que o bônus do snapshot usa.
- Ligações, notificações e outros eventos do sistema podem interromper o seu conteúdo.
- O dispositivo pode recusar o display com `CAPABILITY_DENIED` mesmo com as versões corretas. Trate isso como um resultado esperado.
- Dica de design: em displays de guia de onda, **preto fica transparente**. Fundos escuros "somem" e cores claras aparecem.

---

## 9. Problemas comuns

| Sintoma | Causa provável e solução |
|---|---|
| `checkDebugAarMetadata`: *requires compileSdk 35* / *AGP 8.6.0* | Unity antigo ou Target API < 35. Use Unity 6.3 LTS (ou 6000.0.45f1+) e Target API 35/36. |
| `Duplicate class kotlin.*` ou `kotlinx.*` | Outro plugin Unity traz uma Kotlin stdlib antiga em `.jar`. Remova o `.jar` ou deixe o Gradle resolver uma versão só. |
| `2 files found with path 'META-INF/...'` | Ative *Custom Launcher Gradle Template* e, no bloco `android { }`, adicione `packaging { resources { excludes += ['/META-INF/{AL2.0,LGPL2.1}'] } }`. |
| `ClassNotFoundException: com.estudos.metadisplay...` | O `.aar` não está em `Plugins/Android` ou o R8 removeu a classe. Confira o `consumer-rules.pro` dentro do `.aar`. |
| `NoClassDefFoundError: com/meta/wearable/...` | Faltam as linhas `implementation 'com.meta.wearable:...'` no `mainTemplate.gradle`. |
| Registro não volta para o app | Confira o `<intent-filter>` com `android:scheme` na activity correta (GameActivity ou Activity). |
| `DAT_APP_ON_THE_GLASSES_UPDATE_REQUIRED` | Use o botão "Atualizar app DAT nos óculos" (`Wearables.openDATGlassesAppUpdate`). |
| Firmware abaixo de V128 | `Wearables.openFirmwareUpdate(activity)` (já exposto na ponte como `openFirmwareUpdate`). |
| `CAPABILITY_DENIED` | Dev Mode desligado, app DAT não instalado nos óculos ou recusa do dispositivo. Reinstale o DAT pelo Meta AI com os óculos no rosto. |
| Sessão não sai de `STARTING` | Os óculos não estão no rosto, não estão conectados ao Meta AI ou não têm display. |

---

## 10. Testar sem os óculos (opcional)

O SDK tem o **Mock Device Kit** (`com.meta.wearable:mwdat-mockdevice`). O sample `DisplayAccess` usa esse kit para um *phone preview* e um *Chrome preview*:

- `MockDeviceKit.getInstance(context).enable()`
- `pairGlasses(GlassesModel.META_RAYBAN_DISPLAY)`, depois `powerOn()` e `don()`
- `startTestServer(9000)`, depois `adb forward tcp:9000 tcp:9000`, e abra `http://127.0.0.1:9000/` no Chrome do PC

Desde o SDK 1.0.0, o CHANGELOG também lista:

- **`MockDisplayKit`**: renderiza a sessão de Display no próprio celular. `createPreviewView(context)` cria a view de preview e `sendClick(identifier)` simula toques.
- **`simulateRegistrationOutcome(Boolean)`**: simula o resultado do registro no Meta AI, sem precisar dos óculos.

Para usar isso no Unity, adicione a dependência `mwdat-mockdevice` no módulo e no `mainTemplate.gradle` e exponha um método `enableMock()` na ponte, copiando a lógica do `WearablesRepository.kt` do sample. Ainda não testei esse fluxo junto com o Unity.

Outro sample do repositório, o **BirdSpotter**, é um app "de verdade" estendido para os óculos. Ele usa cards no display, inputs, IMU e fala, e roda inteiro no MockDeviceKit. É uma boa referência de como organizar um app maior.

---

## 11. Próximos passos

- **Inputs, voz e sensores:** o DAT também oferece eventos de navegação, reconhecimento de fala, áudio e IMU. É o mesmo padrão: expor na ponte e enviar eventos para o C#.
- **Vídeo:** criar `VideoPlayer(VideoSource.Url(...), VideoCodec.MP4)`, enviar como raiz com `sendContent { video(player) }` e chamar `play()` quando o envio der certo.
- **Registro iniciado pelo Meta AI:** o SDK 1.0+ permite que o Meta AI inicie o registro. Para isso, `Wearables.handleIntent(intent, ...)` precisa ser chamado no `onCreate` e no `onNewIntent` da activity. No Unity, isso exige uma activity própria que estenda `UnityPlayerGameActivity`.
- **Publicação:** trocar os `0` por `APPLICATION_ID` e `CLIENT_TOKEN` reais do Wearables Developer Center e configurar *release channels*.

---

## Fontes

- [Display – Android (DAT)](https://wearables.developer.meta.com/docs/develop/dat/display-android)
- [Display overview / requisitos](https://wearables.developer.meta.com/docs/develop/dat/display-overview)
- [Integração Android (manifest, registro, sessão)](https://wearables.developer.meta.com/docs/develop/dat/build-integration-android)
- [Setup / Developer Mode](https://wearables.developer.meta.com/docs/develop/dat/getting-started-toolkit/)
- [Version Dependencies](https://wearables.developer.meta.com/docs/develop/dat/version-dependencies)
- [Visão geral do DAT](https://wearables.developer.meta.com/docs/develop/dat/build-overview)
- [Streaming da câmera (resolução, fps, Bluetooth Classic)](https://wearables.developer.meta.com/docs/develop/dat/streaming)
- [Processamento do frame da câmera (HEVC, estabilização)](https://wearables.developer.meta.com/docs/develop/dat/camera-stream-processing)
- [Microfones e alto-falantes (HFP/A2DP)](https://wearables.developer.meta.com/docs/develop/dat/microphones-and-speakers)
- [Motion (IMU)](https://wearables.developer.meta.com/docs/develop/dat/motion)
- [Web Apps: sensores e localização](https://wearables.developer.meta.com/docs/develop/webapps/build/sensors-and-location/)
- [Referência da API Android](https://wearables.developer.meta.com/docs/reference/android/dat/latest)
- [Repositório oficial + sample DisplayAccess](https://github.com/meta/meta-wearables-dat-android/tree/main/samples/DisplayAccess)
- [Maven Central: com.meta.wearable](https://central.sonatype.com/artifact/com.meta.wearable/mwdat-core/versions)
- [Unity 6.3: compatibilidade Gradle/AGP](https://docs.unity.com/en-us/engine/6000.3/manual/platform-specific/android/introducing/gradle-overview/version-compatibility)
- [Unity 6.0: compatibilidade Gradle/AGP](https://docs.unity3d.com/6000.0/Documentation/Manual/android-gradle-version-compatibility.html)
