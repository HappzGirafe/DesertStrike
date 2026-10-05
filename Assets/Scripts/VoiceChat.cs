using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Voice chat for LAN games: hold V during a match to talk (or, with Open mic, just speak); everyone in the game
/// hears you.
///
/// The microphone is recorded at 16 kHz, cut into 20 ms frames, squeezed to 8 bits per sample (G.711 mu-law,
/// 16 KB/s, plenty for a LAN) and sent through <see cref="NetSession"/>: a client sends to the host, the host plays
/// it and passes it on to the other clients.
///
/// The microphone starts the first time V is pressed in a match (or with TEST MICROPHONE in Settings): the game
/// asks for permission (macOS shows its question then), tries the default microphone and then each one by name,
/// at 16 kHz and at the device's own rate, and checks that real sound arrives. When something is wrong,
/// <see cref="Problem"/> says what and how to fix it, and the HUD and Settings show it. During a LAN game the
/// microphone stays on (no delay on the next V); otherwise it closes shortly after use.
/// </summary>
public class VoiceChat : MonoBehaviour
{
    public const int SampleRate = 16000;
    public const KeyCode TalkKey = KeyCode.V;
    const int FrameSamples = SampleRate / 50;   // 20 ms per packet

    public enum MicState { Off, Starting, On, Failed }

    public static VoiceChat Instance { get; private set; }

    /// <summary>A LAN game with someone to talk to.</summary>
    public bool InSession { get; private set; }

    /// <summary>V is held during a match (LAN or not): the HUD shows "You" with the microphone level.</summary>
    public bool Holding { get; private set; }

    /// <summary>In a LAN game with V held, or speaking with Open mic: the voice is being sent.</summary>
    public bool Talking { get; private set; }

    /// <summary>A LAN match is running (the HUD shows the microphone's state).</summary>
    public bool InMatch { get; private set; }

    /// <summary>The microphone test in Settings is running.</summary>
    public bool Testing { get; set; }

    public MicState State { get; private set; }

    /// <summary>How loud the microphone is right now, 0 to 1.</summary>
    public float Level { get; private set; }

    /// <summary>The microphone in use.</summary>
    public string DeviceName { get; private set; } = "";

    /// <summary>What is wrong with the microphone and how to fix it, or null.</summary>
    public string Problem { get; private set; }

    GameManager gm;
    AudioClip micClip;
    string micDevice;        // null = the system's default input
    int micRate, micPosition;
    float micStartedAt, lastWanted, silencePeak, lastLoud = -10f;
    float envelope, gain = 1f;
    bool silenceChecked, wasTesting;
    long samplesRead;
    float[] chunk = new float[0];
    readonly List<float> pending = new List<float>();   // 16 kHz samples not sent yet
    float resampleCarry;
    ushort sequence;
    readonly byte[] frame = new byte[FrameSamples];

    // -ds-voice-test sends a tone instead of the microphone (testing two copies of the game on one PC);
    // -ds-voice-log logs what arrives every two seconds; -ds-mic-check runs the microphone test for 4 seconds and
    // logs only which microphone answered and how loud it was (nothing is sent, played or kept).
    bool testTone, log, micCheck, micCheckDone;
    double tonePhase;
    float nextLog;

    readonly Dictionary<string, VoiceOutput> speakers = new Dictionary<string, VoiceOutput>();

    void Awake()
    {
        Instance = this;
        gm = GetComponent<GameManager>();
        var args = Environment.GetCommandLineArgs();
        testTone = Array.IndexOf(args, "-ds-voice-test") >= 0;
        log = Array.IndexOf(args, "-ds-voice-log") >= 0;
        micCheck = Array.IndexOf(args, "-ds-mic-check") >= 0;
    }

