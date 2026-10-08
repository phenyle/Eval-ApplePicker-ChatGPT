using System;
using System.IO;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Rendering;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Forge.Bench.Editor
{
    /// <summary>
    /// Reproducible project configuration (plan §2.7). Every setting that affects measurement is applied here
    /// from code, so both games and all variants are configured identically and the configuration is
    /// reviewable in git. Run in batch mode:
    ///   Unity -batchmode -quit -projectPath P -executeMethod &lt;Game&gt;Setup.Run
    /// </summary>
    public static class BenchProjectSetup
    {
        public class Options
        {
            public string ProductName;
            public bool MainLightShadows;
            public Type BootComponent;
        }

        const string BenchDir = "Assets/Bench";
        const string ResourcesDir = "Assets/Bench/Resources";
        const string ScenePath = "Assets/Scenes/Bench.unity";

        public static void Apply(Options o)
        {
            ImportTmpEssentials();
            var urp = CreateUrp(o.MainLightShadows);
            ConfigureGraphics(urp);
            ConfigureQuality(urp);
            ConfigurePlayer(o.ProductName);
            CreateAssetSet();
            CreateScene(o.BootComponent);
            Physics.simulationMode = SimulationMode.Script;   // no engine physics in any variant (D3)
            AssetDatabase.SaveAssets();
            Debug.Log("[bench-setup] done");
        }

        /// <summary>TMP Essential Resources must be imported first with Unity's synchronous command-line flag:
        ///   -importPackage ".../BuiltInPackages/com.unity.ugui/Package Resources/TMP Essential Resources.unitypackage"
        /// (AssetDatabase.ImportPackage is deferred in batch mode and is not visible within the same call.)</summary>
        static void ImportTmpEssentials()
        {
            if (!AssetDatabase.IsValidFolder("Assets/TextMesh Pro"))
                throw new Exception("TMP Essential Resources missing: run Unity once with -importPackage (see BenchProjectSetup)");
        }

        static UniversalRenderPipelineAsset CreateUrp(bool shadows)
        {
            Directory.CreateDirectory(BenchDir);
            string rdPath = BenchDir + "/BenchURP_Renderer.asset", aPath = BenchDir + "/BenchURP.asset";
            var rd = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(rdPath);
            if (rd == null)
            {
                rd = ScriptableObject.CreateInstance<UniversalRendererData>();
                AssetDatabase.CreateAsset(rd, rdPath);
            }
            rd.renderingMode = RenderingMode.ForwardPlus;
            EditorUtility.SetDirty(rd);
            var asset = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(aPath);
            if (asset == null)
            {
                asset = UniversalRenderPipelineAsset.Create(rd);
                AssetDatabase.CreateAsset(asset, aPath);
            }
            var so = new SerializedObject(asset);
            so.FindProperty("m_MSAA").intValue = 1;
            so.FindProperty("m_SupportsHDR").boolValue = false;
            so.FindProperty("m_MainLightShadowsSupported").boolValue = shadows;
            so.FindProperty("m_SoftShadowsSupported").boolValue = false;
            so.FindProperty("m_UseSRPBatcher").boolValue = true;
            so.FindProperty("m_GPUResidentDrawerMode").intValue = 1;  // GPUResidentDrawerMode.InstancedDrawing (D2)
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(asset);
            AssetDatabase.SaveAssets();
            return asset;
        }

        static void ConfigureGraphics(UniversalRenderPipelineAsset urp)
        {
            GraphicsSettings.defaultRenderPipeline = urp;
            // BatchRendererGroup variants: Keep All (required by the GPU Resident Drawer and Entities Graphics).
            var gs = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/GraphicsSettings.asset")[0]);
            gs.FindProperty("m_BrgStripping").intValue = (int)BatchRendererGroupStrippingMode.KeepAll;
            gs.ApplyModifiedPropertiesWithoutUndo();
        }

        static void ConfigureQuality(UniversalRenderPipelineAsset urp)
        {
            string[] names = QualitySettings.names;
            for (int i = 0; i < names.Length; i++)
            {
                QualitySettings.SetQualityLevel(i, false);
                QualitySettings.vSyncCount = 0;
                QualitySettings.renderPipeline = urp;
                QualitySettings.antiAliasing = 0;
            }
            int hi = Array.IndexOf(names, "High");
            if (hi < 0) hi = names.Length - 1;
            QualitySettings.SetQualityLevel(hi, false);
            var so = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/QualitySettings.asset")[0]);
            var per = so.FindProperty("m_PerPlatformDefaultQuality");
            for (int i = 0; i < per.arraySize; i++)
            {
                var e = per.GetArrayElementAtIndex(i);
                if (e.FindPropertyRelative("first").stringValue == "Standalone")
                    e.FindPropertyRelative("second").intValue = hi;
            }
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        static void ConfigurePlayer(string product)
        {
            var nt = NamedBuildTarget.Standalone;
            PlayerSettings.companyName = "FORGE";
            PlayerSettings.productName = product;
            PlayerSettings.SetScriptingBackend(nt, ScriptingImplementation.IL2CPP);
            PlayerSettings.SetIl2CppCompilerConfiguration(nt, Il2CppCompilerConfiguration.Release);
            PlayerSettings.SetManagedStrippingLevel(nt, ManagedStrippingLevel.Low);
            PlayerSettings.SetApiCompatibilityLevel(nt, ApiCompatibilityLevel.NET_Standard);
            PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.StandaloneWindows64, false);
            // Both APIs are included; the harness selects one explicitly per session with -force-d3d11/-force-d3d12.
            PlayerSettings.SetGraphicsAPIs(BuildTarget.StandaloneWindows64, new[] { GraphicsDeviceType.Direct3D12, GraphicsDeviceType.Direct3D11 });
            PlayerSettings.enableFrameTimingStats = true;
            PlayerSettings.gcIncremental = true;
            PlayerSettings.runInBackground = true;
            PlayerSettings.visibleInBackground = true;
            PlayerSettings.fullScreenMode = FullScreenMode.Windowed;
            PlayerSettings.defaultScreenWidth = 1280;
            PlayerSettings.defaultScreenHeight = 720;
            PlayerSettings.resizableWindow = false;
            PlayerSettings.usePlayerLog = true;
            PlayerSettings.SplashScreen.show = false;
            PlayerSettings.colorSpace = ColorSpace.Linear;
            PlayerSettings.insecureHttpOption = InsecureHttpOption.AlwaysAllowed;   // stub is http://127.0.0.1
        }

        static void CreateAssetSet()
        {
            Directory.CreateDirectory(ResourcesDir);
            Directory.CreateDirectory(BenchDir + "/Materials");
            string path = ResourcesDir + "/BenchAssetSet.asset";
            var set = AssetDatabase.LoadAssetAtPath<BenchAssetSet>(path);
            if (set == null)
            {
                set = ScriptableObject.CreateInstance<BenchAssetSet>();
                AssetDatabase.CreateAsset(set, path);
            }
            Mesh M(PrimitiveType t)
            {
                var go = GameObject.CreatePrimitive(t);
                var m = go.GetComponent<MeshFilter>().sharedMesh;
                UnityEngine.Object.DestroyImmediate(go);
                return m;
            }
            set.sphere = M(PrimitiveType.Sphere);
            set.capsule = M(PrimitiveType.Capsule);
            set.cylinder = M(PrimitiveType.Cylinder);
            set.cube = M(PrimitiveType.Cube);
            set.plane = M(PrimitiveType.Plane);
            var lit = Shader.Find("Universal Render Pipeline/Lit");
            var names = Enum.GetNames(typeof(BenchAssetSet.MaterialId));
            set.materials = new Material[names.Length];
            for (int i = 0; i < names.Length; i++)
            {
                string mp = $"{BenchDir}/Materials/{names[i]}.mat";
                var mat = AssetDatabase.LoadAssetAtPath<Material>(mp);
                if (mat == null) { mat = new Material(lit); AssetDatabase.CreateAsset(mat, mp); }
                mat.shader = lit;
                mat.SetColor("_BaseColor", BenchAssetSet.Colors[i]);
                mat.SetFloat("_Smoothness", 0.2f);
                mat.enableInstancing = true;
                EditorUtility.SetDirty(mat);
                set.materials[i] = mat;
            }
            // Load by path: TMP_Settings is not initialised in the same session that imports TMP Essential Resources.
            set.font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/TextMesh Pro/Resources/Fonts & Materials/LiberationSans SDF.asset");
            if (set.font == null) throw new Exception("TMP font asset missing; rerun setup after TMP Essential Resources import");
            EditorUtility.SetDirty(set);
        }

        static void CreateScene(Type boot)
        {
            Directory.CreateDirectory("Assets/Scenes");
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var go = new GameObject("GameBoot");
            go.AddComponent(boot);
            EditorSceneManager.SaveScene(scene, ScenePath);
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
        }
    }
}
