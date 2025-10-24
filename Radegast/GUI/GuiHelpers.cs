/**
 * Radegast Metaverse Client
 * Copyright(c) 2009-2014, Radegast Development Team
 * Copyright(c) 2016-2025, Sjofn, LLC
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

using System;
using System.Windows.Forms;

namespace Radegast.GUI
{
    public static class GuiHelpers
    {
        public static void ApplyGuiFixes(object root)
        {
            try
            {
                if (RadegastInstanceForms.Initialized
                    && RadegastInstanceForms.Instance.GlobalSettings["theme_compatibility_mode"])
                {
                    ApplyThemeCompatibilityModeRecursive(root);
                }
            }
            catch (Exception)
            {
                // Suppress exceptions that will be raised above in designer mode.
            }

        }

        private static void ApplyThemeCompatibilityModeRecursive(object root)
        {
            if (root is ToolStrip toolstrip)
            {
                toolstrip.RenderMode = ToolStripRenderMode.System;
                toolstrip.BackColor = System.Drawing.SystemColors.Control;
                toolstrip.ForeColor = System.Drawing.SystemColors.ControlText;
                toolstrip.BackColor = System.Drawing.Color.Transparent;

                foreach (var item in toolstrip.Items)
                {
                    ApplyThemeCompatibilityModeRecursive(item);
                }
            }
            else if (root is ToolStripDropDownItem dropDownItem)
            {
                dropDownItem.BackColor = System.Drawing.SystemColors.Control;
                dropDownItem.ForeColor = System.Drawing.SystemColors.ControlText;
                dropDownItem.DropDown.RenderMode = ToolStripRenderMode.System;
                dropDownItem.DropDown.BackColor = System.Drawing.SystemColors.Control;
                dropDownItem.DropDown.ForeColor = System.Drawing.SystemColors.ControlText;

                foreach (var item in dropDownItem.DropDownItems)
                {
                    ApplyThemeCompatibilityModeRecursive(item);
                }
            }
            else if (root is Control control && !(control is ProgressBar))
            {
                control.BackColor = System.Drawing.SystemColors.Control;
                control.ForeColor = System.Drawing.SystemColors.ControlText;

                foreach (var item in control.Controls)
                {
                    ApplyThemeCompatibilityModeRecursive(item);
                }
            }
        }
    }
}
