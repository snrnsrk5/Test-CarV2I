// PNU 도로 생성기 — 노면 글자(한글·숫자)를 다각형으로 만듦
//   모양: 사용자 SVG 글꼴(Group_282)을 따름 — 획 굵기 20, 자모 사이 10, 글자 110×280(진행 방향으로 긴 노면용), 네모 끝,
//         ㅇ = 세로로 긴 둥근 고리, ㅏ ㅓ 꼭지 = 짧고 높은 네모(길이 10, 높이 = 모음 높이 × 0.2)
//   SVG 를 잘라 붙이지 않고 획(중심선 + 굵기)으로 다시 그림 → 칸 크기가 달라도 획 굵기·빈칸이 늘 같음, 숫자도 같은 획
//   빈칸 규칙: 자모 사이 = G(가로·세로 같음), 글자 둘레 = M(상하좌우 같음) → 글자와 글자 사이 = 2M
//   좌표: 글꼴 단위, x = 오른쪽, z = 위(글자 위쪽 = 운전자에게서 먼 쪽). 다각형끼리 겹칠 수 있음(합친 모양이 글자)
using System;
using System.Collections.Generic;

namespace PNU.RoadGen
{
    [Serializable]
    public class GlyphStyle
    {
        public double W = 110, H = 280;                  // 글자(잉크) 칸
        public double S = 20;                            // 획 굵기
        public double G = 10;                            // 자모 사이 빈칸(가로·세로 같음)
        public double M = 10;                            // 글자 둘레 빈칸(상하좌우 같음)
        public double stubLen = 10, stubFrac = 0.2;      // ㅏ ㅓ 꼭지: 길이, 높이(모음 높이 × 비율)
        public double pairGap = 5;                       // ㅐ ㅔ 두 기둥 사이
        public double finH = 90;                         // 받침 높이
        public double hvTall = 90, hvShort = 50;         // ㅗ ㅜ 류 높이(받침 없음 / 있음)
        public int topMark = 2;                          // ㅊ ㅎ 꼭지: 0 짧은 가로획(띄움) 1 짧은 세로획(띄움) 2 붙은 세로획(원래 SVG — 사용자 선택)
        public double legFrac = 0.65;                    // ㅅ ㅈ ㅊ 다리 높이 = 칸 높이 × legFrac (1 = 꼭대기부터 Λ, 작을수록 위 기둥이 김). 사용자 19:44: 60~70% 가 예쁨
        public bool tuckStubs = false;                   // ㅓ ㅕ ㅔ ㅖ: 자음 칸을 꼭지 자리까지 넓히고 꼭지는 자음 안쪽 빈 곳에. 사용자 23:34: 일단 끔(①, 자음은 꼭지 앞에서 끝남)
        public double tuckMaxShift = 0.06;               // 그때 꼭지를 위아래로 옮겨도 되는 만큼(모음 높이 비율). 많이 옮기면(터·러) 어색 → 조금만, 안 되면 보통 배치
        public double legK = 0.62;                       // 다리 최소 높이 = 칸 폭 × legK(납작한 칸에서도 다리가 보이게)
        public double digitW = 110;                      // 숫자 칸 폭
        public double arcStepDeg = 10;
    }

    public static class RoadFont
    {
        const string CHO = "ㄱㄲㄴㄷㄸㄹㅁㅂㅃㅅㅆㅇㅈㅉㅊㅋㅌㅍㅎ";
        const string JUNG = "ㅏㅐㅑㅒㅓㅔㅕㅖㅗㅘㅙㅚㅛㅜㅝㅞㅟㅠㅡㅢㅣ";
        static readonly string[] JONG = { "", "ㄱ", "ㄲ", "ㄱㅅ", "ㄴ", "ㄴㅈ", "ㄴㅎ", "ㄷ", "ㄹ", "ㄹㄱ", "ㄹㅁ", "ㄹㅂ", "ㄹㅅ", "ㄹㅌ", "ㄹㅍ", "ㄹㅎ", "ㅁ", "ㅂ", "ㅂㅅ", "ㅅ", "ㅆ", "ㅇ", "ㅈ", "ㅊ", "ㅋ", "ㅌ", "ㅍ", "ㅎ" };

