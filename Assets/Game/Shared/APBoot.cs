using System;
using Forge.Bench;
using Forge.Bench.GenAI;
using Forge.Bench.Labels;
using UnityEngine;

namespace Game.AP
{
    /// <summary>
    /// Scene entry point (the only object in Bench.unity). Builds the parts that are identical for every
    /// variant (camera, light), then delegates to the OOP or ECS builder registered by those assemblies,
    /// wires the shared GenAI service and label pool, and marks the run ready.
    /// </summary>
    public class APBoot : MonoBehaviour
    {
        /// <summary>Set by Game.AP.OOP / Game.AP.ECS at load; returns the variant's label binder.</summary>
        public static Func<BenchConfig, ILabelBinder> BuildOop, BuildEcs;

        void Start()
        {
            var cfg = BenchConfig.Load();
            TreePath.Init(cfg.seed);

            var cam = new GameObject("Camera").AddComponent<Camera>();
            cam.orthographic = true;
            cam.orthographicSize = APSpec.OrthoSize;
            cam.transform.position = new Vector3(0, 0, -10);
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.55f, 0.75f, 0.95f);
            var light = new GameObject("Sun").AddComponent<Light>();
            light.type = LightType.Directional;
            light.shadows = LightShadows.None;
            light.transform.rotation = Quaternion.Euler(50, -30, 0);

            var build = cfg.IsEcs ? BuildEcs : BuildOop;
            if (build == null) { RunController.Fail($"no builder registered for arch {cfg.arch}"); return; }
            var binder = build(cfg);

            if (cfg.GenAIOn)
            {
                LabelPool.Init(binder, BenchAssetSet.Get().font, APSpec.LabelFontSize);
                GenAIService.InitIfEnabled(double.PositiveInfinity);   // AP labels last until the apple despawns
            }
            RunController.Extra["game"] = "AP";
            RunController.MarkReady();
        }
    }
}
