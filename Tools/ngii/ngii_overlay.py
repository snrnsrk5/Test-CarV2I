# -*- coding: utf-8 -*-
"""수치지형도(국토지리정보원 연속수치지형도 SHP) → 유니티 비교용 오버레이 (내 컴퓨터에서만 실행)

- 국가기본도(1:5000)는 국외 반출이 제한되므로, 이 스크립트는 데이터를 어디로도 보내지 않고
  내 컴퓨터 안에서 유니티 폴더(Assets/PNU_Map3/NGII)에 비교용 메시(JSON)만 만듭니다.
- 코스 주변(경로 ±300m, ngii_ref.json)만 잘라서 씁니다. 높이는 유니티에서 현재 지형·도로에 붙입니다.

사용: python ngii_overlay.py --data D:\\NGII_data --ref ngii_ref.json --out "..\\..\\Assets\\PNU_Map3\\NGII"
필요 패키지: pyshp pyproj shapely numpy mapbox-earcut
"""
import argparse, glob, json, math, os, sys, time

import numpy as np
import shapefile                          # pyshp
from pyproj import CRS, Transformer
from shapely.geometry import Polygon, LineString, MultiPolygon, MultiLineString, shape
from shapely.prepared import prep
from shapely.ops import unary_union
import mapbox_earcut as earcut

# 수치지도 2.0 지형지물 코드(파일 이름에 들어 있음, 예: N3A_A0010000.shp). 확실치 않은 것은 '?'로 표시 — 첫 실행 요약을 보고 맞춤
CATS = [
    # (코드 앞부분, 분류, 설명)
    ('A0010000', 'road_area', '도로경계(면)'),
    ('A0020000', 'road_center', '도로중심선'),
    ('A0033320', 'sidewalk', '보도(?)'),
    ('A0030000', 'sidewalk', '인도/보도(?)'),
    ('A0040000', 'crosswalk', '횡단보도(?)'),
    ('A0050000', 'safety', '안전지대(?)'),
    ('A0070000', 'bridge', '교량(?)'),
    ('A0110000', 'tunnel', '터널(?)'),
    ('B0010000', 'building', '건물'),
]
COLORS = {'road_area': (0.10, 0.75, 1.00, 0.45), 'road_edge': (0.0, 0.9, 1.0, 0.95), 'road_center': (1.0, 0.85, 0.1, 0.9),
          'sidewalk': (1.0, 0.55, 0.15, 0.45), 'crosswalk': (1.0, 1.0, 1.0, 0.7), 'safety': (1.0, 0.3, 0.6, 0.5),
          'bridge': (0.6, 0.4, 1.0, 0.35), 'tunnel': (0.5, 0.5, 0.5, 0.35), 'building': (1.0, 0.2, 0.9, 0.9), 'other_area': (0.6, 0.6, 0.6, 0.25),
          'other_line': (0.8, 0.8, 0.8, 0.6)}
MAXV = 60000


def log(*a):
    print(*a, flush=True)


def guess_crs(xs, ys):
    """.prj 가 없을 때 좌표 범위로 추정"""
    x = float(np.median(xs)); y = float(np.median(ys))
    if 800000 < x < 1400000 and 1400000 < y < 2200000: return CRS.from_epsg(5179)          # UTM-K
    if 100000 < x < 400000 and 100000 < y < 700000: return CRS.from_epsg(5187)              # 동부원점(부산)
    if 400000 < x < 700000 and 3600000 < y < 4300000: return CRS.from_epsg(32652)            # UTM 52N
    return None


def cat_of(fname):
    base = os.path.basename(fname).upper()
    for code, cat, desc in CATS:
        if code in base: return cat, desc
    return None, ''


def ribbon(line, w):
    """선 → 얇은 띠(면)"""
    return line.buffer(w / 2, cap_style=2, join_style=2)


