// 5/6 악천후(113~115): 비 세기 w(0~1)를 시나리오가 정함(경로 자리로) → 비 줄기 입자·젖은 노면(매끈·어둡게)·안개·어두운 하늘·유리 물방울
// 터널·덮개 안에서는 비 입자만 멈춤. 끝나면(플레이 끝) 재질·조명 원래대로
// v17: 터널·지하차도 '비 막는 구역'(PNUDriveRainBlock 상자들, 빌더가 만듦 — 씬에서 옮기거나 늘릴 수 있음)
//   빗방울은 그 상자에 닿으면 사라지고(입자 충돌), 카메라가 상자 안이면 유리 물방울·빗소리 끔 → 입구·출구에서 바로 바뀜
//   하늘: 예전엔 대기 두께를 2.6 으로 올려 비 올 때 노을처럼 주황이 됐음 → 두께는 그대로, 하늘·땅 색을 회색으로, 해 원반 숨김
using System;
using UnityEngine;

namespace PNU.Drive
{
    public class PNUDriveWeather : MonoBehaviour
    {
        public PNUDriveEgo ego; public Camera cam;
        public Material[] wetMats;                     // 젖는 재질(아스팔트·차선·보도·연석 …)
        public Texture2D streakTex, dropsTex;
        public Light sun;
        [Range(0, 1)] public float w;                  // 지금 비 세기
        public bool covered;                           // 터널·지하차도 안
        public bool glassDrops = true;
        public int maxDrops = 4200;
        public PNUDriveRainBlock blockers;             // 비 막는 구역(없으면 시나리오가 구간으로 covered 를 정함)
        public bool HasBlockers { get { return blockers != null && blockers.boxes != null && blockers.boxes.Length > 0; } }
        [NonSerialized] public bool nearPortal;        // 구역 안이지만 입구·출구 가까움(바깥 비가 보임)

        ParticleSystem rain; ParticleSystemRenderer rainR; Material rainMat, dropMat; Transform glass; MeshRenderer glassR;
        float[] sm0; Color[] col0; bool[] hasSm, hasCol; bool saved;
        float sun0, amb0, fog0s, fog0e, sky0e, sky0t, sky0sun; Color fogC0, sunC0, sky0tint, sky0ground; bool fog0; FogMode fogM0; Material sky;
        float wPrev = -1f, scroll, giPrev = -1f;

        void Start()
        {
            if (cam == null) cam = Camera.main;
            if (sun == null) sun = RenderSettings.sun;
            Save();
            // 비 줄기: 카메라 둘레 상자에서 떨어짐(월드 기준)
            var go = new GameObject("비"); go.transform.SetParent(transform, false);
            rain = go.AddComponent<ParticleSystem>(); rainR = go.GetComponent<ParticleSystemRenderer>();
            rain.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = rain.main; main.loop = true; main.playOnAwake = false; main.startLifetime = 1.15f; main.startSpeed = 0f; main.maxParticles = maxDrops;
            main.simulationSpace = ParticleSystemSimulationSpace.World; main.startSize = 0.022f; main.gravityModifier = 0f;
            main.startColor = new Color(0.78f, 0.82f, 0.9f, 0.42f);
            var vel = rain.velocityOverLifetime; vel.enabled = true; vel.space = ParticleSystemSimulationSpace.World;
            vel.x = new ParticleSystem.MinMaxCurve(-0.8f, -0.2f); vel.y = new ParticleSystem.MinMaxCurve(-12.5f, -10.5f); vel.z = new ParticleSystem.MinMaxCurve(-0.3f, 0.3f);
            var em = rain.emission; em.rateOverTime = 0f;
            var sh = rain.shape; sh.enabled = true; sh.shapeType = ParticleSystemShapeType.Box; sh.scale = new Vector3(44f, 0.5f, 52f);
            rainMat = new Material(Shader.Find("Sprites/Default")) { name = "PNU 비 줄기", mainTexture = streakTex };
            rainR.sharedMaterial = rainMat; rainR.renderMode = ParticleSystemRenderMode.Stretch; rainR.velocityScale = 0.055f; rainR.lengthScale = 1.2f;
            rainR.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; rainR.receiveShadows = false; rainR.sortingOrder = 5;
            if (blockers == null) blockers = FindObjectOfType<PNUDriveRainBlock>();
            if (HasBlockers)
            {
                // 비 막는 상자(Ignore Raycast 층)에 닿은 빗방울은 사라짐
                var colm = rain.collision; colm.enabled = true; colm.type = ParticleSystemCollisionType.World; colm.mode = ParticleSystemCollisionMode.Collision3D;
                colm.collidesWith = 1 << PNUDriveRainBlock.Layer; colm.lifetimeLoss = 1f; colm.quality = ParticleSystemCollisionQuality.High;   // 빗방울마다 매 프레임(상자 근처에서만 켬)
                colm.enableDynamicColliders = false; colm.radiusScale = 0.2f; colm.maxCollisionShapes = 32; colm.bounce = 0f; colm.dampen = 1f;
            }
            rain.Play();
            // 유리 물방울(운전석 앞 유리 자리 판)
            if (dropsTex != null)
            {
                var g = GameObject.CreatePrimitive(PrimitiveType.Quad); g.name = "앞유리 물방울";
                var col = g.GetComponent<Collider>(); if (col != null) Destroy(col);
                g.transform.SetParent(transform, false); glass = g.transform; glassR = g.GetComponent<MeshRenderer>();
                dropMat = new Material(Shader.Find("Sprites/Default")) { name = "PNU 유리 물방울", mainTexture = dropsTex };
                dropsTex.wrapMode = TextureWrapMode.Repeat;
                glassR.sharedMaterial = dropMat; glassR.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; glassR.receiveShadows = false;
                g.SetActive(false);
            }
        }

