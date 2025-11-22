/**
 * Radegast Metaverse Client
 * Copyright(c) 2009-2014, Radegast Development Team
 * Copyright(c) 2016-2025, Sjofn, LLC
 * All rights reserved.
 *  
 * Radegast is free software: you can redistribute it and/or modify
 * it under the terms of the GNU Lesser General Public License as published
 * by the Free Software Foundation, either version 3 of the License, or
 * (at your option) any later version.
 * 
 * This program is distributed in the hope that it will be useful,
 * but WITHOUT ANY WARRANTY; without even the implied warranty of
 * MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.See the
 * GNU General Public License for more details.
 * 
 * You should have received a copy of the GNU Lesser General Public License
 * along with this program.If not, see<https://www.gnu.org/licenses/>.
 */

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Text.RegularExpressions;
using System.IO;
using System.Windows.Forms;
using System.Threading;
using System.Threading.Tasks;
using OpenMetaverse;
using OpenMetaverse.Assets;

namespace Radegast
{

    public partial class ScriptEditor : UserControl
    {
        private readonly RadegastInstanceForms instance;
        private GridClient client => instance.Client;
        private readonly InventoryLSL script;
        private string scriptName;
        private string fileName;
        private readonly Primitive prim;

        public ScriptEditor(RadegastInstanceForms instance)
            : this(instance, null, null)
        {
            scriptName = "New Script";
        }

        public ScriptEditor(RadegastInstanceForms instance, InventoryLSL script)
            : this(instance, script, null)
        {
        }

        public ScriptEditor(RadegastInstanceForms instance, InventoryLSL script, Primitive prim)
        {
            InitializeComponent();
            Disposed += ScriptEditor_Disposed;

            this.instance = instance;
            this.script = script;
            this.prim = prim;

            rtb.SyntaxHighlightEnabled = instance.GlobalSettings["script_syntax_highlight"].AsBoolean();
            lblScripStatus.Text = string.Empty;
            lblScripStatus.TextChanged += (sender, e) =>
                instance.ShowNotificationInChat(lblScripStatus.Text, ChatBufferTextStyle.Invisible);
            Dock = DockStyle.Fill;
            TabStop = false;

            if (prim == null)
            {
                cbRunning.Visible = false;
            }

            // Download script
            if (script != null)
            {
                scriptName = script.Name;
                var transferID = UUID.Random();
                if (prim != null)
                {
                    client.Assets.RequestInventoryAsset(script.AssetUUID, script.UUID, prim.ID, prim.OwnerID, script.AssetType, 
                        true, transferID, Assets_OnAssetReceived);
                    client.Inventory.RequestGetScriptRunning(prim.ID, script.UUID);
                    client.Inventory.ScriptRunningReply += OnScriptRunningReplyReceived;
                }
                else
                {
                    client.Assets.RequestInventoryAsset(script, true, transferID, Assets_OnAssetReceived);
                }
                rtb.Text = lblScripStatus.Text = "Loading script...";
            }
            else
            {
                rtb.Text = " "; //bugs in control grrrr
                rtb.SelectionStart = 0;
            }

            GUI.GuiHelpers.ApplyGuiFixes(this);
        }

        private void ScriptEditor_Disposed(object sender, EventArgs e)
        {
            client.Inventory.ScriptRunningReply -= OnScriptRunningReplyReceived;
        }

        private void OnScriptRunningReplyReceived(object sender, ScriptRunningReplyEventArgs e)
        {
            if (InvokeRequired)
            {
                if (!instance.MonoRuntime || IsHandleCreated)
                    BeginInvoke(new MethodInvoker(() => OnScriptRunningReplyReceived(sender, e)));
                return;
            }

            cbRunning.Checked = e.IsRunning;
            cbMono.Checked = e.IsMono;
        }

