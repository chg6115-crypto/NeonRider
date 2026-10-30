#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Overlays;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UIElements;

/// <summary>
/// Built-in → URP/Lit 머티리얼 변환 (원래 설정 유지)
/// 사용법 : Project 창에서 머티리얼 또는 폴더 선택 → Scene 뷰 "URP Preserve" 오버레이의 버튼
///
/// VARCO URP Converter와 다른 점
/// - Metallic, Smoothness, Emission(색·맵·켜짐 여부)을 원래 값 그대로 옮김
/// - Rendering Mode 유지 : Opaque / Cutout(Alpha Clipping) / Fade(Alpha) / Transparent(Premultiply)
/// - Specular 방식 머티리얼은 URP Specular 워크플로로 옮김
/// - Detail 맵 옮김
/// - Standard를 복제한 커스텀 셰이더(예: DoubleSided/Metalic)도 Standard처럼 변환, 양면이면 Render Face = Both
/// - HDRP/Lit 머티리얼도 변환 (Mask Map → Metallic·Occlusion 맵, 투명·Cutout·양면 유지)
/// - 이미 URP인 머티리얼, 그 외 커스텀 셰이더, Unlit·파티클·UI·스카이박스는 건너뜀 (덮어쓰지 않음)
/// - FBX 등 모델 파일 안의 머티리얼(읽기 전용)은 건너뜀 → 모델 Inspector의 Extract Materials로 꺼낸 뒤 변환
/// </summary>
[Overlay(typeof(SceneView), "URPConverterPreserve", "URP Preserve", true)]
public class URPConverterPreserveOverlay : Overlay
{
    const string UrpLitShaderName = "Universal Render Pipeline/Lit";

    public override VisualElement CreatePanelContent()
    {
        var root = new VisualElement();
        root.style.flexDirection = FlexDirection.Row;
        root.style.paddingLeft = 6;
        root.style.paddingRight = 6;

        var button = new Button(ConvertSelected)
        {
            text = "Convert to URP (Preserve)",
            tooltip = "선택한 머티리얼/폴더의 Built-in 머티리얼을 URP/Lit으로 변환.\n" +
                      "Metallic, Emission, 투명·Cutout 설정을 유지하고, 이미 URP인 머티리얼은 건너뜀"
        };
        button.style.paddingLeft = 6;
        button.style.paddingRight = 6;
        button.style.backgroundColor = new Color(0.15f, 0.6f, 0.45f);
        button.style.color = Color.white;
        root.Add(button);
        return root;
    }

    // ---------------- 변환 ----------------
    static void ConvertSelected()
    {
        Material[] mats = Selection.GetFiltered<Material>(SelectionMode.DeepAssets);
        if (mats == null || mats.Length == 0)
        {
            Debug.LogWarning("[URP Preserve] Project 창에서 머티리얼 또는 폴더를 선택하세요.");
            return;
        }

        Shader lit = Shader.Find(UrpLitShaderName);
        if (lit == null)
        {
            Debug.LogError($"[URP Preserve] 셰이더를 찾을 수 없습니다: {UrpLitShaderName}");
            return;
        }

        int converted = 0, alreadyUrp = 0, unsupported = 0, embedded = 0;
        var done = new HashSet<Material>();

        foreach (var m in mats)
        {
            if (m == null || !done.Add(m)) continue;

            // FBX 등 모델 파일 안에 들어 있는 머티리얼은 읽기 전용 (바꿔도 저장되지 않고 재임포트 시 원래대로 돌아감)
            string matPath = AssetDatabase.GetAssetPath(m);
            if (!matPath.EndsWith(".mat", System.StringComparison.OrdinalIgnoreCase))
            {
                embedded++;
                Debug.LogWarning($"[URP Preserve] 건너뜀 (모델 파일 안의 머티리얼, 먼저 Extract Materials 필요): {m.name} / {matPath}", m);
                continue;
            }

            var kind = Classify(m);
            if (kind == SourceKind.AlreadyUrp) { alreadyUrp++; continue; }
            if (kind == SourceKind.Unsupported)
            {
                unsupported++;
                Debug.LogWarning($"[URP Preserve] 건너뜀 (지원하지 않는 셰이더): {m.name} / {(m.shader ? m.shader.name : "null")}", m);
                continue;
            }

            var snap = kind == SourceKind.Hdrp ? Snapshot.CaptureHdrp(m) : Snapshot.Capture(m, kind);
            Undo.RecordObject(m, "Convert Material to URP/Lit (Preserve)");
            m.shader = lit;
            snap.Apply(m);
            EditorUtility.SetDirty(m);
            converted++;
        }

        AssetDatabase.SaveAssets();
        Debug.Log($"[URP Preserve] 완료 : 변환 {converted}개 / 이미 URP {alreadyUrp}개 건너뜀 / 지원 안 함 {unsupported}개 건너뜀" +
                  (embedded > 0 ? $" / 모델 파일 안의 머티리얼 {embedded}개 건너뜀 (모델 Inspector > Materials > Extract Materials 후 다시 실행)" : ""));
    }

