// A lit sprite shader with the four effects the game needs and the stock one cannot express:
// a hit flash, a knockout dissolve, a normal-mapped rim light and an inner outline.
//
// The rim and the outline say different things on purpose. The rim is *shape* - it traces the
// volume the sprite's normal map describes and is set once per material. The outline is
// *state*, driven per renderer from a property block: the counter window is open, or this is
// the fighter you are driving. Conflating them would mean a fighter could not be highlighted
// without also changing how round they look.
//
// Built as a variant of URP's Sprite-Lit-Default rather than from scratch, so the fighters keep
// responding to the ring's 2D lights and keep writing normals. Losing that to gain a flash
// would have been a bad trade.
//
// SRP Batcher compatibility: the UnityPerMaterial block is byte-identical in all three passes.
// The damage block (_BruiseColor, _SwellLeft, _SwellRight, _CutAmount) was added to all three
// together for exactly that reason - it is the easiest of the five effects to add to only the
// pass being looked at, and doing so drops the fighters out of the batcher with no error.
// Unity silently drops a shader out of the batcher if the layouts disagree between passes, so
// any property added here must be added to every one of them.
Shader "PoRumble/SpriteLitFX"
{
    Properties
    {
        _MainTex("Diffuse", 2D) = "white" {}
        _MaskTex("Mask", 2D) = "white" {}
        _NormalMap("Normal Map", 2D) = "bump" {}
        [MaterialToggle] _ZWrite("ZWrite", Float) = 0

        [Header(Impact)]
        _FlashColor("Flash Colour", Color) = (1,1,1,1)
        _FlashAmount("Flash Amount", Range(0,1)) = 0

        [Header(Knockout)]
        _DissolveAmount("Dissolve Amount", Range(0,1)) = 0
        _DissolveEdgeColor("Dissolve Edge", Color) = (1,0.45,0.2,1)

        // Rim reads the sprite's normal map, so it only says anything on a sprite that has
        // one bound as a secondary texture. On a flat sprite the term is identically zero.
        [Header(Rim)]
        _RimColor("Rim Colour", Color) = (1,0.93,0.78,1)
        _RimAmount("Rim Amount", Range(0,2)) = 0
        _RimPower("Rim Falloff", Range(0.5,8)) = 2.5

        // A state tell rather than an impact: the counter window, and marking which fighter
        // in a ten-way is yours. Drawn inward from the silhouette - see SpriteFX.hlsl.
        [Header(Outline)]
        _OutlineColor("Outline Colour", Color) = (1,0.85,0.25,1)
        _OutlineAmount("Outline Amount", Range(0,1)) = 0
        _OutlineWidth("Outline Width (px)", Range(0,6)) = 1.5

        // Accumulated damage, written per renderer by BoxerView and only ever onto the head.
        // Two swelling values rather than one, because which side a fighter has been marked
        // on is the whole tell - a single averaged number renders every boxer equally puffy.
        [Header(Damage)]
        _BruiseColor("Bruise Tint", Color) = (0.62,0.36,0.46,1)
        _SwellLeft("Swell Left", Range(0,1)) = 0
        _SwellRight("Swell Right", Range(0,1)) = 0
        _CutAmount("Cut Amount", Range(0,1)) = 0

        // Sweat. Driven per renderer by BoxerView from the same fatigue measure that droops
        // the guard, and written onto the head alone for the reason the bruise is. Reads the
        // sprite's normal map like the rim does, so it only says anything where there is
        // volume for a highlight to sit on.
        [Header(Sheen)]
        _SheenColor("Sheen Colour", Color) = (1,0.98,0.94,1)
        _SheenAmount("Sheen Amount", Range(0,1)) = 0
        _SheenPower("Sheen Tightness", Range(2,64)) = 22

        // Legacy properties, kept so materials can fall back to the built-in sprite shader.
        [HideInInspector] _Color("Tint", Color) = (1,1,1,1)
        [HideInInspector] _RendererColor("RendererColor", Color) = (1,1,1,1)
        [HideInInspector] _AlphaTex("External Alpha", 2D) = "white" {}
        [HideInInspector] _EnableExternalAlpha("Enable External Alpha", Float) = 0
    }

    SubShader
    {
        Tags {"Queue" = "Transparent" "RenderType" = "Transparent" "RenderPipeline" = "UniversalPipeline" }

        Blend SrcAlpha OneMinusSrcAlpha, One OneMinusSrcAlpha
        Cull Off
        ZWrite [_ZWrite]

        Pass
        {
            Tags { "LightMode" = "Universal2D" }

            HLSLPROGRAM
            #include "Packages/com.unity.render-pipelines.universal/Shaders/2D/Include/Core2D.hlsl"

            #pragma vertex LitVertex
            #pragma fragment LitFragment

            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/Shaders/2D/Include/ShapeLightShared.hlsl"

            #pragma multi_compile_instancing
            #pragma multi_compile _ DEBUG_DISPLAY
            #pragma multi_compile _ SKINNED_SPRITE

            struct Attributes
            {
                COMMON_2D_INPUTS
                half4 color        : COLOR;
                UNITY_SKINNED_VERTEX_INPUTS
            };

            struct Varyings
            {
                COMMON_2D_LIT_OUTPUTS
                half4 color        : COLOR;
            };

            #include "Packages/com.unity.render-pipelines.universal/Shaders/2D/Include/Lit2DCommon.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
                half4 _FlashColor;
                half4 _DissolveEdgeColor;
                half4 _RimColor;
                half4 _OutlineColor;
                half4 _BruiseColor;
                half4 _SheenColor;
                half _FlashAmount;
                half _DissolveAmount;
                half _RimAmount;
                half _RimPower;
                half _OutlineAmount;
                half _OutlineWidth;
                half _SwellLeft;
                half _SwellRight;
                half _CutAmount;
                half _SheenAmount;
                half _SheenPower;
            CBUFFER_END

            #include "SpriteFX.hlsl"

            Varyings LitVertex(Attributes input)
            {
                UNITY_SKINNED_VERTEX_COMPUTE(input);
                SetUpSpriteInstanceProperties();
                input.positionOS = UnityFlipSprite(input.positionOS, unity_SpriteProps.xy);

                Varyings o = CommonLitVertex(input);
                o.color = input.color * _Color * unity_SpriteColor;

                return o;
            }

            half4 LitFragment(Varyings input) : SV_Target
            {
                half4 lit = CommonLitFragment(input, input.color);

                // Sampled a second time rather than plumbed out of CommonLitFragment, which
                // returns only the shaded colour. One extra fetch of a texture already in
                // cache is cheaper than forking URP's include to hand the normal back.
                half3 normalTS = UnpackNormal(SAMPLE_TEXTURE2D(_NormalMap, sampler_NormalMap, input.uv));

                half innerEdge = 0.0;
                if (_OutlineAmount > 0.0)
                {
                    float2 step = PoRumbleOutlineStep(input.uv, _OutlineWidth);
                    half ownAlpha = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv).a;
                    innerEdge = PoRumbleInnerEdge(TEXTURE2D_ARGS(_MainTex, sampler_MainTex),
                                                  input.uv, step, ownAlpha);
                }

                return ApplySpriteFX(lit, input.uv, _FlashColor, _FlashAmount,
                                     _DissolveAmount, _DissolveEdgeColor,
                                     normalTS, _RimColor, _RimAmount, _RimPower,
                                     _OutlineColor, _OutlineAmount, innerEdge,
                                     _SwellLeft, _SwellRight, _CutAmount, _BruiseColor,
                                     _SheenAmount, _SheenPower, _SheenColor);
            }
            ENDHLSL
        }

        Pass
        {
            Tags { "LightMode" = "NormalsRendering"}

            HLSLPROGRAM
            #include "Packages/com.unity.render-pipelines.universal/Shaders/2D/Include/Core2D.hlsl"

            #pragma vertex NormalsRenderingVertex
            #pragma fragment NormalsRenderingFragment

            #pragma multi_compile_instancing
            #pragma multi_compile _ SKINNED_SPRITE

            struct Attributes
            {
                COMMON_2D_NORMALS_INPUTS
                float4 color        : COLOR;
                UNITY_SKINNED_VERTEX_INPUTS
            };

            struct Varyings
            {
                COMMON_2D_NORMALS_OUTPUTS
                half4   color           : COLOR;
            };

            #include "Packages/com.unity.render-pipelines.universal/Shaders/2D/Include/Normals2DCommon.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
                half4 _FlashColor;
                half4 _DissolveEdgeColor;
                half4 _RimColor;
                half4 _OutlineColor;
                half4 _BruiseColor;
                half4 _SheenColor;
                half _FlashAmount;
                half _DissolveAmount;
                half _RimAmount;
                half _RimPower;
                half _OutlineAmount;
                half _OutlineWidth;
                half _SwellLeft;
                half _SwellRight;
                half _CutAmount;
                half _SheenAmount;
                half _SheenPower;
            CBUFFER_END

            Varyings NormalsRenderingVertex(Attributes input)
            {
                UNITY_SKINNED_VERTEX_COMPUTE(input);
                SetUpSpriteInstanceProperties();
                input.positionOS = UnityFlipSprite(input.positionOS, unity_SpriteProps.xy);

                Varyings o = CommonNormalsVertex(input);
                o.color = input.color * _Color * unity_SpriteColor;

                return o;
            }

            half4 NormalsRenderingFragment(Varyings input) : SV_Target
            {
                SetUpSpriteInstanceProperties();
                return CommonNormalsFragment(input, input.color);
            }
            ENDHLSL
        }

        Pass
        {
            Tags { "LightMode" = "UniversalForward" "Queue"="Transparent" "RenderType"="Transparent"}

            HLSLPROGRAM
            #include "Packages/com.unity.render-pipelines.universal/Shaders/2D/Include/Core2D.hlsl"

            #pragma vertex UnlitVertex
            #pragma fragment UnlitFragment

            struct Attributes
            {
                COMMON_2D_INPUTS
                half4 color : COLOR;
                UNITY_SKINNED_VERTEX_INPUTS
            };

            struct Varyings
            {
                COMMON_2D_OUTPUTS
                half4 color : COLOR;
            };

            #include "Packages/com.unity.render-pipelines.universal/Shaders/2D/Include/2DCommon.hlsl"

            #pragma multi_compile_instancing
            #pragma multi_compile _ DEBUG_DISPLAY SKINNED_SPRITE

            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
                half4 _FlashColor;
                half4 _DissolveEdgeColor;
                half4 _RimColor;
                half4 _OutlineColor;
                half4 _BruiseColor;
                half4 _SheenColor;
                half _FlashAmount;
                half _DissolveAmount;
                half _RimAmount;
                half _RimPower;
                half _OutlineAmount;
                half _OutlineWidth;
                half _SwellLeft;
                half _SwellRight;
                half _CutAmount;
                half _SheenAmount;
                half _SheenPower;
            CBUFFER_END

            #include "SpriteFX.hlsl"

            Varyings UnlitVertex(Attributes input)
            {
                UNITY_SKINNED_VERTEX_COMPUTE(input);
                SetUpSpriteInstanceProperties();
                input.positionOS = UnityFlipSprite(input.positionOS, unity_SpriteProps.xy);

                Varyings o = CommonUnlitVertex(input);
                o.color = input.color *_Color * unity_SpriteColor;
                return o;
            }

            half4 UnlitFragment(Varyings input) : SV_Target
            {
                half4 lit = CommonUnlitFragment(input, input.color);

                half innerEdge = 0.0;
                if (_OutlineAmount > 0.0)
                {
                    float2 step = PoRumbleOutlineStep(input.uv, _OutlineWidth);
                    half ownAlpha = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv).a;
                    innerEdge = PoRumbleInnerEdge(TEXTURE2D_ARGS(_MainTex, sampler_MainTex),
                                                  input.uv, step, ownAlpha);
                }

                // 2DCommon.hlsl declares no _NormalMap, and this pass never runs under the
                // 2D renderer anyway - it is the fallback for a forward camera. A flat normal
                // zeroes the rim term rather than reading an unbound sampler.
                return ApplySpriteFX(lit, input.uv, _FlashColor, _FlashAmount,
                                     _DissolveAmount, _DissolveEdgeColor,
                                     half3(0.0, 0.0, 1.0), _RimColor, _RimAmount, _RimPower,
                                     _OutlineColor, _OutlineAmount, innerEdge,
                                     _SwellLeft, _SwellRight, _CutAmount, _BruiseColor,
                                     _SheenAmount, _SheenPower, _SheenColor);
            }
            ENDHLSL
        }
    }

    Fallback "Universal Render Pipeline/2D/Sprite-Lit-Default"
}
