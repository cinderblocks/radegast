// 
// Radegast Metaverse Client
// Copyright (c) 2009-2014, Radegast Development Team
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
            lblDrawDistance.Text = string.Format("Draw distance: {0}", tbDrawDistance.Value);
            cbWaterReflections.Enabled = RenderSettings.HasMultiTexturing && RenderSettings.HasShaders;
            if (cbWaterReflections.Enabled)
            {
                cbWaterReflections.Checked = instance.GlobalSettings["water_reflections"];
            }
            cbOcclusionCulling.Checked = Instance.GlobalSettings["rendering_occlusion_culling_enabled2"];
            cbShiny.Checked = Instance.GlobalSettings["scene_viewer_shiny"];
            cbVBO.Checked = Instance.GlobalSettings["rendering_use_vbo"];

            GUI.GuiHelpers.ApplyGuiFixes(this);
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
            lblDrawDistance.Text = string.Format("Draw distance: {0}", tbDrawDistance.Value);
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

        private void cbVBO_CheckedChanged(object sender, EventArgs e)
        {
            Instance.GlobalSettings["rendering_use_vbo"] = cbVBO.Checked;
            if (Window != null)
            {
                RenderSettings.UseVBO = cbVBO.Checked &&
                    (RenderSettings.ARBQuerySupported || RenderSettings.CoreQuerySupported);
            }
        }
    }
}
