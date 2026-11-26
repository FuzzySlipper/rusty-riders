// Stylized Metal Hurlant / Moebius ligne claire rift portal surface (`Portals.cs`, `content/levels/points.json`).
//
// Drawn on a double-sided circular disc tucked inside the portal machine's hollow ring aperture. Advances on
// `frame.time.x` (Engine presentation time, so it freezes cleanly while the world holds). Uses view-parallax offset
// into the ring's tangent plane for 3D abyss depth, crisp logarithmic comic-cel vortex spirals with saturated
// destination-tinted bands, counter-rotating astrolabe rune-arcs near the rim, and bold graphic ink contours.
//
// material.parameters[0]: rgb linear portal tint (entry / destination world / return), w glow intensity.
// material.parameters[1]: x swirl speed (rad/s), y pulse strength, z pulse hz, w ink contour strength (0..1).

#import rusty::types::Surface
#import rusty::material::material
#import rusty::view::frame
#import rusty::finish::finish

const TAU: f32 = 6.283185307;

fn hash21(p: vec2<f32>) -> f32 {
    var q = fract(p * vec2<f32>(123.34, 456.21));
    q += dot(q, q + 45.32);
    return fract(q.x * q.y);
}

fn noise(p: vec2<f32>) -> f32 {
    let i = floor(p);
    let f = fract(p);
    let u = f * f * (3.0 - 2.0 * f);
    let a = hash21(i);
    let b = hash21(i + vec2<f32>(1.0, 0.0));
    let c = hash21(i + vec2<f32>(0.0, 1.0));
    let d = hash21(i + vec2<f32>(1.0, 1.0));
    return mix(mix(a, b, u.x), mix(c, d, u.x), u.y);
}

fn fbm(p: vec2<f32>) -> f32 {
    var v = 0.0;
    var amp = 0.5;
    var q = p;
    for (var i = 0u; i < 3u; i++) {
        v += amp * noise(q);
        q = q * 2.05 + vec2<f32>(11.3, 5.9);
        amp *= 0.5;
    }
    return v;
}

