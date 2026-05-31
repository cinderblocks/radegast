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
using OpenMetaverse;
using OpenMetaverse.Marketplace;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Radegast.Veles.ViewModels;

/// <summary>
/// Observable UI record for a single Marketplace listing.
/// Combines backend data from <see cref="MarketplaceListing"/> with local
/// inventory state and per-listing UI state (pending, dirty, validation).
/// </summary>
public partial class MarketplaceListingRecord : ObservableObject
{
    /// <summary>UUID of the listing folder in the agent's local inventory.</summary>
    public UUID ListingFolderUUID { get; }

    /// <summary>Display name of the listing folder from inventory.</summary>
    [ObservableProperty]
    private string _folderName = string.Empty;

    // ── Backend data ─────────────────────────────────────────────────────────

    /// <summary>Backend listing ID, or null if not yet registered with the SLM backend.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsAssociated))]
    [NotifyPropertyChangedFor(nameof(StatusLabel))]
    private int? _listingId;

    /// <summary>UUID of the version folder as reported by the backend, or null.</summary>
    [ObservableProperty]
    private UUID? _versionFolderUUID;

    /// <summary>Backend listing status.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsListed))]
    [NotifyPropertyChangedFor(nameof(StatusLabel))]
    [NotifyPropertyChangedFor(nameof(StatusBadgeText))]
    private MarketplaceListingStatus _status = MarketplaceListingStatus.Unknown;

    /// <summary>Listing title from the backend.</summary>
    [ObservableProperty]
    private string _title = string.Empty;

    /// <summary>Price in Linden Dollars from the backend.</summary>
    [ObservableProperty]
    private int _priceLinden;

    /// <summary>Stock count as reported by the backend.</summary>
    [ObservableProperty]
    private int _stockCount;

    /// <summary>URL to the listing edit page on marketplace.secondlife.com, if available.</summary>
    [ObservableProperty]
    private string? _editUrl;

    /// <summary>UTC timestamp of the last successful sync with the backend.</summary>
    [ObservableProperty]
    private DateTime _lastSyncUtc = DateTime.MinValue;

    // ── Local inventory state ─────────────────────────────────────────────────

    /// <summary>Locally computed stock count (items inside version folder).</summary>
    [ObservableProperty]
    private int _localStockCount;

    /// <summary>Locally computed validation result.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasValidationWarnings))]
    [NotifyPropertyChangedFor(nameof(ValidationSummary))]
    private MarketplaceValidationFlags _validationFlags = MarketplaceValidationFlags.Valid;

    // ── Pending / dirty state ─────────────────────────────────────────────────

    /// <summary>True while an API call (activate, deactivate, create, delete) is in flight.</summary>
    [ObservableProperty]
    private bool _isUpdatePending;

    /// <summary>True while validation is being re-computed after an inventory change.</summary>
    [ObservableProperty]
    private bool _isValidationPending;

    // ── Computed ──────────────────────────────────────────────────────────────

    /// <summary>True when the listing has been registered with the SLM backend.</summary>
    public bool IsAssociated => ListingId.HasValue;

    /// <summary>True when the listing is active and visible to buyers.</summary>
    public bool IsListed => Status == MarketplaceListingStatus.Listed;

    /// <summary>True when the validation check found structural problems.</summary>
    public bool HasValidationWarnings => ValidationFlags != MarketplaceValidationFlags.Valid;

    /// <summary>Short human-readable status string for use in the UI.</summary>
    public string StatusLabel => Status switch
    {
        MarketplaceListingStatus.Listed   => "Active",
        MarketplaceListingStatus.Unlisted => "Inactive",
        _                                 => IsAssociated ? "Unknown" : "Unassociated"
    };

    /// <summary>Short badge text for the status indicator chip.</summary>
    public string StatusBadgeText => Status switch
    {
        MarketplaceListingStatus.Listed   => "●",
        MarketplaceListingStatus.Unlisted => "○",
        _                                 => "?"
    };

    /// <summary>Human-readable description of all validation issues, or empty string if valid.</summary>
    public string ValidationSummary
    {
        get
        {
            if (ValidationFlags == MarketplaceValidationFlags.Valid) return string.Empty;
            var parts = new System.Collections.Generic.List<string>();
            if (ValidationFlags.HasFlag(MarketplaceValidationFlags.MissingVersionFolder))
                parts.Add("missing version folder");
            if (ValidationFlags.HasFlag(MarketplaceValidationFlags.MultipleVersionFolders))
                parts.Add("multiple version folders");
            if (ValidationFlags.HasFlag(MarketplaceValidationFlags.EmptyListing))
                parts.Add("no items in version folder");
            if (ValidationFlags.HasFlag(MarketplaceValidationFlags.NotRegisteredWithServer))
                parts.Add("not registered with SLM");
            if (ValidationFlags.HasFlag(MarketplaceValidationFlags.ServerMetadataMismatch))
                parts.Add("server metadata mismatch");
            if (ValidationFlags.HasFlag(MarketplaceValidationFlags.InvalidStructure))
                parts.Add("invalid structure");
            return string.Join(", ", parts);
        }
    }

    public MarketplaceListingRecord(UUID listingFolderUUID, string folderName)
    {
        ListingFolderUUID = listingFolderUUID;
        _folderName = folderName;
    }

    /// <summary>Applies data from a <see cref="MarketplaceListing"/> backend record.</summary>
    public void ApplyBackendData(MarketplaceListing listing)
    {
        ListingId = listing.ListingId;
        VersionFolderUUID = listing.VersionFolderUUID == UUID.Zero
            ? (UUID?)null : listing.VersionFolderUUID;
        Status = listing.Status;
        Title = listing.Title;
        PriceLinden = listing.PriceLinden;
        StockCount = listing.StockCount;
        EditUrl = listing.EditUrl;
        LastSyncUtc = listing.LastSyncUtc;
    }
}