    void Update()
    {
        var net = gm.Net;
        InSession = net.IsHost ? net.Peers.Count > 0 : net.IsClient && net.Connected;
        bool inMatch = gm.State != MatchState.Menu && !gm.IsPaused;
        InMatch = InSession && inMatch;
        bool openMic = GameSettings.VoiceChat && GameSettings.Voice == VoiceMode.OpenMic && InMatch;
        Holding = GameSettings.VoiceChat && inMatch && Input.GetKey(TalkKey);
        bool pressed = GameSettings.VoiceChat && inMatch && Input.GetKeyDown(TalkKey);
        if (micCheck && !micCheckDone) Testing = true;
        bool testStarted = Testing && !wasTesting;
        wasTesting = Testing;

        bool wantMicrophone = !testTone && (Testing || Holding || openMic);
        if (wantMicrophone)
        {
            lastWanted = Time.unscaledTime;
            if (State == MicState.Failed && (pressed || testStarted))
            {
                State = MicState.Off;   // try again on a new press of V or a new test
                Problem = null;
            }
            if (State == MicState.Off) StartCoroutine(OpenMicrophone());
        }
        else if (State == MicState.On && !InSession && Time.unscaledTime - lastWanted > 1.5f)
        {
            StopMicrophone();
        }

        if (!InSession && speakers.Count > 0)
        {
            foreach (var speaker in speakers.Values) Destroy(speaker.gameObject);
            speakers.Clear();
        }

        // Open mic sends while you speak (and 0.4 s after, so word endings are not cut off).
        bool speaking = openMic && Time.unscaledTime - lastLoud < 0.4f;
        Talking = InSession && (testTone ? GameSettings.VoiceChat && inMatch : (Holding || speaking) && State == MicState.On);
        Capture(Talking);

        if (micCheck && !micCheckDone) ReportCheck();
        if (log && Time.unscaledTime >= nextLog)
        {
            nextLog = Time.unscaledTime + 2f;
            foreach (var pair in speakers)
                Debug.Log($"[Voice] {pair.Key}: {pair.Value.Packets} packets, {pair.Value.BufferedMs:0} ms buffered, {pair.Value.Underruns} gaps");
            if (Talking) Debug.Log($"[Voice] sending, packet {sequence}");
        }
    }

    /// <summary>Names of the players whose voice is playing right now.</summary>
    public IEnumerable<string> Speaking()
    {
        foreach (var pair in speakers)
            if (Time.unscaledTime - pair.Value.LastHeard < 0.35f) yield return pair.Key;
    }

    /// <summary>One voice frame from another player (called by NetSession).</summary>
    public void Receive(string speaker, byte[] data, int offset, int count)
    {
        if (!GameSettings.VoiceChat || count <= 0) return;
        if (!speakers.TryGetValue(speaker, out var output) || output == null)
        {
            var go = new GameObject("Voice " + speaker);
            go.transform.SetParent(transform, false);
            go.AddComponent<AudioSource>();          // the AudioSource comes first, so the voice is mixed into its output
            output = go.AddComponent<VoiceOutput>();
            speakers[speaker] = output;
        }
        output.Push(data, offset, count);
    }

    IEnumerator OpenMicrophone()
    {
        State = MicState.Starting;
        Problem = null;
        if (!Application.HasUserAuthorization(UserAuthorization.Microphone))
            yield return Application.RequestUserAuthorization(UserAuthorization.Microphone);
        bool allowed = Application.HasUserAuthorization(UserAuthorization.Microphone);
        string[] devices = Microphone.devices;
        Debug.Log($"[Voice] Microphone allowed: {allowed}; microphones: {devices.Length}" +
                  (devices.Length > 0 ? " (" + string.Join(", ", devices) + ")" : ""));
        if (!allowed)
        {
            Fail("The game is not allowed to use the microphone.");
            yield break;
        }
        if (devices.Length == 0)
        {
            Fail("No microphone found.");
            yield break;
        }

        // The system's default microphone first, then each by name; at 16 kHz, then at the device's own rates.
        var candidates = new List<string> { null };
        candidates.AddRange(devices);
        foreach (string device in candidates)
        {
            Microphone.GetDeviceCaps(device, out int min, out int max);
            var rates = min == 0 && max == 0
                ? new List<int> { SampleRate, 48000, 44100 }
                : new List<int> { Mathf.Clamp(SampleRate, min, max), max };
            foreach (int rate in rates)
            {
                var clip = Microphone.Start(device, true, 1, rate);
                if (clip == null) continue;
                float until = Time.unscaledTime + 1.5f;
                while (Microphone.GetPosition(device) <= 0 && Time.unscaledTime < until) yield return null;
                if (Microphone.GetPosition(device) > 0)
                {
                    micClip = clip;
                    micDevice = device;
                    micRate = rate;
                    micPosition = Microphone.GetPosition(device);
                    resampleCarry = 0f;
                    DeviceName = device ?? devices[0];
                    micStartedAt = Time.unscaledTime;
                    silencePeak = 0f;
                    silenceChecked = false;
                    State = MicState.On;
                    Debug.Log($"[Voice] Microphone on: {(device ?? "default (" + devices[0] + ")")} at {rate} Hz, {clip.channels} channel(s)");
                    yield break;
                }
                Microphone.End(device);
                Destroy(clip);
                Debug.Log($"[Voice] {(device ?? "The default microphone")} at {rate} Hz sent nothing");
            }
        }
        Fail("The microphone does not start.");
    }

    void Fail(string what)
    {
        State = MicState.Failed;
        Level = 0f;
        Problem = what + FixHint();
        Debug.Log("[Voice] " + Problem);
    }

