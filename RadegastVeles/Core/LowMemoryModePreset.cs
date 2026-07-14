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

namespace Radegast.Veles.Core;

/// <summary>
/// Conservative texture/asset cache sizes applied while
/// <see cref="RadegastInstanceAvalonia.LowMemoryModeEnabled"/> is on, in place of the user's
/// own <c>PreferencesViewModel</c> slider values. Flat constants for now; revisit with
/// RAM-scaled recommenders (like <see cref="GridTextureHelper.RecommendSkBitmapCacheCap"/>)
/// only if real bot-fleet usage shows these are wrong.
/// </summary>
public static class LowMemoryModePreset
{
    public const int SkBitmapCacheCap = GridTextureHelper.MinRecommendedCacheCap;
    public const int AssetCacheMaxSizeMb = 128;
    public const int TextureBitmapCacheCapacity = 400;
    public const int TextureDiskCacheMaxFiles = 1024;
    // Low-memory mode disables the 3D scene viewer, but attachments for the avatar
    // viewer still decode meshes — keep a small budget rather than zero.
    public const long MeshDecodeCacheMaxVertices = 200_000;
}
