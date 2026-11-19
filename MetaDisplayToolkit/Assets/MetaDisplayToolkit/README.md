# Meta Display Toolkit para Unity

Toolkit para criar apps Unity (Android) que usam o **Meta Ray-Ban Display** e os demais óculos Meta através do
**Meta Wearables Device Access Toolkit (DAT) 1.0.1**. Ele cobre:

| Recurso | Classe C# | Observação |
|---|---|---|
| Registro no Meta AI, dispositivos, permissões, atualizações | `MetaWearables` | |
| Sessão com os óculos | `MetaWearables.StartSession` / `StopSession` | |
| **Display**: telas, cliques, imagens, ícones, vídeo | `MetaGlassesDisplay`, `Glasses`, `GlassesScreen` | 600×600 |
| **UI dos óculos desenhada no Canvas** | `GlassesScreen` + `Glasses*` | conversão automática |
| Câmera (vídeo → `RenderTexture`, fotos, áudio) | `MetaCamera` | áudio e foto HQ são experimentais |
| IMU dos óculos e da Neural Band | `MetaMotion` | experimental |
| Inputs (navegação, seleção, voltar, captura, arraste) | `MetaInputs` | experimental |
| Reconhecimento de fala nos óculos | `MetaSpeech` | experimental |
| "Hey Meta, start {app}" | `MetaVoiceInvocations` | experimental |
| Óculos simulados (sem hardware) + preview do display | `MetaMockDevice` | |
| Simulação no Editor (Play Mode) | automática | |

> "Experimental" = funciona em **Developer Mode** e canais beta, mas ainda não pode ir para produção (regra da Meta).

---

## 1. Começo rápido (5 minutos)

1. **Requisitos:** Unity **6.0.45+ ou 6.3 LTS+** com Android Build Support; óculos com firmware V128+; app Meta AI V292+;
   **Developer Mode** ligado e o **Device Access Toolkit instalado nos óculos** (Meta AI → Settings → App Info → toque 5× na versão → Install, com os óculos no rosto).
2. Importe o pacote: **Assets → Import Package → Custom Package… → `MetaDisplayToolkit.unitypackage`**.
3. Abra **Meta Display → Configurar Projeto (Setup)** e clique em **Corrigir tudo**
   (Android, uGUI/TextMeshPro, API 31/35+, IL2CPP ARM64, Internet).
4. **Meta Display → Criar cena de exemplo**. Dê **Play**: o Editor simula os óculos e você pode navegar pelas telas
   clicando no espelho no meio da tela.
5. **File → Build Profiles → Android → Build And Run**. No celular, use o painel **Meta Display**:
   *1. Registrar no Meta AI* → *2. Conectar aos óculos*. A tela "Início" aparece nos óculos.

Não é preciso editar `AndroidManifest.xml` nem `mainTemplate.gradle`: o `MetaDisplayGradlePostProcessor`
adiciona as dependências do Maven Central, permissões, `APPLICATION_ID`/`CLIENT_TOKEN` e o esquema de callback do Meta AI.

---

## 2. O que vai para os óculos e o que fica no celular

- **Celular:** qualquer Canvas normal do Unity.
- **Óculos:** objetos com o componente **`GlassesScreen`** (uma área de 600×600). Cada tela tem a opção
  **Visibilidade no celular**:
  - *Só nos óculos*: escondida no celular;
  - *Óculos + espelho no celular*: a tela atual aparece centralizada no celular (ótimo para depurar);
  - *Óculos + miniatura*: aparece pequena no canto.
- No Inspector do **`MetaDisplayManager`**, a seção **Mapa de UIs** lista todos os Canvas da cena e permite trocar o
  destino (Celular ↔ Óculos) com um clique.
- Um objeto com **`GlassesIgnore`** aparece só no celular, mesmo dentro de uma tela dos óculos.

## 3. Desenhando a UI dos óculos no Canvas

Use **GameObject → Meta Display →** *Tela dos Óculos, Container (Coluna/Linha/Cartão), Texto (Título/Corpo/Legenda),
Botão, Grupo de Botões, Imagem, Ícone*. O Canvas é o **preview**: os componentes `Glasses*` ajustam layout, cores e
tamanhos para ficar parecido com os óculos.

| No Canvas | Nos óculos |
|---|---|
| `GlassesFlexBox` ou Horizontal/Vertical/Grid **LayoutGroup** | `flexBox` (direção, gap, padding, alinhamentos, fundo *Card*, clique) |
| `GlassesText`, **TextMeshPro** ou **Text** | `text` (Título/Corpo/Legenda; cor primária/secundária) |
| `GlassesButton` ou **Button** | `button` (estilo, ícone, ação principal, **Navegar para** outra tela) |
| `GlassesImage`, **RawImage** ou **Image** com sprite | `image` (textura enviada como PNG uma vez, ou URL HTTPS) |
| `GlassesIcon` | um dos 116 ícones nativos (`GlassesIconName`) |
| `GlassesButtonGroup` | `buttonGroup` |

Dicas:
- **Preto = transparente** nos óculos. O fundo preto da tela é só para o preview.
- O conteúdo é convertido **na hora do envio**. Com *Auto Refresh* ligado, mudar um texto já reenvia a tela.
- Toggle, Slider, InputField e Dropdown não existem nos óculos (o Inspector avisa).
- O Inspector da `GlassesScreen` mostra **o JSON enviado** e os avisos de validação.
- Navegação sem código: no `GlassesButton`, preencha **Navigate To** com outra `GlassesScreen`.

## 4. Por código

