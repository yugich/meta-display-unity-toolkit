using System;
using System.IO;
using MetaDisplay;
using MetaDisplay.Editor;
using UnityEditor;
using UnityEditor.Build;
using UnityEngine;

// Test script (lives only in the test project, it is not part of the package).
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
            Glasses.Heading("Hi \"quotes\"\nline"),
            Glasses.Button("OK", () => { }, icon: GlassesIconName.Checkmark, primary: true),
            Glasses.ButtonGroup(Glasses.Button("A"), Glasses.Button("B", () => { }, GlassesButtonStyle.Outline)),
            Glasses.Row(8).Add(Glasses.Icon(GlassesIconName.Circle8RaysLarge, GlassesIconStyle.Outline), Glasses.Spacer(), Glasses.Meta("end")),
            Glasses.ImageUrl("https://example.com/a.png", GlassesImageSize.Icon));
        MetaGlassesDisplay.Show(node);
        Debug.Log("MDTEST code JSON=" + MetaGlassesDisplay.LastLayoutJson);
    }

    public static void BuildApk()
    {
        PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Android, "com.metadisplay.demo");
        PlayerSettings.productName = "Meta Display Demo";
        PlayerSettings.companyName = "MetaDisplayToolkit";
        var settings = MetaDisplaySettings.Load();
        Debug.Log("MDTEST scheme: " + settings.EffectiveScheme);
        PlayerSettings.Android.minSdkVersion = (AndroidSdkVersions)31;
        PlayerSettings.Android.targetSdkVersion = AndroidSdkVersions.AndroidApiLevelAuto;
        PlayerSettings.SetScriptingBackend(NamedBuildTarget.Android, ScriptingImplementation.IL2CPP);
        PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
        PlayerSettings.Android.forceInternetPermission = true;
        var output = Path.GetFullPath("../build/MetaDisplayDemo.apk");
        Directory.CreateDirectory(Path.GetDirectoryName(output));
        var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
        {
            scenes = new[] { "Assets/MetaDisplayDemo.unity" },
            locationPathName = output,
            target = BuildTarget.Android,
            options = BuildOptions.None,
        });
        Debug.Log("MDTEST build: " + report.summary.result + " errors=" + report.summary.totalErrors + " size=" + report.summary.totalSize + " -> " + output);
        EditorApplication.Exit(report.summary.result == UnityEditor.Build.Reporting.BuildResult.Succeeded ? 0 : 1);
    }

    /// <summary>Exports Assets/MetaDisplayToolkit to the path in MD_EXPORT_PATH.</summary>
    public static void Export()
    {
        AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        var output = Environment.GetEnvironmentVariable("MD_EXPORT_PATH");
#pragma warning disable CS0618
        AssetDatabase.ExportPackage("Assets/MetaDisplayToolkit", output, ExportPackageOptions.Recurse);
#pragma warning restore CS0618
        Debug.Log("MDTEST exported: " + output + " (" + new FileInfo(output).Length + " bytes)");
    }
}
