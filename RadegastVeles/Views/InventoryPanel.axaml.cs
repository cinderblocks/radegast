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
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using LibreMetaverse.Marketplace;
using Radegast.Veles.Controls;
using Radegast.Veles.Core;
using Radegast.Veles.ViewModels;

namespace Radegast.Veles.Views;

public partial class InventoryPanel : UserControl
{
    private InventoryViewModel? _vm;
    private Control? _activeEditorPanel;
    private PanelHostWindow? _activeHostWindow;
    private readonly InventoryFilterViewModel _filterVm = new();
    private InventoryFilterWindow? _filterWindow;
    private Window? _outfitWindow;

    // Drag-source tracking
    private Point? _dragStartPos;
    private PointerPressedEventArgs? _dragPressedArgs;
    private bool _dragging;

    public InventoryPanel()
    {
        InitializeComponent();
    }

    protected override void OnLoaded(RoutedEventArgs e)
    {
        base.OnLoaded(e);

        _vm = DataContext as InventoryViewModel;
        if (_vm == null) return;

        // Give ViewModel access to the system clipboard
        _vm.TopLevel = TopLevel.GetTopLevel(this)?.Clipboard;

        var searchBox = this.FindControl<TextBox>("TxtInvSearch");
        if (searchBox != null)
            searchBox.KeyDown += SearchBox_KeyDown;

        var treeView = this.FindControl<TreeView>("InvTreeView");
        if (treeView != null)
        {
            treeView.SelectionChanged   += InvTreeView_SelectionChanged;
            treeView.DoubleTapped       += InvTreeView_DoubleTapped;
            treeView.KeyDown            += InvTreeView_KeyDown;
            treeView.ContextRequested   += InvTreeView_ContextRequested;
            treeView.PointerPressed     += InvTree_PointerPressed;
            treeView.PointerMoved       += InvTree_PointerMoved;
            treeView.PointerReleased    += InvTree_PointerReleased;
            DragDrop.SetAllowDrop(treeView, true);
            treeView.AddHandler(DragDrop.DragOverEvent, InvTree_DragOver);
            treeView.AddHandler(DragDrop.DropEvent,     InvTree_Drop);
        }

        var searchList = this.FindControl<ListBox>("SearchResultsList");
        if (searchList != null)
        {
            searchList.DoubleTapped += SearchResultsList_DoubleTapped;
            searchList.ContextRequested += SearchResultsList_ContextRequested;
            searchList.PointerPressed   += InvTree_PointerPressed;
            searchList.PointerMoved     += InvTree_PointerMoved;
            searchList.PointerReleased  += InvTree_PointerReleased;
        }

        _vm.EditorRequested     += OnEditorRequested;
        _vm.PropertiesRequested += OnPropertiesRequested;
        _vm.SaveCurrentOutfitRequested += OnSaveCurrentOutfitRequested;

        var btnFilter = this.FindControl<Button>("BtnFilter");
        if (btnFilter != null) btnFilter.Click += BtnFilter_Click;

        var btnClearFilter = this.FindControl<Button>("BtnClearFilter");
        if (btnClearFilter != null) btnClearFilter.Click += (_, _) => _filterVm.ClearCommand.Execute(null);

        var btnNewWindow = this.FindControl<Button>("BtnNewWindow");
        if (btnNewWindow != null) btnNewWindow.Click += BtnNewWindow_Click;

        var btnSort = this.FindControl<Button>("BtnSort");
        if (btnSort != null) btnSort.Click += BtnSort_Click;

        var btnMarketplace = this.FindControl<Button>("BtnMarketplace");
        if (btnMarketplace != null)
            btnMarketplace.Click += (_, _) =>
                (TopLevel.GetTopLevel(this) as MainWindow)?.OpenOrActivateMarketplace();

        var btnOutfit = this.FindControl<Button>("BtnOutfit");
        if (btnOutfit != null) btnOutfit.Click += BtnOutfit_Click;

        _filterVm.FilterApplied += async (_, _) => { if (_vm != null) await _vm.ApplyFilterAsync(_filterVm); };
        _filterVm.FilterCleared += (_, _) => _vm?.ClearFilter();
    }

