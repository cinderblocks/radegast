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
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OpenMetaverse;
using OpenMetaverse.Assets;
using Radegast.Veles.Core;

namespace Radegast.Veles.ViewModels;

public partial class ScriptEditorViewModel : ObservableObject, IDisposable
{
    private readonly RadegastInstanceAvalonia _instance;
    private GridClient Client => _instance.Client;
    private readonly InventoryLSL _item;
    private bool _isSettingText;

    public ItemMetadataViewModel Metadata { get; }

    [ObservableProperty] private string _scriptName = string.Empty;
    [ObservableProperty] private string _scriptText = string.Empty;
    [ObservableProperty] private string _statusText = "Loading...";
    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private bool _isSaving;
    [ObservableProperty] private bool _isModified;
    [ObservableProperty] private int _cursorLine = 1;
    [ObservableProperty] private int _cursorColumn = 1;
    [ObservableProperty] private string _compileOutput = string.Empty;
    [ObservableProperty] private bool _hasCompileOutput;

    public ScriptEditorViewModel(RadegastInstanceAvalonia instance, InventoryLSL item)
    {
        _instance = instance;
        _item = item;
        ScriptName = item.Name;
        Metadata = new ItemMetadataViewModel(instance, item);
        LoadScript();
    }

    private void LoadScript()
    {
        if (_item.AssetUUID == UUID.Zero)
        {
            IsLoading = false;
            ScriptText = string.Empty;
            StatusText = "Ready";
            SaveCommand.NotifyCanExecuteChanged();
            return;
        }
        IsLoading = true;
        StatusText = "Loading...";
        Client.Assets.RequestInventoryAsset(_item, true, UUID.Random(), OnAssetReceived);
    }

    private void OnAssetReceived(AssetDownload transfer, Asset? asset)
    {
        Dispatcher.UIThread.Post(() =>
        {
            IsLoading = false;
            if (!transfer.Success || asset is not AssetScriptText scriptAsset)
            {
                StatusText = "Failed to load";
                return;
            }
            scriptAsset.Decode();
            _isSettingText = true;
            ScriptText = scriptAsset.Source ?? string.Empty;
            _isSettingText = false;
            IsModified = false;
            StatusText = "Ready";
            SaveCommand.NotifyCanExecuteChanged();
        });
    }

    partial void OnScriptTextChanged(string value)
    {
        if (!_isSettingText)
            IsModified = true;
    }

    partial void OnIsLoadingChanged(bool value) => SaveCommand.NotifyCanExecuteChanged();
    partial void OnIsSavingChanged(bool value) => SaveCommand.NotifyCanExecuteChanged();

    [RelayCommand(CanExecute = nameof(CanSave))]
    private async Task Save()
    {
        IsSaving = true;
        StatusText = "Saving...";
        HasCompileOutput = false;
        CompileOutput = string.Empty;

        var scriptAsset = new AssetScriptText { Source = ScriptText };
        scriptAsset.Encode();

        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(1));
            var tcs = new TaskCompletionSource<(bool uploadOk, bool compileOk, List<string>? messages)>(
                TaskCreationOptions.RunContinuationsAsynchronously);

            cts.Token.Register(() => tcs.TrySetCanceled(), useSynchronizationContext: false);

            InventoryManager.ScriptUpdatedCallback cb = (uploadSuccess, _, compileSuccess, msgs, _, _) =>
                tcs.TrySetResult((uploadSuccess, compileSuccess, msgs));

            _ = Task.Run(async () =>
            {
                try
                {
                    await Client.Inventory.RequestUpdateScriptAgentInventoryAsync(
                        scriptAsset.AssetData, _item.UUID, true, cb, cts.Token).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    tcs.TrySetException(ex);
                }
            }, cts.Token);

            var (uploadOk, compileOk, messages) = await tcs.Task.ConfigureAwait(false);

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                IsSaving = false;
                if (uploadOk && compileOk)
                {
                    IsModified = false;
                    StatusText = "Saved";
                }
                else if (!compileOk && messages?.Count > 0)
                {
                    StatusText = "Compile errors";
                    CompileOutput = string.Join(Environment.NewLine, messages);
                    HasCompileOutput = true;
                }
                else
                {
                    StatusText = "Save failed";
                }
                SaveCommand.NotifyCanExecuteChanged();
            });
        }
        catch (OperationCanceledException)
        {
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                IsSaving = false;
                StatusText = "Timed out";
                SaveCommand.NotifyCanExecuteChanged();
            });
        }
        catch (Exception ex)
        {
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                IsSaving = false;
                StatusText = $"Error: {ex.Message}";
                SaveCommand.NotifyCanExecuteChanged();
            });
        }
    }

    private bool CanSave() => !IsLoading && !IsSaving;

    public void Dispose() => Metadata.Dispose();
}