        /// <summary>한 줄 글자(가로쓰기 — 운전자가 보는 왼쪽→오른쪽). 결과 원점 = 왼쪽 아래(운전자 쪽) 칸 모서리</summary>
        public static List<List<V2>> Line(string text, GlyphStyle st, out double width, out double height)
        {
            var polys = new List<List<V2>>(); double x = 0;
            foreach (char ch in text)
            {
                if (ch == ' ') { x += (st.W + 2 * st.M) * 0.4; continue; }
                bool dg = ch >= '0' && ch <= '9';
                double cw = dg ? st.digitW : st.W;
                if (dg) Digit(ch, st, polys, x + st.M, st.M); else Syllable(ch, st, polys, x + st.M, st.M);
                x += cw + 2 * st.M;
            }
            width = x; height = st.H + 2 * st.M; return polys;
        }

        public static bool CanDraw(char ch) { return ch == ' ' || (ch >= '0' && ch <= '9') || (ch >= 0xAC00 && ch < 0xAC00 + 11172); }

        // ---------------- 음절 ----------------
        /// <summary>음절 하나(잉크 칸 왼쪽 아래 = ox, oz)</summary>
        public static void Syllable(char ch, GlyphStyle st, List<List<V2>> polys, double ox, double oz)
        {
            int code = ch - 0xAC00; if (code < 0 || code >= 11172) return;
            char cho = CHO[code / 588], jung = JUNG[(code % 588) / 28]; string jong = JONG[code % 28];
            var root = new Pen(polys, st, ox, oz + st.H, st.W, st.H);
            double W = st.W, H = st.H, G = st.G, s = st.S;
            bool hasF = jong.Length > 0;
            double topH = hasF ? H - G - st.finH : H;                     // 받침 위 칸 높이
            string vert = "ㅏㅐㅑㅒㅓㅔㅕㅖㅣ", horz = "ㅗㅛㅜㅠㅡ";
            if (vert.IndexOf(jung) >= 0)
            {
                double vw = VW(jung, st);
                if (!(st.tuckStubs && "ㅓㅕㅔㅖ".IndexOf(jung) >= 0 && Tuck(root, jung, cho, topH)))
                {
                    VVowel(root.Sub(W - vw, 0, vw, topH), jung);
                    Cons(root.Sub(0, 0, W - vw - G, topH), cho);
                }
            }
            else if (horz.IndexOf(jung) >= 0)
            {
                double hv = jung == 'ㅡ' ? s : (hasF ? st.hvShort : st.hvTall);
                HVowel(root.Sub(0, topH - hv, W, hv), jung, -1);
                Cons(root.Sub(0, 0, W, topH - hv - G), cho);
            }
            else Compound(root, jung, cho, topH, hasF);
            if (hasF)
            {   // 받침 오른쪽 끝 = 모음 기둥(ㅣ) 오른쪽 끝 — ㅏ ㅑ ㅘ 는 꼭지(·)만큼 안쪽(예: 학 의 ㄱ 은 ㅏ 기둥까지)
                double fw = (jung == 'ㅏ' || jung == 'ㅑ' || jung == 'ㅘ') ? W - st.stubLen : W;
                var f = root.Sub(0, H - st.finH, fw, st.finH);
                if (jong.Length == 1) Cons(f, jong[0]);
                else { double hw = (fw - G) / 2; Cons(f.Sub(0, 0, hw, st.finH), jong[0]); Cons(f.Sub(fw - hw, 0, hw, st.finH), jong[1]); }
            }
        }

