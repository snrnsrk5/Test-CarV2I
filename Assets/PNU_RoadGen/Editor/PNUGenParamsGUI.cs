// PNU 도로 생성기 — 생성 규칙(GenParams) 인스펙터: 칸 이름을 한글로, 묶음 제목, 번호로 고르는 칸은 목록으로
// (GenParams 는 유니티를 모르는 순수 C# 이라 여기서 이름표를 붙임. 칸이 새로 생기면 영어 이름 그대로 '기타'에 나옴)
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

public static class PNUGenParamsGUI
{
    class L
    {
        public string g, n, t; public string[] opt;
        public L(string g, string n, string t = "", string[] opt = null) { this.g = g; this.n = n; this.t = t; this.opt = opt; }
    }

    static readonly string[] EdgeOpt = { "흰 실선 (505 길가장자리구역선)", "황색 점선 (515 주차금지)", "황색 실선 (516 정차·주차금지)", "황색 복선 (516의2 절대 정차·주차금지)", "없음" };
    static readonly string[] SignalOpt = { "자동 (제1주 + 편도 3차로 이상 제2주)", "제1주만 (교차로 건너기 전)", "제1주 + 제2주 (건너편)", "정지선 부근 + 제2주", "건너편만 (예전 방식)" };
    static readonly string[] PedSideOpt = { "횡단보도 옆 교차로 쪽", "횡단보도 옆 바깥쪽" };
    static readonly string[] TopMarkOpt = { "짧은 가로획 (띄움)", "짧은 세로획 (띄움)", "붙은 세로획 (원래 모양)" };

    const string G0 = "도로망 만들기", G1 = "차로·단면", G2 = "인도·연석", G3 = "교차로", G4 = "횡단보도", G5 = "정지선", G6 = "높이", G7 = "지형 맞춤",
                 G8 = "선 폭·종류 (업무편람 표 2-1)", G9 = "진행방향 화살표 (537~539)", G10 = "좌회전 유도선 (525)", G11 = "노면표시 전체", G12 = "신호등 (신호기 업무편람)",
                 G13 = "노면 글자 (편람 2장 3절, 사용자 글꼴)", G14 = "땅 밑 구간 (지하차도·터널)", G15 = "교량·고가 상판·교각", G16 = "광안대교·복층 다리·바다 위 다리", G17 = "층 나누기·합류점",
                 G18 = "차로 연결·교차로 면 한 장·도로 겹침";

