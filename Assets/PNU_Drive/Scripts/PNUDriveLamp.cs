// 신호등 하나(v13): 후배 신호등 꾸러미(TrafficLightSystem)의 조립 신호등을 신호 계획대로 켬
//   차량: TrafficLight_Modular(TrafficLightController) — 0 빨강, 1 노랑, 2 초록, 3 빨강+좌회전 화살표
//   보행: 같은 기둥의 보행 등(TrafficLightController.SetPedestrian) 또는 따로 선 보행 기둥(PedestrianSignal 의 PedestrianSignalController)
//   상태가 바뀔 때만 부름(꾸러미는 같은 상태의 재질을 함께 씀)
using UnityEngine;
using ModularTrafficLights;

namespace PNU.Drive
{
    public class PNUDriveLamp : MonoBehaviour
    {
        public int junction, group;
        [Tooltip("보행 등이 가로지르는 차 무리(횡단보도가 놓인 팔의 무리)")] public int pedGroup;
        [Tooltip("따로 선 보행 기둥")] public bool pedestrian;
        [Tooltip("차량 기둥에 보행 등도 달림")] public bool hasPed;
        public TrafficLightController vehicle;
        [Tooltip("보조신호등(정지선 쪽을 보는 작은 등)")] public SignalLightController aux;
        public PedestrianSignalController ped;
        int last = -99, lastNum = -99; bool lastWalk; bool pedInit;

        public void SetCar(int aspect)
        {
            if (aspect == last || vehicle == null) return; last = aspect;
            VehicleAspect a = aspect == 0 ? VehicleAspect.Red : aspect == 1 ? VehicleAspect.Yellow : aspect == 3 ? VehicleAspect.RedAndLeft : VehicleAspect.Green;
            vehicle.SetVehicleAspect(a);
            if (aux != null) aux.SetAspect(a);
        }

        /// <summary>보행: 걷기·남은 초(숫자 또는 막대 비율). 꾸러미의 SetState 는 초록을 끄면 빨강이 켜지므로 깜빡임 대신 남은 초만 줄어듦</summary>
        public void SetPed(bool walk, float remain, float frac, float t)
        {
            int num = walk ? Mathf.Clamp(Mathf.CeilToInt(remain), 0, 99) : 0;
            int total = walk ? Mathf.Clamp(Mathf.RoundToInt(remain / Mathf.Max(0.02f, frac)), 1, 99) : 30;
            bool shown = walk;
            if (pedInit && shown == lastWalk && num == lastNum) return;
            pedInit = true; lastWalk = shown; lastNum = num;
            if (ped != null) ped.SetState(shown, num, total);
            else if (vehicle != null && hasPed) vehicle.SetPedestrian(shown, num, total);
        }
    }
}