        /// <summary>ㅓ ㅕ ㅔ ㅖ: 자음 칸을 꼭지 자리까지 넓히고(처·체·터 …), 꼭지는 자음 오른쪽 안쪽 빈 곳에 넣음.
        /// 꼭지 둘레 G 안에 자음 획이 없는 높이를 원래 자리(가운데 / 0.3·0.7)에서 가까운 순으로 찾음. 못 찾으면(ㄱ ㅁ ㅇ 처럼 오른쪽이 막힌 자음) false → 보통 배치</summary>
        static bool Tuck(Pen root, char jung, char cho, double topH)
        {
            var st = root.st; double W = st.W, G = st.G, s = st.S, sl = st.stubLen, sh = st.stubFrac * topH;
            double stemsW = VW(jung, st) - sl, cw = W - stemsW - G;          // 넓힌 자음 칸 폭
            if (cw < 2 * s) return false;
            var tmp = new List<List<V2>>();
            Cons(new Pen(tmp, st, root.x0, root.top, cw, topH), cho);
            double sx0 = W - stemsW - sl;                                    // 꼭지 왼쪽 x(칸 안)
            bool two = jung == 'ㅕ' || jung == 'ㅖ';
            double[] want = two ? new[] { 0.3 * topH, 0.7 * topH } : new[] { 0.5 * topH };
            var got = new double[want.Length]; double minY = 0;
            for (int i = 0; i < want.Length; i++)
            {
                double best = double.NaN, lim = st.tuckMaxShift * topH;
                for (double d = 0; d <= lim && double.IsNaN(best); d += 2)
                    foreach (double sg in d == 0 ? new[] { 1.0 } : new[] { -1.0, 1.0 })
                    {
                        double yc = want[i] + sg * d, y0 = yc - sh / 2, y1 = yc + sh / 2;
                        if (y0 < minY || y1 > topH) continue;
                        if (!Hits(tmp, root.x0 + sx0 - G, root.top - (y1 + G), root.x0 + sx0 + sl, root.top - (y0 - G))) { best = yc; break; }
                    }
                if (double.IsNaN(best)) return false;
                got[i] = best; minY = best + sh / 2 + G;
            }
            root.polys.AddRange(tmp);
            var v = root.Sub(W - stemsW, 0, stemsW, topH);
            v.Rect(0, 0, s, topH); if (jung == 'ㅔ' || jung == 'ㅖ') v.Rect(s + st.pairGap, 0, 2 * s + st.pairGap, topH);
            foreach (var yc in got) root.Rect(sx0, yc - sh / 2, sx0 + sl, yc + sh / 2);
            return true;
        }
        /// <summary>다각형 중 하나라도 네모(x0,z0)~(x1,z1)와 겹치나</summary>
        static bool Hits(List<List<V2>> polys, double x0, double z0, double x1, double z1)
        {
            foreach (var p in polys) { var c = Clip(p, x0, z0, x1, z1); if (c.Count >= 3 && Math.Abs(Geom.SignedArea(c)) > 0.5) return true; }
            return false;
        }

        static double VW(char v, GlyphStyle st)
        {
            switch (v)
            {
                case 'ㅣ': return st.S;
                case 'ㅏ': case 'ㅑ': case 'ㅓ': case 'ㅕ': return st.S + st.stubLen;
                case 'ㅐ': case 'ㅒ': return 2 * st.S + st.pairGap;
                default: return st.stubLen + 2 * st.S + st.pairGap;      // ㅔ ㅖ
            }
        }

        /// <summary>세로 모음(칸 폭 = VW)</summary>
        static void VVowel(Pen p, char v)
        {
            var st = p.st; double s = st.S, h = p.h, sl = st.stubLen, sh = st.stubFrac * h, pg = st.pairGap;
            double[] one = { 0.5 }, two = { 0.3, 0.7 };
            switch (v)
            {
                case 'ㅣ': p.Rect(0, 0, s, h); break;
                case 'ㅏ': case 'ㅑ':
                    p.Rect(0, 0, s, h); foreach (var f in v == 'ㅏ' ? one : two) p.Rect(s, f * h - sh / 2, s + sl, f * h + sh / 2); break;
                case 'ㅓ': case 'ㅕ':
                    p.Rect(sl, 0, sl + s, h); foreach (var f in v == 'ㅓ' ? one : two) p.Rect(0, f * h - sh / 2, sl, f * h + sh / 2); break;
                case 'ㅐ': case 'ㅒ':
                    p.Rect(0, 0, s, h); p.Rect(s + pg, 0, 2 * s + pg, h);
                    foreach (var f in v == 'ㅐ' ? one : two) p.Rect(s, f * h - sh / 2, s + pg, f * h + sh / 2); break;
                case 'ㅔ': case 'ㅖ':
                    p.Rect(sl, 0, sl + s, h); p.Rect(sl + s + pg, 0, sl + 2 * s + pg, h);
                    foreach (var f in v == 'ㅔ' ? one : two) p.Rect(0, f * h - sh / 2, sl, f * h + sh / 2); break;
            }
        }

