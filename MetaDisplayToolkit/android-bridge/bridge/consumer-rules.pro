# The bridge is called from C# through JNI/reflection: it must not be removed or renamed.
-keep class com.metadisplay.bridge.** { *; }

# Optional DAT modules can be excluded from the build (Meta Display > Configure Project).
-dontwarn com.meta.wearable.dat.camera.**
-dontwarn com.meta.wearable.dat.inputs.**
-dontwarn com.meta.wearable.dat.motion.**
-dontwarn com.meta.wearable.dat.speech.**
-dontwarn com.meta.wearable.dat.mockdevice.**
