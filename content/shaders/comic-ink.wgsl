// Graphic French-comic ink edge outlines over the finished view (`CameraView.SetImageEffect`).
//
// Combines scale-invariant screen-space depth silhouette jumps (`dpdx`/`dpdy` on `pixel.depth`) with structural
// colour/chroma boundaries sampled from `picture_at`, fading gently into distant fog so silhouettes of piers, ribs,
// arches, ledges and doorways get crisp ink contours without muddying internal stone textures.
//
// effect_parameter(0): x depth silhouette threshold, y colour/chroma boundary threshold, z ink strength (0..1), w depth fade ceiling.
// effect_parameter(1): rgb ink colour (display/sRGB space).

#import rusty::image::{ImagePixel, picture_at, effect_parameter, picture_size}

fn luma(c: vec3<f32>) -> f32 {
    return dot(c, vec3<f32>(0.2126, 0.7152, 0.0722));
}

fn image_effect(pixel: ImagePixel) -> vec4<f32> {
    let p0 = effect_parameter(0u);
    let ink = effect_parameter(1u).rgb;
    let depth_thresh = max(p0.x, 1e-4);
    let color_thresh = max(p0.y, 1e-4);
    let strength = clamp(p0.z, 0.0, 1.0);
    let fade_end = p0.w;

    // Screen derivatives in uniform control flow before any branch.
    let dd_dx = abs(dpdx(pixel.depth));
    let dd_dy = abs(dpdy(pixel.depth));

    if strength <= 0.0 || pixel.depth >= 0.9999 {
        return pixel.color;
    }

    // Standard perspective depth (0 near .. 1 far): (1 - d) is proportional to 1 / z_view,
    // so |dd| / (1 - d) approximates relative view-distance jump per pixel (scale-invariant).
    let inv_z = max(1.0 - pixel.depth, 1e-4);
    let rel_dx = dd_dx / inv_z;
    // Slight damping on vertical gradient so flat floors viewed at grazing angles do not outline themselves.
    let rel_dy = (dd_dy / inv_z) * 0.72;
    let depth_jump = max(rel_dx, rel_dy);
    let depth_edge = smoothstep(depth_thresh, depth_thresh * 2.4, depth_jump);

    // Structural colour / chroma boundary (e.g. red pilaster or arch meeting grey wall, runner edge on floor)
    // using a 4-neighbour cross so thin 1 px lines stay crisp without smudging.
    let texel = 1.0 / max(picture_size(), vec2<f32>(1.0));
    let c = pixel.color.rgb;
    let cl = picture_at(pixel.uv + vec2<f32>(-texel.x, 0.0)).rgb;
    let cr = picture_at(pixel.uv + vec2<f32>(texel.x, 0.0)).rgb;
    let cu = picture_at(pixel.uv + vec2<f32>(0.0, -texel.y)).rgb;
    let cd = picture_at(pixel.uv + vec2<f32>(0.0, texel.y)).rgb;

    // Emphasize hue/chroma transitions over pure luminance specks inside stone textures.
    let grad_x = abs(cr - cl);
    let grad_y = abs(cd - cu);
    let chroma_x = max(max(grad_x.r, grad_x.g), grad_x.b);
    let chroma_y = max(max(grad_y.r, grad_y.g), grad_y.b);
    let luma_edge = max(abs(luma(cr) - luma(cl)), abs(luma(cd) - luma(cu)));
    let color_jump = max(max(chroma_x, chroma_y) * 0.85, luma_edge * 0.55);
    let color_edge = smoothstep(color_thresh, color_thresh * 2.1, color_jump) * 0.55;

    // Distance fade so distant geometry fades smoothly into fog instead of becoming noisy black lines.
    let dist_fade = 1.0 - smoothstep(fade_end - 0.012, fade_end, pixel.depth);
    let edge = clamp(max(depth_edge, color_edge), 0.0, 1.0) * strength * dist_fade;

    return vec4<f32>(mix(c, ink, edge), pixel.color.a);
}
