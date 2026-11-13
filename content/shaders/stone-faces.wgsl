// Stone faces carved in the shader (kit recipe `stoneFaces`; StoneShape.Faces.cs builds the stones).
//
// A stone is a plain box of its size and jut. Its face vertices' product data carries where they lie on the stone (xy: 0
// to 1 along and up) and the stone's shape (zw: whole numbers, decoded below as StoneShape.Data packs them; z has bit 19
// set). This stage finds where the view ray meets the carved face under the box's top (parallax), then shades the stone's
// texture there with the carved face's normal: its bevel, the bulge and facets of its kind, chipped corners and nicked
// edges. Vertices without data (zero) are not stones and draw as usual.
//
// material.parameters[0]: x how deep the carving is, times the kind's; y parallax steps; z how dark the hollows go.

#import rusty::types::Surface
#import rusty::material::{material, albedo, albedo_sampler, normal_map, normal_sampler}
#import rusty::surface::transform_uv
#import rusty::view::frame
#import rusty::shade::standard_shade

struct Stone {
    size: vec2<f32>, // metres along and up
    seed: u32,
    hewn: bool,
    chip_chance: f32,
    nicks: f32,
    bevel: f32,
    pillow: f32,
    facet: f32,
    chip: f32, // the most a chip or nick takes, metres
    open: f32, // how deep a chip or nick cuts before the stone is gone there
};

fn hash(seed: u32, salt: u32) -> f32 {
    var x = seed * 747796405u + salt * 2891336453u + 1u;
    x = ((x >> ((x >> 28u) + 4u)) ^ x) * 277803737u;
    x = (x >> 22u) ^ x;
    return f32(x & 0xffffffu) / 16777216.0;
}

const STONE: u32 = 1u << 19u;

fn decode(data: vec4<f32>, size: vec2<f32>) -> Stone {
    let a = u32(round(data.z));
    let c = u32(round(data.w));
    let depth = material.parameters[0].x;
    var s: Stone;
    s.size = size;
    s.seed = a & 255u;
    s.hewn = ((a >> 8u) & 1u) == 1u;
    s.chip_chance = f32((a >> 9u) & 7u) / 7.0;
    s.nicks = f32((a >> 12u) & 7u) / 7.0;
    s.bevel = f32((a >> 15u) & 15u) * 0.01 * depth;
    s.pillow = f32(c & 127u) * 0.001 * depth;
    s.facet = f32((c >> 7u) & 63u) * 0.001 * depth;
    s.chip = min(f32((c >> 13u) & 31u) * 0.01, min(size.x, size.y) / 3.0);
    s.open = s.pillow + s.bevel + 0.025;
    return s;
}

// How far under the box's top the carved face lies at `p`, metres from the stone's bottom-left; past `NONE` where a
// chip or nick has taken the stone away.
const NONE: f32 = 1000.0;

