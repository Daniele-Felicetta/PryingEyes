"""
Hair, brows, moustache and braid of the survivors.

The hair is a sheet laid over the head, not a shell cut out of a sphere. Its columns are the flow lines of the hair: they start at the
crown (or at the edge of the cap) and follow the real surface of the head (the same field that sculpted the face, so the hair always
clears the brow, the temples and the ears) down to the lower edge of the style, or, for hair that hangs free, they leave the head at
a release angle and fall straight down hugging the head's outline. Each column has its own length (so the tips of a fringe or of a
bob are uneven, in locks) and its own thickness; a rows-by-columns grid of those points is the surface, so the edge of the hair is
a clean curve, never a staircase.

Two meshes come out of one description: a high-poly sheet with the relief of the locks (the source of the bake) and a coarse sheet of the
same shape without relief (the low-poly the game uses, see export.py: the part carries it in its 'lowproxy' property). Per-vertex
attributes tell the hair material where the strands run (`hflow`), which lock a point belongs to and how far it is from the root (`hf`).
"""
from kit import *
import numpy as np
from mathutils import Euler
import skeleton as sk
import face as fc
from face import f32, sstep, march

HAIR_O = (0.0, 0.010, 0.024)         # the centre of the polar grid of the scalp (head-local coordinates)


def hrand(i):
    """A repeatable pseudo-random number in [0, 1) per integer (array)."""
    return np.mod(np.sin(np.asarray(i, dtype=np.float64) * 12.9898 + 78.233) * 43758.5453, 1.0)


def smooth_periodic(vals, sigma):
    n = len(vals)
    k = int(3 * sigma) + 1
    w = np.exp(-0.5 * (np.arange(-k, k + 1) / sigma) ** 2)
    w /= w.sum()
    ext = np.concatenate([vals[-k:], vals, vals[:k]])
    return np.convolve(ext, w, mode='same')[k:-k]


def periodic_fn(left, right=None, sigma_deg=4.0):
    """Function of the azimuth (radians, -pi..pi, positive towards the character's left) from (degrees, value) lists for each
    side (the right one defaults to the mirror image of the left one), smoothed so that the curve has no corners."""
    right = left if right is None else right
    la, lv = zip(*left)
    ra, rv = zip(*right)
    deg = np.linspace(-180.0, 180.0, 720, endpoint=False)
    tab = np.where(deg >= 0, np.interp(np.abs(deg), la, lv), np.interp(np.abs(deg), ra, rv))
    tab = smooth_periodic(tab, sigma_deg / 0.5)

    def f(theta):
        d = (np.degrees(theta) + 180.0) % 360.0 - 180.0
        return np.interp(d, np.concatenate([deg, [180.0]]), np.concatenate([tab, [tab[0]]]))
    return f


def gradient(sdf, S, e=0.0006):
    x, y, z = S[:, 0], S[:, 1], S[:, 2]
    g = np.stack([sdf(x + e, y, z) - sdf(x - e, y, z), sdf(x, y + e, z) - sdf(x, y - e, z), sdf(x, y, z + e) - sdf(x, y, z - e)], axis=1)
    return g / np.maximum(np.linalg.norm(g, axis=1, keepdims=True), 1e-9)


def vnoise_periodic(th, n, seed):
    """Smooth value noise (-1..1) around the head with n lattice points: no steps, and the same value at both ends of the circle."""
    u = (np.degrees(th) + 180.0) / 360.0 * n
    i = np.floor(u).astype(int)
    f = u - i
    w = f * f * (3.0 - 2.0 * f)
    a = hrand(((i % n) + seed * 17.0) * 3.7) * 2.0 - 1.0
    b = hrand((((i + 1) % n) + seed * 17.0) * 3.7) * 2.0 - 1.0
    return a + (b - a) * w


def lock_length(th, st):
    """Variation of the lower edge around the head in metres: slow waves (the hair never ends on a ruler) plus a fine sawtooth, a
    pointed tip for every lock."""
    n1 = max(int(round(360.0 / (st['clump'] * 2.2))), 4)
    n2 = max(int(round(360.0 / (st['clump'] * 0.8))), 8)
    wave = 0.8 * vnoise_periodic(th, n1, st['seed']) + 0.45 * vnoise_periodic(th, n2, st['seed'] + 3.0)
    u = np.degrees(th) / st['clump'] + st['seed'] * 7.0
    idc = np.floor(u)
    tip = 1.0 - np.abs(2.0 * (u - idc) - 1.0)                 # 0 between two locks, 1 at the middle of one
    saw = -tip * (0.4 + 0.6 * hrand(idc * 2.3))
    return wave * st['tips'] + saw * st['saw']


