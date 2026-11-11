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
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.LogicalTree;
using Avalonia.Rendering;
using Avalonia.Rendering.Composition;
using Avalonia.VisualTree;
using SkiaSharp;
using Silk.NET.Vulkan;
using Radegast.Veles.Core;
using Buffer = Silk.NET.Vulkan.Buffer;
using Image = Silk.NET.Vulkan.Image;

namespace Radegast.Veles.Rendering;

public partial class VkViewportControl
{
    /// <summary>
    /// Renders every face's flat-color pick ID into the offscreen pick target, reads back the
    /// single pixel at (<paramref name="px"/>,<paramref name="py"/>), decodes it against
    /// <see cref="_pickMap"/>, and raises <see cref="FaceClicked"/> on a hit. Runs as its own
    /// one-off command buffer (create/record/submit/wait), separate from the main frame's
    /// swapchain-present command buffer -- avoids entangling this deliberate CPU stall with the
    /// swapchain's keyed-mutex/present cycle, same reasoning <see cref="VkTexture"/>'s
    /// synchronous upload already uses for its own one-off buffer.
    /// </summary>
    private unsafe void RunPickPass(VkContext vk, PixelSize pixelSize, Matrix4x4 view, Matrix4x4 proj, int px, int py)
    {
        _pickRequested = false;
        if (_pick == null) return;

        // Flip py once, here, before BOTH the readback coordinate below and the ComputeHitInfo
        // call, so the two can't disagree on which screen row was clicked -- mirrors GL's own
        // single already-flipped py, used identically by its ReadPixels and its ComputeHitInfo
        // call. Flipping py inside ComputeHitInfo ALONE (leaving the readback unflipped) desyncs
        // the two, so the ray no longer lands on the face the readback selected.
        py = Math.Clamp(pixelSize.Height - py, 0, pixelSize.Height - 1);

        EnsurePickTarget(vk, pixelSize);

        // Rebuilt every pick to match whatever draw order was actually used this frame --
        // required, not just tidy, since the alpha list's back-to-front sort changes whenever
        // the camera moves.
        _pickMap.Clear();
        _cpuFaceData.Clear();
        foreach (var (_, _, face) in _opaqueFaces)
        {
            _pickMap.Add((face.PrimLocalId, face.FaceIndex));
            _cpuFaceData.Add((face.PickerVertices ?? Array.Empty<float>(), face.NormalUvVertices ?? Array.Empty<float>(), face.Indices, face.Transform));
        }
        foreach (var (_, _, face) in _alphaFaces)
        {
            _pickMap.Add((face.PrimLocalId, face.FaceIndex));
            _cpuFaceData.Add((face.PickerVertices ?? Array.Empty<float>(), face.NormalUvVertices ?? Array.Empty<float>(), face.Indices, face.Transform));
        }
        foreach (var (_, _, face) in _sceneOpaque)
        {
            _pickMap.Add((face.PrimLocalId, face.FaceIndex));
            _cpuFaceData.Add((face.PickerVertices ?? Array.Empty<float>(), face.NormalUvVertices ?? Array.Empty<float>(), face.Indices, face.Transform));
        }
        foreach (var (_, _, face) in _sceneAlpha)
        {
            _pickMap.Add((face.PrimLocalId, face.FaceIndex));
            _cpuFaceData.Add((face.PickerVertices ?? Array.Empty<float>(), face.NormalUvVertices ?? Array.Empty<float>(), face.Indices, face.Transform));
        }

        var cmd = vk.Pool.CreateCommandBuffer("VkViewportControl.RunPickPass");
        cmd.BeginRecording();

        Framebuffer framebuffer = default;
        try
        {
            var attachments = stackalloc ImageView[2] { _pickColorView, _pickDepthView };
            var fbInfo = new FramebufferCreateInfo
            {
                SType = StructureType.FramebufferCreateInfo,
                RenderPass = _pickRenderPass,
                AttachmentCount = 2,
                PAttachments = attachments,
                Width = (uint)pixelSize.Width,
                Height = (uint)pixelSize.Height,
                Layers = 1
            };
            vk.Api.CreateFramebuffer(vk.Device, in fbInfo, null, out framebuffer).ThrowOnError();

            var viewport = new Viewport { X = 0, Y = 0, Width = pixelSize.Width, Height = pixelSize.Height, MinDepth = 0, MaxDepth = 1 };
            vk.Api.CmdSetViewport(cmd.InternalHandle, 0, 1, in viewport);
            var scissor = new Rect2D { Offset = new Offset2D(0, 0), Extent = new Extent2D((uint)pixelSize.Width, (uint)pixelSize.Height) };
            vk.Api.CmdSetScissor(cmd.InternalHandle, 0, 1, &scissor);

            // Clear color alpha=0 acts as the "no hit" sentinel -- matches GL's
            // ClearColor(0,0,0,0) exactly; a real face's pick color always has alpha=1.
            var clearValues = stackalloc ClearValue[2]
            {
                new ClearValue { Color = new ClearColorValue { Float32_0 = 0f, Float32_1 = 0f, Float32_2 = 0f, Float32_3 = 0f } },
                new ClearValue { DepthStencil = new ClearDepthStencilValue { Depth = 1f, Stencil = 0 } }
            };
            var beginInfo = new RenderPassBeginInfo
            {
                SType = StructureType.RenderPassBeginInfo,
                RenderPass = _pickRenderPass,
                Framebuffer = framebuffer,
                RenderArea = new Rect2D(new Offset2D(0, 0), new Extent2D((uint)pixelSize.Width, (uint)pixelSize.Height)),
                ClearValueCount = 2,
                PClearValues = clearValues
            };
            vk.Api.CmdBeginRenderPass(cmd.InternalHandle, in beginInfo, SubpassContents.Inline);

            vk.Api.CmdBindPipeline(cmd.InternalHandle, PipelineBindPoint.Graphics, _pick.Pipeline);
            DrawPickList(vk, cmd.InternalHandle, _opaqueFaces, view, proj, 1);
            DrawPickList(vk, cmd.InternalHandle, _alphaFaces, view, proj, 1 + _opaqueFaces.Count);
            DrawPickList(vk, cmd.InternalHandle, _sceneOpaque, view, proj, 1 + _opaqueFaces.Count + _alphaFaces.Count);
            DrawPickList(vk, cmd.InternalHandle, _sceneAlpha, view, proj, 1 + _opaqueFaces.Count + _alphaFaces.Count + _sceneOpaque.Count);

            vk.Api.CmdEndRenderPass(cmd.InternalHandle);

            // Render loop owns this transition, same established pattern as the main swapchain
            // image (VkRenderPass.cs's layout-ownership decision): FinalLayout=ColorAttachmentOptimal
            // needs an explicit barrier to TransferSrcOptimal before the copy below.
            var barrier = new ImageMemoryBarrier
            {
                SType = StructureType.ImageMemoryBarrier,
                OldLayout = ImageLayout.ColorAttachmentOptimal,
                NewLayout = ImageLayout.TransferSrcOptimal,
                SrcQueueFamilyIndex = Vk.QueueFamilyIgnored,
                DstQueueFamilyIndex = Vk.QueueFamilyIgnored,
                Image = _pickColorImage,
                SubresourceRange = new ImageSubresourceRange(ImageAspectFlags.ColorBit, 0, 1, 0, 1),
                SrcAccessMask = AccessFlags.ColorAttachmentWriteBit,
                DstAccessMask = AccessFlags.TransferReadBit
            };
            vk.Api.CmdPipelineBarrier(cmd.InternalHandle, PipelineStageFlags.ColorAttachmentOutputBit, PipelineStageFlags.TransferBit,
                0, 0, null, 0, null, 1, in barrier);

            // 1x1 region at the click point, not the whole image -- matches GL's single-pixel
            // ReadPixels, keeps the readback buffer tiny regardless of viewport size.
            var copyRegion = new BufferImageCopy
            {
                BufferOffset = 0,
                BufferRowLength = 0,
                BufferImageHeight = 0,
                ImageSubresource = new ImageSubresourceLayers(ImageAspectFlags.ColorBit, 0, 0, 1),
                ImageOffset = new Offset3D(px, py, 0),
                ImageExtent = new Extent3D(1, 1, 1)
            };
            vk.Api.CmdCopyImageToBuffer(cmd.InternalHandle, _pickColorImage, ImageLayout.TransferSrcOptimal,
                _pickReadbackBuffer, 1, in copyRegion);

            // Forces the wait (fence) before this method returns -- synchronous, matching
            // VkTexture's own upload pattern, required since the map+read below needs the copy
            // to have actually completed. SubmitAndWait waits only on this buffer's own fence,
            // not every other buffer outstanding in the shared pool.
            cmd.SubmitAndWait();
        }
        finally
        {
            if (framebuffer.Handle != 0) vk.Api.DestroyFramebuffer(vk.Device, framebuffer, null);
        }

        void* mapped = null;
        vk.Api.MapMemory(vk.Device, _pickReadbackMemory, 0, 4, 0, ref mapped).ThrowOnError();
        var pixel = new byte[4];
        new ReadOnlySpan<byte>(mapped, 4).CopyTo(pixel);
        vk.Api.UnmapMemory(vk.Device, _pickReadbackMemory);

        // R+G+B encode a 1-based index into _pickMap (clear alpha=0 / non-zero RGB = a real
        // hit). Ported verbatim from GL's own decode.
        var purpose = _pickPurpose;
        if (pixel[0] != 0 || pixel[1] != 0 || pixel[2] != 0)
        {
            uint idx = (uint)(pixel[0] | (pixel[1] << 8) | (pixel[2] << 16));
            if (idx >= 1 && (int)idx <= _pickMap.Count)
            {
                var (primLocalId, faceIndex) = _pickMap[(int)(idx - 1)];
                var hitInfo = ComputeHitInfo((int)(idx - 1), px, py, pixelSize.Width, pixelSize.Height, view, proj);
                if (purpose == PickPurpose.CameraFocus)
                {
                    // Real SL: Alt+click is a pure camera action, resolved directly against the
                    // camera here -- it never reaches the VM/object-interaction layer at all.
                    var focusPos = hitInfo.Position;
                    Avalonia.Threading.Dispatcher.UIThread.Post(() => { _camera.Target = focusPos; RequestRender(); });
                }
                else if (purpose == PickPurpose.Select)
                {
                    Avalonia.Threading.Dispatcher.UIThread.Post(() => ObjectRightClicked?.Invoke(primLocalId, faceIndex, hitInfo));
                }
                else
                {
                    Avalonia.Threading.Dispatcher.UIThread.Post(() => FaceClicked?.Invoke(primLocalId, faceIndex, hitInfo));
                }
            }
        }
        // A double-click (Touch) or an Alt+click (CameraFocus, always a ground-pick candidate --
        // see RequestPick's own doc comment) that missed every object falls through to the
        // ground-hit ray march, mirroring GL's own miss-branch for the double-click case.
        else if (_groundPickRequested)
        {
            if (TryGetGroundHit(px, py, pixelSize.Width, pixelSize.Height, view, proj, out var groundPos))
            {
                if (purpose == PickPurpose.CameraFocus)
                    Avalonia.Threading.Dispatcher.UIThread.Post(() => { _camera.Target = groundPos; RequestRender(); });
                else
                    Avalonia.Threading.Dispatcher.UIThread.Post(() => GroundClicked?.Invoke(groundPos));
            }
        }
    }

