// PNU 도로 생성기 — 씬 루트. 입력·생성 규칙·범위·재질·신호등 프리팹을 들고 있고,
// 인스펙터의 [다시 만들기]로 도로·교차로·인도·노면표시·지형·신호등 자리를 새로 만듦(사람이 고친 값은 유지)
using UnityEngine;
using PNU.RoadGen;

public class PNURoadGenRoot : MonoBehaviour
{
    [Tooltip("노드링크 + 높이 입력(탭 구분 텍스트)")]
    public TextAsset input;

    [Tooltip("생성 규칙(차로 폭, 인도, 교차로 모서리, 노면표시, 지형 맞춤 등)")]
    public GenParams settings = new GenParams();

    [Header("만들 범위 (m, x = 동쪽, z = 북쪽) — 기본: 경로 전체")]
    public double x0 = -4100;
    public double z0 = -6600;
    public double x1 = 2000;
    public double z1 = 6500;

    [Header("만들 것")]
    public bool buildTerrain = true;
    public bool buildSignals = true;
    [Tooltip("Data/pnu_editor_net.txt(도로 편집기에서 고친 도로망)가 있으면 그것으로")]
    public bool useEditorNet = true;
    [Tooltip("예전 지도 건물(Data/pnu_buildings.txt)을 새 지형 위에")]
    public bool buildBuildings = true;
    [Tooltip("예전 지도 구조물(정문·NC백화점·부경대 정문·1호선 고가)")]
    public bool buildOldStructures = true;
    [Tooltip("운전 테스트(자차 테슬라 + 운전석 + 주행 재생, Data/pnu_drive_rg.json)")]
    public bool buildDrive = true;

    [Header("신호등 (비우면 빈 자리만)")]
    [Tooltip("차량 신호등 프리팹. 자리마다 깔리고, IPNUSignalTarget 을 구현한 컴포넌트가 있으면 자리 정보(차로 수, 팔 길이, 부착물 힌트)를 받음")]
    public GameObject vehicleSignalPrefab;
    [Tooltip("보행 신호등 프리팹")]
    public GameObject pedestrianSignalPrefab;

    [Header("재질 (비우면 기본 색 재질을 자동으로 만듦)")]
    public Material asphalt;
    public Material gutter;
    public Material curb;
    public Material sidewalk;
    public Material curbSide;
    public Material markWhite;
    public Material markYellow;
    public Material terrain;

    [HideInInspector] public string lastLog = "";
    [HideInInspector] public int settingsVersion = 0;   // 기본값을 바꾼 항목을 예전 씬에도 올려 주기 위한 번호
}
