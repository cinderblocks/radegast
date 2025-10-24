/**
 * Radegast Metaverse Client
 * Copyright(c) 2022, Sjofn, LLC
 * All rights reserved.
 *  
 * Radegast is free software: you can redistribute it and/or modify
 * it under the terms of the GNU Lesser General Public License as published
 * by the Free Software Foundation, either version 3 of the License, or
 * (at your option) any later version.
 * 
 * This program is distributed in the hope that it will be useful,
 * but WITHOUT ANY WARRANTY; without even the implied warranty of
 * MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.See the
 * GNU General Public License for more details.
 * 
 * You should have received a copy of the GNU Lesser General Public License
 * along with this program.If not, see<https://www.gnu.org/licenses/>.
 */

using System.Windows.Forms;

namespace Radegast.GUI
{
    public partial class MfaPrompt : Form
    {
        private readonly RadegastInstanceForms Instance;
        private INetCom NetCom => Instance.NetCom;

        public MfaPrompt(RadegastInstanceForms instance)
        {
            Instance = instance;
            InitializeComponent();

            AcceptButton = btnSubmit;
            GuiHelpers.ApplyGuiFixes(this);
        }

        private void btnSubmit_Click(object sender, System.EventArgs e)
        {
            NetCom.LoginOptions.MfaToken = tokenBox.Text;
            NetCom.Login();
            DialogResult = DialogResult.OK;
            Close();
        }
    }
}
