namespace Forge.Bench
{
    /// <summary>
    /// Workload fingerprint counters (plan §2.8, SCHEMA.md frames columns). Written only from the main
    /// thread by both OOP and ECS code paths; ECS systems aggregate job results on the main thread.
    /// </summary>
    public static class Fingerprint
    {
        public static int Live;
        public static long Spawned;
        public static long Despawned;
        public static long Hits;

        public static long ReqScheduled;
        public static long ReqSent;
        public static long ReqDone;
        public static long ReqApplied;
        public static long ReqDropped;
        public static int Inflight;
        public static int QueueLen;
        public static int LabelsVisible;
        public static int AppliedThisFrame;
        public static long SentLive;

        public static int NetTick = -1;
        public static int NetTickEst = -1;

        public static void OnSpawn(int n) { Spawned += n; Live += n; }
        public static void OnDespawn(int n) { Despawned += n; Live -= n; }
    }
}
