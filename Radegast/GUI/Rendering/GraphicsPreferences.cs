// 
// Radegast Metaverse Client
// Copyright (c) 2009-2014, Radegast Development Team
// Copyright (c) 2025, Sjofn LLC.
// All rights reserved.
// 
// Redistribution and use in source and binary forms, with or without
// modification, are permitted provided that the following conditions are met:
// 
//     * Redistributions of source code must retain the above copyright notice,
//       this list of conditions and the following disclaimer.
//     * Redistributions in binary form must reproduce the above copyright
//       notice, this list of conditions and the following disclaimer in the
//       documentation and/or other materials provided with the distribution.
//     * Neither the name of the application "Radegast", nor the names of its
//       contributors may be used to endorse or promote products derived from
//       this software without specific prior written permission.
// 
// THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS "AS IS"
// AND ANY EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT LIMITED TO, THE
// IMPLIED WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE ARE
// DISCLAIMED. IN NO EVENT SHALL THE COPYRIGHT HOLDER OR CONTRIBUTORS BE LIABLE
// FOR ANY DIRECT, INDIRECT, INCIDENTAL, SPECIAL, EXEMPLARY, OR CONSEQUENTIAL
// DAMAGES (INCLUDING, BUT NOT LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR
// SERVICES; LOSS OF USE, DATA, OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER
// CAUSED AND ON ANY THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY,
// OR TORT (INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE
// OF THIS SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.
//
// $Id$
//

using System;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using OpenMetaverse;

namespace Radegast.Rendering
{
    public partial class GraphicsPreferences : UserControl
    {
        private readonly RadegastInstanceForms Instance;
        private GridClient Client => Instance.Client;

        private SceneWindow Window
        {
            get
            {
                if (Instance.TabConsole.TabExists("scene_window"))
                {
                    return (SceneWindow)Instance.TabConsole.Tabs["scene_window"].Control;
                }
                return null;
            }
        }

        public GraphicsPreferences()
        {
            InitializeComponent();

            GUI.GuiHelpers.ApplyGuiFixes(this);
        }

