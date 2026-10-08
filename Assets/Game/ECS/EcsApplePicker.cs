using Forge.Bench;
using Forge.Bench.GenAI;
using Forge.Bench.Labels;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Rendering;
using Unity.Transforms;
using UnityEngine;
using UnityEngine.Jobs;
using UnityEngine.Rendering;

namespace Game.AP.ECS
{
    // ------------------------------------------------------------------ bootstrap

    /// <summary>
    /// Creates the default world only for ECS / ECS_ST runs. OOP runs get no Entities world at all, so no ECS
    /// systems (transforms, Entities Graphics, ...) execute in OOP measurements (PROGRESS implementation note).
    /// </summary>
    public class APEcsBootstrap : ICustomBootstrap
    {
        public bool Initialize(string defaultWorldName)
        {
            var cfg = BenchConfig.Load();
            if (!cfg.IsEcs) return true;
            var world = new World(defaultWorldName, WorldFlags.Game);
            World.DefaultGameObjectInjectionWorld = world;
            var systems = DefaultWorldInitialization.GetAllSystems(WorldSystemFilterFlags.Default);
            DefaultWorldInitialization.AddSystemsToRootLevelSystemGroups(world, systems);
            ScriptBehaviourUpdateOrder.AppendWorldToCurrentPlayerLoop(world);
            return true;
        }
    }

    // ------------------------------------------------------------------ components

    public struct APConfig : IComponentData
    {
        public int Rate;
        public bool SingleThread;
        public bool GenAI;
        public int AgeMin, AgeMax;
    }

    public struct StepCounter : IComponentData { public long K; }
    public struct ApplePrefab : IComponentData { public Entity Value; }
    public struct TreeTag : IComponentData { }
    public struct BasketTag : IComponentData { }

    public struct AppleData : IComponentData
    {
        public int SpawnIdx;
        public long KSpawn;
        public double TSpawn;
        public float X;
    }

    // ------------------------------------------------------------------ builder

    public static class EcsBuilder
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Register() => APBoot.BuildEcs = Build;

        public static EcsLabelBinder Binder;

