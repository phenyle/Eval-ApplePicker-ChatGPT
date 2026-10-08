using TMPro;
using UnityEngine;

namespace Forge.Bench
{
    /// <summary>
    /// Shared render assets (plan D11: built-in primitives only). Created by BenchProjectSetup in the editor and
    /// loaded at runtime from Resources so that OOP and ECS variants use the very same Mesh/Material objects.
    /// </summary>
    public class BenchAssetSet : ScriptableObject
    {
        public Mesh sphere, capsule, cylinder, cube, plane;
        public Material[] materials;   // index by MaterialId
        public TMP_FontAsset font;

        public enum MaterialId { Apple = 0, Leaves = 1, Wood = 2, Ground = 3, Skin = 4, Cloth = 5, Dark = 6, Car = 7, Car2 = 8, Wall = 9, Basket = 10 }
        public static readonly Color[] Colors =
        {
            new(0.80f, 0.10f, 0.10f), new(0.20f, 0.55f, 0.20f), new(0.45f, 0.30f, 0.15f), new(0.35f, 0.42f, 0.30f),
            new(0.45f, 0.65f, 0.40f), new(0.30f, 0.30f, 0.55f), new(0.15f, 0.15f, 0.15f), new(0.85f, 0.65f, 0.10f),
            new(0.10f, 0.55f, 0.85f), new(0.55f, 0.55f, 0.55f), new(0.70f, 0.55f, 0.30f),
        };

        static BenchAssetSet _instance;
        public static BenchAssetSet Get()
        {
            if (_instance == null) _instance = Resources.Load<BenchAssetSet>("BenchAssetSet");
            return _instance;
        }

        public Material Mat(MaterialId id) => materials[(int)id];
    }
}