    /// <summary>
    /// Draws each face with a unique flat pick color encoding its 1-based index (R=bits 0-7,
    /// G=bits 8-15, B=bits 16-23, alpha always 1). No instance batching --
    /// wireframe.vert has no per-instance attributes, matches <see cref="DrawWireframeList"/>'s
    /// own per-face push-constant loop.
    /// </summary>
    private unsafe void DrawPickList(VkContext vk, CommandBuffer cmd,
        List<(VkMesh Mesh, VkMaterialDescriptorSet Material, PrimRenderFace Face)> faces,
        Matrix4x4 view, Matrix4x4 proj, int startIdx)
    {
        Span<float> pushData = stackalloc float[20]; // 16 (mat4 uMvp) + 4 (vec4 uPickColor)
        for (int i = 0; i < faces.Count; i++)
        {
            var face = faces[i].Face;
            var mvp = face.Transform * view * proj;
            CopyMatrixSpan(mvp, pushData[..16]);

            uint idx = (uint)(startIdx + i); // 1-based
            pushData[16] = (idx & 0xFF) / 255f;
            pushData[17] = ((idx >> 8) & 0xFF) / 255f;
            pushData[18] = ((idx >> 16) & 0xFF) / 255f;
            pushData[19] = 1f;

            fixed (float* p = pushData)
                vk.Api.CmdPushConstants(cmd, _pick!.Layout, ShaderStageFlags.VertexBit | ShaderStageFlags.FragmentBit, 0, 80, p);
            faces[i].Mesh.Draw(cmd);
        }
    }

