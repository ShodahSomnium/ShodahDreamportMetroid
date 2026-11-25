// Metroid Prime materials, run the way the GameCube runs them (decomp src/Kyoto/Graphics/CCubeMaterial.cpp + CGX.cpp).
// Every material on the disc (37,542 of them, probe "matcensus") uses at most 6 TEV stages, 6 textures, and only the stage form
//   out = clamp(d + lerp(a, b, c))   (op ADD, bias 0, scale 1, clamp on)
// for colour and alpha, so each stage is that formula on inputs picked by index (CPREV APREV C0 A0 C1 A1 C2 A2 TEXC TEXA RASC RASA
// ONE HALF KONST ZERO / APREV A0 A1 A2 TEXA RASA KONST ZERO). Registers: PREV, R0, R1 (= the world light fade, CCubeRenderer
// mTevReg1Color), R2. The final colour is PREV after the last stage (the game appends a pass-through when the last stage writes
// elsewhere). RAS = colour channel 0, lit per vertex (GX lighting). Texture coordinates come from the material's own texgens
// (source POS / NRM / TEX0..4, matrix = one of its UV animations or identity, normalize, post matrix) - CCubeMaterial
// HandleAnimatedUV modes 0..7 with the game's constants. Meshes are kept in the game's own space (Z up) under a root transform,
// so object space here IS game space.

#ifndef MP_TEV_INCLUDED
#define MP_TEV_INCLUDED

TEXTURE2D(_Tex0); SAMPLER(sampler_Tex0);
TEXTURE2D(_Tex1); SAMPLER(sampler_Tex1);
TEXTURE2D(_Tex2); SAMPLER(sampler_Tex2);
TEXTURE2D(_Tex3); SAMPLER(sampler_Tex3);
TEXTURE2D(_Tex4); SAMPLER(sampler_Tex4);
TEXTURE2D(_Tex5); SAMPLER(sampler_Tex5);

CBUFFER_START(UnityPerMaterial)
    float4 _Ci0, _Ci1, _Ci2, _Ci3, _Ci4, _Ci5;      // colour inputs a b c d
    float4 _Ai0, _Ai1, _Ai2, _Ai3, _Ai4, _Ai5;      // alpha inputs a b c d
    float4 _Ot0, _Ot1, _Ot2, _Ot3, _Ot4, _Ot5;      // colour out reg, alpha out reg, ras (0 zero, 1 channel 0), has texture
    float4 _Tg0, _Tg1, _Tg2, _Tg3, _Tg4, _Tg5;      // texgen: source (-1 none, 0 POS, 1 NRM, 4.. TEX0..), anim mode (-1 identity), normalize, -
    float4 _Tp0, _Tp1, _Tp2, _Tp3, _Tp4, _Tp5;      // the anim's parameters
    float4 _K0, _K1, _K2, _K3, _K4, _K5;            // the stage's konst colour (rgb by its colour select, a by its alpha select)
    float _StageCount;
    float _AlphaTest;
    float _Lit;            // 0 world geometry (dynamic lights only), 1 actors (ambient + lights), 2 HUD (constant ambient)
    float4 _Ambient;       // actors: the area's ambient
    float4 _ModColor;      // the actor's colour (CModelFlags colour: fades), multiplies the result
    float _SrcBlend;
    float _DstBlend;
    float _ZWrite;
    float _Cull;
    float _ZTest;
CBUFFER_END

