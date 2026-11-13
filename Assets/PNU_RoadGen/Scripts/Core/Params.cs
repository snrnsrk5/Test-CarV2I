// PNU 도로 생성기 — 생성 규칙(기본값). 유니티 인스펙터에서 바꿀 수 있게 전부 공개 필드
// 치수 근거: 도로의 구조·시설 기준에 관한 규칙(차로 폭·보도), 교통노면표시 설치·관리 매뉴얼(선 폭·점선 길이), Road Moduler 보도 단면
using System;

namespace PNU.RoadGen
{
    [Serializable]
    public class GenParams
    {
        // 도로망 만들기
        public double step = 2.0;                    // 중심선 점 간격(m)
        public double smoothSigmaM = 6.0;            // 중심선 다듬기(m)
        public double junctionLinkMax = 30;          // 이 길이보다 짧은 교차로-교차로 링크는 한 교차로로 묶음
        public double junctionLinkMaxSameName = 45;  // 같은 이름 교차로 노드는 이 길이까지
        public bool mergeThroughStructures = true;   // 교차로 노드 묶음에 걸린 지하차도·터널·고가·다리(마주 보는 같은 구조 팔 둘)는 교차로를 그냥 지나가는 한 도로로
        public bool pairByStructure = true;          // 상행·하행 짝은 구조(땅 / 다리·고가 / 지하·터널)가 같을 때만
        public double pairMeanMax = 22, pairMaxMax = 40;  // 상행·하행 짝 거리(노드링크는 보통 12m 간격)
        public double junctionNodeNear = 20;         // 이보다 가까운 교차로 노드는 한 교차로
        public double junctionSameNameDist = 80;     // 같은 이름 교차로 노드는 이 거리까지 한 교차로
        public double junctionMergeDist = 90;        // 상행·하행이 서로 다른 교차로에 붙었을 때 이 거리 안이면 합침
        public double junctionMaxRadius = 40;        // 한 교차로 노드 묶음 반경 상한(넘으면 안 합침)
        public bool mergeJunctions = true;           // 교차로 노드가 아니어도 길이 갈라지거나 합쳐지는 노드(연결로·지하차도·터널 시종점)를 합류·분기점으로
        public bool pairByName = true;               // 상행·하행 이름이 다 있고 서로 다르면 짝짓지 않음(나란한 다른 길)
        public bool pairByLayer = true;              // OSM 층 참고값(S 줄)이 다르면 상·하행 짝짓지 않음(복층 다리)
        public bool osmBridges = true;               // OSM 층 +인 짧은 구간(노드링크엔 일반)은 다리로(밑 지형 낮춤·교각)
        public double osmBridgeMaxLen = 250;         // 그보다 긴 OSM 다리 구간은 층 나누기에만 씀
        public double deckGap = 7.8;                 // 복층 다리(둘 다 교량, OSM 층 다름) 위층 → 아래층 높이차(m)
        public bool parallelAtGradeNoSplit = true;   // 거의 나란한(10° 안) 지면 길 둘이 겹쳐도 층을 나누지 않음(혹·지하 굴 방지, 겹침은 차도 자르기로)
        // 광안대교(실제 제원): 현수교 200+500+200, 트러스교 720(→ 트러스 1620m), 상층 46m(주경간 가운데), 주탑 꼭대기 112m
        public bool gwEnable = true;
        public double gwTop = 46.0, gwGrade = 0.018, gwCurve = 950, gwApproachGrade = 0.035;
        public double gwBreak = 1150, gwGrade2 = 0.012;  // 가운데에서 이 거리부터 접속교 경사
        public double gwPanel = 12.0, gwSideSpan = 200, gwTrussLen = 1620, gwTowerTop = 112.0;
        public double gwPortalSpacing = 72, gwLampSpacing = 36;
        public double gwSplitSolid = 480;            // 위층 남천 쪽 분기(1·2차로 남구 / 3·4차로 수영구) 앞 2·3차로 사이 백색 실선 길이(m, 부산일보 2022-07-04 보도 약 480m)
        public double gwStackHalfS = 1250;           // 남천 쪽(남쪽 주탑 쪽) 겹침 범위 — 양 층이 같은 오거리로 내려가서 위층이 더 내려갈 거리(약 600m)를 남김
        public double gwStackHalf = 1650;            // 주경간 가운데에서 이 거리 안만 아래층을 위층 바로 밑으로(그 밖은 노드링크 자리 — 양 끝에서 높이차가 줄어듦)
        public double seaBridgeMinLen = 1000;        // 이보다 긴 다리(003)이고 밑 지형(중앙값)이 seaLevelMax 보다 낮으면 바다 위 다리
        public double seaLevelMax = 2.0;
        public double seaBridgeTop = 38.0;           // 바다 위 다리 노면 최고 높이(아래층 기준, 위층은 + deckGap) — 광안대교 상층 약 46m
        public double seaBridgeGrade = 0.03;         // 양 끝에서 오르는 경사
        public double seaBridgeCurve = 120;          // 종단 곡선 길이(m)
        public double splitNear = 20;                // 반대 방향이 끝나는 교차로를 이 거리 안으로 지나가는 사슬은 거기서 자름