        public GraphicsPreferences(RadegastInstanceForms instance)
        {
            Instance = instance;
            InitializeComponent();
            Disposed += GraphicsPreferences_Disposed;

            Text = "Graphics preferences";
            cbAA.Checked = instance.GlobalSettings["use_multi_sampling"];
            tbDrawDistance.Value = Utils.Clamp(Instance.GlobalSettings["draw_distance"].AsInteger(),
                tbDrawDistance.Minimum, tbDrawDistance.Maximum);
            lblDrawDistance.Text = $"Draw distance: {tbDrawDistance.Value}";
            cbWaterReflections.Enabled = RenderSettings.HasMultiTexturing && RenderSettings.HasShaders;
            if (cbWaterReflections.Enabled)
            {
                cbWaterReflections.Checked = instance.GlobalSettings["water_reflections"];
            }
            cbOcclusionCulling.Checked = Instance.GlobalSettings["rendering_occlusion_culling_enabled2"];
            cbShiny.Checked = Instance.GlobalSettings["scene_viewer_shiny"];

            // Initialize lighting controls
            if (!Instance.GlobalSettings.ContainsKey("scene_ambient_light"))
            {
                Instance.GlobalSettings["scene_ambient_light"] = RenderSettings.AmbientLight;
            }
            if (!Instance.GlobalSettings.ContainsKey("scene_diffuse_light"))
            {
                Instance.GlobalSettings["scene_diffuse_light"] = RenderSettings.DiffuseLight;
            }
            if (!Instance.GlobalSettings.ContainsKey("scene_specular_light"))
            {
                Instance.GlobalSettings["scene_specular_light"] = RenderSettings.SpecularLight;
            }

            tbAmbient.Value = (int)(Instance.GlobalSettings["scene_ambient_light"].AsReal() * 100);
            lblAmbient.Text = $"Ambient: {Instance.GlobalSettings["scene_ambient_light"].AsReal():0.00}";
            
            tbDiffuse.Value = (int)(Instance.GlobalSettings["scene_diffuse_light"].AsReal() * 100);
            lblDiffuse.Text = $"Diffuse: {Instance.GlobalSettings["scene_diffuse_light"].AsReal():0.00}";
            
            tbSpecular.Value = (int)(Instance.GlobalSettings["scene_specular_light"].AsReal() * 100);
            lblSpecular.Text = $"Specular: {Instance.GlobalSettings["scene_specular_light"].AsReal():0.00}";

            // Apply saved lighting settings
            RenderSettings.AmbientLight = (float)Instance.GlobalSettings["scene_ambient_light"].AsReal();
            RenderSettings.DiffuseLight = (float)Instance.GlobalSettings["scene_diffuse_light"].AsReal();
            RenderSettings.SpecularLight = (float)Instance.GlobalSettings["scene_specular_light"].AsReal();

            // Initialize gamma setting
            if (!Instance.GlobalSettings.ContainsKey("scene_gamma"))
            {
                Instance.GlobalSettings["scene_gamma"] = RenderSettings.Gamma;
            }

            // Gamma control slider (0.5 - 3.0 mapped to trackbar 50-300)
            var tbGammaCtrl = FindTrackBar("tbGamma");
            var lblGammaCtrl = FindLabel("lblGamma");
            if (tbGammaCtrl != null)
            {
                tbGammaCtrl.Value = Utils.Clamp((int)(Instance.GlobalSettings["scene_gamma"].AsReal() * 100f), tbGammaCtrl.Minimum, tbGammaCtrl.Maximum);
                tbGammaCtrl.Scroll += tbGamma_Scroll;
            }
            if (lblGammaCtrl != null)
            {
                lblGammaCtrl.Text = $"Gamma: {Instance.GlobalSettings["scene_gamma"].AsReal():0.00}";
            }
            RenderSettings.Gamma = (float)Instance.GlobalSettings["scene_gamma"].AsReal();

            // Initialize emissive strength setting
            if (!Instance.GlobalSettings.ContainsKey("scene_emissive_strength"))
            {
                Instance.GlobalSettings["scene_emissive_strength"] = RenderSettings.EmissiveStrength;
            }

            var tbEmissiveCtrl = FindTrackBar("tbEmissive");
            var lblEmissiveCtrl = FindLabel("lblEmissive");
            if (tbEmissiveCtrl != null)
            {
                tbEmissiveCtrl.Value = Utils.Clamp((int)(Instance.GlobalSettings["scene_emissive_strength"].AsReal() * 100f), tbEmissiveCtrl.Minimum, tbEmissiveCtrl.Maximum);
                tbEmissiveCtrl.Scroll += tbEmissive_Scroll;
            }
            if (lblEmissiveCtrl != null)
            {
                lblEmissiveCtrl.Text = $"Emissive: {Instance.GlobalSettings["scene_emissive_strength"].AsReal():0.00}";
            }
            RenderSettings.EmissiveStrength = (float)Instance.GlobalSettings["scene_emissive_strength"].AsReal();

            // Initialize fallback water animation settings in global settings if missing
            if (!instance.GlobalSettings.ContainsKey("fallback_water_animation_enabled"))
                instance.GlobalSettings["fallback_water_animation_enabled"] = RenderSettings.FallbackWaterAnimationEnabled;
            if (!instance.GlobalSettings.ContainsKey("fallback_water_animation_speed"))
                instance.GlobalSettings["fallback_water_animation_speed"] = RenderSettings.FallbackWaterAnimationSpeed;
            if (!instance.GlobalSettings.ContainsKey("fallback_water_animation_amplitude"))
                instance.GlobalSettings["fallback_water_animation_amplitude"] = RenderSettings.FallbackWaterAnimationAmplitude;
            if (!instance.GlobalSettings.ContainsKey("fallback_water_base_alpha"))
                instance.GlobalSettings["fallback_water_base_alpha"] = RenderSettings.FallbackWaterBaseAlpha;

            // Apply saved values to runtime RenderSettings
            RenderSettings.FallbackWaterAnimationEnabled = instance.GlobalSettings["fallback_water_animation_enabled"];
            RenderSettings.FallbackWaterAnimationSpeed = (float)instance.GlobalSettings["fallback_water_animation_speed"];
            RenderSettings.FallbackWaterAnimationAmplitude = (float)instance.GlobalSettings["fallback_water_animation_amplitude"];
            RenderSettings.FallbackWaterBaseAlpha = (float)instance.GlobalSettings["fallback_water_base_alpha"];

            // Wire designer controls for fallback animation
            cbFallbackAnim.Checked = RenderSettings.FallbackWaterAnimationEnabled;
            cbFallbackAnim.CheckedChanged += cbFallbackAnim_CheckedChanged;

            nudFallbackSpeed.Value = (decimal)RenderSettings.FallbackWaterAnimationSpeed;
            nudFallbackSpeed.ValueChanged += nudFallbackSpeed_ValueChanged;

            nudFallbackAmp.Value = (decimal)RenderSettings.FallbackWaterAnimationAmplitude;
            nudFallbackAmp.ValueChanged += nudFallbackAmp_ValueChanged;

            nudFallbackBaseAlpha.Value = (decimal)RenderSettings.FallbackWaterBaseAlpha;
            nudFallbackBaseAlpha.ValueChanged += nudFallbackBaseAlpha_ValueChanged;

            // Initialize glow/materials toggles
            if (!Instance.GlobalSettings.ContainsKey("scene_enable_glow"))
            {
                Instance.GlobalSettings["scene_enable_glow"] = RenderSettings.EnableGlow;
            }
            if (!Instance.GlobalSettings.ContainsKey("scene_enable_materials"))
            {
                Instance.GlobalSettings["scene_enable_materials"] = RenderSettings.EnableMaterials;
            }
            RenderSettings.EnableGlow = Instance.GlobalSettings["scene_enable_glow"];
            RenderSettings.EnableMaterials = Instance.GlobalSettings["scene_enable_materials"];

            // Hook designer checkboxes if present
            var cbGlowCtrl = this.Controls.Find("cbGlow", true).FirstOrDefault() as CheckBox;
            var cbMaterialsCtrl = this.Controls.Find("cbMaterials", true).FirstOrDefault() as CheckBox;
            if (cbGlowCtrl != null)
            {
                cbGlowCtrl.Checked = RenderSettings.EnableGlow;
                cbGlowCtrl.CheckedChanged += (s, e2) =>
                {
                    Instance.GlobalSettings["scene_enable_glow"] = cbGlowCtrl.Checked;
                    RenderSettings.EnableGlow = cbGlowCtrl.Checked;
                    if (Window != null)
                    {
                        Window.SetShaderGlow(cbGlowCtrl.Checked ? 0f : 0f); // Reset per-face; actual glow comes from faces
                    }
                };
            }
            if (cbMaterialsCtrl != null)
            {
                cbMaterialsCtrl.Checked = RenderSettings.EnableMaterials;
                cbMaterialsCtrl.CheckedChanged += (s, e2) =>
                {
                    Instance.GlobalSettings["scene_enable_materials"] = cbMaterialsCtrl.Checked;
                    RenderSettings.EnableMaterials = cbMaterialsCtrl.Checked;
                };
            }

            // Initialize sky shader toggle
            if (!Instance.GlobalSettings.ContainsKey("scene_enable_sky_shader"))
            {
                Instance.GlobalSettings["scene_enable_sky_shader"] = RenderSettings.EnableSkyShader;
            }

            var cbSkyShaderCtrl = this.Controls.Find("cbSkyShader", true).FirstOrDefault() as CheckBox;
            if (cbSkyShaderCtrl != null)
            {
                cbSkyShaderCtrl.Checked = (bool)Instance.GlobalSettings["scene_enable_sky_shader"];
                cbSkyShaderCtrl.CheckedChanged += (s, e2) =>
                {
                    Instance.GlobalSettings["scene_enable_sky_shader"] = cbSkyShaderCtrl.Checked;
                    RenderSettings.EnableSkyShader = cbSkyShaderCtrl.Checked;

                    // Apply immediately: force a repaint so RenderSky will use/skip shader next frame
                    try
                    {
                        var w = Window;
                        if (w != null && w.glControl != null && !w.glControl.IsDisposed)
                        {
                            try { w.glControl.MakeCurrent(); } catch { }
                            w.glControl.Invalidate();
                        }
                    }
                    catch { }
                };
            }

            GUI.GuiHelpers.ApplyGuiFixes(this);
            InitializeShadowControls();
        }

