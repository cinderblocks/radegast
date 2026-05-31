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
using System.Collections.Specialized;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Radegast.Veles.Controls;
using Radegast.Veles.ViewModels;

namespace Radegast.Veles.Views;

public partial class IMPanel : UserControl
{
    private IMViewModel? _vm;

    // Typing-indicator debounce: fires StopTyping ~5 s after the last keystroke
    private DispatcherTimer? _typingStopTimer;
    private bool _isSendingTyping;

    public IMPanel()
    {
        InitializeComponent();
    }

    protected override void OnLoaded(RoutedEventArgs e)
    {
        base.OnLoaded(e);

        _vm = DataContext as IMViewModel;
        if (_vm == null) return;

        // Enter key sends message; TextChanged fires typing notifications
        var inputBox = this.FindControl<TextBox>("IMInputBox");
        if (inputBox != null)
        {
            inputBox.KeyDown += InputBox_KeyDown;
            inputBox.TextChanged += InputBox_TextChanged;
        }

        // Auto-scroll IM log when messages arrive
        var imLog = this.FindControl<ListBox>("IMLog");
        if (imLog != null)
        {
            _vm.PropertyChanged += (_, args) =>
                {
                    if (args.PropertyName == nameof(IMViewModel.SelectedSession))
                    {
                        StopTypingLocally();
                        WireAutoScroll(imLog);
                    }
                };
            WireAutoScroll(imLog);
        }
    }

    private INotifyCollectionChanged? _currentMessagesCollection;
    private ScrollViewer? _imScrollViewer;

    private void WireAutoScroll(ListBox imLog)
    {
        // Unwire previous
        if (_currentMessagesCollection != null)
        {
            _currentMessagesCollection.CollectionChanged -= OnMessagesChanged;
            _currentMessagesCollection = null;
        }

        // Wire new
        if (_vm?.SelectedSession?.Messages is INotifyCollectionChanged ncc)
        {
            _currentMessagesCollection = ncc;
            ncc.CollectionChanged += OnMessagesChanged;
        }

        // Scroll to the newest message (bottom) when switching sessions
        Dispatcher.UIThread.Post(() =>
        {
            if (imLog.ItemCount > 0)
                imLog.ScrollIntoView(imLog.ItemCount - 1);
        }, DispatcherPriority.Background);

        // Wire scroll-to-top for progressive history loading
        if (_imScrollViewer == null)
        {
            Dispatcher.UIThread.Post(() =>
            {
                _imScrollViewer = imLog.FindDescendantOfType<ScrollViewer>();
                if (_imScrollViewer != null)
                    _imScrollViewer.ScrollChanged += OnImScrollChanged;
            }, DispatcherPriority.Loaded);
        }

        void OnMessagesChanged(object? sender, NotifyCollectionChangedEventArgs args)
        {
            // Don't auto-scroll to bottom when inserting history at the top
            if (_vm?.SelectedSession?.IsLoadingHistory == true) return;
            Dispatcher.UIThread.Post(() =>
            {
                if (imLog.ItemCount > 0)
                    imLog.ScrollIntoView(imLog.ItemCount - 1);
            }, DispatcherPriority.Background);
        }
    }

    private void OnImScrollChanged(object? sender, ScrollChangedEventArgs e)
    {
        if (_imScrollViewer == null || _vm?.SelectedSession == null) return;
        var session = _vm.SelectedSession;
        if (_imScrollViewer.Offset.Y < 10 && !session.IsLoadingHistory && !session.HistoryExhausted)
            _vm.LoadMoreHistory(session);
    }

    private void InputBox_TextChanged(object? sender, TextChangedEventArgs e)
    {
        if (_vm == null) return;

        var hasText = sender is TextBox tb && !string.IsNullOrEmpty(tb.Text);

        if (hasText)
        {
            if (!_isSendingTyping)
            {
                _isSendingTyping = true;
                _vm.NotifyTypingStarted();
            }

            // Reset the idle timer every keystroke
            if (_typingStopTimer == null)
            {
                _typingStopTimer = new DispatcherTimer
                {
                    Interval = TimeSpan.FromSeconds(5)
                };
                _typingStopTimer.Tick += TypingStopTimer_Tick;
            }
            _typingStopTimer.Stop();
            _typingStopTimer.Start();
        }
        else
        {
            StopTypingLocally();
        }
    }

