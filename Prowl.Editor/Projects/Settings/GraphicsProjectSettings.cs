
using Infinity.Editor.GUI;
using Infinity.Editor.Inspector;
using Infinity.Editor.Projects.Settings;
using Infinity.Editor.Theming;
using Infinity.OrigamiUI;
using Infinity.PaperUI;
using Infinity.Runtime.Rendering.Pipelines;
using Infinity.Runtime.Rendering.Pipelines.Infinity;

namespace Infinity.Editor.Projects.Settings;

[ProjectSettings("Graphics", EditorIcons.GaugeHigh, order: 24, exportToBuild: true)]
public sealed class GraphicsProjectSettings : ProjectSettingsBase
{
    // Prowl Legacy é o padrão.
    public RenderPipelineType Pipeline = RenderPipelineType.Legacy;

    // Permite habilitar ou desabilitar o Infinity.
    public bool InfinityEnabled = true;

    // Qualidade gráfica.
    public InfinityQuality Quality = InfinityQuality.High;

    // Recursos gráficos.
    public bool HDR = true;
    public bool Shadows = true;
    public bool SSAO = true;
    public bool Bloom = true;
    public bool SSR = true;
    public bool VolumetricFog = false;

    // Configurações avançadas.
    public int ShadowResolution = 2048;
    public float RenderScale = 1.0f;

    private void Save()
    {
        Apply();
        EditorRegistries.SaveSettings();
    }

    public override void Apply()
    {
        var settings = new RenderPipelineSettings
        {
            Pipeline = InfinityEnabled ? Pipeline : RenderPipelineType.Legacy,
            InfinityEnabled = InfinityEnabled,
            Quality = Quality,
            HDR = HDR,
            Shadows = Shadows,
            SSAO = SSAO,
            Bloom = Bloom,
            SSR = SSR,
            VolumetricFog = VolumetricFog,
            ShadowResolution = ShadowResolution,
            RenderScale = RenderScale,
        };

        RenderPipelineManager.LoadSettings(settings);
        // The modern look is also a shader variant. Keep it in step even when the editor is not rendering through
        // RenderPipelineManager yet, so switching the pipeline changes the surfaces on the next view.
        InfinityPipelineMode.SetEnabled(InfinityEnabled && Pipeline == RenderPipelineType.InfinityModern);
    }

    public override void ResetToDefaults()
    {
        Pipeline = RenderPipelineType.Legacy;

        InfinityEnabled = true;
        Quality = InfinityQuality.High;

        HDR = true;
        Shadows = true;
        SSAO = true;
        Bloom = true;
        SSR = true;
        VolumetricFog = false;

        ShadowResolution = 2048;
        RenderScale = 1.0f;
    }

    public override void OnGUI(Paper paper, float width)
    {
        Origami.Header(
            paper,
            "graphics_pipeline_header",
            $"{EditorIcons.GaugeHigh}  Rendering Pipeline"
        ).Underline().Show();

        EditorGUI.SettingsEnumDropdown(
            paper,
            "graphics_pipeline",
            "Rendering Pipeline",
            Pipeline,
            value =>
            {
                Pipeline = value;
                Save();
            }
        );

        Origami.Label(
            paper,
            "graphics_pipeline_description",
            Pipeline == RenderPipelineType.Legacy
                ? "Using the original Prowl rendering system."
                : "Using Infinity Modern Pipeline (Vulkan)."
        ).Show();

        paper.Box("graphics_spacing_1").Height(12);

        // Infinity Settings
        Origami.Header(
            paper,
            "graphics_infinity_header",
            "Infinity Modern Pipeline"
        ).Underline().Show();

        EditorGUI.SettingsToggle(
            paper,
            "graphics_infinity_enabled",
            "Enable Infinity",
            InfinityEnabled,
            value =>
            {
                InfinityEnabled = value;

                if (!InfinityEnabled &&
                    Pipeline == RenderPipelineType.InfinityModern)
                {
                    Pipeline = RenderPipelineType.Legacy;
                }

                Save();
            }
        );

        if (Pipeline != RenderPipelineType.InfinityModern)
        {
            Origami.Label(
                paper,
                "graphics_infinity_inactive",
                "Select Infinity Modern Pipeline to configure its graphics options."
            ).Show();

            return;
        }

        if (!InfinityEnabled)
            return;

        paper.Box("graphics_spacing_2").Height(8);

        Origami.Header(
            paper,
            "graphics_quality_header",
            "Graphics Quality"
        ).Underline().Show();

        EditorGUI.SettingsEnumDropdown(
            paper,
            "graphics_quality",
            "Quality",
            Quality,
            value =>
            {
                Quality = value;
                Save();
            }
        );

        EditorGUI.SettingsSliderField(
            paper,
            "graphics_render_scale",
            "Render Scale",
            RenderScale,
            0.25f,
            2.0f,
            value =>
            {
                RenderScale = value;
                Save();
            }
        );

        paper.Box("graphics_spacing_3").Height(8);

        Origami.Header(
            paper,
            "graphics_features_header",
            "Rendering Features"
        ).Underline().Show();

        EditorGUI.SettingsToggle(
            paper, "graphics_hdr", "HDR", HDR,
            value => { HDR = value; Save(); });

        EditorGUI.SettingsToggle(
            paper, "graphics_shadows", "Shadows", Shadows,
            value => { Shadows = value; Save(); });

        EditorGUI.SettingsToggle(
            paper, "graphics_ssao", "SSAO", SSAO,
            value => { SSAO = value; Save(); });

        EditorGUI.SettingsToggle(
            paper, "graphics_bloom", "Bloom", Bloom,
            value => { Bloom = value; Save(); });

        EditorGUI.SettingsToggle(
            paper, "graphics_ssr", "Screen Space Reflections", SSR,
            value => { SSR = value; Save(); });

        EditorGUI.SettingsToggle(
            paper, "graphics_fog", "Volumetric Fog", VolumetricFog,
            value => { VolumetricFog = value; Save(); });

        paper.Box("graphics_spacing_4").Height(8);

        Origami.Header(
            paper,
            "graphics_advanced_header",
            "Advanced Settings"
        ).Underline().Show();

        EditorGUI.SettingsIntSlider(
            paper,
            "graphics_shadow_resolution",
            "Shadow Resolution",
            ShadowResolution,
            256,
            8192,
            value =>
            {
                ShadowResolution = value;
                Save();
            }
        );

        Origami.Button(
            paper,
            "graphics_reset_defaults",
            "Reset Graphics Settings",
            () =>
            {
                ResetToDefaults();
                Save();
            }
        ).Show();
    }
}