fn carved(s: Stone, p: vec2<f32>) -> f32 {
    let size = s.size;
    let edge = min(min(p.x, size.x - p.x), min(p.y, size.y - p.y));
    var d = 0.0;
    // The bevel: down `bevel` at the edge, meeting the face 1.4 bevels in, as the mesh's ring does.
    if s.bevel > 0.0 {
        d += s.bevel * max(1.0 - edge / (s.bevel * 1.4), 0.0);
    }
    // The bulge, from a peak (off the middle for a hewn face) out to the rim, which stands up to `facet` proud of or
    // below the rest by sector round the peak: straight between sectors (facets) when hewn, eased otherwise.
    var peak = size * 0.5;
    if s.hewn {
        peak += (vec2<f32>(hash(s.seed, 1u), hash(s.seed, 2u)) - 0.5) * 0.5 * size;
    }
    let q = p - peak;
    let reach = vec2<f32>(select(peak.x, size.x - peak.x, q.x > 0.0), select(peak.y, size.y - peak.y, q.y > 0.0));
    let t = clamp(max(abs(q.x) / max(reach.x, 1e-4), abs(q.y) / max(reach.y, 1e-4)), 0.0, 1.0);
    let turn = (atan2(q.y, q.x) / 6.2831853 + 0.5) * 7.0;
    let k = min(u32(floor(turn)), 6u);
    let f = turn - f32(k);
    let l0 = hash(s.seed, 10u + k) * 2.0 - 1.0;
    let l1 = hash(s.seed, 10u + (k + 1u) % 7u) * 2.0 - 1.0;
    if s.hewn {
        d += (s.pillow - mix(l0, l1, f) * s.facet) * t;
    } else {
        d += s.pillow * t * t - mix(l0, l1, smoothstep(0.0, 1.0, f)) * s.facet * t;
    }
    // Chipped corners, cut back from a line across the corner, and nicked edges: V notches into an edge.
    let most = s.chip;
    var cut = 0.0;
    if most > 0.0 {
        for (var c = 0u; c < 4u; c++) {
            if hash(s.seed, 20u + c) < s.chip_chance {
                let corner = vec2<f32>(select(0.0, size.x, c == 1u || c == 2u), select(0.0, size.y, c >= 2u));
                let local = abs(p - corner);
                let legs = vec2<f32>(mix(0.3, 1.0, hash(s.seed, 24u + c)), mix(0.3, 1.0, hash(s.seed, 28u + c))) * most;
                let inside = 1.0 - (local.x / legs.x + local.y / legs.y);
                let chipped = s.bevel + inside * max(legs.x, legs.y) * 12.0; // a short broken slope, then open
                if chipped > s.open {
                    return NONE;
                }
                cut = max(cut, select(0.0, chipped, inside > 0.0));
            }
            if hash(s.seed, 32u + c) < s.nicks {
                // Edge c runs from corner c to the next, anticlockwise.
                var along = p.x;
                var into = p.y;
                var length = size.x;
                if c == 1u {
                    along = p.y;
                    into = size.x - p.x;
                    length = size.y;
                } else if c == 2u {
                    along = size.x - p.x;
                    into = size.y - p.y;
                } else if c == 3u {
                    along = size.y - p.y;
                    into = p.x;
                    length = size.y;
                }
                let width = mix(0.4, 1.0, hash(s.seed, 36u + c)) * most;
                let deep = mix(0.2, 0.5, hash(s.seed, 40u + c)) * most;
                let at = mix(0.25, 0.75, hash(s.seed, 44u + c)) * length;
                if width < length / 3.0 {
                    let left = deep * (1.0 - abs(along - at) / (width * 0.5)) - into;
                    let nicked = s.bevel + left * 12.0;
                    if nicked > s.open {
                        return NONE;
                    }
                    cut = max(cut, select(0.0, nicked, left > 0.0));
                }
            }
        }
    }
    return max(d + cut, 0.0);
}

// The texture gradient through the material's uv transform.
fn base_gradient(g: vec2<f32>) -> vec2<f32> {
    return vec2<f32>(dot(material.base_uv_u.xy, g), dot(material.base_uv_v.xy, g));
}

fn normal_gradient(g: vec2<f32>) -> vec2<f32> {
    return vec2<f32>(dot(material.normal_uv_u.xy, g), dot(material.normal_uv_v.xy, g));
}

