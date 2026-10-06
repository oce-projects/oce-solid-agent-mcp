using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace Oce.Solid.Agent.Installer
{
    /// <summary>
    /// THE WINDOW.
    ///
    /// Written by hand, without a designer: it is a single file, it can be read in full, and
    /// there is no .resx that diverges from what is seen.
    ///
    /// WHAT IT MUST DO, IN ORDER OF IMPORTANCE:
    /// 1. say what is missing BEFORE anyone presses a button, and what to do;
    /// 2. install;
    /// 3. connect Claude — which is the real job, and the one that nobody
    ///    manages to do by hand without getting a path wrong in a JSON;
    /// 4. prove that it works, with the dry run;
    /// 5. be able to uninstall itself, because an installer from which there is no way back
    ///    is not tried willingly.
    /// </summary>
    internal sealed class Window : Form
    {
        private readonly ListView _checks = new ListView();
        private readonly TextBox _folder = new TextBox();
        private readonly TextBox _addin = new TextBox();
        private readonly TextBox _weldmentProfiles = new TextBox();
        private readonly CheckedListBox _client = new CheckedListBox();
        private readonly TextBox _registry = new TextBox();
        private readonly Button _install = new Button();
        private readonly Button _uninstall = new Button();
        private readonly Button _recheck = new Button();
        private readonly Label _outcome = new Label();

        private string _redist;
        private List<Client> _foundClients = new List<Client>();

        public Window()
        {
            Text = Steps.ProductName + " — install";
            Size = new Size(820, 754);
            MinimumSize = new Size(720, 654);
            StartPosition = FormStartPosition.CenterScreen;
            Font = new Font("Segoe UI", 9F);
            BackColor = Color.White;

            Compose();
            Recheck();
        }

        // ─────────────────────────────────────────────────────────────────

        private void Compose()
        {
            int y = 14;

            Add(new Label
            {
                Text = "Connect Claude to SOLIDWORKS",
                Font = new Font("Segoe UI", 15F, FontStyle.Regular),
                Location = new Point(18, y), AutoSize = true,
            });
            y += 34;

            Add(new Label
            {
                Text = "Installs the bridge and writes the client configuration. " +
                       "No administrator needed: no registry, no Program Files.",
                ForeColor = Color.DimGray,
                Location = new Point(18, y), Size = new Size(760, 34),
            });
            y += 40;

            // ── controls ────────────────────────────────────────────────
            Add(Subtitle("What is on this machine", ref y));

            _checks.Location = new Point(18, y);
            _checks.Size = new Size(766, 132);
            _checks.View = View.Details;
            _checks.FullRowSelect = true;
            _checks.HeaderStyle = ColumnHeaderStyle.Nonclickable;
            _checks.Columns.Add("", 26);
            _checks.Columns.Add("", 190);
            _checks.Columns.Add("", 540);
            _checks.OwnerDraw = false;
            Add(_checks);
            y += 140;

            // ── dove ─────────────────────────────────────────────────────
            Add(Subtitle("Dove installare", ref y));
            _folder.Text = Steps.DefaultFolder;
            FieldWithButton(_folder, "Sfoglia…", ref y, ChooseFolder);

            Add(Subtitle("Add-in OpenPDM (facoltativo)", ref y));
            // THE OPERATIONS ARE NOT ASKED FOR ANY MORE. Since 0.6.0 they are in the
            // payload: the field stays to SAY where they come from, read
            // only. Asking for a file that the package already contains is
            // a question with no right answer, and whoever does not know what to
            // answer thinks they got something wrong.
            var nextTo = Steps.FindLibraryNextTo();
            _addin.Text = nextTo ?? "inside the installer";
            _addin.ReadOnly = true;
            _addin.ForeColor = Color.Gray;
            Field(_addin, ref y);

            // THE WELDMENT PROFILES (entry H19, A6): prefilled with the folder
            // SOLIDWORKS uses, or the one it installs, only when one exists;
            // empty otherwise. Written as PROFILI_SALDATURA in the client's env.
            // A folder already in an installed client's entry comes first: it
            // was chosen by someone (H19 review).
            Add(Subtitle("Weldment profiles folder (optional: weldment_profiles needs it)", ref y));
            string source;
            _weldmentProfiles.Text =
                Configuration.ExistingWeldmentProfiles(Configuration.FoundClients().Where(c => c.Installed)) ??
                Steps.FindWeldmentProfiles(Steps.FindRedist(), out source) ?? "";
            FieldWithButton(_weldmentProfiles, "Browse…", ref y, ChooseWeldmentProfiles);

            // ── client ───────────────────────────────────────────────────
            Add(Subtitle("Which programs to connect", ref y));
            _client.Location = new Point(18, y);
            _client.Size = new Size(766, 54);
            _client.CheckOnClick = true;
            _client.BorderStyle = BorderStyle.FixedSingle;
            Add(_client);
            y += 62;

            // ── buttons ───────────────────────────────────────────────────
            _install.Text = "Install";
            _install.Location = new Point(18, y);
            _install.Size = new Size(150, 34);
            _install.BackColor = Color.FromArgb(0, 120, 212);
            _install.ForeColor = Color.White;
            _install.FlatStyle = FlatStyle.Flat;
            _install.Click += (s, e) => Install();
            Add(_install);

            _recheck.Text = "Check again";
            _recheck.Location = new Point(178, y);
            _recheck.Size = new Size(110, 34);
            _recheck.Click += (s, e) => Recheck();
            Add(_recheck);

            _uninstall.Text = "Uninstall";
            _uninstall.Location = new Point(674, y);
            _uninstall.Size = new Size(110, 34);
            _uninstall.Click += (s, e) => Uninstall();
            Add(_uninstall);
            y += 42;

            // ── log ─────────────────────────────────────────────────────
            _registry.Location = new Point(18, y);
            _registry.Size = new Size(766, 150);
            _registry.Multiline = true;
            _registry.ReadOnly = true;
            _registry.ScrollBars = ScrollBars.Vertical;
            _registry.BackColor = Color.FromArgb(250, 250, 250);
            _registry.Font = new Font("Consolas", 8.5F);
            _registry.Anchor = AnchorStyles.Top | AnchorStyles.Left |
                               AnchorStyles.Right | AnchorStyles.Bottom;
            Add(_registry);
            y += 158;

            _outcome.Location = new Point(18, y);
            _outcome.Size = new Size(766, 22);
            _outcome.Anchor = AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            Add(_outcome);
        }

        private Label Subtitle(string text, ref int y)
        {
            var l = new Label
            {
                Text = text,
                Font = new Font("Segoe UI", 9F, FontStyle.Bold),
                Location = new Point(18, y),
                AutoSize = true,
            };
            y += 20;
            return l;
        }

        private void FieldWithButton(TextBox theField, string text, ref int y, Action action)
        {
            theField.Location = new Point(18, y);
            theField.Size = new Size(660, 24);
            theField.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            Add(theField);

            var b = new Button
            {
                Text = text,
                Location = new Point(686, y - 1),
                Size = new Size(98, 26),
                Anchor = AnchorStyles.Top | AnchorStyles.Right,
            };
            b.Click += (s, e) => action();
            Add(b);
            y += 34;
        }

        /// <summary>A field as wide as the whole row, with no button next to it.</summary>
        private void Field(TextBox theField, ref int y)
        {
            theField.Location = new Point(18, y);
            theField.Size = new Size(766, 24);
            theField.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            Add(theField);
            y += 34;
        }

        private void Add(Control c) => Controls.Add(c);

        // ─────────────────────────────────────────────────────────────────

        private void Write(string line)
        {
            _registry.AppendText(line + Environment.NewLine);
            _registry.SelectionStart = _registry.TextLength;
            _registry.ScrollToCaret();
            Application.DoEvents();
        }

        private void Recheck()
        {
            _checks.Items.Clear();
            _redist = Steps.FindRedist();

            var outcomes = Steps.Check(_redist);
            bool locked = false;

            foreach (var c in outcomes)
            {
                var line = new ListViewItem(
                    c.Outcome == Outcome.Fine ? "✓" : c.Outcome == Outcome.Warning ? "!" : "✕");
                line.SubItems.Add(c.Title);
                line.SubItems.Add(
                    c.Detail + (string.IsNullOrWhiteSpace(c.WhatToDo) ? "" : "  — " + c.WhatToDo));
                line.ForeColor =
                    c.Outcome == Outcome.Fine ? Color.FromArgb(0, 120, 40) :
                    c.Outcome == Outcome.Warning ? Color.FromArgb(160, 110, 0) :
                    Color.FromArgb(190, 30, 30);
                _checks.Items.Add(line);
                if (c.Outcome == Outcome.Male) locked = true;
            }

            _client.Items.Clear();
            _foundClients = Configuration.FoundClients();
            foreach (var c in _foundClients)
            {
                var already = Configuration.HowItIsConfigured(c);
                var label = c.Name +
                    (c.Installed ? "" : "  (not installed)") +
                    (already != null ? "  — already connected" : "");
                _client.Items.Add(label, c.Installed);
            }

            _install.Enabled = !locked;
            _outcome.Text = locked
                ? "There are things to fix before installing (rows in red)."
                : "Pronto.";
            _outcome.ForeColor = locked ? Color.FromArgb(190, 30, 30) : Color.DimGray;
        }

        private void ChooseFolder()
        {
            using (var d = new FolderBrowserDialog { SelectedPath = _folder.Text })
                if (d.ShowDialog(this) == DialogResult.OK) _folder.Text = d.SelectedPath;
        }

        private void ChooseWeldmentProfiles()
        {
            using (var d = new FolderBrowserDialog { SelectedPath = _weldmentProfiles.Text })
                if (d.ShowDialog(this) == DialogResult.OK) _weldmentProfiles.Text = d.SelectedPath;
        }

        // ─────────────────────────────────────────────────────────────────

        private void Install()
        {
            _install.Enabled = false;
            _registry.Clear();
            try
            {
                var weldmentProfiles = _weldmentProfiles.Text.Trim();
                if (weldmentProfiles.Length > 0 && !Directory.Exists(weldmentProfiles))
                    throw new Exception("the weldment profiles folder does not exist: " + weldmentProfiles);
                if (weldmentProfiles.Length == 0) weldmentProfiles = null;

                Write("— install —");
                var executable = Steps.Install(_folder.Text, Write);

                // THE CLIENTS: this is the real job. Anyone can copy two files;
                // writing the right entry in a hidden JSON without
                // deleting the other servers, not so.
                Write("");
                Write("— connection —");
                bool someone = false, notWired = false;
                for (int i = 0; i < _foundClients.Count; i++)
                {
                    if (!_client.GetItemChecked(i)) continue;
                    var client = _foundClients[i];
                    try
                    {
                        Program.Wire(client, executable, _redist, weldmentProfiles, null, Write);
                        someone = true;
                        if (Configuration.IsOpen(client))
                            Write("  WARNING: " + client.Name + " is open right now. " +
                                   "Close it and reopen it, or it will not read the new configuration.");
                    }
                    catch (Exception ex) { Write("  " + client.Name + ": " + ex.Message); notWired = true; }
                }
                if (!someone || notWired)
                {
                    Write("  " + Program.PasteHeader);
                    Write(Configuration.BlockToPaste(executable, _redist, weldmentProfiles));
                }

                // THE DRY RUN: it is the only thing that lets one say
                // «installed» knowing that it is true.
                Write("");
                Write("— test —");
                int code;
                foreach (var line in Steps.Test(executable, _redist, out code)
                             .Split('\n')) Write("  " + line.TrimEnd());

                // The exit code of the self-test decides (entry H19, B3). This
                // window used to say "SOLIDWORKS is not open" for every
                // failure, including SOLIDWORKS not installed.
                if (code == Steps.SelfTestAttached && !notWired)
                {
                    _outcome.Text = "Done: the bridge runs and is attached to SOLIDWORKS.";
                    _outcome.ForeColor = Color.FromArgb(0, 120, 40);
                }
                else if (Steps.SelfTestAccepted(code) && !notWired)
                {
                    _outcome.Text = "Installed. SOLIDWORKS is not open, so the " +
                                  "full check could not be done.";
                    _outcome.ForeColor = Color.FromArgb(160, 110, 0);
                }
                else
                {
                    _outcome.Text = notWired
                        ? "Installed, but a client is not wired: paste the block in the log."
                        : "Installed, but the self-test did not confirm: " + Steps.SelfTestMeaning(code) + ".";
                    _outcome.ForeColor = Color.FromArgb(190, 30, 30);
                }
            }
            catch (Exception ex)
            {
                Write("");
                Write("STOPPED: " + ex.Message);
                _outcome.Text = "Not installed: " + ex.Message;
                _outcome.ForeColor = Color.FromArgb(190, 30, 30);
            }
            finally { _install.Enabled = true; }
        }

        private void Uninstall()
        {
            var response = MessageBox.Show(
                "I am removing the bridge files from:\n\n" + _folder.Text + "\n\n" +
                "and the entry from the configuration of the connected programs.\n\n" +
                "Your parts and your drawings are not touched.",
                "Disinstallare?", MessageBoxButtons.OKCancel, MessageBoxIcon.Question);
            if (response != DialogResult.OK) return;

            _registry.Clear();
            Write("— uninstall —");
            foreach (var client in _foundClients)
            {
                try { Program.Unwire(client, _folder.Text, null, Write); }
                catch (Exception ex) { Write("  " + client.Name + ": " + ex.Message); }
            }
            var left = Steps.Uninstall(_folder.Text, Write);
            if (left.Count > 0)
            {
                _outcome.Text = "Not every file could be removed: " + string.Join(", ", left) +
                              ". Close the MCP client and try again.";
                _outcome.ForeColor = Color.FromArgb(190, 30, 30);
            }
            else
            {
                _outcome.Text = "Uninstalled.";
                _outcome.ForeColor = Color.DimGray;
            }
            Recheck();
        }
    }
}