        // 차로 폭(m): 설계속도 기준(도시지역)
        public double laneW80 = 3.5, laneW60 = 3.25, laneWLow = 3.0, laneWExpress = 3.6;
        public double centerHalfGap = 0.25;          // 비분리 도로 중앙선 반폭(황색 복선 0.15×2 + 간격)
        public double medianHalf = 1.0;              // 분리 도로(도시고속 등) 분리대 반폭
        // 오른쪽 길어깨(차로 끝 → 연석): 도로의 구조·시설 기준에 관한 규칙 제12조, 도시지역 일반도로 설계속도 80 이상 1.5 / 60~80 1.0 / 60 미만 0.75m
        public double shoulder80 = 1.5, shoulder60 = 1.0, shoulderLow = 0.75, shoulderExpress = 1.5;
        public double gutterWidth = 0.35;            // 측구(물 빠지는 곳, 연석 앞 콘크리트): 길어깨 안에서 연석 쪽(Road Moduler 배수로 35cm)

        // 보도(연석 포함 전체 폭)
        public double sidewalkArterial = 3.5, sidewalkLocal = 2.5, sidewalkMin = 2.0;
        public double curbHeight = 0.15, curbWidth = 0.2;

        // 차로 수가 바뀌는 곳: 폭 1m 바뀌는 데 쓰는 길이(m) — 넓은 쪽이 좁은 구간 안으로 테이퍼
        public double taperUrban = 15, taperExpress = 25;