// globals (MpShaderGlobals.cs)
float4 _MpC1;                 // TEV register 1: world light fade level (white)
float4 _MpLightPos[4];        // game space position, w = 1 on
float4 _MpLightColor[4];      // rgb
float4 _MpLightAtten[4];      // k0 k1 k2 (distance), w radius
float _MpLightCount;
float4 _MpRetroCam;           // camera position in game space
float4x4 _MpUnityToRetro;     // Unity world -> game space (inverse of the root transform)
float _MpTime;                // seconds mod 900
float _MpWorldScale;          // metres per game unit (the root transform scale)
float4 _MpFogColor;           // rgb, a = on
float4 _MpFogRange;           // start, end (game units, view depth)
float _MpVisor;               // EPlayerVisor: 0 combat, 1 x-ray, 2 scan, 3 thermal
float _MpThermalHot;          // actors: their heat (thermal visor)
float _MpLinearOut;           // 1: the frame buffer is linear - the GameCube result (gamma space) is converted on output
TEXTURE2D(_MpThermalPalette); SAMPLER(sampler_MpThermalPalette);   // TXTR_ThermoPalette
TEXTURE2D(_MpXRayPalette); SAMPLER(sampler_MpXRayPalette);         // TXTR_XRayPalette
float _MpHasPalettes;

// sRGB -> linear (the GameCube result is gamma-space values; a linear frame buffer encodes them back on write)
float3 MpSrgbToLinear(float3 c)
{
    c = saturate(c);
    return c <= 0.04045 ? c / 12.92 : pow((c + 0.055) / 1.055, 2.4);
}

struct MpTevAttributes
{
    float4 positionOS : POSITION;
    float3 normalOS : NORMAL;
    float2 tex0 : TEXCOORD0;
    float2 tex1 : TEXCOORD1;
    float2 tex2 : TEXCOORD2;
    float2 tex3 : TEXCOORD3;
    float2 tex4 : TEXCOORD4;
    UNITY_VERTEX_INPUT_INSTANCE_ID
};

float2 MpPickTex(int src, MpTevAttributes v)
{
    if (src == 4) return v.tex0;
    if (src == 5) return v.tex1;
    if (src == 6) return v.tex2;
    if (src == 7) return v.tex3;
    return v.tex4;
}

// game-space view direction of an object-space vector (the game's "inverse view * model" without translation), as (right, forward, up)
float3 MpViewDirRetro(float3 dirOS)
{
    float3 ws = TransformObjectToWorldDir(dirOS, false);
    float3 vs = mul((float3x3)GetWorldToViewMatrix(), ws);     // Unity view: x right, y up, z towards the viewer
    return float3(vs.x, -vs.z, vs.y);
}

float2 MpTexGen(float4 tg, float4 tp, MpTevAttributes v)
{
    int src = (int)tg.x;
    int mode = (int)tg.y;
    if (src < 0) return float2(0, 0);
    float4 inV;
    bool isTex = src >= 4;
    if (src == 0) inV = float4(v.positionOS.xyz, 1);
    else if (src == 1) inV = float4(v.normalOS, 1);
    else inV = float4(MpPickTex(src, v), 1, 1);

    float3 r = inV.xyz;
    float2 o;
    if (mode == 0 || mode == 7)
    {
        r = MpViewDirRetro(inV.xyz);
    }
    else if (mode == 1)
    {
        float3 ws = TransformObjectToWorld(inV.xyz);
        float3 vs = TransformWorldToView(ws) / max(1e-4, _MpWorldScale);
        r = float3(vs.x, -vs.z, vs.y);
    }
    else if (mode == 2)
    {
        r = float3(inV.x + tp.x + _MpTime * tp.z, inV.y + tp.y + _MpTime * tp.w, inV.z);
    }
    else if (mode == 3)
    {
        float ang = tp.x + _MpTime * tp.y;
        float s = sin(ang), c = cos(ang);
        r = float3(c * inV.x - s * inV.y + 0.5 * (1 - (c - s)), s * inV.x + c * inV.y + 0.5 * (1 - (s + c)), inV.z);
    }
    else if (mode == 4 || mode == 5)
    {
        float value = (tp.w + _MpTime) * tp.x * tp.z;
        float step = trunc(frac(value) * tp.y) * tp.z;
        r = float3(inV.x + (mode == 4 ? step : 0), inV.y + (mode == 5 ? step : 0), inV.z);
    }
    else if (mode == 6)
    {
        float3x3 m = (float3x3)mul(_MpUnityToRetro, unity_ObjectToWorld);
        r = mul(m, inV.xyz);
    }

    if (tg.z > 0.5) r = normalize(r);

    if (mode == 0 || mode == 1)
    {
        o = float2(0.5 * r.x + 0.5, 0.5 * r.z + 0.5);
    }
    else if (mode == 6)
    {
        float3 t = mul(_MpUnityToRetro, float4(unity_ObjectToWorld._m03_m13_m23, 1)).xyz;
        o = float2(0.5 * r.x + 0.05 * t.x, 0.5 * r.z + 0.05 * t.y);
    }
    else if (mode == 7)
    {
        float scale = 0.5 * tp.x;
        float3 c = _MpRetroCam.xyz;
        o = float2(scale * r.x + frac(0.025 * (c.x + c.y) * tp.y), scale * r.z + frac(0.05 * c.z * tp.y));
    }
    else
    {
        o = isTex ? r.xy / max(1e-6, abs(r.z)) * sign(r.z + 1e-9) : r.xy;
    }

    return o;
}