        /// <summary>가로 모음(ㅗ ㅛ ㅜ ㅠ ㅡ) — stemX: 기둥 가운데(음수면 칸 가운데)</summary>
        static void HVowel(Pen p, char v, double stemX)
        {
            double s = p.st.S, w = p.w, h = p.h, c = stemX >= 0 ? stemX : w / 2;
            double c1 = w * 0.318, c2 = w * 0.682;
            switch (v)
            {
                case 'ㅡ': p.Rect(0, 0, w, s); break;
                case 'ㅗ': p.Rect(0, h - s, w, h); p.Rect(c - s / 2, 0, c + s / 2, h - s); break;
                case 'ㅛ': p.Rect(0, h - s, w, h); p.Rect(c1 - s / 2, 0, c1 + s / 2, h - s); p.Rect(c2 - s / 2, 0, c2 + s / 2, h - s); break;
                case 'ㅜ': p.Rect(0, 0, w, s); p.Rect(c - s / 2, s, c + s / 2, h); break;
                case 'ㅠ': p.Rect(0, 0, w, s); p.Rect(c1 - s / 2, s, c1 + s / 2, h); p.Rect(c2 - s / 2, s, c2 + s / 2, h); break;
            }
        }

        /// <summary>섞인 모음(ㅘ ㅙ ㅚ ㅝ ㅞ ㅟ ㅢ): 초성 왼쪽 위, 가로 모음 왼쪽 아래, 세로 모음 오른쪽(전체 높이)</summary>
        static void Compound(Pen root, char jung, char cho, double topH, bool hasF)
        {
            var st = root.st; double W = st.W, G = st.G, s = st.S;
            char hz, vt;
            switch (jung)
            {
                case 'ㅘ': hz = 'ㅗ'; vt = 'ㅏ'; break;
                case 'ㅙ': hz = 'ㅗ'; vt = 'ㅐ'; break;
                case 'ㅚ': hz = 'ㅗ'; vt = 'ㅣ'; break;
                case 'ㅝ': hz = 'ㅜ'; vt = 'ㅓ'; break;
                case 'ㅞ': hz = 'ㅜ'; vt = 'ㅔ'; break;
                case 'ㅟ': hz = 'ㅜ'; vt = 'ㅣ'; break;
                default: hz = 'ㅡ'; vt = 'ㅣ'; break;      // ㅢ
            }
            double hv = hz == 'ㅡ' ? s : (hasF ? st.hvShort : st.hvTall);
            bool lowStub = vt == 'ㅓ' || vt == 'ㅔ';               // ㅝ ㅞ: ㅓ 꼭지는 ㅜ 가로획 아래(SVG 와 같음)
            double vw = lowStub ? (vt == 'ㅓ' ? s : 2 * s + st.pairGap) : VW(vt, st);
            double wl = W - vw - G;                                  // 왼쪽(초성·가로 모음) 폭
            if (lowStub)
            {
                var v = root.Sub(W - vw, 0, vw, topH);
                v.Rect(0, 0, s, topH); if (vt == 'ㅔ') v.Rect(s + st.pairGap, 0, 2 * s + st.pairGap, topH);
                double yc = (topH - hv + s + topH) / 2, sx = W - vw;
                root.Rect(sx - s, yc - s / 2, sx, yc + s / 2);       // 꼭지(ㅜ 가로획 아래, 기둥에 붙음)
                double stemC = Math.Min(wl / 2, sx - s - G - s / 2);
                HVowel(root.Sub(0, topH - hv, wl, hv), hz, stemC);
            }
            else
            {
                VVowel(root.Sub(W - vw, 0, vw, topH), vt);
                HVowel(root.Sub(0, topH - hv, wl, hv), hz, -1);
            }
            Cons(root.Sub(0, 0, wl, topH - hv - G), cho);
        }