        // Add handlers for shadow controls if present in designer
        private void InitializeShadowControls()
        {
            // Ensure global settings have defaults
            if (!Instance.GlobalSettings.ContainsKey("scene_enable_shadows"))
                Instance.GlobalSettings["scene_enable_shadows"] = RenderSettings.EnableShadows;
            if (!Instance.GlobalSettings.ContainsKey("scene_shadow_intensity"))
                Instance.GlobalSettings["scene_shadow_intensity"] = RenderSettings.ShadowIntensity;

            var cbShadows = this.Controls.Find("cbShadows", true).FirstOrDefault() as CheckBox;
            var tbShadowIntensity = this.Controls.Find("tbShadowIntensity", true).FirstOrDefault() as TrackBar;
            var lblShadowIntensity = this.Controls.Find("lblShadowIntensity", true).FirstOrDefault() as Label;

            if (cbShadows != null)
            {
                cbShadows.Checked = Instance.GlobalSettings["scene_enable_shadows"];
                cbShadows.CheckedChanged += (s, e) =>
                {
                    Instance.GlobalSettings["scene_enable_shadows"] = cbShadows.Checked;
                    RenderSettings.EnableShadows = cbShadows.Checked;
                    // Apply to scene window immediately
                    var w = Window;
                    if (w != null) w.SetShaderShadows(cbShadows.Checked, (float)Instance.GlobalSettings["scene_shadow_intensity"].AsReal());
                };
            }

            if (tbShadowIntensity != null)
            {
                tbShadowIntensity.Value = Utils.Clamp((int)(Instance.GlobalSettings["scene_shadow_intensity"].AsReal() * 100f), tbShadowIntensity.Minimum, tbShadowIntensity.Maximum);
                if (lblShadowIntensity != null) lblShadowIntensity.Text = $"Shadow intensity: {Instance.GlobalSettings["scene_shadow_intensity"].AsReal():0.00}";
                tbShadowIntensity.Scroll += (s, e) =>
                {
                    float v = (float)tbShadowIntensity.Value / 100f;
                    if (lblShadowIntensity != null) lblShadowIntensity.Text = $"Shadow intensity: {v:0.00}";
                    Instance.GlobalSettings["scene_shadow_intensity"] = v;
                    RenderSettings.ShadowIntensity = v;
                    var w = Window;
                    if (w != null) w.SetShaderShadows((bool)Instance.GlobalSettings["scene_enable_shadows"], v);
                };
            }
        }