def crossing(s, z, k, zt):
    """Arc length at which the descending height z(s) reaches zt, between the samples k - 1 and k."""
    if k <= 0:
        return s[0]
    f = (z[k - 1] - zt) / max(z[k - 1] - z[k], 1e-9)
    return s[k - 1] + min(max(f, 0.0), 1.0) * (s[k] - s[k - 1])


# ----------------------------------------------------------------------------- styles
#   edge: [(azimuth degrees, height of the lower edge of the hair, head-local)] for the character's left side (the right side
#         mirrors it unless 'edge_r' is given); azimuth 0 is the front, 90 the ear, 180 the nape
#   release: None = the hair lies on the head down to its edge; degrees = polar angle at which the hair leaves the head and falls
#         free (blended out towards the front, where the hair is a fringe lying on the forehead)
#   top, side, front: thickness in metres at the crown, at the sides, extra lift at the front (the quiff)
#   bulge, curl: how much a free-hanging curtain swells out and curls in at the hem
#   clump: width of a lock in degrees of azimuth; relief: its height in metres; tips: slow variation of the length around the head;
#   saw: length of the pointed tip of each lock; wobble: how much a lock waves along its length; cover_ears: the hair passes over the ears
#   part: (azimuth degrees, half width degrees) of a parting; cols, rows: resolution of the sheet; start: heights where the sheet begins
HAIR_DEFAULTS = dict(release=None, top=0.012, side=0.006, front=0.0, front_w=35.0, bulge=0.0, curl=0.0, clump=5.5, relief=0.0016,
                     tips=0.003, saw=0.0035, wobble=0.10, cover_ears=False, part=None, edge_min=0.0018, start=None, hang_front=(32.0, 54.0), seed=1.0)


def style_of(spec):
    s = dict(HAIR_DEFAULTS)
    s.update(spec)
    return s