    enum SourceKind { AlreadyUrp, Unsupported, Standard, StandardSpecular, Legacy, Hdrp }

    static SourceKind Classify(Material m)
    {
        if (m.shader == null) return SourceKind.Unsupported;
        string n = m.shader.name;

        // HDRP/Lit 머티리얼 : HDRP가 없는 프로젝트에서는 셰이더가 에러 셰이더로 바뀌지만,
        // 파일 안에 HDRP 프로퍼티(_BaseColorMap, _MaskMap 등)는 그대로 남아 있으므로 그 값으로 변환
        if (n.StartsWith("HDRP/Lit") || n == "Hidden/InternalErrorShader")
        {
            var saved = new SavedProps(m);
            if (saved.Has("_BaseColorMap") || saved.Has("_MaskMap")) return SourceKind.Hdrp;
            return SourceKind.Unsupported;
        }

        if (n.StartsWith("Universal Render Pipeline/") && !n.Contains("/2D/")) return SourceKind.AlreadyUrp;
        if (n.StartsWith("Universal Render Pipeline/2D/Mesh2D-Lit")) return SourceKind.Standard;   // 잘못 지정된 2D Lit

        // 커스텀 셰이더 (프로젝트 안에 .shader/.shadergraph 파일이 있는 것)
        // - Standard를 복제한 셰이더(예: "DoubleSided/Metalic")는 프로퍼티가 같아서 Standard처럼 변환
        // - 그 외 커스텀 셰이더는 건드리지 않음
        string path = AssetDatabase.GetAssetPath(m.shader);
        bool builtin = string.IsNullOrEmpty(path) || path == "Resources/unity_builtin_extra";
        if (!builtin)
        {
            if (LooksLikeStandard(m))
                return m.HasProperty("_SpecGlossMap") ? SourceKind.StandardSpecular : SourceKind.Standard;
            return SourceKind.Unsupported;
        }

        if (n == "Standard") return SourceKind.Standard;
        if (n == "Standard (Specular setup)") return SourceKind.StandardSpecular;
        if (n == "Autodesk Interactive") return SourceKind.Standard;

        bool excluded = n.Contains("Unlit") || n.Contains("Particles") || n.StartsWith("UI/") ||
                        n.StartsWith("Skybox") || n.StartsWith("Sprites") || n.StartsWith("Hidden") ||
                        n.Contains("Reflective") || n.Contains("Self-Illumin") || n.StartsWith("Nature/");
        if (excluded) return SourceKind.Unsupported;

        if (n.StartsWith("Legacy Shaders/") || n.StartsWith("Mobile/")) return SourceKind.Legacy;
        return SourceKind.Unsupported;
    }

    // Standard 셰이더를 복제해 만든 커스텀 셰이더인지 (Standard 고유 프로퍼티 조합으로 판단)
    static bool LooksLikeStandard(Material m)
    {
        return m.HasProperty("_MainTex") && m.HasProperty("_Glossiness") && m.HasProperty("_Mode");
    }

