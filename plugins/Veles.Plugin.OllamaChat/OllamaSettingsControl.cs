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

using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Radegast.Veles.PluginApi;

namespace Veles.Plugin.OllamaChat;

/// <summary>
/// Code-only Avalonia settings pane shown in the Veles Preferences window.
/// </summary>
internal sealed class OllamaSettingsControl : UserControl
{
    private readonly TextBox     _baseUrlBox;
    private readonly TextBox     _modelBox;
    private readonly TextBox     _systemPromptBox;
    private readonly NumericUpDown _maxTokensBox;
    private readonly CheckBox    _enabledBox;
    private readonly CheckBox    _respondWithoutNameBox;
    private readonly CheckBox    _respondToLocalBox;
    private readonly CheckBox    _respondToPersonalIMBox;
    private readonly CheckBox    _respondToAdHocBox;
    private readonly CheckBox    _respondToGroupBox;
    private readonly CheckBox    _shout2shoutBox;
    private readonly CheckBox    _whisper2whisperBox;
    private readonly CheckBox    _randomDelayBox;
    private readonly CheckBox    _speakAnswersBox;
    private readonly ComboBox    _rangeCombo;
    private readonly Button      _fetchModelsButton;
    private readonly TextBlock   _modelsStatusText;

    // Range combo items
    private static readonly List<(string Label, string Value)> Ranges =
    [
        ("Unlimited",  "-1"),
        ("5 m",         "5"),
        ("10 m",       "10"),
        ("15 m",       "15"),
        ("20 m",       "20"),
    ];

    public OllamaSettingsControl(IPluginContext ctx)
    {
        string S(string k, string d) => ctx.GetSetting($"ollama_{k}") ?? d;
        bool B(string k, bool d)     => (ctx.GetSetting($"ollama_{k}") ?? (d ? "true" : "false")) == "true";

        _enabledBox             = new CheckBox { Content = "Enable chatbot",                          IsChecked = B("enabled",              false) };
        _respondWithoutNameBox  = new CheckBox { Content = "Respond without name mention",             IsChecked = B("respond_without_name", false) };
        _respondToLocalBox      = new CheckBox { Content = "Respond to local chat",                   IsChecked = B("respond_to_local",      true)  };
        _respondToPersonalIMBox = new CheckBox { Content = "Respond to personal IMs",                 IsChecked = B("respond_to_personal_im",true)  };
        _respondToAdHocBox      = new CheckBox { Content = "Respond to IM/Ad-hoc conferences",        IsChecked = B("respond_to_adhoc",      false) };
        _respondToGroupBox      = new CheckBox { Content = "Respond to group chat (name-mention only)",IsChecked = B("respond_to_group",      false) };
        _shout2shoutBox         = new CheckBox { Content = "Match shout with shout",                  IsChecked = B("shout2shout",           false) };
        _whisper2whisperBox    = new CheckBox { Content = "Match whisper with whisper", IsChecked = B("whisper2whisper",       false) };
        _randomDelayBox        = new CheckBox { Content = "Simulate typing delay",      IsChecked = B("random_delay",          false) };
        _speakAnswersBox       = new CheckBox { Content = "Speak answers via TTS (voice channel)", IsChecked = B("speak_answers", false) };

        _baseUrlBox = new TextBox
        {
            Text = S("base_url", "http://localhost:11434"),
            PlaceholderText = "http://localhost:11434",
        };
        _modelBox = new TextBox
        {
            Text = S("model", "llama3"),
            PlaceholderText = "llama3",
        };
        _systemPromptBox = new TextBox
        {
            Text = S("system_prompt",
                "You are a friendly resident of Second Life. Keep replies concise (1-3 sentences). " +
                "You can talk about SL culture, building, scripting, and everyday conversation."),
            TextWrapping = Avalonia.Media.TextWrapping.Wrap,
            AcceptsReturn = true,
            Height = 100,
        };
        _maxTokensBox = new NumericUpDown
        {
            Minimum  = 64,
            Maximum  = 4096,
            Increment = 64,
            Value    = int.TryParse(S("max_tokens", "512"), out int t) ? t : 512,
        };

        _rangeCombo = new ComboBox();
        string savedRange = S("respond_range", "-1");
        int selectedIdx = 0;
        for (int i = 0; i < Ranges.Count; i++)
        {
            _rangeCombo.Items.Add(Ranges[i].Label);
            if (Ranges[i].Value == savedRange) selectedIdx = i;
        }
        _rangeCombo.SelectedIndex = selectedIdx;

        _modelsStatusText = new TextBlock
        {
            Text    = string.Empty,
            Opacity = 0.7,
            TextWrapping = Avalonia.Media.TextWrapping.Wrap,
        };
        _fetchModelsButton = new Button
        {
            Content = "Fetch available models",
        };
        _fetchModelsButton.Click += async (_, _) =>
        {
            _fetchModelsButton.IsEnabled = false;
            _modelsStatusText.Text = "Querying Ollama…";

            var client = new OllamaClient { BaseUrl = _baseUrlBox.Text ?? "http://localhost:11434" };
            List<string> models = await client.ListModelsAsync();
            client.Dispose();

            if (models.Count == 0)
                _modelsStatusText.Text = "No models found — is Ollama running at that URL?";
            else
                _modelsStatusText.Text = "Available: " + string.Join(", ", models);

            _fetchModelsButton.IsEnabled = true;
        };

        Content = new ScrollViewer
        {
            Content = new StackPanel
            {
                Margin  = new Thickness(12),
                Spacing = 12,
                Children =
                {
                    MakeSection("General",
                        _enabledBox,
                        _respondWithoutNameBox,
                        _respondToLocalBox,
                        _respondToPersonalIMBox,
                        _respondToAdHocBox,
                        _respondToGroupBox,
                        _shout2shoutBox,
                        _whisper2whisperBox,
                        _randomDelayBox,
                        _speakAnswersBox,
                        MakeRow("Response range", _rangeCombo)),

                    MakeSection("Ollama Connection",
                        MakeRow("Base URL",  _baseUrlBox),
                        MakeRow("Model",     _modelBox),
                        MakeRow("Max tokens", _maxTokensBox),
                        new StackPanel
                        {
                            Orientation = Orientation.Horizontal,
                            Spacing = 8,
                            Children = { _fetchModelsButton, _modelsStatusText },
                        }),

                    MakeSection("System Prompt",
                        _systemPromptBox,
                        new TextBlock
                        {
                            Text = "Instructs the LLM how to behave. Changes take effect after clicking Apply.",
                            Opacity = 0.6,
                            TextWrapping = Avalonia.Media.TextWrapping.Wrap,
                            Margin = new Thickness(0, 2, 0, 0),
                        }),
                },
            },
        };
    }