    private void TypingStopTimer_Tick(object? sender, EventArgs e)
    {
        StopTypingLocally();
    }

    private void StopTypingLocally()
    {
        _typingStopTimer?.Stop();
        if (_isSendingTyping)
        {
            _isSendingTyping = false;
            _vm?.NotifyTypingStopped();
        }
    }

    private void InputBox_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && _vm?.SendMessageCommand.CanExecute(null) == true)
        {
            _vm.SendMessageCommand.Execute(null);
            e.Handled = true;
        }
    }

    private void OnSessionNameClick(object? sender, RoutedEventArgs e)
    {
        if (_vm == null) return;
        if (sender is Button { Tag: IMSession session })
            _vm.ShowSessionProfile(session);
    }

    private void SessionListBox_ContextRequested(object? sender, ContextRequestedEventArgs e)
    {
        if (_vm == null || sender is not ListBox lb) return;

        IMSession? session = null;
        if (e.Source is Visual v)
            session = (v as ListBoxItem)?.DataContext as IMSession
                   ?? v.FindAncestorOfType<ListBoxItem>()?.DataContext as IMSession;
        session ??= _vm.SelectedSession;
        if (session == null) return;

        switch (session.SessionType)
        {
            case IMSessionType.Personal:
                AvatarMenuBuilder.Build(_vm.Instance, session.TargetId, session.Label).Open(lb);
                e.Handled = true;
                break;
            case IMSessionType.Group:
                GroupMenuBuilder.Build(_vm.Instance, session.TargetId, session.Label).Open(lb);
                e.Handled = true;
                break;
        }
    }

    private void OnIMLineContextRequested(object? sender, ContextRequestedEventArgs e)
    {
        if (sender is not WrapPanel panel || panel.DataContext is not ChatLine line) return;
        var copyItem = new MenuItem { Header = "Copy" };
        copyItem.Click += async (_, _) =>
        {
            var imLog = this.FindControl<ListBox>("IMLog");
            var lines = imLog?.SelectedItems?.OfType<ChatLine>().ToList();
            if (lines == null || lines.Count == 0) lines = [line];
            var text = string.Join(Environment.NewLine, lines.Select(l => l.AutomationText));
            IClipboard? clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
            if (clipboard != null) await clipboard.SetTextAsync(text);
        };
        var menu = new ContextMenu { Items = { copyItem } };
        menu.Open(panel);
        e.Handled = true;
    }

    private void OnIMNameClick(object? sender, RoutedEventArgs e)
    {
        if (_vm == null) return;
        if (sender is Button { Tag: ChatLine chatLine } && chatLine.HasAgentLink)
        {
            _vm.ShowAgentProfile(chatLine.AgentID, chatLine.From);
        }
    }

    private void OnIMNameContextRequested(object? sender, ContextRequestedEventArgs e)
    {
        if (_vm == null || sender is not Button btn || btn.Tag is not ChatLine chatLine || !chatLine.HasAgentLink) return;
        AvatarMenuBuilder.Build(_vm.Instance, chatLine.AgentID, chatLine.From).Open(btn);
        e.Handled = true;
    }

    private void OnParticipantClick(object? sender, RoutedEventArgs e)
    {
        if (_vm == null) return;
        if (sender is Button { Tag: IMParticipant participant })
        {
            _vm.ShowAgentProfile(participant.Id, participant.Name);
        }
    }

    private void OnParticipantContextRequested(object? sender, ContextRequestedEventArgs e)
    {
        if (_vm == null || sender is not Button btn || btn.Tag is not IMParticipant participant) return;
        AvatarMenuBuilder.Build(_vm.Instance, participant.Id, participant.Name).Open(btn);
        e.Handled = true;
    }
}
