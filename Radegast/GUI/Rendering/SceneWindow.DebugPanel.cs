// 
// Radegast Metaverse Client
// Copyright (c) 2009-2014, Radegast Development Team
// Copyright (c) 2019-2025, Sjofn LLC
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
//       this software without specific prior permission.
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
using System.Globalization;
using System.Windows.Forms;
using OpenMetaverse;

namespace Radegast.Rendering
{
    /// <summary>
    /// Debug panel and WinForms event handlers for SceneWindow
    /// </summary>
    public partial class SceneWindow
    {
        #region Winform hooks

        private void hsAmbient_Scroll(object sender, ScrollEventArgs e)
        {
            ambient = (float)hsAmbient.Value / 100f;
            RenderSettings.AmbientLight = ambient;
            SetSun();
        }

        private void hsDiffuse_Scroll(object sender, ScrollEventArgs e)
        {
            diffuse = (float)hsDiffuse.Value / 100f;
            RenderSettings.DiffuseLight = diffuse;
            SetSun();
        }

        private void hsSpecular_Scroll(object sender, ScrollEventArgs e)
        {
            specular = (float)hsSpecular.Value / 100f;
            RenderSettings.SpecularLight = specular;
            SetSun();
        }

        private void hsLOD_Scroll(object sender, ScrollEventArgs e)
        {
            minLODFactor = (float)hsLOD.Value / 5000f;
        }

        private void button_vparam_Click(object sender, EventArgs e)
        {
            //int paramid = int.Parse(textBox_vparamid.Text);
            //float weight = (float)hScrollBar_weight.Value/100f;
            var weightx = float.Parse(textBox_x.Text);
            var weighty = float.Parse(textBox_y.Text);
            var weightz = float.Parse(textBox_z.Text);

            foreach (var av in Avatars.Values)
            {
                //av.glavatar.applyMorph(av.avatar,paramid,weight);
                av.glavatar.skel.deformbone(comboBox1.Text, new Vector3(float.Parse(textBox_sx.Text), float.Parse(textBox_sy.Text), float.Parse(textBox_sz.Text)), Quaternion.CreateFromEulers((float)(Math.PI * (weightx / 180)), (float)(Math.PI * (weighty / 180)), (float)(Math.PI * (weightz / 180))));

                foreach (var mesh in av.glavatar._meshes.Values)
                {
                    mesh.applyjointweights();
                }

            }
        }

        private void textBox_vparamid_TextChanged(object sender, EventArgs e)
        {

        }

        private void comboBox1_SelectedIndexChanged(object sender, EventArgs e)
        {

            var bone = comboBox1.Text;
            foreach (var av in Avatars.Values)
            {
                if (av.glavatar.skel.mBones.TryGetValue(bone, out var b))
                {
                    textBox_sx.Text = (b.scale.X - 1.0f).ToString(CultureInfo.InvariantCulture);
                    textBox_sy.Text = (b.scale.Y - 1.0f).ToString(CultureInfo.InvariantCulture);
                    textBox_sz.Text = (b.scale.Z - 1.0f).ToString(CultureInfo.InvariantCulture);

                    b.rot.GetEulerAngles(out var x, out var y, out var z);
                    textBox_x.Text = x.ToString(CultureInfo.InvariantCulture);
                    textBox_y.Text = y.ToString(CultureInfo.InvariantCulture);
                    textBox_z.Text = z.ToString(CultureInfo.InvariantCulture);

                }

            }


        }

        private void textBox_y_TextChanged(object sender, EventArgs e)
        {

        }

        private void textBox_z_TextChanged(object sender, EventArgs e)
        {

        }

        private void comboBox_morph_SelectedIndexChanged(object sender, EventArgs e)
        {

        }

        private void button1_Click(object sender, EventArgs e)
        {
            foreach (var av in Avatars.Values)
            {
                var id = -1;

                //foreach (VisualParamEx vpe in VisualParamEx.morphParams.Values)
                //{
                //    if (vpe.Name == comboBox_morph.Text)
                //    {
                //        id = vpe.ParamID;
                //        break;
                //    }
                //
                //}

                av.glavatar.applyMorph(av.avatar, id, float.Parse(textBox_morphamount.Text));

                foreach (var mesh in av.glavatar._meshes.Values)
                {
                    mesh.applyjointweights();
                }

            }



        }

        private void gbZoom_Enter(object sender, EventArgs e)
        {

        }

        private void button_driver_Click(object sender, EventArgs e)
        {
            /*
            foreach (RenderAvatar av in Avatars.Values)
            {
                int id = -1;
                foreach (VisualParamEx vpe in VisualParamEx.drivenParams.Values)
                {
                    if (vpe.Name == comboBox_driver.Text)
                    {
                        id = vpe.ParamID;
                        break;
                    }

                }
                av.glavatar.applyMorph(av.avatar, id, float.Parse(textBox_driveramount.Text));

                foreach (GLMesh mesh in av.glavatar._meshes.Values)
                {
                    mesh.applyjointweights();
                }

            }
            */
        }

        private bool miscEnabled = true;
        private void cbMisc_CheckedChanged(object sender, EventArgs e)
        {
            miscEnabled = cbMisc.Checked;
            RenderSettings.OcclusionCullingEnabled = miscEnabled;
        }

        #endregion
    }
}