        private void Assets_OnAssetReceived(AssetDownload transfer, Asset asset)
        {
            if (InvokeRequired)
            {
                if (!instance.MonoRuntime || IsHandleCreated)
                    BeginInvoke(new MethodInvoker(() => Assets_OnAssetReceived(transfer, asset)));
                return;
            }

            if (!transfer.Success || asset.AssetType != AssetType.LSLText) {
                lblScripStatus.Text = rtb.Text = "Failed to download.";
                return;
            } else {
                lblScripStatus.Text = rtb.Text = "OK";
            }
            asset.Decode();
            rtb.Text = ((AssetScriptText)asset).Source;
            lineNubersForRtb.Invalidate();
            SetTitle();
        }

        private void SetTitle()
        {
            if (detached)
            {
                FindForm().Text = $"{scriptName} - {Properties.Resources.ProgramName} script editor";
            }
        }

        #region Detach/Attach
        private Control originalParent;
        private Form detachedForm;
        private bool detached = false;

        public bool Detached
        {
            get => detached;

            set
            {
                if (detached == value) return; // no change

                detached = value;

                if (detached)
                {
                    Detach();
                }
                else
                {
                    Retach();
                }
            }
        }

        public void ShowDetached()
        {
            Detached = true;
        }

        private void Detach()
        {
            detached = true;

            detachedForm?.Dispose();

            detachedForm = new Form();
            originalParent = Parent;
            Parent = detachedForm;
            detachedForm.ClientSize = new Size(873, 580);
            SetTitle();
            detachedForm.ActiveControl = this;
            detachedForm.Show();
            detachedForm.FormClosing += detachedForm_FormClosing;

            if (originalParent != null)
            {
                originalParent.ControlAdded += originalParent_ControlAdded;
            }
            else
            {
                tbtnAttach.Visible = false;
            }

            tbtnAttach.Text = "Retach";

            if (originalParent == null)
            {
                tSeparator1.Visible = true;
                tbtnExit.Visible = true;
            }
            else
            {
                tSeparator1.Visible = false;
                tbtnExit.Visible = false;
            }
        }

        private void originalParent_ControlAdded(object sender, ControlEventArgs e)
        {
            if (detachedForm != null)
            {
                detachedForm.FormClosing -= detachedForm_FormClosing;
            }
            originalParent.ControlAdded -= originalParent_ControlAdded;
            tbtnAttach.Visible = false;
        }

        private void detachedForm_FormClosing(object sender, FormClosingEventArgs e)
        {
            Retach();
        }

        private void Retach()
        {
            detached = false;

            if (originalParent != null)
            {
                originalParent.ControlAdded -= originalParent_ControlAdded;
                Parent = originalParent;
            }

            if (detachedForm != null)
            {
                detachedForm.Dispose();
                detachedForm = null;
            }

            tbtnAttach.Text = "Detach";

            if (originalParent == null)
            {
                Dispose();
            }
        }

        private void tbtnAttach_Click(object sender, EventArgs e)
        {
            if (detached)
            {
                Retach();
            }
            else
            {
                Detach();
            }
        }
        #endregion


        #region File I/O
        private void tbtbSaveToDisk_Click_1(object sender, EventArgs e)
        {
            if (string.IsNullOrEmpty(fileName))
            {
                tbtbSaveToDisk_Click(sender, e);
                return;
            }
            File.WriteAllText(fileName, rtb.Text);
        }

        private void tbtbSaveToDisk_Click(object sender, EventArgs e)
        {
            SaveFileDialog dlg = new SaveFileDialog
            {
                Title = "Save script",
                Filter = "LSL script file (*.lsl)|*.lsl|Plain text file (*.txt)|*.txt",
                FileName = Utilities.SafeFileName(scriptName)
            };
            DialogResult res = dlg.ShowDialog();

            if (res == DialogResult.OK)
            {
                fileName = dlg.FileName;
                scriptName = Path.GetFileName(fileName);
                SetTitle();
                File.WriteAllText(fileName, rtb.Text);
            }

        }