```csharp
using MetaDisplay;

MetaGlassesDisplay.Show(
    Glasses.Column(gap: 12, padding: 24).Add(
        Glasses.Row(gap: 12).Add(Glasses.Icon(GlassesIconName.SmartGlasses), Glasses.Heading("Olá!")),
        Glasses.Card().Add(Glasses.Text("Corpo do texto"), Glasses.Meta("legenda")),
        Glasses.Image(minhaTextura),
        Glasses.ButtonGroup(
            Glasses.Button("OK", () => Debug.Log("toque"), icon: GlassesIconName.Checkmark, primary: true),
            Glasses.Button("Cancelar", null, GlassesButtonStyle.Secondary))));

GlassesScreen.Find("inicio").Show();          // tela desenhada no Canvas
MetaGlassesDisplay.PlayVideo("https://.../clip.mp4");

MetaCamera.Start(CameraQuality.Low, CameraFps.Fps15);
rawImage.texture = MetaCamera.Texture;        // RenderTexture atualizada a cada frame
MetaCamera.CapturePhoto(tex => foto = tex);

MetaMotion.SampleReceived += s => Debug.Log(s.Orientation);
MetaInputs.InputReceived += e => Debug.Log(e);
MetaSpeech.FinalTranscription += texto => Debug.Log(texto);
```

Veja `Samples/MetaDisplayCodeSample.cs` para um exemplo completo só com código.

## 5. Testar sem os óculos

- **Editor (Play Mode):** simulação automática de registro → sessão → display. Clique no espelho da tela para testar os botões.
- **Celular, sem óculos:** marque *Óculos simulados no celular* no `MetaDisplayManager` (ou use a aba *Mock* do painel).
  O MockDeviceKit desenha o **display simulado por cima do app** e permite injetar inputs, fala e "Hey Meta".
  *Preview no Chrome do PC*: botão *Preview no Chrome* → `adb forward tcp:9000 tcp:9000` → abra `http://127.0.0.1:9000/`.

## 6. Limitações do hardware/SDK

- Tela de 600×600. Cada envio substitui a tela inteira (não há atualização parcial).
- Tudo passa por Bluetooth: imagens grandes e muitas imagens deixam o envio lento.
- O display escurece em 20 s e dorme em 25 s sem interação (a sessão continua).
- Voltar com dois dedos na haste, na tela raiz, **encerra a sessão**.
- Vídeo: só MP4 por HTTPS, até 400 px por lado e 70.000 px no total.
- A cena 3D do Unity **não** é espelhada nos óculos. Dá para enviar *snapshots* como imagem (`Glasses.Image(renderTexture)`), mas não em tempo real.

## 7. Problemas comuns

| Sintoma | Solução |
|---|---|
| `NO_ELIGIBLE_DEVICE` ao conectar | Óculos desligados, fora do rosto ou sem display. Confira no Meta AI. |
| `DAT_APP_ON_THE_GLASSES_UPDATE_REQUIRED` | Botão *Atualizar app DAT nos óculos* no painel (ou `MetaWearables.OpenGlassesAppUpdate()`). |
| `CAPABILITY_DENIED` | Developer Mode desligado ou DAT não instalado nos óculos; recursos experimentais precisam estar liberados no Wearables Developer Center para builds de produção. |
| Registro não volta ao app | Confira o *Esquema de callback* em Configurar Projeto (o build injeta o intent-filter). |
| Cores da câmera trocadas | `MetaCamera.LayoutOverride = YuvLayout.NV21` (ou o botão no painel). |
| Vídeo não volta depois de parar | Veja o log do painel: se aparecer *Permissão Camera: Denied*, o usuário escolheu "Permitir uma vez" no Meta AI (cada início pede de novo). Se a câmera não mandar frames em 12 s, o toolkit reinicia o stream sozinho uma vez. |
| Foto HQ demora | A foto vem pelo Bluetooth: na resolução máxima (~1,6 MB) leva ~1 min, e a conversão HEIC → JPEG mais alguns segundos. O vídeo pausa durante a captura e volta sozinho. Use `PhotoResolution.Medium` para testes. |
| "Iniciar" durante a foto HQ | O pedido fica na fila e o vídeo começa quando a foto terminar. |
| Erro de Gradle `minCompileSdk 35` / AGP | Unity antigo. Use 6.0.45+ ou 6.3+ e Target API ≥ 35. |
| `ClassNotFoundException com.estudos.metadisplay` | O `MetaDisplayBridge.aar` precisa estar com a plataforma Android marcada. |

Logs no PC: `adb logcat -s MetaDisplayUnity Unity DAT:*`
(o Android descarta logs antigos de apps "falantes"; para investigar algo que já passou, aumente o buffer antes do teste com `adb logcat -G 16M`).

## 8. Estrutura

```
MetaDisplayToolkit/
├─ Plugins/Android/MetaDisplayBridge.aar   ponte Kotlin (código-fonte em android-bridge/)
├─ Runtime/Core          MetaWearables, eventos, simulação no Editor
├─ Runtime/Display       GlassesNode (modelo da UI), MetaGlassesDisplay, ícones
├─ Runtime/Canvas        GlassesScreen, componentes Glasses*, conversor Canvas → óculos
├─ Runtime/Capabilities  câmera, IMU, inputs, fala, voz, MockDevice
├─ Runtime/Components    MetaDisplayManager, painel de debug, contador de exemplo
├─ Resources/MetaDisplay shader YUV → RGB da câmera
├─ Editor                Setup, pós-processador do Gradle, menus, inspectors
└─ Samples               exemplo só com código
```

Configurações do projeto ficam em `ProjectSettings/MetaDisplaySettings.json` (credenciais, módulos incluídos no APK, versão do SDK).
Para produção: troque `Application ID`/`Client Token` (Wearables Developer Center) e desmarque *Mock Device*.
