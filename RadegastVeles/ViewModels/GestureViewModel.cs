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
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LibreMetaverse;
using LibreMetaverse.Assets;
using Radegast.Veles.Core;

namespace Radegast.Veles.ViewModels;

public abstract partial class GestureStepViewModelBase : ObservableObject
{
    public abstract string TypeLabel { get; }
    public abstract string Summary { get; }
    public abstract GestureStep ToGestureStep();
}

public partial class ChatGestureStepViewModel : GestureStepViewModelBase
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Summary))]
    private string _text = string.Empty;

    public override string TypeLabel => "Chat";
    public override string Summary => string.IsNullOrEmpty(Text) ? "(empty)" : Text;
    public override GestureStep ToGestureStep() => new GestureStepChat { Text = Text };
}

public partial class WaitGestureStepViewModel : GestureStepViewModelBase
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Summary))]
    private decimal _waitTime;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Summary))]
    private bool _waitForTime;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Summary))]
    private bool _waitForAnimation;

    public override string TypeLabel => "Wait";
    public override string Summary
    {
        get
        {
            if (WaitForAnimation && WaitForTime) return $"animations + {WaitTime:0.0}s";
            if (WaitForAnimation) return "animations";
            if (WaitForTime) return $"{WaitTime:0.0}s";
            return "(no condition)";
        }
    }

    public override GestureStep ToGestureStep() => new GestureStepWait
    {
        WaitTime = (float)WaitTime,
        WaitForTime = WaitForTime,
        WaitForAnimation = WaitForAnimation
    };
}

public partial class AnimationGestureStepViewModel : GestureStepViewModelBase
{
    private readonly RadegastInstanceAvalonia? _instance;

    public AnimationGestureStepViewModel(RadegastInstanceAvalonia? instance = null)
    {
        _instance = instance;
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Summary))]
    [NotifyPropertyChangedFor(nameof(TypeLabel))]
    private bool _animationStart = true;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Summary))]
    private string _name = string.Empty;

    [ObservableProperty]
    private string _idText = string.Empty;

    public override string TypeLabel => AnimationStart ? "Anim Start" : "Anim Stop";
    public override string Summary => string.IsNullOrEmpty(Name) ? "(unnamed)" : Name;

    public override GestureStep ToGestureStep() => new GestureStepAnimation
    {
        AnimationStart = AnimationStart,
        Name = Name,
        ID = UUID.TryParse(IdText, out var id) ? id : UUID.Zero
    };

    [RelayCommand]
    private void PickAsset()
    {
        if (_instance == null) return;
        _instance.ShowInventoryPicker("Pick Animation", [AssetType.Animation], entry =>
        {
            Name = entry.Name;
            IdText = entry.AssetId.ToString();
        });
    }
}

public partial class SoundGestureStepViewModel : GestureStepViewModelBase
{
    private readonly RadegastInstanceAvalonia? _instance;

    public SoundGestureStepViewModel(RadegastInstanceAvalonia? instance = null)
    {
        _instance = instance;
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Summary))]
    private string _name = string.Empty;

    [ObservableProperty]
    private string _idText = string.Empty;

    public override string TypeLabel => "Sound";
    public override string Summary => string.IsNullOrEmpty(Name) ? "(unnamed)" : Name;

    public override GestureStep ToGestureStep() => new GestureStepSound
    {
        Name = Name,
        ID = UUID.TryParse(IdText, out var id) ? id : UUID.Zero
    };

    [RelayCommand]
    private void PickAsset()
    {
        if (_instance == null) return;
        _instance.ShowInventoryPicker("Pick Sound", [AssetType.Sound], entry =>
        {
            Name = entry.Name;
            IdText = entry.AssetId.ToString();
        });
    }
}

public partial class GestureViewModel : ObservableObject, IDisposable
{
    private readonly RadegastInstanceAvalonia _instance;
    private GridClient Client => _instance.Client;
    private readonly InventoryGesture _item;
    private bool _disposed;
    private bool _isPopulating;

    public ItemMetadataViewModel Metadata { get; }

