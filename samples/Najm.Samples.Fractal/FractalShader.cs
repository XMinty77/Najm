namespace Najm.Samples.Fractal;

/// <summary>
/// The author's GLSL ES 3.00 program. Najm never sees this source; it is compiled by the driver,
/// rendered into a texture this project owns, and handed to the engine as a texture id.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Colouring.</strong> Continuous (smooth) escape time with a large bailout radius, indexed
/// logarithmically into a nine-anchor ramp defined in <em>linear light</em>: ink, navy, petrol,
/// aqua, mint-cream, gold, burnt orange, plum, back to ink. Banding is a choice about ramp
/// frequency, not an artefact of integer iteration counts.
/// </para>
/// <para>
/// <strong>Distance estimation</strong> (the <c>dz/dc</c> recurrence) does two jobs. It fades the
/// ramp toward the deep base colour as the ramp's spatial frequency passes Nyquist near the set
/// boundary — without it, the boundary is an aliasing mess at any zoom — and it lifts a pale rim
/// out of the filaments, which is what makes the thing read as lit rather than tinted.
/// </para>
/// <para>
/// <strong>The iteration limit is a float, not an int.</strong> An integer limit pops: the interior
/// jumps outward a whole filament at a time. Here the limit animates continuously, because the
/// interior test is a <c>smoothstep</c> over the smooth iteration count rather than a comparison
/// against the loop bound, and the transition band itself is tinted so the changing limit reads as
/// a wavefront sweeping through the filaments.
/// </para>
/// <para>
/// <strong>Precision.</strong> The centre arrives split into two floats. Everything a pixel adds to
/// it is small, so the low part is added to the pixel offset <em>first</em> and the high part last —
/// a compensated sum that keeps <c>c</c> exact to roughly double precision. That buys the sample
/// positions; it does not buy the iteration, which is the real single-precision limit. See
/// NOTES.md, "Precision".
/// </para>
/// </remarks>
internal static class FractalShader
{
    /// <summary>A fullscreen triangle from <c>gl_VertexID</c> alone — no buffers, no attributes.</summary>
    internal const string Vertex =
        """
        #version 300 es
        void main()
        {
            vec2 corners[3] = vec2[3](vec2(-1.0, -1.0), vec2(3.0, -1.0), vec2(-1.0, 3.0));
            gl_Position = vec4(corners[gl_VertexID], 0.0, 1.0);
        }
        """;

