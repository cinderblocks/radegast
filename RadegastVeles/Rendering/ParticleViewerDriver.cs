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
using System.Collections.Generic;
using System.Diagnostics;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using LibreMetaverse;
using SkiaSharp;
using Radegast.Veles.Core;
using OmVector3    = LibreMetaverse.Vector3;
using OmQuaternion = LibreMetaverse.Quaternion;
using Vector3      = System.Numerics.Vector3;
using Vector4      = System.Numerics.Vector4;

namespace Radegast.Veles.Rendering;

/// <summary>
/// Drives the CPU particle simulation for all emitter prims in a linkset
/// and submits billboard quads to a <see cref="GlViewportControl"/> at ~30 Hz.
/// <para>
/// Usage: construct, call <see cref="SetViewport"/>, then <see cref="StartAsync"/>.
/// Call <see cref="Dispose"/> when the viewer closes.
/// </para>
/// </summary>
internal sealed class ParticleViewerDriver : IDisposable
{
    private readonly GridClient               _client;
    private readonly IReadOnlyList<Primitive> _prims;
    private readonly ulong                    _key;
    private          GlViewportControl?       _viewport;
    private          CancellationTokenSource? _cts;
    private          bool                     _disposed;

    // World-space position of the root emitter prim (updated externally).
    private Vector3 _worldPos;
    private readonly object _worldPosLock = new();

    // Per-emitter state (one entry per prim that has a particle system).
    private sealed class EmitterState
    {
        public readonly Primitive           Prim;
        public readonly ParticleSimulator   Sim;
        public          SKBitmap?           PendingBmp; // downloaded async, consumed once
        public          Matrix4x4           Transform;  // object-space transform

        public EmitterState(Primitive prim, ParticleSimulator sim, Matrix4x4 transform)
        {
            Prim      = prim;
            Sim       = sim;
            Transform = transform;
        }
    }

    public ParticleViewerDriver(GridClient client, IReadOnlyList<Primitive> prims, ulong key, Vector3 worldPos)
    {
        _client   = client;
        _prims    = prims;
        _key      = key;
        _worldPos = worldPos;
    }

    /// <summary>Update the world-space root position (called when the prim moves).</summary>
    public void UpdateWorldPos(Vector3 worldPos)
    {
        lock (_worldPosLock) _worldPos = worldPos;
    }

    public void SetViewport(GlViewportControl viewport) => _viewport = viewport;

    public void Start()
    {
        _cts = new CancellationTokenSource();
        _ = RunAsync(_cts.Token);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = null;
        // Remove this emitter's particle submission from the viewport.
        _viewport?.RemoveParticles(_key);
    }

    // ── Loop ──────────────────────────────────────────────────────────────────────

