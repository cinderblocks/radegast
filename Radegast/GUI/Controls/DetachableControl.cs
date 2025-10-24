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

using System.Drawing;
using System.Windows.Forms;

namespace Radegast
{
    public class DetachableControl : UserControl
    {
        private bool detached = false;
        private Form detachedForm;

        public delegate void DetachedCallback();
        public event DetachedCallback OnDetached;

        public delegate void ReattachedCallback();
        public event ReattachedCallback OnReattach;

        protected Control OriginalParent;
        protected Size AttachedSize;

        /// <summary>
        /// If in detached state and detached form is closing, and we have no parent
        /// do we dispose ourselves</summary>
        public bool DisposeOnDetachedClose = true;

        public DetachableControl()
        {
            SizeChanged += DetachableControl_SizeChanged;
        }

        private void DetachableControl_SizeChanged(object sender, System.EventArgs e)
        {
            if (detached)
            {
                detachedForm.ClientSize = Size;
            }
        }

        public virtual bool Detached
        {
            get => detached;

            set
            {
                if (detached == value) return; // no change

                detached = value;

                if (detached)
                {
                    Detach();
                }
                else
                {
                    Retach();
                }
            }
        }

        public virtual void ShowDetached()
        {
            Detached = true;
        }

        protected virtual void Detach()
        {
            detached = true;
            AttachedSize = Size;

            detachedForm?.Dispose();

            detachedForm =
                new RadegastForm(RadegastInstanceForms.Instance)
                {
                    SettingsKeyBase = GetType().ToString(),
                    AutoSavePosition = true,
                    Icon = Properties.Resources.radegast_icon
                };
            OriginalParent = Parent;
            Parent = detachedForm;
            Dock = DockStyle.Fill;
            detachedForm.ClientSize = Size;
            SetTitle();
            detachedForm.ActiveControl = this;
            detachedForm.Show();
            detachedForm.FormClosing += detachedForm_FormClosing;

            if (OriginalParent == null)
            {
                ControlIsNotRetachable();
            }
            else
            {
                OriginalParent.ControlAdded += originalParent_ControlAdded;
            }

            OnDetached?.Invoke();
        }

        protected virtual void ControlIsNotRetachable() { }

        private void originalParent_ControlAdded(object sender, ControlEventArgs e)
        {
            if (detachedForm != null)
            {
                detachedForm.FormClosing -= detachedForm_FormClosing;
            }
            OriginalParent.ControlAdded -= originalParent_ControlAdded;
            ControlIsNotRetachable();
        }

        private void detachedForm_FormClosing(object sender, FormClosingEventArgs e)
        {
            Retach();
        }

        protected virtual void Retach()
        {
            detached = false;

            if (OriginalParent != null)
            {
                OriginalParent.ControlAdded -= originalParent_ControlAdded;
                Size = AttachedSize;
            }

            Parent = OriginalParent;

            if (detachedForm != null)
            {
                detachedForm.Dispose();
                detachedForm = null;
            }

            if (OriginalParent == null && DisposeOnDetachedClose)
            {
                Dispose();
            }

            OnReattach?.Invoke();
        }

        protected virtual void SetTitle()
        {
            if (detached)
            {
                FindForm().Text = Text + " - " + Properties.Resources.ProgramName;
            }
        }
    }
}