    static string FixHint()
    {
        switch (Application.platform)
        {
            case RuntimePlatform.OSXPlayer:
            case RuntimePlatform.OSXEditor:
                return " On a Mac: System Settings > Privacy & Security > Microphone > turn on Low Strike, then restart the game.";
            case RuntimePlatform.WindowsPlayer:
            case RuntimePlatform.WindowsEditor:
                return " On Windows: Settings > Privacy & security > Microphone > let desktop apps use the microphone.";
            default:
                return "";
        }
    }

    void StopMicrophone()
    {
        if (micClip != null)
        {
            Microphone.End(micDevice);
            Destroy(micClip);
            micClip = null;
        }
        if (State == MicState.On) State = MicState.Off;
        pending.Clear();
        Level = 0f;
    }

    void Capture(bool send)
    {
        if (testTone)
        {
            if (send)
            {
                // A tone whose pitch depends on the player's name, so two test copies are told apart.
                double hz = 330 + Math.Abs(gm.Net.PlayerName.GetHashCode() % 300);
                int count = Mathf.RoundToInt(Time.unscaledDeltaTime * SampleRate);
                for (int i = 0; i < count; i++)
                {
                    tonePhase += 2 * Math.PI * hz / SampleRate;
                    pending.Add((float)Math.Sin(tonePhase) * 0.25f);
                }
            }
        }
        else ReadMicrophone(send);

        if (!send)
        {
            pending.Clear();
            return;
        }
        while (pending.Count >= FrameSamples)
        {
            // Quiet microphones (a MacBook's is) are turned up, at most 4 times, towards a comfortable loudness.
            float peak = 0f;
            for (int i = 0; i < FrameSamples; i++) peak = Mathf.Max(peak, Mathf.Abs(pending[i]));
            envelope = Mathf.Max(peak, envelope * 0.92f);
            gain = Mathf.Lerp(gain, Mathf.Clamp(0.45f / Mathf.Max(envelope, 0.03f), 1f, 4f), 0.2f);
            for (int i = 0; i < FrameSamples; i++) frame[i] = MuLaw.Encode(Mathf.Clamp(pending[i] * gain, -1f, 1f));
            pending.RemoveRange(0, FrameSamples);
            gm.Net.SendVoice(sequence++, frame);
        }
    }

    // Everything recorded since the last call: updates the level, checks for silence, and keeps the sound
    // (as 16 kHz samples in pending) when keep is set.
    void ReadMicrophone(bool keep)
    {
        if (micClip == null || State != MicState.On) return;
        int position = Microphone.GetPosition(micDevice);
        int length = micClip.samples;
        int available = (position - micPosition + length) % length;
        int channels = micClip.channels;
        float peak = 0f;
        while (available > 0)
        {
            int count = Mathf.Min(available, length - micPosition);
            if (chunk.Length != count * channels) chunk = new float[count * channels];
            micClip.GetData(chunk, micPosition);
            for (int i = 0; i < chunk.Length; i += channels) peak = Mathf.Max(peak, Mathf.Abs(chunk[i]));
            if (keep) AddResampled(chunk, channels);
            samplesRead += count;
            micPosition = (micPosition + count) % length;
            available -= count;
        }
        Level = Mathf.Max(Mathf.Min(1f, peak), Level - Time.unscaledDeltaTime * 1.5f);
        if (peak > 0.04f) lastLoud = Time.unscaledTime;   // louder than room noise: someone speaks (Open mic)

        // A working microphone always picks up a little noise; nothing but exact zeros means the system blocks it.
        if (!silenceChecked)
        {
            silencePeak = Mathf.Max(silencePeak, peak);
            if (Time.unscaledTime - micStartedAt > 2f)
            {
                silenceChecked = true;
                if (silencePeak == 0f)
                {
                    StopMicrophone();
                    Fail("The microphone only sends silence.");
                }
            }
        }
    }

    void ReportCheck()
    {
        if (State == MicState.Failed)
        {
            micCheckDone = true;
            Testing = false;
            return;
        }
        if (State != MicState.On || Time.unscaledTime - micStartedAt < 4f) return;
        Debug.Log($"[Voice] Microphone check: {DeviceName} at {micRate} Hz, {samplesRead / (Time.unscaledTime - micStartedAt):0} samples per second, " +
                  $"level now {Level:0.000}, silence check {(silenceChecked ? "passed" : "pending")}");
        micCheckDone = true;
        Testing = false;
    }