# ----------------------------------------------------------------------------- the sheet
def sheet_arrays(P, style, hi=True):
    """Computes the outer and inner point grids of the hair sheet. Returns a dict of numpy arrays shaped (columns, rows + 1, 3)."""
    st = style_of(style)
    p = fc.params(P.get('face', {}))
    sdf, _ = fc.face_field(p)
    if st['cover_ears']:
        head_sdf = sdf
        ax = 0.0700 * p['width']

        def sdf(x, y, z):               # the head and the volume of the ears: hair that covers the ears has to clear them
            return fc.smin(head_sdf(x, y, z), fc.sd_ell(np.abs(x), y, z, (ax + 0.0030, 0.0245, -0.0178), (0.0125, 0.0310, 0.0390)), 0.008)
    J = 360 if hi else 40
    th = np.linspace(-np.pi, np.pi, J, endpoint=False) + (0.0 if hi else np.pi / J)
    K = 175
    phis = np.radians(np.linspace(2.0, 176.0, K))             # one sample per degree, down to under the occiput (the nape of short hair)
    TH, PH = np.meshgrid(th, phis, indexing='ij')
    D = np.stack([np.sin(PH) * np.sin(TH), -np.sin(PH) * np.cos(TH), np.cos(PH)], axis=-1).reshape(-1, 3).astype(np.float32)
    tt = march(sdf, D, o=HAIR_O, t0=0.030, t1=0.170, steps=60)
    S = (f32(HAIR_O) + D * tt[:, None])
    N = gradient(sdf, S).reshape(J, K, 3)
    S = S.reshape(J, K, 3)
    zk = S[:, :, 2]
    edge = periodic_fn(st['edge'], st.get('edge_r'))
    z_end = np.maximum(edge(th) + lock_length(th, st), -0.30)
    phis_deg = np.degrees(phis)
    rel = np.full(J, 999.0)
    if st['release'] is not None:
        a, b = st['hang_front']
        rel = st['release'] + 70.0 * (1.0 - sstep(a, b, np.abs(np.degrees(th))))
    start_z = None if st['start'] is None else periodic_fn(st['start'])(th)
    out = dict(th=th, J=J)
    drop_rows = []
    L_sk = np.zeros(J)
    L_dr = np.zeros(J)
    s_start = np.zeros(J)
    s_all = np.concatenate([np.zeros((J, 1)), np.cumsum(np.linalg.norm(np.diff(S, axis=1), axis=2), axis=1)], axis=1)
    for j in range(J):
        z, s = zk[j], s_all[j]
        if start_z is None:
            s0 = s[0]
        else:
            ab = np.nonzero(z <= start_z[j])[0]
            s0 = crossing(s, z, int(ab[0]), start_z[j]) if len(ab) else s[-2]
        s_rel = np.interp(rel[j], phis_deg, s)
        z_rel = np.interp(s_rel, s, z)
        if rel[j] < phis_deg[-1] and z_rel > z_end[j]:
            s1, drop = s_rel, z_rel - z_end[j]            # the hair leaves the head here and falls free
        else:
            below = np.nonzero(z <= z_end[j])[0]
            s1 = crossing(s, z, int(below[0]), z_end[j]) if len(below) else s[-1]
            drop = 0.0
        s1 = max(s1, s0 + 0.004)
        s_start[j], L_sk[j], L_dr[j] = s0, s1 - s0, max(drop, 0.0)
    L_tot = L_sk + L_dr
    # Rows are about `step` apart along the hair, not a fraction of each column's own length (neighbouring columns of very different
    # lengths would shear and fold), and there is one row exactly where the hair leaves the head; a short column stops at its tip.
    step = 0.0016 if hi else float(L_tot.max()) / 16.0
    n_s = np.maximum(np.ceil(L_sk / step).astype(int), 1)
    n_d = np.where(L_dr > 1e-5, np.maximum(np.ceil(L_dr / step).astype(int), 1), 0)
    itip = n_s + n_d
    M = int(itip.max())
    out.update(M=M)
    O = np.zeros((J, M + 1, 3), dtype=np.float32)
    I = np.zeros_like(O)
    Fl = np.zeros_like(O)
    AX = np.zeros((J, M + 1), dtype=np.float32)       # 0 at the root, 1 at the tip
    ID = np.zeros((J, M + 1), dtype=np.float32)
    hang = np.zeros((J, M + 1), dtype=bool)
    nw = max(int(round(360.0 / (st['clump'] * 3.0))), 4)
    cw = np.radians(st['clump'])

    def relief_term(thj, ell, ltot):
        """Height (metres) of the ridges of the locks at azimuth thj and distance ell from the root: ridges of unequal width and
        height, waving a little along their length. The same function serves the part on the skull and the part that hangs."""
        if not (hi and st['relief'] > 0.0):
            return 0.0
        thw = thj + cw * 0.45 * vnoise_periodic(thj, nw, st['seed'] + 7.0)
        ph = thw / cw + st['wobble'] * np.sin(ell / 0.030 * 2 * np.pi + 6.0 * hrand(np.floor(thw / cw)))
        idc = np.floor(ph + st['seed'] * 7.0)
        fr = ph - np.floor(ph)
        ridge = np.power(np.sin(np.pi * fr), 0.7)
        amp = st['relief'] * (0.55 + 0.9 * hrand(idc * 5.1)) * sstep(0.012, 0.045, ell) * (1.0 - 0.45 * np.minimum(ell / ltot, 1.0))
        ph2 = thw / (cw * 0.33) + 0.35 * np.sin(ell / 0.018 * 2 * np.pi + 3.0 * hrand(idc))
        r2 = np.power(np.sin(np.pi * (ph2 - np.floor(ph2))), 0.8)
        return amp * (ridge - 0.55) + 0.45 * amp * (r2 - 0.55)

    # thickness model
    T_top, T_side, T_front = st['top'], st['side'], st['front']
    for j in range(J):
        ell = np.concatenate([np.linspace(0.0, L_sk[j], n_s[j] + 1), L_sk[j] + np.linspace(0.0, L_dr[j], n_d[j] + 1)[1:]])
        ell = np.concatenate([ell, np.full(M + 1 - len(ell), L_tot[j])])
        u = ell / L_tot[j]
        on_sk = ell <= L_sk[j] + 1e-9
        s_q = s_start[j] + np.minimum(ell, L_sk[j])
        Sj = np.stack([np.interp(s_q, s_all[j], S[j, :, c]) for c in range(3)], axis=1)
        Nj = np.stack([np.interp(s_q, s_all[j], N[j, :, c]) for c in range(3)], axis=1)
        Nj /= np.maximum(np.linalg.norm(Nj, axis=1, keepdims=True), 1e-9)
        lat = np.degrees(np.interp(s_q, s_all[j], phis))
        # the hair is offset along the surface normal on the top of the head and horizontally lower down (that is how the part
        # that hangs free is offset: with both the same, the two parts meet without a step)
        dh = np.array([np.sin(th[j]), -np.cos(th[j]), 0.0])
        wh = sstep(60.0, 95.0, lat)[:, None]
        Nj = Nj * (1.0 - wh) + dh * wh
        Nj /= np.maximum(np.linalg.norm(Nj, axis=1, keepdims=True), 1e-9)
        tth = np.degrees(th[j])
        w_top = 1.0 - sstep(30.0, 80.0, lat)
        # the lift at the front depends on the azimuth, so it has to die out towards the pole of the crown, where every azimuth is the
        # same point (otherwise the crown gets a crease and a spike)
        t = T_side + (T_top - T_side) * w_top + T_front * np.exp(-(tth / st['front_w']) ** 2) * sstep(3.0, 26.0, lat) * (1.0 - sstep(38.0, 78.0, lat))
        if st['part'] is not None:
            pa, pw = st['part']
            t = t - 0.0012 * np.exp(-((tth - pa) / pw) ** 2) * sstep(0.0, 0.25, u) * (1.0 - sstep(0.75, 1.0, u))
        t_base = t
        t = t + relief_term(th[j], ell, L_tot[j])
        # the edge: the lock thins towards its tip
        taper = 1.0 - sstep(0.80, 1.0, u)
        t = st['edge_min'] + (t - st['edge_min']) * np.power(np.maximum(taper, 0.0), 0.8)
        outer = Sj + Nj * t[:, None]
        inner = Sj + Nj * 0.0004
        if L_dr[j] > 1e-5 and (~on_sk).any():
            idx = np.nonzero(~on_sk)[0]
            q = (ell[idx] - L_sk[j]) / max(L_dr[j], 1e-6)
            last = outer[max(idx[0] - 1, 0)]                  # the last row on the skull: the hanging part continues from it
            zz = last[2] - (ell[idx] - ell[max(idx[0] - 1, 0)])
            drop_rows.append((j, idx, zz, q, t_base[idx[0] - 1] if idx[0] > 0 else t_base[0], last))
            hang[j, idx] = True
        O[j], I[j], AX[j] = outer, inner, u
        ID[j] = hrand(np.floor(th[j] / np.radians(st['clump']) + st['seed'] * 7.0) * 5.1)
    # the part that hangs free: horizontal rays at each height hug the outline of the head, the hair swells and curls in
    if drop_rows:
        origins, dirs, where = [], [], []
        for j, idx, zz, q, trel, last in drop_rows:
            ang = th[j]
            for a, zv in zip(idx, zz):
                origins.append((0.0, HAIR_O[1], zv))
                dirs.append((np.sin(ang), -np.cos(ang), 0.0))
                where.append((j, a))
        origins = f32(origins)
        dirs = f32(dirs)
        tt = march(sdf, dirs, o=origins, t0=0.020, t1=0.170, steps=60)
        k = 0
        for j, idx, zz, q, trel, last in drop_rows:
            ang = th[j]
            d2 = np.array([np.sin(ang), -np.cos(ang), 0.0])
            r = tt[k:k + len(idx)]
            k += len(idx)
            r_rel = r[0] if len(r) else 0.0
            ro = np.maximum(r, r_rel * (1.0 - 0.10 * q))
            tq = trel + st['bulge'] * np.sin(np.pi * np.clip(q * 0.92, 0, 1)) ** 2 - st['curl'] * q * q
            tq = np.maximum(tq, st['edge_min'])
            tq = tq + relief_term(th[j], L_sk[j] + q * L_dr[j], L_tot[j])
            tq = st['edge_min'] + (tq - st['edge_min']) * np.power(np.maximum(1.0 - sstep(0.88, 1.0, q), 0.0), 0.6)
            ax0, ay0 = 0.0, HAIR_O[1]
            rad_last = float(np.hypot(last[0] - ax0, last[1] - ay0))      # no step where the hair leaves the head
            fix = (rad_last - (ro[0] + tq[0])) * (1.0 - sstep(0.0, 0.22, q))
            for n_, a in enumerate(idx):
                rr = ro[n_] + tq[n_] + fix[n_]
                O[j, a] = (ax0 + d2[0] * rr, ay0 + d2[1] * rr, zz[n_])
                ri = max(r[n_] + 0.0004, rr - 0.0085)
                I[j, a] = (ax0 + d2[0] * ri, ay0 + d2[1] * ri, zz[n_])
    # flow of the strands along the columns
    d = np.zeros_like(O)
    d[:, 1:-1] = O[:, 2:] - O[:, :-2]
    d[:, 0] = O[:, 1] - O[:, 0]
    d[:, -1] = O[:, -1] - O[:, -2]
    Fl = d / np.maximum(np.linalg.norm(d, axis=2, keepdims=True), 1e-9)
    out.update(outer=O, inner=I, flow=Fl, along=AX, ident=ID, hang=hang, itip=itip)
    out['crown'] = f32(HAIR_O) + f32([0, 0, 1]) * float(tt_crown(sdf)) if st['start'] is None else None
    return out


