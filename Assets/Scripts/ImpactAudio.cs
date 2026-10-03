using UnityEngine;

/// <summary>
/// Procedurally synthesized sounds (no audio assets needed), played through a small pool of 3D
/// AudioSources. Heavier matter plays lower and louder; metals clang, rubber boings, the rest thud.
/// </summary>
public static class ImpactAudio
{
    const int SampleRate = 44100;

    static AudioClip thud, clang, boing, rumble, zap, shatter;
    static AudioSource[] voices;
    static int nextVoice;

    public static AudioClip Thud => thud ? thud : (thud = BuildThud());
    public static AudioClip Clang => clang ? clang : (clang = BuildClang());
    public static AudioClip Boing => boing ? boing : (boing = BuildBoing());
    public static AudioClip Rumble => rumble ? rumble : (rumble = BuildRumble());
    public static AudioClip Zap => zap ? zap : (zap = BuildZap());
    public static AudioClip Shatter => shatter ? shatter : (shatter = BuildShatter());

    public static void PlayImpact(Vector3 position, float speed, float mass, float metalness, float bounciness)
    {
        AudioClip clip = metalness >= 0.6f ? Clang : bounciness >= 0.6f ? Boing : Thud;
        float heft = Mathf.Log10(1f + mass); // 1 kg ~ 0.3, 1 t ~ 3
        float volume = Mathf.Clamp01(0.12f + speed / 10f) * Mathf.Lerp(0.45f, 1f, heft / 4f);
        float pitch = Mathf.Clamp(1.55f - heft * 0.27f, 0.45f, 1.8f) * Random.Range(0.94f, 1.06f);
        Play(clip, position, volume, pitch);
    }

    public static void Play(AudioClip clip, Vector3 position, float volume, float pitch)
    {
        if (!Application.isPlaying || clip == null) return;
        AudioSource voice = NextVoice();
        voice.transform.position = position;
        voice.clip = clip;
        voice.volume = Mathf.Clamp01(volume);
        voice.pitch = pitch;
        voice.Play();
    }

    static AudioSource NextVoice()
    {
        if (voices == null || voices[0] == null)
        {
            var host = new GameObject("ImpactAudio");
            Object.DontDestroyOnLoad(host);
            voices = new AudioSource[12];
            for (int i = 0; i < voices.Length; i++)
            {
                var go = new GameObject("Voice" + i);
                go.transform.SetParent(host.transform, false);
                var source = go.AddComponent<AudioSource>();
                source.playOnAwake = false;
                source.spatialBlend = 0.9f;
                source.rolloffMode = AudioRolloffMode.Linear;
                source.minDistance = 2f;
                source.maxDistance = 45f;
                source.dopplerLevel = 0f;
                voices[i] = source;
            }
        }
        AudioSource next = voices[nextVoice];
        nextVoice = (nextVoice + 1) % voices.Length;
        return next;
    }

    // ------------------------------------------------------------------ synthesis

    static float[] Buffer(float seconds) => new float[Mathf.CeilToInt(seconds * SampleRate)];

    static AudioClip Make(string name, float[] data)
    {
        float peak = 0.0001f;
        foreach (float s in data) peak = Mathf.Max(peak, Mathf.Abs(s));
        for (int i = 0; i < data.Length; i++) data[i] *= 0.9f / peak;
        var clip = AudioClip.Create(name, data.Length, 1, SampleRate, false);
        clip.SetData(data, 0);
        return clip;
    }

    static AudioClip BuildThud()
    {
        float[] d = Buffer(0.6f);
        var rng = new System.Random(1);
        float lowpass = 0f;
        for (int i = 0; i < d.Length; i++)
        {
            float t = i / (float)SampleRate;
            float noise = (float)(rng.NextDouble() * 2.0 - 1.0);
            lowpass += (noise - lowpass) * 0.08f;
            float body = Mathf.Sin(2f * Mathf.PI * (48f + 40f * Mathf.Exp(-t * 18f)) * t) * Mathf.Exp(-t * 7f);
            d[i] = 0.9f * body + 1.6f * lowpass * Mathf.Exp(-t * 22f);
        }
        return Make("Thud", d);
    }

