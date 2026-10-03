using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Voice chat for LAN games: hold V during a match to talk; everyone in the game hears you.
///
/// The microphone is recorded at 16 kHz, cut into 20 ms frames, squeezed to 8 bits per sample (G.711 mu-law,
/// 16 KB/s, plenty for a LAN) and sent through <see cref="NetSession"/>: a client sends to the host, the host plays
/// it and passes it on to the other clients. The microphone only starts the first time someone presses V in a
/// LAN game (that is when macOS asks for permission) and stops when the LAN game ends.
/// </summary>
public class VoiceChat : MonoBehaviour
{
    public const int SampleRate = 16000;
    public const KeyCode TalkKey = KeyCode.V;
    const int FrameSamples = SampleRate / 50;   // 20 ms per packet

    public static VoiceChat Instance { get; private set; }

    /// <summary>The local player is holding V and their voice is being sent.</summary>
    public bool Talking { get; private set; }

    /// <summary>Why the microphone does not work ("No microphone found"), or null.</summary>
    public string Problem { get; private set; }

    GameManager gm;
    AudioClip micClip;
    int micRate, micPosition;
    bool micFailed;
    float[] chunk = new float[0];
    readonly List<float> pending = new List<float>();   // 16 kHz samples not sent yet
    float resampleCarry;
    ushort sequence;
    readonly byte[] frame = new byte[FrameSamples];

    // -ds-voice-test sends a tone instead of the microphone (testing two copies of the game on one PC);
    // -ds-voice-log logs what arrives every two seconds; -ds-mic-check reads the microphone for 4 seconds and
    // logs only how many samples came in and how loud they were (nothing is sent, played or kept).
    bool testTone, log, micCheck, micCheckDone;
    double tonePhase, checkLevel;
    float nextLog, checkStarted;
    long checkSamples;

    readonly Dictionary<string, VoiceOutput> speakers = new Dictionary<string, VoiceOutput>();

    void Awake()
    {
        Instance = this;
        gm = GetComponent<GameManager>();
        var args = Environment.GetCommandLineArgs();
        testTone = Array.IndexOf(args, "-ds-voice-test") >= 0;
        log = Array.IndexOf(args, "-ds-voice-log") >= 0;
        micCheck = Array.IndexOf(args, "-ds-mic-check") >= 0;
        if (log) Debug.Log("[Voice] Microphones found: " + Microphone.devices.Length);
    }

    void Update()
    {
        if (micCheck)
        {
            CheckMicrophone();
            return;
        }
        var net = gm.Net;
        bool session = net.IsHost ? net.Peers.Count > 0 : net.IsClient && net.Connected;
        if (!session)
        {
            StopMicrophone();
            foreach (var speaker in speakers.Values) Destroy(speaker.gameObject);
            speakers.Clear();
            micFailed = false;
            Problem = null;
            Talking = false;
            return;
        }

        bool inMatch = gm.State != MatchState.Menu && !gm.IsPaused;
        bool wantsToTalk = GameSettings.VoiceChat && inMatch && (testTone || Input.GetKey(TalkKey));
        if (wantsToTalk && !testTone && micClip == null && !micFailed) StartMicrophone();
        Talking = wantsToTalk && (testTone || micClip != null);
        Capture(Talking);

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

    void StartMicrophone()
    {
        if (Microphone.devices.Length == 0)
        {
            micFailed = true;
            Problem = "No microphone found";
            Debug.Log("[Voice] No microphone found");
            return;
        }
        Microphone.GetDeviceCaps(null, out int min, out int max);
        micRate = min == 0 && max == 0 ? SampleRate : Mathf.Clamp(SampleRate, min, max);
        micClip = Microphone.Start(null, true, 1, micRate);
        micPosition = 0;
        resampleCarry = 0f;
        if (micClip == null)
        {
            micFailed = true;
            Problem = "The microphone could not be started";
            Debug.Log("[Voice] The microphone could not be started");
            return;
        }
        Debug.Log($"[Voice] Microphone on: {Microphone.devices[0]} at {micRate} Hz");
    }

    void StopMicrophone()
    {
        if (micClip == null) return;
        Microphone.End(null);
        Destroy(micClip);
        micClip = null;
        pending.Clear();
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
            for (int i = 0; i < FrameSamples; i++) frame[i] = MuLaw.Encode(pending[i]);
            pending.RemoveRange(0, FrameSamples);
            gm.Net.SendVoice(sequence++, frame);
        }
    }

    // Everything recorded since the last call; kept (as 16 kHz samples in pending) only when keep is set.
    void ReadMicrophone(bool keep)
    {
        if (micClip != null)
        {
            int position = Microphone.GetPosition(null);
            int length = micClip.samples;
            int available = (position - micPosition + length) % length;
            while (available > 0)
            {
                int count = Mathf.Min(available, length - micPosition);
                if (chunk.Length != count * micClip.channels) chunk = new float[count * micClip.channels];
                micClip.GetData(chunk, micPosition);
                if (keep) AddResampled(chunk, micClip.channels);
                micPosition = (micPosition + count) % length;
                available -= count;
            }
        }
    }

    void CheckMicrophone()
    {
        if (micCheckDone) return;
        if (micClip == null && !micFailed)
        {
            StartMicrophone();
            checkStarted = Time.unscaledTime;
        }
        if (micFailed)
        {
            micCheckDone = true;
            return;
        }
        ReadMicrophone(true);
        foreach (float sample in pending) checkLevel += sample * sample;
        checkSamples += pending.Count;
        pending.Clear();
        float elapsed = Time.unscaledTime - checkStarted;
        if (elapsed < 4f) return;
        Debug.Log($"[Voice] Microphone check: {checkSamples / elapsed:0} samples per second (16000 expected), " +
                  $"level {Math.Sqrt(checkLevel / Math.Max(1, checkSamples)):0.0000}");
        StopMicrophone();
        micCheckDone = true;
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
/// thread, resampled from 16 kHz to the output rate. Playback starts once 60 ms are buffered, which hides small
/// network hiccups without much delay.
/// </summary>
public class VoiceOutput : MonoBehaviour
{
    const int StartSamples = VoiceChat.SampleRate * 60 / 1000;
    const int CatchUpSamples = VoiceChat.SampleRate * 150 / 1000;   // more behind than this: play 6% faster
    const int MaxSamples = VoiceChat.SampleRate * 400 / 1000;       // more behind than this: skip ahead

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
