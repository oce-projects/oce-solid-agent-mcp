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
    internal sealed class Finestra : Form
    {
        private readonly ListView _controlli = new ListView();
        private readonly TextBox _cartella = new TextBox();
        private readonly TextBox _addin = new TextBox();
        private readonly TextBox _weldmentProfiles = new TextBox();
        private readonly CheckedListBox _client = new CheckedListBox();
        private readonly TextBox _registro = new TextBox();
        private readonly Button _installa = new Button();
        private readonly Button _disinstalla = new Button();
        private readonly Button _ricontrolla = new Button();
        private readonly Label _esito = new Label();

        private string _redist;
        private List<Client> _clientTrovati = new List<Client>();

        public Finestra()
        {
            Text = Passi.NomeProdotto + " — installazione";
            Size = new Size(820, 754);
            MinimumSize = new Size(720, 654);
            StartPosition = FormStartPosition.CenterScreen;
            Font = new Font("Segoe UI", 9F);
            BackColor = Color.White;

            Componi();
            Ricontrolla();
        }

        // ─────────────────────────────────────────────────────────────────

        private void Componi()
        {
            int y = 14;

            Aggiungi(new Label
            {
                Text = "Collega Claude a SOLIDWORKS",
                Font = new Font("Segoe UI", 15F, FontStyle.Regular),
                Location = new Point(18, y), AutoSize = true,
            });
            y += 34;

            Aggiungi(new Label
            {
                Text = "Installa il ponte e scrive la configurazione del client. " +
                       "Non serve l'amministratore: niente registro, niente Program Files.",
                ForeColor = Color.DimGray,
                Location = new Point(18, y), Size = new Size(760, 34),
            });
            y += 40;

            // ── controlli ────────────────────────────────────────────────
            Aggiungi(Titoletto("Cosa c'e' su questa macchina", ref y));

            _controlli.Location = new Point(18, y);
            _controlli.Size = new Size(766, 132);
            _controlli.View = View.Details;
            _controlli.FullRowSelect = true;
            _controlli.HeaderStyle = ColumnHeaderStyle.Nonclickable;
            _controlli.Columns.Add("", 26);
            _controlli.Columns.Add("", 190);
            _controlli.Columns.Add("", 540);
            _controlli.OwnerDraw = false;
            Aggiungi(_controlli);
            y += 140;

            // ── dove ─────────────────────────────────────────────────────
            Aggiungi(Titoletto("Dove installare", ref y));
            _cartella.Text = Passi.CartellaPredefinita;
            CampoConBottone(_cartella, "Sfoglia…", ref y, ScegliCartella);

            Aggiungi(Titoletto("Add-in OpenPDM (facoltativo)", ref y));
            // LE OPERAZIONI NON SI CHIEDONO PIU'. Dalla 0.6.0 stanno nel
            // carico: il campo resta per DIRE da dove vengono, in sola
            // lettura. Chiedere un file che il pacchetto gia' contiene e'
            // una domanda senza risposta giusta, e chi non sa cosa
            // rispondere pensa di aver sbagliato qualcosa.
            var accanto = Passi.TrovaLibreriaAccanto();
            _addin.Text = accanto ?? "dentro l'installatore";
            _addin.ReadOnly = true;
            _addin.ForeColor = Color.Gray;
            Campo(_addin, ref y);

            // THE WELDMENT PROFILES (entry H19, A6): prefilled with the folder
            // SOLIDWORKS uses, or the one it installs, only when one exists;
            // empty otherwise. Written as PROFILI_SALDATURA in the client's env.
            // A folder already in an installed client's entry comes first: it
            // was chosen by someone (H19 review).
            Aggiungi(Titoletto("Weldment profiles folder (optional: profili_saldatura needs it)", ref y));
            string source;
            _weldmentProfiles.Text =
                Configurazione.ExistingWeldmentProfiles(Configurazione.ClientTrovati().Where(c => c.Installato)) ??
                Passi.FindWeldmentProfiles(Passi.TrovaRedist(), out source) ?? "";
            CampoConBottone(_weldmentProfiles, "Browse…", ref y, ChooseWeldmentProfiles);

            // ── client ───────────────────────────────────────────────────
            Aggiungi(Titoletto("Quali programmi collegare", ref y));
            _client.Location = new Point(18, y);
            _client.Size = new Size(766, 54);
            _client.CheckOnClick = true;
            _client.BorderStyle = BorderStyle.FixedSingle;
            Aggiungi(_client);
            y += 62;

            // ── bottoni ──────────────────────────────────────────────────
            _installa.Text = "Installa";
            _installa.Location = new Point(18, y);
            _installa.Size = new Size(150, 34);
            _installa.BackColor = Color.FromArgb(0, 120, 212);
            _installa.ForeColor = Color.White;
            _installa.FlatStyle = FlatStyle.Flat;
            _installa.Click += (s, e) => Installa();
            Aggiungi(_installa);

            _ricontrolla.Text = "Ricontrolla";
            _ricontrolla.Location = new Point(178, y);
            _ricontrolla.Size = new Size(110, 34);
            _ricontrolla.Click += (s, e) => Ricontrolla();
            Aggiungi(_ricontrolla);

            _disinstalla.Text = "Disinstalla";
            _disinstalla.Location = new Point(674, y);
            _disinstalla.Size = new Size(110, 34);
            _disinstalla.Click += (s, e) => Disinstalla();
            Aggiungi(_disinstalla);
            y += 42;

            // ── registro ─────────────────────────────────────────────────
            _registro.Location = new Point(18, y);
            _registro.Size = new Size(766, 150);
            _registro.Multiline = true;
            _registro.ReadOnly = true;
            _registro.ScrollBars = ScrollBars.Vertical;
            _registro.BackColor = Color.FromArgb(250, 250, 250);
            _registro.Font = new Font("Consolas", 8.5F);
            _registro.Anchor = AnchorStyles.Top | AnchorStyles.Left |
                               AnchorStyles.Right | AnchorStyles.Bottom;
            Aggiungi(_registro);
            y += 158;

            _esito.Location = new Point(18, y);
            _esito.Size = new Size(766, 22);
            _esito.Anchor = AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            Aggiungi(_esito);
        }

        private Label Titoletto(string testo, ref int y)
        {
            var l = new Label
            {
                Text = testo,
                Font = new Font("Segoe UI", 9F, FontStyle.Bold),
                Location = new Point(18, y),
                AutoSize = true,
            };
            y += 20;
            return l;
        }

        private void CampoConBottone(TextBox campo, string testo, ref int y, Action azione)
        {
            campo.Location = new Point(18, y);
            campo.Size = new Size(660, 24);
            campo.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            Aggiungi(campo);

            var b = new Button
            {
                Text = testo,
                Location = new Point(686, y - 1),
                Size = new Size(98, 26),
                Anchor = AnchorStyles.Top | AnchorStyles.Right,
            };
            b.Click += (s, e) => azione();
            Aggiungi(b);
            y += 34;
        }

        /// <summary>Un campo largo tutta la riga, senza bottone accanto.</summary>
        private void Campo(TextBox campo, ref int y)
        {
            campo.Location = new Point(18, y);
            campo.Size = new Size(766, 24);
            campo.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            Aggiungi(campo);
            y += 34;
        }

        private void Aggiungi(Control c) => Controls.Add(c);

        // ─────────────────────────────────────────────────────────────────

        private void Scrivi(string riga)
        {
            _registro.AppendText(riga + Environment.NewLine);
            _registro.SelectionStart = _registro.TextLength;
            _registro.ScrollToCaret();
            Application.DoEvents();
        }

        private void Ricontrolla()
        {
            _controlli.Items.Clear();
            _redist = Passi.TrovaRedist();

            var esiti = Passi.Controlla(_redist);
            bool bloccato = false;

            foreach (var c in esiti)
            {
                var riga = new ListViewItem(
                    c.Esito == Esito.Bene ? "✓" : c.Esito == Esito.Avviso ? "!" : "✕");
                riga.SubItems.Add(c.Titolo);
                riga.SubItems.Add(
                    c.Dettaglio + (string.IsNullOrWhiteSpace(c.CosaFare) ? "" : "  — " + c.CosaFare));
                riga.ForeColor =
                    c.Esito == Esito.Bene ? Color.FromArgb(0, 120, 40) :
                    c.Esito == Esito.Avviso ? Color.FromArgb(160, 110, 0) :
                    Color.FromArgb(190, 30, 30);
                _controlli.Items.Add(riga);
                if (c.Esito == Esito.Male) bloccato = true;
            }

            _client.Items.Clear();
            _clientTrovati = Configurazione.ClientTrovati();
            foreach (var c in _clientTrovati)
            {
                var gia = Configurazione.ComeEConfigurato(c);
                var etichetta = c.Nome +
                    (c.Installato ? "" : "  (non risulta installato)") +
                    (gia != null ? "  — gia' collegato" : "");
                _client.Items.Add(etichetta, c.Installato);
            }

            _installa.Enabled = !bloccato;
            _esito.Text = bloccato
                ? "Ci sono cose da sistemare prima di installare (righe in rosso)."
                : "Pronto.";
            _esito.ForeColor = bloccato ? Color.FromArgb(190, 30, 30) : Color.DimGray;
        }

        private void ScegliCartella()
        {
            using (var d = new FolderBrowserDialog { SelectedPath = _cartella.Text })
                if (d.ShowDialog(this) == DialogResult.OK) _cartella.Text = d.SelectedPath;
        }

        private void ChooseWeldmentProfiles()
        {
            using (var d = new FolderBrowserDialog { SelectedPath = _weldmentProfiles.Text })
                if (d.ShowDialog(this) == DialogResult.OK) _weldmentProfiles.Text = d.SelectedPath;
        }

        // ─────────────────────────────────────────────────────────────────

        private void Installa()
        {
            _installa.Enabled = false;
            _registro.Clear();
            try
            {
                var weldmentProfiles = _weldmentProfiles.Text.Trim();
                if (weldmentProfiles.Length > 0 && !Directory.Exists(weldmentProfiles))
                    throw new Exception("the weldment profiles folder does not exist: " + weldmentProfiles);
                if (weldmentProfiles.Length == 0) weldmentProfiles = null;

                Scrivi("— installazione —");
                var eseguibile = Passi.Installa(_cartella.Text, Scrivi);

                // I CLIENT: e' il lavoro vero. Copiare due file lo sa fare
                // chiunque; scrivere la voce giusta in un JSON nascosto senza
                // cancellare gli altri server no.
                Scrivi("");
                Scrivi("— collegamento —");
                bool qualcuno = false, notWired = false;
                for (int i = 0; i < _clientTrovati.Count; i++)
                {
                    if (!_client.GetItemChecked(i)) continue;
                    var client = _clientTrovati[i];
                    try
                    {
                        Programma.Wire(client, eseguibile, _redist, weldmentProfiles, null, Scrivi);
                        qualcuno = true;
                        if (Configurazione.EAperto(client))
                            Scrivi("  ATTENZIONE: " + client.Nome + " e' aperto adesso. " +
                                   "Chiudilo e riaprilo, se no non legge la configurazione nuova.");
                    }
                    catch (Exception ex) { Scrivi("  " + client.Nome + ": " + ex.Message); notWired = true; }
                }
                if (!qualcuno || notWired)
                {
                    Scrivi("  " + Programma.PasteHeader);
                    Scrivi(Configurazione.BloccoDaIncollare(eseguibile, _redist, weldmentProfiles));
                }

                // IL GIRO SECCO: e' l'unica cosa che permette di dire
                // «installato» sapendo che e' vero.
                Scrivi("");
                Scrivi("— prova —");
                int code;
                foreach (var riga in Passi.Prova(eseguibile, _redist, out code)
                             .Split('\n')) Scrivi("  " + riga.TrimEnd());

                // The exit code of the self-test decides (entry H19, B3). This
                // window used to say "SOLIDWORKS is not open" for every
                // failure, including SOLIDWORKS not installed.
                if (code == Passi.SelfTestAttached && !notWired)
                {
                    _esito.Text = "Fatto: il ponte gira e vede il documento aperto.";
                    _esito.ForeColor = Color.FromArgb(0, 120, 40);
                }
                else if (Passi.SelfTestAccepted(code) && !notWired)
                {
                    _esito.Text = "Installato. SOLIDWORKS non e' aperto, quindi la " +
                                  "verifica completa non si e' potuta fare.";
                    _esito.ForeColor = Color.FromArgb(160, 110, 0);
                }
                else
                {
                    _esito.Text = notWired
                        ? "Installed, but a client is not wired: paste the block in the log."
                        : "Installed, but the self-test did not confirm: " + Passi.SelfTestMeaning(code) + ".";
                    _esito.ForeColor = Color.FromArgb(190, 30, 30);
                }
            }
            catch (Exception ex)
            {
                Scrivi("");
                Scrivi("FERMATO: " + ex.Message);
                _esito.Text = "Non installato: " + ex.Message;
                _esito.ForeColor = Color.FromArgb(190, 30, 30);
            }
            finally { _installa.Enabled = true; }
        }

        private void Disinstalla()
        {
            var risposta = MessageBox.Show(
                "Tolgo i file del ponte da:\n\n" + _cartella.Text + "\n\n" +
                "e la voce dalla configurazione dei programmi collegati.\n\n" +
                "I tuoi pezzi e le tue tavole non vengono toccati.",
                "Disinstallare?", MessageBoxButtons.OKCancel, MessageBoxIcon.Question);
            if (risposta != DialogResult.OK) return;

            _registro.Clear();
            Scrivi("— disinstallazione —");
            foreach (var client in _clientTrovati)
            {
                try { Programma.Unwire(client, _cartella.Text, null, Scrivi); }
                catch (Exception ex) { Scrivi("  " + client.Nome + ": " + ex.Message); }
            }
            var left = Passi.Disinstalla(_cartella.Text, Scrivi);
            if (left.Count > 0)
            {
                _esito.Text = "Not every file could be removed: " + string.Join(", ", left) +
                              ". Close the MCP client and try again.";
                _esito.ForeColor = Color.FromArgb(190, 30, 30);
            }
            else
            {
                _esito.Text = "Disinstallato.";
                _esito.ForeColor = Color.DimGray;
            }
            Ricontrolla();
        }
    }
}