    /// <summary>
    /// CPU ray-vs-heightfield intersection for a double-click that missed every
    /// object in the pick buffer. Marches the same screen-to-world ray used by
    /// <see cref="ComputeHitInfo"/> in 1m steps via <see cref="TerrainHeightProvider"/> until it
    /// crosses the terrain surface, then linearly interpolates between the two straddling
    /// samples (pure
    /// System.Numerics math) with the same view/proj-as-parameters and no-Y-flip adaptations
    /// <see cref="ComputeHitInfo"/>'s own doc comment already documents for this port.
    /// </summary>
    private bool TryGetGroundHit(int px, int py, int w, int h, Matrix4x4 view, Matrix4x4 proj, out Vector3 worldPos)
    {
        worldPos = default;
        var heightAt = TerrainHeightProvider;
        if (heightAt == null) return false;

        float ndcX = (2f * px / w) - 1f;
        float ndcY = (2f * py / h) - 1f;
        Matrix4x4.Invert(view * proj, out var invVP);
        var nearH = Vector4.Transform(new Vector4(ndcX, ndcY, -1f, 1f), invVP);
        var farH = Vector4.Transform(new Vector4(ndcX, ndcY, 1f, 1f), invVP);
        nearH /= nearH.W;
        farH /= farH.W;
        var rayOrigin = new Vector3(nearH.X, nearH.Y, nearH.Z);
        var rayDir = Vector3.Normalize(new Vector3(farH.X, farH.Y, farH.Z) - rayOrigin);

        const float step = 1f;
        const float maxDist = 512f;
        float prevDiff = float.NaN;
        Vector3 prevPos = rayOrigin;
        for (float t = 0f; t <= maxDist; t += step)
        {
            var p = rayOrigin + rayDir * t;
            float? terrainH = heightAt((int)MathF.Floor(p.X), (int)MathF.Floor(p.Y));
            if (terrainH == null)
            {
                if (!float.IsNaN(prevDiff)) break; // left the region after a valid sample
                continue;
            }
            float diff = p.Z - terrainH.Value;
            if (!float.IsNaN(prevDiff) && prevDiff > 0f && diff <= 0f)
            {
                float denom = prevDiff - diff;
                float frac = denom > 1e-6f ? prevDiff / denom : 0f;
                worldPos = Vector3.Lerp(prevPos, p, frac);
                return true;
            }
            prevDiff = diff;
            prevPos = p;
        }
        return false;
    }

