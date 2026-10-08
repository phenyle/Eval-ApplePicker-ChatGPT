using Forge.Bench.Editor;

namespace Game.AP.Editor
{
    /// <summary>Unity -batchmode -quit -projectPath repos/apple-picker -executeMethod Game.AP.Editor.APSetup.Run</summary>
    public static class APSetup
    {
        public static void Run() => BenchProjectSetup.Apply(new BenchProjectSetup.Options
        {
            ProductName = "AP",
            MainLightShadows = false,           // plan §2.7: shadows off in Apple Picker
            BootComponent = typeof(APBoot),
        });
    }
}
