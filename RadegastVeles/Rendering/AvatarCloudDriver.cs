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
using System.Diagnostics;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using Silk.NET.OpenGL;
using SkiaSharp;

namespace Radegast.Veles.Rendering;

/// <summary>
/// Emits a particle cloud billboard for an avatar that has not yet loaded its
/// appearance — exactly matching the SL viewer's cloud-puff fallback behavior
/// (see <c>LLVOAvatar::isFullyLoaded()</c> and the cloud prim particle system
/// emitted in <c>LLVOAvatar::updateVisibility()</c>).
/// <para>
/// The cloud uses additive blending over a spherical billboard burst, mimicking
/// the SL viewer's cloud_puff texture particle system (PSYS_PART_INTERP_COLOR_MASK
/// + PSYS_PART_INTERP_SCALE_MASK, emitting 8 particles per burst at 1 Hz with
/// a 3-second lifetime, 50% white fading to transparent over life).
/// </para>
/// </summary>
internal sealed class AvatarCloudDriver : IDisposable
{
    // The key passed to GlViewportControl.SubmitParticles / RemoveParticles.
    // We use a high bit flag so avatar cloud keys never collide with prim LocalIDs
    // (which are uint, fitting in 32 bits). 0xC100_0000_0000_0000 is in the upper
    // 64-bit range well above any uint value.
    private const ulong AvatarCloudKeyFlag = 0xC100_0000_0000_0000UL;

    private readonly ulong              _key;
    private          GlViewportControl? _viewport;
    private          CancellationTokenSource? _cts;
    private          bool               _disposed;

    // World-space position (updated from the avatar streamer as the avatar moves).
    private Vector3       _worldPos;
    private readonly object _lock = new();

    // Simple cloud particle state: an array of live cloud puffs.
    private sealed class Puff
    {
        public Vector3   Pos;       // offset from emitter centre
        public float     Age;       // seconds since spawn
        public float     Lifetime;  // seconds
        public float     StartScale;
        public float     EndScale;
        public float     Angle;     // random rotation for variety
    }

    private readonly Puff[] _puffs = new Puff[16];
    private readonly Random _rng   = new();

    // The soft puff sprite only needs to be uploaded once: GlViewportControl's particle
    // drain keeps the previously-uploaded GlTexture for a submission whose Texture is
    // null, so every SubmitFrame after the first just reuses it.
    private bool _spriteSubmitted;

    public AvatarCloudDriver(uint localId, Vector3 worldPos, GlViewportControl viewport)
    {
        // Encode the avatar LocalID in the lower 32 bits with the cloud flag.
        _key      = AvatarCloudKeyFlag | localId;
        _worldPos = worldPos;
        _viewport = viewport;

        for (int i = 0; i < _puffs.Length; i++)
            _puffs[i] = new Puff { Age = float.MaxValue }; // all dead initially
    }

