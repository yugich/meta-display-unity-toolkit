import org.jetbrains.kotlin.gradle.dsl.JvmTarget

plugins {
  alias(libs.plugins.android.library)
  alias(libs.plugins.jetbrains.kotlin.android)
}

android {
  namespace = "com.metadisplay.bridge"
  compileSdk = 36

  defaultConfig {
    // The Meta SDK declares minSdk 29 (Android 10)
    minSdk = 29
    // Shipped inside the .aar and applied by the Unity build
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
    // Experimental DAT APIs (audio, voice invocations, standalone photo) require opt-in
    freeCompilerArgs.add("-opt-in=kotlin.RequiresOptIn")
  }
}

dependencies {
  // Compile only: the .aar does NOT carry these dependencies.
  // Unity adds the same artifacts at build time (MetaDisplayGradlePostProcessor.cs).
  compileOnly(libs.mwdat.core)
  compileOnly(libs.mwdat.display)
  compileOnly(libs.mwdat.camera)
  compileOnly(libs.mwdat.inputs)
  compileOnly(libs.mwdat.motion)
  compileOnly(libs.mwdat.speech)
  compileOnly(libs.mwdat.mockdevice)
}
