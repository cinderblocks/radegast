/*
 * Radegast Metaverse Client
 * Copyright (c) 2026, Sjofn LLC
 * All rights reserved.
 *
 * Radegast is free software: you can redistribute it and/or modify
 * it under the terms of the GNU Lesser General Public License as published
 * by the Free Software Foundation, either version 3 of the License, or
 * (at your option) any later version.
 *
 * This program is distributed in the hope that it will be useful,
 * but WITHOUT ANY WARRANTY; without even the implied warranty of
 * MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the
 * GNU General Public License for more details.
 *
 * You should have received a copy of the GNU Lesser General Public License
 * along with this program. If not, see <https://www.gnu.org/licenses/>.
 */

using LibreMetaverse;

namespace Radegast
{
    /// <summary>
    /// Avatar body yaw. Identity faces east (+X), which is also the value a new
    /// <see cref="AgentManager.AgentMovement"/> starts with.
    /// </summary>
    public static class AvatarFacing
    {
        /// <summary>
        /// Horizontal length below which a look-at has no usable yaw (straight up or down, or unset).
        /// </summary>
        private const float MinHorizontalLengthSquared = 1e-8f;

        /// <summary>
        /// Build the body rotation that faces <paramref name="lookAt"/>.
        /// The simulator stores this as a direction (X = east, Y = north), not a position.
        /// Pitch is ignored so a look slightly above the horizon still restores the compass facing.
        /// </summary>
        public static bool TryGetBodyRotation(Vector3 lookAt, out Quaternion rotation)
        {
            rotation = Quaternion.Identity;
            float horizontal = lookAt.X * lookAt.X + lookAt.Y * lookAt.Y;
            if (horizontal < MinHorizontalLengthSquared)
                return false;

            float yaw = (float)System.Math.Atan2(lookAt.Y, lookAt.X);
            rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, yaw);
            return true;
        }

        /// <summary>
        /// Yaw the body and keep the head looking along it. HeadRotation is an absolute
        /// world orientation; leaving it at the session default keeps the face pointing east.
        /// </summary>
        public static void Yaw(AgentManager.AgentMovement movement, float radians)
        {
            movement.BodyRotation *= Quaternion.CreateFromAxisAngle(Vector3.UnitZ, radians);
            movement.HeadRotation = movement.BodyRotation;
        }
    }
}