    public void UpdateWorldPos(Vector3 worldPos)
    {
        lock (_lock) _worldPos = worldPos;
    }

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
        _viewport?.RemoveParticles(_key);
        _viewport = null;
    }

    private async Task RunAsync(CancellationToken ct)
    {
        // Burst-emit cloud puffs at 1 Hz (SL default for cloud prim particle system).
        float burstInterval = 1.0f;
        float nextBurst     = 0f;

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

                if (now >= nextBurst)
                {
                    EmitBurst();
                    nextBurst = now + burstInterval;
                }

                TickPuffs(dt);
                SubmitFrame();
            }
        }
        catch (OperationCanceledException) { }
    }

    private void EmitBurst()
    {
        // SL cloud prim: BurstPartCount = 8, BurstRadius = 0.1, particle lifetime = 3 s.
        const int   BurstCount   = 8;
        const float BurstRadius  = 0.1f;
        const float Lifetime     = 3.0f;
        const float StartScale   = 0.4f;
        const float EndScale     = 1.0f;

        int spawned = 0;
        for (int i = 0; i < _puffs.Length && spawned < BurstCount; i++)
        {
            if (_puffs[i].Age < _puffs[i].Lifetime) continue; // still alive
            // Random position within a sphere centred 0.85 m up (avatar torso height).
            float theta = (float)(_rng.NextDouble() * Math.PI * 2.0);
            float phi   = (float)(Math.Acos(2.0 * _rng.NextDouble() - 1.0));
            float r     = BurstRadius * (float)Math.Cbrt(_rng.NextDouble());
            _puffs[i].Pos       = new Vector3(
                r * MathF.Sin(phi) * MathF.Cos(theta),
                r * MathF.Sin(phi) * MathF.Sin(theta),
                r * MathF.Cos(phi) + 0.85f); // offset upward toward torso
            _puffs[i].Age       = 0f;
            _puffs[i].Lifetime  = Lifetime;
            _puffs[i].StartScale = StartScale;
            _puffs[i].EndScale   = EndScale;
            _puffs[i].Angle      = (float)(_rng.NextDouble() * Math.PI * 2.0);
            spawned++;
        }
    }

    private void TickPuffs(float dt)
    {
        foreach (var puff in _puffs)
            puff.Age += dt;
    }

    private void SubmitFrame()
    {
        var vp = _viewport;
        if (vp == null) return;

        // Count live puffs.
        int count = 0;
        foreach (var puff in _puffs)
            if (puff.Age < puff.Lifetime) count++;

        if (count == 0) return;

        Vector3 wp;
        lock (_lock) wp = _worldPos;

        var verts = new ParticleVertex[count];
        int vi = 0;
        foreach (var puff in _puffs)
        {
            if (puff.Age >= puff.Lifetime) continue;

            float t     = puff.Age / puff.Lifetime;
            // SL cloud: colour interpolates white→transparent (INTERP_COLOR_MASK)
            float alpha = 1f - t;
            // Scale interpolates start→end (INTERP_SCALE_MASK)
            float scale = puff.StartScale + (puff.EndScale - puff.StartScale) * t;
            verts[vi++] = new ParticleVertex
            {
                Position = puff.Pos,
                Color    = new Vector4(1f, 1f, 1f, alpha),
                HalfW    = scale * 0.5f,
                HalfH    = scale * 0.5f,
                Glow     = 0f,
            };
        }

        // Soft radial-gradient sprite only needs to be submitted once — GlViewportControl's
        // particle drain keeps the previously-uploaded GlTexture when Texture is null, so
        // reuploading every frame would be wasted GPU work.
        SKBitmap? sprite = null;
        if (!_spriteSubmitted)
        {
            sprite = CreatePuffSprite();
            _spriteSubmitted = true;
        }

        // World translation applied via EmitterTransform (particles are emitter-relative).
        vp.SubmitParticles(_key, new ParticleRenderSubmission
        {
            EmitterTransform = Matrix4x4.CreateTranslation(wp),
            Particles        = verts,
            Texture          = sprite,
            // SL cloud uses SRC_ALPHA / ONE_MINUS_SRC_ALPHA blending
            BlendSrc = (int)BlendingFactor.SrcAlpha,
            BlendDst = (int)BlendingFactor.OneMinusSrcAlpha,
        });
    }

    /// <summary>
    /// Procedurally generates a small soft circular sprite (opaque white centre fading
    /// to transparent edge) so cloud puffs render as fluffy blobs instead of the shader's
    /// flat-quad fallback for an untextured particle. Ownership transfers to the caller —
    /// GlViewportControl's particle-submission drain disposes it after the GL upload.
    /// </summary>
    private static SKBitmap CreatePuffSprite()
    {
        const int size = 32;
        var bmp = new SKBitmap(size, size, SKColorType.Rgba8888, SKAlphaType.Unpremul);
        using (var canvas = new SKCanvas(bmp))
        using (var paint = new SKPaint
        {
            Shader = SKShader.CreateRadialGradient(
                new SKPoint(size / 2f, size / 2f), size / 2f,
                [new SKColor(255, 255, 255, 255), new SKColor(255, 255, 255, 0)],
                null, SKShaderTileMode.Clamp),
            IsAntialias = true,
        })
        {
            canvas.Clear(SKColors.Transparent);
            canvas.DrawRect(0, 0, size, size, paint);
        }
        return bmp;
    }
}