    static readonly Dictionary<string, L> Map = new Dictionary<string, L>
    {
        { "step", new L(G0, "중심선 점 간격 (m)") },
        { "smoothSigmaM", new L(G0, "중심선 다듬기 (m)") },
        { "junctionLinkMax", new L(G0, "교차로로 묶을 짧은 링크 (m)", "이보다 짧은 교차로-교차로 링크는 한 교차로로") },
        { "junctionLinkMaxSameName", new L(G0, "같은 이름 교차로 묶음 링크 (m)") },
        { "mergeThroughStructures", new L(G0, "교차로를 지나가는 지하차도·고가는 한 도로로", "교차로 노드 묶음에 걸린 마주 보는 같은 구조 팔 둘을 이어 교차로에서 뺌(교차로 위·밑을 그냥 지나감)") },
        { "pairByStructure", new L(G0, "상·하행 짝은 구조가 같을 때만", "땅 / 다리·고가 / 지하·터널이 다르면 같은 자리라도 다른 도로(다리 위 길과 밑 길)") },
        { "pairMeanMax", new L(G0, "상·하행 짝 평균 거리 (m)") },
        { "pairMaxMax", new L(G0, "상·하행 짝 최대 거리 (m)") },
        { "junctionNodeNear", new L(G0, "한 교차로로 볼 노드 거리 (m)") },
        { "junctionSameNameDist", new L(G0, "같은 이름 교차로 노드 거리 (m)") },
        { "junctionMergeDist", new L(G0, "상·하행 교차로 합치는 거리 (m)") },
        { "junctionMaxRadius", new L(G0, "교차로 노드 묶음 반경 상한 (m)") },
        { "splitNear", new L(G0, "사슬 자르는 거리 (m)") },

        { "laneW80", new L(G1, "차로 폭: 설계속도 80 이상 (m)") },
        { "laneW60", new L(G1, "차로 폭: 60~80 (m)") },
        { "laneWLow", new L(G1, "차로 폭: 60 미만 (m)") },
        { "laneWExpress", new L(G1, "차로 폭: 고속·전용 (m)") },
        { "centerHalfGap", new L(G1, "중앙선 자리 반폭 (m)") },
        { "medianHalf", new L(G1, "중앙분리대 반폭 (m)") },
        { "shoulder80", new L(G1, "길어깨: 80 이상 (m)", "도로의 구조·시설 기준에 관한 규칙 제12조, 도시지역 일반도로") },
        { "shoulder60", new L(G1, "길어깨: 60~80 (m)") },
        { "shoulderLow", new L(G1, "길어깨: 60 미만 (m)") },
        { "shoulderExpress", new L(G1, "길어깨: 고속·전용 (m)") },
        { "gutterWidth", new L(G1, "측구(물 빠지는 곳) 폭 (m)") },
        { "taperUrban", new L(G1, "차로 늘고 줄 때 폭 1m당 길이: 도시 (m)") },
        { "taperExpress", new L(G1, "차로 늘고 줄 때 폭 1m당 길이: 고속 (m)") },

        { "sidewalkArterial", new L(G2, "인도 폭: 간선 (m)") },
        { "sidewalkLocal", new L(G2, "인도 폭: 지방도 (m)") },
        { "sidewalkMin", new L(G2, "인도 폭: 그 밖 (m)") },
        { "curbHeight", new L(G2, "연석 높이 (m)") },
        { "curbWidth", new L(G2, "연석 폭 (m)") },

        { "cornerRadiusArterial", new L(G3, "모서리 반경: 간선 (m)") },
        { "cornerRadiusLocal", new L(G3, "모서리 반경: 이면 (m)") },
        { "mouthMin", new L(G3, "중심 → 입구 최소 (m)") },
        { "shortRoadKeep", new L(G3, "짧은 도로 최소 남김 (m)", "양 끝 교차로 입구가 서로 넘어가면 입구를 줄여서라도 이만큼은 도로로 남김") },
        { "cornerToMouth", new L(G3, "가각부 → 횡단보도 (m)", "업무편람 532: 3m 이상") },
        { "maxCut", new L(G3, "입구 거리 상한 (m)") },
        { "sideBySideDeg", new L(G3, "나란히 붙은 팔로 볼 각 (°)") },
        { "filletMaxDeg", new L(G3, "곡선 대신 완만히 잇는 각 (°)") },

        { "crosswalkWidth", new L(G4, "폭 (m)", "4m 이상, 넘으면 2m 단위") },
        { "crosswalkStripe", new L(G4, "줄 폭 (m)", "45~50cm") },
        { "crosswalkGap", new L(G4, "줄 간격 (m)", "줄 폭의 1.5배") },
        { "crosswalkStripeMin", new L(G4, "줄 폭 하한 (m)", "끝선까지 채울 때 줄·간격을 조금씩 맞추는 아래 한도") },
        { "crosswalkCenterGap", new L(G4, "중앙선 위 빈칸 (m)", "90~100cm") },
        { "crosswalkWidthSplit", new L(G4, "2단 횡단보도 폭 (m)", "6m 이상은 2등분, 두 줄 엇갈림") },
        { "crosswalkSplitGap", new L(G4, "2단 사이 띄움 (m)", "0 = 붙임, 45~50cm 가능") },
        { "crosswalkArrowLen", new L(G4, "보행 화살표 길이 (m)") },
        { "crosswalkArrowW", new L(G4, "보행 화살표 폭 (m)") },
        { "crosswalkArrowHead", new L(G4, "보행 화살표 머리 비율") },
        { "crosswalkArrows", new L(G4, "보행 방향 화살표 (2단, 출발 쪽 2개)") },
        { "centerLineThroughCrosswalk", new L(G4, "중앙선을 횡단보도 틈으로 이어 그림") },
        { "splitCrosswalkMinLanes", new L(G4, "자동 2단: 편도 이 차로 수 이상") },

        { "stopLineGap", new L(G5, "횡단보도 앞 거리 (m)", "업무편람 530: 3m 이상(2~10m)") },
        { "stopLineWidth", new L(G5, "정지선 폭 (m)", "도심 30~45cm") },
        { "solidBeforeStop", new L(G5, "정지선 앞 실선 길이 (m)", "진로변경제한선 506, 15m 이상") },
        { "tunnelSolidLines", new L(G5, "터널·지하차도 안 차선 실선", "업무편람 4.차선(503) 해설: 터널 내 진로변경 제한 차선") },
        { "tunnelSolidExtend", new L(G5, "터널 앞뒤 실선 연장 (m)", "입구 앞·출구 뒤로 더 긋는 길이") },

        { "heightSmoothM", new L(G6, "지형 높이 다듬기 (m)") },
        { "junctionBlend", new L(G6, "교차로 평면 → 도로 높이 (m)") },
        { "maxJunctionGrade", new L(G6, "교차로 최대 기울기") },
        { "overpassClear", new L(G6, "고가차도 높이 (m)") },
        { "separateLevels", new L(G6, "겹친 다리·고가 ↔ 땅 ↔ 지하 층 나누기", "같은 자리를 지나는 구조가 다른 길을 위아래로 떼어 놓음(앞뒤 경사로)") },
        { "liftExpressways", new L(G6, "도시고속이 일반 길과 겹치면 고가로", "노드링크에 고가 표시가 빠진 곳(광안대로·번영로 등)") },
        { "levelClear", new L(G6, "층 사이 도로면 높이 차 (m)", "높이 제한 4.5 + 상판 두께") },
        { "underpassDepth", new L(G6, "지하차도 깊이 (m)") },
        { "rampGrade", new L(G6, "경사로 기울기") },
        { "skirtDepth", new L(G6, "인도 바깥 치마 깊이 (m)") },

        { "terrainStep", new L(G7, "격자 간격 (m)") },
        { "terrainBlend", new L(G7, "원래 지형으로 넘어가는 거리 (m)") },
        { "terrainClear", new L(G7, "도로 면보다 낮게 (m)") },
        { "terrainTile", new L(G7, "메쉬 조각 크기 (m)") },
        { "terrainCorridor", new L(G7, "도로 둘레만 지형 (m)", "도로·교차로에서 이 거리 안 조각만 만듦(0 = 범위 전체). 경로 전체를 볼 때 가볍게") },
        { "terrainStepLarge", new L(G7, "넓은 범위 지형 격자 (m)") },
        { "terrainLargeArea", new L(G7, "넓은 범위 기준 (km²)") },
        { "bridgeClear", new L(G7, "교량·고가 밑 여유 (m)") },
        { "bridgeBlend", new L(G7, "교량 밑 비탈 거리 (m)") },

        { "lineCenterW", new L(G8, "중앙선 실선 폭 (m)", "15~20cm") },
        { "lineCenterDoubleW", new L(G8, "중앙선 복선 한 줄 폭 (m)", "10~15cm") },
        { "doubleGap", new L(G8, "중앙선 복선 간격 (m)", "10~15cm") },
        { "lineLaneW", new L(G8, "차선 폭 (m)", "10~15cm") },
        { "lineEdgeW", new L(G8, "가장자리선 폭 (m)", "15~20cm") },
        { "lineNoChangeW", new L(G8, "진로변경제한선 폭 (m)", "15~20cm") },
        { "lineParkW", new L(G8, "정차·주차금지선 폭 (m)", "15~20cm") },
        { "lineParkDoubleW", new L(G8, "정차·주차금지 복선 한 줄 폭 (m)", "10cm") },
        { "parkDoubleGap", new L(G8, "정차·주차금지 복선 간격 (m)", "5cm") },
        { "edgeLineArterial", new L(G8, "끝선: 간선 (국도·광역시도)", "인도 있는 쪽. 도로마다 인스펙터에서 따로 바꿀 수 있음", EdgeOpt) },
        { "edgeLineLocal", new L(G8, "끝선: 그 밖 (지방도·시군도·이면)", "", EdgeOpt) },
        { "parkDashOn", new L(G8, "주차금지 점선 칠 길이 (m)", "515: 3m") },
        { "parkDashOff", new L(G8, "주차금지 점선 빈 길이 (m)", "515: 3m") },
        { "dashUrbanOn", new L(G8, "차선 점선 칠 길이: 도시 (m)", "503 도시부 3m") },
        { "dashUrbanOff", new L(G8, "차선 점선 빈 길이: 도시 (m)", "503 도시부 5m") },
        { "dashExpressOn", new L(G8, "차선 점선 칠 길이: 전용 (m)", "503 자동차전용 10m") },
        { "dashExpressOff", new L(G8, "차선 점선 빈 길이: 전용 (m)", "503 자동차전용 10m") },

        { "laneArrows", new L(G9, "진행방향 화살표 그리기") },
        { "laneArrowLen", new L(G9, "길이 (m)", "가정값 — 시행규칙 별표6 도면으로 확인 필요") },
        { "laneArrowShaftW", new L(G9, "줄기 폭 (m)") },
        { "laneArrowHeadW", new L(G9, "머리 폭 (m)") },
        { "laneArrowHeadLen", new L(G9, "머리 길이 (m)") },
        { "laneArrowSideLen", new L(G9, "옆 머리 길이 (m)") },
        { "laneArrowGap", new L(G9, "정지선에서 (m)", "정지선 2~3m 앞, 차로 가운데") },
        { "laneArrowRepeat", new L(G9, "한 번 더 그리는 간격 (m)", "25~50m, 0 = 안 함") },
        { "leftOnlyMinLanes", new L(G9, "1차로 좌회전 전용: 편도 이 차로 수 이상") },

        { "guideLines", new L(G10, "좌회전 유도선 그리기") },
        { "guideOn", new L(G10, "점선 칠 길이 (m)", "50~100cm") },
        { "guideOff", new L(G10, "점선 빈 길이 (m)", "50~100cm") },
        { "guideMinLen", new L(G10, "이보다 짧으면 안 그림 (m)") },

        { "markLift", new L(G11, "표시 띄움 (m)") },
        { "markings", new L(G11, "노면표시 그리기") },
        { "crosswalks", new L(G11, "횡단보도·정지선 그리기") },

        { "roadText", new L(G13, "노면 글자 그리기", "제한속도 숫자 + 도로마다 직접 넣은 글자") },
        { "textTopMark", new L(G13, "ㅊ·ㅎ 꼭지 모양", "", TopMarkOpt) },
        { "textElong", new L(G13, "진행 방향 늘림 (배)", "글꼴 110x280 비율의 배수. 운전석에서 납작하게 보이면 키움") },
        { "textCharW", new L(G13, "글자 한 칸 폭 (m)", "둘레 빈칸 포함. 모든 글자 같은 크기 — 차로가 좁아 한 줄이 안 들어가면 그 차로만 줄임") },
        { "textMaxPerLine", new L(G13, "한 줄 최대 글자 수", "넘으면 줄을 나눔(어린이/보호구역)") },
        { "textLaneClear", new L(G13, "차선에서 띄움 (m)", "양쪽") },
        { "textFirstLineFar", new L(G13, "두 줄: 첫 줄을 먼 쪽에", "끄면 첫 줄이 운전자 쪽") },
        { "speedMarks", new L(G13, "제한속도 숫자 (517) 자동", "교차로에서 나온 뒤 차로마다 하나") },
        { "speedMarkAt", new L(G13, "숫자 자리: 횡단보도 지나서 (m)") },
        { "speedMarkMinLen", new L(G13, "숫자 쓰는 도로 최소 길이 (m)") },

        { "signalMode", new L(G12, "차량 신호 배치", "교차로마다 따로 바꿀 수 있음", SignalOpt) },
        { "signalNearDist", new L(G12, "정지선 → 제1주 최소 (m)", "그림 3-2: 10m 이상") },
        { "signalBeyondCrosswalk", new L(G12, "횡단보도 끝 → 제1주 (m)", "그림 3-2: 1m") },
        { "secondMainMinLanes", new L(G12, "자동 제2주: 편도 이 차로 수 이상") },
        { "signalFaces", new L(G12, "등면 수 (편도 1~6차로, 제2주 포함)", "표 3-2") },
        { "signalFaceSpacing", new L(G12, "등면 간격 (m)", "2.4m 이상") },
        { "mastHeadHeight", new L(G12, "가로형 등 아래 끝 높이 (도로 면, m)", "4.5m 이상") },
        { "postHeadHeight", new L(G12, "세로형 등 아래 끝 높이 (인도 면, m)", "2.5~3.5m") },
        { "auxSignals", new L(G12, "보조신호등 (제1주 기둥, 정지한 차용)") },
        { "auxHeadHeight", new L(G12, "보조신호등 높이 (m)") },
        { "pedHeadHeight", new L(G12, "보행 신호등 높이 (m)", "2~3m") },
        { "signalMergeDist", new L(G12, "기둥 합치는 거리 (m)", "이 거리 안 기둥은 하나로") },
        { "signalPolePad", new L(G12, "연석 → 기둥 (m)") },
        { "pedSignalSide", new L(G12, "보행 신호 자리", "", PedSideOpt) },
        { "pedSignalGap", new L(G12, "횡단보도 끝 → 보행 신호 (m)") },
        { "elevThreshold", new L(G6, "이보다 높이 올리면 고가 (m)") },
        { "trimFacingSidewalks", new L(G2, "나란한 길 사이 인도 빼기", "분리대 쪽에서 다른 길 차도를 덮는 인도") },
        { "trimOverlappingSidewalks", new L(G2, "나란한 길 인도끼리 겹치면 없앰", "가운데는 지형 — 겹친 면 깜빡임 방지") },
        { "trimMergingRoads", new L(G1, "합류 램프 차도 자르기", "같은 높이로 겹치는 두 길: 작은 길 차도를 큰 길 가장자리에서 자름(램프가 본선에 붙는 곳)") },
        { "trimInnerHalf", new L(G1, "큰 길 안에 다 든 쪽 반은 숨김", "작은 길 중심선이 큰 길 안이고 한쪽만 밖으로 나갈 때 — 겹친 면 줄임") },
        { "trimTaper", new L(G1, "차도 자르기 상판 쪽 부드럽게 (m)", "방호벽·끝선이 꺾이지 않게 앞뒤로 이만큼 걸쳐 좁힘") },
        { "mergeJunctions", new L(G0, "합류·분기점 만들기", "교차로 노드가 아니어도 길이 갈라지거나 합쳐지는 노드(연결로·지하차도·터널 시종점)를 이음(횡단보도·신호 없음)") },
        { "terrainFarStep", new L(G7, "회랑 밖 지형 격자 (m)", "도로에서 먼 곳은 성기게 채움. 0 이면 회랑 밖은 비움") },
        { "terrainSkirt", new L(G7, "지형 조각 틈 가림 깊이 (m)") },
        { "coverMin", new L(G14, "덮개 위 최소 흙 두께 (m)", "이보다 얕으면 개착(옹벽)") },
        { "cutMinDepth", new L(G14, "개착 시작 깊이 (m)", "원래 땅보다 이만큼 낮으면 옹벽, 덜 낮으면 지형 맞춤") },
        { "cutMinLen", new L(G14, "짧은 개착 → 덮개 (m)") },
        { "coverMinLen", new L(G14, "짧은 덮개 → 개착 (m)") },
        { "boxHeight", new L(G14, "지하차도 박스 안 높이 (m)", "건축한계 4.7m + 조명") },
        { "archSpring", new L(G14, "터널 옆벽 높이 (m)", "그 위가 반원 아치") },
        { "tubeMargin", new L(G14, "차도 가장자리 → 벽 (m)", "보도턱·점검로") },
        { "tubeShell", new L(G14, "벽·지붕 두께 (m)") },
        { "wallParapet", new L(G14, "옹벽 난간 높이 (지형 위, m)") },
        { "wallThick", new L(G14, "옹벽 두께 (m)") },
        { "portalWing", new L(G14, "갱구 면벽 옆 폭 (m)") },
        { "twinTubes", new L(G14, "붙은 상·하행 지하차도는 한 박스로", "가운데 벽 대신 콘크리트 분리대 + 기둥 + 보") },
        { "twinPillarSpacing", new L(G14, "가운데 기둥 간격 (m)") },
        { "twinBarrierHeight", new L(G14, "가운데 분리대 높이 (m)") },
        { "tunnelFixtures", new L(G14, "터널 조명등·제트팬", "조명은 발광 재질(RG_Lamp)") },
        { "tunnelLightSpacing", new L(G14, "조명등 간격 (m)", "입구·출구 쪽은 절반") },
        { "tunnelLightEntry", new L(G14, "입구·출구 조명 촘촘 구간 (m)") },
        { "tunnelFanSpacing", new L(G14, "제트팬 한 쌍 간격 (m)") },
        { "tunnelFanMinLen", new L(G14, "제트팬 다는 터널 최소 길이 (m)") },
        { "deckDepth", new L(G15, "상판 두께 (m)") },
        { "barrierWidth", new L(G15, "방호벽 폭 (m)") },
        { "barrierHeight", new L(G15, "방호벽 높이 (m)") },
        { "pierSpacing", new L(G15, "교각 간격 (m)") },
        { "pierRadius", new L(G15, "교각 반지름 (m)") },
        { "pierTwinWidth", new L(G15, "이 폭보다 넓으면 교각 둘 (m)") },
        { "pairByLayer", new L(G17, "층 다르면 상·하행 짝 안 지음", "OSM 층 참고값(복층 다리)") },
        { "osmBridges", new L(G17, "OSM 짧은 다리 반영", "노드링크엔 일반인데 OSM 층 + 인 짧은 구간") },
        { "osmBridgeMaxLen", new L(G17, "OSM 다리 최대 길이 (m)") },
        { "maxEndGrade", new L(G17, "교차로 쪽 끝 최대 경사", "층 나누기 뒤 낭떠러지 방지 (0.08 = 8%)") },
        { "deckValleyGrade", new L(G17, "상판 골 메우기 경사") },
        { "parallelAtGradeNoSplit", new L(G17, "거의 나란한 지면 길은 층 안 나눔", "10° 안 + 많이 겹침: 혹·지하 굴 대신 같은 높이(겹침은 차도 자르기)") },
        { "equalizeSideBySideCuts", new L(G17, "(시험) 겹친 본선·램프 입구 맞추기", "기본 끔: 회동교차로 본선 겹침은 없어지나 교차로 면 자체 겹침이 늘어남") },
        { "equalizeCutDeg", new L(G17, "(시험) 겹친 팔로 보는 각도 (도)") },
        { "deckGap", new L(G16, "복층 다리 위·아래층 높이차 (m)", "광안대교 약 7.8m") },
        { "gwEnable", new L(G16, "광안대교 상세 (현수교·트러스·주탑)") },
        { "gwTop", new L(G16, "위층 노면 최고 높이 (m)", "항로 높이 35m + 트러스") },
        { "gwGrade", new L(G16, "주경간 쪽 경사") },
        { "gwCurve", new L(G16, "꼭대기 종단 곡선 길이 (m)") },
        { "gwApproachGrade", new L(G16, "양 끝 오르막 최대 경사") },
        { "gwBreak", new L(G16, "접속교 경사 바뀌는 곳 (가운데에서 m)") },
        { "gwGrade2", new L(G16, "접속교 경사") },
        { "gwPanel", new L(G16, "트러스 격간 길이 (m)") },
        { "gwSideSpan", new L(G16, "측경간 (m)", "200 + 500 + 200m") },
        { "gwTrussLen", new L(G16, "트러스 구간 길이 (m)") },
        { "gwTowerTop", new L(G16, "주탑 꼭대기 높이 (m)") },
        { "gwPortalSpacing", new L(G16, "접속교 교각 간격 (m)") },
        { "gwLampSpacing", new L(G16, "가로등 간격 (m)") },
        { "gwSplitSolid", new L(G16, "위층 분기 앞 2·3차로 실선 (m)", "남구 방면 1·2차로 / 수영구 방면 3·4차로, 약 480m") },
        { "gwStackHalf", new L(G16, "아래층을 위층 밑으로 겹치는 범위 (가운데에서 m)") },
        { "gwStackHalfS", new L(G16, "남천 쪽 겹침 범위 (가운데에서 m)", "양 층이 같은 오거리로 내려가서 위층이 더 내려갈 거리를 남김") },
        { "seaBridgeMinLen", new L(G16, "바다 위 다리 최소 길이 (m)") },
        { "seaLevelMax", new L(G16, "바다로 보는 지형 높이 (m)") },
        { "seaBridgeTop", new L(G16, "바다 위 다리 노면 높이 (m)") },
        { "seaBridgeGrade", new L(G16, "바다 위 다리 오르막 경사") },
        { "seaBridgeCurve", new L(G16, "바다 위 다리 종단 곡선 (m)") },
        { "waterLevel", new L(G14, "물(바다·강) 높이 (m)", "DEM 이 이 높이 이하면 물 재질") },
        { "landAroundRoads", new L(G14, "지면 도로 둘레 땅 폭 (m)", "DEM 이 0 이어도 땅으로(매립지·해안 도로)") },
        { "throughGuides", new L(G3, "직진 유도선 (꺾이거나 어긋난 직진)", "편람 525 노면유도선") },
        { "throughGuideDeg", new L(G3, "직진 유도선: 꺾임 각도 (도)") },
        { "throughGuideOffset", new L(G3, "직진 유도선: 어긋남 (m)") },
        { "mergeLaneLines", new L(G3, "합류·분기점 안 본선 차선 잇기", "신호·횡단보도 없는 교차로(나들목·합류점)에서 본선 차선·중앙선을 이어 그림") },
        { "laneJunctions", new L(G18, "차로 단위 교차로 면 (한 장)", "교차로 면 = 차로 연결선·입구·모서리 합집합 − 같은 높이 도로 면. 부채꼴·겹침 없음. 끄면 예전 다각형") },
        { "laneStraightDeg", new L(G18, "직진으로 보는 각도 (도)", "이 각 안이면 직진, 밖은 좌·우회전") },
        { "laneUturnDeg", new L(G18, "유턴으로 보는 각도 (도)", "이보다 크게 꺾이면 연결선 안 만듦") },
        { "sheetCell", new L(G18, "교차로 면 격자 (m)", "작을수록 둘레가 정확하고 느림") },
        { "sheetMouthDepth", new L(G18, "입구 띠 깊이 (m)", "꼬인·손으로 이은 교차로: 입구 단면을 안쪽으로 이만큼 깖") },
        { "sheetSubtractRoads", new L(G18, "같은 높이 도로 면은 교차로 면에서 빼기", "입구 밖은 도로가 깖 → 겹침 없음") },
        { "sheetHeightTol", new L(G18, "뺄 높이 차 (m)", "이 안이면 깜빡임(z-fighting)이라 뺌. 더 다르면 위에 있는 면이 덮음") },
        { "sheetSimplify", new L(G18, "둘레 다듬기 허용 오차 (m)") },
        { "sheetMinArea", new L(G18, "버릴 작은 조각·구멍 넓이 (m²)") },
        { "roadOverlapCut", new L(G18, "겹친 도로는 작은 쪽 잘라내기", "같은 높이로 포개진 두 도로(본선 옆 램프 등): 큰 도로는 그대로, 작은 도로에서 겹친 부분을 잘라냄") },
        { "roadOverlapTol", new L(G18, "같은 층으로 볼 높이 차 (m)", "이 안이면 깜빡이는 범위라 작은 도로 쪽을 자름") },
    };

