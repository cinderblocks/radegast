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
using System.ComponentModel;
using System.IO;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using AvaloniaEdit;
using AvaloniaEdit.CodeCompletion;
using AvaloniaEdit.Document;
using Radegast.Veles.Controls;
using Radegast.Veles.ViewModels;

namespace Radegast.Veles.Views;

public partial class ScriptEditorPanel : UserControl
{
    private ScriptEditorViewModel? _vm;
    private TextEditor? _editor;
    private CompletionWindow? _completionWindow;
    private bool _updatingEditor;
    private bool _updatingVm;

    public event EventHandler? DetachRequested;
    public event EventHandler? CloseRequested;

    public ScriptEditorPanel()
    {
        InitializeComponent();
    }

    protected override void OnLoaded(RoutedEventArgs e)
    {
        base.OnLoaded(e);

        _vm = DataContext as ScriptEditorViewModel;
        if (_vm == null) return;

        _editor = this.FindControl<TextEditor>("ScriptEditor");
        if (_editor != null)
        {
            _editor.SyntaxHighlighting = LslHighlighting.GetDefinition();
            _editor.Options.ConvertTabsToSpaces = true;
            _editor.Options.IndentationSize = 4;

            // Programmatically reflect IsLoading as IsReadOnly (more reliable than AXAML binding
            // on AvaloniaEdit's DirectProperty with a computed getter).
            _editor.IsReadOnly = _vm.IsLoading;

            // Set initial text from VM
            _updatingEditor = true;
            _editor.Text = _vm.ScriptText;
            _updatingEditor = false;

            // Sync editor → VM
            _editor.Document.TextChanged += OnEditorTextChanged;

            // Sync cursor position
            _editor.TextArea.Caret.PositionChanged += OnCaretPositionChanged;

            // Code completion
            _editor.TextArea.TextEntering += OnTextEntering;
            _editor.TextArea.TextEntered  += OnTextEntered;

            // Ctrl+Space to manually invoke completion
            _editor.TextArea.KeyDown += OnEditorKeyDown;
        }

        // Sync VM → editor (e.g., when script loads from server)
        _vm.PropertyChanged += OnVmPropertyChanged;

        var btnSaveFile = this.FindControl<Button>("BtnSaveFile");
        if (btnSaveFile != null) btnSaveFile.Click += OnSaveFileClick;

        var btnLoadFile = this.FindControl<Button>("BtnLoadFile");
        if (btnLoadFile != null) btnLoadFile.Click += OnLoadFileClick;

        var standalone = VisualRoot is ProfileWindow;

        var btnDetach = this.FindControl<Button>("BtnDetach");
        if (btnDetach != null)
        {
            if (standalone)
                btnDetach.IsVisible = false;
            else
                btnDetach.Click += (_, _) => DetachRequested?.Invoke(this, EventArgs.Empty);
        }

        var btnClose = this.FindControl<Button>("BtnClose");
        if (btnClose != null)
        {
            if (standalone)
                btnClose.IsVisible = false;
            else
                btnClose.Click += (_, _) => CloseRequested?.Invoke(this, EventArgs.Empty);
        }
    }

    private void OnEditorTextChanged(object? sender, EventArgs e)
    {
        if (_updatingEditor || _vm == null || _editor == null) return;
        _updatingVm = true;
        _vm.ScriptText = _editor.Text;
        _updatingVm = false;
    }

    private void OnCaretPositionChanged(object? sender, EventArgs e)
    {
        if (_editor == null || _vm == null) return;
        _vm.CursorLine = _editor.TextArea.Caret.Line;
        _vm.CursorColumn = _editor.TextArea.Caret.Column;
    }