        private void cbFallbackAnim_CheckedChanged(object sender, EventArgs e)
        {
            Instance.GlobalSettings["fallback_water_animation_enabled"] = cbFallbackAnim.Checked;
            RenderSettings.FallbackWaterAnimationEnabled = cbFallbackAnim.Checked;
        }

        private void nudFallbackSpeed_ValueChanged(object sender, EventArgs e)
        {
            Instance.GlobalSettings["fallback_water_animation_speed"] = (float)nudFallbackSpeed.Value;
            RenderSettings.FallbackWaterAnimationSpeed = (float)nudFallbackSpeed.Value;
        }

        private void nudFallbackAmp_ValueChanged(object sender, EventArgs e)
        {
            Instance.GlobalSettings["fallback_water_animation_amplitude"] = (float)nudFallbackAmp.Value;
            RenderSettings.FallbackWaterAnimationAmplitude = (float)nudFallbackAmp.Value;
        }

        private void nudFallbackBaseAlpha_ValueChanged(object sender, EventArgs e)
        {
            Instance.GlobalSettings["fallback_water_base_alpha"] = (float)nudFallbackBaseAlpha.Value;
            RenderSettings.FallbackWaterBaseAlpha = (float)nudFallbackBaseAlpha.Value;
        }

        // Designer event handler for Shadows checkbox (also used by InitializeShadowControls)
        private void cbShadows_CheckedChanged(object sender, EventArgs e)
        {
            var cb = sender as CheckBox ?? this.Controls.Find("cbShadows", true).FirstOrDefault() as CheckBox;
            if (cb == null) return;
            Instance.GlobalSettings["scene_enable_shadows"] = cb.Checked;
            RenderSettings.EnableShadows = cb.Checked;
            var w = Window;
            if (w != null)
            {
                float intensity = (float)Instance.GlobalSettings["scene_shadow_intensity"].AsReal();
                w.SetShaderShadows(cb.Checked, intensity);
            }
        }

