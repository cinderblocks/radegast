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
using Avalonia.Controls;

namespace Radegast.Veles.Views;

public partial class CrashWindow : Window
{
    private readonly string _text;

    public CrashWindow() { InitializeComponent(); _text = string.Empty; }

    public CrashWindow(Exception ex)
    {
        InitializeComponent();
        _text = FormatException(ex);
    }

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);

        var txt = this.FindControl<TextBlock>("TxtException");
        txt?.Text = _text;

        var btnClose = this.FindControl<Button>("BtnClose");
        if (btnClose != null) btnClose.Click += (_, _) => Close();

        var btnCopy = this.FindControl<Button>("BtnCopy");
        if (btnCopy != null)
            btnCopy.Click += async (_, _) =>
            {
                var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
                if (clipboard != null)
                    await clipboard.SetTextAsync(_text);
            };
    }

    private static string FormatException(Exception ex)
    {
        var sb = new System.Text.StringBuilder();
        var current = ex;
        int depth = 0;
        while (current != null)
        {
            if (depth > 0) sb.AppendLine().AppendLine("--- Inner Exception ---");
            sb.AppendLine($"{current.GetType().FullName}: {current.Message}");
            if (current.StackTrace != null)
                sb.AppendLine(current.StackTrace);
            current = current.InnerException;
            depth++;
        }
        return sb.ToString();
    }
}