    static AudioClip BuildClang()
    {
        float[] d = Buffer(1.4f);
        float[] freq = { 520f, 1435f, 2808f, 4643f };
        float[] decay = { 2.2f, 3.4f, 5.2f, 7.5f };
        float[] amp = { 1f, 0.6f, 0.4f, 0.25f };
        var rng = new System.Random(2);
        for (int i = 0; i < d.Length; i++)
        {
            float t = i / (float)SampleRate;
            float s = 0f;
            for (int p = 0; p < freq.Length; p++) s += amp[p] * Mathf.Sin(2f * Mathf.PI * freq[p] * t) * Mathf.Exp(-t * decay[p]);
            s += (float)(rng.NextDouble() * 2.0 - 1.0) * Mathf.Exp(-t * 80f) * 0.8f;
            d[i] = s;
        }
        return Make("Clang", d);
    }

    static AudioClip BuildBoing()
    {
        float[] d = Buffer(0.9f);
        float phase = 0f;
        for (int i = 0; i < d.Length; i++)
        {
            float t = i / (float)SampleRate;
            float f = 110f + 120f * Mathf.Exp(-t * 7f) + 6f * Mathf.Sin(2f * Mathf.PI * 7f * t);
            phase += 2f * Mathf.PI * f / SampleRate;
            d[i] = Mathf.Sin(phase) * Mathf.Exp(-t * 3.2f) * (1f - Mathf.Exp(-t * 400f));
        }
        return Make("Boing", d);
    }

    static AudioClip BuildRumble()
    {
        float[] d = Buffer(1.8f);
        var rng = new System.Random(3);
        float lowpass = 0f;
        for (int i = 0; i < d.Length; i++)
        {
            float t = i / (float)SampleRate;
            lowpass += ((float)(rng.NextDouble() * 2.0 - 1.0) - lowpass) * 0.02f;
            float envelope = (1f - Mathf.Exp(-t * 12f)) * Mathf.Exp(-t * 1.6f);
            d[i] = (4f * lowpass + 0.5f * Mathf.Sin(2f * Mathf.PI * 34f * t)) * envelope;
        }
        return Make("Rumble", d);
    }

    static AudioClip BuildZap()
    {
        float[] d = Buffer(0.7f);
        var rng = new System.Random(4);
        for (int i = 0; i < d.Length; i++)
        {
            float t = i / (float)SampleRate;
            float buzz = Mathf.Sign(Mathf.Sin(2f * Mathf.PI * 120f * t)) * 0.35f;
            float crackle = rng.NextDouble() > 0.985 ? (float)(rng.NextDouble() * 2.0 - 1.0) : 0f;
            float hiss = (float)(rng.NextDouble() * 2.0 - 1.0) * 0.25f;
            d[i] = (buzz + crackle + hiss) * Mathf.Exp(-t * 4.5f);
        }
        return Make("Zap", d);
    }

    static AudioClip BuildShatter()
    {
        float[] d = Buffer(1.6f);
        var rng = new System.Random(5);
        float lowpass = 0f;
        for (int i = 0; i < d.Length; i++)
        {
            float t = i / (float)SampleRate;
            float noise = (float)(rng.NextDouble() * 2.0 - 1.0);
            lowpass += (noise - lowpass) * 0.2f;
            d[i] = (noise - lowpass) * Mathf.Exp(-t * 9f) * 1.4f; // bright crash
        }
        for (int p = 0; p < 22; p++) // falling fragments
        {
            float start = (float)rng.NextDouble() * 1.0f;
            float freq = 2400f + (float)rng.NextDouble() * 4800f;
            float amp = 0.15f + (float)rng.NextDouble() * 0.35f;
            for (int i = Mathf.FloorToInt(start * SampleRate); i < d.Length; i++)
            {
                float t = i / (float)SampleRate - start;
                if (t > 0.25f) break;
                d[i] += amp * Mathf.Sin(2f * Mathf.PI * freq * t) * Mathf.Exp(-t * 28f);
            }
        }
        return Make("Shatter", d);
    }
}