def tt_crown(sdf):
    t = march(sdf, f32([[0.0, 0.0, 1.0]]), o=HAIR_O, t0=0.030, t1=0.170, steps=60)
    return t[0]


def sheet_object(P, style, hi=True, name='hair', mat=None):
    """The hair sheet as a Blender object in world coordinates (head-local + the skeleton's head centre)."""
    st = style_of(style)
    A = sheet_arrays(P, style, hi)
    J, M = A['J'], A['M']
    O, I, Fl, AX, ID = A['outer'], A['inner'], A['flow'], A['along'], A['ident']
    bm = bmesh.new()
    off = f32([sk.HEAD_C.x, sk.HEAD_C.y, sk.HEAD_C.z])
    itip = A['itip']
    attrs = {}
    ov, iv = [], []
    for j in range(J):
        ro, ri = [], []
        for i in range(M + 1):
            if i > itip[j]:                         # past the tip of this column: the same vertex again (the column has ended)
                ro.append(ro[itip[j]])
                ri.append(ri[itip[j]])
                continue
            a = bm.verts.new(tuple(O[j, i] + off))
            b = bm.verts.new(tuple(I[j, i] + off))
            attrs[a.index] = attrs[b.index] = (ID[j, i], AX[j, i], Fl[j, i])
            ro.append(a)
            ri.append(b)
        ov.append(ro)
        iv.append(ri)
    faces = []

    def quad(vs):
        u = []
        for v in vs:
            if not u or v is not u[-1]:
                u.append(v)
        if len(u) > 1 and u[0] is u[-1]:
            u.pop()
        if len(u) >= 3 and len(set(id(v) for v in u)) == len(u):
            try:
                faces.append(bm.faces.new(u))
            except ValueError:                       # the same triangle twice at a tip
                pass

    hang = A['hang']
    for j in range(J):
        j2 = (j + 1) % J
        for i in range(M):
            quad((ov[j][i], ov[j2][i], ov[j2][i + 1], ov[j][i + 1]))
        quad((ov[j][M], ov[j2][M], iv[j2][M], iv[j][M]))             # the hem: outer edge to inner edge
        # inner sheet, only where the hair hangs free (elsewhere it would lie on the skin)
        for i in range(M):
            if hang[j, i] and hang[j, i + 1] and hang[j2, i] and hang[j2, i + 1]:
                quad((iv[j][i], iv[j][i + 1], iv[j2][i + 1], iv[j2][i]))
    if st['start'] is None:
        pv = bm.verts.new(tuple(O[:, 0].mean(axis=0) + off))          # the pole of the crown sits in the middle of the first ring
        attrs[pv.index] = (0.5, 0.0, f32([0, 0, 1]))
        for j in range(J):
            j2 = (j + 1) % J
            quad((pv, ov[j2][0], ov[j][0]))
    # orientation: the outer sheet must look outwards (the outermost face points away from the head)
    bm.normal_update()
    f0 = max(faces, key=lambda f: f.calc_center_median().x)
    if f0.normal.x < 0:
        bmesh.ops.reverse_faces(bm, faces=list(bm.faces))
    bm.verts.ensure_lookup_table()
    n = len(bm.verts)
    fid = np.zeros((n, 4), dtype=np.float32)
    fflow = np.zeros((n, 4), dtype=np.float32)
    for k, (idv, ax, fl) in attrs.items():
        fid[k] = (idv, ax, 0.0, 1.0)
        fflow[k] = (*(0.5 + 0.5 * np.asarray(fl)), 1.0)
    ob = bm_obj(bm, name)
    ob.data.shade_smooth()
    for nm, arr in (('hf', fid), ('hflow', fflow)):
        at = ob.data.color_attributes.new(nm, 'FLOAT_COLOR', 'POINT')
        at.data.foreach_set('color', arr.ravel())
    if mat is not None:
        assign(ob, mat)
    return ob