    /// <summary>
    /// Pure CPU Möller-Trumbore
    /// ray-triangle intersection + barycentric normal/UV interpolation + Lengyel's-method
    /// binormal, with two adaptations: takes <paramref name="view"/>/<paramref name="proj"/>
    /// as parameters (this class computes them as locals each frame rather than caching them
    /// in fields the way GL does), and takes <paramref name="px"/>/<paramref name="py"/> already
    /// flipped by the caller (<see cref="RunPickPass"/>, see its own comment on why the flip must
    /// happen once, shared with the pixel readback).
    /// </summary>
    private FaceHitInfo ComputeHitInfo(int pickIdx, int px, int py, int w, int h, Matrix4x4 view, Matrix4x4 proj)
    {
        if (pickIdx < 0 || pickIdx >= _cpuFaceData.Count)
            return FaceHitInfo.Unknown;

        var (pickerVerts, normalUvVerts, indices, model) = _cpuFaceData[pickIdx];
        const int PickerStride = 3;
        const int NormalUvStride = 5;

        float ndcX = (2f * px / w) - 1f;
        float ndcY = (2f * py / h) - 1f;

        Matrix4x4.Invert(view * proj, out var invVP);
        var nearH = Vector4.Transform(new Vector4(ndcX, ndcY, -1f, 1f), invVP);
        var farH = Vector4.Transform(new Vector4(ndcX, ndcY, 1f, 1f), invVP);
        nearH /= nearH.W;
        farH /= farH.W;
        var rayOrigin = new Vector3(nearH.X, nearH.Y, nearH.Z);
        var rayDir = Vector3.Normalize(new Vector3(farH.X, farH.Y, farH.Z) - rayOrigin);

        float bestT = float.MaxValue;
        float bestU = 0f, bestV = 0f;
        int bestTri = -1;

        for (int i = 0; i + 2 < indices.Length; i += 3)
        {
            int i0 = indices[i + 0] * PickerStride;
            int i1 = indices[i + 1] * PickerStride;
            int i2 = indices[i + 2] * PickerStride;

            var t0 = Vector4.Transform(new Vector4(pickerVerts[i0], pickerVerts[i0 + 1], pickerVerts[i0 + 2], 1f), model); var p0w = new Vector3(t0.X, t0.Y, t0.Z);
            var t1 = Vector4.Transform(new Vector4(pickerVerts[i1], pickerVerts[i1 + 1], pickerVerts[i1 + 2], 1f), model); var p1w = new Vector3(t1.X, t1.Y, t1.Z);
            var t2 = Vector4.Transform(new Vector4(pickerVerts[i2], pickerVerts[i2 + 1], pickerVerts[i2 + 2], 1f), model); var p2w = new Vector3(t2.X, t2.Y, t2.Z);

            var edge1 = p1w - p0w;
            var edge2 = p2w - p0w;
            var h2 = Vector3.Cross(rayDir, edge2);
            float det = Vector3.Dot(edge1, h2);
            if (MathF.Abs(det) < 1e-7f) continue;
            float invDet = 1f / det;
            var s = rayOrigin - p0w;
            float u = Vector3.Dot(s, h2) * invDet;
            if (u < 0f || u > 1f) continue;
            var q = Vector3.Cross(s, edge1);
            float v = Vector3.Dot(rayDir, q) * invDet;
            if (v < 0f || u + v > 1f) continue;
            float t = Vector3.Dot(edge2, q) * invDet;
            if (t < 0f || t >= bestT) continue;
            bestT = t;
            bestU = u;
            bestV = v;
            bestTri = i;
        }

        if (bestTri < 0)
            return FaceHitInfo.Unknown; // no intersection (shouldn't happen after the GPU pick)

        int p0 = indices[bestTri + 0] * PickerStride;
        int p1 = indices[bestTri + 1] * PickerStride;
        int p2 = indices[bestTri + 2] * PickerStride;
        float w0 = 1f - bestU - bestV, w1 = bestU, w2 = bestV;

        var lpos0 = new Vector3(pickerVerts[p0], pickerVerts[p0 + 1], pickerVerts[p0 + 2]);
        var lpos1 = new Vector3(pickerVerts[p1], pickerVerts[p1 + 1], pickerVerts[p1 + 2]);
        var lpos2 = new Vector3(pickerVerts[p2], pickerVerts[p2 + 1], pickerVerts[p2 + 2]);

        // World-space hit point straight from the ray parameter -- the intersection loop
        // above already ran in world space, matches SL's ObjectGrab Position contract
        // (world/region space, not object-local).
        var worldPos = rayOrigin + rayDir * bestT;

        int n0i = indices[bestTri + 0] * NormalUvStride;
        int n1i = indices[bestTri + 1] * NormalUvStride;
        int n2i = indices[bestTri + 2] * NormalUvStride;

        var n0 = Vector3.Normalize(new Vector3(normalUvVerts[n0i], normalUvVerts[n0i + 1], normalUvVerts[n0i + 2]));
        var n1 = Vector3.Normalize(new Vector3(normalUvVerts[n1i], normalUvVerts[n1i + 1], normalUvVerts[n1i + 2]));
        var n2 = Vector3.Normalize(new Vector3(normalUvVerts[n2i], normalUvVerts[n2i + 1], normalUvVerts[n2i + 2]));
        var localNormal = Vector3.Normalize(n0 * w0 + n1 * w1 + n2 * w2);

        // Inverse-transpose of the model matrix (row-vector convention: n_world = n_local *
        // (M^-1)^T), correct under non-uniform scale. Falls back to the plain matrix for a
        // singular model.
        Vector3 normal;
        if (Matrix4x4.Invert(model, out var invModel))
            normal = Vector3.Normalize(Vector3.TransformNormal(localNormal, Matrix4x4.Transpose(invModel)));
        else
            normal = Vector3.Normalize(Vector3.TransformNormal(localNormal, model));

        float uvX = normalUvVerts[n0i + 3] * w0 + normalUvVerts[n1i + 3] * w1 + normalUvVerts[n2i + 3] * w2;
        float uvY = normalUvVerts[n0i + 4] * w0 + normalUvVerts[n1i + 4] * w1 + normalUvVerts[n2i + 4] * w2;

        // Binormal from world-space triangle edge/UV delta pair (Lengyel's method), so the
        // resulting frame matches the world-space normal above.
        var w0h = Vector4.Transform(new Vector4(lpos0, 1f), model);
        var w1h = Vector4.Transform(new Vector4(lpos1, 1f), model);
        var w2h = Vector4.Transform(new Vector4(lpos2, 1f), model);
        var edge1w = new Vector3(w1h.X, w1h.Y, w1h.Z) - new Vector3(w0h.X, w0h.Y, w0h.Z);
        var edge2w = new Vector3(w2h.X, w2h.Y, w2h.Z) - new Vector3(w0h.X, w0h.Y, w0h.Z);
        float duv1x = normalUvVerts[n1i + 3] - normalUvVerts[n0i + 3];
        float duv1y = normalUvVerts[n1i + 4] - normalUvVerts[n0i + 4];
        float duv2x = normalUvVerts[n2i + 3] - normalUvVerts[n0i + 3];
        float duv2y = normalUvVerts[n2i + 4] - normalUvVerts[n0i + 4];
        float denom = duv1x * duv2y - duv2x * duv1y;
        Vector3 binormal;
        if (MathF.Abs(denom) > 1e-7f)
        {
            float r = 1f / denom;
            var tangent = Vector3.Normalize((edge1w * duv2y - edge2w * duv1y) * r);
            binormal = Vector3.Normalize(Vector3.Cross(normal, tangent));
        }
        else
        {
            binormal = Vector3.Normalize(Vector3.Cross(normal, Vector3.UnitX));
        }

        var uvVec = new Vector3(uvX, uvY, 0f);
        return new FaceHitInfo
        {
            UvCoord = uvVec,
            StCoord = uvVec,
            Position = worldPos,
            Normal = normal,
            Binormal = binormal,
        };
    }

