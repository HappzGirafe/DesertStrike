using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.Profiling;
using UnityEditorInternal;
using UnityEngine;

/// <summary>
/// Summarises a profiler recording made by a development build started with -ds-profile &lt;file&gt;:
/// average milliseconds per frame for each part of the frame (main and render thread), and the markers
/// that cost the most on their own. Run in batch mode:
///   Unity -batchmode -quit -projectPath . -executeMethod ProfileReport.Analyze -profileFile Logs/prof.raw
/// The report is written next to the recording as a .txt file.
/// </summary>
public static class ProfileReport
{
    const int MaxDepth = 6;

    public static void Analyze()
    {
        var args = Environment.GetCommandLineArgs();
        int index = Array.IndexOf(args, "-profileFile");
        string file = index >= 0 && index + 1 < args.Length ? args[index + 1] : "Logs/prof.raw";
        if (!ProfilerDriver.LoadProfile(file, false))
        {
            Debug.LogError("[ProfileReport] Could not load " + file);
            return;
        }

        int first = ProfilerDriver.firstFrameIndex, last = ProfilerDriver.lastFrameIndex;
        int frames = 0;
        float frameMs = 0f;
        var report = new StringBuilder();
        var threads = new Dictionary<string, int>();
        for (int t = 0; t < 64; t++)
            using (var raw = ProfilerDriver.GetRawFrameDataView(first, t))
            {
                if (raw == null || !raw.valid) break;
                if (raw.threadName == "Main Thread" || raw.threadName == "Render Thread") threads[raw.threadName] = t;
            }

        foreach (var thread in threads)
        {
            var totals = new Dictionary<string, float>();
            var selfTimes = new Dictionary<string, float>();
            frames = 0;
            frameMs = 0f;
            for (int frame = first; frame <= last; frame++)
            {
                using (var view = ProfilerDriver.GetHierarchyFrameDataView(frame, thread.Value,
                           HierarchyFrameDataView.ViewModes.MergeSamplesWithTheSameName, HierarchyFrameDataView.columnTotalTime, false))
                {
                    if (view == null || !view.valid) continue;
                    frames++;
                    frameMs += view.frameTimeMs;
                    Walk(view, view.GetRootItemID(), "", 0, totals, selfTimes);
                }
            }
            if (frames == 0) continue;

            report.AppendLine($"=== {thread.Key}: {frames} frames, {frameMs / frames:0.00} ms per frame ({1000f * frames / frameMs:0} fps)");
            report.AppendLine("--- Total time per part of the frame (ms per frame, nested)");
            foreach (var pair in totals.Where(p => p.Value / frames >= 0.02f).OrderBy(p => p.Key))
                report.AppendLine($"{pair.Value / frames,8:0.000}  {pair.Key}");
            report.AppendLine("--- Costliest markers on their own (self time, ms per frame)");
            foreach (var pair in selfTimes.OrderByDescending(p => p.Value).Take(40))
                report.AppendLine($"{pair.Value / frames,8:0.000}  {pair.Key}");
            report.AppendLine();
        }

        string output = Path.ChangeExtension(file, ".txt");
        File.WriteAllText(output, report.ToString());
        Debug.Log("[ProfileReport] Wrote " + output + "\n" + report);
    }

    static void Walk(HierarchyFrameDataView view, int id, string path, int depth,
                     Dictionary<string, float> totals, Dictionary<string, float> selfTimes)
    {
        var children = new List<int>();
        view.GetItemChildren(id, children);
        foreach (int child in children)
        {
            string name = view.GetItemName(child);
            float self = view.GetItemColumnDataAsFloat(child, HierarchyFrameDataView.columnSelfTime);
            selfTimes.TryGetValue(name, out float s);
            selfTimes[name] = s + self;

            string childPath = path.Length == 0 ? name : path + " / " + name;
            if (depth < MaxDepth)
            {
                float total = view.GetItemColumnDataAsFloat(child, HierarchyFrameDataView.columnTotalTime);
                totals.TryGetValue(childPath, out float t);
                totals[childPath] = t + total;
            }
            Walk(view, child, childPath, depth + 1, totals, selfTimes);
        }
    }
}