        // Designer event handler for Shadow intensity trackbar
        private void tbShadowIntensity_Scroll(object sender, EventArgs e)
        {
            var tb = sender as TrackBar ?? this.Controls.Find("tbShadowIntensity", true).FirstOrDefault() as TrackBar;
            var lbl = FindLabel("lblShadowIntensity");
            if (tb == null) return;
            float v = (float)tb.Value / 100f;
            if (lbl != null) lbl.Text = $"Shadow intensity: {v:0.00}";
            Instance.GlobalSettings["scene_shadow_intensity"] = v;
            RenderSettings.ShadowIntensity = v;
            var w = Window;
            if (w != null)
            {
                bool enable = Instance.GlobalSettings.ContainsKey("scene_enable_shadows") && Instance.GlobalSettings["scene_enable_shadows"];
                w.SetShaderShadows(enable, v);
            }
        }

        private void GraphicsPreferences_Disposed(object sender, EventArgs e)
        {
        }

        private void chkWireFrame_CheckedChanged(object sender, EventArgs e)
        {
            Instance.GlobalSettings["render_wireframe"] = chkWireFrame.Checked;

            if (Window != null)
            {
                Window.Wireframe = chkWireFrame.Checked;
            }
        }

        private void cbAA_CheckedChanged(object sender, EventArgs e)
        {
            Instance.GlobalSettings["use_multi_sampling"] = cbAA.Checked;
        }

        private void tbDrawDistance_Scroll(object sender, EventArgs e)
        {
            lblDrawDistance.Text = $"Draw distance: {tbDrawDistance.Value}";
            Instance.GlobalSettings["draw_distance"] = tbDrawDistance.Value;

            if (Client != null)
            {
                Client.Self.Movement.Camera.Far = tbDrawDistance.Value;
            }

            if (Window != null)
            {
                Window.DrawDistance = (float)tbDrawDistance.Value;
                Window.UpdateCamera();
            }
        }

        private void cbWaterReflections_CheckedChanged(object sender, EventArgs e)
        {
            Instance.GlobalSettings["water_reflections"] = cbWaterReflections.Checked;

            if (Window != null)
            {
                if (RenderSettings.HasMultiTexturing && RenderSettings.HasShaders)
                {
                    RenderSettings.WaterReflections = cbWaterReflections.Checked;
                }
            }
        }

        private void cbOcclusionCulling_CheckedChanged(object sender, EventArgs e)
        {
            Instance.GlobalSettings["rendering_occlusion_culling_enabled2"] = cbOcclusionCulling.Checked;
            if (Window != null)
            {
                RenderSettings.OcclusionCullingEnabled = Instance.GlobalSettings["rendering_occlusion_culling_enabled2"]
                    && (RenderSettings.ARBQuerySupported || RenderSettings.CoreQuerySupported);
            }
        }

        private void cbShiny_CheckedChanged(object sender, EventArgs e)
        {
            Instance.GlobalSettings["scene_viewer_shiny"] = cbShiny.Checked;
            if (Window != null)
            {
                if (RenderSettings.HasShaders)
                {
                    RenderSettings.EnableShiny = cbShiny.Checked;
                }
            }
        }

        private void tbAmbient_Scroll(object sender, EventArgs e)
        {
            float ambient = (float)tbAmbient.Value / 100f;
            lblAmbient.Text = $"Ambient: {ambient:0.00}";
            Instance.GlobalSettings["scene_ambient_light"] = ambient;
            RenderSettings.AmbientLight = ambient;
            if (Window != null)
            {
                Window.UpdateLighting();
            }
        }

