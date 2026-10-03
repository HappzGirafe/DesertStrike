using System.Runtime.InteropServices;
using Unity.Profiling;
using UnityEngine;

/// <summary>
/// Frame statistics for the FPS counter and the -ds-perf log: frames per second, how long the CPU and the
/// graphics chip work on a frame (whichever is near 1000 / FPS is the one holding the game back), and the
/// draw-call counts.
/// </summary>
public class PerfStats : MonoBehaviour
{
    public static float Fps { get; private set; }
    public static float CpuMs { get; private set; }
    public static float GpuMs { get; private set; }
    public static int Batches { get; private set; }
    public static int SetPassCalls { get; private set; }
    public static int Triangles { get; private set; }

    /// <summary>"1440x900 at 75%, Apple M2, arm64" — what the game is running on.</summary>
    public static string Device { get; private set; } = "";

    readonly FrameTiming[] timings = new FrameTiming[1];
    ProfilerRecorder batchesRecorder, setPassRecorder, trianglesRecorder;
    int frames;
    float elapsed, cpuSum, gpuSum;
    int cpuCount, gpuCount;

    void OnEnable()
    {
        batchesRecorder = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Batches Count");
        setPassRecorder = ProfilerRecorder.StartNew(ProfilerCategory.Render, "SetPass Calls Count");
        trianglesRecorder = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Triangles Count");
    }

    void OnDisable()
    {
        batchesRecorder.Dispose();
        setPassRecorder.Dispose();
        trianglesRecorder.Dispose();
    }

    void Update()
    {
        FrameTimingManager.CaptureFrameTimings();
        if (FrameTimingManager.GetLatestTimings(1, timings) > 0)
        {
            if (timings[0].cpuMainThreadFrameTime > 0) { cpuSum += (float)timings[0].cpuMainThreadFrameTime; cpuCount++; }
            if (timings[0].gpuFrameTime > 0) { gpuSum += (float)timings[0].gpuFrameTime; gpuCount++; }
        }

        frames++;
        elapsed += Time.unscaledDeltaTime;
        if (elapsed < 0.5f) return;

        Fps = frames / elapsed;
        CpuMs = cpuCount > 0 ? cpuSum / cpuCount : 0f;
        GpuMs = gpuCount > 0 ? gpuSum / gpuCount : 0f;
        Batches = (int)batchesRecorder.LastValue;
        SetPassCalls = (int)setPassRecorder.LastValue;
        Triangles = (int)trianglesRecorder.LastValue;
        Device = $"{Screen.width}x{Screen.height} at {Mathf.RoundToInt(GameSettings.RenderScale * 100f)}%, " +
                 $"{SystemInfo.graphicsDeviceName}, {RuntimeInformation.ProcessArchitecture.ToString().ToLowerInvariant()}";
        frames = cpuCount = gpuCount = 0;
        elapsed = cpuSum = gpuSum = 0f;
    }
}
