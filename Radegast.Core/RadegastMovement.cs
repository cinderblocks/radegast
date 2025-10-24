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
using System.Timers;
using OpenMetaverse;

namespace Radegast
{
    public class RadegastMovement : IDisposable
    {
        private readonly IRadegastInstance instance;
        private GridClient client => instance.Client;
        private Timer timer;
        private Vector3 forward = new Vector3(1, 0, 0);
        private bool turningLeft = false;
        private bool turningRight = false;
        private bool movingForward = false;
        private bool movingBackward = false;
        private bool crouching = false;
        private bool flying = false;
        private bool jumping = false;
        private bool running = false;

        public bool TurningLeft
        {
            get => turningLeft;
            set {
                turningLeft = value;
                if (value) {
                    timer_Elapsed(null, null);
                    timer.Enabled = true;
                } else {
                    timer.Enabled = false;
                    client.Self.Movement.TurnLeft = false;
                    client.Self.Movement.SendUpdate(true);
                }
            }
        }

        public bool TurningRight
        {
            get => turningRight;
            set
            {
                turningRight = value;
                if (value) {
                    timer_Elapsed(null, null);
                    timer.Enabled = true;
                } else {
                    timer.Enabled = false;
                    client.Self.Movement.TurnRight = false;
                    client.Self.Movement.SendUpdate(true);
                }
            }
        }

        public bool MovingForward
        {
            get => movingForward;
            set
            {
                movingForward = value;
                if (value) {
                    client.Self.Movement.AtPos = true;
                    client.Self.Movement.SendUpdate(true);
                } else {
                    client.Self.Movement.AtPos = false;
                    client.Self.Movement.SendUpdate(true);
                }
            }
        }

        public bool MovingBackward
        {
            get => movingBackward;
            set
            {
                movingBackward = value;
                if (value) {
                    client.Self.Movement.AtNeg = true;
                    client.Self.Movement.SendUpdate(true);
                } else {
                    client.Self.Movement.AtNeg = false;
                    client.Self.Movement.SendUpdate(true);
                    
                }
            }
        }

        public bool Jump
        {
            get => jumping;
            set
            {
                jumping = value;
                client.Self.Jump(value);
            }
        }

        public bool Crouch
        {
            get => crouching;
            set
            {
                crouching = value;
                client.Self.Crouch(value);
            }
        }

        public bool Fly
        {
            get => flying;
            set
            {
                flying = value;
                client.Self.Fly(value);
            }
        }

        public bool AlwaysRun
        {
            get => running;
            set
            {
                running = value;
                client.Self.Movement.AlwaysRun = value;
            }
        }

        public bool ToggleFlight()
        {
            Fly = !Fly;
            return Fly;
        }

        public bool ToggleAlwaysRun()
        {
            AlwaysRun = !AlwaysRun;
            return AlwaysRun;
        }

        public RadegastMovement(IRadegastInstance instance)
        {
            this.instance = instance;
            timer = new Timer(100);
            timer.Elapsed +=timer_Elapsed;
            timer.Enabled = false;
        }

        public void Dispose()
        {
            timer.Enabled = false;
            timer.Dispose();
            timer = null;
        }

        private void timer_Elapsed(object sender, ElapsedEventArgs e)
        {
            float delta = (float)timer.Interval / 1000f;
            if (turningLeft) {
                client.Self.Movement.TurnLeft = true;
                client.Self.Movement.BodyRotation = client.Self.Movement.BodyRotation * Quaternion.CreateFromAxisAngle(Vector3.UnitZ, delta);
                client.Self.Movement.SendUpdate(true);
            } else if (turningRight) {
                client.Self.Movement.TurnRight = true;
                client.Self.Movement.BodyRotation = client.Self.Movement.BodyRotation * Quaternion.CreateFromAxisAngle(Vector3.UnitZ, -delta);
                client.Self.Movement.SendUpdate(true);
            }
        }
    }
}