    [ObservableProperty] private string _gestureName = string.Empty;
    [ObservableProperty] private string _statusText = "Loading...";
    [ObservableProperty] private bool _isLoading = true;
    [ObservableProperty] private bool _isSaving;
    [ObservableProperty] private bool _isModified;
    [ObservableProperty] private bool _isActive;
    [ObservableProperty] private string _trigger = string.Empty;
    [ObservableProperty] private string _replaceWith = string.Empty;
    [ObservableProperty] private decimal _triggerKey;
    [ObservableProperty] private decimal _triggerKeyMask;
    [ObservableProperty] private GestureStepViewModelBase? _selectedStep;

    public ObservableCollection<GestureStepViewModelBase> EditSteps { get; } = [];

    public GestureViewModel(RadegastInstanceAvalonia instance, InventoryGesture item)
    {
        _instance = instance;
        _item = item;
        GestureName = item.Name;
        Metadata = new ItemMetadataViewModel(instance, item);
        _isActive = Metadata.IsWorn;
        EditSteps.CollectionChanged += OnEditStepsCollectionChanged;

        if (item.AssetUUID == UUID.Zero)
        {
            IsLoading = false;
            StatusText = "New gesture - add steps below";
            return;
        }

        _ = Task.Run(async () =>
        {
            var asset = await Client.Assets.RequestAssetAsync(item.AssetUUID, AssetType.Gesture, true);
            Dispatcher.UIThread.Post(() =>
            {
                IsLoading = false;
                if (asset is not AssetGesture gestureAsset)
                {
                    StatusText = "Failed to download gesture.";
                    return;
                }
                if (!gestureAsset.Decode())
                {
                    StatusText = "Could not decode gesture sequence.";
                    return;
                }
                PopulateFromAsset(gestureAsset);
            });
        });
    }

    private void PopulateFromAsset(AssetGesture asset)
    {
        _isPopulating = true;
        Trigger = asset.Trigger;
        ReplaceWith = asset.ReplaceWith;
        TriggerKey = asset.TriggerKey;
        TriggerKeyMask = asset.TriggerKeyMask;
        EditSteps.Clear();
        foreach (var step in asset.Sequence)
        {
            GestureStepViewModelBase? stepVm = step switch
            {
                GestureStepChat chat => new ChatGestureStepViewModel { Text = chat.Text },
                GestureStepWait wait => new WaitGestureStepViewModel
                {
                    WaitTime = (decimal)wait.WaitTime,
                    WaitForTime = wait.WaitForTime,
                    WaitForAnimation = wait.WaitForAnimation
                },
                GestureStepAnimation anim => new AnimationGestureStepViewModel(_instance)
                {
                    AnimationStart = anim.AnimationStart,
                    Name = anim.Name,
                    IdText = anim.ID.ToString()
                },
                GestureStepSound snd => new SoundGestureStepViewModel(_instance)
                {
                    Name = snd.Name,
                    IdText = snd.ID.ToString()
                },
                _ => null
            };
            if (stepVm != null) EditSteps.Add(stepVm);
        }
        IsModified = false;
        var count = EditSteps.Count;
        StatusText = count == 1 ? "1 step" : $"{count} steps";
        _isPopulating = false;
    }

