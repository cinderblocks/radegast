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
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;

namespace Veles.Plugin.Automation.UI;

internal sealed class AutomationRulesPanelViewModel : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    private readonly RuleEngine _engine;
    private readonly List<AutomationRule> _rules;
    private readonly Action _onSave;
    private readonly Action _onImport;
    private readonly Action _onExport;

    public ObservableCollection<AutomationRuleViewModel> Rules { get; } = [];

    private AutomationRuleViewModel? _selectedRule;
    public AutomationRuleViewModel? SelectedRule
    {
        get => _selectedRule;
        set
        {
            _selectedRule = value;
            Notify();
            RaiseCanExecuteChanged();
        }
    }

    // ── Commands ───────────────────────────────────────────────────
    public ICommand AddRuleCommand    { get; }
    public ICommand EditRuleCommand   { get; }
    public ICommand RemoveRuleCommand { get; }
    public ICommand ToggleRuleCommand { get; }
    public ICommand MoveUpCommand     { get; }
    public ICommand MoveDownCommand   { get; }
    public ICommand ImportRulesCommand { get; }
    public ICommand ExportRulesCommand { get; }

    private readonly List<DelegateCommand> _selectionDependentCmds = [];

    public AutomationRulesPanelViewModel(RuleEngine engine, List<AutomationRule> rules,
        Action onSave, Action onImport, Action onExport)
    {
        _engine   = engine;
        _rules    = rules;
        _onSave   = onSave;
        _onImport = onImport;
        _onExport = onExport;

        AddRuleCommand     = new DelegateCommand(AddRule);
        EditRuleCommand    = new DelegateCommand(EditRule,   HasSelection);
        RemoveRuleCommand  = new DelegateCommand(RemoveRule, HasSelection);
        ToggleRuleCommand  = new DelegateCommand(ToggleRule, HasSelection);
        MoveUpCommand      = new DelegateCommand(MoveUp,    CanMoveUp);
        MoveDownCommand    = new DelegateCommand(MoveDown,  CanMoveDown);
        ImportRulesCommand = new DelegateCommand(() => _onImport());
        ExportRulesCommand = new DelegateCommand(() => _onExport());

        _selectionDependentCmds.AddRange([
            (DelegateCommand)EditRuleCommand,
            (DelegateCommand)RemoveRuleCommand,
            (DelegateCommand)ToggleRuleCommand,
            (DelegateCommand)MoveUpCommand,
            (DelegateCommand)MoveDownCommand,
        ]);

        RefreshList();
    }

    private void RefreshList()
    {
        Rules.Clear();
        List<AutomationRule> snapshot;
        lock (_rules) { snapshot = [.._rules]; }
        foreach (var rule in snapshot)
            Rules.Add(new AutomationRuleViewModel(rule));
    }

    private void RaiseCanExecuteChanged()
    {
        foreach (var cmd in _selectionDependentCmds)
            cmd.RaiseCanExecuteChanged();
    }

    // ── Command implementations ────────────────────────────────────

    private void AddRule()
    {
        var newRule = new AutomationRule { Name = "New Rule" };
        var editor = new RuleEditorWindow(newRule);
        editor.Closed += (_, _) =>
        {
            if (!editor.Committed) return;
            lock (_rules) _rules.Add(newRule);
            _engine.AddRule(newRule);
            Rules.Add(new AutomationRuleViewModel(newRule));
            _onSave();
        };
        editor.Show();
    }

    private void EditRule()
    {
        if (_selectedRule == null) return;
        var editor = new RuleEditorWindow(_selectedRule.Rule);
        editor.Closed += (_, _) =>
        {
            if (!editor.Committed) return;
            _selectedRule.Refresh();
            _onSave();
        };
        editor.Show();
    }

    private void RemoveRule()
    {
        if (_selectedRule == null) return;
        var id = _selectedRule.Rule.Id;
        lock (_rules) _rules.RemoveAll(r => r.Id == id);
        _engine.RemoveRule(id);
        Rules.Remove(_selectedRule);
        SelectedRule = null;
        _onSave();
    }

    private void ToggleRule()
    {
        if (_selectedRule == null) return;
        _engine.SetEnabled(_selectedRule.Rule.Id, !_selectedRule.Rule.Enabled);
        _selectedRule.Refresh();
        _onSave();
    }

    private void MoveUp()
    {
        if (_selectedRule == null) return;
        var idx = Rules.IndexOf(_selectedRule);
        if (idx <= 0) return;
        Rules.Move(idx, idx - 1);
        lock (_rules) { var item = _rules[idx]; _rules.RemoveAt(idx); _rules.Insert(idx - 1, item); }
        _onSave();
        RaiseCanExecuteChanged();
    }

    private void MoveDown()
    {
        if (_selectedRule == null) return;
        var idx = Rules.IndexOf(_selectedRule);
        if (idx < 0 || idx >= Rules.Count - 1) return;
        Rules.Move(idx, idx + 1);
        lock (_rules) { var item = _rules[idx]; _rules.RemoveAt(idx); _rules.Insert(idx + 1, item); }
        _onSave();
        RaiseCanExecuteChanged();
    }

    private bool HasSelection()  => _selectedRule != null;
    private bool CanMoveUp()     => _selectedRule != null && Rules.IndexOf(_selectedRule) > 0;
    private bool CanMoveDown()   => _selectedRule != null && Rules.IndexOf(_selectedRule) < Rules.Count - 1;

    private void Notify([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

/// <summary>Minimal ICommand implementation.</summary>
internal sealed class DelegateCommand : ICommand
{
    private readonly Action _execute;
    private readonly Func<bool>? _canExecute;

    public event EventHandler? CanExecuteChanged;

    public DelegateCommand(Action execute, Func<bool>? canExecute = null)
    {
        _execute    = execute;
        _canExecute = canExecute;
    }

    public bool CanExecute(object? parameter) => _canExecute?.Invoke() ?? true;
    public void Execute(object? parameter)    => _execute();
    public void RaiseCanExecuteChanged()      => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}
