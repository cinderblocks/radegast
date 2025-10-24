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
using System.Windows.Forms.Design;
using System.ComponentModel;

namespace Radegast
{
    [ToolStripItemDesignerAvailability(
        ToolStripItemDesignerAvailability.ToolStrip)]
    public class ToolStripCheckBox
        : ToolStripControlHost
    {
        public CheckBox CheckBoxControl => Control as CheckBox;

        /// <summary>
        /// Is check box ticked
        /// </summary>
        [Category("Appearance")]
        public bool Checked
        {
            get => CheckBoxControl.Checked;
            set => CheckBoxControl.Checked = value;
        }

        /// <summary>
        /// Checked state
        /// </summary>
        [Category("Appearance")]
        public CheckState CheckState
        {
            get => CheckBoxControl.CheckState;
            set => CheckBoxControl.CheckState = value;
        }

        /// <summary>
        /// Label text
        /// </summary>
        [Category("Appearance")]
        public override string Text
        {
            get => CheckBoxControl.Text;
            set => CheckBoxControl.Text = value;
        }

        /// <summary>
        /// Occurs when check property is changed
        /// </summary>
        [Category("Misc")]
        public event EventHandler CheckedChanged;

        /// <summary>
        /// Occurs when check state of the control changes
        /// </summary>
        [Category("Misc")]
        public event EventHandler CheckStateChanged;

        public ToolStripCheckBox()
            : base(new CheckBox())
        {
            CheckBoxControl.MouseHover += chk_MouseHover;
        }

        private void chk_MouseHover(object sender, EventArgs e)
        {
            OnMouseHover(e);
        }
 
        protected override void OnSubscribeControlEvents(Control c)
        {
            base.OnSubscribeControlEvents(c);
            ((CheckBox)c).CheckedChanged += ToolStripCheckBox_CheckedChanged;
            ((CheckBox)c).CheckStateChanged += ToolStripCheckBox_CheckStateChanged;
        }

        protected override void OnUnsubscribeControlEvents(Control c)
        {
            base.OnUnsubscribeControlEvents(c);
            ((CheckBox)c).CheckedChanged -= ToolStripCheckBox_CheckedChanged;
            ((CheckBox)c).CheckStateChanged -= ToolStripCheckBox_CheckStateChanged;
        }

        private void ToolStripCheckBox_CheckedChanged(object sender, EventArgs e)
        {
            CheckedChanged?.Invoke(this, e);
        }

        private void ToolStripCheckBox_CheckStateChanged(object sender, EventArgs e)
        {
            CheckStateChanged?.Invoke(this, e);
        }
    }
}