        private void tbDiffuse_Scroll(object sender, EventArgs e)
        {
            float diffuse = (float)tbDiffuse.Value / 100f;
            lblDiffuse.Text = $"Diffuse: {diffuse:0.00}";
            Instance.GlobalSettings["scene_diffuse_light"] = diffuse;
            RenderSettings.DiffuseLight = diffuse;
            if (Window != null)
            {
                Window.UpdateLighting();
            }
        }

        private void tbSpecular_Scroll(object sender, EventArgs e)
        {
            float specular = (float)tbSpecular.Value / 100f;
            lblSpecular.Text = $"Specular: {specular:0.00}";
            Instance.GlobalSettings["scene_specular_light"] = specular;
            RenderSettings.SpecularLight = specular;
            if (Window != null)
            {
                Window.UpdateLighting();
            }
        }

        private void tbGamma_Scroll(object sender, EventArgs e)
        {
            var tb = sender as TrackBar ?? FindTrackBar("tbGamma");
            var lbl = FindLabel("lblGamma");
            if (tb == null) return;
            float gamma = (float)tb.Value / 100f;
            if (lbl != null) lbl.Text = $"Gamma: {gamma:0.00}";
            Instance.GlobalSettings["scene_gamma"] = gamma;
            RenderSettings.Gamma = gamma;
            if (Window != null)
            {
                Window.SetShaderGamma(gamma);
            }
        }

        private void tbEmissive_Scroll(object sender, EventArgs e)
        {
            var tb = sender as TrackBar ?? FindTrackBar("tbEmissive");
            var lbl = FindLabel("lblEmissive");
            if (tb == null) return;
            float v = (float)tb.Value / 100f;
            if (lbl != null) lbl.Text = $"Emissive: {v:0.00}";
            Instance.GlobalSettings["scene_emissive_strength"] = v;
            RenderSettings.EmissiveStrength = v;
            if (Window != null)
            {
                Window.SetShaderEmissiveStrength(v);
            }
        }