    // 양면 렌더링 셰이더인지 (이름 또는 _Cull 값으로 판단)
    static bool IsDoubleSided(Material m)
    {
        string n = m.shader != null ? m.shader.name.Replace(" ", "").ToLowerInvariant() : "";
        if (n.Contains("doublesided") || n.Contains("twosided")) return true;
        return m.HasProperty("_Cull") && Mathf.RoundToInt(m.GetFloat("_Cull")) == 0;
    }

    // ---------------- 값 보관 ----------------
    struct Snapshot
    {
        // 0 Opaque, 1 Cutout, 2 Fade, 3 Transparent
        public int mode;
        public bool specularWorkflow;
        public bool doubleSided;

        public Texture baseMap; public Vector2 baseScale, baseOffset; public Color baseColor;
        public Texture normalMap; public float normalScale;
        public float metallic; public Texture metallicMap;
        public Color specColor; public Texture specMap;
        public float smoothness; public float smoothnessChannel;   // 0 = Metallic/Specular 알파, 1 = Albedo 알파
        public Texture occlusionMap; public float occlusionStrength;
        public Texture heightMap; public float heightScale;
        public bool emissionOn; public Color emissionColor; public Texture emissionMap;
        public MaterialGlobalIlluminationFlags giFlags;
        public float cutoff;
        public Texture detailMask, detailAlbedo, detailNormal; public float detailNormalScale;
        public Vector2 detailScale, detailOffset;

        public static Snapshot Capture(Material m, SourceKind kind)
        {
            var s = new Snapshot();
            string n = m.shader.name;
            string mainTex = m.HasProperty("_MainTex") ? "_MainTex" : "_BaseMap";
            string mainCol = m.HasProperty("_Color") ? "_Color" : "_BaseColor";

            s.baseMap = Tex(m, mainTex);
            s.baseScale = m.HasProperty(mainTex) ? m.GetTextureScale(mainTex) : Vector2.one;
            s.baseOffset = m.HasProperty(mainTex) ? m.GetTextureOffset(mainTex) : Vector2.zero;
            s.baseColor = m.HasProperty(mainCol) ? m.GetColor(mainCol) : Color.white;

            s.normalMap = Tex(m, "_BumpMap");
            s.normalScale = F(m, "_BumpScale", 1f);
            s.occlusionMap = Tex(m, "_OcclusionMap");
            s.occlusionStrength = F(m, "_OcclusionStrength", 1f);
            s.heightMap = Tex(m, "_ParallaxMap");
            s.heightScale = F(m, "_Parallax", 0.005f);
            s.cutoff = F(m, "_Cutoff", 0.5f);
            s.smoothnessChannel = F(m, "_SmoothnessTextureChannel", 0f);

            s.detailMask = Tex(m, "_DetailMask");
            s.detailAlbedo = Tex(m, "_DetailAlbedoMap");
            s.detailNormal = Tex(m, "_DetailNormalMap");
            s.detailNormalScale = F(m, "_DetailNormalMapScale", 1f);
            s.detailScale = m.HasProperty("_DetailAlbedoMap") ? m.GetTextureScale("_DetailAlbedoMap") : Vector2.one;
            s.detailOffset = m.HasProperty("_DetailAlbedoMap") ? m.GetTextureOffset("_DetailAlbedoMap") : Vector2.zero;

            // Emission : 원래 켜져 있던 경우만 유지
            s.emissionColor = m.HasProperty("_EmissionColor") ? m.GetColor("_EmissionColor") : Color.black;
            s.emissionMap = Tex(m, "_EmissionMap");
            s.emissionOn = m.IsKeywordEnabled("_EMISSION") &&
                           (s.emissionMap != null || s.emissionColor.maxColorComponent > 0.0001f);
            s.giFlags = m.globalIlluminationFlags;
            s.doubleSided = IsDoubleSided(m);

            switch (kind)
            {
                case SourceKind.Standard:
                    s.mode = Mathf.RoundToInt(F(m, "_Mode", 0f));
                    s.metallic = F(m, "_Metallic", 0f);
                    s.metallicMap = Tex(m, "_MetallicGlossMap");
                    // Standard : 맵이 있으면 _GlossMapScale, 없으면 _Glossiness
                    s.smoothness = s.metallicMap != null || s.smoothnessChannel > 0.5f
                        ? F(m, "_GlossMapScale", F(m, "_Glossiness", 0.5f))
                        : F(m, "_Glossiness", 0.5f);
                    break;

                case SourceKind.StandardSpecular:
                    s.specularWorkflow = true;
                    s.mode = Mathf.RoundToInt(F(m, "_Mode", 0f));
                    s.specColor = m.HasProperty("_SpecColor") ? m.GetColor("_SpecColor") : new Color(0.2f, 0.2f, 0.2f);
                    s.specMap = Tex(m, "_SpecGlossMap");
                    s.smoothness = s.specMap != null || s.smoothnessChannel > 0.5f
                        ? F(m, "_GlossMapScale", F(m, "_Glossiness", 0.5f))
                        : F(m, "_Glossiness", 0.5f);
                    break;

                case SourceKind.Legacy:
                    s.mode = n.Contains("Cutout") ? 1 : n.Contains("Transparent") ? 2 : 0;
                    bool spec = n.Contains("Specular");
                    s.specularWorkflow = spec;
                    s.metallic = 0f;
                    s.specColor = spec && m.HasProperty("_SpecColor") ? m.GetColor("_SpecColor") : Color.black;
                    s.smoothness = spec ? F(m, "_Shininess", 0.5f) : 0f;
                    break;
            }
            return s;
        }