    internal const string Fragment =
        """
        #version 300 es
        precision highp float;
        precision highp int;

        uniform vec2  uResolution;    // texture size in pixels
        uniform vec2  uCentreHi;      // complex centre, high half
        uniform vec2  uCentreLo;      // complex centre, low half; centre == Hi + Lo
        uniform float uScale;         // complex units per half-height of the frame
        uniform vec2  uRotor;         // (cos, sin) of the frame rotation
        uniform float uMaxIter;       // fractional and animated
        uniform float uPaletteShift;  // scrolls the ramp
        uniform float uBands;         // ramp cycles per smooth iteration (1 / band period)
        uniform float uNuFloor;       // the smooth iteration count the ramp's zero sits at
        uniform float uRimGain;       // strength of the distance-estimated filament rim
        uniform float uFrontGain;     // strength of the moving iteration-limit wavefront
        uniform float uExposure;
        uniform int   uSamples;       // 1 or 4 (rotated-grid supersampling)

        out vec4 fragColor;

        const float Bailout = 512.0;
        const float BailoutSq = Bailout * Bailout;
        const float LogBailout = log(Bailout);

        // The ramp, in linear light. Knots are unevenly spaced on purpose: the cool half of the
        // wheel gets more room than the warm half, so gold reads as an accent rather than a stripe.
        const vec3 K0 = vec3(0.00121, 0.00182, 0.00439);  // #04060e ink
        const vec3 K1 = vec3(0.00304, 0.00857, 0.02956);  // #0a1730 navy
        const vec3 K2 = vec3(0.00857, 0.05286, 0.11193);  // #17415e slate
        const vec3 K3 = vec3(0.04231, 0.25818, 0.29177);  // #3a8b93 teal
        const vec3 K4 = vec3(0.62396, 0.74540, 0.68669);  // #cfe0d8 pale
        const vec3 K5 = vec3(0.74540, 0.39676, 0.07619);  // #e0a94e amber
        const vec3 K6 = vec3(0.39157, 0.05951, 0.02519);  // #a8452c rust
        const vec3 K7 = vec3(0.06848, 0.00913, 0.03190);  // #4a1832 plum
        const float T1 = 0.13, T2 = 0.27, T3 = 0.40, T4 = 0.50;
        const float T5 = 0.59, T6 = 0.70, T7 = 0.83, T8 = 1.00;

        // The colour the boundary resolves to once the ramp is finer than a pixel, and the colour
        // of the interior. Sharing one value is what makes the set's edge read as a single object.
        const vec3 Deep = vec3(0.00230, 0.00310, 0.00700);
        const vec3 Rim  = vec3(0.95000, 0.86000, 0.66000);  // warm cream, added not mixed
        const vec3 Front = vec3(0.60000, 0.72000, 0.90000); // cool blue-white wavefront

        vec3 ramp(float t)
        {
            t = fract(t);
            vec3 c = K0;
            c = mix(c, K1, smoothstep(0.0, T1, t));
            c = mix(c, K2, smoothstep(T1, T2, t));
            c = mix(c, K3, smoothstep(T2, T3, t));
            c = mix(c, K4, smoothstep(T3, T4, t));
            c = mix(c, K5, smoothstep(T4, T5, t));
            c = mix(c, K6, smoothstep(T5, T6, t));
            c = mix(c, K7, smoothstep(T6, T7, t));
            c = mix(c, K0, smoothstep(T7, T8, t));
            return c;
        }

        // Exact interior tests for the main cardioid and the period-2 bulb. In the wide shot these
        // are most of the black pixels, and without them each one costs the full iteration budget.
        bool inKnownInterior(vec2 c)
        {
            float x = c.x - 0.25;
            float q = x * x + c.y * c.y;
            if (q * (q + x) <= 0.25 * c.y * c.y) { return true; }
            float bx = c.x + 1.0;
            return bx * bx + c.y * c.y <= 0.0625;
        }

        vec3 shade(vec2 c, float pixelSize)
        {
            if (inKnownInterior(c)) { return Deep; }

            vec2 z = vec2(0.0);
            vec2 dz = vec2(0.0);
            float m2 = 0.0;
            int cap = int(uMaxIter) + 8;
            int n = 0;
            for (n = 0; n < cap; n++)
            {
                // dz <- 2*z*dz + 1, the derivative of z_n with respect to c.
                dz = 2.0 * vec2(z.x * dz.x - z.y * dz.y, z.x * dz.y + z.y * dz.x) + vec2(1.0, 0.0);
                z = vec2(z.x * z.x - z.y * z.y, 2.0 * z.x * z.y) + c;
                m2 = dot(z, z);
                if (m2 > BailoutSq) { break; }
            }

            if (n >= cap) { return Deep; }

            // Continuous iteration count. The second log is the fractional part of the escape:
            // |z| overshoots the bailout by an amount that says where inside the last step it left.
            float logZ = 0.5 * log(m2);
            float nu = float(n) + 1.0 - log(logZ / LogBailout) / log(2.0);

            // Koebe distance estimate, in c-plane units, then in pixels.
            float dzLen = length(dz);
            float de = dzLen > 0.0 ? (sqrt(m2) * logZ / dzLen) : 0.0;
            float dePix = clamp(de / pixelSize, 0.0, 64.0);

            vec3 col = ramp(uPaletteShift + (uBands * (nu - uNuFloor)));

            // Below about a pixel from the boundary the ramp runs faster than the sampling grid can
            // carry, so resolve it to one colour instead of letting it alias.
            float near = exp(-dePix * 1.35);
            col = mix(col, Deep, near * 0.80);
            col += Rim * pow(near, 2.6) * uRimGain;

            // The animated limit. Exterior weight one, interior weight zero, a soft band between.
            // The band is a fraction of the limit rather than a fixed number of iterations: at a
            // limit of two thousand, three iterations of transition is a sub-pixel hard edge, and
            // the whole point is that the limit's movement is legible.
            float band = max(1.4, 0.018 * uMaxIter);
            float w = 1.0 - smoothstep(uMaxIter - band, uMaxIter + band, nu);
            col = mix(Deep, col, w);
            col += Front * (1.0 - abs(2.0 * w - 1.0)) * uFrontGain;

            return col;
        }

        vec3 tonemap(vec3 x)
        {
            // Exponential roll-off, not a filmic S-curve. The interior and the far field live at a
            // few thousandths of linear one, and every ACES-style approximation crushes that to
            // zero — which turns the set into a hole punched in the frame instead of the darkest
            // thing in it. This is linear in the shadows by construction.
            return vec3(1.0) - exp(-max(x * uExposure, vec3(0.0)));
        }

        vec3 encodeSrgb(vec3 c)
        {
            c = clamp(c, 0.0, 1.0);
            vec3 lo = c * 12.92;
            vec3 hi = 1.055 * pow(c, vec3(1.0 / 2.4)) - 0.055;
            return mix(lo, hi, step(vec3(0.0031308), c));
        }

        void main()
        {
            vec2 halfRes = 0.5 * uResolution;
            float invHalfHeight = 1.0 / halfRes.y;
            float pixelSize = uScale * invHalfHeight;

            // Rotated-grid 4x: better than an ordered grid on the near-axis filaments that dominate
            // this set, for the same four samples.
            vec2 offsets[4] = vec2[4](
                vec2( 0.125,  0.375), vec2( 0.375, -0.125),
                vec2(-0.125, -0.375), vec2(-0.375,  0.125));

            int samples = uSamples > 1 ? 4 : 1;
            vec3 sum = vec3(0.0);
            for (int s = 0; s < samples; s++)
            {
                vec2 offset = samples == 1 ? vec2(0.0) : offsets[s];
                vec2 p = (gl_FragCoord.xy + offset - halfRes) * invHalfHeight * uScale;
                vec2 q = vec2(p.x * uRotor.x - p.y * uRotor.y, p.x * uRotor.y + p.y * uRotor.x);

                // Compensated: the low half of the centre is the same magnitude as the pixel
                // offset, so they are summed together before the high half is added.
                vec2 c = uCentreHi + (uCentreLo + q);
                sum += shade(c, pixelSize);
            }

            vec3 linear = sum / float(samples);
            fragColor = vec4(encodeSrgb(tonemap(linear)), 1.0);
        }
        """;
}