        /// <summary>자음(초성·받침) — 칸을 꽉 채움</summary>
        static void Cons(Pen p, char c)
        {
            var st = p.st; double w = p.w, h = p.h, s = st.S;
            if (w < 2 || h < 2) return;
            switch (c)
            {
                case 'ㄱ': p.Line(0, s / 2, w - s / 2, s / 2, w - s / 2, h); break;
                case 'ㄴ': p.Line(s / 2, 0, s / 2, h - s / 2, w, h - s / 2); break;
                case 'ㄷ': p.Line(w, s / 2, s / 2, s / 2, s / 2, h - s / 2, w, h - s / 2); break;
                case 'ㄹ': { double ym = h / 2; p.Line(0, s / 2, w - s / 2, s / 2, w - s / 2, ym, s / 2, ym, s / 2, h - s / 2, w, h - s / 2); break; }
                case 'ㅁ': p.Rect(0, 0, w, s); p.Line(s / 2, 0, s / 2, h - s / 2, w - s / 2, h - s / 2, w - s / 2, 0); break;
                case 'ㅂ': { p.Line(s / 2, 0, s / 2, h - s / 2, w - s / 2, h - s / 2, w - s / 2, 0); double ym = (h - s) / 2; p.Rect(s, ym - s / 2, w - s, ym + s / 2); break; }
                case 'ㅅ': p.Siot(0, 0, w, h); break;
                case 'ㅇ': p.Ring(0, 0, w, h); break;
                case 'ㅈ': p.Rect(0, 0, w, s); p.Siot(0, s, w, h); break;
                case 'ㅊ': { double y = p.TopMark(w); p.Rect(0, y, w, y + s); p.Siot(0, y + s, w, h); break; }
                case 'ㅋ': { p.Line(0, s / 2, w - s / 2, s / 2, w - s / 2, h); double ym = (h + s) / 2; p.Rect(0, ym - s / 2, w - s, ym + s / 2); break; }
                case 'ㅌ': { p.Line(w, s / 2, s / 2, s / 2, s / 2, h - s / 2, w, h - s / 2); double ym = h / 2; p.Rect(s, ym - s / 2, w, ym + s / 2); break; }
                case 'ㅍ':
                    {
                        p.Rect(0, 0, w, s); p.Rect(0, h - s, w, h);
                        double avail = w - 2 * s, gi = Math.Min(20, Math.Max(st.G, avail - 10)), ins = Math.Max(0, Math.Min(25, (avail - gi) / 2));
                        gi = avail - 2 * ins; double xa = ins, xb = ins + s + gi;
                        p.Rect(xa, s, xa + s, h - s); p.Rect(xb, s, xb + s, h - s); break;
                    }
                case 'ㅎ':
                    {
                        double y = p.TopMark(w); p.Rect(0, y, w, y + s);
                        double r0 = y + s + st.G; if (h - r0 >= 2 * s + 4) p.Ring(0, r0, w, h); else p.Rect(0, r0, w, h);
                        break;
                    }
                case 'ㄲ': case 'ㄸ': case 'ㅃ': case 'ㅆ': case 'ㅉ':
                    {
                        char b = c == 'ㄲ' ? 'ㄱ' : c == 'ㄸ' ? 'ㄷ' : c == 'ㅃ' ? 'ㅂ' : c == 'ㅆ' ? 'ㅅ' : 'ㅈ';
                        double hw = (w - st.G) / 2; Cons(p.Sub(0, 0, hw, h), b); Cons(p.Sub(w - hw, 0, hw, h), b); break;
                    }
            }
        }