        static ILabelBinder Build(BenchConfig cfg)
        {
            var world = World.DefaultGameObjectInjectionWorld;
            var em = world.EntityManager;
            var a = BenchAssetSet.Get();
            world.GetExistingSystemManaged<FixedStepSimulationSystemGroup>().Timestep = APSpec.Dt;

            var rma = new RenderMeshArray(
                new[] { a.Mat(BenchAssetSet.MaterialId.Apple), a.Mat(BenchAssetSet.MaterialId.Wood),
                        a.Mat(BenchAssetSet.MaterialId.Leaves), a.Mat(BenchAssetSet.MaterialId.Basket) },
                new[] { a.sphere, a.cylinder, a.cube });
            var desc = new RenderMeshDescription(ShadowCastingMode.Off, receiveShadows: false);

            Entity Renderable(int mat, int mesh, float3 pos, float3 scale)
            {
                var e = em.CreateEntity();
                RenderMeshUtility.AddComponents(e, em, desc, rma, MaterialMeshInfo.FromRenderMeshArrayIndices(mat, mesh));
                em.AddComponentData(e, LocalTransform.FromPosition(pos));
                if (!scale.Equals(new float3(1)))
                    em.AddComponentData(e, new PostTransformMatrix { Value = float4x4.Scale(scale) });
                return e;
            }

            var prefab = Renderable(0, 0, new float3(0, APSpec.TreeY, 0), new float3(1));
            em.AddComponentData(prefab, new AppleData());
            em.AddComponent<Prefab>(prefab);

            var trunk = Renderable(1, 1, new float3(0, APSpec.TreeY - 1.5f, 0), new float3(1, 2, 1));
            var leaves = Renderable(2, 0, new float3(0, APSpec.TreeY + 1.5f, 0), new float3(5, 4, 5));
            em.AddComponent<TreeTag>(trunk);
            em.AddComponent<TreeTag>(leaves);
            var basket = Renderable(3, 2, new float3(0, APSpec.BasketY, 0), new float3(4, 1, 1));
            em.AddComponent<BasketTag>(basket);

            var single = em.CreateEntity();
            em.AddComponentData(single, new APConfig
            {
                Rate = cfg.load, SingleThread = cfg.EcsSingleThread, GenAI = cfg.GenAIOn,
                AgeMin = APSpec.CatchAgeMin, AgeMax = APSpec.CatchAgeMax,
            });
            em.AddComponentData(single, new StepCounter { K = 0 });
            em.AddComponentData(single, new ApplePrefab { Value = prefab });

            Binder = new EcsLabelBinder(em);
            return Binder;
        }
    }

    // ------------------------------------------------------------------ fixed-step systems (50 Hz)

    /// <summary>Lifetime and basket-catch test for existing apples; runs before the spawner in each fixed step.</summary>
    [UpdateInGroup(typeof(FixedStepSimulationSystemGroup))]
    public partial struct AppleFixedSystem : ISystem
    {
        NativeQueue<int2> _despawned;   // (spawnIdx, hit)

        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<APConfig>();
            _despawned = new NativeQueue<int2>(Allocator.Persistent);
        }

        public void OnDestroy(ref SystemState state) => _despawned.Dispose();

        [BurstCompile]
        partial struct CheckJob : IJobEntity
        {
            public long K;
            public float BasketX;
            public int AgeMin, AgeMax, Lifetime;
            public EntityCommandBuffer.ParallelWriter Ecb;
            public NativeQueue<int2>.ParallelWriter Out;

            void Execute(Entity e, [ChunkIndexInQuery] int chunk, in AppleData a)
            {
                long age = K - a.KSpawn;
                bool dead = age >= Lifetime;
                bool hit = !dead && APMath.Caught(age, a.X, BasketX, AgeMin, AgeMax);
                if (dead || hit)
                {
                    Ecb.DestroyEntity(chunk, e);
                    Out.Enqueue(new int2(a.SpawnIdx, hit ? 1 : 0));
                }
            }
        }

        public void OnUpdate(ref SystemState state)
        {
            var cfg = SystemAPI.GetSingleton<APConfig>();
            long k = SystemAPI.GetSingleton<StepCounter>().K;
            var ecb = new EntityCommandBuffer(Allocator.TempJob);
            var job = new CheckJob
            {
                K = k, BasketX = APSpec.BasketX(k * (double)APSpec.Dt),
                AgeMin = cfg.AgeMin, AgeMax = cfg.AgeMax, Lifetime = APSpec.LifetimeSteps,
                Ecb = ecb.AsParallelWriter(), Out = _despawned.AsParallelWriter(),
            };
            if (cfg.SingleThread) job.Run();
            else job.ScheduleParallel();
            state.Dependency.Complete();
            ecb.Playback(state.EntityManager);
            ecb.Dispose();

            int n = 0, hits = 0;
            var binder = EcsBuilder.Binder;
            var genai = cfg.GenAI ? GenAIService.Instance : null;
            while (_despawned.TryDequeue(out var d))
            {
                n++; hits += d.y;
                if (cfg.GenAI && binder.Live.Remove(d.x)) genai?.OnDespawn(d.x);
            }
            Fingerprint.OnDespawn(n);
            Fingerprint.Hits += hits;
        }
    }

    /// <summary>Tree movement and apple spawning (batched EntityManager.Instantiate + parallel init job).</summary>
    [UpdateInGroup(typeof(FixedStepSimulationSystemGroup))]
    [UpdateAfter(typeof(AppleFixedSystem))]
    public partial struct TreeSpawnSystem : ISystem
    {
        int _nextSpawn;

        public void OnCreate(ref SystemState state) => state.RequireForUpdate<APConfig>();

        [BurstCompile]
        struct InitJob : IJobParallelFor
        {
            [ReadOnly] public NativeArray<Entity> Entities;
            [NativeDisableParallelForRestriction] public ComponentLookup<AppleData> Data;
            [NativeDisableParallelForRestriction] public ComponentLookup<LocalTransform> Xform;
            public int FirstIdx; public long K; public double T; public float X;

            public void Execute(int i)
            {
                var e = Entities[i];
                Data[e] = new AppleData { SpawnIdx = FirstIdx + i, KSpawn = K, TSpawn = T, X = X };
                Xform[e] = LocalTransform.FromPosition(new float3(X, APSpec.TreeY, 0));
            }
        }

        public void OnUpdate(ref SystemState state)
        {
            var cfg = SystemAPI.GetSingleton<APConfig>();
            var counter = SystemAPI.GetSingletonRW<StepCounter>();
            long k = counter.ValueRO.K;
            float x = TreePath.X(k);
            foreach (var t in SystemAPI.Query<RefRW<LocalTransform>>().WithAll<TreeTag>())
                t.ValueRW.Position.x = x;

            var phase = RunController.CurrentPhase;
            if (phase >= RunPhase.Ramp && phase < RunPhase.Done)
            {
                int n = BenchRng.EventsInStep(k, cfg.Rate, APSpec.FixedHz);
                if (n > 0)
                {
                    var prefab = SystemAPI.GetSingleton<ApplePrefab>().Value;
                    var ents = state.EntityManager.Instantiate(prefab, n, Allocator.TempJob);
                    var job = new InitJob
                    {
                        Entities = ents, Data = SystemAPI.GetComponentLookup<AppleData>(),
                        Xform = SystemAPI.GetComponentLookup<LocalTransform>(),
                        FirstIdx = _nextSpawn, K = k, T = SystemAPI.Time.ElapsedTime, X = x,
                    };
                    if (cfg.SingleThread) job.Run(n);
                    else job.Schedule(n, 64, state.Dependency).Complete();
                    if (cfg.GenAI)
                    {
                        var binder = EcsBuilder.Binder;
                        var genai = GenAIService.Instance;
                        for (int i = 0; i < n; i++)
                        {
                            binder.Live[_nextSpawn + i] = ents[i];
                            genai?.OnSpawn(_nextSpawn + i);
                        }
                    }
                    ents.Dispose();
                    _nextSpawn += n;
                }
                Fingerprint.OnSpawn(n);
                GenAIService.Instance?.OnFixedStep(k);
            }
            counter.ValueRW.K = k + 1;
            FrameRecorder.FixedStepsTotal = k + 1;
        }
    }

    // ------------------------------------------------------------------ per-frame systems

    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateBefore(typeof(TransformSystemGroup))]
    public partial struct AppleMoveSystem : ISystem
    {
        public void OnCreate(ref SystemState state) => state.RequireForUpdate<APConfig>();

        [BurstCompile]
        partial struct MoveJob : IJobEntity
        {
            public double T;
            void Execute(ref LocalTransform t, in AppleData a) =>
                t.Position = new float3(a.X, APSpec.TreeY - APSpec.FallSpeed * (float)(T - a.TSpawn), 0);
        }

        public void OnUpdate(ref SystemState state)
        {
            var cfg = SystemAPI.GetSingleton<APConfig>();
            var job = new MoveJob { T = SystemAPI.Time.ElapsedTime };
            if (cfg.SingleThread) job.Run();
            else job.ScheduleParallel();

            float bx = APSpec.BasketX(SystemAPI.Time.ElapsedTime);
            foreach (var t in SystemAPI.Query<RefRW<LocalTransform>>().WithAll<BasketTag>())
                t.ValueRW.Position.x = bx;
        }
    }

    /// <summary>
    /// ECS label binding (variant-differences B2): shared pooled TMP labels are positioned from the owner entity's
    /// LocalToWorld by an IJobParallelForTransform over a TransformAccessArray, after transforms are computed.
    /// </summary>
    public sealed class EcsLabelBinder : ILabelBinder
    {
        public readonly System.Collections.Generic.Dictionary<int, Entity> Live = new();
        public TransformAccessArray Transforms = new(256);
        public NativeList<Entity> Owners = new(256, Allocator.Persistent);
        readonly System.Collections.Generic.Dictionary<int, int> _slot = new();
        readonly System.Collections.Generic.List<int> _slotSpawn = new();
        readonly EntityManager _em;

        public EcsLabelBinder(EntityManager em) { _em = em; }

        public bool IsAlive(int spawnIdx) => Live.ContainsKey(spawnIdx);

        public void Attach(int spawnIdx, Label label)
        {
            _slot[spawnIdx] = Transforms.length;
            _slotSpawn.Add(spawnIdx);
            Transforms.Add(label.T);
            Owners.Add(Live[spawnIdx]);
        }

        public void Detach(int spawnIdx, Label label)
        {
            if (!_slot.Remove(spawnIdx, out int s)) return;
            int last = Transforms.length - 1;
            Transforms.RemoveAtSwapBack(s);
            Owners.RemoveAtSwapBack(s);
            int moved = _slotSpawn[last];
            _slotSpawn[s] = moved;
            _slotSpawn.RemoveAt(last);
            if (s != last) _slot[moved] = s;
        }
    }

    [UpdateInGroup(typeof(PresentationSystemGroup))]
    public partial class LabelSyncSystem : SystemBase
    {
        [BurstCompile]
        struct SyncJob : IJobParallelForTransform
        {
            [ReadOnly] public NativeArray<Entity> Owners;
            [ReadOnly] public ComponentLookup<LocalToWorld> L2W;
            public float3 Offset;

            public void Execute(int i, TransformAccess t)
            {
                if (L2W.TryGetComponent(Owners[i], out var m)) t.position = m.Position + Offset;
            }
        }

        protected override void OnCreate() => RequireForUpdate<APConfig>();

        protected override void OnUpdate()
        {
            var b = EcsBuilder.Binder;
            if (b == null || b.Transforms.length == 0) return;
            var job = new SyncJob
            {
                Owners = b.Owners.AsArray(), L2W = GetComponentLookup<LocalToWorld>(true),
                Offset = new float3(0, APSpec.LabelOffsetY, -0.5f),
            };
            Dependency = job.Schedule(b.Transforms, Dependency);
            Dependency.Complete();
        }
    }
}
