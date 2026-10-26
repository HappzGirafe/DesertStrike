using System.Runtime.InteropServices;

/// <summary>
/// The browser version's (WebGL) link to an online room: a browser cannot send UDP, so <see cref="NetSession"/> sends
/// its packets over a WebSocket to the online server, which passes them on to the room's game host and back
/// (online-server/webrooms.py; the WebSocket itself is in Plugins/WebGL/WebLink.jslib). Anywhere but in a browser it
/// does nothing: the game uses UDP there.
/// </summary>
public class WebLink
{
    public enum LinkState { Connecting, Open, Closed }

#if UNITY_WEBGL && !UNITY_EDITOR
    [DllImport("__Internal")] static extern int WebLinkOpen(string url);
    [DllImport("__Internal")] static extern int WebLinkState(int id);
    [DllImport("__Internal")] static extern int WebLinkSend(int id, byte[] buffer, int length);
    [DllImport("__Internal")] static extern int WebLinkReceive(int id, byte[] buffer, int capacity);
    [DllImport("__Internal")] static extern void WebLinkClose(int id);
    public static bool Available => true;
#else
    static int WebLinkOpen(string url) => 0;
    static int WebLinkState(int id) => 2;
    static int WebLinkSend(int id, byte[] buffer, int length) => 0;
    static int WebLinkReceive(int id, byte[] buffer, int capacity) => 0;
    static void WebLinkClose(int id) { }
    public static bool Available => false;
#endif

    readonly int id;
    readonly byte[] buffer = new byte[2048];

    public WebLink(string url) => id = WebLinkOpen(url);

    public LinkState State => (LinkState)WebLinkState(id);

    public void Send(byte[] data) => WebLinkSend(id, data, data.Length);

    /// <summary>The next message from the game host, or null when none has come.</summary>
    public byte[] Receive()
    {
        while (true)
        {
            int length = WebLinkReceive(id, buffer, buffer.Length);
            if (length == 0) return null;
            if (length < 0) continue;   // too big to be a game packet
            var data = new byte[length];
            System.Array.Copy(buffer, data, length);
            return data;
        }
    }

    public void Close() => WebLinkClose(id);
}