        // ---------------- 숫자 ----------------
        public static void Digit(char d, GlyphStyle st, List<List<V2>> polys, double ox, double oz)
        {
            var p = new Pen(polys, st, ox, oz + st.H, st.digitW, st.H);
            if (d == '9') { p.flip = true; DigitShape(p, '6'); return; }
            DigitShape(p, d);
        }
        static void DigitShape(Pen p, char d)
        {
            double w = p.w, h = p.h, s = p.st.S, R = w / 2, rc = R - s / 2, c = w / 2;
            switch (d)
            {
                case '0': p.Ring(0, 0, w, h); break;
                case '1':
                    {
                        double xs = c + 0.08 * w; p.Rect(xs - s / 2, 0, xs + s / 2, h);
                        p.Stroke(new List<V2> { new V2(xs, s * 0.5), new V2(xs - 0.36 * w, s * 0.5 + 0.3 * w) }, s); break;
                    }
                case '2':
                    {
                        var b = new PathB(p.st); b.L(s / 2, R + 0.05 * h); b.L(s / 2, R); b.A(c, R, rc, 180, 0); b.L(w - s / 2, R + 0.12 * h);
                        b.L(s / 2, h - s / 2); b.L(w, h - s / 2); p.Stroke(b.p, s); break;
                    }
                case '3':
                    {
                        double ym = h * 0.46;
                        p.Stroke(Bowl(p.st, w, s, 0, ym + s / 2, 0.06 * w, 0.32 * w), s);
                        p.Stroke(Bowl(p.st, w, s, ym - s / 2, h, 0.32 * w, 0.06 * w), s); break;
                    }
                case '4':
                    {
                        double xs = 0.66 * w, yb = 0.70 * h;
                        p.Stroke(new List<V2> { new V2(xs, -s), new V2(s * 0.55, yb) }, s * 1.05);
                        p.Rect(0, yb - s / 2, w, yb + s / 2); p.Rect(xs - s / 2, 0, xs + s / 2, h); break;
                    }
                case '5':
                    {
                        double y5 = 0.40 * h; p.Rect(0, 0, w, s); p.Rect(0, 0, s, y5 + s);
                        p.Stroke(Bowl(p.st, w, s, y5, h, s / 2, 0), s); break;
                    }
                case '6':
                    {
                        double y6 = 0.40 * h; p.Ring(0, y6, w, h);
                        var b = new PathB(p.st); b.L(w - s / 2, R + 0.05 * h); b.L(w - s / 2, R); b.A(c, R, rc, 0, 180); b.L(s / 2, y6 + R); p.Stroke(b.p, s); break;
                    }
                case '7': p.Line(0, s / 2, w - s / 2, s / 2, 0.30 * w, h + s); break;
                case '8':
                    {
                        double ym = h * 0.46, ins = 0.06 * w;
                        p.Sub(ins, 0, w - 2 * ins, ym + s / 2).Ring(0, 0, w - 2 * ins, ym + s / 2);
                        p.Ring(0, ym - s / 2, w, h); break;
                    }
            }
        }
        /// <summary>오른쪽으로 불룩한 그릇(3·5 의 아래): 위 가로획 → 오른쪽 반원 → 아래 가로획 중심선</summary>
        static List<V2> Bowl(GlyphStyle st, double w, double s, double y0, double y1, double xStart, double xEnd)
        {
            double R = Math.Min(w / 2, (y1 - y0) / 2), rc = R - s / 2, cx = w - R;
            var b = new PathB(st); b.L(xStart, y0 + s / 2); b.L(cx, y0 + s / 2);
            b.A(cx, y0 + R, rc, 90, 0); b.L(w - s / 2, y1 - R); b.A(cx, y1 - R, rc, 0, -90); b.L(xEnd, y1 - s / 2);
            return b.p;
        }

        // ---------------- 그리기 도구(칸 안 좌표: x 오른쪽, y 아래) ----------------
        sealed class PathB
        {
            public readonly List<V2> p = new List<V2>(); public double stepDeg = 10;
            public PathB() { } public PathB(GlyphStyle st) { stepDeg = Math.Max(3, st.arcStepDeg); }
            public void L(double x, double y) { var q = new V2(x, y); if (p.Count == 0 || (p[p.Count - 1] - q).Len > 1e-6) p.Add(q); }
            public void A(double cx, double cy, double r, double a0, double a1)
            {
                double r0 = a0 * Math.PI / 180, r1 = a1 * Math.PI / 180; int n = Math.Max(2, (int)Math.Ceiling(Math.Abs(r1 - r0) / (stepDeg * Math.PI / 180)));
                for (int i = 0; i <= n; i++) { double a = r0 + (r1 - r0) * i / n; L(cx + r * Math.Cos(a), cy - r * Math.Sin(a)); }
            }
        }

