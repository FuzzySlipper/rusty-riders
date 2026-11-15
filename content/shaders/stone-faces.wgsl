// Stone faces carved in the shader (kit recipe `stoneFaces`; StoneShape.Faces.cs builds the stones).
//
// A stone is a plain box of its size and jut. Its face vertices' product data carries where they lie on the stone (xy: 0
// to 1 along and up) and the stone's shape (zw: whole numbers, decoded below as StoneShape.Data packs them; z has bit 19
// set). This stage finds where the view ray meets the carved face under the box's top (parallax), then shades the stone's
// texture there with the carved face's normal: its bevel, the bulge and facets of its kind, chipped corners and nicked
// edges. A plain box's sides carry where they lie too (z bit 22; StoneShape.Faces.cs, CarvedSide): a ray that enters a
// side is marched on into the stone, and lights the side, the carved face or a chip's broken wall, whichever it meets
// first. Vertices without data (zero) are not stones and draw as usual.
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
    bottom: f32, // a side's: how deep it runs under its face, metres
    two_faced: bool, // a side's: whether a second face lies the same depth further on
};

fn hash(seed: u32, salt: u32) -> f32 {
    var x = seed * 747796405u + salt * 2891336453u + 1u;
    x = ((x >> ((x >> 28u) + 4u)) ^ x) * 277803737u;
    x = (x >> 22u) ^ x;
    return f32(x & 0xffffffu) / 16777216.0;
}

const STONE: u32 = 1u << 19u;
const SIDE: u32 = 1u << 22u;
const TWO_FACED: u32 = 1u << 23u;

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
    s.bottom = f32((c >> 18u) & 63u) * 0.01;
    s.two_faced = (a & TWO_FACED) != 0u;
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
    let broken = breaks(s, p);
    if broken.y > 0.0 {
        return NONE;
    }
    return max(d + broken.x, 0.0);
}

// How a stone's chipped corners and nicked edges break it at `p`: x how far they cut down into the face; y how far past
// opening they cut, so the stone is gone where it is above zero (-1 where nothing breaks it).
fn breaks(s: Stone, p: vec2<f32>) -> vec2<f32> {
    let size = s.size;
    // Chipped corners, cut back from a line across the corner, and nicked edges: V notches into an edge.
    let most = s.chip;
    var cut = 0.0;
    var gone = -1.0;
    if most > 0.0 {
        for (var c = 0u; c < 4u; c++) {
            if hash(s.seed, 20u + c) < s.chip_chance {
                let corner = vec2<f32>(select(0.0, size.x, c == 1u || c == 2u), select(0.0, size.y, c >= 2u));
                let local = abs(p - corner);
                let legs = vec2<f32>(mix(0.3, 1.0, hash(s.seed, 24u + c)), mix(0.3, 1.0, hash(s.seed, 28u + c))) * most;
                let inside = 1.0 - (local.x / legs.x + local.y / legs.y);
                let chipped = s.bevel + inside * max(legs.x, legs.y) * 12.0; // a short broken slope, then open
                gone = max(gone, chipped - s.open);
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
                    gone = max(gone, nicked - s.open);
                    cut = max(cut, select(0.0, nicked, left > 0.0));
                }
            }
        }
    }
    return vec2<f32>(cut, gone);
}

// The texture gradient through the material's uv transform.
fn base_gradient(g: vec2<f32>) -> vec2<f32> {
    return vec2<f32>(dot(material.base_uv_u.xy, g), dot(material.base_uv_v.xy, g));
}

fn normal_gradient(g: vec2<f32>) -> vec2<f32> {
    return vec2<f32>(dot(material.normal_uv_u.xy, g), dot(material.normal_uv_v.xy, g));
}

// Whether the stone is at `p`, `h` under the face a side is measured from: under the carved face, and for a two-faced
// stone under the far face too; a one-faced stone ends where its sides do.
fn solid(s: Stone, p: vec2<f32>, h: f32) -> bool {
    if any(p < vec2<f32>(0.0)) || any(p > s.size) || h < 0.0 {
        return false;
    }
    var under = h;
    if s.two_faced {
        under = min(h, 2.0 * s.bottom - h);
    } else if h > s.bottom {
        return false;
    }
    let c = carved(s, p);
    return c < NONE * 0.5 && under >= c;
}

// The texture's own normal map, under a surface normal given in the frame its texture runs in (x, y along the texture's
// u and v, z out).
fn mapped(relief: vec3<f32>, uv: vec2<f32>, dt_dx: vec2<f32>, dt_dy: vec2<f32>) -> vec3<f32> {
#ifdef NORMAL_MAP
    let sample = textureSampleGrad(normal_map, normal_sampler, transform_uv(material.normal_uv_u, material.normal_uv_v, uv),
        normal_gradient(dt_dx), normal_gradient(dt_dy)).rgb;
    let m = vec3<f32>((sample.xy * 2.0 - 1.0) * material.normal_scale, sample.z * 2.0 - 1.0);
    return normalize(vec3<f32>(relief.xy + m.xy, relief.z * m.z));
#else
    return relief;
#endif
}

