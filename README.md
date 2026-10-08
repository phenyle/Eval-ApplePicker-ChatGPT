# Apple Picker benchmark (Unity 6000.6.4f1)

Benchmark re-implementation of the Apple Picker prototype with workload-equivalent OOP and ECS variants,
used for the FORGE 2027 study. One executable; the architecture (OOP, OOP_POOL, ECS, ECS_ST), load,
and GenAI mode are chosen at runtime from a JSON config passed with `-bench-config <path>`.

- `Assets/Game/Shared` — specification shared by every variant (fixed 50 Hz clock, deterministic tree path,
  analytic apple fall, basket, catch test), scene boot.
- `Assets/Game/OOP` — GameObjects with per-object MonoBehaviours (Instantiate/Destroy or pooled).
- `Assets/Game/ECS` — Entities: runtime-built prefab, Burst `IJobEntity` (parallel or `.Run()`), batched
  instantiation, label sync via `IJobParallelForTransform`.
- `Packages/com.forge.bench` — shared harness: run control, frame recorder, deterministic RNG, workload
  fingerprint, GenAI client/scheduler, label pool, project setup and build scripts.

Setup and build (batch mode):

```
Unity -batchmode -quit -projectPath . -importPackage "<Editor>/Data/Resources/PackageManager/BuiltInPackages/com.unity.ugui/Package Resources/TMP Essential Resources.unitypackage"
Unity -batchmode -quit -projectPath . -executeMethod Game.AP.Editor.APSetup.Run
Unity -batchmode -quit -projectPath . -executeMethod Forge.Bench.Editor.BenchBuild.Build -buildOut <dir> -buildName AP
```

The original 2022.3 prototype is on branch `main`.
