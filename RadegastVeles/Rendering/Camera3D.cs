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

using System;
using OpenTK.Mathematics;

namespace Radegast.Veles.Rendering;

/// <summary>
/// Orbit / arcball camera that revolves around a <see cref="Target"/> point.
/// <para>
/// Controls:
/// <list type="bullet">
///   <item>Left-drag → orbit (yaw + pitch)</item>
///   <item>Right-drag or Ctrl+left-drag → pan target</item>
///   <item>Scroll wheel → zoom (exponential)</item>
/// </list>
/// </para>
/// </summary>
public sealed class Camera3D
{
    /// <summary>World-space point the camera orbits around.</summary>
    public Vector3 Target   { get; set; } = Vector3.Zero;

    /// <summary>Distance from the eye to <see cref="Target"/>.</summary>
    public float Distance   { get; set; } = 3.0f;

    /// <summary>Horizontal rotation in degrees.</summary>
    public float Yaw        { get; set; } = 45f;

    /// <summary>Vertical tilt in degrees; clamped to ±89°.</summary>
    public float Pitch      { get; set; } = 20f;

    /// <summary>Vertical field of view in degrees.</summary>
    public float Fov        { get; set; } = 50f;

    public float Near       { get; set; } = 0.01f;
    public float Far        { get; set; } = 500f;

    /// <summary>Computed eye position in world space (Z-up convention).</summary>
    public Vector3 EyePosition
    {
        get
        {
            float y = MathHelper.DegreesToRadians(Yaw);
            float p = MathHelper.DegreesToRadians(Pitch);
            return Target + new Vector3(
                Distance * MathF.Cos(p) * MathF.Cos(y),
                Distance * MathF.Cos(p) * MathF.Sin(y),
                Distance * MathF.Sin(p));
        }
    }

    // Z-up view matrix; matches Second Life's coordinate system.
    public Matrix4 GetViewMatrix() =>
        Matrix4.LookAt(EyePosition, Target, Vector3.UnitZ);

    public Matrix4 GetProjectionMatrix(float aspect) =>
        Matrix4.CreatePerspectiveFieldOfView(
            MathHelper.DegreesToRadians(Fov), aspect, Near, Far);

    /// <summary>Orbit around target on left-button drag.</summary>
    public void OrbitDrag(float dx, float dy)
    {
        Yaw   += dx * 0.4f;
        Pitch  = Math.Clamp(Pitch + dy * 0.4f, -89f, 89f);
    }

    /// <summary>Step-orbit by exact degree amounts (for button-driven navigation).</summary>
    public void OrbitStep(float dyaw, float dpitch)
    {
        Yaw   += dyaw;
        Pitch  = Math.Clamp(Pitch + dpitch, -89f, 89f);
    }

    /// <summary>Pan the target on right-button drag or Ctrl+left drag.</summary>
    public void PanDrag(float dx, float dy)
    {
        var view  = GetViewMatrix();
        var right = new Vector3(view.Row0.X, view.Row0.Y, view.Row0.Z);
        var up    = new Vector3(view.Row1.X, view.Row1.Y, view.Row1.Z);
        float s   = Distance * 0.002f;
        Target   -= right * dx * s;
        Target   += up    * dy * s;
    }

    /// <summary>Exponential zoom on mouse-wheel scroll.</summary>
    public void Zoom(float delta) =>
        Distance = Math.Clamp(Distance * MathF.Pow(0.9f, delta), 0.05f, 200f);

    /// <summary>Reframe the camera to fit the given AABB.</summary>
    public void FrameBounds(Vector3 min, Vector3 max)
    {
        Target   = (min + max) * 0.5f;
        Distance = Math.Max((max - min).Length * 1.5f, 0.5f);
        Yaw   = 45f;
        Pitch = 20f;
    }

    /// <summary>Reframe facing front-on (for HUDs and flat panels).</summary>
    public void FrameBoundsFront(Vector3 min, Vector3 max)
    {
        Target   = (min + max) * 0.5f;
        Distance = Math.Max((max - min).Length * 1.5f, 0.5f);
        // Legacy camera sits at −Y looking toward +Y with a 90° world yaw applied to
        // the mesh, which is mathematically equivalent to placing our camera at −X
        // (Yaw=180°) looking toward +X with no world rotation.
        Yaw   = 180f;
        Pitch = 0f;
    }
}