def hair(P, style, mat):
    """(high-poly sheet, low-poly proxy). The proxy is hidden from the render and carried by the high-poly part ('lowproxy')."""
    hi = sheet_object(P, style, True, 'hair', mat)
    lo = sheet_object(P, style, False, 'hair_low', mat)
    lo['group'] = 'lowproxy'
    hi['lowproxy'] = lo.name
    lo.hide_render = True
    return hi, lo


def hairline_tint(style):
    """Weight (0..1) of the shade of the hair roots on the skin just below the edge of the hair (for fc.paint)."""
    st = style_of(style)
    edge = periodic_fn(st['edge'], st.get('edge_r'))
    c = np.array(HAIR_O, dtype=np.float32)

    def f(x, y, z):
        th = np.arctan2(x - c[0], -(y - c[1]))
        ze = edge(th)
        free = np.ones_like(x)
        if st['release'] is not None:
            a, b = st['hang_front']
            free = 1.0 - sstep(a, b, np.abs(np.degrees(th)))
        gap = ze - z
        w = (1.0 - sstep(0.0, 0.009, gap)) * (gap > -0.002) * (z > ze - 0.020)
        return w * free * (y < 0.030)
    return f


# ----------------------------------------------------------------------------- patches on the skin: brows, moustache
def patch(head, weight, thick, flow, mat, name, wmin=0.12, sub=False):
    """A thin hair piece that lies on the skin: the faces of 'head' whose vertices all have weight > wmin (a function of the vertex
    position, head-local), lifted along the normals by thick(weight). flow(x, y, z) gives the direction of the strands."""
    bm = bm_from(head)
    bm.normal_update()
    off = f32([sk.HEAD_C.x, sk.HEAD_C.y, sk.HEAD_C.z])
    pos = np.array([[v.co.x, v.co.y, v.co.z] for v in bm.verts], dtype=np.float32) - off
    w = weight(pos[:, 0], pos[:, 1], pos[:, 2])
    keep = [f for f in bm.faces if all(w[v.index] > wmin for v in f.verts)]
    new = bmesh.new()
    vmap = {}
    for f in keep:
        vs = []
        for v in f.verts:
            if v.index not in vmap:
                t = float(thick(w[v.index]))
                vmap[v.index] = new.verts.new(v.co + v.normal * t)
            vs.append(vmap[v.index])
        new.faces.new(vs)
    bm.free()
    # the outline of the patch follows the faces of the head, a staircase of its triangles: relax the boundary into a smooth curve
    for _ in range(4):
        moved = {}
        for v in new.verts:
            be = [e for e in v.link_edges if e.is_boundary]
            if len(be) == 2:
                moved[v] = v.co.lerp((be[0].other_vert(v).co + be[1].other_vert(v).co) * 0.5, 0.6)
        for v, c in moved.items():
            v.co = c
    new.verts.ensure_lookup_table()
    ob = bm_obj(new, name)
    ob.data.shade_smooth()
    me = ob.data
    n = len(me.vertices)
    co = fc.vertex_array(ob) - off
    d = flow(co[:, 0], co[:, 1], co[:, 2])
    fid = np.zeros((n, 4), dtype=np.float32)
    fid[:, 0] = 0.5                                # no lock-to-lock tone steps on a patch: the strands vary inside the material
    fid[:, 1] = 0.5
    fid[:, 3] = 1.0
    fl = np.zeros((n, 4), dtype=np.float32)
    fl[:, :3] = 0.5 + 0.5 * d
    fl[:, 3] = 1.0
    for nm, arr in (('hf', fid), ('hflow', fl)):
        at = me.color_attributes.new(nm, 'FLOAT_COLOR', 'POINT')
        at.data.foreach_set('color', arr.ravel())
    assign(ob, mat)
    return ob