    private void OnVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_editor == null || _vm == null) return;

        if (e.PropertyName == nameof(ScriptEditorViewModel.IsLoading))
        {
            _editor.IsReadOnly = _vm.IsLoading;
            return;
        }

        if (e.PropertyName != nameof(ScriptEditorViewModel.ScriptText)) return;
        if (_updatingVm) return;

        _updatingEditor = true;
        _editor.Text = _vm.ScriptText;
        _updatingEditor = false;
    }

    private async void OnSaveFileClick(object? sender, RoutedEventArgs e)
    {
        if (_vm == null) return;
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel == null) return;

        var file = await topLevel.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Save script",
            SuggestedFileName = _vm.ScriptName,
            FileTypeChoices =
            [
                new FilePickerFileType("LSL script") { Patterns = ["*.lsl"] },
                new FilePickerFileType("Text file") { Patterns = ["*.txt"] }
            ]
        });

        if (file != null)
        {
            await using var stream = await file.OpenWriteAsync();
            await using var writer = new StreamWriter(stream);
            await writer.WriteAsync(_vm.ScriptText);
        }
    }

    private async void OnLoadFileClick(object? sender, RoutedEventArgs e)
    {
        if (_vm == null) return;
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel == null) return;

        var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Open script",
            AllowMultiple = false,
            FileTypeFilter =
            [
                new FilePickerFileType("LSL scripts") { Patterns = ["*.lsl"] },
                new FilePickerFileType("Text files") { Patterns = ["*.txt"] },
                new FilePickerFileType("All files") { Patterns = ["*"] }
            ]
        });

        if (files.Count > 0)
        {
            await using var stream = await files[0].OpenReadAsync();
            using var reader = new StreamReader(stream);
            _vm.ScriptText = await reader.ReadToEndAsync();
        }
    }

    protected override void OnUnloaded(RoutedEventArgs e)
    {
        if (_editor != null)
        {
            _editor.Document.TextChanged -= OnEditorTextChanged;
            _editor.TextArea.Caret.PositionChanged -= OnCaretPositionChanged;
            _editor.TextArea.TextEntering -= OnTextEntering;
            _editor.TextArea.TextEntered  -= OnTextEntered;
            _editor.TextArea.KeyDown      -= OnEditorKeyDown;
        }
        _completionWindow?.Close();
        _completionWindow = null;
        if (_vm != null)
        {
            _vm.PropertyChanged -= OnVmPropertyChanged;
        }
        base.OnUnloaded(e);
        (_vm as IDisposable)?.Dispose();
    }

    // ── Code completion ────────────────────────────────────────────────────────

    private void OnTextEntered(object? sender, TextInputEventArgs e)
    {
        if (_editor == null || string.IsNullOrEmpty(e.Text)) return;

        var ch = e.Text[0];

        // Open completion after any identifier character.
        if (char.IsLetter(ch) || ch == '_')
        {
            var prefix = GetCurrentWordPrefix();
            if (prefix.Length == 0) return;

            OpenCompletionWindow(prefix);
        }
    }

    private void OnTextEntering(object? sender, TextInputEventArgs e)
    {
        if (_completionWindow == null || string.IsNullOrEmpty(e.Text)) return;

        // If the user types a non-identifier character, close the window
        // and let it fall through (natural completion of the current item
        // is handled by the CompletionWindow itself for Tab/Enter).
        var ch = e.Text[0];
        if (!char.IsLetterOrDigit(ch) && ch != '_')
        {
            _completionWindow.Close();
        }
    }

    private void OpenCompletionWindow(string prefix)
    {
        if (_editor == null) return;

        // Re-use existing window if it is already open.
        if (_completionWindow != null)
        {
            RefreshCompletionItems(prefix);
            return;
        }

        _completionWindow = new CompletionWindow(_editor.TextArea);
        _completionWindow.Closed += (_, _) => _completionWindow = null;

        RefreshCompletionItems(prefix);

        if (_completionWindow.CompletionList.CompletionData.Count == 0)
        {
            _completionWindow.Close();
            return;
        }

        _completionWindow.Show();
    }

    private void RefreshCompletionItems(string prefix)
    {
        if (_completionWindow == null) return;

        var list = _completionWindow.CompletionList.CompletionData;
        list.Clear();
        foreach (var item in LslCompletionProvider.GetItemsForPrefix(prefix))
            list.Add(item);
    }

    /// <summary>Returns the identifier characters immediately before the caret.</summary>
    private string GetCurrentWordPrefix()
    {
        if (_editor == null) return string.Empty;
        var offset = _editor.TextArea.Caret.Offset;
        var doc = _editor.Document;
        var start = offset;
        while (start > 0)
        {
            var ch = doc.GetCharAt(start - 1);
            if (char.IsLetterOrDigit(ch) || ch == '_') start--;
            else break;
        }
        return doc.GetText(start, offset - start);
    }

    private void OnEditorKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Space && e.KeyModifiers == KeyModifiers.Control)
        {
            e.Handled = true;
            var prefix = GetCurrentWordPrefix();
            OpenCompletionWindow(prefix);
        }
    }
}