// GX lighting of channel 0 (per vertex): world geometry gets the dynamic lights only (CCubeRenderer::ActivateLightsForModel,
// ambient 0); actors ambient + lights, or ambient * ambient with no light (the material colour falls back to the ambient).
float4 MpRas(float3 positionOS, float3 normalOS)
{
    if (_Lit > 1.5) return float4(_Ambient.rgb, 1);      // HUD models: the GUI's white ambient, no lights (CGuiFrame::Draw)
    float3 posR = positionOS;
    float3 nrm = normalize(normalOS);
    if (_Lit > 0.5)
    {
        float4x4 m = mul(_MpUnityToRetro, unity_ObjectToWorld);
        posR = mul(m, float4(positionOS, 1)).xyz;
        nrm = normalize(mul((float3x3)m, normalOS));
    }

    float3 sum = 0;
    int count = (int)_MpLightCount;
    [unroll] for (int i = 0; i < 4; i++)
    {
        if (i >= count) break;
        float3 d = _MpLightPos[i].xyz - posR;
        float dist = length(d);
        float3 l = d / max(1e-4, dist);
        float diff = saturate(dot(nrm, l));
        float4 k = _MpLightAtten[i];
        float att = 1.0 / max(1e-4, k.x + k.y * dist + k.z * dist * dist);
        if (k.w > 0 && dist > k.w) att = 0;
        sum += _MpLightColor[i].rgb * diff * att;
    }

    if (_Lit > 0.5)
    {
        float3 amb = _Ambient.rgb;
        return float4(count > 0 ? saturate(amb + sum) : amb * amb, 1);
    }

    return float4(saturate(sum), 1);
}

struct MpTevVaryings
{
    float4 positionCS : SV_POSITION;
    float4 uv01 : TEXCOORD0;
    float4 uv23 : TEXCOORD1;
    float4 uv45 : TEXCOORD2;
    float4 ras : TEXCOORD3;
    float3 normalWS : TEXCOORD4;
    float viewDepth : TEXCOORD5;
    UNITY_VERTEX_INPUT_INSTANCE_ID
    UNITY_VERTEX_OUTPUT_STEREO
};

MpTevVaryings MpTevVert(MpTevAttributes v)
{
    MpTevVaryings o = (MpTevVaryings)0;
    UNITY_SETUP_INSTANCE_ID(v);
    UNITY_TRANSFER_INSTANCE_ID(v, o);
    UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
    float3 ws = TransformObjectToWorld(v.positionOS.xyz);
    o.positionCS = TransformWorldToHClip(ws);
    o.normalWS = TransformObjectToWorldNormal(v.normalOS);
    int n = (int)_StageCount;
    o.uv01.xy = MpTexGen(_Tg0, _Tp0, v);
    if (n > 1) o.uv01.zw = MpTexGen(_Tg1, _Tp1, v);
    if (n > 2) o.uv23.xy = MpTexGen(_Tg2, _Tp2, v);
    if (n > 3) o.uv23.zw = MpTexGen(_Tg3, _Tp3, v);
    if (n > 4) o.uv45.xy = MpTexGen(_Tg4, _Tp4, v);
    if (n > 5) o.uv45.zw = MpTexGen(_Tg5, _Tp5, v);
    o.ras = MpRas(v.positionOS.xyz, v.normalOS);
    o.viewDepth = -TransformWorldToView(ws).z;
    return o;
}

