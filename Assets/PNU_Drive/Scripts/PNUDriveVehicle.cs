// 4/6 NPC 차 한 대(프리팹 뿌리, 빌더가 PNU_Vehicles.fbx 로 만듦): 앞 = +Z, 원점 = 앞뒤 바퀴축 가운데 노면
//   몸체·바퀴·LOD1 = 팔레트 재질 한 장(차 색마다 팔레트 그림이 다름), 브레이크등·방향지시등 면은 따로(켜질 때만 보임)
//   나타나기·사라지기: 투명 팔레트 재질 + _BaseColor 알파(MaterialPropertyBlock)
using UnityEngine;

namespace PNU.Drive
{
    public class PNUDriveVehicle : MonoBehaviour
    {
        public string kind = "car";
        public float length = 4.5f, width = 1.9f, height = 1.5f, wheelbase = 2.7f, wheelR = 0.33f, weight = 1f;
        [Tooltip("택시·버스·트럭처럼 색이 정해진 차(팔레트 0번만)")] public bool fixedPaint;
        public Transform[] wheels; public bool[] wheelFront;
        [Tooltip("팔레트 재질을 쓰는 렌더러(몸체·바퀴·LOD1)")] public Renderer[] paintRenderers;
        public Renderer lightL, lightR, blinkL, blinkR;
        public LODGroup lod;

        Quaternion[] baseRot; float spin, steer; bool faded = true; MaterialPropertyBlock mpb;
        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        Material curMat;

        public void Init()
        {
            if (baseRot != null) return;
            if (wheels == null) wheels = new Transform[0];
            baseRot = new Quaternion[wheels.Length];       // 바퀴 회전(차 뿌리 기준)
            for (int i = 0; i < wheels.Length; i++) if (wheels[i] != null) baseRot[i] = Quaternion.Inverse(transform.rotation) * wheels[i].rotation;
            mpb = new MaterialPropertyBlock();
        }

        /// <summary>색·투명도: 다 보이면 불투명 재질, 나타나거나 사라지는 중이면 투명 재질 + 알파</summary>
        public void SetLook(Material opaque, Material fade, float alpha)
        {
            Init();
            bool f = alpha < 0.995f && fade != null;
            var m = f ? fade : opaque;
            if (m != curMat)
            {
                curMat = m;
                if (paintRenderers != null) foreach (var r in paintRenderers) if (r != null) r.sharedMaterial = m;
            }
            if (f)
            {
                mpb.SetColor(BaseColorId, new Color(1f, 1f, 1f, Mathf.Clamp01(alpha)));
                foreach (var r in paintRenderers) if (r != null) r.SetPropertyBlock(mpb);
                faded = true;
            }
            else if (faded)
            {
                foreach (var r in paintRenderers) if (r != null) r.SetPropertyBlock(null);
                faded = false;
            }
        }

        /// <summary>등: 브레이크(뒷등 빨강), 방향지시(깜빡이 면이 없으면 그쪽 뒷등을 주황으로). 다 보일 때만</summary>
        public void SetLights(bool brake, int blinkSide, bool blinkPhase, bool visible, Material brakeMat, Material blinkMat)
        {
            bool bl = visible && blinkPhase && (blinkSide < 0 || blinkSide == 2), br = visible && blinkPhase && blinkSide > 0;   // 2 = 비상등(양쪽)
            if (blinkL != null) Lamp(blinkL, bl, blinkMat);
            if (blinkR != null) Lamp(blinkR, br, blinkMat);
            bool useRearL = blinkL == null && bl, useRearR = blinkR == null && br;
            if (lightL != null) Lamp(lightL, useRearL || (visible && brake), useRearL ? blinkMat : brakeMat);
            if (lightR != null) Lamp(lightR, useRearR || (visible && brake), useRearR ? blinkMat : brakeMat);
        }

        static void Lamp(Renderer r, bool on, Material m)
        {
            if (r.enabled != on) r.enabled = on;
            if (on && m != null && r.sharedMaterial != m) r.sharedMaterial = m;
        }

        /// <summary>바퀴: ds 만큼 굴림(앞바퀴는 steerDeg 만큼 꺾음)</summary>
        public void Roll(float ds, float steerDeg)
        {
            Init();
            spin = Mathf.Repeat(spin + ds / Mathf.Max(0.2f, wheelR) * Mathf.Rad2Deg, 360f);
            steer = Mathf.Lerp(steer, steerDeg, 0.25f);
            for (int i = 0; i < wheels.Length; i++)
            {
                var w = wheels[i]; if (w == null) continue;
                bool front = wheelFront != null && i < wheelFront.Length && wheelFront[i];
                // 차 뿌리 기준: 꺾기(위 축) → 굴리기(차 오른쪽 축, 앞으로 갈 때 위가 앞으로) → 원래 바퀴 방향
                w.rotation = transform.rotation * (front ? Quaternion.Euler(0f, steer, 0f) : Quaternion.identity) * Quaternion.Euler(spin, 0f, 0f) * baseRot[i];
            }
        }
    }
}
