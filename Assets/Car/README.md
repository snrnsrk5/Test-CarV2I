# PNU01 자율주행 시험차 (PNU 미래자동차 XR)

차체색: 부산대 CI 파랑 **PNU BLUE (PANTONE 2945 C, sRGB #004C97)**, 메탈릭 0.6

## 넣는 법 (Unity 2022.3 · URP)
1. 예전 `Assets/PNU_AV01` 폴더가 있으면 **지우고** 이 폴더를 통째로 `Assets/PNU_AV01` 에 둡니다.
   (같은 이름의 스크립트가 두 벌 생기지 않게 폴더 이름은 그대로입니다.)
2. 메뉴 **PNU Map → PNU01 → Build Prefab** 을 실행합니다.
   - 재질(URP Lit) 52개 생성, FBX 재질 연결, `Assets/PNU_AV01/PNU01.prefab` 생성
3. 씬에 `PNU01.prefab` 을 놓습니다. 앞 = +Z, 원점 = 휠베이스 가운데 노면.

## 로고·번호판 바꾸기
- `Textures/PNU01_Logo.png` (앞 후드 끝·트렁크 로고, 투명 배경 PNG 권장)
- `Textures/PNU01_Plate.png` (앞·뒤 번호판, 520×110 긴 번호판 비율)
- 파일을 같은 이름으로 덮어쓰면 앞·뒤가 함께 바뀝니다(메뉴를 한 번 더 실행해도 됨).
- 위치 기준점: `Badges/Logo_F_Anchor, Logo_R_Anchor, Plate_F_Anchor, Plate_R_Anchor`

## 켜고 끄기 (PNUAVCar 인스펙터 체크박스 · 플레이 중 단축키)
| 키 | 그룹 | 내용 |
|---|---|---|
| 1 | roofTower | 지붕 크라운: 회전 라이다, 카메라 11개(검은 유리 띠), 청록 자율주행 표시등 띠, GNSS 2 |
| 2 | cornerLidar | 범퍼 네 모서리 라이다(차체색 하우징 + 검은 유리 창) |
| 3 | fenderPods | 앞 펜더 포드 2(앞·옆 카메라 + 작은 회전 라이다) |
| 4 | cameras | 앞유리 위 3안 카메라, B필러 카메라 2, 번호판 위 후방 카메라 |
| 5 | radar | 앞 레이더 창, 모서리 레이더 4(차체색 커버), 뒤 레이더, 초음파 8 |
| 6 | wrap | 래핑(PNU01, AUTONOMOUS TEST VEHICLE, 청록 줄) |
| 0 | 전체 | 센서 전부 켜기/끄기 |
| 7 | 휠 | Turbine → MultiSpoke → PixelAero → **Split5(기본)** 순환 |
| 8 | 스캔 연출 | PNUAVSensorFX 켜기/끄기(라이다 레이저, 카메라 시야, 레이더 부채꼴) |
| 9 | 운전석 유리 | 유리를 거의 투명하게(운전석 시점용) |

스크립트에서는 `car.SetGroup("RoofTower", false)`, `car.SetAllSensors(true)`, `car.SetWheelStyle(3)`, `car.SetDriverView(true)` 로 바꿀 수 있습니다.

## 움직임
- `autoFromMotion`(기본 켬): 차가 움직인 만큼 바퀴가 구르고, 회전량으로 앞바퀴 조향각과 운전대를 계산합니다.
- 직접 넣으려면 끄고 `speed`(m/s), `steerAngle`(도, +오른쪽)를 넣으면 됩니다.
- 라이다 회전: `LidarTop_Spin`, `FP_Lidar_L_Spin`, `FP_Lidar_R_Spin` (보이는 속도 lidarRpm=120).

## 구조
PNU01 ─ Body · Glass · Interior · SteeringWheel(피벗) · DriverEye(운전자 눈) · Badges(로고·번호판)
      ─ Steer_FL/FR/RL/RR → Wheel_xx(굴림) → Tire_xx, RimA/RimB/RimC/RimD_xx · Caliper_xx
      ─ Sensors → Sensors_RoofTower / CornerLidar / FenderPods / Cameras / Radar / Wrap
      ─ FX → FX 기준점 20개(라이다 7, 카메라 7, 레이더 6 — 위치·방향은 Data/pnu01_data.json)

- 센서는 모두 바닥 윤곽을 차체 곡면에 맞춰 잘랐습니다(틈 0.3~1.6 mm, 유리 지붕 이음 홈 위만 3 mm로 건넘, 4 mm 고무 패킹 띠).
  차체를 뚫는 곳 0, 좌우 차이 ≤ 0.6 mm, 앞바퀴 ±35° 조향에서 간섭 없음.
- 모든 면이 바깥을 향하도록 정리했습니다(유니티는 뒷면을 그리지 않음). 원본 G80 판넬·타이어·휠의 안쪽으로 감긴 면 4.5만 개를 뒤집었고,
  FBX 를 다시 불러와 뒷면을 끈 렌더로 빠진 면이 없는지 확인했습니다.
- 삼각형: 그려지는 양 약 26만(Split5 휠 기준; 차체 16만, 센서 4.5만). 예전(10.5만)보다 무겁습니다.
  퀘스트에서 무거우면 LOD(간략 버전)를 따로 만들 수 있습니다.

## 차체 출처
- 차체 판넬은 받은 Genesis G80 2022 모델을 바탕으로 했습니다. 엠블럼, 크레스트 그릴, 번호판, 브랜드 램프 그래픽은 지웠고,
  앞모습(활 모양 후드, 얇은 검은 바이저), 뒤 전폭 램프, 휠, 센서, 실내, 래핑은 새로 만들었습니다.
- **원 모델을 받은 사이트의 라이선스가 수정과 프로젝트 사용을 허용하는지 확인이 필요합니다.**
