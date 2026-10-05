using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace Oce.Solid.Agent.Installer
{
    /// <summary>
    /// LA FINESTRA.
    ///
    /// Scritta a mano, senza designer: e' un file solo, si legge tutto, e non
    /// c'e' un .resx che diverge da quello che si vede.
    ///
    /// COSA DEVE FARE, IN ORDINE DI IMPORTANZA:
    /// 1. dire cosa manca PRIMA che qualcuno prema un bottone, e cosa fare;
    /// 2. installare;
    /// 3. collegare Claude — che e' il lavoro vero, e quello che nessuno
    ///    riesce a fare a mano senza sbagliare un percorso in un JSON;
    /// 4. dimostrare che funziona, con il giro secco;
    /// 5. potersi disinstallare, perche' un installatore da cui non si torna
    ///    indietro non si prova volentieri.
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
            Text = Steps.ProductName + " — installazione";
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
                Text = "Collega Claude a SOLIDWORKS",
                Font = new Font("Segoe UI", 15F, FontStyle.Regular),
                Location = new Point(18, y), AutoSize = true,
            });
            y += 34;

            Add(new Label
            {
                Text = "Installa il ponte e scrive la configurazione del client. " +
                       "Non serve l'amministratore: niente registro, niente Program Files.",
                ForeColor = Color.DimGray,
                Location = new Point(18, y), Size = new Size(760, 34),
            });
            y += 40;

            // ── controlli ────────────────────────────────────────────────
            Add(Subtitle("Cosa c'e' su questa macchina", ref y));

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
            // LE OPERAZIONI NON SI CHIEDONO PIU'. Dalla 0.6.0 stanno nel
            // carico: il campo resta per DIRE da dove vengono, in sola
            // lettura. Chiedere un file che il pacchetto gia' contiene e'
            // una domanda senza risposta giusta, e chi non sa cosa
            // rispondere pensa di aver sbagliato qualcosa.
            var nextTo = Steps.FindLibraryNextTo();
            _addin.Text = nextTo ?? "dentro l'installatore";
            _addin.ReadOnly = true;
            _addin.ForeColor = Color.Gray;
            Field(_addin, ref y);

            // THE WELDMENT PROFILES (entry H19, A6): prefilled with the folder
            // SOLIDWORKS uses, or the one it installs, only when one exists;
            // empty otherwise. Written as PROFILI_SALDATURA in the client's env.
            // A folder already in an installed client's entry comes first: it
            // was chosen by someone (H19 review).
            Add(Subtitle("Weldment profiles folder (optional: profili_saldatura needs it)", ref y));
            string source;
            _weldmentProfiles.Text =
                Configuration.ExistingWeldmentProfiles(Configuration.FoundClients().Where(c => c.Installed)) ??
                Steps.FindWeldmentProfiles(Steps.FindRedist(), out source) ?? "";
            FieldWithButton(_weldmentProfiles, "Browse…", ref y, ChooseWeldmentProfiles);

            // ── client ───────────────────────────────────────────────────
            Add(Subtitle("Quali programmi collegare", ref y));
            _client.Location = new Point(18, y);
            _client.Size = new Size(766, 54);
            _client.CheckOnClick = true;
            _client.BorderStyle = BorderStyle.FixedSingle;
            Add(_client);
            y += 62;

            // ── bottoni ──────────────────────────────────────────────────
            _install.Text = "Installa";
            _install.Location = new Point(18, y);
            _install.Size = new Size(150, 34);
            _install.BackColor = Color.FromArgb(0, 120, 212);
            _install.ForeColor = Color.White;
            _install.FlatStyle = FlatStyle.Flat;
            _install.Click += (s, e) => Install();
            Add(_install);

            _recheck.Text = "Ricontrolla";
            _recheck.Location = new Point(178, y);
            _recheck.Size = new Size(110, 34);
            _recheck.Click += (s, e) => Recheck();
            Add(_recheck);

            _uninstall.Text = "Disinstalla";
            _uninstall.Location = new Point(674, y);
            _uninstall.Size = new Size(110, 34);
            _uninstall.Click += (s, e) => Uninstall();
            Add(_uninstall);
            y += 42;

            // ── registro ─────────────────────────────────────────────────
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

        /// <summary>Un campo largo tutta la riga, senza bottone accanto.</summary>
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
                    (c.Installed ? "" : "  (non risulta installato)") +
                    (already != null ? "  — gia' collegato" : "");
                _client.Items.Add(label, c.Installed);
            }

            _install.Enabled = !locked;
            _outcome.Text = locked
                ? "Ci sono cose da sistemare prima di installare (righe in rosso)."
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

                Write("— installazione —");
                var executable = Steps.Install(_folder.Text, Write);

                // I CLIENT: e' il lavoro vero. Copiare due file lo sa fare
                // chiunque; scrivere la voce giusta in un JSON nascosto senza
                // cancellare gli altri server no.
                Write("");
                Write("— collegamento —");
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
                            Write("  ATTENZIONE: " + client.Name + " e' aperto adesso. " +
                                   "Chiudilo e riaprilo, se no non legge la configurazione nuova.");
                    }
                    catch (Exception ex) { Write("  " + client.Name + ": " + ex.Message); notWired = true; }
                }
                if (!someone || notWired)
                {
                    Write("  " + Program.PasteHeader);
                    Write(Configuration.BlockToPaste(executable, _redist, weldmentProfiles));
                }

                // IL GIRO SECCO: e' l'unica cosa che permette di dire
                // «installato» sapendo che e' vero.
                Write("");
                Write("— prova —");
                int code;
                foreach (var line in Steps.Test(executable, _redist, out code)
                             .Split('\n')) Write("  " + line.TrimEnd());

                // The exit code of the self-test decides (entry H19, B3). This
                // window used to say "SOLIDWORKS is not open" for every
                // failure, including SOLIDWORKS not installed.
                if (code == Steps.SelfTestAttached && !notWired)
                {
                    _outcome.Text = "Fatto: il ponte gira e vede il documento aperto.";
                    _outcome.ForeColor = Color.FromArgb(0, 120, 40);
                }
                else if (Steps.SelfTestAccepted(code) && !notWired)
                {
                    _outcome.Text = "Installato. SOLIDWORKS non e' aperto, quindi la " +
                                  "verifica completa non si e' potuta fare.";
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
                Write("FERMATO: " + ex.Message);
                _outcome.Text = "Non installato: " + ex.Message;
                _outcome.ForeColor = Color.FromArgb(190, 30, 30);
            }
            finally { _install.Enabled = true; }
        }

        private void Uninstall()
        {
            var response = MessageBox.Show(
                "Tolgo i file del ponte da:\n\n" + _folder.Text + "\n\n" +
                "e la voce dalla configurazione dei programmi collegati.\n\n" +
                "I tuoi pezzi e le tue tavole non vengono toccati.",
                "Disinstallare?", MessageBoxButtons.OKCancel, MessageBoxIcon.Question);
            if (response != DialogResult.OK) return;

            _registry.Clear();
            Write("— disinstallazione —");
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
                _outcome.Text = "Disinstallato.";
                _outcome.ForeColor = Color.DimGray;
            }
            Recheck();
        }
    }
}