fn shade(surface: Surface) -> vec4<f32> {
    let data = surface.product_data;
    // Screen derivatives first, in uniform control flow: the stone's place, the world and the texture uv.
    let dp_dx = dpdx(surface.world_position);
    let dp_dy = dpdy(surface.world_position);
    let dl_dx = dpdx(data.xy);
    let dl_dy = dpdy(data.xy);
    let dt_dx = dpdx(surface.uv);
    let dt_dy = dpdy(surface.uv);
    let det = dl_dx.x * dl_dy.y - dl_dy.x * dl_dx.y;
    var lit = surface;
    if (u32(round(data.z)) & STONE) != 0u && abs(det) > 1e-14 {
        // The stone's frame: world and texture uv per unit of its place along (u) and up (v).
        let along_u = (dp_dx * dl_dy.y - dp_dy * dl_dx.y) / det;
        let along_v = (dp_dy * dl_dx.x - dp_dx * dl_dy.x) / det;
        let tex_u = (dt_dx * dl_dy.y - dt_dy * dl_dx.y) / det;
        let tex_v = (dt_dy * dl_dx.x - dt_dx * dl_dy.x) / det;
        let view = normalize(frame.camera.xyz - surface.world_position);
        var n = normalize(cross(dp_dx, dp_dy));
        if dot(n, view) < 0.0 {
            n = -n;
        }
        let t = normalize(along_u - n * dot(n, along_u));
        let b = normalize(along_v - n * dot(n, along_v));
        let s = decode(data, vec2<f32>(length(along_u), length(along_v)));

        // March the view ray down into the stone until it passes under the carved face, then close in between. A ray
        // that leaves the stone's rectangle first, or finds no stone (a chip), shows what is behind.
        let ray = vec3<f32>(dot(view, t), dot(view, b), max(dot(view, n), 0.05));
        let drift = -ray.xy / ray.z; // metres across the face per metre down
        let start = data.xy * s.size;
        let deepest = 2.0 * s.bevel + s.pillow + 2.0 * s.facet + 0.03;
        let steps = max(u32(material.parameters[0].y), 4u);
        var depth = 0.0;
        var gap = carved(s, start);
        var hit = -1.0;
        for (var i = 1u; i <= steps; i++) {
            let next = deepest * f32(i) / f32(steps);
            let at = start + drift * next;
            if any(at < vec2<f32>(0.0)) || any(at > s.size) {
                break;
            }
            let next_gap = carved(s, at) - next;
            if next_gap <= 0.0 {
                hit = select(next, mix(depth, next, gap / max(gap - next_gap, 1e-6)), gap < NONE * 0.5);
                break;
            }
            depth = next;
            gap = next_gap;
        }
        if hit < 0.0 {
            discard;
        }
        let p = start + drift * hit;
        let shift = (p - start) / s.size;
        let uv = surface.uv + tex_u * shift.x + tex_v * shift.y;

        // The carved face's normal in the stone's frame, under the texture's own normal map.
        let e = 0.004;
        let slope = vec2<f32>(carved(s, p + vec2<f32>(e, 0.0)) - carved(s, p - vec2<f32>(e, 0.0)),
            carved(s, p + vec2<f32>(0.0, e)) - carved(s, p - vec2<f32>(0.0, e))) / (2.0 * e);
        var relief = normalize(vec3<f32>(slope, 1.0));
#ifdef NORMAL_MAP
        let sample = textureSampleGrad(normal_map, normal_sampler, transform_uv(material.normal_uv_u, material.normal_uv_v, uv),
            normal_gradient(dt_dx), normal_gradient(dt_dy)).rgb;
        let mapped = vec3<f32>((sample.xy * 2.0 - 1.0) * material.normal_scale, sample.z * 2.0 - 1.0);
        relief = normalize(vec3<f32>(relief.xy + mapped.xy, relief.z * mapped.z));
#endif
        lit.normal = normalize(t * relief.x + b * relief.y + n * relief.z);

        let color = textureSampleGrad(albedo, albedo_sampler, transform_uv(material.base_uv_u, material.base_uv_v, uv),
            base_gradient(dt_dx), base_gradient(dt_dy));
        let hollow = clamp(carved(s, p) / max(s.pillow + s.bevel + 0.02, 1e-3), 0.0, 1.5) / 1.5;
        let cavity = material.parameters[0].z;
        lit.base = vec4<f32>(color.rgb * (1.0 - 0.5 * cavity * hollow), 1.0);
        lit.occlusion *= 1.0 - cavity * hollow;
    }
    return standard_shade(lit);
}