        // 교차로
        public double cornerRadiusArterial = 10, cornerRadiusLocal = 6;
        public double crosswalkWidth = 4.0, crosswalkStripe = 0.5, crosswalkGap = 0.75;   // 업무편람 그림 3-46: 폭 4m, 줄 l1 45~50cm, 간격 l2 = 1.5 l1
        public double crosswalkStripeMin = 0.45;     // 폭을 끝까지 채울 때 줄 폭 하한(45cm) — 줄·간격을 조금씩 늘이고 줄여 양 끝을 맞춤
        public double crosswalkCenterGap = 1.0;      // 중앙선 위는 비움(l3 = 90~100cm)
        public double crosswalkWidthSplit = 6.0;     // 폭 6m 이상이면 2등분 — 두 줄은 엇갈리게(1010/0101), 붙이거나 45~50cm 띄움
        public double crosswalkSplitGap = 0.0;       // 2등분 사이 띄움(0 = 붙임, 0.45~0.5 가능)
        public double crosswalkArrowLen = 0.9, crosswalkArrowW = 0.7, crosswalkArrowHead = 0.6;   // 보행 방향 화살표(실무 관행: 줄마다 양 끝에 2개, 머리(세모)가 몸통(네모)보다 김)
        public bool crosswalkArrows = true;
        public bool centerLineThroughCrosswalk = false;   // 중앙선을 횡단보도 가운데 틈으로 지나 입구까지(기본: 횡단보도 앞까지)
        public int splitCrosswalkMinLanes = 3;       // 자동일 때 편도 이 차로 수 이상인 길은 2단(6m) 횡단보도
        public double stopLineGap = 3.0, stopLineWidth = 0.45;        // 정지선(530): 횡단보도 3m 이상 앞(2~10m), 폭 도심 30~45cm
        public double mouthMin = 6.0;                // 교차로 중심에서 입구까지 최소 거리
        public double shortRoadKeep = 6.0;           // 짧은 도로는 양 끝 입구를 줄여서라도 이만큼(m)은 남김(도로가 사라지지 않게)
        public double cornerToMouth = 3.0;           // 모서리 곡선 끝(가각부)에서 입구(횡단보도 시작)까지: 3m 이상
        public double maxCut = 60;                   // 입구 거리 상한(넘으면 모서리 반경을 줄임)
        // 차로 단위 교차로(LaneJunction.cs): 차로 끝점 → 연결선 → 교차로 면 한 장
        public bool laneJunctions = true;            // 켜면 교차로 면을 [연결선·입구·모서리 합집합 − 도로 면]으로 한 장(부채꼴·겹침 없음)
        public double laneStraightDeg = 35;          // 이 각 안이면 직진(그 밖은 좌·우회전)
        public double laneUturnDeg = 160;            // 이 각보다 크게 꺾이면 유턴(연결선 안 만듦)
        public double sheetCell = 0.25;              // 교차로 면 격자 칸(m) — 작을수록 둘레가 정확, 느림
        public double sheetMouthDepth = 3.0;         // 입구 띠 깊이(m, 꼬인 교차로)
        public bool sheetSubtractRoads = true;       // 같은 높이 도로 면은 교차로 면에서 뺌(겹침 없앰)
        public double sheetHeightTol = 0.3;          // 이 높이 차 안(깜빡이는 범위)의 도로만 뺌 — 더 다르면 위에 있는 면이 덮음
        public double sheetSimplify = 0.04;          // 둘레 다듬기 허용 오차(m)
        public double sheetMinArea = 0.3;            // 이보다 작은 조각·구멍은 버림(m²)
        public bool roadOverlapCut = true;           // 같은 높이로 겹친 두 도로: 큰 도로는 그대로, 작은 도로에서 겹친 부분을 잘라냄(RoadOverlap.cs)
        public double roadOverlapTol = 0.3;          // 이 높이 차 안(깜빡이는 범위)이면 작은 도로 쪽을 잘라냄
        public double sideBySideDeg = 20;            // 두 팔 사이 각이 이보다 작으면 나란히 붙은 것으로(모서리·인도 없음)
        public bool equalizeSideBySideCuts = false;  // (시험, 기본 끔) 거의 겹친 양방향 본선·일방 램프 팔끼리 입구 거리를 같게 — 회동교차로 본선 겹침은 없어지나 교차로 면 자체 겹침이 늘어 끔
        public double equalizeCutDeg = 3.0;
        public double filletMaxDeg = 150;            // 이보다 벌어진 모서리는 곡선 대신 완만하게 이음
        public double solidBeforeStop = 30.0;        // 정지선 앞 백색 실선 길이(진로변경 제한)
        public bool tunnelSolidLines = true;         // 터널·지하차도 안 차선은 백색 실선(업무편람 4.차선 해설: 터널 내 진로변경 제한)
        public double tunnelSolidExtend = 50.0;      // 터널 입구 앞·출구 뒤로 실선을 더 긋는 길이(m)

