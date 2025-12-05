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
using System.Drawing;
using System.Windows.Forms;
using System.Threading;
using OpenMetaverse;

namespace Radegast.Rendering
{
    /// <summary>
    /// UI-related functionality for SceneWindow
    /// </summary>
    public partial class SceneWindow
    {
        #region Mouse handling

        private bool dragging = false;
        private int dragX, dragY;

        private void glControl_MouseWheel(object sender, MouseEventArgs e)
        {
            Camera.MoveToTarget(e.Delta / -500f);
        }

        private SceneObject RightclickedObject;
        private int RightclickedFaceID;
        private int LeftclickedFaceID;

        private Vector3 RightclickedPosition;

        private void glControl_MouseDown(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                dragging = true;
                var downX = dragX = e.X;
                var downY = dragY = e.Y;

                if (ModifierKeys == Keys.None)
                {
                    if (TryPick(e.X, e.Y, out var picked, out LeftclickedFaceID))
                    {
                        if (picked is RenderPrimitive primitive)
                        {
                            TryTouchObject(primitive);
                        }
                    }
                }
                else if (ModifierKeys == Keys.Alt)
                {
                    if (TryPick(e.X, e.Y, out var picked, out var LeftclickedFaceID, out var worldPosition))
                    {
                        trackedObject = null;
                        Camera.FocalPoint = worldPosition;
                        var screenCenter = new Point(glControl.Width / 2, glControl.Height / 2);
                        Cursor.Position = glControl.PointToScreen(screenCenter);
                        downX = dragX = screenCenter.X;
                        downY = dragY = screenCenter.Y;
                        Cursor.Hide();
                    }
                }
            }
            else if (e.Button == MouseButtons.Right)
            {
                RightclickedObject = null;
                if (TryPick(e.X, e.Y, out var picked, out RightclickedFaceID, out RightclickedPosition))
                {
                    if (picked is SceneObject sceneObject)
                    {
                        RightclickedObject = sceneObject;
                    }
                }
                ctxMenu.Show(glControl, e.X, e.Y);
            }
        }

        private RenderPrimitive m_currentlyTouchingObject = null;
        private void TryTouchObject(RenderPrimitive LeftclickedObject)
        {
            if ((LeftclickedObject.Prim.Flags & PrimFlags.Touch) != 0)
            {
                if (m_currentlyTouchingObject != null)
                {
                    if (m_currentlyTouchingObject.Prim.LocalID != LeftclickedObject.Prim.LocalID)
                    {
                        //Changed what we are touching... stop touching the old one
                        TryEndTouchObject();

                        //Then set the new one and touch it for the first time
                        m_currentlyTouchingObject = LeftclickedObject;
                        Client.Self.Grab(LeftclickedObject.Prim.LocalID, Vector3.Zero, Vector3.Zero, Vector3.Zero, LeftclickedFaceID, Vector3.Zero, Vector3.Zero, Vector3.Zero);
                        Client.Self.GrabUpdate(LeftclickedObject.Prim.ID, Vector3.Zero);
                    }
                    else
                        Client.Self.GrabUpdate(LeftclickedObject.Prim.ID, Vector3.Zero, Vector3.Zero, Vector3.Zero, Vector3.Zero, LeftclickedFaceID, Vector3.Zero, Vector3.Zero, Vector3.Zero);
                }
                else
                {
                    m_currentlyTouchingObject = LeftclickedObject;
                    Client.Self.Grab(LeftclickedObject.Prim.LocalID, Vector3.Zero, Vector3.Zero, Vector3.Zero, LeftclickedFaceID, Vector3.Zero, Vector3.Zero, Vector3.Zero);
                    Client.Self.GrabUpdate(LeftclickedObject.Prim.ID, Vector3.Zero, Vector3.Zero, Vector3.Zero, Vector3.Zero, LeftclickedFaceID, Vector3.Zero, Vector3.Zero, Vector3.Zero);
                }
            }
        }

