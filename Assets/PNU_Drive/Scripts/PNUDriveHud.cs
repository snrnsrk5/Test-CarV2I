// 5/6 스토리보드 화면(월드 공간 UI — 나중에 VR 손 조작에도 그대로): 메뉴바·고정해제·학습 변경, 제목·안내 띠·경고,
//   상황 체크리스트 판(108·109·112·113), 목적지 설정(105~106)·경로 카드(107)·결과판(117)·조작안내·학습 변경 목록, 주변 물체 목록
// 위치: "사용자 시점 방면 중앙 10도 아래" 1.1m 앞(고정 = 차 기준, 고정해제 = 고개 방향 따라옴, 따라가기 시점 = 카메라 앞)
// 글자: 나눔바른고딕(OFL, 원본), 그림: UI/ 둥근 판·아이콘 — 모두 코드로 만듦(프리팹 없음). 계기판에 가리지 않게 ZTest Always
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UI;

namespace PNU.Drive
{
    public class PNUDriveHud : MonoBehaviour
    {
        [Header("글꼴·그림(빌더가 채움)")]
        public Font fontRegular, fontBold;
        public Texture2D texRound, texRoundS, texRing, texPill, texShadow;
        public Texture2D[] iconTex; public string[] iconNames;
        [Header("자리")]
        public Camera cam; public PNUDriveEgo ego;
        public float distance = 1.1f, downAngle = 10f;
        public bool locked = true;                                  // 고정(차 기준) / 고정해제(고개 따라옴)
        public Action<string> onButton;

        // 색
        public static readonly Color Blue = new Color(0.17f, 0.42f, 0.95f), BlueD = new Color(0.11f, 0.3f, 0.78f), BlueL = new Color(0.86f, 0.91f, 1f);
        public static readonly Color Ink = new Color(0.13f, 0.15f, 0.2f), Gray = new Color(0.45f, 0.48f, 0.53f), GrayL = new Color(0.9f, 0.91f, 0.93f);
        public static readonly Color Green = new Color(0.1f, 0.62f, 0.32f), Red = new Color(0.9f, 0.2f, 0.18f), Orange = new Color(1f, 0.6f, 0.12f), Navy = new Color(0.05f, 0.1f, 0.22f);

        Sprite sRound, sRoundS, sRing, sPill, sShadow; readonly Dictionary<string, Sprite> icons = new Dictionary<string, Sprite>();
        Material uiMat; Canvas canvas; RectTransform root; Transform anchor; float followYaw; bool built;
        // 요소
        RectTransform menu, toggles, titleRt, bannerRt, warnRt, panelRt, destRt, routeRt, resultRt, helpRt, switchRt, objRt;
        Text driveLabel, lockLabel, titleText, bannerText, warnText1, warnText2, panelHead, panelPhase, panelFoot, routeEta, routeKm, routeTitle, destTitle, destInfo, destInfo2;
        Image driveIcon, lockIcon, panelHeadBg, panelBg, panelFootIcon, phaseSpin;
        readonly Image[] rowIcon = new Image[4], rowStIcon = new Image[4], rowPillBg = new Image[4], rowNum = new Image[4];
        readonly Text[] rowName = new Text[4], rowDetail = new Text[4], rowSt = new Text[4], rowNumT = new Text[4];
        Text resTitle, resTime, resDist, resAvg, resStops; readonly Text[] resEv = new Text[4], resEvR = new Text[4]; readonly Image[] resEvI = new Image[4]; Text resLog;
        readonly Image[] objIcon = new Image[3]; readonly Text[] objText = new Text[3]; readonly GameObject[] objRow = new GameObject[3];
        RectTransform v2iRt; Image v2iIcon; Text v2iHead, v2iDetail;          // v17 V2I 신호 정보(왼쪽 위, 주행 내내)
        CanvasGroup titleCg, bannerCg, warnCg, panelCg; float titleUntil, bannerUntil; bool panelOn, warnOn; float panelA, titleA, bannerA, warnA;
        float titleX = -40f, bannerX = -40f; // 상황 판이 열려 있으면 판과 안 겹치게 왼쪽으로
        string panelKey = "";
        // 페이드(카메라 앞 검은 판)
        Canvas fadeCanvas; Image fadeImg; float fadeA, fadeTarget, fadeSpeed = 1f;

        public bool Built { get { return built; } }

        void Start() { Build(); }

        // ------------------------------------------------------------------ 만들기
        Sprite Spr(Texture2D t, float border)
        {
            if (t == null) return null;
            return Sprite.Create(t, new Rect(0, 0, t.width, t.height), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect, new Vector4(border, border, border, border));
        }
        public Sprite Icon(string name)
        {
            Sprite s; if (icons.TryGetValue(name, out s)) return s;
            if (iconTex != null && iconNames != null)
                for (int i = 0; i < Mathf.Min(iconTex.Length, iconNames.Length); i++)
                    if (iconNames[i] == name && iconTex[i] != null) { s = Spr(iconTex[i], 0f); icons[name] = s; return s; }
            return null;
        }