def brow_weight(P, scale=1.0):
    p = fc.params(P.get('face', {}))
    pts, rad = fc.brow_curve(p)
    xz = [(a[0], a[2]) for a in pts]

    def w(x, y, z):
        X = np.abs(x)
        d = fc.dist2d(X, z, xz)
        wid = np.interp(np.clip((X - pts[0][0]) / (pts[-1][0] - pts[0][0] + 1e-9), 0, 1), np.linspace(0, 1, len(rad)), rad) * scale
        edge = 1.0 - sstep(wid * 0.55, wid * 1.0, d)
        return edge * (y < -0.045) * (X > pts[0][0] - 0.002)
    return w


def brow_flow(P):
    """Direction of the strands of the brow at a point: the tangent of the brow chain, blended smoothly between its nodes (a tangent
    per segment would turn the streaks of the material at every node)."""
    p = fc.params(P.get('face', {}))
    pts, _ = fc.brow_curve(p)
    xz = np.array([(a[0], a[2]) for a in pts])
    seg = xz[1:] - xz[:-1]
    seg /= np.maximum(np.linalg.norm(seg, axis=1, keepdims=True), 1e-9)
    node = np.vstack([seg[:1], seg[:-1] + seg[1:], seg[-1:]])
    node /= np.maximum(np.linalg.norm(node, axis=1, keepdims=True), 1e-9)

    def f(x, y, z):
        X = np.abs(x)
        kf = np.interp(X, xz[:, 0], np.arange(len(xz)))
        k = np.clip(np.floor(kf).astype(int), 0, len(xz) - 2)
        w = (kf - k)[:, None]
        t = node[k] * (1.0 - w) + node[k + 1] * w
        t = t / np.maximum(np.linalg.norm(t, axis=1, keepdims=True), 1e-9)
        sg = np.sign(x + 1e-9)
        return np.stack([t[:, 0] * sg, np.zeros_like(X), t[:, 1]], axis=1)
    return f