    public void Apply(IPluginContext ctx)
    {
        void Save(string k, string v) => ctx.SetSetting($"ollama_{k}", v);

        Save("enabled",              _enabledBox.IsChecked == true            ? "true" : "false");
        Save("respond_without_name", _respondWithoutNameBox.IsChecked == true  ? "true" : "false");
        Save("respond_to_local",     _respondToLocalBox.IsChecked == true      ? "true" : "false");
        Save("respond_to_personal_im",_respondToPersonalIMBox.IsChecked == true ? "true" : "false");
        Save("respond_to_adhoc",     _respondToAdHocBox.IsChecked == true      ? "true" : "false");
        Save("respond_to_group",     _respondToGroupBox.IsChecked == true      ? "true" : "false");
        Save("shout2shout",          _shout2shoutBox.IsChecked == true         ? "true" : "false");
        Save("whisper2whisper",      _whisper2whisperBox.IsChecked == true      ? "true" : "false");
        Save("random_delay",         _randomDelayBox.IsChecked == true         ? "true" : "false");
        Save("speak_answers",        _speakAnswersBox.IsChecked == true        ? "true" : "false");
        Save("base_url",             _baseUrlBox.Text?.Trim()   ?? "http://localhost:11434");
        Save("model",                _modelBox.Text?.Trim()     ?? "llama3");
        Save("system_prompt",        _systemPromptBox.Text      ?? string.Empty);
        Save("max_tokens",           ((int)(_maxTokensBox.Value ?? 512)).ToString());

        int idx = _rangeCombo.SelectedIndex;
        Save("respond_range", idx >= 0 && idx < Ranges.Count ? Ranges[idx].Value : "-1");
    }

    // ── Layout helpers ────────────────────────────────────────────────────

    private static Border MakeSection(string title, params Control[] rows)
    {
        var stack = new StackPanel { Spacing = 6 };
        stack.Children.Add(new TextBlock
        {
            Text       = title,
            FontWeight = Avalonia.Media.FontWeight.SemiBold,
            Margin     = new Thickness(0, 0, 0, 2),
        });
        foreach (var row in rows)
            stack.Children.Add(row);

        return new Border
        {
            BorderThickness = new Thickness(1),
            CornerRadius    = new CornerRadius(4),
            Padding         = new Thickness(10),
            Child           = stack,
        };
    }

    private static Control MakeRow(string label, Control control)
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("140,*") };
        var lbl  = new TextBlock
        {
            Text = label,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 8, 0),
        };
        Grid.SetColumn(lbl,     0);
        Grid.SetColumn(control, 1);
        grid.Children.Add(lbl);
        grid.Children.Add(control);
        return grid;
    }
}