    private void OnEditStepsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems != null)
            foreach (GestureStepViewModelBase step in e.OldItems)
                step.PropertyChanged -= OnStepPropertyChanged;
        if (e.NewItems != null)
            foreach (GestureStepViewModelBase step in e.NewItems)
                step.PropertyChanged += OnStepPropertyChanged;
        if (!_isPopulating) SetModified();
        UpdateStepCommands();
    }

    private void OnStepPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (!_isPopulating) SetModified();
    }

    partial void OnTriggerChanged(string value) { if (!_isPopulating) SetModified(); }
    partial void OnReplaceWithChanged(string value) { if (!_isPopulating) SetModified(); }
    partial void OnTriggerKeyChanged(decimal value) { if (!_isPopulating) SetModified(); }
    partial void OnTriggerKeyMaskChanged(decimal value) { if (!_isPopulating) SetModified(); }

    partial void OnSelectedStepChanged(GestureStepViewModelBase? value) => UpdateStepCommands();

    partial void OnIsLoadingChanged(bool value)
    {
        SaveGestureCommand.NotifyCanExecuteChanged();
    }

    partial void OnIsSavingChanged(bool value)
    {
        SaveGestureCommand.NotifyCanExecuteChanged();
    }

    private void SetModified()
    {
        IsModified = true;
        SaveGestureCommand.NotifyCanExecuteChanged();
    }

    private void UpdateStepCommands()
    {
        MoveStepUpCommand.NotifyCanExecuteChanged();
        MoveStepDownCommand.NotifyCanExecuteChanged();
        RemoveStepCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand]
    private void Play()
    {
        _ = Client.Self.PlayGestureAsync(_item.AssetUUID);
        StatusText = "Playing...";
    }

    [RelayCommand]
    private void ToggleActive()
    {
        if (IsActive)
        {
            Client.Self.DeactivateGesture(_item.UUID);
            IsActive = false;
            StatusText = "Deactivated.";
        }
        else
        {
            Client.Self.ActivateGesture(_item.UUID, _item.AssetUUID);
            IsActive = true;
            StatusText = "Activated.";
        }
    }

    [RelayCommand]
    private void AddChatStep()
    {
        var step = new ChatGestureStepViewModel();
        EditSteps.Add(step);
        SelectedStep = step;
    }

    [RelayCommand]
    private void AddWaitStep()
    {
        var step = new WaitGestureStepViewModel();
        EditSteps.Add(step);
        SelectedStep = step;
    }

    [RelayCommand]
    private void AddAnimationStep()
    {
        var step = new AnimationGestureStepViewModel(_instance);
        EditSteps.Add(step);
        SelectedStep = step;
    }

    [RelayCommand]
    private void AddSoundStep()
    {
        var step = new SoundGestureStepViewModel(_instance);
        EditSteps.Add(step);
        SelectedStep = step;
    }

    [RelayCommand(CanExecute = nameof(CanMoveStepUp))]
    private void MoveStepUp()
    {
        if (SelectedStep == null) return;
        var idx = EditSteps.IndexOf(SelectedStep);
        if (idx <= 0) return;
        EditSteps.Move(idx, idx - 1);
    }
    private bool CanMoveStepUp() => SelectedStep != null && EditSteps.IndexOf(SelectedStep) > 0;

    [RelayCommand(CanExecute = nameof(CanMoveStepDown))]
    private void MoveStepDown()
    {
        if (SelectedStep == null) return;
        var idx = EditSteps.IndexOf(SelectedStep);
        if (idx < 0 || idx >= EditSteps.Count - 1) return;
        EditSteps.Move(idx, idx + 1);
    }
    private bool CanMoveStepDown() => SelectedStep != null && EditSteps.IndexOf(SelectedStep) < EditSteps.Count - 1;

    [RelayCommand(CanExecute = nameof(CanRemoveStep))]
    private void RemoveStep()
    {
        if (SelectedStep == null) return;
        var idx = EditSteps.IndexOf(SelectedStep);
        EditSteps.Remove(SelectedStep);
        SelectedStep = EditSteps.Count > 0 ? EditSteps[Math.Max(0, idx - 1)] : null;
    }
    private bool CanRemoveStep() => SelectedStep != null;

    [RelayCommand(CanExecute = nameof(CanSave))]
    private async Task SaveGesture()
    {
        IsSaving = true;
        StatusText = "Saving...";

        var asset = new AssetGesture
        {
            Trigger = Trigger,
            ReplaceWith = ReplaceWith,
            TriggerKey = (byte)Math.Clamp((int)TriggerKey, 0, 255),
            TriggerKeyMask = (uint)Math.Clamp((int)TriggerKeyMask, 0, int.MaxValue)
        };
        foreach (var stepVm in EditSteps)
            asset.Sequence.Add(stepVm.ToGestureStep());
        asset.Sequence.Add(new GestureStepEOF());
        asset.Encode();

        var (success, status, _, _) = await Client.Inventory.RequestUploadGestureAssetAsync(asset.AssetData, _item.UUID);
        Dispatcher.UIThread.Post(() =>
        {
            IsSaving = false;
            if (success)
            {
                IsModified = false;
                var count = EditSteps.Count;
                StatusText = count == 1 ? "Saved - 1 step" : $"Saved - {count} steps";
            }
            else
            {
                StatusText = $"Save failed: {status}";
            }
            SaveGestureCommand.NotifyCanExecuteChanged();
        });
    }
    private bool CanSave() => !IsLoading && !IsSaving;

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        EditSteps.CollectionChanged -= OnEditStepsCollectionChanged;
        foreach (var step in EditSteps)
            step.PropertyChanged -= OnStepPropertyChanged;
        Metadata.Dispose();
    }
}
