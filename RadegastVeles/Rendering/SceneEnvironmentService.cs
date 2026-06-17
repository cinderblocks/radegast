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
using OpenMetaverse.Messages.Linden;
using OpenMetaverse.StructuredData;
using System.Numerics;
using Radegast.Veles.Core;

namespace Radegast.Veles.Rendering;

/// <summary>
/// Bridges EEP / Windlight region environment data from LibreMetaverse to the
/// scene renderer.  Subscribes to <c>RegionEnvironmentUpdated</c>, parses the
/// LLSD day-cycle (EEP and legacy Windlight formats), and computes per-frame sky
/// and water parameters that are sampled by <see cref="GlViewportControl"/> once
/// per render pass via <see cref="GetCurrentSky"/> and
/// <see cref="GetCurrentWaterFogColor"/>.
/// </summary>
public sealed class SceneEnvironmentService : IDisposable
{
    private readonly RadegastInstanceAvalonia _instance;
    private bool _disposed;

    // Day-cycle timing (seconds)
    private int _dayLength = 14400;  // one full cycle (default 4 h)
    private int _dayOffset = 57600;  // offset from Unix epoch to cycle-start

    // Parsed keyframe tracks – sorted by normalised time [0, 1).
    // Written from the network thread, read from the GL thread.
    // List references are replaced atomically; reads always see a consistent snapshot.
    private volatile IReadOnlyList<(double t, ParsedSkyFrame sf)>   _skyTrack;
    private volatile IReadOnlyList<(double t, ParsedWaterFrame wf)> _waterTrack;

    // ── Internal frame types ──────────────────────────────────────────────────────

    private readonly record struct ParsedSkyFrame(
        Quaternion SunRotation,
        Vector3    BlueHorizon,
        Vector3    BlueDensity,
        float      HazeHorizon,
        float      HazeDensity,
        Vector3    SunlightColor,
        Vector3    Ambient,
        float      GlowFocus,
        float      GlowSize
    );

    private readonly record struct ParsedWaterFrame(Vector4 FogColor);

    // ── Constructor / teardown ────────────────────────────────────────────────────

    public SceneEnvironmentService(RadegastInstanceAvalonia instance)
    {
        _instance   = instance;
        _skyTrack   = MakeDefaultSkyTrack();
        _waterTrack = MakeDefaultWaterTrack();

        instance.Client.Environment.RegionEnvironmentUpdated += OnRegionEnvironmentUpdated;

        // Kick off an initial fetch; result arrives via the event.
        _ = instance.Client.Environment.GetRegionEnvironmentAsync();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _instance.Client.Environment.RegionEnvironmentUpdated -= OnRegionEnvironmentUpdated;
    }

    /// <summary>
    /// Called by <see cref="ViewModels.SceneViewerViewModel"/> when the active region changes.
    /// Resets to defaults and triggers an environment re-fetch for the new region.
    /// </summary>
    public void OnRegionChanged()
    {
        _skyTrack   = MakeDefaultSkyTrack();
        _waterTrack = MakeDefaultWaterTrack();
        _ = _instance.Client.Environment.GetRegionEnvironmentAsync();
    }

    // ── GL-thread API ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Returns interpolated <see cref="SkySettings"/> for the current day-cycle time.
    /// Call once per frame from the GL render loop.
    /// </summary>
    public SkySettings GetCurrentSky()
        => InterpolateSky(ComputeProgress(), _skyTrack);

    /// <summary>
    /// Returns interpolated water fog colour for the current day-cycle time.
    /// Call once per frame from the GL render loop.
    /// </summary>
    public Vector4 GetCurrentWaterFogColor()
        => InterpolateWater(ComputeProgress(), _waterTrack);

    // ── Event handlers ────────────────────────────────────────────────────────────

    private void OnRegionEnvironmentUpdated(object? sender, OpenMetaverse.RegionEnvironmentEventArgs e)
        => ParseEnvironment(e.Environment?.Environment);

    // ── Parsing ───────────────────────────────────────────────────────────────────

    private void ParseEnvironment(EnvironmentData? env)
    {
        if (env?.DayCycle == null)
        {
            _skyTrack   = MakeDefaultSkyTrack();
            _waterTrack = MakeDefaultWaterTrack();
            return;
        }
        _dayLength = Math.Max(1, env.DayLength);
        _dayOffset = env.DayOffset;
        ParseDayCycleLlsd(env.DayCycle);
    }