        // 높이
        public double heightSmoothM = 12;            // 지형 높이 다듬기(m)
        public double junctionBlend = 20;            // 교차로 평면 → 도로 높이로 넘어가는 길이(m)
        public double maxJunctionGrade = 0.08;       // 교차로 평면 최대 기울기
        public double overpassClear = 7.0, underpassDepth = 7.0, rampGrade = 0.06;   // 고가·지하차도(노드링크 도로유형 001/002)
        // 땅 밑 구간(지하차도·터널): 덮개(박스·말굽 아치) / 개착(U형 옹벽) / 갱구 면벽
        public double coverMin = 0.3;                // 덮개 윗면 위로 이만큼 흙이 있어야 덮개(아니면 개착)
        public double cutMinDepth = 1.0;             // 원래 땅보다 이만큼 이상 낮으면 개착(옹벽), 덜 낮으면 그냥 지형 맞춤
        public double cutMinLen = 10, coverMinLen = 16;   // 이보다 짧은 개착(덮개 사이)·덮개는 앞뒤에 붙임
        public double boxHeight = 5.0;               // 박스(지하차도) 안 높이(건축한계 4.7m + 조명)
        public double archSpring = 2.6;              // 터널 말굽 아치: 옆벽 높이(그 위 반원)
        public double tubeMargin = 0.75;             // 차도 가장자리 → 벽(보도턱·점검로)
        public double tubeShell = 0.7;               // 벽·지붕 두께
        public double wallParapet = 1.0;             // 옹벽 위 난간벽(지형보다 이만큼 위)
        public double wallThick = 0.6;               // 옹벽 두께
        public double portalWing = 2.5;              // 갱구 면벽이 터널 밖으로 나가는 폭(양옆)
        public bool twinTubes = true;                // 상·하행 지하차도·개착이 붙어 있으면 한 박스로(가운데 벽 대신 기둥)
        public double twinPillarSpacing = 10.0;      // 쌍둥이 박스 가운데 기둥 간격(m)
        public double twinBarrierHeight = 0.9;       // 쌍둥이 지하차도·개착 사이 콘크리트 중앙분리대 높이(m)
        public bool tunnelFixtures = true;           // 터널·지하차도 안 조명등(발광)·제트팬
        public double tunnelLightSpacing = 8.0;      // 조명등 간격(m) — 입구·출구 쪽은 절반
        public double tunnelLightEntry = 60.0;       // 입구·출구에서 이 거리 안은 조명 촘촘히(m)
        public double tunnelFanSpacing = 150.0;      // 제트팬 한 쌍 간격(m)
        public double tunnelFanMinLen = 300.0;       // 이보다 긴 터널(004)만 제트팬
        // 교량·고가 상판 구조
        public double deckDepth = 1.8;               // 상판 두께(옆면이 도로 면 아래로 내려오는 깊이)
        public double barrierWidth = 0.45, barrierHeight = 1.0;   // 가장자리 콘크리트 방호벽
        public double pierSpacing = 32;              // 교각 간격(m)
        public double pierRadius = 1.0;              // 교각 반지름
        public double pierTwinWidth = 14;            // 상판 폭이 이보다 넓으면 교각 둘
        public double skirtDepth = 0.4;              // 인도 바깥 아래로 내린 치마(지형과 틈 가림)
        public bool separateLevels = true;           // 같은 자리를 지나는 다리·고가 ↔ 땅 ↔ 지하차도를 높이로 떼어 놓음(겹친 곳 경사로 포함)
        public bool trimMergingRoads = true;         // 같은 높이로 겹치는 두 길(램프 합류 등): 작은 길 차도를 큰 길 가장자리에서 자름
        public bool trimInnerHalf = true;            // 작은 길 중심선이 큰 길 안이고 한쪽만 밖으로 나가면, 큰 길 안에 다 든 쪽 반은 숨김
        public double trimTaper = 8.0;               // 차도 자르기 폭 바뀜을 이 길이(m, 앞뒤 각각)에 걸쳐 부드럽게(방호벽·끝선이 꺾이지 않게)
        public bool trimFacingSidewalks = true;      // 나란한 길 사이(분리대 쪽)에서 다른 길 차도를 덮는 인도는 뺌
        public bool trimOverlappingSidewalks = true; // 나란한 두 길 인도끼리 같은 높이로 겹치면 둘 다 없앰(가운데는 지형) — 겹친 면 깜빡임 방지
        public bool liftExpressways = true;          // 도시고속(101·102)이 일반 길과 같은 높이로 겹치면 그 구간을 고가로 올림
        public double levelClear = 6.5;              // 그때 위아래 도로면 높이 차(m): 높이 제한 4.5 + 상판 두께
        public double elevThreshold = 4.5;           // 직접 올린 높이가 이보다 크면 고가로 봄(땅은 그대로)
        public double maxEndGrade = 0.08;            // 교차로에 닿은 도로 끝 쪽 최대 경사(층 나누기 뒤 낭떠러지 방지)
        public double deckValleyGrade = 0.012;       // 상판 골 메우기: 높은 곳에서 이 경사보다 더 내려앉은 골은 메움