float3 MpColorIn(int i, float4 prev, float4 r0, float4 r1, float4 r2, float4 tex, float4 ras, float4 k)
{
    if (i == 0) return prev.rgb;
    if (i == 1) return prev.aaa;
    if (i == 2) return r0.rgb;
    if (i == 3) return r0.aaa;
    if (i == 4) return r1.rgb;
    if (i == 5) return r1.aaa;
    if (i == 6) return r2.rgb;
    if (i == 7) return r2.aaa;
    if (i == 8) return tex.rgb;
    if (i == 9) return tex.aaa;
    if (i == 10) return ras.rgb;
    if (i == 11) return ras.aaa;
    if (i == 12) return float3(1, 1, 1);
    if (i == 13) return float3(0.5, 0.5, 0.5);
    if (i == 14) return k.rgb;
    return float3(0, 0, 0);
}

float MpAlphaIn(int i, float4 prev, float4 r0, float4 r1, float4 r2, float4 tex, float4 ras, float4 k)
{
    if (i == 0) return prev.a;
    if (i == 1) return r0.a;
    if (i == 2) return r1.a;
    if (i == 3) return r2.a;
    if (i == 4) return tex.a;
    if (i == 5) return ras.a;
    if (i == 6) return k.a;
    return 0;
}

void MpStage(float4 ci, float4 ai, float4 ot, float4 k, float4 tex, float4 rasIn,
             inout float4 prev, inout float4 r0, inout float4 r1, inout float4 r2)
{
    float4 ras = ot.z > 0.5 ? rasIn : float4(0, 0, 0, 0);
    float3 a = MpColorIn((int)ci.x, prev, r0, r1, r2, tex, ras, k);
    float3 b = MpColorIn((int)ci.y, prev, r0, r1, r2, tex, ras, k);
    float3 c = MpColorIn((int)ci.z, prev, r0, r1, r2, tex, ras, k);
    float3 d = MpColorIn((int)ci.w, prev, r0, r1, r2, tex, ras, k);
    float aa = MpAlphaIn((int)ai.x, prev, r0, r1, r2, tex, ras, k);
    float ab = MpAlphaIn((int)ai.y, prev, r0, r1, r2, tex, ras, k);
    float ac = MpAlphaIn((int)ai.z, prev, r0, r1, r2, tex, ras, k);
    float ad = MpAlphaIn((int)ai.w, prev, r0, r1, r2, tex, ras, k);
    float3 col = saturate(d + lerp(a, b, c));
    float alp = saturate(ad + lerp(aa, ab, ac));
    int co = (int)ot.x, ao = (int)ot.y;
    if (co == 0) prev.rgb = col; else if (co == 1) r0.rgb = col; else if (co == 2) r1.rgb = col; else r2.rgb = col;
    if (ao == 0) prev.a = alp; else if (ao == 1) r0.a = alp; else if (ao == 2) r1.a = alp; else r2.a = alp;
}