        private void TryEndTouchObject()
        {
            if (m_currentlyTouchingObject != null)
                Client.Self.DeGrab(m_currentlyTouchingObject.Prim.LocalID, Vector3.Zero, Vector3.Zero, LeftclickedFaceID, Vector3.Zero, Vector3.Zero, Vector3.Zero);
            m_currentlyTouchingObject = null;
        }

        private void glControl_MouseMove(object sender, MouseEventArgs e)
        {
            if (dragging)
            {
                var deltaX = e.X - dragX;
                var deltaY = e.Y - dragY;
                var pixelToM = 1f / 75f;
                if (e.Button == MouseButtons.Left)
                {
                    if (ModifierKeys == Keys.None)
                    {
                        //Only touch if we aren't doing anything else
                        if (TryPick(e.X, e.Y, out var picked, out var LeftclickedFaceID))
                        {
                            if (picked is RenderPrimitive primitive)
                            {
                                TryTouchObject(primitive);
                            }
                        }
                        else
                        {
                            TryEndTouchObject();
                        }
                    }

                    // Pan
                    if (ModifierKeys == Keys.Control || ModifierKeys == (Keys.Alt | Keys.Control | Keys.Shift))
                    {
                        Camera.Pan(deltaX * pixelToM * 2, deltaY * pixelToM * 2);
                    }

                    // Alt-zoom (up down move camera closer to target, left right rotate around target)
                    if (ModifierKeys == Keys.Alt)
                    {
                        Camera.MoveToTarget(deltaY * pixelToM);
                        Camera.Rotate(-deltaX * pixelToM, true);
                    }

                    // Rotate camera in a vertical circle around target on up down mouse movement
                    if (ModifierKeys == (Keys.Alt | Keys.Control))
                    {
                        Camera.Rotate(deltaY * pixelToM, false);
                        Camera.Rotate(-deltaX * pixelToM, true);
                    }
                }

                dragX = e.X;
                dragY = e.Y;
            }
        }

