// The main camera's finishing pass (PostEffects): bloom (the bright parts, blurred at smaller and smaller sizes and
// added back), colour grading that keeps the maps' cartoon colours vivid, a soft vignette, and FXAA (3.11, console
// style) in the same full-size pass. Built-in pipeline, LDR, gamma colour space.
Shader "Hidden/Low Strike/Post"
{
    Properties
    {
        _MainTex ("", 2D) = "black" {}
        _BloomTex ("", 2D) = "black" {}
        _Threshold ("", Vector) = (0.8, 0.48, 0.64, 0.78)
        _Grade ("", Vector) = (1, 0, 1, 0)
        _Shoulder ("", Float) = 0
        _HighlightTint ("", Vector) = (0, 0, 0, 0)
        _ShadowTint ("", Vector) = (0, 0, 0, 0)
        _BloomStrength ("", Float) = 0
        _Scatter ("", Float) = 0.7
        _Vignette ("", Float) = 0
    }

    CGINCLUDE
    #include "UnityCG.cginc"

    #define LUMA half3(0.299, 0.587, 0.114)
    // The filmic shoulder: colours above START bend smoothly towards END instead of clipping flat at white.
    #define SHOULDER_START 0.85
    #define SHOULDER_END 1.35
    // FXAA: how far along an edge it looks, and how much contrast makes an edge (less in the dark).
    #define FXAA_SHARPNESS 8.0
    #define FXAA_THRESHOLD 0.125
    #define FXAA_THRESHOLD_MIN 0.04

    sampler2D _MainTex;
    float4 _MainTex_TexelSize;
    sampler2D _BloomTex;        // the upsample's own size of the bloom; the grading's finished bloom
    float4 _BloomTex_TexelSize;
    half4 _Threshold;           // x: from which brightness things glow; yzw: the soft knee (threshold - knee, knee * 2, 0.25 / knee)
    half4 _Grade;               // x: exposure, y: contrast (how much S-curve), z: saturation, w: vibrance
    half _Shoulder;             // how much of the filmic shoulder (0: none, colours up to white stay as they are)
    half4 _HighlightTint;       // split toning: colour shifts with no brightness of their own (rgb)
    half4 _ShadowTint;
    half _BloomStrength;
    half _Scatter;              // how much each smaller (wider) size of the bloom counts against the one above it
    half _Vignette;

    struct v2f
    {
        float4 pos : SV_POSITION;
        float2 uv : TEXCOORD0;
        float2 uvBloom : TEXCOORD1;   // for _BloomTex: on D3D-like platforms with MSAA the camera's picture comes upside down
    };

    v2f vert (appdata_img v)
    {
        v2f o;
        o.pos = UnityObjectToClipPos(v.vertex);
        o.uv = v.texcoord.xy;
        o.uvBloom = v.texcoord.xy;
        #if UNITY_UV_STARTS_AT_TOP
        if (_MainTex_TexelSize.y < 0.0) o.uvBloom.y = 1.0 - o.uvBloom.y;
        #endif
        return o;
    }

    // Four bilinear taps one texel round the pixel: a 4x4 box of the picture twice the size.
    half3 Box4 (sampler2D tex, float2 uv, float2 texel)
    {
        float4 d = texel.xyxy * float4(-1.0, -1.0, 1.0, 1.0);
        return (tex2D(tex, uv + d.xy).rgb + tex2D(tex, uv + d.zy).rgb + tex2D(tex, uv + d.xw).rgb + tex2D(tex, uv + d.zw).rgb) * 0.25;
    }

    // Pass 0: the bright parts at half size.
    half4 FragPrefilter (v2f i) : SV_Target
    {
        half3 c = Box4(_MainTex, i.uv, _MainTex_TexelSize.xy) * _Grade.x;
        half bright = max(c.r, max(c.g, c.b));
        // A soft knee: a little below the threshold things glow a little, on a curve, so no hard edge shows.
        half knee = clamp(bright - _Threshold.y, 0.0, _Threshold.z);
        knee = _Threshold.w * knee * knee;
        c *= max(knee, bright - _Threshold.x) / max(bright, 0.0001);
        return half4(c, 1.0);
    }

    // Pass 1: half the size again.
    half4 FragDown (v2f i) : SV_Target
    {
        return half4(Box4(_MainTex, i.uv, _MainTex_TexelSize.xy), 1.0);
    }

    // Pass 2: back up a size: a 3x3 tent of the smaller picture (_MainTex) blended over this size's own (_BloomTex).
    half4 FragUp (v2f i) : SV_Target
    {
        float4 d = _MainTex_TexelSize.xyxy * float4(1.0, 1.0, -1.0, 0.0);
        half3 s = tex2D(_MainTex, i.uv - d.xy).rgb;
        s += tex2D(_MainTex, i.uv - d.wy).rgb * 2.0;
        s += tex2D(_MainTex, i.uv - d.zy).rgb;
        s += tex2D(_MainTex, i.uv + d.zw).rgb * 2.0;
        s += tex2D(_MainTex, i.uv).rgb * 4.0;
        s += tex2D(_MainTex, i.uv + d.xw).rgb * 2.0;
        s += tex2D(_MainTex, i.uv + d.zy).rgb;
        s += tex2D(_MainTex, i.uv + d.wy).rgb * 2.0;
        s += tex2D(_MainTex, i.uv + d.xy).rgb;
        return half4(lerp(tex2D(_BloomTex, i.uvBloom).rgb, s * (1.0 / 16.0), _Scatter), 1.0);
    }

    // The grading, on a colour that already has its exposure (and bloom).
    half3 Grade (half3 c, float2 uv)
    {
        // A gentle filmic shoulder, so light that exposure and bloom push past white rolls off into it instead of
        // clipping flat. As much of it as the map's look has: none for a neutral look, which then changes nothing.
        half3 over = max(c - SHOULDER_START, 0.0);
        half3 rolled = min(c, SHOULDER_START) + over / (1.0 + over * (1.0 / (SHOULDER_END - SHOULDER_START)));
        c = saturate(lerp(c, rolled, _Shoulder));

        // Contrast as an S-curve, so nothing clips.
        c = lerp(c, c * c * (3.0 - 2.0 * c), _Grade.y);

        // Saturation, and vibrance: greyer colours get more of it than already strong ones.
        half luma = dot(c, LUMA);
        half chroma = max(c.r, max(c.g, c.b)) - min(c.r, min(c.g, c.b));
        c = lerp(half3(luma, luma, luma), c, _Grade.z + _Grade.w * (1.0 - chroma));

        // Split toning: cool in the dark mid-tones (black stays black), warm in the brights.
        half dark = luma * (1.0 - luma) * (1.0 - luma) * 6.75;
        c += _ShadowTint.rgb * dark + _HighlightTint.rgb * (luma * luma);

        // A soft vignette.
        float2 d = uv - 0.5;
        c *= 1.0 - _Vignette * smoothstep(0.08, 0.5, dot(d, d));

        // A little noise against banding (8 bits a colour).
        float2 pixel = uv * abs(_MainTex_TexelSize.zw);
        c += (frac(52.9829189 * frac(dot(pixel, float2(0.06711056, 0.00583715)))) - 0.5) / 255.0;
        return saturate(c);
    }

    // The half-size bloom, smoothed as it is stretched: four bilinear taps half a texel round the pixel.
    half3 BloomAt (float2 uvBloom)
    {
        return Box4(_BloomTex, uvBloom, _BloomTex_TexelSize.xy * 0.5) * _BloomStrength;
    }

    // FXAA's taps: no mipmaps, and fine after the early return (no gradients needed).
    #define FXAA_TAP(p) tex2Dlod(_MainTex, float4(p, 0.0, 0.0)).rgb

    // FXAA on the camera's picture, before the grading (which keeps the order of brightnesses, so the same edges
    // show): the colour at uv, its edge smoothed.
    half3 Fxaa (float2 uv)
    {
        float2 t = _MainTex_TexelSize.xy;
        half3 rgbM = FXAA_TAP(uv);
        // The four corners, each a bilinear average of 2x2 pixels ("north" is -y here). The tiny extra on one keeps
        // the edge's direction from ever being zero.
        float lumaNw = dot(FXAA_TAP(uv + float2(-0.5, -0.5) * t), LUMA);
        float lumaNe = dot(FXAA_TAP(uv + float2(0.5, -0.5) * t), LUMA) + 1.0 / 384.0;
        float lumaSw = dot(FXAA_TAP(uv + float2(-0.5, 0.5) * t), LUMA);
        float lumaSe = dot(FXAA_TAP(uv + float2(0.5, 0.5) * t), LUMA);
        float lumaM = dot(rgbM, LUMA);
        float lumaMin = min(min(lumaNw, lumaNe), min(lumaSw, lumaSe));
        float lumaMax = max(max(lumaNw, lumaNe), max(lumaSw, lumaSe));
        // No edge here: left as it is.
        if (max(lumaMax, lumaM) - min(lumaMin, lumaM) < max(FXAA_THRESHOLD_MIN, lumaMax * FXAA_THRESHOLD))
            return rgbM;

        // Along the edge: two taps close by, two further out, which are dropped when they reach past the edge.
        float dirSwMinusNe = lumaSw - lumaNe;
        float dirSeMinusNw = lumaSe - lumaNw;
        float2 dir1 = normalize(float2(dirSwMinusNe + dirSeMinusNw, dirSwMinusNe - dirSeMinusNw));
        half3 rgbN1 = FXAA_TAP(uv - dir1 * t * 0.5);
        half3 rgbP1 = FXAA_TAP(uv + dir1 * t * 0.5);
        float2 dir2 = clamp(dir1 / max(min(abs(dir1.x), abs(dir1.y)) * FXAA_SHARPNESS, 0.0001), -2.0, 2.0);
        half3 rgbN2 = FXAA_TAP(uv - dir2 * t * 2.0);
        half3 rgbP2 = FXAA_TAP(uv + dir2 * t * 2.0);
        half3 rgbA = rgbN1 + rgbP1;
        half3 rgbB = (rgbN2 + rgbP2) * 0.25 + rgbA * 0.25;
        float lumaB = dot(rgbB, LUMA);
        if (lumaB < lumaMin || lumaB > lumaMax) rgbB = rgbA * 0.5;
        return rgbB;
    }

    // Pass 3: grading and vignette (High with MSAA, on a map with no bloom).
    half4 FragGrade (v2f i) : SV_Target
    {
        half3 c = tex2D(_MainTex, i.uv).rgb * _Grade.x;
        return half4(Grade(c, i.uv), 1.0);
    }

    // Pass 4: the bloom added, then grading and vignette (High with MSAA).
    half4 FragGradeBloom (v2f i) : SV_Target
    {
        half3 c = tex2D(_MainTex, i.uv).rgb * _Grade.x + BloomAt(i.uvBloom);
        return half4(Grade(c, i.uv), 1.0);
    }

    // Pass 5: FXAA, then grading and vignette (no MSAA, on a map with no bloom).
    half4 FragFxaaGrade (v2f i) : SV_Target
    {
        return half4(Grade(Fxaa(i.uv) * _Grade.x, i.uv), 1.0);
    }

    // Pass 6: FXAA, the bloom added, then grading and vignette (Medium).
    half4 FragFxaaGradeBloom (v2f i) : SV_Target
    {
        half3 bloom = BloomAt(i.uvBloom);   // first, outside FXAA's early return: the bloom's taps use gradients
        return half4(Grade(Fxaa(i.uv) * _Grade.x + bloom, i.uv), 1.0);
    }
    ENDCG

    SubShader
    {
        Cull Off ZWrite Off ZTest Always

        Pass // 0: bright parts, half size
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment FragPrefilter
            #pragma target 3.0
            ENDCG
        }

        Pass // 1: downsample
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment FragDown
            #pragma target 3.0
            ENDCG
        }

        Pass // 2: upsample
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment FragUp
            #pragma target 3.0
            ENDCG
        }

        Pass // 3: grading and vignette
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment FragGrade
            #pragma target 3.0
            ENDCG
        }

        Pass // 4: bloom, grading and vignette
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment FragGradeBloom
            #pragma target 3.0
            ENDCG
        }

        Pass // 5: FXAA, grading and vignette
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment FragFxaaGrade
            #pragma target 3.0
            ENDCG
        }

        Pass // 6: FXAA, bloom, grading and vignette
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment FragFxaaGradeBloom
            #pragma target 3.0
            ENDCG
        }
    }
    Fallback Off
}