    static bool open = true;

    public static void Draw(SerializedProperty settings)
    {
        open = EditorGUILayout.Foldout(open, "생성 규칙 (바꾸고 [다시 만들기])", true);
        if (!open) return;
        EditorGUI.indentLevel++;
        string group = null;
        var p = settings.Copy(); var end = settings.GetEndProperty();
        bool enter = true;
        while (p.NextVisible(enter) && !SerializedProperty.EqualContents(p, end))
        {
            enter = false;
            L l; Map.TryGetValue(p.name, out l);
            string g = l != null ? l.g : "기타";
            if (g != group) { group = g; EditorGUILayout.Space(4); EditorGUILayout.LabelField(g, EditorStyles.boldLabel); }
            var gc = new GUIContent(l != null ? l.n : p.displayName, l != null ? l.t : "");
            if (l != null && l.opt != null && p.propertyType == SerializedPropertyType.Integer)
            {
                var opts = new GUIContent[l.opt.Length]; for (int i = 0; i < opts.Length; i++) opts[i] = new GUIContent(l.opt[i]);
                p.intValue = EditorGUILayout.Popup(gc, Mathf.Clamp(p.intValue, 0, l.opt.Length - 1), opts);
            }
            else EditorGUILayout.PropertyField(p, gc, true);
        }
        EditorGUI.indentLevel--;
    }
}
