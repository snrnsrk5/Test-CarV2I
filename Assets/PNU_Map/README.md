# PNU_Map (부산대 → 부경대 코스) 1차 에셋

## 좌표
- 유니티 X = 동쪽, Y = 높이, Z = 북쪽 (단위 m). 원점은 위도 35.1865, 경도 129.1225 (UTM 52N 기준 로컬).
- FBX는 이 좌표 그대로 들어오게 만들었어요. 만약 맵이 180° 돌아가 보이면 Map 루트를 Y축 180° 돌려주세요.

## 폴더
- Map/Section_*.fbx : 경로 구간별(A 캠퍼스 ~ H 남천·부경대) 도로면(Asphalt), 보도(Pavers), 연석(Curb), 구조물(Concrete: 교량·터널·옹벽), 노면표시(Marking_White/Yellow/Blue), 화살표(RoadMarking_Atlas), 터널 조명(TunnelLight), 지형(Terrain_Landcover)
- Map/Buildings_*.fbx : 구간별 건물 상자 (Far = 경로 700m 밖)
- Map/Background.fbx : 코리도 밖 거친 지형 + 바다
- Data/map_data.json : 자차 경로·타임라인(s, pos, t), 정지(S0·S1·S3·S5), 구간 라벨, 이벤트, 하이퍼랩스 구간, 제한속도, 신호등 위치·신호 주기, 가로수·가로등·교각
- Data/npc_tracks.json : NPC 차량 궤적(경로 + 0.5초마다 거리)
- Modules/*.fbx : 사용자 모듈(RoadModuler, TrafficLightAnim, TL_Human_Shape)
- Textures/ : 원본 텍스처 + Terrain_Landcover.jpg(지형 색, UV는 FBX에 포함)

## 재질 연결
FBX 임포트 후 Materials 탭 > Extract Materials 로 꺼낸 뒤 이름대로 텍스처 연결:
Asphalt → Asphalt_Base.png (UV 1 = 3.5m), Pavers → Pavers_1.jpg (UV 1 = 3.5m), RoadMarking_Atlas → Road_Marking_I.png(알파), Terrain_Landcover → Terrain_Landcover.jpg, TunnelLight → 발광.
(다음 단계에서 이 연결과 신호등·나무·NPC 배치, 타임라인 재생을 자동으로 하는 에디터 스크립트를 드릴게요.)