float4 MpTevShade(MpTevVaryings i)
{
    float4 prev = 0, r0 = 0, r1 = _MpC1, r2 = 0;
    int n = (int)_StageCount;
    float4 t;
    t = _Ot0.w > 0.5 ? SAMPLE_TEXTURE2D(_Tex0, sampler_Tex0, i.uv01.xy) : float4(1, 1, 1, 1);
    MpStage(_Ci0, _Ai0, _Ot0, _K0, t, i.ras, prev, r0, r1, r2);
    if (n > 1)
    {
        t = _Ot1.w > 0.5 ? SAMPLE_TEXTURE2D(_Tex1, sampler_Tex1, i.uv01.zw) : float4(1, 1, 1, 1);
        MpStage(_Ci1, _Ai1, _Ot1, _K1, t, i.ras, prev, r0, r1, r2);
    }
    if (n > 2)
    {
        t = _Ot2.w > 0.5 ? SAMPLE_TEXTURE2D(_Tex2, sampler_Tex2, i.uv23.xy) : float4(1, 1, 1, 1);
        MpStage(_Ci2, _Ai2, _Ot2, _K2, t, i.ras, prev, r0, r1, r2);
    }
    if (n > 3)
    {
        t = _Ot3.w > 0.5 ? SAMPLE_TEXTURE2D(_Tex3, sampler_Tex3, i.uv23.zw) : float4(1, 1, 1, 1);
        MpStage(_Ci3, _Ai3, _Ot3, _K3, t, i.ras, prev, r0, r1, r2);
    }
    if (n > 4)
    {
        t = _Ot4.w > 0.5 ? SAMPLE_TEXTURE2D(_Tex4, sampler_Tex4, i.uv45.xy) : float4(1, 1, 1, 1);
        MpStage(_Ci4, _Ai4, _Ot4, _K4, t, i.ras, prev, r0, r1, r2);
    }
    if (n > 5)
    {
        t = _Ot5.w > 0.5 ? SAMPLE_TEXTURE2D(_Tex5, sampler_Tex5, i.uv45.zw) : float4(1, 1, 1, 1);
        MpStage(_Ci5, _Ai5, _Ot5, _K5, t, i.ras, prev, r0, r1, r2);
    }
    return prev * _ModColor;
}

// The thermal and x-ray visors: the world is drawn cold and dim through the game's thermal palette (actors warmer), the x-ray
// visor maps brightness through its palette (CCubeRenderer thermal / x-ray passes, approximated per pixel).
float3 MpVisorLook(float3 c)
{
    if (_MpHasPalettes < 0.5) return c;
    float lum = dot(c, float3(0.299, 0.587, 0.114));
    if (_MpVisor > 2.5)
    {
        // the 16-colour heat ramp (cyan, navy, blue, purple ... red, orange, yellow, white): the cold world stays in its navy-to-purple
        // part and dim (the game's cold pass darkens the scene's intensity), warm actors climb the hot end
        float idx = _Lit > 0.5 ? 7.0 + 8.0 * lum : 1.0 + 3.0 * lum;
        float3 heat = SAMPLE_TEXTURE2D(_MpThermalPalette, sampler_MpThermalPalette, float2((idx + 0.5) / 16.0, 0.5)).rgb;
        return _Lit > 0.5 ? heat : heat * (0.35 + 0.65 * lum);
    }

    if (_MpVisor > 0.5 && _MpVisor < 1.5)
        return SAMPLE_TEXTURE2D(_MpXRayPalette, sampler_MpXRayPalette, float2(saturate(1 - lum * 0.8), 0.5)).rgb;
    return c;
}

// GX alpha compare GEQUAL 64 (CCubeMaterial::SetupBlendMode)
void MpAlphaClip(float a)
{
    if (_AlphaTest > 0.5) clip(a - 64.0 / 255.0);
}

float3 MpFog(float3 c, float depth)
{
    if (_MpFogColor.a < 0.5) return c;
    float f = saturate((depth / max(1e-4, _MpWorldScale) - _MpFogRange.x) / max(1e-4, _MpFogRange.y - _MpFogRange.x));
    // additive surfaces fade to nothing, not to the fog colour
    bool additive = _DstBlend > 0.5 && _DstBlend < 1.5;
    return additive ? c * (1 - f) : lerp(c, _MpFogColor.rgb, f);
}

#endif
