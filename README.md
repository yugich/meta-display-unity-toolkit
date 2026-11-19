# Meta Display Toolkit para Unity

Toolkit para criar apps **Unity (Android)** para o **Meta Ray-Ban Display** usando o
**Meta Wearables Device Access Toolkit (DAT) 1.0.1**, com a UI dos óculos desenhada no **Canvas** do Unity.

## Downloads (Releases)

Na página de **Releases** deste repositório:

| Arquivo | Para quê |
|---|---|
| `MetaDisplayToolkit.unitypackage` | Importar no Unity (Assets → Import Package → Custom Package…) |
| `MetaDisplayDemo.apk` | Cena de exemplo pronta para testar nos óculos: `adb install -r MetaDisplayDemo.apk` |

## O que tem aqui

| Caminho | Conteúdo |
|---|---|
| [`MetaDisplayToolkit/Assets/MetaDisplayToolkit/`](MetaDisplayToolkit/Assets/MetaDisplayToolkit/) | Código do pacote Unity (C#, Editor, shader e a ponte `.aar`). Manual: [LEIAME.md](MetaDisplayToolkit/Assets/MetaDisplayToolkit/LEIAME.md) |
| [`MetaDisplayToolkit/android-bridge/`](MetaDisplayToolkit/android-bridge/) | Projeto Gradle da ponte Kotlin (Unity ↔ SDK da Meta) |
| [`MetaDisplayToolkit/tools/unity-tests/`](MetaDisplayToolkit/tools/unity-tests/) | Scripts usados para validar o pacote (build do APK, exportação, teste PlayMode) |
| [`Unity-MetaRayBanDisplay-HelloWorld.md`](Unity-MetaRayBanDisplay-HelloWorld.md) | Guia de estudo: como Unity, Android e o display dos óculos se conectam |

Detalhes de arquitetura, como recompilar a ponte e o que foi validado: [MetaDisplayToolkit/README.md](MetaDisplayToolkit/README.md).

## Recursos

- Registro no Meta AI, dispositivos, permissões, sessão e atualizações
- **Display**: telas, cliques, imagens, 116 ícones nativos e vídeo, montados por código (`Glasses.*`) ou **no Canvas** (`GlassesScreen`)
- Escolha o que aparece **no celular** e o que aparece **nos óculos** (espelho, miniatura ou só nos óculos)
- Câmera (vídeo → `RenderTexture`, fotos, áudio), IMU dos óculos e da Neural Band, inputs, fala e "Hey Meta"
- Óculos simulados (MockDeviceKit) e simulação no Editor
- Gradle e manifest configurados automaticamente no build

## Requisitos

Unity 6.0.45+ ou 6.3 LTS+ com Android Build Support · Meta Ray-Ban Display (firmware V128+) · app Meta AI V292+
com Developer Mode e o Device Access Toolkit instalado nos óculos.

O uso do SDK da Meta está sujeito aos [Meta Wearables Developer Terms](https://wearables.developer.meta.com/terms).
