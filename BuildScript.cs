using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace TomYum.EditorTools
{
    public static class BuildScript
    {
        const string ScenePath = "Assets/Scenes/Main.unity";
        const string ApkPath = "build/Android/TomYumKitchen.apk";

        // The game builds its whole kitchen at runtime, so the scene only needs to exist (empty).
        [MenuItem("TomYum/Create Main Scene")]
        public static void EnsureScene()
        {
            if (!File.Exists(ScenePath))
            {
                Directory.CreateDirectory("Assets/Scenes");
                var s = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                EditorSceneManager.SaveScene(s, ScenePath);
                AssetDatabase.Refresh();
            }
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
        }

        public static void ConfigureAndroid()
        {
            PlayerSettings.companyName = "TomYumKitchen";
            PlayerSettings.productName = "Tom Yum Kitchen";
            PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Android, "com.tomyumkitchen.game");
            PlayerSettings.SetScriptingBackend(NamedBuildTarget.Android, ScriptingImplementation.IL2CPP);
            PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
            PlayerSettings.Android.minSdkVersion = AndroidSdkVersions.AndroidApiLevel24;
            PlayerSettings.Android.targetSdkVersion = AndroidSdkVersions.AndroidApiLevelAuto;
            PlayerSettings.Android.forceInternetPermission = true; // needed for sockets

            PlayerSettings.defaultInterfaceOrientation = UIOrientation.AutoRotation;
            PlayerSettings.allowedAutorotateToPortrait = false;
            PlayerSettings.allowedAutorotateToPortraitUpsideDown = false;
            PlayerSettings.allowedAutorotateToLandscapeLeft = true;
            PlayerSettings.allowedAutorotateToLandscapeRight = true;
            EditorUserBuildSettings.buildAppBundle = false; // APK, not AAB
        }

        // Called by game-ci:  buildMethod: TomYum.EditorTools.BuildScript.BuildAndroid
        public static void BuildAndroid()
        {
            EnsureScene();
            ConfigureAndroid();

            var opts = new BuildPlayerOptions
            {
                scenes = new[] { ScenePath },
                locationPathName = ApkPath,
                target = BuildTarget.Android,
                options = BuildOptions.None
            };
            BuildReport report = BuildPipeline.BuildPlayer(opts);
            if (report.summary.result != BuildResult.Succeeded)
            {
                Debug.LogError("Build failed: " + report.summary.result);
                EditorApplication.Exit(1);
            }
        }
    }
}