def tri_poly(pg):
    rings = [np.asarray(pg.exterior.coords)[:-1]] + [np.asarray(r.coords)[:-1] for r in pg.interiors]
    rings = [r for r in rings if len(r) >= 3]
    if not rings: return None, None
    V = np.vstack(rings).astype(np.float64)
    ends = np.cumsum([len(r) for r in rings]).astype(np.uint32)
    T = earcut.triangulate_float64(V, ends).reshape(-1, 3)
    return V, T


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument('--data', required=True, help='수치지형도 SHP를 풀어 둔 폴더(하위 폴더 포함)')
    ap.add_argument('--ref', required=True)
    ap.add_argument('--out', required=True)
    ap.add_argument('--margin', type=float, default=0.0, help='코스 띠 바깥으로 더 넓힐 거리(m)')
    a = ap.parse_args()
    t0 = time.time()
    ref = json.load(open(a.ref, encoding='utf-8'))
    E0, N0 = ref['E0'], ref['N0']
    cor = Polygon(ref['corridor']).buffer(a.margin) if a.margin else Polygon(ref['corridor'])
    pcor = prep(cor)
    to_local = {}
    files = sorted(set(glob.glob(os.path.join(a.data, '**', '*.shp'), recursive=True) + glob.glob(os.path.join(a.data, '**', '*.SHP'), recursive=True)))
    if not files:
        log('SHP 파일을 못 찾았어요:', a.data); sys.exit(1)
    os.makedirs(a.out, exist_ok=True)
    summary = []
    buckets = {}          # cat -> list of polygons(local)
    for f in files:
        cat, desc = cat_of(f)
        try:
            r = shapefile.Reader(f, encoding='cp949')
        except Exception as e:
            summary.append(dict(file=os.path.basename(f), error=str(e))); continue
        st = r.shapeType; n = len(r)
        fields = [fd[0] for fd in r.fields[1:]][:12]
        prj = os.path.splitext(f)[0] + '.prj'
        crs = None
        if os.path.exists(prj):
            try: crs = CRS.from_wkt(open(prj, encoding='utf-8', errors='ignore').read())
            except Exception: crs = None
        if crs is None and n:
            bb = r.bbox; crs = guess_crs([bb[0], bb[2]], [bb[1], bb[3]])
        info = dict(file=os.path.basename(f), shapeType=st, features=n, fields=fields, crs=(crs.to_string() if crs else '?'), cat=cat or '-', desc=desc)
        if crs is None or n == 0:
            summary.append(info); continue
        key = crs.to_wkt()
        if key not in to_local: to_local[key] = Transformer.from_crs(crs, CRS.from_epsg(ref['epsg_local']), always_xy=True)
        tf = to_local[key]
        kept = 0
        if cat is None:
            cat = 'other_area' if st in (5, 15, 25) else ('other_line' if st in (3, 13, 23) else None)
        if cat is None:
            summary.append(info); continue
        for sr in r.iterShapes():
            if not sr.points: continue
            pts = np.asarray(sr.points, float)
            X, Y = tf.transform(pts[:, 0], pts[:, 1]); P = np.column_stack([np.asarray(X) - E0, np.asarray(Y) - N0])
            parts = list(sr.parts) + [len(P)]
            if st in (5, 15, 25):          # 면: 바깥 고리 + 구멍(링 방향으로 구분하지 않고 shapely로 정리)
                rings = [P[parts[k]:parts[k + 1]] for k in range(len(parts) - 1)]
                rings = [rg for rg in rings if len(rg) >= 4]
                if not rings: continue
                try:
                    g = Polygon(rings[0], rings[1:]).buffer(0)
                except Exception:
                    continue
                if g.is_empty or not pcor.intersects(g): continue
                g = g.intersection(cor)
                for pg in getattr(g, 'geoms', [g]):
                    if pg.geom_type == 'Polygon' and pg.area > 0.5: buckets.setdefault(cat, []).append(pg); kept += 1
                if cat == 'road_area':           # 도로 가장자리 선도 따로(보기 쉽게)
                    for pg in getattr(g, 'geoms', [g]):
                        if pg.geom_type != 'Polygon': continue
                        for ring in [pg.exterior] + list(pg.interiors):
                            buckets.setdefault('road_edge', []).append(ribbon(LineString(ring.coords), 0.25))
            elif st in (3, 13, 23):        # 선
                for k in range(len(parts) - 1):
                    seg = P[parts[k]:parts[k + 1]]
                    if len(seg) < 2: continue
                    ln = LineString(seg)
                    if not pcor.intersects(ln): continue
                    g = ln.intersection(cor)
                    for l in getattr(g, 'geoms', [g]):
                        if l.geom_type == 'LineString' and l.length > 0.5:
                            buckets.setdefault(cat, []).append(ribbon(l, 0.35 if cat == 'road_center' else 0.25)); kept += 1
        info['kept_in_course'] = kept
        summary.append(info)
        log(f"{info['file']:<28} {desc or cat:<14} 전체 {n:>7}  코스 안 {kept:>6}  {info['crs'][:40]}")
    # 메시 JSON (분류별, 6만 꼭짓점씩)
    out_files = []
    for cat, polys in buckets.items():
        chunks = []; V_acc = []; T_acc = []; nv = 0
        def flush():
            nonlocal V_acc, T_acc, nv
            if nv == 0: return
            V = np.vstack(V_acc); T = np.vstack(T_acc)
            chunks.append(dict(cat=cat, v=np.round(V, 3).ravel().tolist(), t=T.astype(int).ravel().tolist()))
            V_acc = []; T_acc = []; nv = 0
        for pg in polys:
            V, T = tri_poly(pg)
            if V is None or len(T) == 0: continue
            if nv + len(V) > MAXV: flush()
            V_acc.append(V); T_acc.append(T + nv); nv += len(V)
        flush()
        if not chunks: continue
        fn = os.path.join(a.out, f'ngii_{cat}.json')
        json.dump(dict(cat=cat, color=COLORS.get(cat, (0.7, 0.7, 0.7, 0.5)), chunks=chunks), open(fn, 'w', encoding='utf-8'), separators=(',', ':'))
        out_files.append((fn, sum(len(c['v']) // 2 for c in chunks)))
    with open(os.path.join(a.out, 'ngii_summary.txt'), 'w', encoding='utf-8') as fh:
        fh.write('수치지형도 오버레이 요약(레이어 이름·개수만, 좌표 없음)\n')
        for s in summary: fh.write(json.dumps(s, ensure_ascii=False) + '\n')
        for fn, nv in out_files: fh.write(f'출력 {os.path.basename(fn)} 꼭짓점 {nv}\n')
    log('')
    for fn, nv in out_files: log('만듦:', fn, '꼭짓점', nv)
    log(f'끝 ({time.time() - t0:.0f}초). 유니티 메뉴 PNU Map > NGII 오버레이 불러오기 를 누르세요.')
    log('요약 파일:', os.path.join(a.out, 'ngii_summary.txt'), '(레이어 이름·개수만 들어 있어요)')


if __name__ == '__main__':
    main()
