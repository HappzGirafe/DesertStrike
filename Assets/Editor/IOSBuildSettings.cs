#if UNITY_IOS
using System.IO;
using UnityEditor;
using UnityEditor.Callbacks;
using UnityEditor.iOS.Xcode;

/// <summary>
/// After an iOS build: the text iOS shows when the game first uses the local network (LAN games, friends online),
/// and full screen only (no iPad split view, which would squash the controls).
/// </summary>
public static class IOSBuildSettings
{
    [PostProcessBuild]
    public static void AddPlistKeys(BuildTarget target, string path)
    {
        if (target != BuildTarget.iOS) return;
        string plistPath = Path.Combine(path, "Info.plist");
        var plist = new PlistDocument();
        plist.ReadFromFile(plistPath);
        plist.root.SetString("NSLocalNetworkUsageDescription",
            "Low Strike looks for LAN games and shows which friends are online on your WiFi.");
        plist.root.SetBoolean("UIRequiresFullScreen", true);
        plist.WriteToFile(plistPath);
    }
}
#endif
