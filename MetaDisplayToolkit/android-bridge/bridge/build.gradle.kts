import org.jetbrains.kotlin.gradle.dsl.JvmTarget

plugins {
  alias(libs.plugins.android.library)
  alias(libs.plugins.jetbrains.kotlin.android)
}

android {
  namespace = "com.estudos.metadisplay"
  compileSdk = 36

  defaultConfig {
    // O SDK da Meta declara minSdk 29 (Android 10)
    minSdk = 29
    // Vai dentro do .aar e é aplicado no build do Unity
    consumerProguardFiles("consumer-rules.pro")
  }

  buildTypes {
    release { isMinifyEnabled = false }
  }

  compileOptions {
    sourceCompatibility = JavaVersion.VERSION_17
    targetCompatibility = JavaVersion.VERSION_17
  }
}

kotlin {
  compilerOptions {
    jvmTarget = JvmTarget.JVM_17
    // APIs experimentais do DAT (áudio, voice invocations, foto standalone) exigem opt-in
    freeCompilerArgs.add("-opt-in=kotlin.RequiresOptIn")
  }
}

dependencies {
  // Só para compilar: o .aar NÃO carrega estas dependências.
  // O Unity adiciona os mesmos artefatos no build (MetaDisplayGradlePostProcessor.cs).
  compileOnly(libs.mwdat.core)
  compileOnly(libs.mwdat.display)
  compileOnly(libs.mwdat.camera)
  compileOnly(libs.mwdat.inputs)
  compileOnly(libs.mwdat.motion)
  compileOnly(libs.mwdat.speech)
  compileOnly(libs.mwdat.mockdevice)
}