        void Save()
        {
            if (saved) return; saved = true;
            int n = wetMats != null ? wetMats.Length : 0;
            sm0 = new float[n]; col0 = new Color[n]; hasSm = new bool[n]; hasCol = new bool[n];
            for (int i = 0; i < n; i++)
            {
                var m = wetMats[i]; if (m == null) continue;
                if (m.HasProperty("_Smoothness")) { hasSm[i] = true; sm0[i] = m.GetFloat("_Smoothness"); }
                if (m.HasProperty("_BaseColor")) { hasCol[i] = true; col0[i] = m.GetColor("_BaseColor"); }
            }
            if (sun != null) { sun0 = sun.intensity; sunC0 = sun.color; }
            amb0 = RenderSettings.ambientIntensity; fog0 = RenderSettings.fog; fogM0 = RenderSettings.fogMode; fog0s = RenderSettings.fogStartDistance; fog0e = RenderSettings.fogEndDistance; fogC0 = RenderSettings.fogColor;
            sky = RenderSettings.skybox;
            if (sky != null)
            {
                sky0e = sky.HasProperty("_Exposure") ? sky.GetFloat("_Exposure") : 1f; sky0t = sky.HasProperty("_AtmosphereThickness") ? sky.GetFloat("_AtmosphereThickness") : 1f;
                sky0sun = sky.HasProperty("_SunSize") ? sky.GetFloat("_SunSize") : 0.04f;
                sky0tint = sky.HasProperty("_SkyTint") ? sky.GetColor("_SkyTint") : Color.gray; sky0ground = sky.HasProperty("_GroundColor") ? sky.GetColor("_GroundColor") : Color.gray;
            }
        }

        void Restore()
        {
            if (!saved) return;
            ApplyLook(0f, true);
        }
        void OnDisable() { Restore(); }
        void OnApplicationQuit() { Restore(); }