        // 지형 맞춤
        public double terrainStep = 2.0;             // 지형 격자 간격(m)
        public double terrainBlend = 25;             // 도로 가장자리 → 원래 지형으로 넘어가는 거리(m)
        public double terrainClear = 0.3;            // 도로·인도 면보다 이만큼 낮게
        public double terrainTile = 128;             // 지형 메쉬 조각 크기(m)
        public double terrainFarStep = 16;           // 회랑(terrainCorridor) 밖 지형 격자(m) — 0 이면 회랑 밖은 비움
        public double terrainSkirt = 4.0;            // 촘촘·성긴 지형 조각 경계 틈 가림 띠 깊이(m)
        public double waterLevel = 0.05;             // DEM 이 이 높이 이하인 곳은 물(바다·강) 재질
        public double landAroundRoads = 6.0;         // 지면 도로·교차로 둘레 이 거리(m) 안은 DEM 이 0 이어도 땅(매립지·해안 도로가 물 위에 뜨지 않게)
        public double terrainCorridor = 60;          // 도로·교차로에서 이 거리(m) 안에 걸친 지형 조각만 만듦(0 = 범위 전체). 경로 전체를 한 번에 볼 때 가볍게
        public double terrainStepLarge = 4.0;        // 범위가 넓으면(terrainLargeArea 넘으면) 지형 격자 간격(m)
        public double terrainLargeArea = 6.0;        // 넓은 범위 기준(km²)
        public double bridgeClear = 3.0;             // 교량·고가 상판 밑 지형 최소 여유(m)
        public double bridgeBlend = 8.0;             // 그 둘레 비탈 거리(m)

