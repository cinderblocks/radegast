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
using System.IO;
using System.Linq;
using System.Text;
using System.Xml;
using AvaloniaEdit.Highlighting;
using AvaloniaEdit.Highlighting.Xshd;
using LibreMetaverse;

namespace Radegast.Veles.Controls;

public static class LslHighlighting
{
    private static IHighlightingDefinition? _definition;

    public static IHighlightingDefinition? GetDefinition()
    {
        if (_definition != null) return _definition;

        try
        {
            // Populate LslSyntax keywords from disk (linden/keywords_lsl_default.xml)
            _ = new LslSyntax();
            var xml = BuildXml(LslSyntax.Keywords);
            using var reader = XmlReader.Create(new StringReader(xml));
            _definition = HighlightingLoader.Load(reader, HighlightingManager.Instance);
        }
        catch (Exception ex)
        {
            LibreMetaverse.Logger.Warn($"LslHighlighting: failed to build definition: {ex.Message}");
        }

        return _definition;
    }

    private static string BuildXml(System.Collections.Frozen.FrozenDictionary<string, LslSyntax.LslKeyword> keywords)
    {
        // Group by category
        var byCategory = keywords.Values
            .Where(k => !k.GodMode)
            .GroupBy(k => k.Category)
            .ToDictionary(g => g.Key, g => g.Select(k => k.Keyword).OrderBy(k => k).ToList());

        var sb = new StringBuilder();
        sb.AppendLine("""<?xml version="1.0"?>""");
        sb.AppendLine("""<SyntaxDefinition name="LSL" xmlns="http://icsharpcode.net/sharpdevelop/syntaxdefinition/2008">""");

        // Color definitions
        sb.AppendLine("""  <Color name="Comment" foreground="#CB6020" />""");
        sb.AppendLine("""  <Color name="String" foreground="#006600" />""");
        sb.AppendLine("""  <Color name="Number" foreground="#A020F0" />""");
        sb.AppendLine("""  <Color name="Control" foreground="#B000B0" fontWeight="bold" />""");
        sb.AppendLine("""  <Color name="DataType" foreground="#0055EE" fontWeight="bold" />""");
        sb.AppendLine("""  <Color name="Event" foreground="#005599" fontWeight="bold" />""");
        sb.AppendLine("""  <Color name="Constant" foreground="#1A1A7F" />""");
        sb.AppendLine("""  <Color name="Function" foreground="#880000" />""");

        sb.AppendLine("  <RuleSet>");

        // Line comments
        sb.AppendLine("""    <Span color="Comment"><Begin>//</Begin></Span>""");
        // Block comments
        sb.AppendLine("""    <Span color="Comment" multiline="true"><Begin>/\*</Begin><End>\*/</End></Span>""");
        // Strings with escape sequences
        sb.AppendLine("""    <Span color="String"><Begin>"</Begin><End>"</End><RuleSet><Span begin="\\" end="." /></RuleSet></Span>""");

        // Keywords by category
        AppendKeywords(sb, "Control", byCategory, LslSyntax.LslCategory.Control, LslSyntax.LslCategory.Flow);
        AppendKeywords(sb, "DataType", byCategory, LslSyntax.LslCategory.Datatype);
        AppendKeywords(sb, "Event", byCategory, LslSyntax.LslCategory.Event);
        AppendKeywords(sb, "Constant", byCategory, LslSyntax.LslCategory.Constant);
        AppendKeywords(sb, "Function", byCategory, LslSyntax.LslCategory.Function);

        // Numbers: hex and decimal/float
        sb.AppendLine("""    <Rule color="Number">\b0x[0-9a-fA-F]+|(\b\d+(\.\d+)?|\.\d+)([eE][+-]?\d+)?</Rule>""");

        sb.AppendLine("  </RuleSet>");
        sb.AppendLine("</SyntaxDefinition>");

        return sb.ToString();
    }

    private static void AppendKeywords(StringBuilder sb, string colorName,
        Dictionary<LslSyntax.LslCategory, List<string>> byCategory,
        params LslSyntax.LslCategory[] categories)
    {
        var words = categories
            .Where(byCategory.ContainsKey)
            .SelectMany(c => byCategory[c])
            .Distinct()
            .OrderBy(k => k)
            .ToList();

        if (words.Count == 0) return;

        sb.AppendLine($"""    <Keywords color="{colorName}">""");
        foreach (var word in words)
        {
            sb.AppendLine($"      <Word>{System.Security.SecurityElement.Escape(word)}</Word>");
        }
        sb.AppendLine("    </Keywords>");
    }
}