        private void tbtbLoadFromDisk_Click(object sender, EventArgs e)
        {
            OpenFileDialog dlg = new OpenFileDialog
            {
                Title = "Open script",
                Filter = "LSL script files (*.lsl)|*.lsl|Plain text files (*.txt)|*.txt|All files (*.*)|*.*",
                Multiselect = false
            };
            DialogResult res = dlg.ShowDialog();

            if (res == DialogResult.OK)
            {
                fileName = dlg.FileName;
                scriptName = Path.GetFileName(fileName);
                SetTitle();
                rtb.Text = File.ReadAllText(fileName);
                lineNubersForRtb.Invalidate();
            }
        }
        #endregion

        private void tbtnExit_Click(object sender, EventArgs e)
        {
            FindForm()?.Close();
        }

        private void rtb_SelectionChanged(object sender, EventArgs e)
        {
            RRichTextBox.CursorLocation c = rtb.CursorPosition;
            lblLine.Text = $"Ln {c.Line + 1}";
            lblCol.Text = $"Col {c.Column + 1}";
        }

        private bool spaceOrTab(char c)
        {
            return c == ' ' || c == '\t';
        }

        private int lastPos(string s, char c)
        {
            s = s.TrimEnd();

            if (s == string.Empty)
                return -1;

            if (s[s.Length - 1] == c)
            {
                return s.LastIndexOf(c);
            }
            return -1;
        }

        //private RRichTextBox.CursorLocation prevCursor;

        private void rtb_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.S && e.Control && !e.Shift)
            {
                tbtbSave_Click(this, EventArgs.Empty);
                e.Handled = e.SuppressKeyPress = true;
                return;
            }

            if (e.KeyCode == Keys.L && e.Control && !e.Shift)
            {
                ReadCursorPosition();
                e.Handled = e.SuppressKeyPress = true;
                return;
            }

