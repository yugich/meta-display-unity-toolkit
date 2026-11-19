using System;
using System.IO;
using MetaDisplay;
using MetaDisplay.Editor;
using UnityEditor;
using UnityEditor.Build;
using UnityEngine;

// Script de teste (fica só no projeto de teste, não vai no pacote).
public static class MDTestRunner
{
    public static void Demo()
    {
        MetaDisplayMenus.CreateDemoScene();
        foreach (var s in MetaDisplayMenus.FindAll<GlassesScreen>())
        {
            Debug.Log("MDTEST screen '" + s.name + "' JSON=" + s.BuildJson());
            foreach (var w in GlassesCanvasConverter.Validate(s)) Debug.Log("MDTEST warn '" + s.name + "': " + w);
        }
        var node = Glasses.Column(12, 24).Add(
            Glasses.Heading("Oi \"aspas\"\nlinha"),
            Glasses.Button("OK", () => { }, icon: GlassesIconName.Checkmark, primary: true),
            Glasses.ButtonGroup(Glasses.Button("A"), Glasses.Button("B", () => { }, GlassesButtonStyle.Outline)),
            Glasses.Row(8).Add(Glasses.Icon(GlassesIconName.Circle8RaysLarge, GlassesIconStyle.Outline), Glasses.Spacer(), Glasses.Meta("fim")),
            Glasses.ImageUrl("https://example.com/a.png", GlassesImageSize.Icon));
        MetaGlassesDisplay.Show(node);
        Debug.Log("MDTEST code JSON=" + MetaGlassesDisplay.LastLayoutJson);
    }

    public static void BuildApk()
    {
        var settings = MetaDisplaySettings.Load();
        Debug.Log("MDTEST esquema: " + settings.EffectiveScheme);
        PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Android, "com.estudos.metadisplaytest");
        PlayerSettings.Android.minSdkVersion = (AndroidSdkVersions)31;
        PlayerSettings.Android.targetSdkVersion = AndroidSdkVersions.AndroidApiLevelAuto;
        PlayerSettings.SetScriptingBackend(NamedBuildTarget.Android, ScriptingImplementation.IL2CPP);
        PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
        PlayerSettings.Android.forceInternetPermission = true;
        var output = Path.GetFullPath("../build/MetaDisplayTest.apk");
        Directory.CreateDirectory(Path.GetDirectoryName(output));
        var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
        {
            scenes = new[] { "Assets/MetaDisplayDemo.unity" },
            locationPathName = output,
            target = BuildTarget.Android,
            options = BuildOptions.None,
        });
        Debug.Log("MDTEST build: " + report.summary.result + " erros=" + report.summary.totalErrors + " tamanho=" + report.summary.totalSize + " -> " + output);
        EditorApplication.Exit(report.summary.result == UnityEditor.Build.Reporting.BuildResult.Succeeded ? 0 : 1);
    }

    /// <summary>Exporta Assets/MetaDisplayToolkit para o caminho em MD_EXPORT_PATH.</summary>
    public static void Export()
    {
        AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        var output = Environment.GetEnvironmentVariable("MD_EXPORT_PATH");
#pragma warning disable CS0618
        AssetDatabase.ExportPackage("Assets/MetaDisplayToolkit", output, ExportPackageOptions.Recurse);
        Debug.Log("MDTEST exportado: " + output + " (" + new FileInfo(output).Length + " bytes)");
    }
}