    /// <summary>Pure math,
    /// no GL dependency -- slices the interleaved Position(3)+Normal(3)+TexCoord(2)+Tangent(4)
    /// buffer <see cref="VkMesh"/> already consumes into a position-only buffer for
    /// <see cref="ComputeHitInfo"/>'s ray-triangle intersection.</summary>
    private static float[] PickerFromInterleaved(float[]? interleaved, int length = 0)
    {
        if (interleaved == null || interleaved.Length < 3)
            return Array.Empty<float>();
        int len = length > 0 ? Math.Min(length, interleaved.Length) : interleaved.Length;
        len -= len % 12; // align down to a whole-vertex boundary
        int vCount = len / 12;
        var pos = new float[vCount * 3];
        for (int i = 0, j = 0; i < len; i += 12, j += 3)
        {
            pos[j] = interleaved[i];
            pos[j + 1] = interleaved[i + 1];
            pos[j + 2] = interleaved[i + 2];
        }
        return pos;
    }

    /// <summary>Slices
    /// the same interleaved buffer into a compact normal+UV buffer for
    /// <see cref="ComputeHitInfo"/>'s barycentric interpolation.</summary>
    private static float[] NormalUvFromInterleaved(float[]? interleaved, int length = 0)
    {
        if (interleaved == null || interleaved.Length < 12)
            return Array.Empty<float>();
        int len = length > 0 ? Math.Min(length, interleaved.Length) : interleaved.Length;
        len -= len % 12;
        int vCount = len / 12;
        var nuv = new float[vCount * 5];
        for (int i = 0, j = 0; i < len; i += 12, j += 5)
        {
            nuv[j] = interleaved[i + 3];
            nuv[j + 1] = interleaved[i + 4];
            nuv[j + 2] = interleaved[i + 5];
            nuv[j + 3] = interleaved[i + 6];
            nuv[j + 4] = interleaved[i + 7];
        }
        return nuv;
    }