// Lights the stone's texture at `uv`, its normal `relief` in the frame (x, y, z), darkened by how far into a hollow it lies.
fn lit_at(surface: Surface, uv: vec2<f32>, dt_dx: vec2<f32>, dt_dy: vec2<f32>, x: vec3<f32>, y: vec3<f32>, z: vec3<f32>,
    relief: vec3<f32>, hollow: f32) -> vec4<f32> {
    var lit = surface;
    let r = mapped(relief, uv, dt_dx, dt_dy);
    lit.normal = normalize(x * r.x + y * r.y + z * r.z);
    let color = textureSampleGrad(albedo, albedo_sampler, transform_uv(material.base_uv_u, material.base_uv_v, uv),
        base_gradient(dt_dx), base_gradient(dt_dy));
    let cavity = material.parameters[0].z;
    lit.base = vec4<f32>(color.rgb * (1.0 - 0.5 * cavity * hollow), 1.0);
    lit.occlusion *= 1.0 - cavity * hollow;
    return standard_shade(lit);
}

// The carved face's normal at `p`, in the stone's frame (along, up, out).
fn face_normal(s: Stone, p: vec2<f32>) -> vec3<f32> {
    let e = 0.004;
    let slope = vec2<f32>(carved(s, p + vec2<f32>(e, 0.0)) - carved(s, p - vec2<f32>(e, 0.0)),
        carved(s, p + vec2<f32>(0.0, e)) - carved(s, p - vec2<f32>(0.0, e))) / (2.0 * e);
    return normalize(vec3<f32>(slope, 1.0));
}

fn face_hollow(s: Stone, depth: f32) -> f32 {
    return clamp(depth / max(s.pillow + s.bevel + 0.02, 1e-3), 0.0, 1.5) / 1.5;
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
    let code = u32(round(data.z));
    if (code & STONE) == 0u || abs(det) <= 1e-14 {
        return standard_shade(surface);
    }
    // World and texture uv per unit of the data's x and y.
    let along_u = (dp_dx * dl_dy.y - dp_dy * dl_dx.y) / det;
    let along_v = (dp_dy * dl_dx.x - dp_dx * dl_dy.x) / det;
    let tex_u = (dt_dx * dl_dy.y - dt_dy * dl_dx.y) / det;
    let tex_v = (dt_dy * dl_dx.x - dt_dx * dl_dy.x) / det;
    let view = normalize(frame.camera.xyz - surface.world_position);
    var facing = normalize(cross(dp_dx, dp_dy));
    if dot(facing, view) < 0.0 {
        facing = -facing;
    }
    if (code & SIDE) != 0u {
        return shade_side(surface, data, code, along_u, along_v, tex_u, tex_v, facing, view, dt_dx, dt_dy);
    }

    // A face: x and y are its place along and up, so the stone's frame and size follow.
    let n = facing;
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
    return lit_at(surface, uv, dt_dx, dt_dy, t, b, n, face_normal(s, p), face_hollow(s, carved(s, p)));
}