        void Update()
        {
            if (!saved) return;
            float k = Mathf.Clamp01(w);
            if (Mathf.Abs(k - wPrev) > 0.002f) { ApplyLook(k, false); wPrev = k; }
            // 비 막는 구역: 카메라가 상자 안이면 covered(바로), 입구·출구 45m 안이면 바깥 비가 보이게 계속 뿌림(상자에 닿으면 사라짐)
            if (HasBlockers && cam != null)
            {
                float far; covered = blockers.Inside(cam.transform.position, out far);
                nearPortal = covered && far < 45f;
                // 입자 충돌은 상자(터널·지하차도) 가까이에서만(나머지 구간은 계산 안 함)
                if (rain != null)
                {
                    bool need = covered || blockers.PortalDist(cam.transform.position) < 140f;
                    var colm = rain.collision; if (colm.enabled != need) colm.enabled = need;
                }
            }
            // 비 입자: 카메라 위·앞쪽, 차 속도만큼 앞에서 생김
            if (rain != null && cam != null)
            {
                var ct = cam.transform; Vector3 f = ct.forward; f.y = 0f; if (f.sqrMagnitude < 1e-4f) f = Vector3.forward; f.Normalize();
                float v = ego != null ? ego.Speed : 0f;
                rain.transform.SetPositionAndRotation(ct.position + Vector3.up * 11f + f * (8f + v * 0.9f), Quaternion.LookRotation(f, Vector3.up));
                bool emit = !covered || (HasBlockers && nearPortal);
                var em = rain.emission; em.rateOverTime = emit ? maxDrops / 1.15f * k * k : 0f;
            }
            // 유리 물방울(운전석 시점에서만)
            if (glass != null)
            {
                bool show = k > 0.02f && !covered && ego != null && ego.camMode == PNUDriveEgo.CamMode.Driver && ego.car != null;
                if (glass.gameObject.activeSelf != show) glass.gameObject.SetActive(show);
                if (show)
                {
                    var car = ego.car; Vector3 eye = car.InverseTransformPoint(cam.transform.position);
                    Vector3 lp = new Vector3(eye.x * 0.6f, eye.y + 0.1f, eye.z + 0.74f);
                    glass.position = car.TransformPoint(lp); glass.rotation = car.rotation * Quaternion.Euler(-28f, 0f, 0f);
                    glass.localScale = new Vector3(1.55f, 0.62f, 1f);
                    scroll += Time.deltaTime * (0.012f + (ego != null ? ego.Speed : 0f) * 0.0016f);
                    dropMat.mainTextureScale = new Vector2(2.2f, 0.9f); dropMat.mainTextureOffset = new Vector2(0f, scroll);
                    dropMat.color = new Color(1f, 1f, 1f, 0.55f * k);
                }
            }
        }

        void ApplyLook(float k, bool restore)
        {
            int n = wetMats != null ? wetMats.Length : 0;
            for (int i = 0; i < n; i++)
            {
                var m = wetMats[i]; if (m == null) continue;
                if (hasSm[i]) m.SetFloat("_Smoothness", Mathf.Lerp(sm0[i], Mathf.Max(sm0[i], 0.78f), k));
                if (hasCol[i]) { var c = col0[i]; m.SetColor("_BaseColor", new Color(c.r * (1f - 0.42f * k), c.g * (1f - 0.42f * k), c.b * (1f - 0.38f * k), c.a)); }
            }
            if (sun != null) { sun.intensity = sun0 * (1f - 0.62f * k); sun.color = Color.Lerp(sunC0, new Color(0.72f, 0.78f, 0.9f), k); }
            RenderSettings.ambientIntensity = amb0 * (1f - 0.42f * k);
            if (k > 0.001f && !restore)
            {
                RenderSettings.fog = true; RenderSettings.fogMode = FogMode.Linear;
                RenderSettings.fogStartDistance = Mathf.Lerp(fog0s, 18f, k); RenderSettings.fogEndDistance = Mathf.Lerp(fog0e, 420f, k);
                RenderSettings.fogColor = Color.Lerp(fogC0, new Color(0.5f, 0.54f, 0.6f), k);
            }
            else { RenderSettings.fog = fog0; RenderSettings.fogMode = fogM0; RenderSettings.fogStartDistance = fog0s; RenderSettings.fogEndDistance = fog0e; RenderSettings.fogColor = fogC0; }
            if (sky != null)
            {
                // 흐린 하늘: 밝기 낮추고 하늘·땅 색을 회색으로, 해 원반 숨김. 대기 두께는 그대로(올리면 노을처럼 주황)
                if (sky.HasProperty("_Exposure")) sky.SetFloat("_Exposure", sky0e * (1f - 0.45f * k));
                if (sky.HasProperty("_AtmosphereThickness")) sky.SetFloat("_AtmosphereThickness", sky0t);
                if (sky.HasProperty("_SkyTint")) sky.SetColor("_SkyTint", Color.Lerp(sky0tint, new Color(0.4f, 0.42f, 0.46f), k));
                if (sky.HasProperty("_GroundColor")) sky.SetColor("_GroundColor", Color.Lerp(sky0ground, new Color(0.28f, 0.29f, 0.31f), k));
                if (sky.HasProperty("_SunSize")) sky.SetFloat("_SunSize", Mathf.Lerp(sky0sun, 0f, k));
            }
            // 하늘로 정하는 주변광도 따라 바뀌게(가끔만)
            if (restore || Mathf.Abs(k - giPrev) > 0.08f || (k == 0f && giPrev != 0f)) { giPrev = k; DynamicGI.UpdateEnvironment(); }
        }
    }
}