    private void ParseDayCycleLlsd(OSD osd)
    {
        if (osd is not OSDMap map)
        {
            _skyTrack   = MakeDefaultSkyTrack();
            _waterTrack = MakeDefaultWaterTrack();
            return;
        }

        var type = map.ContainsKey("type") ? map["type"].AsString() : string.Empty;

        switch (type)
        {
            case "daycycle":
                ParseFullDayCycle(map);
                break;

            case "sky":
                // Single sky snapshot – no time progression; pin sun at this frame.
                _skyTrack   = TryParseSkyFrame(map, out var sf) ? [(0.0, sf)] : MakeDefaultSkyTrack();
                _waterTrack = MakeDefaultWaterTrack();
                break;

            default:
                _skyTrack   = MakeDefaultSkyTrack();
                _waterTrack = MakeDefaultWaterTrack();
                break;
        }
    }

    private void ParseFullDayCycle(OSDMap map)
    {
        // Build name → raw frame dictionary
        var frames = new Dictionary<string, OSDMap>(StringComparer.OrdinalIgnoreCase);
        if (map.ContainsKey("frames") && map["frames"] is OSDMap framesOsd)
        {
            foreach (var kv in (System.Collections.Generic.IEnumerable<System.Collections.Generic.KeyValuePair<string, OSD>>)framesOsd)
                if (kv.Value is OSDMap fm) frames[kv.Key] = fm;
        }

        var skyKeys   = new List<(double, ParsedSkyFrame)>();
        var waterKeys = new List<(double, ParsedWaterFrame)>();

        if (map.ContainsKey("tracks") && map["tracks"] is OSDArray tracks)
        {
            // Track 0 = water; Track 1 = ground-level sky.
            CollectWaterKeyframes(tracks, 0, frames, waterKeys);
            CollectSkyKeyframes  (tracks, 1, frames, skyKeys);
        }

        _skyTrack   = skyKeys  .Count > 0 ? (IReadOnlyList<(double, ParsedSkyFrame)>)skyKeys   : MakeDefaultSkyTrack();
        _waterTrack = waterKeys.Count > 0 ? (IReadOnlyList<(double, ParsedWaterFrame)>)waterKeys : MakeDefaultWaterTrack();
    }

    private static void CollectSkyKeyframes(
        OSDArray tracks, int trackIdx,
        Dictionary<string, OSDMap> frames,
        List<(double t, ParsedSkyFrame sf)> sink)
    {
        if (trackIdx >= tracks.Count || tracks[trackIdx] is not OSDArray track) return;
        foreach (OSD kfOsd in track)
        {
            if (kfOsd is not OSDMap kf) continue;
            double t    = kf.ContainsKey("key_keyframe") ? kf["key_keyframe"].AsReal() : 0.0;
            string name = kf.ContainsKey("key_name")     ? kf["key_name"].AsString()   : string.Empty;
            if (frames.TryGetValue(name, out var fm) && TryParseSkyFrame(fm, out var sf))
                sink.Add((t, sf));
        }
        sink.Sort(static (a, b) => a.t.CompareTo(b.t));
    }

    private static void CollectWaterKeyframes(
        OSDArray tracks, int trackIdx,
        Dictionary<string, OSDMap> frames,
        List<(double t, ParsedWaterFrame wf)> sink)
    {
        if (trackIdx >= tracks.Count || tracks[trackIdx] is not OSDArray track) return;
        foreach (OSD kfOsd in track)
        {
            if (kfOsd is not OSDMap kf) continue;
            double t    = kf.ContainsKey("key_keyframe") ? kf["key_keyframe"].AsReal() : 0.0;
            string name = kf.ContainsKey("key_name")     ? kf["key_name"].AsString()   : string.Empty;
            if (frames.TryGetValue(name, out var fm))
                sink.Add((t, ParseWaterFrame(fm)));
        }
        sink.Sort(static (a, b) => a.t.CompareTo(b.t));
    }

    // ── Frame parsers ─────────────────────────────────────────────────────────────