    protected override void OnUnloaded(RoutedEventArgs e)
    {
        base.OnUnloaded(e);
        if (_vm != null)
        {
            _vm.EditorRequested     -= OnEditorRequested;
            _vm.PropertiesRequested -= OnPropertiesRequested;
            _vm.SaveCurrentOutfitRequested -= OnSaveCurrentOutfitRequested;
        }

        _outfitWindow?.Close();
        _outfitWindow = null;

        _filterWindow?.Close();
        _filterWindow = null;

        var treeView = this.FindControl<TreeView>("InvTreeView");
        if (treeView != null)
        {
            treeView.SelectionChanged   -= InvTreeView_SelectionChanged;
            treeView.DoubleTapped       -= InvTreeView_DoubleTapped;
            treeView.KeyDown            -= InvTreeView_KeyDown;
            treeView.ContextRequested   -= InvTreeView_ContextRequested;
            treeView.PointerPressed     -= InvTree_PointerPressed;
            treeView.PointerMoved       -= InvTree_PointerMoved;
            treeView.PointerReleased    -= InvTree_PointerReleased;
            treeView.RemoveHandler(DragDrop.DragOverEvent, InvTree_DragOver);
            treeView.RemoveHandler(DragDrop.DropEvent,     InvTree_Drop);
        }

        var searchList = this.FindControl<ListBox>("SearchResultsList");
        if (searchList != null)
        {
            searchList.DoubleTapped -= SearchResultsList_DoubleTapped;
            searchList.ContextRequested -= SearchResultsList_ContextRequested;
            searchList.PointerPressed   -= InvTree_PointerPressed;
            searchList.PointerMoved     -= InvTree_PointerMoved;
            searchList.PointerReleased  -= InvTree_PointerReleased;
        }
    }

    // ── Tree view event handlers ─────────────────────────────────────────────