        // HDRP/Lit → URP/Lit
        // HDRP Mask Map = R 금속성, G AO, B 디테일 마스크, A 스무스니스
        // → URP Metallic 맵(R 금속성, A 스무스니스)과 Occlusion 맵(G)에 그대로 연결하면 같은 결과
        public static Snapshot CaptureHdrp(Material m)
        {
            var p = new SavedProps(m);
            var s = new Snapshot();

            s.baseMap = p.GetTex("_BaseColorMap", out s.baseScale, out s.baseOffset);
            s.baseColor = p.GetColor("_BaseColor", Color.white);

            Texture mask = p.GetTex("_MaskMap", out _, out _);
            if (mask != null)
            {
                s.metallicMap = mask;
                s.metallic = 1f;
                s.smoothness = p.GetFloat("_SmoothnessRemapMax", 1f);
                s.occlusionMap = mask;
                s.occlusionStrength = 1f;
            }
            else
            {
                s.metallic = p.GetFloat("_Metallic", 0f);
                s.smoothness = p.GetFloat("_Smoothness", 0.5f);
                s.occlusionStrength = 1f;
            }

            s.normalMap = p.GetTex("_NormalMap", out _, out _);
            s.normalScale = p.GetFloat("_NormalScale", 1f);
            s.heightScale = 0.005f;
            s.detailNormalScale = 1f;
            s.detailScale = Vector2.one;

            // Emission : HDRP는 물리 단위(밝기 값이 매우 큼)라 LDR 색을 사용. 밝기는 변환 후 조정
            s.emissionMap = p.GetTex("_EmissiveColorMap", out _, out _);
            Color ldr = p.GetColor("_EmissiveColorLDR", Color.black);
            if (ldr.maxColorComponent <= 0.0001f)
            {
                Color hdr = p.GetColor("_EmissiveColor", Color.black);
                float max = hdr.maxColorComponent;
                ldr = max > 1f ? hdr / max : hdr;
            }
            s.emissionColor = ldr;
            s.emissionOn = s.emissionColor.maxColorComponent > 0.0001f;
            s.giFlags = MaterialGlobalIlluminationFlags.BakedEmissive;

            // 투명·Cutout·양면
            bool transparent = p.GetFloat("_SurfaceType", 0f) > 0.5f;
            bool clip = p.GetFloat("_AlphaCutoffEnable", 0f) > 0.5f;
            int blend = Mathf.RoundToInt(p.GetFloat("_BlendMode", 0f));   // 0 Alpha, 1 Additive, 4 Premultiply
            s.mode = transparent ? (blend == 4 ? 3 : 2) : (clip ? 1 : 0);
            s.cutoff = p.GetFloat("_AlphaCutoff", 0.5f);
            s.doubleSided = p.GetFloat("_DoubleSidedEnable", 0f) > 0.5f;
            return s;
        }

