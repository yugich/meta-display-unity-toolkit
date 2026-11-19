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

    /// <summary>Avatar sample scene (3D strategies) + a Web App export next to the project.</summary>
    public static void Avatar()
    {
        Glasses3DMenus.CreateAvatarScene();
        var view = MetaDisplayMenus.FindAll<Glasses3DView>()[0];
        Debug.Log("MDTEST avatar: target=" + view.target.name + " poses=" + view.poses.Count + " buttons=" + view.buttons.Count);
        var folder = Path.GetFullPath("../build/WebApp");
        Debug.Log("MDTEST webapp: " + Glasses3DWebAppExporter.Export(folder, Glasses3DWebAppExporter.OptionsFor(view)));
    }

    /// <summary>Renders a few frames of the avatar scene to PNG (what the glasses receive) in ../build/snapshots.</summary>
    public static void Snapshot()
    {
        UnityEditor.SceneManagement.EditorSceneManager.OpenScene(Glasses3DMenus.AvatarScenePath);
        var view = MetaDisplayMenus.FindAll<Glasses3DView>()[0];
        var folder = Path.GetFullPath("../build/snapshots");
        Directory.CreateDirectory(folder);
        foreach (var name in new[] { "idle", "wave", "happy", "think" })
        {
            var pose = view.FindPose(name);
            var frames = view.RenderFrames(pose, 300, 300, 4);
            File.WriteAllBytes(Path.Combine(folder, name + ".png"), frames[1].EncodeToPNG());
            File.WriteAllBytes(Path.Combine(folder, name + ".jpg"), frames[1].EncodeToJPG(view.jpegQuality));
            Debug.Log($"MDTEST snapshot {name}: jpg {frames[1].EncodeToJPG(view.jpegQuality).Length} bytes");
        }
    }

    public static void BuildApk() => Build("com.metadisplay.demo", "Meta Display Demo", "Assets/MetaDisplayDemo.unity", "MetaDisplayDemo.apk");

    public static void BuildAvatarApk() => Build("com.metadisplay.avatar", "Meta Display Avatar", "Assets/MetaDisplayAvatarDemo.unity", "MetaDisplayAvatarDemo.apk");

    static void Build(string id, string productName, string scene, string apk)
    {
        PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Android, id);
        PlayerSettings.productName = productName;
        PlayerSettings.companyName = "MetaDisplayToolkit";
        var settings = MetaDisplaySettings.Load();
        Debug.Log("MDTEST scheme: " + settings.EffectiveScheme);
        PlayerSettings.Android.minSdkVersion = (AndroidSdkVersions)31;
        PlayerSettings.Android.targetSdkVersion = AndroidSdkVersions.AndroidApiLevelAuto;
        PlayerSettings.SetScriptingBackend(NamedBuildTarget.Android, ScriptingImplementation.IL2CPP);
        PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
        PlayerSettings.Android.forceInternetPermission = true;
        var output = Path.GetFullPath("../build/" + apk);
        Directory.CreateDirectory(Path.GetDirectoryName(output));
        var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
        {
            scenes = new[] { scene },
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