def brows(head, P, mat, thick=0.0013, scale=1.0):
    return patch(head, brow_weight(P, scale), lambda w: thick * (0.35 + 0.65 * np.sqrt(w)), brow_flow(P), mat, 'brows')


def moustache(head, P, mat, size=1.0, droop=1.0, thick=0.0075):
    """A walrus moustache: a patch over the upper lip that thickens at the middle and droops past the corners of the mouth."""
    p = fc.params(P.get('face', {}))
    mw = p['mouth_w']
    line = [(0.0, -0.0610), (0.012 * mw, -0.0650), (0.026 * mw, -0.0745), (0.034 * mw * size, -0.0840 * droop + 0.004 * (1 - droop)),
            (0.036 * mw * size, -0.0950 * droop + 0.010 * (1 - droop))]
    wid = [0.0100 * size, 0.0100 * size, 0.0085 * size, 0.0065 * size, 0.0040 * size]

    def w(x, y, z):
        X = np.abs(x)
        d = fc.dist2d(X, z, line)
        k = np.clip(np.interp(X, [a[0] for a in line], np.arange(len(line))), 0, len(line) - 1)
        wd = np.interp(k, np.arange(len(line)), wid)
        return (1.0 - sstep(wd * 0.45, wd * 1.0, d)) * (y < -0.060) * (z < -0.050)

    def flow(x, y, z):
        sg = np.sign(x + 1e-9)
        v = np.stack([sg * 0.55, np.zeros_like(x), -0.85 * np.ones_like(x)], axis=1)
        return v / np.linalg.norm(v, axis=1, keepdims=True)

    return patch(head, w, lambda wv: thick * np.power(wv, 0.7) + 0.0006, flow, mat, 'moustache')


# ----------------------------------------------------------------------------- the braid
BRAID_PATH = [(0.074, 1.606, 0.019), (0.097, 1.552, 0.021), (0.110, 1.472, 0.022), (0.128, 1.392, 0.021),
              (0.142, 1.312, 0.019), (0.150, 1.222, 0.016), (0.150, 1.150, 0.009)]