        sealed class Pen
        {
            public readonly List<List<V2>> polys; public readonly GlyphStyle st;
            public readonly double x0, top, w, h; public bool flip;
            public Pen(List<List<V2>> polys, GlyphStyle st, double x0, double top, double w, double h) { this.polys = polys; this.st = st; this.x0 = x0; this.top = top; this.w = w; this.h = h; }
            public Pen Sub(double x, double y, double ww, double hh) { return new Pen(polys, st, x0 + x, top - y, ww, hh) { flip = flip }; }
            V2 World(V2 q) { if (flip) q = new V2(w - q.x, h - q.z); return new V2(x0 + q.x, top - q.z); }
            void Emit(List<V2> loc)
            {
                if (loc.Count < 3) return;
                var o = new List<V2>(loc.Count); foreach (var q in loc) o.Add(World(q));
                if (Geom.SignedArea(o) < 0) o.Reverse();
                if (Math.Abs(Geom.SignedArea(o)) > 1e-3) polys.Add(o);
            }
            public void Rect(double ax, double ay, double bx, double by)
            {
                ax = Math.Max(0, ax); ay = Math.Max(0, ay); bx = Math.Min(w, bx); by = Math.Min(h, by);
                if (bx - ax < 1e-6 || by - ay < 1e-6) return;
                Emit(new List<V2> { new V2(ax, ay), new V2(bx, ay), new V2(bx, by), new V2(ax, by) });
            }
            /// <summary>중심선(점 x,y 차례) + 획 굵기, 끝은 자른 모양, 칸 밖은 잘라냄</summary>
            public void Line(params double[] xy)
            {
                var pts = new List<V2>(); for (int i = 0; i + 1 < xy.Length; i += 2) pts.Add(new V2(xy[i], xy[i + 1]));
                Stroke(pts, st.S);
            }
            public void Stroke(List<V2> pts, double width) { Stroke(pts, width, 0, 0, w, h); }
            public void Stroke(List<V2> pts, double width, double cx0, double cy0, double cx1, double cy1)
            {
                var q = new List<V2>(); foreach (var t in pts) if (q.Count == 0 || (q[q.Count - 1] - t).Len > 1e-6) q.Add(t);
                if (q.Count < 2) return;
                Emit(Clip(Outline(q, width), cx0, cy0, cx1, cy1));
            }
            /// <summary>ㅊ ㅎ 꼭지 → 아래 가로획이 시작하는 y</summary>
            public double TopMark(double ww)
            {
                double s = st.S, c = ww / 2;
                switch (st.topMark)
                {
                    case 0: { double a = Math.Max(1.5 * s, 0.45 * ww); Rect(c - a / 2, 0, c + a / 2, s); return s + st.G; }
                    case 1: { Rect(c - s / 2, 0, c + s / 2, 1.25 * s); return 1.25 * s + st.G; }
                    default: { double t = h >= 200 ? 1.5 * s : s; Rect(c - s / 2, 0, c + s / 2, t); return t; }   // 원래 SVG: 바로 아래 가로획에 붙음
                }
            }
            /// <summary>ㅅ 꼴(기둥 + 두 다리) — 칸 (ax,ay)~(bx,by)</summary>
            public void Siot(double ax, double ay, double bx, double by)
            {
                double ww = bx - ax, hh = by - ay, s = st.S, c = (ax + bx) / 2;
                if (ww < 2 || hh < 2) return;
                double Lh = Math.Min(hh, Math.Max(st.legFrac * hh, Math.Min(hh, Math.Max(30, st.legK * ww))));
                double yj = by - Lh;
                if (yj - ay > 0.5) Rect(c - s / 2, ay, c + s / 2, yj + s * 0.6);
                var J = new V2(c, yj);
                foreach (var foot in new[] { new V2(ax + s * 0.55, by), new V2(bx - s * 0.55, by) })
                {
                    var d = (foot - J).Norm;
                    Stroke(new List<V2> { J - d * (s * 0.4), foot + d * (s * 1.5) }, s * 0.95, ax, ay, bx, by);
                }
            }
            /// <summary>ㅇ 고리(둥근 네모, 짧은 쪽이 반원) — 왼쪽·오른쪽 반쪽 두 장(구멍 없는 다각형)</summary>
            public void Ring(double ax, double ay, double bx, double by)
            {
                double ww = bx - ax, hh = by - ay, s = st.S, cx = (ax + bx) / 2;
                double R = Math.Min(ww, hh) / 2, rc = R - s / 2;
                if (rc < 2) { Rect(ax, ay, bx, by); return; }
                var L = new PathB(st); var Rt = new PathB(st); const double e = 0.5;     // 두 반쪽이 가운데서 살짝 겹치게(이음 자국 없음)
                if (ww <= hh)
                {
                    double cyT = ay + R, cyB = by - R;
                    L.L(cx + e, ay + s / 2); L.A(cx, cyT, rc, 90, 180); L.A(cx, cyB, rc, 180, 270); L.L(cx + e, by - s / 2);
                    Rt.L(cx - e, ay + s / 2); Rt.A(cx, cyT, rc, 90, 0); Rt.A(cx, cyB, rc, 0, -90); Rt.L(cx - e, by - s / 2);
                }
                else
                {
                    double cxL = ax + R, cxR = bx - R, cy = (ay + by) / 2;
                    L.L(cx + e, ay + s / 2); L.A(cxL, cy, rc, 90, 270); L.L(cx + e, by - s / 2);
                    Rt.L(cx - e, ay + s / 2); Rt.A(cxR, cy, rc, 90, -90); Rt.L(cx - e, by - s / 2);
                }
                Stroke(L.p, s, ax, ay, bx, by); Stroke(Rt.p, s, ax, ay, bx, by);
            }
        }