        public void Apply(Material m)
        {
            // 텍스처·색
            m.SetTexture("_BaseMap", baseMap);
            m.SetTextureScale("_BaseMap", baseScale);
            m.SetTextureOffset("_BaseMap", baseOffset);
            m.SetColor("_BaseColor", baseColor);
            // 원래 프로퍼티도 맞춰 둠 (스크립트가 _MainTex/_Color를 참조하는 경우 대비)
            m.SetTexture("_MainTex", baseMap);
            m.SetColor("_Color", baseColor);

            m.SetTexture("_BumpMap", normalMap);
            m.SetFloat("_BumpScale", normalScale);
            Kw(m, "_NORMALMAP", normalMap != null || detailNormal != null);

            // 워크플로
            m.SetFloat("_WorkflowMode", specularWorkflow ? 0f : 1f);
            Kw(m, "_SPECULAR_SETUP", specularWorkflow);
            if (specularWorkflow)
            {
                m.SetColor("_SpecColor", specColor);
                m.SetTexture("_SpecGlossMap", specMap);
                Kw(m, "_METALLICSPECGLOSSMAP", specMap != null);
            }
            else
            {
                m.SetFloat("_Metallic", metallic);
                m.SetTexture("_MetallicGlossMap", metallicMap);
                Kw(m, "_METALLICSPECGLOSSMAP", metallicMap != null);
            }
            m.SetFloat("_Smoothness", smoothness);
            m.SetFloat("_SmoothnessTextureChannel", smoothnessChannel);
            Kw(m, "_SMOOTHNESS_TEXTURE_ALBEDO_CHANNEL_A", smoothnessChannel > 0.5f);

            m.SetTexture("_OcclusionMap", occlusionMap);
            m.SetFloat("_OcclusionStrength", occlusionStrength);
            Kw(m, "_OCCLUSIONMAP", occlusionMap != null);

            m.SetTexture("_ParallaxMap", heightMap);
            m.SetFloat("_Parallax", heightScale);
            Kw(m, "_PARALLAXMAP", heightMap != null);

            // Detail
            m.SetTexture("_DetailMask", detailMask);
            m.SetTexture("_DetailAlbedoMap", detailAlbedo);
            m.SetTextureScale("_DetailAlbedoMap", detailScale);
            m.SetTextureOffset("_DetailAlbedoMap", detailOffset);
            m.SetTexture("_DetailNormalMap", detailNormal);
            m.SetFloat("_DetailNormalMapScale", detailNormalScale);
            Kw(m, "_DETAIL_MULX2", detailAlbedo != null || detailNormal != null);

            // Emission : 원래 값 그대로
            m.SetColor("_EmissionColor", emissionColor);
            m.SetTexture("_EmissionMap", emissionMap);
            Kw(m, "_EMISSION", emissionOn);
            m.globalIlluminationFlags = emissionOn
                ? (giFlags & ~MaterialGlobalIlluminationFlags.EmissiveIsBlack)
                : MaterialGlobalIlluminationFlags.EmissiveIsBlack;

            // 투명·Cutout
            ApplySurface(m);

            // 양면 렌더링 (URP Lit의 Render Face : 0 Both, 2 Front)
            m.SetFloat("_Cull", doubleSided ? 0f : 2f);
            m.doubleSidedGI = doubleSided;
        }