// A side of a plain box: x is its place along its edge (from the edge's first corner, anticlockwise round the stone), y
// its depth under the face over the stone's size across the edge. The ray is marched on into the stone from where it
// enters, and lights whatever it meets first: the side itself, the carved face (over the rim, or a chip's broken slope),
// or the broken wall of a chip or nick.
fn shade_side(surface: Surface, data: vec4<f32>, code: u32, along_u: vec3<f32>, along_v: vec3<f32>, tex_u: vec2<f32>,
    tex_v: vec2<f32>, side_out: vec3<f32>, view: vec3<f32>, dt_dx: vec2<f32>, dt_dy: vec2<f32>) -> vec4<f32> {
    let edge = (code >> 20u) & 3u;
    // The edge's way along and out, and its first corner, in the stone's place (x along, y up).
    var way = vec2<f32>(1.0, 0.0);
    var outward = vec2<f32>(0.0, -1.0);
    if edge == 1u {
        way = vec2<f32>(0.0, 1.0);
        outward = vec2<f32>(1.0, 0.0);
    } else if edge == 2u {
        way = vec2<f32>(-1.0, 0.0);
        outward = vec2<f32>(0.0, 1.0);
    } else if edge == 3u {
        way = vec2<f32>(0.0, -1.0);
        outward = vec2<f32>(-1.0, 0.0);
    }
    let length_along = length(along_u);
    let across = length(along_v);
    let n = -along_v / across; // out of the face the depth is measured from
    let w = along_u / length_along;
    // The stone's axes in the world: its way along the edge is `w`, its way out of the edge is the side's facing.
    let t = way.x * w + outward.x * side_out;
    let b = way.y * w + outward.y * side_out;
    let size = select(vec2<f32>(across, length_along), vec2<f32>(length_along, across), edge % 2u == 0u);
    let s = decode(data, size);
    let corner = vec2<f32>(select(0.0, size.x, edge == 1u || edge == 2u), select(0.0, size.y, edge >= 2u));
    let p0 = clamp(corner + way * data.x * length_along, vec2<f32>(0.0), size);
    let h0 = data.y * across;
    // Texture uv per metre along the stone's x and y (the side's uv folds the face's over the edge), and at its origin.
    let per_way = tex_u / length_along;
    let per_out = tex_v / across;
    let tex_x = way.x * per_way + outward.x * per_out;
    let tex_y = way.y * per_way + outward.y * per_out;
    let folded = p0 + outward * h0;
    let uv0 = surface.uv - tex_x * folded.x - tex_y * folded.y;

    if solid(s, p0, h0) {
        // The side itself: its texture as folded, its frame the fold's (the face's x or y turned down the side).
        let x = select(t, -outward.x * n, outward.x != 0.0);
        let y = select(b, -outward.y * n, outward.y != 0.0);
        return lit_at(surface, surface.uv, dt_dx, dt_dy, x, y, side_out, vec3<f32>(0.0, 0.0, 1.0), 0.0);
    }

    // March on in, the steps closer near the side, where most rays meet the stone; then halve the last step.
    let ray = vec3<f32>(-dot(view, t), -dot(view, b), dot(view, n)); // per metre along the ray: x, y, depth
    var reach = 4.0;
    if abs(ray.x) > 1e-5 {
        reach = min(reach, select(-p0.x, s.size.x - p0.x, ray.x > 0.0) / ray.x);
    }
    if abs(ray.y) > 1e-5 {
        reach = min(reach, select(-p0.y, s.size.y - p0.y, ray.y > 0.0) / ray.y);
    }
    if ray.z < -1e-5 {
        reach = min(reach, -h0 / ray.z);
    } else if ray.z > 1e-5 {
        reach = min(reach, (select(s.bottom, 2.0 * s.bottom, s.two_faced) - h0) / ray.z);
    }
    let steps = max(u32(material.parameters[0].y), 4u);
    var outside = 0.0;
    var inside = -1.0;
    for (var i = 1u; i <= steps; i++) {
        let f = f32(i) / f32(steps);
        let at = reach * f * f;
        if solid(s, p0 + ray.xy * at, h0 + ray.z * at) {
            inside = at;
            break;
        }
        outside = at;
    }
    if inside < 0.0 {
        discard;
    }
    for (var i = 0u; i < 6u; i++) {
        let mid = (outside + inside) * 0.5;
        if solid(s, p0 + ray.xy * mid, h0 + ray.z * mid) {
            inside = mid;
        } else {
            outside = mid;
        }
    }
    let p = p0 + ray.xy * inside;
    let h = h0 + ray.z * inside;
    let before = p0 + ray.xy * outside;
    let before_h = h0 + ray.z * outside;
    // Under which face it lies (a two-faced stone's far one past halfway), and how deep under it.
    let far = s.two_faced && h > s.bottom;
    let under = select(h, 2.0 * s.bottom - h, far);
    let face_out = select(n, -n, far);
    let gone = breaks(s, before).y;
    if gone <= 0.0 && all(before >= vec2<f32>(0.0)) && all(before <= s.size) {
        // Through the carved face, from above it.
        let uv = uv0 + tex_x * p.x + tex_y * p.y;
        let relief = face_normal(s, p);
        return lit_at(surface, uv, dt_dx, dt_dy, t, b, face_out, relief, face_hollow(s, carved(s, p)));
    }
    // Through a chip's or nick's broken wall: its normal is the way the break deepens, its texture folded over the
    // nearest edge's way, as a side's is.
    let e = 0.003;
    var g = vec2<f32>(breaks(s, p + vec2<f32>(e, 0.0)).y - breaks(s, p - vec2<f32>(e, 0.0)).y,
        breaks(s, p + vec2<f32>(0.0, e)).y - breaks(s, p - vec2<f32>(0.0, e)).y);
    if dot(g, g) < 1e-10 {
        g = -ray.xy;
    }
    let wall = normalize(g);
    var fold = vec2<f32>(sign(wall.x), 0.0);
    if abs(wall.y) > abs(wall.x) {
        fold = vec2<f32>(0.0, sign(wall.y));
    }
    let at = p + fold * under;
    let uv = uv0 + tex_x * at.x + tex_y * at.y;
    let z = normalize(t * wall.x + b * wall.y);
    let x = normalize(select(t, -fold.x * face_out, fold.x != 0.0) - z * dot(z, select(t, -fold.x * face_out, fold.x != 0.0)));
    let y = normalize(cross(z, x)) * select(1.0, -1.0, dot(cross(z, x), select(b, -fold.y * face_out, fold.y != 0.0)) < 0.0);
    return lit_at(surface, uv, dt_dx, dt_dy, x, y, z, vec3<f32>(0.0, 0.0, 1.0), face_hollow(s, under) * 0.6);
}