def braid_points(links):
    out = []
    for i in range(links):
        f = i / (links - 1.0) * (len(BRAID_PATH) - 1)
        k = min(int(f), len(BRAID_PATH) - 2)
        u = f - k
        a, b = BRAID_PATH[k], BRAID_PATH[k + 1]
        out.append(tuple(a[c] + (b[c] - a[c]) * u for c in range(3)))
    return out


def braid(mat, strands=3, turns=9.0, name='braid', links=160):
    """A three-strand plait: each strand is a chain of lumps weaving left, middle and right along the path (flattened so that the
    weave reads), merged into one skin. Weighted like the old plait: the head at the nape, the upper chest lower down."""
    pts = braid_points(links)
    P3 = np.array([(0.0, y, z) for (y, z, r) in pts], dtype=np.float32)
    R = np.array([r for (y, z, r) in pts], dtype=np.float32)

    def fill(bm):
        for i in range(links):
            tdir = P3[min(i + 1, links - 1)] - P3[max(i - 1, 0)]
            tdir = tdir / (np.linalg.norm(tdir) + 1e-9)
            for sidx in range(strands):
                ph = 2 * np.pi * (turns * i / links + sidx / strands)
                lx = np.sin(ph) * R[i] * 0.78
                ly = -np.cos(ph) * R[i] * 0.30
                c = V(lx, P3[i][1] + ly + 0.0, P3[i][2])
                bm_ellipsoid(bm, c, (R[i] * 0.52, R[i] * 0.44, R[i] * 0.95), None, 14)
    ob = organic(name, fill, 0.0014, sm_iter=2, sm_fac=0.35)
    assign(ob, mat)
    # strands run along the plait
    me = ob.data
    n = len(me.vertices)
    co = np.empty(n * 3, dtype=np.float32)
    me.vertices.foreach_get('co', co)
    co = co.reshape(-1, 3)
    path = np.array([(0.0, y, z) for (y, z, r) in pts], dtype=np.float32)
    idx = np.argmin(((co[:, None, 1:3] - path[None, :, 1:3]) ** 2).sum(axis=2), axis=1)
    tang = path[np.minimum(idx + 1, len(path) - 1)] - path[np.maximum(idx - 1, 0)]
    tang /= np.maximum(np.linalg.norm(tang, axis=1, keepdims=True), 1e-9)
    fl = np.concatenate([0.5 + 0.5 * tang, np.ones((n, 1), dtype=np.float32)], axis=1)
    fid = np.zeros((n, 4), dtype=np.float32)
    fid[:, 0] = hrand(np.floor(idx / 3.0) * 1.7 + np.floor((co[:, 0] + 1) * 60))
    fid[:, 1] = idx / (links - 1.0)
    fid[:, 3] = 1.0
    for nm, arr in (('hf', fid), ('hflow', fl)):
        at = me.color_attributes.new(nm, 'FLOAT_COLOR', 'POINT')
        at.data.foreach_set('color', arr.ravel())
    return ob


def braid_proxy(mat, name='braid_low', links=34, sides=8):
    """The low-poly plait: a smooth tube along the path (the weave comes from the bake of the high-poly plait)."""
    pts = braid_points(links)
    bm = bmesh.new()
    rings = []
    for i, (y, z, r) in enumerate(pts):
        f = i / (links - 1.0)
        rad = r * (1.12 - 0.55 * f ** 1.5)
        a = V(0.0, y, z)
        b = V(0.0, *pts[min(i + 1, links - 1)][:2])
        c = V(0.0, *pts[max(i - 1, 0)][:2])
        t = (b - c).normalized()
        up = V(1.0, 0.0, 0.0)
        side = up
        nrm = t.cross(side).normalized()
        ring = []
        for k in range(sides):
            ang = 2 * np.pi * k / sides
            ring.append(bm.verts.new(a + side * (np.cos(ang) * rad * 1.05) + nrm * (np.sin(ang) * rad * 0.95)))
        rings.append(ring)
    for i in range(links - 1):
        for k in range(sides):
            bm.faces.new((rings[i][k], rings[i][(k + 1) % sides], rings[i + 1][(k + 1) % sides], rings[i + 1][k]))
    bm.faces.new(rings[-1])
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    ob = bm_obj(bm, name)
    ob.data.shade_smooth()
    assign(ob, mat)
    ob.hide_render = True
    ob['group'] = 'lowproxy'
    return ob
