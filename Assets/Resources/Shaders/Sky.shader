// Every map's sky (MapSky): the low sky rising into the zenith, a haze in the fog colour at and below the horizon
// (so the empty land round the walled map, and far walls fading into the fog, meet the sky without a seam), the
// sun's or moon's glow and disc in the sun's direction (_WorldSpaceLightPos0, like Unity's procedural sky), and
// stars at night. Built-in pipeline, LDR, gamma colour space.
Shader "Hidden/Low Strike/Sky"
{
    Properties
    {
        _Zenith ("Overhead", Color) = (0.24, 0.46, 0.82, 1)
        _Horizon ("Low sky", Color) = (0.56, 0.72, 0.9, 1)
        _Fog ("Haze at and below the horizon", Color) = (0.72, 0.8, 0.88, 1)
        _SunGlow ("Sun or moon glow", Color) = (1, 0.87, 0.62, 1)
        _SunDisc ("Disc radius (radians)", Range(0, 0.2)) = 0.035
        _HazeHeight ("Haze height", Range(0.02, 1)) = 0.1
        _Stars ("Stars", Range(0, 1)) = 0
    }

    SubShader
    {
        Tags { "Queue" = "Background" "RenderType" = "Background" "PreviewType" = "Skybox" }
        Cull Off ZWrite Off

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0
            #include "UnityCG.cginc"

            half4 _Zenith, _Horizon, _Fog, _SunGlow;
            float _SunDisc, _HazeHeight, _Stars;

            struct appdata
            {
                float4 vertex : POSITION;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float3 dir : TEXCOORD0;      // the way the camera looks (the sky mesh sits round the camera)
                float4 screen : TEXCOORD1;
            };

            v2f vert (appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.dir = v.vertex.xyz;
                o.screen = ComputeScreenPos(o.pos);
                return o;
            }

            // A random number for a star cell, without sin (steady on phone graphics chips).
            float Hash (float3 p)
            {
                p = frac(p * 0.1031);
                p += dot(p, p.zyx + 31.32);
                return frac((p.x + p.y) * p.z);
            }

            half4 frag (v2f i) : SV_Target
            {
                float3 dir = normalize(i.dir);
                float up = saturate(dir.y);

                // The low sky into the zenith: quickly just above the horizon, slowly overhead.
                float3 color = lerp(_Horizon.rgb, _Zenith.rgb, sqrt(up));

                // Stars: a few cells of a fine grid round the camera hold one, of random size and brightness; they
                // fade out low down, where the haze is.
                float3 grid = dir * 140.0;
                float3 cell = floor(grid);
                float star = saturate((0.32 - length(grid - cell - 0.5)) * 5.0) * saturate((Hash(cell) - 0.965) * 28.6);
                color += star * _Stars * saturate(up * 4.0);

                // The sun's (or moon's) glow: a wide halo and a tight bright one, then the disc, a paler colour.
                float facing = dot(dir, _WorldSpaceLightPos0.xyz);
                float toward = max(facing, 0.0001);
                color += _SunGlow.rgb * (0.3 * pow(toward, 6.0) + 0.7 * pow(toward, 90.0));
                float angle = sqrt(max(2.0 - 2.0 * facing, 0.0));   // about the angle from the sun, near it
                float radius = max(_SunDisc, 0.0001);
                float disc = 1.0 - smoothstep(radius * 0.8, radius, angle);
                color = lerp(color, lerp(_SunGlow.rgb, float3(1.0, 1.0, 1.0), 0.6), disc);

                // Haze in the fog colour: all of it at and below the horizon, thinning upwards.
                color = lerp(color, _Fog.rgb, exp(-up / _HazeHeight));

                // A little noise against banding in the smooth gradient (8 bits a colour).
                float2 pixel = i.screen.xy / i.screen.w * _ScreenParams.xy;
                color += (frac(52.9829189 * frac(dot(pixel, float2(0.06711056, 0.00583715)))) - 0.5) / 255.0;
                return half4(color, 1.0);
            }
            ENDCG
        }
    }
    Fallback Off
}