            if (e.Modifiers == Keys.Control && e.KeyCode == Keys.F)
            {
                findToolStripMenuItem_Click(null, null);
            }
            if (e.KeyCode == Keys.Tab)
            {
                rtb.SelectedText = "    ";
                e.Handled = true;
            }
            /*
            else if (e.KeyCode == Keys.Enter)
            {
                e.Handled = true;
                e.SuppressKeyPress = true;

                // RRichTextBox.CursorLocation cl = prevCursor;
                RRichTextBox.CursorLocation cl = rtb.CursorPosition;
                string prevLine = rtb.Lines[cl.Line];
                string addIndent = "\n";
                int pos;

                for (int spaces = 0;
                    spaces < prevLine.Length && spaceOrTab(prevLine[spaces]);
                    addIndent += prevLine[spaces++]) ;

                System.Console.WriteLine("In identer");
                if ((pos = lastPos(prevLine.Substring(0, cl.Column), '{')) != -1)
                {
                    addIndent += "    ";
                }

                rtb.SelectedText = addIndent;
                int eat = 0;
                for (eat = 0; (eat + rtb.SelectionStart) < rtb.Text.Length && rtb.Text[rtb.SelectionStart + eat] == ' '; eat++) ;
                rtb.SelectionLength = eat;
                rtb.SelectedText = "";
            }
            */

        }


        private void rtb_KeyPress(object sender, KeyPressEventArgs e)
        {/*
            if (rtb.Lines.Length > 0)
            {
                if (e.KeyChar == '}')
                {
                    string li = rtb.Lines[rtb.GetLineFromCharIndex(rtb.SelectionStart)];

                    if (li.Trim() == "")
                    {
                        rtb.BeginUpdate();
                        int toDelete = li.Length;

                        if (toDelete > 4)
                        {
                            toDelete = 4;
                        }

                        rtb.SelectionStart -= toDelete;
                        rtb.SelectionLength = toDelete;
                        rtb.SelectedText = "";
                        rtb.EndUpdate();
                    }
                }
            }
          */
        }

        #region Edit menu handlers
        private void undoToolStripMenuItem_Click(object sender, EventArgs e)
        {
            rtb.Undo();
        }

        private void redoToolStripMenuItem_Click(object sender, EventArgs e)
        {
            rtb.Redo();
        }

        private void cutToolStripMenuItem_Click(object sender, EventArgs e)
        {
            rtb.Cut();
        }

        private void copyToolStripMenuItem_Click(object sender, EventArgs e)
        {
            rtb.Copy();
        }

        private void pasteToolStripMenuItem_Click(object sender, EventArgs e)
        {
            rtb.Paste();
        }

        private void deleteToolStripMenuItem_Click(object sender, EventArgs e)
        {
            rtb.SelectedText = string.Empty;
        }

        private void selectAllToolStripMenuItem_Click(object sender, EventArgs e)
        {
            rtb.SelectionStart = 0;
            rtb.SelectionLength = rtb.Text.Length;
        }

        private void findToolStripMenuItem_Click(object sender, EventArgs e)
        {
            tsFindReplace.Show();
            tfindFindText.Focus();
        }
        #endregion

        private void tfindClose_Click(object sender, EventArgs e)
        {
            tsFindReplace.Hide();
        }

        private class FindHistoryItem
        {
            public FindHistoryItem(string term, int ss, int sl)
            {
                Term = term;
                SelStart = ss;
                SelLength = sl;
            }

            public readonly string Term;
            public readonly int SelStart;
            public readonly int SelLength;

            public override string ToString()
            {
                return Term;
            }
        }

        private readonly Dictionary<string, FindHistoryItem> FindHistory = new Dictionary<string, FindHistoryItem>();
        private FindHistoryItem startPos;
        private int searchFrom = 0;

        private void tfindFindText_TextChanged(object sender, EventArgs e)
        {
            string st = tfindFindText.Text;

            if (startPos == null)
            {
                startPos = new FindHistoryItem(string.Empty, rtb.SelectionStart, rtb.SelectionLength);
                FindHistory[startPos.Term] = startPos;
            }

            StringComparison type = tfindMatchCase.Checked ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;

            if (FindHistory.ContainsKey(st))
            {
                tfindFindText.BackColor = Color.FromKnownColor(KnownColor.Window);
                FindHistoryItem h = FindHistory[st];
                rtb.BeginUpdate();
                rtb.Select(h.SelStart, h.SelLength);
                rtb.ScrollToCaret();
                rtb.EndUpdate();
                searchFrom = h.SelStart;

                if (st == string.Empty)
                {
                    FindHistory.Clear();
                    tfindFindText.BackColor = Color.FromKnownColor(KnownColor.Window);
                }

                return;
            }

            if (st == string.Empty)
            {
                FindHistory.Clear();
                tfindFindText.BackColor = Color.FromKnownColor(KnownColor.Window);
                return;
            }

            int pos = rtb.Text.IndexOf(st, searchFrom, type);

            if (pos != -1)
            {
                tfindFindText.BackColor = Color.FromKnownColor(KnownColor.Window);
                FindHistory[st] = new FindHistoryItem(st, pos, st.Length);
                rtb.BeginUpdate();
                rtb.Select(pos, st.Length);
                rtb.ScrollToCaret();
                rtb.EndUpdate();
                searchFrom = pos;
            }
            else
            {
                searchFrom = 0;
                tfindFindText.BackColor = Color.FromArgb(200, 0, 0);
            }
        }

        private void tfindFindText_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter)
            {
                e.SuppressKeyPress = true;
            }
        }

        private void tfindFindText_KeyUp(object sender, KeyEventArgs e)
        {
            switch (e.KeyCode)
            {
                case Keys.Enter:
                    tfindDoFind_Click(null, null);
                    e.Handled = true;
                    e.SuppressKeyPress = true;
                    break;
            }
        }

        private void tfindDoFind_Click(object sender, EventArgs e)
        {
            FindHistory.Clear();
            searchFrom += rtb.SelectionLength;
            tfindFindText_TextChanged(sender, e);
        }

        private void tfindFindText_Leave(object sender, EventArgs e)
        {
            startPos = null;
            FindHistory.Clear();
        }

        private void tfindFindText_Enter(object sender, EventArgs e)
        {
            searchFrom = rtb.SelectionStart;
        }


        private void tfindFindNextReplace_Click(object sender, EventArgs e)
        {
            tfindDoFind_Click(null, null);
        }

        private void tfindReplace_Click(object sender, EventArgs e)
        {
            if (tfindFindText.Text.Length > 0 && rtb.SelectionLength > 0)
            {
                rtb.SelectedText = tfindReplaceText.Text;
                tfindDoFind_Click(null, null);
            }
        }

        private static string ReplaceEx(string original, string pattern, string replacement)
        {
            int position0, position1;
            var count = position0 = position1 = 0;
            string upperString = original.ToUpper();
            string upperPattern = pattern.ToUpper();
            int inc = (original.Length / pattern.Length) * (replacement.Length - pattern.Length);
            char[] chars = new char[original.Length + Math.Max(0, inc)];
            while ((position1 = upperString.IndexOf(upperPattern, position0, StringComparison.Ordinal)) != -1)
            {
                for (int i = position0; i < position1; ++i)
                    chars[count++] = original[i];
                foreach (var r in replacement)
                    chars[count++] = r;

                position0 = position1 + pattern.Length;
            }
            if (position0 == 0) return original;
            for (int i = position0; i < original.Length; ++i)
                chars[count++] = original[i];
            return new string(chars, 0, count);
        }

        private void tfindReplaceAll_Click(object sender, EventArgs e)
        {
            if (tfindFindText.Text.Length > 0)
            {
                if (tfindMatchCase.Checked)
                {
                    rtb.Text.Replace(tfindFindText.Text, tfindReplaceText.Text);
                }
                else
                {
                    rtb.Text = ReplaceEx(rtb.Text, tfindFindText.Text, tfindReplaceText.Text);
                }
            }
        }

        private void syntaxHighlightingToolStripMenuItem_Click(object sender, EventArgs e)
        {
            rtb.SyntaxHighlightEnabled = !rtb.SyntaxHighlightEnabled;
            syntaxHiglightingToolStripMenuItem.Checked = rtb.SyntaxHighlightEnabled;
        }

        private void ReadCursorPosition()
        {
            instance.ShowNotificationInChat(
                $"Cursor at line {rtb.CursorPosition.Line + 1}, column {rtb.CursorPosition.Column + 1}",
                ChatBufferTextStyle.Invisible);
        }

        private async void tbtbSave_Click(object sender, EventArgs e)
        {
            lblScripStatus.Text = "Saving...";
            txtStatus.Hide();
            txtStatus.Text = string.Empty;

            AssetScriptText n = new AssetScriptText { Source = rtb.Text };
            n.Encode();

            try
            {
                // Use a cancellation token with a reasonable timeout to avoid waiting indefinitely
                using (var cts = new CancellationTokenSource(TimeSpan.FromMinutes(1)))
                {
                    var result = await UpdateScriptAsync(n.AssetData, script.UUID, cbMono.Checked, prim?.ID, cbRunning.Checked, cts.Token).ConfigureAwait(false);

                    // Resume on UI thread to update controls
                    if (!IsHandleCreated && instance.MonoRuntime) return;

                    BeginInvoke(new MethodInvoker(() =>
                    {
                        if (result.UploadSuccess && result.CompileSuccess)
                        {
                            lblScripStatus.Text = "Saved OK";
                        }
                        else
                        {
                            if (!result.CompileSuccess)
                            {
                                lblScripStatus.Text = "Compilation failed";
                                if (result.CompileMessages != null)
                                {
                                    txtStatus.Show();
                                    txtStatus.Text = string.Empty;
                                    for (int i = 0; i < result.CompileMessages.Count; i++)
                                    {
                                        Match m = Regex.Match(result.CompileMessages[i], @"\((?<line>\d+),\s*(?<column>\d+)\s*\)\s*:\s*(?<kind>\w+)\s*:\s*(?<msg>.*)", RegexOptions.IgnoreCase);

                                        if (m.Success)
                                        {
                                            int line = 1 + int.Parse(m.Groups["line"].Value, Utils.EnUsCulture);
                                            int column = 1 + int.Parse(m.Groups["column"].Value, Utils.EnUsCulture);
                                            string kind = m.Groups["kind"].Value;
                                            string msg = m.Groups["msg"].Value;
                                            instance.ShowNotificationInChat($"{kind} on line {line}, column {column}: {msg}", ChatBufferTextStyle.Invisible);
                                            txtStatus.Text += $"{kind} (Ln {line}, Col {column}): {msg}";

                                            if (i == 0)
                                            {
                                                rtb.CursorPosition = new RRichTextBox.CursorLocation(line - 1, column - 1);
                                                ReadCursorPosition();
                                                rtb.Focus();
                                            }
                                        }
                                        else
                                        {
                                            txtStatus.Text += result.CompileMessages[i] + Environment.NewLine;
                                            instance.ShowNotificationInChat(result.CompileMessages[i]);
                                        }
                                    }
                                }
                            }
                            else
                            {
                                lblScripStatus.Text = rtb.Text = "Failed to download.";
                            }
                        }
                    }));
                }
            }
            catch (OperationCanceledException)
            {
                lblScripStatus.Text = "Save cancelled";
            }
            catch (Exception ex)
            {
                lblScripStatus.Text = $"Save failed: {ex.Message}";
            }
        }

        private class ScriptUpdateResult
        {
            public bool UploadSuccess { get; set; }
            public string UploadStatus { get; set; }
            public bool CompileSuccess { get; set; }
            public List<string> CompileMessages { get; set; }
            public UUID ItemID { get; set; }
            public UUID AssetID { get; set; }
        }

        private Task<ScriptUpdateResult> UpdateScriptAsync(byte[] data, UUID itemID, bool mono, UUID? taskID, bool running, CancellationToken cancellationToken = default)
        {
            var tcs = new TaskCompletionSource<ScriptUpdateResult>(TaskCreationOptions.RunContinuationsAsynchronously);

            InventoryManager.ScriptUpdatedCallback cb = (uploadSuccess, uploadStatus, compileSuccess, compileMessages, returnedItemID, assetID) =>
            {
                var res = new ScriptUpdateResult
                {
                    UploadSuccess = uploadSuccess,
                    UploadStatus = uploadStatus,
                    CompileSuccess = compileSuccess,
                    CompileMessages = compileMessages,
                    ItemID = returnedItemID,
                    AssetID = assetID
                };

                tcs.TrySetResult(res);
            };

            // Register cancellation to cancel the task
            if (cancellationToken.CanBeCanceled)
            {
                cancellationToken.Register(() => tcs.TrySetCanceled(), useSynchronizationContext: false);
            }

            // Fire the async request which will invoke our callback when done
            Task.Run(async () =>
            {
                try
                {
                    if (taskID.HasValue)
                    {
                        await client.Inventory.RequestUpdateScriptTaskAsync(data, itemID, taskID.Value, mono, running, cb, cancellationToken).ConfigureAwait(false);
                    }
                    else
                    {
                        await client.Inventory.RequestUpdateScriptAgentInventoryAsync(data, itemID, mono, cb, cancellationToken).ConfigureAwait(false);
                    }
                }
                catch (Exception ex)
                {
                    // Surface exception to awaiting code
                    tcs.TrySetException(ex);
                }
            }, cancellationToken);

            return tcs.Task;
        }
    }
}