        // 노면표시
        // 선 폭(표 2-1, m): 중앙선 실선 15~20cm, 중앙선 복선 10~15cm·간격 10~15cm, 차선 10~15cm, 길가장자리구역선 15~20cm,
        //   진로변경제한선(정지선 앞 실선) 15~20cm, 정차·주차금지선 실선 15~20cm / 복선(516의2) 10cm·간격 5cm
        public double lineCenterW = 0.20, lineCenterDoubleW = 0.15, doubleGap = 0.15;
        public double lineLaneW = 0.15, lineEdgeW = 0.20, lineNoChangeW = 0.20;
        public double lineParkW = 0.15, lineParkDoubleW = 0.10, parkDoubleGap = 0.05;
        // 끝선(차도 바깥, 연석 쪽): 0 흰 실선(505 길가장자리구역선), 1 황색 점선(515 주차금지), 2 황색 실선(516 정차·주차금지),
        //   3 황색 복선(516의2 절대 정차·주차금지), 4 없음 — 황색을 쓰면 흰 가장자리선은 안 그림. 인도 없는 길(자동차전용 등)은 늘 흰 실선
        //   기본(사용자 결정 17:4x): 간선(일반국도·특별/광역시도, 노드링크 등급 103·104)은 황색 실선, 그 밖(지방도·시군도·이면)은 황색 점선
        public int edgeLineArterial = 2, edgeLineLocal = 1;
        public double parkDashOn = 3, parkDashOff = 3;                   // 515 주차금지 점선 3/3m
        public double dashUrbanOn = 3, dashUrbanOff = 5, dashExpressOn = 10, dashExpressOff = 10;   // 표 2-1 차선(503): 도시부 3/5m, 자동차전용 10/10m
        // 진행방향 화살표(537~539): 정지선에서 2~3m 앞, 차로 가운데, 25~50m 간격으로 하나 더(편람 43). 크기는 내 가정값(시행규칙 별표6 도면으로 확인 필요)
        public bool laneArrows = true;
        public double laneArrowLen = 5.0, laneArrowShaftW = 0.25, laneArrowHeadW = 0.9, laneArrowHeadLen = 1.5, laneArrowSideLen = 0.9;
        public double laneArrowGap = 2.5, laneArrowRepeat = 30;
        public int leftOnlyMinLanes = 3;             // 편도 이 차로 수 이상이면 1차로를 좌회전 전용으로(아니면 직진+좌회전)
        // 좌회전 유도선(525): 흰 점선 50~100cm / 50~100cm, 폭은 이어지는 선(차선) 폭, 진행 방향 왼쪽
        public bool guideLines = true;
        public double guideOn = 0.75, guideOff = 0.75, guideMinLen = 12;
        public bool throughGuides = true;            // 직진 유도선(중앙선 연장): 꺾이거나 어긋난 직진만
        public bool mergeLaneLines = true;           // 신호·횡단보도 없는 교차로(합류·분기점·나들목) 안에 본선 차선·중앙선을 이어 그림(노면표시 빈 곳 없앰, 마주 보는 본선 짝이 하나일 때만)
        public double throughGuideDeg = 12, throughGuideOffset = 1.5;
        public double markLift = 0.03;               // 아스팔트 위로 띄우기(면을 따라가게 나눠서 깔고 3cm)
        public bool markings = true, crosswalks = true;
        // 노면 글자(편람 2장 3절: 그림 2-2 모양, 차로 폭 따라 0.5~2배) — 사용자 글꼴(RoadText.cs: 획 20·빈칸 10·글자 110x280)
        //   가로쓰기(운전자 기준 왼→오), 진행 방향으로 길게. 문구는 법에 있는 것만(천천히, 어린이보호구역, 방면·시설명, 00앞, 속도 숫자)
        public bool roadText = true;
        public int textTopMark = 2;                  // ㅊ ㅎ 꼭지: 0 짧은 가로획 1 짧은 세로획(띄움) 2 붙은 세로획(원래 모양, 사용자 선택)
        public double textElong = 1.0;               // 진행 방향으로 늘림(글꼴 110x280 비율의 배수). 사용자 19:30: 일단 늘리지 않음(폰트는 나중에)
        public double textCharW = 0.55;              // 글자 한 칸 폭(m, 둘레 빈칸 포함) — 모든 글자 같은 크기(사용자 19:37). 차로가 좁아 한 줄이 안 들어가면 그 차로만 줄임
        public int textMaxPerLine = 4;               // 한 줄 최대 글자 수(넘으면 줄을 나눔: 어린이/보호구역)
        public double textLaneClear = 0.35;          // 차선에서 띄우는 거리(m, 양쪽)
        public bool textFirstLineFar = true;         // 두 줄: 첫 줄을 먼 쪽에(운전자가 위→아래로 읽음)
        public bool speedMarks = true;               // 제한속도 숫자(517): 교차로에서 나온 뒤 차로마다 하나
        public double speedMarkAt = 25;              // 들어온 쪽 횡단보도(없으면 입구)에서 숫자 시작까지(m)
        public double speedMarkMinLen = 300;         // 도로가 이보다 짧으면 안 씀(m). 150 이면 경로 전체 1084개 → 도로 정점 +22% 라 300 으로
        public GlyphStyle TextStyle() { return new GlyphStyle { topMark = textTopMark, arcStepDeg = 20 }; }

