Shader "OptimizedRoadSigns/Atlas URP" {
 Properties {
  _MainTex("2K RGB Atlas",2D)="white" {}
  [PerRendererData] _AtlasRect("Atlas Rect",Vector)=(0,0,1,1)
 }
 SubShader {
  PackageRequirements { "com.unity.render-pipelines.universal": "12.0" }
  Tags {"RenderPipeline"="UniversalPipeline" "RenderType"="Opaque" "Queue"="Geometry"}
  Pass {
   Name "ForwardLit"
   Tags {"LightMode"="UniversalForward"}
   HLSLPROGRAM
   #pragma vertex vert
   #pragma fragment frag
   #pragma target 3.0
   #pragma multi_compile_instancing
   #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
   #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
   #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
   #pragma multi_compile_fragment _ _SHADOWS_SOFT
   #pragma multi_compile_fog
   #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
   #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
   TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);
   UNITY_INSTANCING_BUFFER_START(Signs)
    UNITY_DEFINE_INSTANCED_PROP(float4, _AtlasRect)
   UNITY_INSTANCING_BUFFER_END(Signs)
   struct Attributes {float4 positionOS:POSITION; float3 normalOS:NORMAL; float2 uv:TEXCOORD0; float2 surface:TEXCOORD1; UNITY_VERTEX_INPUT_INSTANCE_ID};
   struct Varyings {float4 positionCS:SV_POSITION; float3 positionWS:TEXCOORD0; half3 normalWS:TEXCOORD1; float2 uv:TEXCOORD2; half mask:TEXCOORD3; half fog:TEXCOORD4; UNITY_VERTEX_INPUT_INSTANCE_ID UNITY_VERTEX_OUTPUT_STEREO};
   Varyings vert(Attributes IN) {
    Varyings OUT=(Varyings)0; UNITY_SETUP_INSTANCE_ID(IN); UNITY_TRANSFER_INSTANCE_ID(IN,OUT); UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(OUT);
    VertexPositionInputs pos=GetVertexPositionInputs(IN.positionOS.xyz);
    OUT.positionCS=pos.positionCS; OUT.positionWS=pos.positionWS;
    OUT.normalWS=TransformObjectToWorldNormal(IN.normalOS);OUT.uv=IN.uv;OUT.mask=IN.surface.x;OUT.fog=ComputeFogFactor(pos.positionCS.z);return OUT;
   }
   half4 frag(Varyings IN):SV_Target {
    UNITY_SETUP_INSTANCE_ID(IN);UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(IN);
    float4 rect=UNITY_ACCESS_INSTANCED_PROP(Signs,_AtlasRect);half mask=step(.5,IN.mask);
    half3 artwork=SAMPLE_TEXTURE2D(_MainTex,sampler_MainTex,IN.uv*rect.zw+rect.xy).rgb;
    SurfaceData surface=(SurfaceData)0;
    surface.albedo=lerp(half3(.45,.49,.52),artwork,mask);surface.metallic=lerp(.82,.02,mask);
    surface.smoothness=lerp(.58,.64,mask);surface.normalTS=half3(0,0,1);surface.occlusion=1;surface.alpha=1;
    InputData data=(InputData)0;data.positionWS=IN.positionWS;data.normalWS=normalize(IN.normalWS);
    data.viewDirectionWS=GetWorldSpaceNormalizeViewDir(IN.positionWS);data.shadowCoord=TransformWorldToShadowCoord(IN.positionWS);
    data.bakedGI=SampleSH(data.normalWS);data.vertexLighting=VertexLighting(IN.positionWS,data.normalWS);
    data.normalizedScreenSpaceUV=GetNormalizedScreenSpaceUV(IN.positionCS);data.shadowMask=half4(1,1,1,1);
    half4 color=UniversalFragmentPBR(data,surface);color.rgb=MixFog(color.rgb,IN.fog);return color;
   }
   ENDHLSL
  }
  UsePass "Universal Render Pipeline/Lit/ShadowCaster"
  UsePass "Universal Render Pipeline/Lit/DepthOnly"
 }
 FallBack "OptimizedRoadSigns/Atlas BuiltIn"
}
