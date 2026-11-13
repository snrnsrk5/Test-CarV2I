Shader "OptimizedRoadSigns/Atlas BuiltIn" {
 Properties {
  _MainTex("2K RGB Atlas",2D)="white" {}
  [PerRendererData] _AtlasRect("Atlas Rect",Vector)=(0,0,1,1)
 }
 SubShader {
  Tags {"RenderType"="Opaque" "Queue"="Geometry"}
  LOD 200
  CGPROGRAM
  #pragma surface surf Standard fullforwardshadows addshadow
  #pragma target 3.0
  #pragma multi_compile_instancing
  sampler2D _MainTex;
  struct Input { float2 uv_MainTex; float2 uv2_MainTex; };
  UNITY_INSTANCING_BUFFER_START(Signs)
   UNITY_DEFINE_INSTANCED_PROP(float4, _AtlasRect)
  UNITY_INSTANCING_BUFFER_END(Signs)
  void surf(Input IN, inout SurfaceOutputStandard o) {
   float4 rect = UNITY_ACCESS_INSTANCED_PROP(Signs,_AtlasRect);
   half mask=step(0.5,IN.uv2_MainTex.x);
   half3 artwork=tex2D(_MainTex,IN.uv_MainTex*rect.zw+rect.xy).rgb;
   o.Albedo=lerp(half3(.45,.49,.52),artwork,mask);
   o.Metallic=lerp(.82,.02,mask); o.Smoothness=lerp(.58,.64,mask); o.Occlusion=1; o.Alpha=1;
  }
  ENDCG
 }
 FallBack "Standard"
}