fn shade(surface: Surface) -> vec4<f32> {
    // Screen derivatives in uniform control flow for view-parallax depth into the rift.
    let dp_dx = dpdx(surface.world_position);
    let dp_dy = dpdy(surface.world_position);
    let du_dx = dpdx(surface.uv);
    let du_dy = dpdy(surface.uv);
    let det = du_dx.x * du_dy.y - du_dy.x * du_dx.y;

    let raw_tint = material.parameters[0].rgb;
    let glow = material.parameters[0].w;
    let speed = material.parameters[1].x;
    let pulse_amp = material.parameters[1].y;
    let pulse_hz = material.parameters[1].z;
    let ink_strength = clamp(material.parameters[1].w, 0.0, 1.0);

    // Enrich hue saturation slightly so destination colours read jewel-rich through ACES tone mapping.
    let luma = dot(raw_tint, vec3<f32>(0.2126, 0.7152, 0.0722));
    let tint = clamp(vec3<f32>(luma) + (raw_tint - vec3<f32>(luma)) * 1.28, vec3<f32>(0.02), vec3<f32>(1.2));

    let t = frame.time.x;
    let p = surface.uv * 2.0 - vec2<f32>(1.0);
    let r = length(p);
    let angle = atan2(p.y, p.x);

    let rim_ripple = 0.018 * sin(angle * 7.0 + t * 1.3) + 0.010 * cos(angle * 13.0 - t * 2.0);
    let edge_r = 0.98 + rim_ripple;
    if r > 1.0 {
        discard;
    }

    // Parallax offset into the portal plane as the camera walks past the ring.
    var parallax = vec2<f32>(0.0);
    if abs(det) > 1e-12 {
        let along_u = (dp_dx * du_dy.y - dp_dy * du_dx.y) / det;
        let along_v = (dp_dy * du_dx.x - dp_dx * du_dy.x) / det;
        let n = normalize(cross(dp_dx, dp_dy));
        let tu = normalize(along_u - n * dot(n, along_u));
        let tv = normalize(along_v - n * dot(n, along_v));
        let view = normalize(frame.camera.xyz - surface.world_position);
        let depth = (1.0 - smoothstep(0.12, 0.96, r)) * 0.22;
        let vn = max(abs(dot(view, n)), 0.28);
        parallax = -vec2<f32>(dot(view, tu), dot(view, tv)) * (depth / vn);
    }

    let q = p + parallax;
    let qr = length(q);
    let qa = atan2(q.y, q.x);
    let breath = 1.0 + pulse_amp * sin(t * pulse_hz * TAU);

    // Clean, unmistakable logarithmic Moebius vortex spiral (4 main arms + harmonic ripples, gentle hand-drawn warp).
    let log_r = log(max(qr, 0.035));
    let swirl_dir = vec2<f32>(cos(qa - t * speed * 0.4), sin(qa - t * speed * 0.4));
    let warp = (fbm(swirl_dir * (qr * 2.1) + vec2<f32>(t * 0.16, -t * 0.13)) - 0.5) * 0.55;

    let arm_main = sin(qa * 4.0 - log_r * 7.2 - t * speed * 1.55 + warp * 1.8);
    let arm_sub = sin(qa * 2.0 - log_r * 4.4 - t * speed * 0.95 - warp * 1.1);
    let rings = sin(log_r * 10.5 + t * speed * 2.6 + warp * 1.2);

    // Center abyss darkens toward an indigo singularity surrounded by tight bright spiral filaments,
    // while the outer collar picks up luminous rim energy against the dark iron ring.
    let abyss_core = smoothstep(0.05, 0.34, qr);
    let rim_boost = smoothstep(0.76, 0.96, r) * 0.32;
    let field = clamp(0.42 + 0.30 * arm_main + 0.16 * arm_sub + 0.10 * rings + rim_boost, 0.0, 1.0) * (0.28 + 0.72 * abyss_core);

    // Four crisp cel-shaded graphic bands calibrated to keep deep void shadows and rich destination saturation.
    let b_deep = smoothstep(0.32, 0.36, field);
    let b_mid = smoothstep(0.56, 0.60, field);
    let b_high = smoothstep(0.79, 0.83, field);

    // Bold comic ink contours hugging the cel band boundaries.
    let ink1 = 1.0 - smoothstep(0.0, 0.036, abs(field - 0.34));
    let ink2 = 1.0 - smoothstep(0.0, 0.030, abs(field - 0.58));
    let ink3 = 1.0 - smoothstep(0.0, 0.024, abs(field - 0.81));
    var ink_line = max(max(ink1 * 0.95, ink2 * 0.75), ink3 * 0.50);

    // Counter-rotating concentric astrolabe arcs and fine inked calibration circles near the outer rim.
    let ring_outer = 1.0 - smoothstep(0.007, 0.019, abs(r - 0.87));
    let seg_outer = smoothstep(0.15, 0.25, sin(angle * 12.0 + t * speed * 0.45));
    let ring_inner = 1.0 - smoothstep(0.006, 0.016, abs(r - 0.71));
    let seg_inner = smoothstep(0.35, 0.45, sin(angle * 8.0 - t * speed * 0.65));
    let tick_ring = (1.0 - smoothstep(0.005, 0.013, abs(r - 0.79))) * smoothstep(0.55, 0.65, sin(angle * 24.0 + t * speed * 0.22));
    let astro_glow = ring_outer * seg_outer * 0.72 + ring_inner * seg_inner * 0.52 + tick_ring * 0.45;

    let astro_ink_a = (1.0 - smoothstep(0.005, 0.013, abs(r - 0.905))) * 0.75;
    let astro_ink_b = (1.0 - smoothstep(0.004, 0.011, abs(r - 0.68))) * 0.45;
    ink_line = clamp(max(ink_line, max(astro_ink_a, astro_ink_b)), 0.0, 1.0) * ink_strength;

    // Jewel-toned Metal Hurlant palette (keeps strong hue saturation through ACES instead of blowing to white):
    let void_col = vec3<f32>(0.018, 0.015, 0.045) + tint * 0.055;
    let deep_col = tint * 0.34;
    let mid_col = tint * 0.82;
    let high_col = mix(tint, vec3<f32>(1.0, 0.97, 0.92), 0.38) * 1.38;

    var col = mix(void_col, deep_col, b_deep);
    col = mix(col, mid_col, b_mid);
    col = mix(col, high_col, b_high);

    // Subtle glowing star-pin eye inside the dark central singularity + crisp rim collar.
    let core_eye = (1.0 - smoothstep(0.0, 0.09, qr)) * (0.65 + 0.25 * sin(t * 2.8));
    let rim_collar = smoothstep(0.88, 0.97, r) * (1.0 - smoothstep(0.97, 1.0, r));
    col += mix(tint, vec3<f32>(1.0, 0.98, 0.94), 0.55) * (astro_glow * 0.85 + rim_collar * 0.70 + core_eye * 0.95);

    // Apply glow & breathing, then punch deep ink contours on top so linework never washes out.
    var radiance = col * glow * breath;
    let ink_col = vec3<f32>(0.010, 0.008, 0.020);
    radiance = mix(radiance, ink_col, ink_line * 0.92);

    let alpha = 1.0 - smoothstep(edge_r - 0.035, edge_r + 0.02, r);
    if alpha <= 0.01 {
        discard;
    }
    return finish(vec4<f32>(radiance, alpha), surface.world_position);
}
