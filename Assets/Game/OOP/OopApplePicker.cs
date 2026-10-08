using System.Collections.Generic;
using Forge.Bench;
using Forge.Bench.GenAI;
using Forge.Bench.Labels;
using UnityEngine;

namespace Game.AP.OOP
{
    /// <summary>
    /// OOP variant (plan §2.1): one GameObject per apple with a per-object MonoBehaviour; Instantiate/Destroy
    /// (arch OOP) or SetActive pooling (arch OOP_POOL). Builder registered with APBoot at load time.
    /// </summary>
    public static class OopBuilder
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Register() => APBoot.BuildOop = Build;

        static ILabelBinder Build(BenchConfig cfg)
        {
            var a = BenchAssetSet.Get();

            var template = new GameObject("ApplePrefab");
            template.SetActive(false);
            template.AddComponent<MeshFilter>().sharedMesh = a.sphere;
            var mr = template.AddComponent<MeshRenderer>();
            mr.sharedMaterial = a.Mat(BenchAssetSet.MaterialId.Apple);
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
            template.AddComponent<Apple>();

            var tree = new GameObject("Tree");
            Part(tree.transform, a.cylinder, a.Mat(BenchAssetSet.MaterialId.Wood), new Vector3(0, -1.5f, 0), new Vector3(1, 2, 1));
            Part(tree.transform, a.sphere, a.Mat(BenchAssetSet.MaterialId.Leaves), new Vector3(0, 1.5f, 0), new Vector3(5, 4, 5));
            var spawner = tree.AddComponent<AppleTree>();
            spawner.Init(cfg, template);

            var basket = new GameObject("Basket");
            Part(basket.transform, a.cube, a.Mat(BenchAssetSet.MaterialId.Basket), Vector3.zero, new Vector3(4, 1, 1));
            basket.AddComponent<Basket>();

            return Apple.Binder = new OopLabelBinder();
        }

        static void Part(Transform parent, Mesh mesh, Material mat, Vector3 pos, Vector3 scale)
        {
            var go = new GameObject("part");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            go.transform.localScale = scale;
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = mat;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }
    }

    /// <summary>The tree: advances along the deterministic path and spawns apples on the fixed clock.</summary>
    public class AppleTree : MonoBehaviour
    {
        /// <summary>Number of completed fixed steps; apples read it to compute their age.</summary>
        public static long StepCount;
        public static float BasketXAtStep;

        BenchConfig _cfg;
        GameObject _template;
        readonly Stack<Apple> _pool = new();
        bool _pooled, _genai;
        int _nextSpawn;

        public void Init(BenchConfig cfg, GameObject template)
        {
            _cfg = cfg; _template = template; _pooled = cfg.OopPooled; _genai = cfg.GenAIOn;
            StepCount = 0;
        }

        void FixedUpdate()
        {
            long k = StepCount;
            float x = TreePath.X(k);
            transform.position = new Vector3(x, APSpec.TreeY, 0);
            if (RunController.CurrentPhase >= RunPhase.Ramp && RunController.CurrentPhase < RunPhase.Done)
            {
                int n = BenchRng.EventsInStep(k, _cfg.load, APSpec.FixedHz);
                double tNow = Time.fixedTimeAsDouble;
                for (int i = 0; i < n; i++)
                {
                    var apple = Get();
                    apple.Launch(this, _nextSpawn, k, tNow, x);
                    if (_genai) { Apple.Binder.Live[_nextSpawn] = apple; GenAIService.Instance?.OnSpawn(_nextSpawn); }
                    _nextSpawn++;
                }
                Fingerprint.OnSpawn(n);
                GenAIService.Instance?.OnFixedStep(k);
            }
            StepCount = k + 1;
            BasketXAtStep = APSpec.BasketX(StepCount * (double)APSpec.Dt);   // basket position for next step's catch test
            FrameRecorder.FixedStepsTotal = StepCount;
        }

        Apple Get()
        {
            if (_pooled && _pool.Count > 0)
            {
                var p = _pool.Pop();
                p.gameObject.SetActive(true);
                return p;
            }
            var go = Instantiate(_template);
            go.SetActive(true);
            return go.GetComponent<Apple>();
        }

        public void Release(Apple a)
        {
            if (_pooled) { a.gameObject.SetActive(false); _pool.Push(a); }
            else Destroy(a.gameObject);
        }
    }

    /// <summary>One apple. Update: analytic fall. FixedUpdate: lifetime and basket catch (runs before the tree).</summary>
    [DefaultExecutionOrder(-100)]
    public class Apple : MonoBehaviour
    {
        public static OopLabelBinder Binder;
        AppleTree _tree;
        int _spawnIdx;
        long _kSpawn;
        double _tSpawn;
        float _x;

        public void Launch(AppleTree tree, int spawnIdx, long k, double t, float x)
        {
            _tree = tree; _spawnIdx = spawnIdx; _kSpawn = k; _tSpawn = t; _x = x;
            transform.position = new Vector3(x, APSpec.TreeY, 0);
        }

        void Update()
        {
            transform.position = new Vector3(_x, APSpec.AppleY(Time.timeAsDouble - _tSpawn), 0);
        }

        void FixedUpdate()
        {
            long k = AppleTree.StepCount;
            long age = k - _kSpawn;
            if (age >= APSpec.LifetimeSteps) Despawn(false);
            else if (APMath.Caught(age, _x, AppleTree.BasketXAtStep, APSpec.CatchAgeMin, APSpec.CatchAgeMax)) Despawn(true);
        }

        void Despawn(bool hit)
        {
            Fingerprint.OnDespawn(1);
            if (hit) Fingerprint.Hits++;
            if (Binder != null && Binder.Live.Remove(_spawnIdx)) GenAIService.Instance?.OnDespawn(_spawnIdx);
            _tree.Release(this);
        }
    }

    public class Basket : MonoBehaviour
    {
        void Update() => transform.position = new Vector3(APSpec.BasketX(Time.timeAsDouble), APSpec.BasketY, 0);
    }

    /// <summary>OOP label binding (variant-differences B2): each label follows its apple's Transform in LateUpdate.</summary>
    public sealed class OopLabelBinder : ILabelBinder
    {
        public readonly Dictionary<int, Apple> Live = new();

        public bool IsAlive(int spawnIdx) => Live.ContainsKey(spawnIdx);

        public void Attach(int spawnIdx, Label label)
        {
            var f = label.Go.GetComponent<LabelFollow>() ?? label.Go.AddComponent<LabelFollow>();
            f.Target = Live[spawnIdx].transform;
            f.Offset = new Vector3(0, APSpec.LabelOffsetY, -0.5f);
            f.LateUpdate();
        }

        public void Detach(int spawnIdx, Label label)
        {
            var f = label.Go.GetComponent<LabelFollow>();
            if (f != null) f.Target = null;
        }
    }

    public class LabelFollow : MonoBehaviour
    {
        public Transform Target;
        public Vector3 Offset;

        public void LateUpdate()
        {
            if (Target != null) transform.position = Target.position + Offset;
        }
    }
}