        // 신호등 자리(교통신호기 설치·관리 업무편람 2026, 제1편 3장)
        public int signalMode = 0;                   // 0 자동(제1주 + 편도 secondMainMinLanes 차로 이상 제2주), 1 제1주만, 2 제1주+제2주, 3 정지선 부근+제2주, 4 건너편만(예전 방식)
        public double signalNearDist = 10;           // 정지선 → 제1주 기둥 최소 거리(그림 3-2: d = 10m 이상, 등면 높이 5m·앙각 15°)
        public double signalBeyondCrosswalk = 1.0;   // 횡단보도 끝(교차로 쪽)에서 제1주 기둥까지(그림 3-2: 1.0m)
        public int secondMainMinLanes = 3;           // 자동: 편도 이 차로 수 이상이면 건너편 제2주 추가(편도 2차로 이하는 원칙적으로 제1주만)
        public int[] signalFaces = { 1, 2, 2, 3, 3, 3 };   // 표 3-2: 편도 1·2·3·4·5·6차로 이상 주신호등면 수(제2주 포함)
        public double signalFaceSpacing = 2.4;       // 한 팔의 등면 가운데 간격 2.4m 이상
        public double mastHeadHeight = 5.0;          // 가로형(내민식) 등면 아래 끝: 도로 면에서(4.5m 이상)
        public double postHeadHeight = 3.0;          // 세로형(기둥식) 등면 아래 끝: 인도 면에서(2.5~3.5m)
        public bool auxSignals = true;               // 보조신호등(200mm)을 제1주 기둥에: 정지한 차 운전자가 보는 낮은 신호등(표 3-2 주1)
        public double auxHeadHeight = 3.0;           // 보조신호등 아래 끝(인도 면에서)
        public double pedHeadHeight = 2.2;           // 보행 신호등 아래 끝(인도 면에서 2~3m)
        public double signalMergeDist = 3.5;         // 보행 신호등을 이 거리 안 차량 신호 기둥에 붙임(가급적 차량 신호기에 부착)
        public double signalPolePad = 0.6;           // 연석에서 기둥까지
        public int pedSignalSide = 0;                // 보행 신호 자리: 0 횡단보도 옆 교차로 쪽, 1 바깥쪽
        public double pedSignalGap = 0.6;            // 횡단보도 띠 끝에서 보행 신호 자리까지

        public bool IsExpress(string rank) { return rank == "101" || rank == "102"; }
        public bool IsArterial(string rank) { return rank == "103" || rank == "104"; }
        public int EdgeLine(string rank) { return IsArterial(rank) ? edgeLineArterial : edgeLineLocal; }
        public double LaneWidth(string rank, int spd)
        {
            if (IsExpress(rank)) return laneWExpress;
            return spd >= 80 ? laneW80 : spd >= 60 ? laneW60 : laneWLow;
        }
        public double Shoulder(string rank, int spd) { return IsExpress(rank) ? shoulderExpress : spd >= 80 ? shoulder80 : spd >= 60 ? shoulder60 : shoulderLow; }
        public double SidewalkWidth(string rank, string connect)
        {
            if (IsExpress(rank) || !(string.IsNullOrEmpty(connect) || connect == "0" || connect == "000")) return 0;
            return rank == "103" || rank == "104" ? sidewalkArterial : rank == "105" || rank == "106" ? sidewalkLocal : sidewalkMin;
        }
        public double CornerRadius(string rankA, string rankB)
        {
            bool art = rankA == "103" || rankA == "104" || rankB == "103" || rankB == "104";
            return art ? cornerRadiusArterial : cornerRadiusLocal;
        }
    }
}
