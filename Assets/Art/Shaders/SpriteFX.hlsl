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

// Swelling and a cut, drawn into the sprite's own UV space.
//
// UV space rather than world space, and that is the point of doing it in the shader at all:
// the head is a child of the torso and turns with the fighter, so a mark placed at uv.x 0.25
// stays on the same cheek for the whole match however the boxer pivots. Anything computed
// from a world direction would slide around the head as the fighter turned.
//
// The vertical band keeps both marks up around the eye and brow. A swelling that covered the
// whole sprite evenly would read as the fighter having been recoloured rather than hit, and
// the faces are circular-cropped photographs, so the interesting half of the sprite is the
// top of it.
//
// Note the side convention: uv.x below 0.5 is taken as the fighter's left. If a head sprite
// is ever authored mirrored, the two arguments swap at the call site rather than here - the
// shader has no way to know which way round a photograph was cropped.
half3 PoRumbleApplyBruise(
    half3 shaded,
    float2 uv,
    half swellLeft,
    half swellRight,
    half cut,
    half4 bruiseColor)
{
    // Wide, soft side masks that overlap in the middle, so a punch straight down the centre
    // marks both cheeks a little instead of picking one at random.
    half leftMask = smoothstep(0.62, 0.16, uv.x);
    half rightMask = smoothstep(0.38, 0.84, uv.x);

    // A soft horizontal band across the eyes. Gaussian rather than a smoothstep pair because
    // it has to fall off in both directions and a single exp is cheaper than two steps.
    half fromBrow = (uv.y - 0.60) * 3.2;
    half band = exp(-fromBrow * fromBrow);

    half swelling = saturate((leftMask * swellLeft + rightMask * swellRight) * band);

    // Multiplied toward the bruise colour rather than lerped to it: a bruise darkens the skin
    // that is already there. Lerping would paint the same flat purple onto every fighter and
    // lose the photograph underneath.
    shaded = lerp(shaded, shaded * bruiseColor.rgb, swelling);

    if (cut > 0.0)
    {
        // A short diagonal above the brow. Signed distance to a sloped line, clipped to the
        // middle of the face so it reads as a cut over one eye rather than as a scratch
        // across the whole head.
        half toLine = abs((uv.y - 0.74) - (uv.x - 0.5) * 0.30);
        half streak = smoothstep(0.035, 0.004, toLine);
        half span = smoothstep(0.16, 0.30, uv.x) * smoothstep(0.74, 0.60, uv.x);

        shaded = lerp(shaded, bruiseColor.rgb * 0.35, saturate(streak * span * cut));
    }

    return shaded;
}

// A wet highlight, read off the sprite's normal map.
//
// A directional term rather than the rim's facing term, and that is what makes it read as
// sweat rather than as a second rim. Rim asks "has this surface turned away from me", which
// traces a silhouette; a specular glint asks "is this surface angled to bounce the key light
// into my eye", which picks out the few spots on a brow or a shoulder that actually catch it.
// Take the facing term instead and a tired fighter simply glows at the edges.
//
// The direction is a constant rather than the real key light's. The key light moves - it
// follows the survivors across the ring - and a highlight that slid around a fighter's head as
// the rig drifted would read as the fighter turning rather than as the light moving. A fixed
// up-and-left source is the convention the hand-authored sprite shading already assumes.
half3 PoRumbleApplySheen(
    half3 shaded,
    half alpha,
    half3 normalTS,
    half amount,
    half power,
    half4 sheenColor)
{
    const half3 keyDirection = half3(-0.371, 0.557, 0.743);

    half facing = saturate(dot(normalize(normalTS), keyDirection));
    half glint = pow(facing, max(power, 1.0));

    // Additive and scaled by alpha, like the rim: sweat is light coming off the fighter, and
    // adding it into transparent pixels would give the silhouette a halo.
    return shaded + sheenColor.rgb * (glint * amount * alpha);
}

// Applies sweat, rim light, outline, hit flash and knockout dissolve on top of a shaded sprite.
//
// The order is deliberate and each step depends on the one before it:
//   bruise   - accumulated damage, which is part of the sprite by the time anything else runs
//   sheen    - sweat sitting on that surface, so it lies over the bruise rather than under it
//   rim      - shape, so it sits under everything that is an event
//   outline  - a state tell (counter window, the player's own fighter), over the shape
//   flash    - the impact itself, which should wash out all of the above
//   dissolve - last, because it eats alpha and nothing may draw into what it removed
//
// The bruise goes first because it is the only one of these that is a change to the
// fighter rather than a thing happening to them: a rim light should trace a swollen face,
// and a hit flash should white it out, which only works in that order. Sweat follows it for
// the same reason in miniature - it is on the skin, and the skin is already bruised.
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
    half innerEdge,
    half swellLeft,
    half swellRight,
    half cutAmount,
    half4 bruiseColor,
    half sheenAmount,
    half sheenPower,
    half4 sheenColor)
{
    if (swellLeft > 0.0 || swellRight > 0.0 || cutAmount > 0.0)
    {
        shaded.rgb = PoRumbleApplyBruise(
            shaded.rgb, uv, swellLeft, swellRight, cutAmount, bruiseColor);
    }

    if (sheenAmount > 0.0)
    {
        shaded.rgb = PoRumbleApplySheen(
            shaded.rgb, shaded.a, normalTS, sheenAmount, sheenPower, sheenColor);
    }

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