        private void btnDefaults_Click(object sender, EventArgs e)
        {
            // Reset GlobalSettings to defaults
            Instance.GlobalSettings["use_multi_sampling"] = true;
            Instance.GlobalSettings["draw_distance"] = 128;
            Instance.GlobalSettings["water_reflections"] = false;
            Instance.GlobalSettings["scene_enable_sky_shader"] = RenderSettings.EnableSkyShader = true;
            var cbSky = this.Controls.Find("cbSkyShader", true).FirstOrDefault() as CheckBox;
            if (cbSky != null) cbSky.Checked = true;
            Instance.GlobalSettings["rendering_occlusion_culling_enabled2"] = false;
            Instance.GlobalSettings["scene_viewer_shiny"] = false;
            Instance.GlobalSettings["scene_ambient_light"] = RenderSettings.AmbientLight = 0.70f;
            Instance.GlobalSettings["scene_diffuse_light"] = RenderSettings.DiffuseLight = 0.80f;
            Instance.GlobalSettings["scene_specular_light"] = RenderSettings.SpecularLight = 0.50f;
            Instance.GlobalSettings["scene_gamma"] = RenderSettings.Gamma = 1.0f;
            Instance.GlobalSettings["scene_emissive_strength"] = RenderSettings.EmissiveStrength = 1.0f;
            Instance.GlobalSettings["fallback_water_animation_enabled"] = RenderSettings.FallbackWaterAnimationEnabled = true;
            Instance.GlobalSettings["fallback_water_animation_speed"] = RenderSettings.FallbackWaterAnimationSpeed = 1.5f;
            Instance.GlobalSettings["fallback_water_animation_amplitude"] = RenderSettings.FallbackWaterAnimationAmplitude = 0.12f;
            Instance.GlobalSettings["fallback_water_base_alpha"] = RenderSettings.FallbackWaterBaseAlpha = 0.84f;
            Instance.GlobalSettings["scene_enable_glow"] = RenderSettings.EnableGlow = true;
            Instance.GlobalSettings["scene_enable_materials"] = RenderSettings.EnableMaterials = true;

            // Update UI controls
            var tbGammaCtrl = FindTrackBar("tbGamma");
            var lblGammaCtrl = FindLabel("lblGamma");
            if (tbGammaCtrl != null) tbGammaCtrl.Value = (int)(RenderSettings.Gamma * 100f);
            if (lblGammaCtrl != null) lblGammaCtrl.Text = $"Gamma: {RenderSettings.Gamma:0.00}";

            var tbEmissiveCtrl = FindTrackBar("tbEmissive");
            var lblEmissiveCtrl = FindLabel("lblEmissive");
            if (tbEmissiveCtrl != null) tbEmissiveCtrl.Value = (int)(RenderSettings.EmissiveStrength * 100f);
            if (lblEmissiveCtrl != null) lblEmissiveCtrl.Text = $"Emissive: {RenderSettings.EmissiveStrength:0.00}";

            cbAA.Checked = Instance.GlobalSettings["use_multi_sampling"];
            tbDrawDistance.Value = Utils.Clamp(Instance.GlobalSettings["draw_distance"].AsInteger(), tbDrawDistance.Minimum, tbDrawDistance.Maximum);
            lblDrawDistance.Text = $"Draw distance: {tbDrawDistance.Value}";
            cbWaterReflections.Checked = Instance.GlobalSettings["water_reflections"];
            cbOcclusionCulling.Checked = Instance.GlobalSettings["rendering_occlusion_culling_enabled2"];
            cbShiny.Checked = Instance.GlobalSettings["scene_viewer_shiny"];

            tbAmbient.Value = (int)(RenderSettings.AmbientLight * 100);
            lblAmbient.Text = $"Ambient: {RenderSettings.AmbientLight:0.00}";
            tbDiffuse.Value = (int)(RenderSettings.DiffuseLight * 100);
            lblDiffuse.Text = $"Diffuse: {RenderSettings.DiffuseLight:0.00}";
            tbSpecular.Value = (int)(RenderSettings.SpecularLight * 100);
            lblSpecular.Text = $"Specular: {RenderSettings.SpecularLight:0.00}";

            cbFallbackAnim.Checked = RenderSettings.FallbackWaterAnimationEnabled;
            nudFallbackSpeed.Value = (decimal)RenderSettings.FallbackWaterAnimationSpeed;
            nudFallbackAmp.Value = (decimal)RenderSettings.FallbackWaterAnimationAmplitude;
            nudFallbackBaseAlpha.Value = (decimal)RenderSettings.FallbackWaterBaseAlpha;

            var cbGlowCtrl = this.Controls.Find("cbGlow", true);
            if (cbGlowCtrl != null && cbGlowCtrl.Length > 0 && cbGlowCtrl[0] is System.Windows.Forms.CheckBox cbGlow)
                cbGlow.Checked = RenderSettings.EnableGlow;
            var cbMaterialsCtrl = this.Controls.Find("cbMaterials", true);
            if (cbMaterialsCtrl != null && cbMaterialsCtrl.Length > 0 && cbMaterialsCtrl[0] is System.Windows.Forms.CheckBox cbMaterials)
                cbMaterials.Checked = RenderSettings.EnableMaterials;

            // Apply to SceneWindow if available
            var window = Instance.TabConsole.TabExists("scene_window") ? (SceneWindow)Instance.TabConsole.Tabs["scene_window"].Control : null;
            if (window != null)
            {
                window.DrawDistance = tbDrawDistance.Value;
                window.UpdateLighting();
                window.SetShaderGamma(RenderSettings.Gamma);
                window.SetShaderEmissiveStrength(RenderSettings.EmissiveStrength);
                RenderSettings.EnableShiny = cbShiny.Checked && RenderSettings.HasShaders;
            }
        }

        // Helper to find a TrackBar by name in the control hierarchy
        private TrackBar FindTrackBar(string name)
        {
            var ctrls = this.Controls.Find(name, true);
            if (ctrls != null && ctrls.Length > 0)
            {
                return ctrls[0] as TrackBar;
            }
            return null;
        }

        // Helper to find a Label by name in the control hierarchy
        private Label FindLabel(string name)
        {
            var ctrls = this.Controls.Find(name, true);
            if (ctrls != null && ctrls.Length > 0)
            {
                return ctrls[0] as Label;
            }
            return null;
        }
    }
}