    /// <summary>
    /// Creates/resizes the pick pass's offscreen render target (color + depth) and its 4-byte
    /// readback buffer. Mirrors <see cref="EnsureDepthTarget"/>'s resize-on-demand pattern
    /// exactly -- same role, a second per-panel offscreen target, just for a different pass.
    /// </summary>
    private unsafe void EnsurePickTarget(VkContext vk, PixelSize size)
    {
        if (_pickSize == size && _pickColorView.Handle != 0) return;
        DestroyPickTarget(vk);

        var colorInfo = new ImageCreateInfo
        {
            SType = StructureType.ImageCreateInfo,
            ImageType = ImageType.Type2D,
            Format = Format.R8G8B8A8Unorm,
            Extent = new Extent3D((uint)size.Width, (uint)size.Height, 1),
            MipLevels = 1,
            ArrayLayers = 1,
            Samples = SampleCountFlags.Count1Bit,
            Tiling = ImageTiling.Optimal,
            // TransferSrcBit on top of the usual ColorAttachmentBit -- this image is the
            // source of the post-render-pass readback copy (see RunPickPass).
            Usage = ImageUsageFlags.ColorAttachmentBit | ImageUsageFlags.TransferSrcBit,
            SharingMode = SharingMode.Exclusive,
            InitialLayout = ImageLayout.Undefined
        };
        vk.Api.CreateImage(vk.Device, in colorInfo, null, out _pickColorImage).ThrowOnError();
        vk.Api.GetImageMemoryRequirements(vk.Device, _pickColorImage, out var colorReq);
        var colorAlloc = new MemoryAllocateInfo
        {
            SType = StructureType.MemoryAllocateInfo,
            AllocationSize = colorReq.Size,
            MemoryTypeIndex = (uint)VkMemoryHelper.FindSuitableMemoryTypeIndex(vk.Api, vk.PhysicalDevice, colorReq.MemoryTypeBits, MemoryPropertyFlags.DeviceLocalBit)
        };
        vk.Api.AllocateMemory(vk.Device, in colorAlloc, null, out _pickColorMemory).ThrowOnError();
        vk.Api.BindImageMemory(vk.Device, _pickColorImage, _pickColorMemory, 0).ThrowOnError();

        var colorViewInfo = new ImageViewCreateInfo
        {
            SType = StructureType.ImageViewCreateInfo,
            Image = _pickColorImage,
            ViewType = ImageViewType.Type2D,
            Format = Format.R8G8B8A8Unorm,
            SubresourceRange = new ImageSubresourceRange(ImageAspectFlags.ColorBit, 0, 1, 0, 1)
        };
        vk.Api.CreateImageView(vk.Device, in colorViewInfo, null, out _pickColorView).ThrowOnError();

        var depthInfo = new ImageCreateInfo
        {
            SType = StructureType.ImageCreateInfo,
            ImageType = ImageType.Type2D,
            Format = Format.D32Sfloat,
            Extent = new Extent3D((uint)size.Width, (uint)size.Height, 1),
            MipLevels = 1,
            ArrayLayers = 1,
            Samples = SampleCountFlags.Count1Bit,
            Tiling = ImageTiling.Optimal,
            Usage = ImageUsageFlags.DepthStencilAttachmentBit,
            SharingMode = SharingMode.Exclusive,
            InitialLayout = ImageLayout.Undefined
        };
        vk.Api.CreateImage(vk.Device, in depthInfo, null, out _pickDepthImage).ThrowOnError();
        vk.Api.GetImageMemoryRequirements(vk.Device, _pickDepthImage, out var depthReq);
        var depthAlloc = new MemoryAllocateInfo
        {
            SType = StructureType.MemoryAllocateInfo,
            AllocationSize = depthReq.Size,
            MemoryTypeIndex = (uint)VkMemoryHelper.FindSuitableMemoryTypeIndex(vk.Api, vk.PhysicalDevice, depthReq.MemoryTypeBits, MemoryPropertyFlags.DeviceLocalBit)
        };
        vk.Api.AllocateMemory(vk.Device, in depthAlloc, null, out _pickDepthMemory).ThrowOnError();
        vk.Api.BindImageMemory(vk.Device, _pickDepthImage, _pickDepthMemory, 0).ThrowOnError();

        var depthViewInfo = new ImageViewCreateInfo
        {
            SType = StructureType.ImageViewCreateInfo,
            Image = _pickDepthImage,
            ViewType = ImageViewType.Type2D,
            Format = Format.D32Sfloat,
            SubresourceRange = new ImageSubresourceRange(ImageAspectFlags.DepthBit, 0, 1, 0, 1)
        };
        vk.Api.CreateImageView(vk.Device, in depthViewInfo, null, out _pickDepthView).ThrowOnError();

        VkBufferHelper.AllocateEmpty(vk, BufferUsageFlags.TransferDstBit, 4, out _pickReadbackBuffer, out _pickReadbackMemory);

        _pickSize = size;
    }

