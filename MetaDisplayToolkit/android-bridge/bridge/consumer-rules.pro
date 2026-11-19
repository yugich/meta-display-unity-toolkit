# A ponte é chamada do C# por JNI/reflexão: não pode ser removida nem renomeada.
-keep class com.estudos.metadisplay.** { *; }

# Módulos opcionais do DAT podem ser excluídos do build (Meta Display > Configurações).
-dontwarn com.meta.wearable.dat.camera.**
-dontwarn com.meta.wearable.dat.inputs.**
-dontwarn com.meta.wearable.dat.motion.**
-dontwarn com.meta.wearable.dat.speech.**
-dontwarn com.meta.wearable.dat.mockdevice.**