    private async Task RunAsync(CancellationToken ct)
    {
        // Build per-emitter state for every prim that has a non-trivial particle system.
        var emitters = BuildEmitters();
        if (emitters.Count == 0) return;

        // Kick off texture downloads in the background (non-blocking).
        foreach (var em in emitters)
            _ = DownloadParticleTextureAsync(em, ct);

        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1.0 / 30.0));
        var sw   = Stopwatch.StartNew();
        float prev = 0f;

        try
        {
            while (await timer.WaitForNextTickAsync(ct).ConfigureAwait(false))
            {
                float now = (float)sw.Elapsed.TotalSeconds;
                float dt  = Math.Min(now - prev, 0.1f);
                prev = now;

                foreach (var em in emitters)
                {
                    em.Sim.Tick(dt);
                    SubmitEmitter(em);
                }
            }
        }
        catch (OperationCanceledException) { }
    }

    private List<EmitterState> BuildEmitters()
    {
        var result = new List<EmitterState>();
        if (_prims.Count == 0) return result;

        // Root prim's rotation (for object-relative emission).
        var rootRot = _prims[0].Rotation;

        for (int i = 0; i < _prims.Count; i++)
        {
            var prim = _prims[i];
            if (prim.ParticleSys.Pattern == Primitive.ParticleSystem.SourcePattern.None &&
                prim.ParticleSys.BurstPartCount == 0)
                continue;  // no particle system configured

            var sim = new ParticleSimulator(prim.ParticleSys)
            {
                SourceRotation = new OmQuaternion(
                    prim.Rotation.X, prim.Rotation.Y,
                    prim.Rotation.Z, prim.Rotation.W)
            };

            // Build a simple object-space translation transform for child prims.
            Matrix4x4 transform;
            if (i == 0)
            {
                transform = Matrix4x4.Identity;
            }
            else
            {
                // Rotate child position by root prim's rotation and offset.
                var pos = RotateVec(prim.Position, rootRot);
                transform = Matrix4x4.CreateTranslation(pos.X, pos.Y, pos.Z);
            }

            result.Add(new EmitterState(prim, sim, transform));
        }

        return result;
    }

    private void SubmitEmitter(EmitterState em)
    {
        var vp = _viewport;
        if (vp == null) return;

        var liveParticles = em.Sim.GetParticles();
        if (liveParticles.Count == 0) return;

        // Consume pending texture bitmap if one has been downloaded.
        SKBitmap? bmp = null;
        if (em.PendingBmp != null)
        {
            bmp = em.PendingBmp;
            em.PendingBmp = null;
        }

        var pverts = new ParticleVertex[liveParticles.Count];
        for (int i = 0; i < liveParticles.Count; i++)
        {
            var p = liveParticles[i];
            pverts[i] = new ParticleVertex
            {
                Position = new Vector3(p.Position.X, p.Position.Y, p.Position.Z),
                Color    = new Vector4(p.Color.R, p.Color.G, p.Color.B, p.Color.A),
                HalfW    = p.ScaleX * 0.5f,
                HalfH    = p.ScaleY * 0.5f,
                Glow     = p.Glow,
            };
        }

        // Apply world-space translation of the root prim so particles appear at the
        // correct sim-local position regardless of the child prim's local offset.
        Vector3 wp;
        lock (_worldPosLock) wp = _worldPos;
        var worldTranslation = Matrix4x4.CreateTranslation(wp);
        var emitterWorld = em.Transform * worldTranslation;

        vp.SubmitParticles(_key, new ParticleRenderSubmission
        {
            EmitterTransform = emitterWorld,
            Particles        = pverts,
            Texture          = bmp,
            BlendSrc         = MapBlendFunc(em.Prim.ParticleSys.BlendFuncSource),
            BlendDst         = MapBlendFunc(em.Prim.ParticleSys.BlendFuncDest),
        });
    }

    private async Task DownloadParticleTextureAsync(EmitterState em, CancellationToken ct)
    {
        var texId = em.Prim.ParticleSys.Texture;
        if (texId == UUID.Zero) return;

        try
        {
            var bmp = await GridTextureHelper.DownloadSkBitmapAsync(_client, texId, null, ct)
                                             .ConfigureAwait(false);
            if (bmp != null)
                em.PendingBmp = bmp;
        }
        catch { /* non-fatal */ }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────────

    private static OmVector3 RotateVec(OmVector3 v, OmQuaternion q)
    {
        return v * q;
    }

    private static int MapBlendFunc(byte slBlend)
    {
        // Maps Primitive.ParticleSystem.BlendFunc enum to OpenTK BlendingFactor int values.
        // Matches lldrawpoolalpha.cpp blend func mapping in the SL viewer.
        return slBlend switch
        {
            0 => (int)Silk.NET.OpenGL.BlendingFactor.One,
            1 => (int)Silk.NET.OpenGL.BlendingFactor.Zero,
            2 => (int)Silk.NET.OpenGL.BlendingFactor.DstColor,
            3 => (int)Silk.NET.OpenGL.BlendingFactor.SrcColor,
            4 => (int)Silk.NET.OpenGL.BlendingFactor.OneMinusDstColor,
            5 => (int)Silk.NET.OpenGL.BlendingFactor.OneMinusSrcColor,
            6 => (int)Silk.NET.OpenGL.BlendingFactor.DstAlpha,
            7 => (int)Silk.NET.OpenGL.BlendingFactor.SrcAlpha,
            8 => (int)Silk.NET.OpenGL.BlendingFactor.OneMinusDstAlpha,
            9 => (int)Silk.NET.OpenGL.BlendingFactor.OneMinusSrcAlpha,
            _ => (int)Silk.NET.OpenGL.BlendingFactor.SrcAlpha,
        };
    }
}
