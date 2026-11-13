// PNU 도로 생성기 — 도로 조각 하나. 정보 + 사람이 고칠 값(다시 만들어도 노드링크 ID 키로 유지)
using System.Collections.Generic;
using UnityEngine;
using PNU.RoadGen;

public enum PNUHeightMode
{
    [InspectorName("자동 (지형·도로유형)")] Auto = 0,
    [InspectorName("직접 (올리고 내리기)")] Manual = 1,
}

public enum PNUToggle
{
    [InspectorName("자동")] Auto = -1,
    [InspectorName("없음")] Off = 0,
    [InspectorName("있음")] On = 1,
}

public enum PNUEdgeLine
{
    [InspectorName("자동 (생성 규칙의 끝선)")] Auto = -1,
    [InspectorName("흰 실선 (505 길가장자리구역선)")] White = 0,
    [InspectorName("황색 점선 (515 주차금지)")] YellowDash = 1,
    [InspectorName("황색 실선 (516 정차·주차금지)")] Yellow = 2,
    [InspectorName("황색 복선 (516의2 절대 정차·주차금지)")] YellowDouble = 3,
    [InspectorName("없음")] None = 4,
}

public enum PNUTextDir
{
    [InspectorName("정방향 (도로 시작 → 끝으로 가는 차)")] Forward = 1,
    [InspectorName("역방향 (끝 → 시작으로 가는 차)")] Backward = -1,
}

/// <summary>도로 위 글자 하나 — 문구는 법에 있는 것만(천천히, 어린이보호구역, 방면·시설명, 00앞, 버스 …)</summary>
[System.Serializable]
public class PNURoadText
{
    [Tooltip("'/' 로 줄 나눔(예: 어린이/보호구역). 한글·숫자만")] public string text = "천천히";
    public PNUTextDir direction = PNUTextDir.Forward;
    [Tooltip("그 방향 들어온 쪽(횡단보도 지나서)에서 글자 시작까지 m. 음수 = 앞쪽 정지선에서 거꾸로 잰 거리(글자 끝까지)")] public float distance = 30f;
    [Tooltip("0 = 모든 차로, 1 = 중앙선 쪽 1차로 …")] public int lane = 0;
}

public class PNURoadPiece : MonoBehaviour
{
    [HideInInspector] public string key;
    [HideInInspector] public int roadId;
    public string roadName;
    [TextArea(2, 6)] public string info;

    [Header("높이")]
    public PNUHeightMode heightMode = PNUHeightMode.Auto;
    [Tooltip("+ 올림(많이 올리면 고가: 땅은 그대로), − 내림(주변 땅도 같이 깎임)")]
    public float heightOffset = 0f;
    [Tooltip("양 끝에서 이 길이(m)에 걸쳐 서서히 올리고 내림")]
    public float rampLength = 60f;

    [Header("인도 (진행 방향 기준)")]
    public PNUToggle sidewalkLeft = PNUToggle.Auto;
    public PNUToggle sidewalkRight = PNUToggle.Auto;

    [Header("차로 수 (0 = 데이터대로)")]
    [Tooltip("정방향(도로 시작 → 끝) 차로 수")] public int lanesFwd = 0;
    [Tooltip("역방향(끝 → 시작) 차로 수")] public int lanesBwd = 0;

    [Header("끝선 (인도 있는 쪽, 연석 앞)")]
    [Tooltip("황색을 쓰면 흰 가장자리선은 안 그림(측구 바로 옆). 인도 없는 구간은 늘 흰 실선")]
    public PNUEdgeLine edgeLine = PNUEdgeLine.Auto;

    [Header("노면 글자")]
    [Tooltip("제한속도 숫자(517). 자동 = 생성 규칙(속도 숫자 켜짐, 도로 길이)대로")] public PNUToggle speedMark = PNUToggle.Auto;
    [Tooltip("직접 넣는 글자(차로마다 또는 한 차로)")] public List<PNURoadText> texts = new List<PNURoadText>();

    public bool HasOverride
    {
        get { return heightMode != PNUHeightMode.Auto || sidewalkLeft != PNUToggle.Auto || sidewalkRight != PNUToggle.Auto || lanesFwd > 0 || lanesBwd > 0 || edgeLine != PNUEdgeLine.Auto
                || speedMark != PNUToggle.Auto || (texts != null && texts.Count > 0); }
    }

    public RoadOverride ToOverride()
    {
        return new RoadOverride
        {
            key = key, heightMode = (int)heightMode, heightOffset = heightOffset, rampLength = rampLength,
            sidewalkLeft = (int)sidewalkLeft, sidewalkRight = (int)sidewalkRight, lanesFwd = lanesFwd, lanesBwd = lanesBwd, edgeLine = (int)edgeLine,
            speedMark = (int)speedMark, texts = TextsOut()
        };
    }

    List<RoadTextMark> TextsOut()
    {
        var o = new List<RoadTextMark>();
        if (texts != null) foreach (var t in texts) if (t != null && !string.IsNullOrEmpty(t.text)) o.Add(new RoadTextMark { text = t.text.Trim(), dir = (int)t.direction, at = t.distance, lane = t.lane });
        return o;
    }

    public void From(RoadOverride o)
    {
        if (o == null) return;
        heightMode = (PNUHeightMode)o.heightMode; heightOffset = (float)o.heightOffset; rampLength = (float)o.rampLength;
        sidewalkLeft = (PNUToggle)o.sidewalkLeft; sidewalkRight = (PNUToggle)o.sidewalkRight; lanesFwd = o.lanesFwd; lanesBwd = o.lanesBwd; edgeLine = (PNUEdgeLine)o.edgeLine;
        speedMark = (PNUToggle)o.speedMark;
        texts = new List<PNURoadText>();
        if (o.texts != null) foreach (var t in o.texts) texts.Add(new PNURoadText { text = t.text, direction = t.dir < 0 ? PNUTextDir.Backward : PNUTextDir.Forward, distance = (float)t.at, lane = t.lane });
    }
}