    private void InvTreeView_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_vm == null) return;
        if (e.AddedItems.Count > 0 && e.AddedItems[0] is InvTreeNode node)
            _vm.SelectedNode = node;
    }

    private void InvTreeView_DoubleTapped(object? sender, RoutedEventArgs e)
    {
        _vm?.ExecuteDefaultAction();
    }

    private void InvTreeView_KeyDown(object? sender, KeyEventArgs e)
    {
        var node = _vm?.SelectedNode;
        bool isLibrary = node?.IsLibrary == true;

        switch (e.Key)
        {
            case Key.F2 when !isLibrary:
                _ = BeginRenameAsync();
                e.Handled = true;
                break;
            case Key.Delete when node != null && !isLibrary:
                _vm!.DeleteItemCommand.Execute(null);
                e.Handled = true;
                break;
            case Key.C when e.KeyModifiers == KeyModifiers.Control:
                _vm?.CopyItemCommand.Execute(null);
                e.Handled = true;
                break;
            case Key.X when e.KeyModifiers == KeyModifiers.Control && !isLibrary:
                _vm?.CutItemCommand.Execute(null);
                e.Handled = true;
                break;
            case Key.V when e.KeyModifiers == KeyModifiers.Control && !isLibrary:
                _vm?.PasteItemCommand.Execute(null);
                e.Handled = true;
                break;
            case Key.L when e.KeyModifiers == KeyModifiers.Control && !isLibrary:
                _vm?.CreateLinkCommand.Execute(null);
                e.Handled = true;
                break;
            case Key.P when e.KeyModifiers == KeyModifiers.Control:
                _vm?.ShowPropertiesCommand.Execute(null);
                e.Handled = true;
                break;
        }
    }

    private void InvTreeView_ContextRequested(object? sender, ContextRequestedEventArgs e)
    {
        if (_vm == null) return;
        var treeView = sender as TreeView ?? this.FindControl<TreeView>("InvTreeView");
        if (treeView == null) return;

        // Walk up from the source to find the right-clicked InvTreeNode
        var node = FindNodeFromSource(e.Source as Visual, treeView) ?? _vm.SelectedNode;
        if (node == null) return;

        // Ensure selection reflects what was right-clicked
        _vm.SelectedNode = node;
        var marketplace = (TopLevel.GetTopLevel(this) as Window)?.DataContext as MainViewModel;
        treeView.ContextMenu = InventoryMenuBuilder.Build(_vm, node, BeginRenameAsync, marketplace?.Marketplace);
    }

    private static InvTreeNode? FindNodeFromSource(Visual? source, Visual treeViewRoot)
    {
        var current = source;
        while (current != null && !ReferenceEquals(current, treeViewRoot))
        {
            if (current is TreeViewItem tvi && tvi.DataContext is InvTreeNode n)
                return n;
            current = current.GetVisualParent();
        }
        return null;
    }

    // ── Search results event handlers ────────────────────────────────────────

    private void SearchResultsList_DoubleTapped(object? sender, RoutedEventArgs e)
    {
        if (_vm == null) return;
        if (sender is ListBox lb && lb.SelectedItem is InventorySearchResult result)
        {
            if (_vm.TrySelectNode(result.ItemId))
                _vm.ExecuteDefaultAction();
        }
    }

    private void SearchResultsList_ContextRequested(object? sender, ContextRequestedEventArgs e)
    {
        if (_vm == null || sender is not ListBox lb) return;
        if (lb.SelectedItem is not InventorySearchResult result) return;
        if (!_vm.TrySelectNode(result.ItemId) || _vm.SelectedNode == null) return;

        var marketplace = (TopLevel.GetTopLevel(this) as Window)?.DataContext as MainViewModel;
        lb.ContextMenu = InventoryMenuBuilder.Build(_vm, _vm.SelectedNode, BeginRenameAsync, marketplace?.Marketplace);
    }

    // ── Search box ───────────────────────────────────────────────────────────

    private void SearchBox_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && _vm?.SearchInventoryCommand.CanExecute(null) == true)
            _vm.SearchInventoryCommand.Execute(null);
    }

    // ── Rename dialog ────────────────────────────────────────────────────────

    private async Task BeginRenameAsync()
    {
        if (_vm?.SelectedNode is not { } node) return;
        var parentWindow = TopLevel.GetTopLevel(this) as Window;
        if (parentWindow == null) return;

        var dialog = new RenameDialog(node.Name);
        await dialog.ShowDialog(parentWindow);

        if (!string.IsNullOrWhiteSpace(dialog.Result))
            await _vm.CommitRenameAsync(dialog.Result);
    }

    // ── Filter ───────────────────────────────────────────────────────────────

    private void BtnFilter_Click(object? sender, RoutedEventArgs e)
    {
        if (_filterWindow == null)
        {
            _filterWindow = new InventoryFilterWindow { DataContext = _filterVm };
            _filterWindow.Closed += (_, _) => _filterWindow = null;
            _filterWindow.Show();
        }
        else
        {
            _filterWindow.Activate();
        }
    }

    // ── Outfit editor window ─────────────────────────────────────────────────

    private void BtnOutfit_Click(object? sender, RoutedEventArgs e)
    {
        if (_outfitWindow != null)
        {
            _outfitWindow.Activate();
            return;
        }
        if (_vm?.Instance == null) return;

        var outfitVm = new OutfitEditViewModel(_vm.Instance);
        var panel = new OutfitEditPanel { DataContext = outfitVm };
        _outfitWindow = new Window
        {
            Title = "Current Outfit",
            Content = panel,
            Width = 320,
            Height = 500,
            WindowStartupLocation = WindowStartupLocation.CenterOwner
        };
        _outfitWindow.Closed += (_, _) =>
        {
            outfitVm.Dispose();
            _outfitWindow = null;
        };
        var owner = TopLevel.GetTopLevel(this) as Window;
        if (owner != null)
            _outfitWindow.Show(owner);
        else
            _outfitWindow.Show();
    }

    private void OnSaveCurrentOutfitRequested(object? sender, EventArgs e)
    {
        _ = BeginSaveOutfitAsync();
    }

    private async Task BeginSaveOutfitAsync()
    {
        var parentWindow = TopLevel.GetTopLevel(this) as Window;
        if (parentWindow == null || _vm == null) return;
        var dialog = new RenameDialog("My Outfit") { Title = "Save Outfit As" };
        await dialog.ShowDialog(parentWindow);
        if (!string.IsNullOrWhiteSpace(dialog.Result))
            await _vm.SaveCurrentOutfitAsync(dialog.Result);
    }

    // ── Editor management ────────────────────────────────────────────────────

    private void OnEditorRequested(object? sender, InventoryEditorRequestedEventArgs e)
    {
        CloseActiveEditor();

        Control editorPanel;
        if (e.EditorViewModel is ScriptEditorViewModel)
        {
            var panel = new ScriptEditorPanel { DataContext = e.EditorViewModel };
            panel.DetachRequested += (_, _) => OnEditorDetachRequested(panel);
            panel.CloseRequested  += (_, _) => OnEditorCloseRequested();
            editorPanel = panel;
        }
        else if (e.EditorViewModel is NotecardViewModel)
        {
            var panel = new NotecardPanel { DataContext = e.EditorViewModel };
            panel.DetachRequested += (_, _) => OnEditorDetachRequested(panel);
            panel.CloseRequested  += (_, _) => OnEditorCloseRequested();
            editorPanel = panel;
        }
        else if (e.EditorViewModel is TextureViewerViewModel)
        {
            var panel = new TextureViewerPanel { DataContext = e.EditorViewModel };
            panel.DetachRequested += (_, _) => OnEditorDetachRequested(panel);
            panel.CloseRequested  += (_, _) => OnEditorCloseRequested();
            editorPanel = panel;
        }
        else if (e.EditorViewModel is LandmarkViewModel)
        {
            var panel = new LandmarkPanel { DataContext = e.EditorViewModel };
            panel.DetachRequested += (_, _) => OnEditorDetachRequested(panel);
            panel.CloseRequested  += (_, _) => OnEditorCloseRequested();
            editorPanel = panel;
        }
        else if (e.EditorViewModel is CallingCardViewModel)
        {
            var panel = new CallingCardPanel { DataContext = e.EditorViewModel };
            panel.DetachRequested += (_, _) => OnEditorDetachRequested(panel);
            panel.CloseRequested  += (_, _) => OnEditorCloseRequested();
            editorPanel = panel;
        }
        else if (e.EditorViewModel is SoundViewModel)
        {
            var panel = new SoundPanel { DataContext = e.EditorViewModel };
            panel.DetachRequested += (_, _) => OnEditorDetachRequested(panel);
            panel.CloseRequested  += (_, _) => OnEditorCloseRequested();
            editorPanel = panel;
        }
        else if (e.EditorViewModel is GestureViewModel)
        {
            var panel = new GesturePanel { DataContext = e.EditorViewModel };
            panel.DetachRequested += (_, _) => OnEditorDetachRequested(panel);
            panel.CloseRequested  += (_, _) => OnEditorCloseRequested();
            editorPanel = panel;
        }
        else if (e.EditorViewModel is AnimationViewModel)
        {
            var panel = new AnimationPanel { DataContext = e.EditorViewModel };
            panel.DetachRequested += (_, _) => OnEditorDetachRequested(panel);
            panel.CloseRequested  += (_, _) => OnEditorCloseRequested();
            editorPanel = panel;
        }
        else if (e.EditorViewModel is WearableViewModel)
        {
            var panel = new WearablePanel { DataContext = e.EditorViewModel };
            panel.DetachRequested += (_, _) => OnEditorDetachRequested(panel);
            panel.CloseRequested  += (_, _) => OnEditorCloseRequested();
            editorPanel = panel;
        }
        else if (e.EditorViewModel is ObjectViewModel)
        {
            var panel = new ObjectPanel { DataContext = e.EditorViewModel };
            panel.DetachRequested += (_, _) => OnEditorDetachRequested(panel);
            panel.CloseRequested  += (_, _) => OnEditorCloseRequested();
            editorPanel = panel;
        }
        else if (e.EditorViewModel is FolderViewModel)
        {
            editorPanel = new FolderPanel { DataContext = e.EditorViewModel };
        }
        else if (e.EditorViewModel is MaterialViewModel)
        {
            editorPanel = new MaterialPanel { DataContext = e.EditorViewModel };
        }
        else if (e.EditorViewModel is SettingsViewModel)
        {
            editorPanel = new SettingsPanel { DataContext = e.EditorViewModel };
        }
        else
        {
            return;
        }

        _activeEditorPanel = editorPanel;
        var editorArea = this.FindControl<ContentControl>("EditorArea");
        editorArea?.Content = editorPanel;
        _vm?.HasActiveEditor = true;
    }

    private void OnEditorDetachRequested(Control editorPanel)
    {
        if (_activeHostWindow != null) return;

        var editorArea = this.FindControl<ContentControl>("EditorArea");
        editorArea?.Content = null;
        _vm?.HasActiveEditor = false;

        var title = editorPanel switch
        {
            ScriptEditorPanel  s   => (s.DataContext   as ScriptEditorViewModel)?.ScriptName     ?? "Script Editor",
            NotecardPanel      n   => (n.DataContext   as NotecardViewModel)?.NotecardName       ?? "Notecard",
            TextureViewerPanel t   => (t.DataContext   as TextureViewerViewModel)?.TextureName   ?? "Texture",
            LandmarkPanel      l   => (l.DataContext   as LandmarkViewModel)?.LandmarkName       ?? "Landmark",
            CallingCardPanel   c   => (c.DataContext   as CallingCardViewModel)?.CardName        ?? "Calling Card",
            SoundPanel         snd => (snd.DataContext as SoundViewModel)?.SoundName             ?? "Sound",
            GesturePanel       g   => (g.DataContext   as GestureViewModel)?.GestureName         ?? "Gesture",
            AnimationPanel     a   => (a.DataContext   as AnimationViewModel)?.AnimationName     ?? "Animation",
            WearablePanel      w   => (w.DataContext   as WearableViewModel)?.WearableName       ?? "Wearable",
            ObjectPanel        o   => (o.DataContext   as ObjectViewModel)?.ObjectName           ?? "Object",
            MaterialPanel      m   => (m.DataContext   as MaterialViewModel)?.MaterialName       ?? "Material",
            SettingsPanel      s   => (s.DataContext   as SettingsViewModel)?.SettingsName       ?? "Settings",
            _                      => "Viewer"
        };

        _activeHostWindow = new PanelHostWindow { Title = title };
        _activeHostWindow.SetPanel(editorPanel);
        _activeHostWindow.DockRequested += OnHostWindowDockRequested;
        _activeHostWindow.Show();
    }

    private void OnHostWindowDockRequested(object? sender, EventArgs e)
    {
        if (_activeHostWindow == null) return;

        _activeHostWindow.DockRequested -= OnHostWindowDockRequested;
        var panel = _activeHostWindow.RemovePanel();
        _activeHostWindow = null;

        if (panel == null) return;

        _activeEditorPanel = panel;
        _vm?.HasActiveEditor = true;

        var editorArea = this.FindControl<ContentControl>("EditorArea");
        editorArea?.Content = panel;
    }

    private void OnEditorCloseRequested() => CloseActiveEditor();

    private void CloseActiveEditor()
    {
        if (_activeHostWindow != null)
        {
            _activeHostWindow.DockRequested -= OnHostWindowDockRequested;
            var hw = _activeHostWindow;
            _activeHostWindow = null;
            hw.Close();
        }

        var editorArea = this.FindControl<ContentControl>("EditorArea");
        editorArea?.Content = null;

        _activeEditorPanel = null;
        _vm?.HasActiveEditor = false;
    }

    // ── Drag source ──────────────────────────────────────────────────────────

    private void InvTree_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        var pt = e.GetCurrentPoint(sender as Control);
        if (pt.Properties.IsLeftButtonPressed)
        {
            _dragStartPos = e.GetPosition(sender as Control);
            _dragPressedArgs = e;
        }
        else
        {
            _dragStartPos = null;
            _dragPressedArgs = null;
        }
        _dragging = false;
    }

    private void InvTree_PointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        _dragStartPos = null;
        _dragPressedArgs = null;
        _dragging = false;
    }

    private async void InvTree_PointerMoved(object? sender, PointerEventArgs e)
    {
        if (_dragging || _dragStartPos == null || _vm == null) return;
        if (!e.GetCurrentPoint(sender as Control).Properties.IsLeftButtonPressed)
        {
            _dragStartPos = null;
            return;
        }

        var pos = e.GetPosition(sender as Control);
        var delta = pos - _dragStartPos.Value;
        if (Math.Abs(delta.X) < 5 && Math.Abs(delta.Y) < 5) return;

        // Resolve the node being dragged
        InvTreeNode? node = null;
        if (sender is TreeView)
            node = _vm.SelectedNode;
        else if (sender is ListBox lb && lb.SelectedItem is InventorySearchResult sr)
            node = _vm.TryGetNode(sr.ItemId);

        if (node == null || node.IsLibrary)
        {
            _dragStartPos = null;
            return;
        }

        _dragging = true;
        _dragStartPos = null;

        var item = new DataTransferItem();
        item.Set(InventoryDragData.Format, InventoryDragData.SetNode(node));
        var data = new DataTransfer();
        data.Add(item);
        var effect = node.IsFolder ? DragDropEffects.Move : DragDropEffects.Copy | DragDropEffects.Move;
        await DragDrop.DoDragDropAsync(_dragPressedArgs!, data, effect);
        _dragging = false;
    }

    // ── In-tree drop (move between folders) ─────────────────────────────────

    private InvTreeNode? _dropHighlightNode;

    private void InvTree_DragOver(object? sender, DragEventArgs e)
    {
        if (_vm == null || !e.DataTransfer.Contains(InventoryDragData.Format))
        {
            e.DragEffects = DragDropEffects.None;
            return;
        }

        var target = FindFolderNodeUnderPointer(e.Source as Visual, sender as Visual);
        if (target == null)
        {
            e.DragEffects = DragDropEffects.None;
        }
        else
        {
            var token = e.DataTransfer.TryGetValue(InventoryDragData.Format);
            var draggedNode = token != null ? InventoryDragData.PeekNode(token) : null;
            var store = _vm.Instance.Client.Inventory.Store;
            bool blocked = draggedNode != null && (
                !IsMarketplaceMoveAllowed(draggedNode, target, store) ||
                (draggedNode.IsFolder && InventoryViewModel.IsAncestorOrSelf(draggedNode, target)) ||
                target.IsLibrary);
            if (blocked)
            {
                e.DragEffects = DragDropEffects.None;
            }
            else
            {
                e.DragEffects = DragDropEffects.Move;
                // Visual highlight via pseudo-class
                if (!ReferenceEquals(target, _dropHighlightNode))
                {
                    ClearDropHighlight();
                    _dropHighlightNode = target;
                    HighlightFolderNode(target, true);
                }
            }
        }
        e.Handled = true;
    }

    private void InvTree_Drop(object? sender, DragEventArgs e)
    {
        ClearDropHighlight();
        if (_vm == null) return;

        var token = e.DataTransfer.TryGetValue(InventoryDragData.Format);
        if (token == null) return;
        var draggedNode = InventoryDragData.GetNode(token);
        if (draggedNode == null) return;

        var targetFolder = FindFolderNodeUnderPointer(e.Source as Visual, sender as Visual);
        if (targetFolder == null || ReferenceEquals(targetFolder, draggedNode)) return;
        // Library is read-only — no drops into it
        if (targetFolder.IsLibrary) return;
        // Marketplace folder hierarchy rules
        var store = _vm.Instance.Client.Inventory.Store;
        if (!IsMarketplaceMoveAllowed(draggedNode, targetFolder, store)) return;

        _ = _vm.MoveNodeToFolder(draggedNode, targetFolder);
        e.Handled = true;
    }

    private void ClearDropHighlight()
    {
        if (_dropHighlightNode != null)
        {
            HighlightFolderNode(_dropHighlightNode, false);
            _dropHighlightNode = null;
        }
    }

    private static void HighlightFolderNode(InvTreeNode node, bool on)
    {
        // We toggle a property the AXAML template can bind to; use a simple flag approach
        // For now just expand/collapse to give visual feedback (lightweight)
        // Real highlight would require a dedicated IsDropTarget property on InvTreeNode
        _ = on; // suppress unused warning — visual feedback deferred to future binding
    }

    private static bool IsMarketplaceMoveAllowed(InvTreeNode dragged, InvTreeNode target, LibreMetaverse.Inventory? store)
    {
        if (store == null) return true;

        var targetRole = MarketplaceFolderClassifier.GetRole(target.ItemId, store);
        var targetIsMarketplace = targetRole != MarketplaceFolderRole.None;

        // Items (non-folders) may be dragged freely — populating a listing is valid
        if (!dragged.IsFolder) return true;

        var draggedRole = MarketplaceFolderClassifier.GetRole(dragged.ItemId, store);
        var draggedIsMarketplace = draggedRole != MarketplaceFolderRole.None;

        // Block: regular folder → marketplace folder (would corrupt the hierarchy)
        if (!draggedIsMarketplace && targetIsMarketplace) return false;

        // Block: marketplace folder → outside marketplace (would orphan it)
        if (draggedIsMarketplace && !targetIsMarketplace) return false;

        // Within marketplace: only Listing folders may be repositioned (into ListingsRoot)
        if (draggedIsMarketplace && draggedRole != MarketplaceFolderRole.Listing) return false;

        // A Listing folder may only be dropped directly into the ListingsRoot
        if (draggedRole == MarketplaceFolderRole.Listing
            && targetRole != MarketplaceFolderRole.ListingsRoot) return false;

        return true;
    }

    private static InvTreeNode? FindFolderNodeUnderPointer(Visual? source, Visual? root)
    {
        var current = source;
        while (current != null && !ReferenceEquals(current, root))
        {
            if (current is TreeViewItem tvi && tvi.DataContext is InvTreeNode n && n.IsFolder)
                return n;
            current = current.GetVisualParent();
        }
        return null;
    }

    // ── Properties window ────────────────────────────────────────────────────

    private void OnPropertiesRequested(object? sender, ItemPropertiesRequestedEventArgs e)
    {
        if (_vm?.Instance == null) return;
        var vm = new ItemPropertiesViewModel(_vm.Instance, e.Item);
        var panel = new ItemPropertiesPanel { DataContext = vm };
        var window = new Window
        {
            Title   = $"Properties: {e.Item.Name}",
            Content = panel,
            Width   = 380,
            Height  = 560,
            WindowStartupLocation = WindowStartupLocation.CenterOwner
        };
        var owner = TopLevel.GetTopLevel(this) as Window;
        if (owner != null)
            window.Show(owner);
        else
            window.Show();
    }

    // ── New window (pop-out inventory) ───────────────────────────────────────

    private void BtnNewWindow_Click(object? sender, RoutedEventArgs e)
    {
        if (_vm?.Instance == null) return;
        var newVm    = new InventoryViewModel(_vm.Instance);
        var newPanel = new InventoryPanel { DataContext = newVm };
        var window   = new Window
        {
            Title  = "Inventory",
            Content = newPanel,
            Width  = 600,
            Height = 700,
            WindowStartupLocation = WindowStartupLocation.CenterOwner
        };
        var owner = TopLevel.GetTopLevel(this) as Window;
        if (owner != null)
            window.Show(owner);
        else
            window.Show();
    }

    private void BtnSort_Click(object? sender, RoutedEventArgs e)
    {
        if (_vm == null || sender is not Button btn) return;

        var menu = new ContextMenu();

        void AddSortItem(string label, InventorySortMode mode)
        {
            var item = new MenuItem
            {
                Header = (_vm.CurrentSort == mode ? "✓ " : "    ") + label
            };
            item.Click += (_, _) => _vm.SetSortCommand.Execute(mode);
            menu.Items.Add(item);
        }

        AddSortItem("By Name", InventorySortMode.ByName);
        AddSortItem("By Date (Newest First)", InventorySortMode.ByDate);

        menu.Open(btn);
    }
}