    // Microphone samples at micRate to 16 kHz: each output sample is the average of the input samples it covers.
    void AddResampled(float[] samples, int channels)
    {
        int count = samples.Length / channels;
        if (micRate == SampleRate)
        {
            for (int i = 0; i < count; i++) pending.Add(samples[i * channels]);
            return;
        }
        float step = micRate / (float)SampleRate;
        float position = resampleCarry;
        while (position + step <= count)
        {
            int from = (int)position, to = Mathf.Min(count, (int)(position + step));
            float sum = 0f;
            for (int i = from; i < to; i++) sum += samples[i * channels];
            pending.Add(to > from ? sum / (to - from) : samples[from * channels]);
            position += step;
        }
        resampleCarry = position - count;
    }
}

/// <summary>
/// Plays one player's voice: frames go into a ring buffer and are mixed into this object's AudioSource on the audio
/// thread, resampled from 16 kHz to the output rate. Playback starts once 100 ms are buffered, which hides WiFi
/// hiccups and stutters of the sending computer without much delay.
/// </summary>
public class VoiceOutput : MonoBehaviour
{
    const int StartSamples = VoiceChat.SampleRate * 100 / 1000;
    const int CatchUpSamples = VoiceChat.SampleRate * 250 / 1000;   // more behind than this: play 6% faster
    const int MaxSamples = VoiceChat.SampleRate * 600 / 1000;       // more behind than this: skip ahead

    static AudioClip silence;

    readonly float[] ring = new float[VoiceChat.SampleRate * 2];
    readonly object gate = new object();
    int read, write, buffered;
    bool playing;
    double step, fraction;

    public float LastHeard { get; private set; }
    public int Packets { get; private set; }
    public int Underruns { get; private set; }
    public float BufferedMs { get { lock (gate) return buffered * 1000f / VoiceChat.SampleRate; } }

    void Awake()
    {
        int rate = AudioSettings.outputSampleRate;
        step = VoiceChat.SampleRate / (double)rate;
        if (silence == null) silence = AudioClip.Create("Silence", rate, 1, rate, false);
        // A playing (silent) clip keeps the AudioSource running, so OnAudioFilterRead is called continuously.
        var source = GetComponent<AudioSource>();
        source.clip = silence;
        source.loop = true;
        source.spatialBlend = 0f;
        source.playOnAwake = false;
        source.Play();
    }

    public void Push(byte[] data, int offset, int count)
    {
        LastHeard = Time.unscaledTime;
        Packets++;
        lock (gate)
        {
            for (int i = 0; i < count; i++)
            {
                ring[write] = MuLaw.Decode(data[offset + i]);
                write = (write + 1) % ring.Length;
                if (buffered < ring.Length) buffered++;
                else read = (read + 1) % ring.Length;
            }
            if (buffered > MaxSamples)
            {
                int skip = buffered - StartSamples;
                read = (read + skip) % ring.Length;
                buffered -= skip;
            }
        }
    }

    void OnAudioFilterRead(float[] data, int channels)
    {
        lock (gate)
        {
            if (!playing && buffered >= StartSamples) playing = true;
            if (!playing) return;
            for (int i = 0; i < data.Length; i += channels)
            {
                if (buffered < 2)
                {
                    playing = false;
                    Underruns++;
                    return;
                }
                float a = ring[read], b = ring[(read + 1) % ring.Length];
                float sample = a + (b - a) * (float)fraction;
                for (int c = 0; c < channels; c++) data[i + c] += sample;
                fraction += buffered > CatchUpSamples ? step * 1.06 : step;
                while (fraction >= 1.0)
                {
                    fraction -= 1.0;
                    read = (read + 1) % ring.Length;
                    buffered--;
                }
            }
        }
    }
}

/// <summary>G.711 mu-law: 16-bit sound in 8 bits, the way telephones do it.</summary>
public static class MuLaw
{
    const int Bias = 0x84;
    const int Clip = 32635;
    static readonly float[] decoded = BuildTable();

    public static byte Encode(float value)
    {
        int pcm = Mathf.Clamp((int)(value * 32767f), -32768, 32767);
        int sign = (pcm >> 8) & 0x80;
        if (sign != 0) pcm = -pcm;
        if (pcm > Clip) pcm = Clip;
        pcm += Bias;
        int exponent = 7;
        for (int mask = 0x4000; (pcm & mask) == 0 && exponent > 0; mask >>= 1) exponent--;
        int mantissa = (pcm >> (exponent + 3)) & 0x0F;
        return (byte)~(sign | (exponent << 4) | mantissa);
    }

    public static float Decode(byte value) => decoded[value];

    static float[] BuildTable()
    {
        var table = new float[256];
        for (int i = 0; i < 256; i++)
        {
            int u = ~i & 0xFF;
            int sign = u & 0x80, exponent = (u >> 4) & 0x07, mantissa = u & 0x0F;
            int sample = (((mantissa << 3) + Bias) << exponent) - Bias;
            table[i] = (sign != 0 ? -sample : sample) / 32768f;
        }
        return table;
    }
}