        void ApplySurface(Material m)
        {
            bool clip = mode == 1;
            bool transparent = mode == 2 || mode == 3;
            bool premultiply = mode == 3;   // Standard "Transparent" = 반사는 남고 표면만 투명 (유리)

            m.SetFloat("_Cutoff", cutoff);
            m.SetFloat("_AlphaClip", clip ? 1f : 0f);
            Kw(m, "_ALPHATEST_ON", clip);

            m.SetFloat("_Surface", transparent ? 1f : 0f);
            Kw(m, "_SURFACE_TYPE_TRANSPARENT", transparent);
            Kw(m, "_ALPHAPREMULTIPLY_ON", transparent && premultiply);

            if (transparent)
            {
                m.SetFloat("_Blend", premultiply ? 1f : 0f);   // 0 Alpha, 1 Premultiply
                m.SetFloat("_SrcBlend", premultiply ? (float)BlendMode.One : (float)BlendMode.SrcAlpha);
                m.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
                m.SetFloat("_SrcBlendAlpha", (float)BlendMode.One);
                m.SetFloat("_DstBlendAlpha", (float)BlendMode.OneMinusSrcAlpha);
                m.SetFloat("_ZWrite", 0f);
                m.SetOverrideTag("RenderType", "Transparent");
                m.renderQueue = (int)RenderQueue.Transparent;
                m.SetShaderPassEnabled("DepthOnly", false);
            }
            else
            {
                m.SetFloat("_Blend", 0f);
                m.SetFloat("_SrcBlend", (float)BlendMode.One);
                m.SetFloat("_DstBlend", (float)BlendMode.Zero);
                m.SetFloat("_SrcBlendAlpha", (float)BlendMode.One);
                m.SetFloat("_DstBlendAlpha", (float)BlendMode.Zero);
                m.SetFloat("_ZWrite", 1f);
                m.SetOverrideTag("RenderType", clip ? "TransparentCutout" : "Opaque");
                m.renderQueue = clip ? (int)RenderQueue.AlphaTest : -1;
                m.SetShaderPassEnabled("DepthOnly", true);
            }
            m.SetShaderPassEnabled("ShadowCaster", true);
        }

        static Texture Tex(Material m, string p) => m.HasProperty(p) ? m.GetTexture(p) : null;
        static float F(Material m, string p, float fallback) => m.HasProperty(p) ? m.GetFloat(p) : fallback;
        static void Kw(Material m, string k, bool on) { if (on) m.EnableKeyword(k); else m.DisableKeyword(k); }
    }

    // 머티리얼 파일에 저장된 값을 셰이더와 상관없이 읽기 (셰이더가 없어진 HDRP 머티리얼용)
    class SavedProps
    {
        readonly SerializedObject so;
        public SavedProps(Material m) { so = new SerializedObject(m); }

        SerializedProperty Find(string section, string name)
        {
            var arr = so.FindProperty("m_SavedProperties." + section);
            if (arr == null || !arr.isArray) return null;
            for (int i = 0; i < arr.arraySize; i++)
            {
                var e = arr.GetArrayElementAtIndex(i);
                var first = e.FindPropertyRelative("first");
                if (first != null && first.stringValue == name) return e.FindPropertyRelative("second");
            }
            return null;
        }

        public bool Has(string name) => Find("m_TexEnvs", name) != null;

        public Texture GetTex(string name, out Vector2 scale, out Vector2 offset)
        {
            scale = Vector2.one; offset = Vector2.zero;
            var p = Find("m_TexEnvs", name);
            if (p == null) return null;
            var sc = p.FindPropertyRelative("m_Scale");
            var of = p.FindPropertyRelative("m_Offset");
            if (sc != null) scale = sc.vector2Value;
            if (of != null) offset = of.vector2Value;
            var t = p.FindPropertyRelative("m_Texture");
            return t != null ? t.objectReferenceValue as Texture : null;
        }

        public Color GetColor(string name, Color fallback)
        {
            var p = Find("m_Colors", name);
            return p != null ? p.colorValue : fallback;
        }

        public float GetFloat(string name, float fallback)
        {
            var p = Find("m_Floats", name);
            return p != null ? p.floatValue : fallback;
        }
    }
}
#endif
