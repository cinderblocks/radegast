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
using Silk.NET.OpenGL;

namespace Radegast.Veles.Rendering;

/// <summary>
/// Per-frame statistics published by <see cref="FrameStatsTracker"/>.
/// All durations are wall-clock; <see cref="GpuTimeMs"/> may be <c>0</c> on
/// drivers that do not expose <c>GL_TIME_ELAPSED</c> queries.
/// </summary>
public readonly record struct FrameStats(
    double CpuTimeMs,
    double GpuTimeMs,
    int    DrawCalls,
    int    Triangles,
    int    FacesSubmitted,
    int    FacesCulled);

/// <summary>
/// Lightweight frame-stat tracker for the Veles GL renderer.
/// <para>
/// Owns a small ring of <c>GL_TIME_ELAPSED</c> query objects so the GPU pipeline never
/// stalls waiting for results — each frame begins a query and reads back a previous
/// query that is already complete. CPU timing uses <see cref="Stopwatch"/>.
/// </para>
/// <para>
/// All methods must be called on the GL thread. If timer queries are not supported
/// the tracker silently degrades to CPU-only timing (<see cref="FrameStats.GpuTimeMs"/>
/// will be <c>0</c>).
/// </para>
/// </summary>
public sealed class FrameStatsTracker : IDisposable
{
    private const int QueryRing = 4;

    private readonly uint[]  _queries = new uint[QueryRing];
    private readonly bool[]  _started = new bool[QueryRing];
    private int    _writeIndex;
    private bool   _supported;
    private bool   _initialized;
    private bool   _disposed;

    private readonly Stopwatch _cpu = new();
    private int    _drawCalls;
    private int    _triangles;
    private int    _facesSubmitted;
    private int    _facesCulled;

    private double _lastGpuMs;

    /// <summary>The most recent published <see cref="FrameStats"/> value.</summary>
    public FrameStats Last { get; private set; }

    /// <summary>Fired (on the GL thread) once per frame after <see cref="EndFrame"/>.</summary>
    public event Action<FrameStats>? FrameCompleted;

    /// <summary>Allocate the GL timer-query ring. Must be called on the GL thread.</summary>
    public void Initialize()
    {
        if (_initialized || _disposed) return;
        _initialized = true;
        try
        {
            // GL_TIME_ELAPSED queries are desktop-only. On GL ES / ANGLE the
            // glGetQueryObjectiv entry point is not exported, and calling the
            // unbound delegate produces an ExecutionEngineException.
            var gl = GlApi.Gl;
            var version = gl.GetStringS(StringName.Version) ?? "";
            if (version.Contains("OpenGL ES"))
            {
                _supported = false;
                return;
            }

            gl.GenQueries((uint)QueryRing, _queries);
            // GenQueries doesn't fail on unsupported drivers — verify by issuing one Begin/End pair.
            gl.BeginQuery(QueryTarget.TimeElapsed, _queries[0]);
            gl.EndQuery(QueryTarget.TimeElapsed);
            // If the driver doesn't support it BeginQuery returns silently;
            // we can still read GetError to confirm.
            var err = gl.GetError();
            _supported = err == GLEnum.NoError;
        }
        catch
        {
            _supported = false;
        }
    }

    /// <summary>Start CPU timing and (if supported) a fresh GPU query.</summary>
    public void BeginFrame()
    {
        if (_disposed) return;
        _drawCalls      = 0;
        _triangles      = 0;
        _facesSubmitted = 0;
        _facesCulled    = 0;
        _cpu.Restart();

        if (!_supported) return;

        // Try to read back the oldest query (write index points to next slot, oldest is one ahead).
        var gl = GlApi.Gl;
        int readSlot = (_writeIndex + 1) % QueryRing;
        if (_started[readSlot])
        {
            gl.GetQueryObject(_queries[readSlot], QueryObjectParameterName.QueryResultAvailable, out int avail);
            if (avail != 0)
            {
                gl.GetQueryObject(_queries[readSlot], QueryObjectParameterName.QueryResult, out long ns);
                _lastGpuMs = ns / 1_000_000.0;
                _started[readSlot] = false;
            }
        }

        gl.BeginQuery(QueryTarget.TimeElapsed, _queries[_writeIndex]);
        _started[_writeIndex] = true;
    }

    /// <summary>Close the active GPU query and publish a <see cref="FrameStats"/> value.</summary>
    public void EndFrame()
    {
        if (_disposed) return;
        _cpu.Stop();

        if (_supported)
        {
            GlApi.Gl.EndQuery(QueryTarget.TimeElapsed);
            _writeIndex = (_writeIndex + 1) % QueryRing;
        }

        var stats = new FrameStats(
            CpuTimeMs:      _cpu.Elapsed.TotalMilliseconds,
            GpuTimeMs:      _lastGpuMs,
            DrawCalls:      _drawCalls,
            Triangles:      _triangles,
            FacesSubmitted: _facesSubmitted,
            FacesCulled:    _facesCulled);
        Last = stats;
        FrameCompleted?.Invoke(stats);
    }

    /// <summary>Record a single draw call covering <paramref name="indexCount"/> indices.</summary>
    public void RecordDraw(int indexCount)
    {
        _drawCalls++;
        _triangles += indexCount / 3;
    }

    /// <summary>Increment the face-submitted counter (called regardless of cull result).</summary>
    public void RecordFaceConsidered()  => _facesSubmitted++;

    /// <summary>Increment the face-culled counter when a face is rejected by the frustum test.</summary>
    public void RecordFaceCulled()      => _facesCulled++;

    /// <summary>
    /// Resets the tracker so it can be re-initialized after a GL context teardown and rebuild
    /// (e.g. when Avalonia destroys and recreates the GL surface on a tab switch).
    /// Clears <see cref="_disposed"/> and <see cref="_initialized"/> so a subsequent call to
    /// <see cref="Initialize"/> will allocate new query objects in the fresh context.
    /// Must be called on the GL thread before <see cref="Initialize"/>.
    /// </summary>
    public void Reset()
    {
        // The old GL context that owned _queries is gone — do NOT attempt to delete them.
        _writeIndex   = 0;
        _supported    = false;
        _initialized  = false;
        _disposed     = false;
        _lastGpuMs    = 0;
        _drawCalls    = 0;
        _triangles    = 0;
        _facesSubmitted = 0;
        _facesCulled  = 0;
        Array.Clear(_started, 0, _started.Length);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_initialized && _supported)
        {
            try { GlApi.Gl.DeleteQueries((uint)QueryRing, _queries); } catch { /* context may already be gone */ }
        }
    }
}