    private static bool TryParseSkyFrame(OSDMap map, out ParsedSkyFrame frame)
    {
        frame = default;
        try
        {
            Quaternion sunRot;
            if (map.ContainsKey("sun_rotation"))
            {
                // EEP format: quaternion [x, y, z, w]; rotate (0,0,1) to get sun direction.
                sunRot = NormaliseQuat(ReadQuat(map, "sun_rotation"));
            }
            else
            {
                // Legacy Windlight: reconstruct from sun_angle (elevation) + east_angle (azimuth).
                float sunAngle  = ReadScalar(map, "sun_angle",  MathF.PI / 2f);
                float eastAngle = ReadScalar(map, "east_angle", 0f);
                var sunDir = new Vector3(
                    MathF.Cos(eastAngle) * MathF.Cos(sunAngle),
                    MathF.Sin(eastAngle) * MathF.Cos(sunAngle),
                    MathF.Sin(sunAngle));
                if (sunDir.LengthSquared() < 0.001f) sunDir = Vector3.UnitZ;
                sunRot = QuatFromTo(Vector3.UnitZ, Vector3.Normalize(sunDir));
            }

            var  blueH  = ReadVec3(map, "blue_horizon",   new Vector3(0.4f, 0.4f, 0.9f));
            var  blueD  = ReadVec3(map, "blue_density",   new Vector3(0.2f, 0.4f, 0.4f));
            float hazeH = ReadScalar(map, "haze_horizon", 0.19f);
            float hazeD = ReadScalar(map, "haze_density", 0.70f);
            var  sunC   = ReadVec3(map, "sun_moon_color", new Vector3(0.8f, 0.8f, 0.8f));
            var  amb    = ReadVec3(map, "sky_ambient",    new Vector3(0.25f, 0.25f, 0.25f));

            float[] g     = ReadFloatArr(map, "glow", [5.0f, 0f, -0.01f, 1.0f]);
            float   focus = g[0] / 20.0f;
            float   size  = g.Length > 2 ? Math.Max(0.001f, -g[2]) : 1.75f;

            frame = new ParsedSkyFrame(sunRot, blueH, blueD, hazeH, hazeD, sunC, amb, focus, size);
            return true;
        }
        catch { return false; }
    }

    private static ParsedWaterFrame ParseWaterFrame(OSDMap map) =>
        new(ReadVec4(map, "water_fog_color", new Vector4(0.09f, 0.28f, 0.63f, 0.84f)));

    // ── LLSD helpers ──────────────────────────────────────────────────────────────

    private static Quaternion ReadQuat(OSDMap map, string key)
    {
        if (map.ContainsKey(key) && map[key] is OSDArray a && a.Count >= 4)
            return new Quaternion((float)a[0].AsReal(), (float)a[1].AsReal(),
                                  (float)a[2].AsReal(), (float)a[3].AsReal());
        return Quaternion.Identity;
    }

    private static Vector3 ReadVec3(OSDMap map, string key, Vector3 def)
    {
        if (map.ContainsKey(key) && map[key] is OSDArray a && a.Count >= 3)
            return new Vector3((float)a[0].AsReal(), (float)a[1].AsReal(), (float)a[2].AsReal());
        return def;
    }

    private static Vector4 ReadVec4(OSDMap map, string key, Vector4 def)
    {
        if (map.ContainsKey(key) && map[key] is OSDArray a && a.Count >= 4)
            return new Vector4((float)a[0].AsReal(), (float)a[1].AsReal(),
                               (float)a[2].AsReal(), (float)a[3].AsReal());
        return def;
    }

    private static float ReadScalar(OSDMap map, string key, float def)
    {
        if (!map.ContainsKey(key)) return def;
        var o = map[key];
        if (o is OSDArray a && a.Count > 0) return (float)a[0].AsReal();
        return (float)o.AsReal();
    }

    private static float[] ReadFloatArr(OSDMap map, string key, float[] def)
    {
        if (map.ContainsKey(key) && map[key] is OSDArray a)
        {
            var r = new float[a.Count];
            for (int i = 0; i < a.Count; i++) r[i] = (float)a[i].AsReal();
            return r;
        }
        return def;
    }

    // ── Quaternion helpers ────────────────────────────────────────────────────────

    private static Quaternion NormaliseQuat(Quaternion q) =>
        q.LengthSquared() > 0.0001f ? Quaternion.Normalize(q) : Quaternion.Identity;

    // Build the shortest-arc quaternion that rotates 'from' to 'to'.
    private static Quaternion QuatFromTo(Vector3 from, Vector3 to)
    {
        var  axis = Vector3.Cross(from, to);
        float dot = Vector3.Dot(from, to);
        if (axis.LengthSquared() < 1e-10f)
            return dot >= 0f ? Quaternion.Identity
                             : Quaternion.CreateFromAxisAngle(Vector3.UnitX, MathF.PI);
        float angle = MathF.Acos(Math.Clamp(dot, -1f, 1f));
        return Quaternion.CreateFromAxisAngle(Vector3.Normalize(axis), angle);
    }

    // Rotate vector v by quaternion q: v' = q * v * q⁻¹ (Rodrigues formula).
    private static Vector3 RotateByQuat(Quaternion q, Vector3 v)
    {
        var xyz  = new Vector3(q.X, q.Y, q.Z);
        var temp = Vector3.Cross(xyz, v) + q.W * v;
        return v + 2f * Vector3.Cross(xyz, temp);
    }

    // ── Default tracks ────────────────────────────────────────────────────────────