        private void glControl_MouseUp(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                dragging = false;
                Cursor.Show();

                if (ModifierKeys == Keys.None)
                {
                    TryEndTouchObject();//Stop touching no matter whether we are touching anything
                }
            }
        }
        #endregion Mouse handling

        #region Keyboard

        private float upKeyHeld = 0;
        private bool isHoldingHome = false;
        ///<summary>The time before we fly instead of trying to jump (in seconds)</summary>
        private const float upKeyHeldBeforeFly = 0.5f;

        private void CheckKeyboard(float time)
        {
            if (ModifierKeys == Keys.None)
            {
                // Movement forwards and backwards and body rotation
                Client.Self.Movement.AtPos = Instance.Keyboard.IsKeyDown(Keys.Up);
                Client.Self.Movement.AtNeg = Instance.Keyboard.IsKeyDown(Keys.Down);
                Client.Self.Movement.TurnLeft = Instance.Keyboard.IsKeyDown(Keys.Left);
                Client.Self.Movement.TurnRight = Instance.Keyboard.IsKeyDown(Keys.Right);

                if (Client.Self.Movement.Fly)
                {
                    //Find whether we are going up or down
                    Client.Self.Movement.UpPos = Instance.Keyboard.IsKeyDown(Keys.PageUp);
                    Client.Self.Movement.UpNeg = Instance.Keyboard.IsKeyDown(Keys.PageDown);
                    //The nudge positions are required to land (at least Neg is, unclear whether we should send Pos)
                    Client.Self.Movement.NudgeUpPos = Client.Self.Movement.UpPos;
                    Client.Self.Movement.NudgeUpNeg = Client.Self.Movement.UpNeg;
                    if (Client.Self.Velocity.Z > 0 && Client.Self.Movement.UpNeg)//HACK: Sometimes, stop fly fails
                        Client.Self.Fly(false);//We've hit something, stop flying
                }
                else
                {
                    //Don't send the nudge pos flags, we don't need them
                    Client.Self.Movement.NudgeUpPos = false;
                    Client.Self.Movement.NudgeUpNeg = false;
                    Client.Self.Movement.UpPos = Instance.Keyboard.IsKeyDown(Keys.PageUp);
                    Client.Self.Movement.UpNeg = Instance.Keyboard.IsKeyDown(Keys.PageDown);
                }
                if (Instance.Keyboard.IsKeyDown(Keys.Home))//Flip fly settings
                {
                    //Holding the home key only makes it change once, 
                    // not flip over and over, so keep track of it
                    if (!isHoldingHome)
                    {
                        Client.Self.Movement.Fly = !Client.Self.Movement.Fly;
                        isHoldingHome = true;
                    }
                }
                else
                    isHoldingHome = false;

                if (!Client.Self.Movement.Fly &&
                    Instance.Keyboard.IsKeyDown(Keys.PageUp))
                {
                    upKeyHeld += time;
                    if (upKeyHeld > upKeyHeldBeforeFly)//Wait for a bit before we fly, they may be trying to jump
                        Client.Self.Movement.Fly = true;
                }
                else
                    upKeyHeld = 0;//Reset the count


                if (Client.Self.Movement.TurnLeft)
                {
                    Client.Self.Movement.BodyRotation *= Quaternion.CreateFromAxisAngle(Vector3.UnitZ, time);
                }
                else if (client.Self.Movement.TurnRight)
                {
                    Client.Self.Movement.BodyRotation *= Quaternion.CreateFromAxisAngle(Vector3.UnitZ, -time);
                }

                if (Instance.Keyboard.IsKeyDown(Keys.Escape))
                {
                    InitCamera();
                    Camera.Manual = false;
                    trackedObject = myself;
                }
            }
            else if (ModifierKeys == Keys.Shift)
            {
                // Strafe
                Client.Self.Movement.LeftNeg = Instance.Keyboard.IsKeyDown(Keys.Right);
                Client.Self.Movement.LeftPos = Instance.Keyboard.IsKeyDown(Keys.Left);
            }
            else if (ModifierKeys == Keys.Alt)
            {
                // Camera horizontal rotation
                if (Instance.Keyboard.IsKeyDown(Keys.Left))
                {
                    Camera.Rotate(-time, true);
                }
                else if (Instance.Keyboard.IsKeyDown(Keys.Right))
                {
                    Camera.Rotate(time, true);
                } // Camera vertical rotation
                else if (Instance.Keyboard.IsKeyDown(Keys.PageDown))
                {
                    Camera.Rotate(-time, false);
                }
                else if (Instance.Keyboard.IsKeyDown(Keys.PageUp))
                {
                    Camera.Rotate(time, false);
                } // Camera zoom
                else if (Instance.Keyboard.IsKeyDown(Keys.Down))
                {
                    Camera.MoveToTarget(time);
                }
                else if (Instance.Keyboard.IsKeyDown(Keys.Up))
                {
                    Camera.MoveToTarget(-time);
                }
            }
            else if (ModifierKeys == (Keys.Alt | Keys.Control))
            {
                // Camera horizontal rotation
                if (Instance.Keyboard.IsKeyDown(Keys.Left))
                {
                    Camera.Rotate(-time, true);
                }
                else if (Instance.Keyboard.IsKeyDown(Keys.Right))
                {
                    Camera.Rotate(time, true);
                } // Camera vertical rotation
                else if (Instance.Keyboard.IsKeyDown(Keys.Down))
                {
                    Camera.Rotate(-time, false);
                }
                else if (Instance.Keyboard.IsKeyDown(Keys.Up))
                {
                    Camera.Rotate(time, false);
                }
            }
            else if (ModifierKeys == Keys.Control)
            {
                // Camera pan
                var timeFactor = 3f;

                if (Instance.Keyboard.IsKeyDown(Keys.Left))
                {
                    Camera.Pan(time * timeFactor, 0f);
                }
                else if (Instance.Keyboard.IsKeyDown(Keys.Right))
                {
                    Camera.Pan(-time * timeFactor, 0f);
                }
                else if (Instance.Keyboard.IsKeyDown(Keys.Up))
                {
                    Camera.Pan(0f, time * timeFactor);
                }
                else if (Instance.Keyboard.IsKeyDown(Keys.Down))
                {
                    Camera.Pan(0f, -time * timeFactor);
                }
            }
        }
        #endregion Keyboard

        #region Form controls handlers
        private void btnReset_Click(object sender, EventArgs e)
        {
            InitCamera();
        }

        #endregion Form controls handlers

        #region Context menu
        /// <summary>
        /// Dynamically construct the context menu when we right-click on the screen
        /// </summary>
        /// <param name="csender"></param>
        /// <param name="ce"></param>
        private void ctxObjects_Opening(object csender, System.ComponentModel.CancelEventArgs ce)
        {
            // Clear all context menu items
            ctxMenu.Items.Clear();
            ce.Cancel = false;
            ToolStripMenuItem item;

            // Always add standup button if we are sitting
            if (Instance.State.IsSitting)
            {
                item = new ToolStripMenuItem("Stand Up", null, (sender, e) =>
                {
                    instance.State.SetSitting(false, UUID.Zero);
                });
                ctxMenu.Items.Add(item);
            }

            // Was it prim that was right-clicked
            if (RightclickedObject is RenderPrimitive prim)
            {
                // Sit button handling
                if (!instance.State.IsSitting)
                {
                    item = new ToolStripMenuItem("Sit", null, (sender, e) =>
                    {
                        instance.State.SetSitting(true, prim.Prim.ID);
                    });

                    if (prim.Prim.Properties != null
                        && !string.IsNullOrEmpty(prim.Prim.Properties.SitName))
                    {
                        item.Text = prim.Prim.Properties.SitName;
                    }
                    ctxMenu.Items.Add(item);
                }

                // Is the prim touchable
                if ((prim.Prim.Flags & PrimFlags.Touch) != 0)
                {
                    item = new ToolStripMenuItem("Touch", null, (sender, e) =>
                    {
                        Client.Self.Grab(prim.Prim.LocalID, Vector3.Zero, Vector3.Zero, Vector3.Zero, RightclickedFaceID, Vector3.Zero, Vector3.Zero, Vector3.Zero);
                        Thread.Sleep(100);
                        Client.Self.DeGrab(prim.Prim.LocalID, Vector3.Zero, Vector3.Zero, RightclickedFaceID, Vector3.Zero, Vector3.Zero, Vector3.Zero);
                    });

                    if (prim.Prim.Properties != null
                        && !string.IsNullOrEmpty(prim.Prim.Properties.TouchName))
                    {
                        item.Text = prim.Prim.Properties.TouchName;
                    }
                    ctxMenu.Items.Add(item);
                }

                // Can I delete and take this object?
                if ((prim.Prim.Flags & (PrimFlags.ObjectYouOwner | PrimFlags.ObjectYouOfficer)) != 0)
                {
                    // Take button
                    item = new ToolStripMenuItem("Take", null, (sender, e) =>
                    {
                        instance.MediaManager.PlayUISound(UISounds.ObjectDelete);
                        Client.Inventory.RequestDeRezToInventory(prim.Prim.LocalID);
                    });
                    ctxMenu.Items.Add(item);

                    // Delete button
                    item = new ToolStripMenuItem("Delete", null, (sender, e) =>
                    {
                        instance.MediaManager.PlayUISound(UISounds.ObjectDelete);
                        Client.Inventory.RequestDeRezToInventory(prim.Prim.LocalID, DeRezDestination.AgentInventoryTake, Client.Inventory.FindFolderForType(FolderType.Trash), UUID.Random());
                    });
                    ctxMenu.Items.Add(item);


                }

                // add prim context menu items
                instance.ContextActionManager.AddContributions(ctxMenu, typeof(Primitive), prim.Prim);

            } // We right-clicked on an avatar, add some context menu items
            else if (RightclickedObject is RenderAvatar av)
            {
                // Profile button
                item = new ToolStripMenuItem("Profile", null, (sender, e) =>
                {
                    Instance.ShowAgentProfile(string.Empty, av.avatar.ID);
                });
                ctxMenu.Items.Add(item);

                if (av.avatar.ID != Client.Self.AgentID)
                {
                    // IM button
                    item = new ToolStripMenuItem("Instant Message", null, (sender, e) =>
                    {
                        Instance.TabConsole.ShowIMTab(av.avatar.ID, Instance.Names.Get(av.avatar.ID), true);
                    });
                    ctxMenu.Items.Add(item);

                    // Pay button
                    item = new ToolStripMenuItem("Pay", null, (sender, e) =>
                    {
                        (new frmPay(Instance, av.avatar.ID, Instance.Names.Get(av.avatar.ID), false)).ShowDialog();
                    });
                    ctxMenu.Items.Add(item);
                }

                // add avatar context menu items
                instance.ContextActionManager.AddContributions(ctxMenu, typeof(Avatar), av.avatar.ID);
            }

            // If we are not the sole menu item, add separator
            if (ctxMenu.Items.Count > 0)
            {
                ctxMenu.Items.Add(new ToolStripSeparator());
            }


            // Dock/undock menu item
            var docked = !instance.TabConsole.Tabs["scene_window"].Detached;
            if (docked)
            {
                item = new ToolStripMenuItem("Undock", null, (sender, e) =>
                {
                    instance.TabConsole.SelectDefaultTab();
                    instance.TabConsole.Tabs["scene_window"].Detach(instance);
                });
            }
            else
            {
                item = new ToolStripMenuItem("Dock", null, (sender, e) =>
                {
                    var p = Parent;
                    instance.TabConsole.Tabs["scene_window"].AttachTo(instance.TabConsole.tstTabs, instance.TabConsole.toolStripContainer1.ContentPanel);
                    (p as Form)?.Close();
                });
            }
            ctxMenu.Items.Add(item);

            item = new ToolStripMenuItem("Options", null, (sender, e) =>
            {
                new Floater(Instance, new GraphicsPreferences(Instance), this).Show(FindForm());
            });
            ctxMenu.Items.Add(item);

            // Show hide debug panel
            if (pnlDebug.Visible)
            {
                item = new ToolStripMenuItem("Hide debug panel", null, (sender, e) =>
                {
                    pnlDebug.Visible = false;
                    Instance.GlobalSettings["scene_viewer_debug_panel"] = false;
                });
            }
            else
            {
                item = new ToolStripMenuItem("Show debug panel", null, (sender, e) =>
                {
                    pnlDebug.Visible = true;
                    Instance.GlobalSettings["scene_viewer_debug_panel"] = true;
                });
            }
            ctxMenu.Items.Add(item);
            instance.ContextActionManager.AddContributions(ctxMenu, typeof(Vector3), RightclickedPosition);
        }
        #endregion Context menu

        #region Chat UI handlers

        private void txtChat_TextChanged(object sender, EventArgs e)
        {
            if (txtChat.Text.Length > 0)
            {
                btnSay.Enabled = cbChatType.Enabled = true;
                if (!txtChat.Text.StartsWith("/"))
                {
                    if (!Instance.State.IsTyping && !Instance.GlobalSettings["no_typing_anim"])
                    {
                        Instance.State.SetTyping(true);
                    }
                }
            }
            else
            {
                btnSay.Enabled = cbChatType.Enabled = false;
                if (!Instance.GlobalSettings["no_typing_anim"])
                {
                    Instance.State.SetTyping(false);
                }
            }
        }

        private void txtChat_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode != Keys.Enter) return;
            e.Handled = e.SuppressKeyPress = true;
            var chat = (ChatConsole)Instance.TabConsole.Tabs["chat"].Control;

            if (e.Shift)
                chat.ProcessChatInput(txtChat.Text, ChatType.Whisper);
            else if (e.Control)
                chat.ProcessChatInput(txtChat.Text, ChatType.Shout);
            else
                chat.ProcessChatInput(txtChat.Text, ChatType.Normal);

            txtChat.Text = string.Empty;
        }

        private void btnSay_Click(object sender, EventArgs e)
        {
            var chat = (ChatConsole)Instance.TabConsole.Tabs["chat"].Control;
            chat.ProcessChatInput(txtChat.Text, (ChatType)cbChatType.SelectedIndex);
            txtChat.Select();
            txtChat.Text = string.Empty;
        }

        #endregion Chat UI handlers
    }
}
