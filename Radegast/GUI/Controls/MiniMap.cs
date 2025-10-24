/*
 * Copyright (c) 2006-2016, openmetaverse.co
 * Copyright (c) 2021-2025, Sjofn LLC.
 * All rights reserved.
 *
 * - Redistribution and use in source and binary forms, with or without 
 *   modification, are permitted provided that the following conditions are met:
 *
 * - Redistributions of source code must retain the above copyright notice, this
 *   list of conditions and the following disclaimer.
 * - Neither the name of the openmetaverse.co nor the names 
 *   of its contributors may be used to endorse or promote products derived from
 *   this software without specific prior written permission.
 *
 * THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS "AS IS" 
 * AND ANY EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT LIMITED TO, THE 
 * IMPLIED WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE 
 * ARE DISCLAIMED. IN NO EVENT SHALL THE COPYRIGHT OWNER OR CONTRIBUTORS BE 
 * LIABLE FOR ANY DIRECT, INDIRECT, INCIDENTAL, SPECIAL, EXEMPLARY, OR 
 * CONSEQUENTIAL DAMAGES (INCLUDING, BUT NOT LIMITED TO, PROCUREMENT OF 
 * SUBSTITUTE GOODS OR SERVICES; LOSS OF USE, DATA, OR PROFITS; OR BUSINESS 
 * INTERRUPTION) HOWEVER CAUSED AND ON ANY THEORY OF LIABILITY, WHETHER IN 
 * CONTRACT, STRICT LIABILITY, OR TORT (INCLUDING NEGLIGENCE OR OTHERWISE) 
 * ARISING IN ANY WAY OUT OF THE USE OF THIS SOFTWARE, EVEN IF ADVISED OF THE 
 * POSSIBILITY OF SUCH DAMAGE.
 */

using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using CoreJ2K;
using OpenMetaverse.Assets;
using OpenMetaverse;
using SkiaSharp;
using SkiaSharp.Views.Desktop;

namespace Radegast.WinForms
{
    /// <summary>
    /// PictureBox GUI component for displaying a client's mini-map
    /// </summary>
    public class MiniMap : PictureBox
    {
        private static readonly Brush BG_COLOR = Brushes.Navy;

        private UUID _MapImageID;
        private GridClient _Client;
        private Image _MapLayer;

        /// <summary>
        /// Gets or sets the GridClient associated with this control
        /// </summary>
        public GridClient Client
        {
            get => _Client;
            set { if (value != null) InitializeClient(value); }
        }

        /// <summary>
        /// PictureBox control for an unspecified client's mini-map
        /// </summary>
        public MiniMap()
        {
            this.BorderStyle = BorderStyle.FixedSingle;
            this.SizeMode = PictureBoxSizeMode.Zoom;
        }

        /// <summary>
        /// PictureBox control for the specified client's mini-map
        /// </summary>
        public MiniMap(GridClient client) : this ()
        {
            InitializeClient(client);
        }

        /// <summary>Sets the map layer to the specified bitmap image</summary>
        /// <param name="mapImage"></param>
        public void SetMapLayer(Bitmap mapImage)
        {
            if (this.InvokeRequired) this.BeginInvoke((MethodInvoker)delegate { SetMapLayer(mapImage); });
            else
            {
                if (mapImage == null)
                {
                    Bitmap bmp = new Bitmap(256, 256);
                    Graphics g = Graphics.FromImage(bmp);
                    g.Clear(this.BackColor); // *HACK:
                    g.FillRectangle(BG_COLOR, 0f, 0f, 256f, 256f);
                    g.DrawImage(bmp, 0, 0);

                    _MapLayer = bmp;
                }
                else _MapLayer = mapImage;
            }
        }

        private void InitializeClient(GridClient client)
        {
            _Client = client;
            FetchMapLayer();
            _Client.Grid.CoarseLocationUpdate += Grid_CoarseLocationUpdate;
            _Client.Network.SimChanged += Network_OnCurrentSimChanged;
        }

        private void Grid_CoarseLocationUpdate(object sender, CoarseLocationUpdateEventArgs e)
        {
            UpdateMiniMap(e.Simulator);
        }

        private void UpdateMiniMap(Simulator sim)
        {
            if (!this.IsHandleCreated) { return; }

            if (this.InvokeRequired)
            {
                this.BeginInvoke((MethodInvoker)delegate { UpdateMiniMap(sim); });
            }
            else
            {
                if (_MapLayer == null)
                {
                    SetMapLayer(null);
                }
                else
                {
                    var bmp = (Bitmap)_MapLayer.Clone();
                    var g = Graphics.FromImage(bmp);

                    if (!sim.AvatarPositions.TryGetValue(Client.Self.AgentID, out var agentCoarsePosition))
                    {
                        return;
                    }

                    foreach (var coarse in _Client.Network.CurrentSim.AvatarPositions)
                    {
                        var x = (int)coarse.Value.X;
                        var y = 255 - (int)coarse.Value.Y;
                        if (coarse.Key == Client.Self.AgentID)
                        {
                            g.FillEllipse(Brushes.Yellow, x - 5, y - 5, 10, 10);
                            g.DrawEllipse(Pens.Khaki, x - 5, y - 5, 10, 10);
                        }
                        else
                        {
                            Pen penColor;
                            Brush brushColor;

                            var kvp = Client.Network.CurrentSim.ObjectsAvatars.FirstOrDefault(
                                av => av.Value.ID == coarse.Key);
                            if (kvp.Value != null)
                            {
                                brushColor = Brushes.PaleGreen;
                                penColor = Pens.Green;
                            }
                            else
                            {
                                brushColor = Brushes.LightGray;
                                penColor = Pens.Gray;
                            }

                            if (agentCoarsePosition.Z - coarse.Value.Z > 1)
                            {
                                var points = new Point[3]
                                    { new Point(x - 6, y - 6), new Point(x + 6, y - 6), new Point(x, y + 6) };
                                g.FillPolygon(brushColor, points);
                                g.DrawPolygon(penColor, points);
                            }

                            else if (agentCoarsePosition.Z - coarse.Value.Z < -1)
                            {
                                var points = new Point[3]
                                    { new Point(x - 6, y + 6), new Point(x + 6, y + 6), new Point(x, y - 6) };
                                g.FillPolygon(brushColor, points);
                                g.DrawPolygon(penColor, points);
                            }

                            else
                            {
                                g.FillEllipse(brushColor, x - 5, y - 5, 10, 10);
                                g.DrawEllipse(penColor, x - 5, y - 5, 10, 10);
                            }
                        }
                    }

                    g.DrawImage(bmp, 0, 0);
                    Image = bmp;
                }
            }
        }

        private void Network_OnCurrentSimChanged(object sender, SimChangedEventArgs e)
        {
            FetchMapLayer();
        }
        
        private void FetchMapLayer()
        {
            if (!_Client.Network.Connected) { return; }

            if (Client.Grid.GetGridRegion(Client.Network.CurrentSim.Name, GridLayerType.Objects, out var region))
            {
                SetMapLayer(null);

                _MapImageID = region.MapImageID;

                Client.Assets.RequestImage(_MapImageID, ImageType.Baked,
                    delegate (TextureRequestState state, AssetTexture asset)
                    {
                        if (state == TextureRequestState.Finished)
                        {
                            _MapLayer = J2kImage.FromBytes(asset.AssetData).As<SKBitmap>().ToBitmap();
                        }
                    });
            }
        }

    }
}
