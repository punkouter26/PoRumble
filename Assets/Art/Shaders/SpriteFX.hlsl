#ifndef PORUMBLE_SPRITE_FX_INCLUDED
#define PORUMBLE_SPRITE_FX_INCLUDED

// Value noise from a hashed lattice. Procedural on purpose: a dissolve texture would be one
// more asset to atlas and one more sampler in a shader that is already sampling three.
half PoRumbleHash(float2 cell)
{
    return frac(sin(dot(cell, float2(127.1, 311.7))) * 43758.5453);
}

half PoRumbleNoise(float2 uv)
{
    float2 cell = floor(uv);
    float2 f = frac(uv);

    // Smoothstep the interpolant so the dissolve edge reads as torn rather than blocky.
    f = f * f * (3.0 - 2.0 * f);

    half a = PoRumbleHash(cell);
    half b = PoRumbleHash(cell + float2(1.0, 0.0));
    half c = PoRumbleHash(cell + float2(0.0, 1.0));
    half d = PoRumbleHash(cell + float2(1.0, 1.0));

    return lerp(lerp(a, b, f.x), lerp(c, d, f.x), f.y);
}

// Distance, in UV, that the outline reaches inward from the silhouette.
//
// Derived from fwidth rather than from _MainTex_TexelSize, for two reasons. A texel-based
// width would thicken and thin as the spectator camera pulls out over a ten-way, because the
// sprite covers fewer screen pixels while its texel count stays fixed. And _MainTex_TexelSize
// would have to live in UnityPerMaterial to keep the shader in the SRP Batcher, which means
// declaring it identically in all three passes for a number the hardware already knows.
float2 PoRumbleOutlineStep(float2 uv, half width)
{
    return fwidth(uv) * max(width, 0.0);
}

// The inner edge of the silhouette: high where this pixel is opaque but a neighbour is not.
//
// Inward rather than outward, and not by preference. A sprite's quad is tight to its own
// bounds and the atlas packs neighbours right up against the padding, so an outline drawn
// outward would either clip at the quad edge or sample whatever sprite was packed next to it.
half PoRumbleInnerEdge(TEXTURE2D_PARAM(tex, samp), float2 uv, float2 step, half ownAlpha)
{
    half neighbour = SAMPLE_TEXTURE2D(tex, samp, uv + float2(step.x, 0.0)).a;
    neighbour = min(neighbour, SAMPLE_TEXTURE2D(tex, samp, uv - float2(step.x, 0.0)).a);
    neighbour = min(neighbour, SAMPLE_TEXTURE2D(tex, samp, uv + float2(0.0, step.y)).a);
    neighbour = min(neighbour, SAMPLE_TEXTURE2D(tex, samp, uv - float2(0.0, step.y)).a);
    return saturate(ownAlpha - neighbour);
}

// Applies rim light, outline, hit flash and knockout dissolve on top of a shaded sprite.
//
// The order is deliberate and each step depends on the one before it:
//   rim      - shape, so it sits under everything that is an event
//   outline  - a state tell (counter window, the player's own fighter), over the shape
//   flash    - the impact itself, which should wash out both of the above
//   dissolve - last, because it eats alpha and nothing may draw into what it removed
//
// normalTS is the tangent-space normal already unpacked by the caller. The unlit pass has no
// normal map bound, so it passes a flat (0,0,1) and the rim term falls out to zero on its own.
half4 ApplySpriteFX(
    half4 shaded,
    float2 uv,
    half4 flashColor,
    half flashAmount,
    half dissolveAmount,
    half4 dissolveEdgeColor,
    half3 normalTS,
    half4 rimColor,
    half rimAmount,
    half rimPower,
    half4 outlineColor,
    half outlineAmount,
    half innerEdge)
{
    // Rim from the normal map's z: 1 where the surface faces the viewer, 0 where it has
    // turned side-on. Now that the sprites carry real domes this traces the actual volume of
    // a glove or a shoulder, which is the whole reason the normal maps were worth generating.
    if (rimAmount > 0.0)
    {
        half facing = saturate(normalTS.z);
        half rim = pow(saturate(1.0 - facing), max(rimPower, 0.001));
        // Scaled by alpha so the rim cannot paint into transparent pixels and give the
        // sprite a halo where its silhouette should simply end.
        shaded.rgb += rimColor.rgb * (rim * rimAmount * shaded.a);
    }

    if (outlineAmount > 0.0)
    {
        shaded.rgb = lerp(shaded.rgb, outlineColor.rgb, saturate(innerEdge * outlineAmount));
    }

    // Preserve alpha: the flash must not make transparent pixels appear.
    shaded.rgb = lerp(shaded.rgb, flashColor.rgb, saturate(flashAmount));

    if (dissolveAmount > 0.0)
    {
        half noise = PoRumbleNoise(uv * 14.0);

        // Cut everything below the threshold away entirely.
        half threshold = dissolveAmount;
        half remaining = noise - threshold;

        // A glowing rim just above the cut line, which is what makes a dissolve read as
        // burning rather than as fading out.
        half edge = 1.0 - saturate(remaining / 0.12);
        shaded.rgb = lerp(shaded.rgb, dissolveEdgeColor.rgb, saturate(edge) * step(0.0, remaining));
        shaded.a *= step(0.0, remaining);
    }

    return shaded;
}

#endif