        public void Build()
        {
            if (built) return; built = true;
            if (cam == null) cam = Camera.main;
            sRound = Spr(texRound, 40f); sRoundS = Spr(texRoundS, 16f); sRing = Spr(texRing, 40f); sPill = Spr(texPill, 31f); sShadow = Spr(texShadow, 44f);
            var sh = Shader.Find("UI/Default");
            uiMat = new Material(sh) { name = "PNU HUD (위에 그림)" };
            uiMat.SetInt("unity_GUIZTestMode", (int)CompareFunction.Always);
            anchor = new GameObject("PNU 화면(월드 UI)").transform; anchor.SetParent(transform, false);
            var go = new GameObject("캔버스", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            go.transform.SetParent(anchor, false);
            canvas = go.GetComponent<Canvas>(); canvas.renderMode = RenderMode.WorldSpace; canvas.worldCamera = cam; canvas.sortingOrder = 60;
            go.GetComponent<CanvasScaler>().dynamicPixelsPerUnit = 3f;
            root = (RectTransform)go.transform; root.sizeDelta = new Vector2(1500, 820); root.localScale = Vector3.one * 0.001f;
            BuildMenu(); BuildTitle(); BuildBanner(); BuildWarn(); BuildPanel(); BuildDest(); BuildRoute(); BuildResult(); BuildHelp(); BuildSwitch(); BuildObjects(); BuildV2i(); BuildFade();
            HideAll();
        }

        RectTransform R(string name, Transform parent, float x, float y, float w, float h, float px = 0.5f, float py = 0.5f)
        {
            var g = new GameObject(name, typeof(RectTransform)); g.transform.SetParent(parent, false);
            var r = (RectTransform)g.transform; r.anchorMin = r.anchorMax = new Vector2(0.5f, 0.5f); r.pivot = new Vector2(px, py);
            r.anchoredPosition = new Vector2(x, y); r.sizeDelta = new Vector2(w, h); return r;
        }
        Image Img(RectTransform r, Sprite s, Color c, float ppuMul = 1f, bool sliced = true)
        {
            var im = r.gameObject.AddComponent<Image>(); im.sprite = s; im.color = c; im.material = uiMat; im.raycastTarget = false;
            if (sliced && s != null) { im.type = Image.Type.Sliced; im.pixelsPerUnitMultiplier = ppuMul; } else im.preserveAspect = true;
            return im;
        }
        Image Box(string name, Transform parent, float x, float y, float w, float h, Sprite s, Color c, float ppuMul = 1f) { var r = R(name, parent, x, y, w, h); return Img(r, s, c, ppuMul); }
        Image Ico(string name, Transform parent, string icon, float x, float y, float size, Color c) { var r = R(name, parent, x, y, size, size); return Img(r, Icon(icon), c, 1f, false); }
        Text Txt(string name, Transform parent, string s, float x, float y, float w, float h, int size, Color c, TextAnchor a = TextAnchor.MiddleLeft, bool bold = false)
        {
            var r = R(name, parent, x, y, w, h);
            var t = r.gameObject.AddComponent<Text>(); t.font = bold && fontBold != null ? fontBold : fontRegular; t.fontSize = size; t.color = c; t.alignment = a;
            t.text = s; t.material = uiMat; t.raycastTarget = false; t.horizontalOverflow = HorizontalWrapMode.Wrap; t.verticalOverflow = VerticalWrapMode.Overflow; t.lineSpacing = 1.05f;
            if (t.font == null) t.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            return t;
        }
        Button Btn(string name, Transform parent, string id, float x, float y, float w, float h, Color bg, Sprite s, float ppuMul = 1f)
        {
            var im = Box(name, parent, x, y, w, h, s, bg, ppuMul); im.raycastTarget = true;
            var b = im.gameObject.AddComponent<Button>(); b.targetGraphic = im;
            var cb = b.colors; cb.highlightedColor = new Color(0.92f, 0.95f, 1f); cb.pressedColor = new Color(0.75f, 0.8f, 0.9f); b.colors = cb;
            b.onClick.AddListener(() => { if (onButton != null) onButton(id); });
            return b;
        }
        void Shadow(Transform parent, float x, float y, float w, float h, float a = 0.35f) { var im = Box("그림자", parent, x, y - 8, w + 40, h + 40, sShadow, new Color(0, 0, 0, a), 1f); im.transform.SetAsFirstSibling(); }
        static CanvasGroup Group(RectTransform r) { var g = r.gameObject.AddComponent<CanvasGroup>(); g.alpha = 0f; return g; }
        void TextShadow(Text t, float a = 0.6f) { var s = t.gameObject.AddComponent<Shadow>(); s.effectColor = new Color(0, 0, 0, a); s.effectDistance = new Vector2(2f, -2f); }

        // ---- 메뉴바(아래): [학습 종료] [공간 나가기 | 조작안내 | 주행 시작 | 시점 변경] + 위 오른쪽 [고정해제] [학습 변경]
        void BuildMenu()
        {
            menu = R("메뉴바", root, 0, -330, 760, 120);
            var c = new Color(0.16f, 0.4f, 0.93f, 0.93f);
            MenuBtn(menu, "end", "학습 종료", "exit", -322, 0, 112, c);
            var bar = Box("메뉴 묶음", menu, 72, 0, 560, 112, sRound, c, 2.4f); Shadow(menu, 72, 0, 560, 112, 0.25f); bar.transform.SetAsLastSibling();
            string[] ids = { "space", "help", "drive", "view" }, lb = { "공간\n나가기", "조작안내", "주행 시작", "시점 변경" }, ic = { "space", "help", "drive", "view" };
            for (int i = 0; i < 4; i++)
            {
                var b = Btn("메뉴 " + lb[i], bar.transform, ids[i], -210 + i * 140, 0, 132, 104, new Color(1, 1, 1, 0.001f), sRound, 2.4f);
                var icn = Ico("아이콘", b.transform, ic[i], 0, 18, 40, Color.white);
                var t = Txt("글자", b.transform, lb[i], 0, -26, 130, 40, lb[i].Contains("\n") ? 17 : 19, Color.white, TextAnchor.MiddleCenter, true);
                if (ids[i] == "drive") { driveLabel = t; driveIcon = icn; }
                if (i > 0) Box("구분선", bar.transform, -280 + i * 140, 0, 2, 64, null, new Color(1, 1, 1, 0.35f));
            }
            toggles = R("고정·학습 변경", root, 150, -246, 330, 48);
            var tb = Btn("고정해제", toggles, "lock", -82, 0, 160, 46, new Color(0.08f, 0.12f, 0.22f, 0.78f), sPill, 1.4f);
            lockIcon = Ico("아이콘", tb.transform, "lock", -52, 0, 24, Color.white); lockLabel = Txt("글자", tb.transform, "고정해제", 14, 0, 100, 40, 19, Color.white, TextAnchor.MiddleCenter, true);
            var sb = Btn("학습 변경", toggles, "switch", 86, 0, 160, 46, new Color(0.16f, 0.4f, 0.93f, 0.95f), sPill, 1.4f);
            Ico("아이콘", sb.transform, "switch", 52, 0, 24, Color.white); Txt("글자", sb.transform, "학습 변경", -14, 0, 110, 40, 19, Color.white, TextAnchor.MiddleCenter, true);
        }
        void MenuBtn(Transform p, string id, string label, string icon, float x, float y, float w, Color c)
        {
            Shadow(p, x, y, w, 112, 0.25f);
            var b = Btn("메뉴 " + label, p, id, x, y, w, 112, c, sRound, 2.4f);
            Ico("아이콘", b.transform, icon, 0, 18, 40, Color.white); Txt("글자", b.transform, label, 0, -26, w, 40, 19, Color.white, TextAnchor.MiddleCenter, true);
        }

        // ---- 제목 카드(가운데 큰 글자, 선택 틀) / 안내 띠 / 경고
        void BuildTitle()
        {
            titleRt = R("제목", root, -40, 120, 760, 150); titleCg = Group(titleRt);
            Box("바탕", titleRt, 0, 0, 760, 150, sRoundS, new Color(0.04f, 0.08f, 0.18f, 0.28f), 2f);
            Box("틀", titleRt, 0, 0, 740, 132, null, new Color(1, 1, 1, 0f));
            // 틀: 얇은 선 + 모서리 손잡이
            float w = 700, h = 118;
            Box("위", titleRt, 0, h / 2, w, 2, null, new Color(1, 1, 1, 0.75f)); Box("아래", titleRt, 0, -h / 2, w, 2, null, new Color(1, 1, 1, 0.75f));
            Box("왼", titleRt, -w / 2, 0, 2, h, null, new Color(1, 1, 1, 0.75f)); Box("오", titleRt, w / 2, 0, 2, h, null, new Color(1, 1, 1, 0.75f));
            foreach (var p in new[] { new Vector2(-w / 2, h / 2), new Vector2(w / 2, h / 2), new Vector2(-w / 2, -h / 2), new Vector2(w / 2, -h / 2), new Vector2(0, h / 2), new Vector2(0, -h / 2), new Vector2(-w / 2, 0), new Vector2(w / 2, 0) })
                Box("손잡이", titleRt, p.x, p.y, 10, 10, null, Color.white);
            titleText = Txt("글자", titleRt, "", 0, 0, 720, 120, 64, Color.white, TextAnchor.MiddleCenter, true); TextShadow(titleText, 0.55f);
        }
        void BuildBanner()
        {
            bannerRt = R("안내 띠", root, -40, 120, 800, 128); bannerCg = Group(bannerRt);
            Box("바탕", bannerRt, 0, 0, 800, 128, sRound, new Color(0.16f, 0.17f, 0.2f, 0.82f), 1.6f);
            Ico("i", bannerRt, "info", -352, 0, 40, Color.white);
            bannerText = Txt("글자", bannerRt, "", 34, 0, 668, 120, 33, Color.white, TextAnchor.MiddleLeft, false);
        }
        void BuildWarn()
        {
            warnRt = R("경고", root, -120, 340, 330, 92); warnCg = Group(warnRt);
            Box("바탕", warnRt, 0, 0, 330, 92, sRoundS, new Color(0.12f, 0.05f, 0.06f, 0.82f), 1.6f);
            Box("테", warnRt, 0, 0, 330, 92, sRing, new Color(1f, 0.25f, 0.2f, 0.95f), 3f);
            Ico("!", warnRt, "warn", -122, 0, 52, new Color(1f, 0.32f, 0.26f));
            warnText1 = Txt("1", warnRt, "카메라 인식 주의", 30, 16, 250, 36, 25, new Color(1f, 0.45f, 0.4f), TextAnchor.MiddleLeft, true);
            warnText2 = Txt("2", warnRt, "시야 저하", 30, -18, 250, 32, 21, Color.white, TextAnchor.MiddleLeft, false);
        }

        // ---- 상황 체크리스트 판(오른쪽)
        void BuildPanel()
        {
            panelRt = R("상황 판", root, 500, 40, 420, 470); panelCg = Group(panelRt);
            Shadow(panelRt, 0, 0, 420, 470);
            panelBg = Box("바탕", panelRt, 0, 0, 420, 470, sRound, new Color(0.97f, 0.98f, 1f, 0.95f), 2f);
            panelHeadBg = Box("머리", panelRt, 0, 200, 420, 70, sRound, Blue, 2f);
            Box("머리 아래", panelRt, 0, 178, 420, 26, null, Blue).name = "머리 아래";
            panelHead = Txt("제목", panelRt, "", 0, 200, 380, 56, 26, Color.white, TextAnchor.MiddleCenter, true);
            panelPhase = Txt("판단", panelRt, "", -14, 132, 340, 44, 25, Ink, TextAnchor.MiddleLeft, true);
            phaseSpin = Ico("돌기", panelRt, "spin", 176, 132, 30, Blue);
            for (int i = 0; i < 4; i++)
            {
                float y = 62 - i * 68;
                // 아이콘·번호 칸(-199~-165) | 글자(-152부터) | 상태 알약(106~194)
                rowNum[i] = Box("번호" + i, panelRt, -182, y, 32, 32, sPill, BlueL, 1f); rowNumT[i] = Txt("n", rowNum[i].transform, (i + 1).ToString(), 0, 0, 32, 32, 18, Blue, TextAnchor.MiddleCenter, true);
                rowIcon[i] = Ico("아이콘" + i, panelRt, "signal", -182, y, 34, Blue);
                rowName[i] = Txt("이름" + i, panelRt, "", -24, y + 12, 256, 34, 22, Ink, TextAnchor.MiddleLeft, true);
                rowDetail[i] = Txt("내용" + i, panelRt, "", -7, y - 15, 290, 30, 17, Gray, TextAnchor.MiddleLeft, false);
                rowPillBg[i] = Box("상태" + i, panelRt, 150, y + 10, 88, 32, sPill, GrayL, 1f);
                rowSt[i] = Txt("상태글" + i, rowPillBg[i].transform, "", -6, 0, 70, 30, 16, Gray, TextAnchor.MiddleCenter, true);
                rowStIcon[i] = Ico("상태아이콘" + i, rowPillBg[i].transform, "check", 30, 0, 20, Gray);
                if (i < 3) Box("선" + i, panelRt, 0, y - 34, 370, 1.5f, null, new Color(0, 0, 0, 0.08f));
            }
            panelFootIcon = Ico("주의", panelRt, "warn", -176, -206, 26, Red);
            panelFoot = Txt("아래", panelRt, "", 20, -206, 340, 40, 17, Red, TextAnchor.MiddleLeft, false);
        }

        // ---- 목적지 설정(105~106)
        void BuildDest()
        {
            destRt = R("목적지 설정", root, -60, 70, 780, 470);
            Shadow(destRt, 0, 0, 780, 470);
            Box("바탕", destRt, 0, 0, 780, 470, sRound, new Color(0.97f, 0.98f, 1f, 0.96f), 2f);
            Txt("제목", destRt, "목적지 설정", 0, 196, 700, 50, 30, Ink, TextAnchor.MiddleCenter, true);
            Box("선", destRt, 0, 168, 700, 2, null, new Color(0, 0, 0, 0.08f));
            var card = Box("카드", destRt, 0, 10, 700, 270, sRound, Color.white, 2.4f);
            Box("카드 테", destRt, 0, 10, 700, 270, sRing, new Color(0.17f, 0.42f, 0.95f, 0.65f), 2.4f);
            Ico("핀", card.transform, "pin", -300, 82, 56, Blue);
            destTitle = Txt("이름", card.transform, "부경대학교", -52, 92, 420, 50, 34, Ink, TextAnchor.MiddleLeft, true);
            destInfo = Txt("부", card.transform, "", -2, 52, 520, 34, 21, Gray, TextAnchor.MiddleLeft, false);
            Box("작은 선", card.transform, 0, 20, 640, 1.5f, null, new Color(0, 0, 0, 0.08f));
            Ico("경로", card.transform, "route", -300, -24, 38, Blue);
            destInfo2 = Txt("경로", card.transform, "", 18, -36, 560, 80, 21, Ink, TextAnchor.MiddleLeft, false);
            var go = Btn("주행하러 가기", card.transform, "dest_go", 210, -96, 230, 56, Blue, sPill, 1.2f);
            Txt("글자", go.transform, "주행하러 가기", 0, 0, 220, 50, 22, Color.white, TextAnchor.MiddleCenter, true);
            Txt("안내", destRt, "지도에서 목적지를 선택하면 권장 경로와 예상 주행 정보가 표시됩니다.", 0, -186, 720, 40, 19, Gray, TextAnchor.MiddleCenter, false);
        }

        // ---- 경로 카드(107)
        void BuildRoute()
        {
            routeRt = R("경로 카드", root, 330, 120, 560, 250);
            Shadow(routeRt, 0, 0, 560, 250);
            Box("바탕", routeRt, 0, 0, 560, 250, sRound, new Color(0.98f, 0.99f, 1f, 0.97f), 2f);
            Ico("회전", routeRt, "route", -226, 74, 48, Ink);
            Txt("작은", routeRt, "목적지", -88, 96, 200, 28, 17, Gray, TextAnchor.MiddleLeft, false);
            routeTitle = Txt("이름", routeRt, "부경대학교", -38, 66, 300, 44, 32, Ink, TextAnchor.MiddleLeft, true);
            var tag = Box("자율주행 모드", routeRt, 190, 74, 150, 38, sPill, Blue, 1.2f); Txt("글자", tag.transform, "자율주행 모드", 0, 0, 150, 34, 17, Color.white, TextAnchor.MiddleCenter, true);
            Txt("도착 예정", routeRt, "도착 예정 시간", -150, 6, 200, 30, 19, Gray, TextAnchor.MiddleLeft, false);
            routeEta = Txt("시간", routeRt, "", -150, -24, 200, 36, 26, Ink, TextAnchor.MiddleLeft, true);
            Txt("예상 거리", routeRt, "예상 주행 거리", 80, 6, 200, 30, 19, Gray, TextAnchor.MiddleLeft, false);
            routeKm = Txt("거리", routeRt, "", 80, -24, 200, 36, 26, Ink, TextAnchor.MiddleLeft, true);
            var b1 = Btn("주행 시작", routeRt, "route_go", -86, -86, 240, 54, Blue, sPill, 1.2f); Txt("글자", b1.transform, "주행 시작", 0, 0, 230, 50, 22, Color.white, TextAnchor.MiddleCenter, true);
            var b2 = Btn("다시 선택", routeRt, "route_back", 150, -86, 190, 54, GrayL, sPill, 1.2f); Txt("글자", b2.transform, "다시 선택", 0, 0, 180, 50, 21, Ink, TextAnchor.MiddleCenter, true);
        }

        // ---- 결과판(117)
        void BuildResult()
        {
            resultRt = R("결과판", root, -40, 70, 980, 560);
            Shadow(resultRt, 0, 0, 980, 560);
            Box("바탕", resultRt, 0, 0, 980, 560, sRound, new Color(0.97f, 0.98f, 1f, 0.97f), 2f);
            Box("머리", resultRt, 0, 245, 980, 70, sRound, Blue, 2f); Box("머리 아래", resultRt, 0, 222, 980, 24, null, Blue);
            resTitle = Txt("제목", resultRt, "주행 결과", 0, 245, 900, 56, 28, Color.white, TextAnchor.MiddleCenter, true);
            string[] k = { "주행 시간", "주행 거리", "평균 속도", "정지 횟수" };
            for (int i = 0; i < 4; i++)
            {
                Box("칸" + i, resultRt, -360 + (i % 2) * 190, 150 - (i / 2) * 92, 176, 80, sRoundS, BlueL, 1.4f);
                Txt("이름" + i, resultRt, k[i], -360 + (i % 2) * 190, 172 - (i / 2) * 92, 170, 26, 16, Gray, TextAnchor.MiddleCenter, false);
            }
            resTime = Txt("시간", resultRt, "", -360, 138, 170, 40, 28, Ink, TextAnchor.MiddleCenter, true);
            resDist = Txt("거리", resultRt, "", -170, 138, 170, 40, 28, Ink, TextAnchor.MiddleCenter, true);
            resAvg = Txt("평균", resultRt, "", -360, 46, 170, 40, 28, Ink, TextAnchor.MiddleCenter, true);
            resStops = Txt("정지", resultRt, "", -170, 46, 170, 40, 28, Ink, TextAnchor.MiddleCenter, true);
            Txt("위험 대응", resultRt, "위험 상황 대응", 220, 186, 440, 32, 21, Blue, TextAnchor.MiddleLeft, true);
            string[] ic = { "signal", "lane", "ped", "rain" };
            for (int i = 0; i < 4; i++)
            {
                float y = 140 - i * 62;
                resEvI[i] = Ico("아이콘" + i, resultRt, ic[i], 26, y, 32, Blue);
                resEv[i] = Txt("이름" + i, resultRt, "", 262, y + 12, 440, 28, 19, Ink, TextAnchor.MiddleLeft, true);
                resEvR[i] = Txt("결과" + i, resultRt, "", 262, y - 14, 440, 28, 15, Gray, TextAnchor.MiddleLeft, false);
            }
            Box("선", resultRt, 0, -110, 900, 1.5f, null, new Color(0, 0, 0, 0.1f));
            Txt("안전 정지", resultRt, "안전 정지 기록", -250, -134, 400, 30, 20, Blue, TextAnchor.MiddleLeft, true);
            resLog = Txt("기록", resultRt, "", 0, -196, 900, 110, 16, Ink, TextAnchor.UpperLeft, false);
            var b = Btn("학습 종료", resultRt, "result_end", 330, -240, 220, 54, Blue, sPill, 1.2f); Txt("글자", b.transform, "학습 종료", 0, 0, 210, 50, 22, Color.white, TextAnchor.MiddleCenter, true);
        }

        // ---- 조작안내 / 학습 변경
        void BuildHelp()
        {
            helpRt = R("조작안내", root, -330, 70, 600, 470);
            Shadow(helpRt, 0, 0, 600, 470);
            Box("바탕", helpRt, 0, 0, 600, 470, sRound, new Color(0.97f, 0.98f, 1f, 0.96f), 2f);
            Txt("제목", helpRt, "조작 안내", 0, 200, 540, 44, 26, Ink, TextAnchor.MiddleCenter, true);
            Txt("내용", helpRt,
                "· 아래 메뉴: 주행 시작/일시 정지 · 시점 변경(운전석↔따라가기)\n" +
                "· 학습 변경: 상황(교차로·차선 변경·사각지대·악천후·도착)으로 이동\n" +
                "· 고정해제: 화면이 고개 방향을 따라옴 / 고정: 차에 붙음\n\n" +
                "PC 조작\n" +
                "· 마우스 왼쪽 = 누르기, 오른쪽 드래그 = 둘러보기(R 정면)\n" +
                "· Space 주행 시작/일시 정지 · C 시점 · M 소리\n" +
                "· 1~5 상황으로 이동 · ←/→ 150m · Home 처음으로\n" +
                "· H 개발용 표시(배속·구간 슬라이더)",
                0, -16, 540, 380, 18, Ink, TextAnchor.UpperLeft, false);
            var b = Btn("닫기", helpRt, "help", 0, -206, 160, 46, GrayL, sPill, 1.2f); Txt("글자", b.transform, "닫기", 0, 0, 150, 40, 19, Ink, TextAnchor.MiddleCenter, true);
        }
        void BuildSwitch()
        {
            switchRt = R("학습 변경", root, 420, 40, 380, 470);
            Shadow(switchRt, 0, 0, 380, 470);
            Box("바탕", switchRt, 0, 0, 380, 470, sRound, new Color(0.97f, 0.98f, 1f, 0.96f), 2f);
            Txt("제목", switchRt, "학습 변경", 0, 200, 340, 40, 24, Ink, TextAnchor.MiddleCenter, true);
            string[] ids = { "go_junction", "go_lane", "go_ped", "go_rain", "go_arrive", "go_start" };
            string[] lb = { "교차로 통과", "차선 변경 보류", "사각지대 보행자", "악천후 주행", "목적지 도착", "처음부터" };
            string[] ic = { "signal", "lane", "ped", "rain", "flag", "switch" };
            for (int i = 0; i < 6; i++)
            {
                var b = Btn(lb[i], switchRt, ids[i], 0, 138 - i * 58, 330, 50, i == 5 ? GrayL : BlueL, sPill, 1.2f);
                Ico("아이콘", b.transform, ic[i], -132, 0, 26, i == 5 ? Ink : Blue); Txt("글자", b.transform, lb[i], 16, 0, 260, 44, 20, Ink, TextAnchor.MiddleLeft, true);
            }
            var c = Btn("닫기", switchRt, "switch", 0, -212, 140, 40, GrayL, sPill, 1.2f); Txt("글자", c.transform, "닫기", 0, 0, 130, 36, 18, Ink, TextAnchor.MiddleCenter, true);
        }

        // ---- 주변 물체(오른쪽 위)
        void BuildObjects()
        {
            objRt = R("주변 물체", root, 560, 330, 330, 66);
            Box("바탕", objRt, 0, 0, 330, 66, sRoundS, new Color(0.05f, 0.1f, 0.22f, 0.7f), 1.6f);
            for (int i = 0; i < 3; i++)
            {
                var r = R("물체" + i, objRt, -110 + i * 110, 0, 104, 60); objRow[i] = r.gameObject;
                objIcon[i] = Ico("아이콘", r, "ped", -30, 0, 28, Color.white);
                objText[i] = Txt("글자", r, "", 16, 0, 74, 56, 15, Color.white, TextAnchor.MiddleLeft, true);
            }
        }

        // ---- v17 V2I 신호 정보(왼쪽 위, 주변 물체 판과 마주 보게): 앞 신호 교차로의 신호·남은 초·거리. 주행 내내 보임
        void BuildV2i()
        {
            v2iRt = R("신호 정보 V2I", root, -560, 330, 330, 66);
            Box("바탕", v2iRt, 0, 0, 330, 66, sRoundS, new Color(0.05f, 0.1f, 0.22f, 0.7f), 1.6f);
            v2iIcon = Ico("신호 아이콘", v2iRt, "signal", -134, 0, 36, Color.white);
            v2iHead = Txt("신호", v2iRt, "", 26, 11, 262, 30, 19, Color.white, TextAnchor.MiddleLeft, true); v2iHead.horizontalOverflow = HorizontalWrapMode.Overflow;
            v2iDetail = Txt("거리", v2iRt, "V2I 신호 정보", 26, -15, 262, 24, 14, new Color(0.72f, 0.8f, 0.92f), TextAnchor.MiddleLeft, false);
            v2iRt.gameObject.SetActive(false);
        }

        // ---- 페이드: 카메라 0.35m 앞 검은 판
        void BuildFade()
        {
            var go = new GameObject("화면 전환(페이드)", typeof(RectTransform), typeof(Canvas));
            go.transform.SetParent(transform, false);
            fadeCanvas = go.GetComponent<Canvas>(); fadeCanvas.renderMode = RenderMode.WorldSpace; fadeCanvas.worldCamera = cam; fadeCanvas.sortingOrder = 100;
            var r = (RectTransform)go.transform; r.sizeDelta = new Vector2(400, 400); r.localScale = Vector3.one * 0.01f;
            var im = R("검정", r, 0, 0, 400, 400); fadeImg = Img(im, null, Color.black, 1f, false); fadeImg.color = new Color(0, 0, 0, 0);
            go.SetActive(false);
        }

        void HideAll()
        {
            foreach (var r in new[] { destRt, routeRt, resultRt, helpRt, switchRt, objRt, v2iRt }) if (r != null) r.gameObject.SetActive(false);
            if (titleCg != null) titleCg.alpha = 0f; if (bannerCg != null) bannerCg.alpha = 0f; if (warnCg != null) warnCg.alpha = 0f; if (panelCg != null) panelCg.alpha = 0f;
        }

        // ------------------------------------------------------------------ 밖에서 부름
        public void ShowMenu(bool on) { if (menu != null) { menu.gameObject.SetActive(on); toggles.gameObject.SetActive(on); } }
        public void SetDrive(bool playing, bool started)
        {
            if (driveLabel == null) return;
            driveLabel.text = !started ? "주행 시작" : (playing ? "일시 정지" : "주행 재개");
            driveIcon.sprite = Icon(!started ? "drive" : (playing ? "pause" : "play"));
        }
        public void SetLocked(bool l) { locked = l; if (lockLabel != null) { lockLabel.text = l ? "고정해제" : "고정"; lockIcon.sprite = Icon(l ? "lock" : "unlock"); } }
        public void ShowTitle(string s, float secs) { if (titleText == null) return; titleText.text = s; titleUntil = Time.unscaledTime + secs; }
        public void ShowBanner(string s, float secs) { if (bannerText == null) return; bannerText.text = s; bannerUntil = Time.unscaledTime + secs; }
        public void HideBanner() { bannerUntil = 0f; }
        public void ShowWarn(bool on, string l1 = null, string l2 = null) { warnOn = on; if (l1 != null) warnText1.text = l1; if (l2 != null) warnText2.text = l2; }
        public void ShowDest(bool on, ScDest d, float minutes)
        {
            destRt.gameObject.SetActive(on); if (!on || d == null) return;
            destTitle.text = d.name; destInfo.text = d.sub + " · 출발 " + d.origin;
            destInfo2.text = "권장 경로: " + d.origin + " → " + d.via + " → " + d.name + "\n주행 거리 " + d.km.ToString("0.0") + "km · 예상 시간 약 " + Mathf.RoundToInt(minutes) + "분";
        }
        public void ShowRoute(bool on, ScDest d, float minutes)
        {
            routeRt.gameObject.SetActive(on); if (!on || d == null) return;
            routeTitle.text = d.name;
            var t = DateTime.Now.AddMinutes(minutes); routeEta.text = t.ToString("HH:mm");
            routeKm.text = d.km.ToString("0.0") + "km";
        }
        public void ShowHelp(bool on) { helpRt.gameObject.SetActive(on); }
        public bool HelpOn { get { return helpRt != null && helpRt.gameObject.activeSelf; } }
        public void ShowSwitch(bool on) { switchRt.gameObject.SetActive(on); }
        public bool SwitchOn { get { return switchRt != null && switchRt.gameObject.activeSelf; } }
        public void ShowObjects(bool on) { if (objRt != null) objRt.gameObject.SetActive(on); }
        public bool ResultOn { get { return resultRt != null && resultRt.gameObject.activeSelf; } }
        public void ShowV2i(bool on) { if (v2iRt != null && v2iRt.gameObject.activeSelf != on) v2iRt.gameObject.SetActive(on); }
        /// <summary>V2I 판: head = 신호·남은 초(큰 글자), detail = "V2I 신호 정보 · 거리", c = 신호 색(아이콘)</summary>
        public void SetV2i(string head, string detail, Color c)
        {
            if (v2iRt == null) return;
            if (v2iHead.text != head) v2iHead.text = head;
            if (v2iDetail.text != detail) v2iDetail.text = detail;
            v2iIcon.color = c;
        }
        public void SetObject(int i, string icon, string text, Color c)
        {
            if (i < 0 || i >= 3) return;
            bool on = !string.IsNullOrEmpty(text); objRow[i].SetActive(on); if (!on) return;
            objIcon[i].sprite = Icon(icon); objIcon[i].color = c; objText[i].text = text;
        }

        public void ShowResult(bool on, string title = null, string time = null, string dist = null, string avg = null, string stops = null, string[] evName = null, string[] evRes = null, int[] evSt = null, string log = null)
        {
            resultRt.gameObject.SetActive(on); if (!on) return;
            resTitle.text = title; resTime.text = time; resDist.text = dist; resAvg.text = avg; resStops.text = stops; resLog.text = log;
            for (int i = 0; i < 4; i++)
            {
                resEv[i].text = evName != null && i < evName.Length ? evName[i] : "";
                resEvR[i].text = evRes != null && i < evRes.Length ? evRes[i] : "";
                int st = evSt != null && i < evSt.Length ? evSt[i] : 0;
                resEvI[i].color = st == 2 ? Green : st == 3 ? Gray : Blue;
            }
        }

        /// <summary>상황 판: 사건 e 의 단계·문장. dark = 악천후 판(남색)</summary>
        public void ShowPanel(bool on, ScEvent e = null, bool dark = false, string[] iconsForRows = null, string foot = null)
        {
            panelOn = on; if (!on || e == null) return;
            if (panelKey != e.id) { panelKey = e.id; panelA = 0f; }
            panelBg.color = dark ? new Color(0.04f, 0.09f, 0.2f, 0.9f) : new Color(0.97f, 0.98f, 1f, 0.95f);
            panelHeadBg.color = dark ? new Color(0.1f, 0.3f, 0.65f, 1f) : Blue;
            var hb = panelRt.Find("머리 아래"); if (hb != null) hb.GetComponent<Image>().color = panelHeadBg.color;
            panelHead.text = e.panel;
            panelPhase.text = string.IsNullOrEmpty(e.phase) ? " " : e.phase; panelPhase.color = dark ? Color.white : Ink;
            bool busy = e.phase.EndsWith("중"); phaseSpin.enabled = busy;
            bool numbered = e.id == "blindped" || e.id == "rain";
            for (int i = 0; i < 4; i++)
            {
                var st = i < e.steps.Length ? e.steps[i] : null;
                bool has = st != null;
                rowName[i].text = has ? st.name : ""; rowDetail[i].text = has ? st.detail : "";
                rowName[i].color = dark ? Color.white : Ink; rowDetail[i].color = dark ? new Color(0.72f, 0.8f, 0.92f) : Gray;
                rowNum[i].enabled = numbered && has; rowNumT[i].enabled = numbered && has;
                rowIcon[i].enabled = !numbered && has && iconsForRows != null && i < iconsForRows.Length;
                if (rowIcon[i].enabled) { rowIcon[i].sprite = Icon(iconsForRows[i]); rowIcon[i].color = dark ? new Color(0.5f, 0.8f, 1f) : Blue; }
                int s = has ? st.st : 0;
                rowPillBg[i].enabled = has; rowSt[i].enabled = has; rowStIcon[i].enabled = has;
                if (!has) continue;
                Color pc, tc; string label, ic;
                if (s == 2) { pc = new Color(0.86f, 0.96f, 0.89f); tc = Green; label = "완료"; ic = "check"; }
                else if (s == 1) { pc = BlueL; tc = Blue; label = "진행 중"; ic = "spin"; }
                else if (s == 3) { pc = new Color(1f, 0.89f, 0.88f); tc = Red; label = "주의"; ic = "warn"; }
                else { pc = dark ? new Color(1, 1, 1, 0.12f) : GrayL; tc = dark ? new Color(0.75f, 0.8f, 0.9f) : Gray; label = "대기 중"; ic = "wait"; }
                if (dark && s != 0)
                {
                    // 남색 판: 옅은 색 바탕 + 밝은 글자(대비)
                    if (s == 2) { pc = new Color(0.2f, 0.62f, 0.38f, 0.34f); tc = new Color(0.55f, 0.92f, 0.66f); }
                    else if (s == 1) { pc = new Color(0.3f, 0.55f, 1f, 0.34f); tc = new Color(0.68f, 0.84f, 1f); }
                    else { pc = new Color(1f, 0.3f, 0.25f, 0.34f); tc = new Color(1f, 0.64f, 0.6f); }
                }
                rowPillBg[i].color = pc; rowSt[i].text = label; rowSt[i].color = tc; rowStIcon[i].sprite = Icon(ic); rowStIcon[i].color = tc;
                if (s == 1) rowStIcon[i].rectTransform.localRotation = Quaternion.Euler(0, 0, -Time.unscaledTime * 300f);
                else rowStIcon[i].rectTransform.localRotation = Quaternion.identity;
            }
            if (busy) phaseSpin.rectTransform.localRotation = Quaternion.Euler(0, 0, -Time.unscaledTime * 300f);
            panelFoot.text = foot ?? ""; panelFootIcon.enabled = !string.IsNullOrEmpty(foot);
        }

        /// <summary>화면 전환: 0 투명 → 1 검정</summary>
        public void Fade(float target, float secs) { fadeTarget = Mathf.Clamp01(target); fadeSpeed = secs > 0.01f ? 1f / secs : 1000f; }
        public void FadeNow(float a) { fadeA = fadeTarget = Mathf.Clamp01(a); }
        public bool Fading { get { return Mathf.Abs(fadeA - fadeTarget) > 0.01f; } }

        // ------------------------------------------------------------------ 자리·나타나기
        void LateUpdate()
        {
            if (!built || cam == null) return;
            float dt = Time.unscaledDeltaTime;
            PlaceAnchor(dt);
            titleA = Mathf.MoveTowards(titleA, Time.unscaledTime < titleUntil ? 1f : 0f, dt * 3f); titleCg.alpha = titleA;
            bannerA = Mathf.MoveTowards(bannerA, Time.unscaledTime < bannerUntil && Time.unscaledTime >= titleUntil - 0.05f ? 1f : 0f, dt * 3f); bannerCg.alpha = bannerA;
            // 상황 판(오른쪽, x 290~710)이 열려 있으면 제목·안내 띠를 왼쪽으로(안 보일 때는 바로, 보일 때는 미끄러지듯)
            float tx = panelOn ? -110f : -40f, bx = panelOn ? -130f : -40f;
            titleX = titleA < 0.01f ? tx : Mathf.MoveTowards(titleX, tx, dt * 300f); titleRt.anchoredPosition = new Vector2(titleX, 120f);
            bannerX = bannerA < 0.01f ? bx : Mathf.MoveTowards(bannerX, bx, dt * 300f); bannerRt.anchoredPosition = new Vector2(bannerX, 120f);
            warnA = Mathf.MoveTowards(warnA, warnOn ? 1f : 0f, dt * 3f); warnCg.alpha = warnA * (0.78f + 0.22f * Mathf.Abs(Mathf.Sin(Time.unscaledTime * 2.4f)));
            panelA = Mathf.MoveTowards(panelA, panelOn ? 1f : 0f, dt * 3.5f); panelCg.alpha = panelA;
            panelRt.anchoredPosition = new Vector2(500 + (1f - panelA) * 40f, 40);
            // 페이드
            fadeA = Mathf.MoveTowards(fadeA, fadeTarget, dt * fadeSpeed);
            bool f = fadeA > 0.002f; if (fadeCanvas.gameObject.activeSelf != f) fadeCanvas.gameObject.SetActive(f);
            if (f)
            {
                fadeImg.color = new Color(0, 0, 0, fadeA);
                fadeCanvas.transform.SetPositionAndRotation(cam.transform.position + cam.transform.forward * Mathf.Max(0.2f, cam.nearClipPlane + 0.1f), cam.transform.rotation);
            }
        }

        void PlaceAnchor(float dt)
        {
            var ct = cam.transform;
            bool driver = ego != null && ego.car != null && ego.camMode == PNUDriveEgo.CamMode.Driver;
            Vector3 pos; Quaternion rot;
            if (driver && locked)
            {
                // 차 기준: 운전자 눈(둘러보기와 상관없는 자리) 앞 · 10도 아래
                var car = ego.car; Vector3 eye = car.InverseTransformPoint(ct.position);
                eye.x = Mathf.Lerp(eye.x, -0.4f, 0.5f);
                Quaternion q = Quaternion.Euler(downAngle, 0f, 0f);
                pos = car.TransformPoint(eye + q * (Vector3.forward * distance)); rot = car.rotation * q;
                followYaw = ct.eulerAngles.y;
            }
            else
            {
                // 고개(카메라) 방향을 천천히 따라옴
                float yaw = ct.eulerAngles.y;
                followYaw = Mathf.LerpAngle(followYaw, yaw, 1f - Mathf.Exp(-dt * (driver ? 2.5f : 8f)));
                Quaternion q = Quaternion.Euler(downAngle + (driver ? 0f : ct.eulerAngles.x), followYaw, 0f);
                pos = ct.position + q * (Vector3.forward * distance); rot = q;
            }
            anchor.SetPositionAndRotation(pos, rot);
        }
    }
}