        /// <summary>굵은 꺾은선 테두리(꺾인 곳은 뾰족이음, 끝은 자름)</summary>
        static List<V2> Outline(List<V2> p, double wdt)
        {
            int n = p.Count; var Lf = new List<V2>(n); var Rg = new List<V2>(n); double hw = wdt / 2;
            for (int i = 0; i < n; i++)
            {
                V2 d0 = i > 0 ? (p[i] - p[i - 1]).Norm : (p[1] - p[0]).Norm;
                V2 d1 = i < n - 1 ? (p[i + 1] - p[i]).Norm : d0;
                V2 n0 = d0.Left, n1 = d1.Left, m = n0 + n1;
                m = m.Len < 1e-9 ? n0 : m.Norm;
                double cs = Math.Max(0.25, V2.Dot(m, n0));
                Lf.Add(p[i] + m * (hw / cs)); Rg.Add(p[i] - m * (hw / cs));
            }
            Rg.Reverse(); Lf.AddRange(Rg); return Lf;
        }
        /// <summary>네모 칸으로 자르기(서덜랜드-호지먼)</summary>
        static List<V2> Clip(List<V2> poly, double x0, double y0, double x1, double y1)
        {
            var r = poly;
            r = ClipHalf(r, q => q.x - x0, (a, b) => Cut(a, b, a.x - x0, b.x - x0));
            r = ClipHalf(r, q => x1 - q.x, (a, b) => Cut(a, b, x1 - a.x, x1 - b.x));
            r = ClipHalf(r, q => q.z - y0, (a, b) => Cut(a, b, a.z - y0, b.z - y0));
            r = ClipHalf(r, q => y1 - q.z, (a, b) => Cut(a, b, y1 - a.z, y1 - b.z));
            return r;
        }
        static V2 Cut(V2 a, V2 b, double fa, double fb) { double t = fa / (fa - fb); return V2.Lerp(a, b, t); }
        static List<V2> ClipHalf(List<V2> p, Func<V2, double> f, Func<V2, V2, V2> cut)
        {
            var o = new List<V2>(); int n = p.Count; if (n == 0) return o;
            for (int i = 0; i < n; i++)
            {
                V2 a = p[i], b = p[(i + 1) % n]; double fa = f(a), fb = f(b);
                if (fa >= 0) o.Add(a);
                if ((fa >= 0) != (fb >= 0)) o.Add(cut(a, b));
            }
            return o;
        }
    }
}
