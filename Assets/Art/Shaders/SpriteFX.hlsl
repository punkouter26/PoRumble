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

// Applies the hit flash and the knockout dissolve on top of an already-shaded sprite colour.
//
// Flash is applied before dissolve so a boxer knocked out by the punch that is flashing them
// still burns away from the flashed colour rather than snapping back to their own tint.
half4 ApplySpriteFX(
    half4 shaded,
    float2 uv,
    half4 flashColor,
    half flashAmount,
    half dissolveAmount,
    half4 dissolveEdgeColor)
{
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