    private unsafe void DestroyPickTarget(VkContext vk)
    {
        if (_pickColorView.Handle != 0) vk.Api.DestroyImageView(vk.Device, _pickColorView, null);
        if (_pickColorImage.Handle != 0) vk.Api.DestroyImage(vk.Device, _pickColorImage, null);
        if (_pickColorMemory.Handle != 0) vk.Api.FreeMemory(vk.Device, _pickColorMemory, null);
        if (_pickDepthView.Handle != 0) vk.Api.DestroyImageView(vk.Device, _pickDepthView, null);
        if (_pickDepthImage.Handle != 0) vk.Api.DestroyImage(vk.Device, _pickDepthImage, null);
        if (_pickDepthMemory.Handle != 0) vk.Api.FreeMemory(vk.Device, _pickDepthMemory, null);
        if (_pickReadbackBuffer.Handle != 0) vk.Api.DestroyBuffer(vk.Device, _pickReadbackBuffer, null);
        if (_pickReadbackMemory.Handle != 0) vk.Api.FreeMemory(vk.Device, _pickReadbackMemory, null);
        _pickColorView = default; _pickColorImage = default; _pickColorMemory = default;
        _pickDepthView = default; _pickDepthImage = default; _pickDepthMemory = default;
        _pickReadbackBuffer = default; _pickReadbackMemory = default;
        _pickSize = default;
    }
}