    private static IReadOnlyList<(double, ParsedSkyFrame)> MakeDefaultSkyTrack()
    {
        var def    = SkySettings.Default;
        var sunDir = Vector3.Normalize(def.SunDirection);
        var sunRot = QuatFromTo(Vector3.UnitZ, sunDir);
        var frame  = new ParsedSkyFrame(sunRot, def.BlueHorizon, def.BlueDensity,
                         def.HazeHorizon, def.HazeDensity, def.SunlightColor,
                         def.Ambient, def.SunGlowFocus, def.SunGlowSize);
        return [(0.0, frame)];  // single frame → no time animation
    }

    private static IReadOnlyList<(double, ParsedWaterFrame)> MakeDefaultWaterTrack() =>
        [(0.0, new ParsedWaterFrame(new Vector4(0.09f, 0.28f, 0.63f, 0.84f)))];

    // ── Per-frame computation ─────────────────────────────────────────────────────

    private double ComputeProgress()
    {
        long nowSec = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        return ((nowSec + _dayOffset) % _dayLength) / (double)_dayLength;
    }

    private static SkySettings InterpolateSky(
        double progress,
        IReadOnlyList<(double t, ParsedSkyFrame sf)> track)
    {
        if (track.Count == 0) return SkySettings.Default;
        if (track.Count == 1) return ToSkySettings(track[0].sf);

        // Find the pair of keyframes that bracket 'progress' (with wrap-around).
        int a = track.Count - 1;
        for (int i = 0; i < track.Count; i++)
        {
            if (track[i].t > progress) { a = i - 1; break; }
        }
        if (a < 0) a = track.Count - 1;
        int b = (a + 1) % track.Count;

        double ta   = track[a].t, tb = track[b].t;
        double span = tb > ta ? tb - ta : (1.0 - ta) + tb;
        double off  = progress >= ta ? progress - ta : (1.0 - ta) + progress;
        float  frac = (float)(span > 0.0 ? off / span : 0.0);

        return LerpSkyFrames(track[a].sf, track[b].sf, frac);
    }

    private static Vector4 InterpolateWater(
        double progress,
        IReadOnlyList<(double t, ParsedWaterFrame wf)> track)
    {
        if (track.Count == 0) return new Vector4(0.09f, 0.28f, 0.63f, 0.84f);
        if (track.Count == 1) return track[0].wf.FogColor;

        int a = track.Count - 1;
        for (int i = 0; i < track.Count; i++)
        {
            if (track[i].t > progress) { a = i - 1; break; }
        }
        if (a < 0) a = track.Count - 1;
        int b = (a + 1) % track.Count;

        double ta   = track[a].t, tb = track[b].t;
        double span = tb > ta ? tb - ta : (1.0 - ta) + tb;
        double off  = progress >= ta ? progress - ta : (1.0 - ta) + progress;
        float  frac = (float)(span > 0.0 ? off / span : 0.0);

        return Vector4.Lerp(track[a].wf.FogColor, track[b].wf.FogColor, frac);
    }

    private static SkySettings ToSkySettings(in ParsedSkyFrame f) => new()
    {
        SunDirection  = Vector3.Normalize(RotateByQuat(f.SunRotation, Vector3.UnitZ)),
        BlueHorizon   = f.BlueHorizon,
        BlueDensity   = f.BlueDensity,
        HazeHorizon   = f.HazeHorizon,
        HazeDensity   = f.HazeDensity,
        SunlightColor = f.SunlightColor,
        Ambient       = f.Ambient,
        SunGlowFocus  = f.GlowFocus,
        SunGlowSize   = f.GlowSize,
    };

    private static SkySettings LerpSkyFrames(in ParsedSkyFrame a, in ParsedSkyFrame b, float t)
    {
        var sunRot = Quaternion.Slerp(a.SunRotation, b.SunRotation, t);
        return new SkySettings
        {
            SunDirection  = Vector3.Normalize(RotateByQuat(sunRot, Vector3.UnitZ)),
            BlueHorizon   = Vector3.Lerp(a.BlueHorizon,   b.BlueHorizon,   t),
            BlueDensity   = Vector3.Lerp(a.BlueDensity,   b.BlueDensity,   t),
            HazeHorizon   = a.HazeHorizon + (b.HazeHorizon - a.HazeHorizon) * t,
            HazeDensity   = a.HazeDensity + (b.HazeDensity - a.HazeDensity) * t,
            SunlightColor = Vector3.Lerp(a.SunlightColor, b.SunlightColor, t),
            Ambient       = Vector3.Lerp(a.Ambient,       b.Ambient,       t),
            SunGlowFocus  = a.GlowFocus + (b.GlowFocus - a.GlowFocus) * t,
            SunGlowSize   = a.GlowSize  + (b.GlowSize  - a.GlowSize)  * t,
        };
    }
}
