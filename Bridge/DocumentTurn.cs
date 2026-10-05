using System;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Threading;

namespace Oce.Solid.Agent.Bridge
{
    /// <summary>
    /// UN PONTE ALLA VOLTA SU UNO STESSO DOCUMENTO (voce W2.9).
    ///
    /// ── IL PRECEDENTE, CHE E' GIA' STATO PAGATO ──────────────────────────
    ///
    /// Il 14 settembre 2026 un blocco 314x274x52 e' stato estruso **dentro il
    /// pezzo di qualcun altro**: due sessioni sullo stesso SOLIDWORKS, e il
    /// documento attivo era cambiato sotto i piedi. Nessun errore, nessun
    /// avviso: il piano e' riuscito, sul pezzo sbagliato.
    ///
    /// Quella volta si e' riparato tenendo il riferimento `IModelDoc2` invece
    /// di richiedere ogni volta l'attivo. **Dentro un processo basta.** Fra due
    /// PROCESSI-ponte — due client MCP aperti insieme, che e' la cosa piu'
    /// normale del mondo — quel riferimento non protegge niente: sono due
    /// oggetti COM diversi che guardano lo stesso file, e ognuno crede di
    /// essere solo.
    ///
    /// ── PERCHE' UN MUTEX E NON UN FILE DI LOCK ───────────────────────────
    ///
    /// Un file di lock sopravvive a chi l'ha creato: se il ponte muore, il file
    /// resta, e il documento diventa inutilizzabile finche' qualcuno non lo
    /// cancella a mano. Un mutex di sistema **lo rilascia Windows** quando il
    /// processo finisce, comunque finisca. Un lucchetto che resta chiuso dopo
    /// che chi l'ha chiuso e' morto non e' una protezione, e' un guasto.
    ///
    /// ── `Local\` E NON `Global\`, E VA DETTO ─────────────────────────────
    ///
    /// Il nome sta nello spazio della sessione Windows corrente. Copre il caso
    /// vero — due client MCP sulla stessa scrivania — e **non** copre due utenti
    /// diversi o un servizio: li' `Global\` servirebbe, ma vuole privilegi che
    /// un ponte lanciato da un client MCP non ha, e un lucchetto che a volte
    /// non si riesce a chiudere e' peggio di uno che si sa dove arriva.
    /// **Il limite si scrive qui invece di scoprirlo.**
    /// </summary>
    internal sealed class DocumentTurn : IDisposable
    {
        private Mutex _mutex;

        private DocumentTurn(Mutex m) { _mutex = m; }

        /// <summary>
        /// Prende il turno sul documento, oppure torna `null` se ce l'ha un
        /// altro. `whose` dice cosa si e' trovato, per il messaggio.
        ///
        /// ATTESA ZERO, DI PROPOSITO. Aspettare vorrebbe dire che il client
        /// resta appeso senza sapere perche', e il ponte ha gia' un problema di
        /// chiamate che non tornano (voce W2.1). Meglio un rifiuto immediato
        /// che dice cosa sta succedendo: chi legge puo' decidere lui se
        /// aspettare.
        /// </summary>
        public static DocumentTurn Take(object doc, out string whose)
        {
            whose = "";
            string key = KeyOf(doc, out string descriptionText);
            whose = descriptionText;
            if (key == null)
            {
                // Non si e' potuto sapere di quale documento si tratta: NON si
                // blocca. Un lucchetto su una chiave sbagliata bloccherebbe
                // documenti diversi fra loro e lascerebbe passare quelli
                // uguali — cioe' farebbe l'opposto di quello che serve.
                return new DocumentTurn(null);
            }

            Mutex m = null;
            try
            {
                bool our;
                m = new Mutex(false, @"Local\OceSolidAgent_" + key, out our);
                if (!m.WaitOne(0))
                {
                    m.Dispose();
                    return null;
                }
                return new DocumentTurn(m);
            }
            catch (AbandonedMutexException)
            {
                // L'ha lasciato un processo morto senza rilasciarlo: e' NOSTRO
                // adesso, e non e' un errore. Windows lo segnala una volta sola
                // proprio per dire «il precedente non ha chiuso la porta».
                return new DocumentTurn(m);
            }
            catch
            {
                // Qualunque altro guaio nel prendere il lucchetto NON deve
                // impedire di lavorare: il ponte funzionava anche prima che
                // questo esistesse. Una difesa nuova che diventa una causa nuova
                // di rifiuto e' un peggioramento.
                try { if (m != null) m.Dispose(); } catch { }
                return new DocumentTurn(null);
            }
        }

        /// <summary>
        /// La chiave del documento: il percorso se c'e', altrimenti il titolo.
        ///
        /// Si passa per uno SHA-1 e non si usa il percorso cosi' com'e': i nomi
        /// di mutex non accettano la barra rovesciata (separa lo spazio dei
        /// nomi) e hanno un limite di lunghezza. Un percorso lungo, troncato,
        /// farebbe collidere due documenti dentro la stessa cartella profonda —
        /// e due documenti diversi che si bloccano a vicenda sono un difetto
        /// che si manifesta solo quando qualcuno lavora davvero.
        /// </summary>
        private static string KeyOf(object doc, out string descriptionText)
        {
            descriptionText = "";
            if (doc == null) return null;
            string filePath = "", docTitle = "";
            try { filePath = Ask(doc, "GetPathName") ?? ""; } catch { }
            try { docTitle = Ask(doc, "GetTitle") ?? ""; } catch { }

            string which = !string.IsNullOrWhiteSpace(filePath)
                ? filePath.ToLowerInvariant()
                : docTitle;
            if (string.IsNullOrWhiteSpace(which)) return null;

            descriptionText = !string.IsNullOrWhiteSpace(filePath) ? filePath : docTitle;

            using (var sha = SHA1.Create())
            {
                var b = sha.ComputeHash(Encoding.UTF8.GetBytes(which));
                var s = new StringBuilder(b.Length * 2);
                foreach (var x in b) s.Append(x.ToString("x2", CultureInfo.InvariantCulture));
                return s.ToString();
            }
        }

        private static string Ask(object obj, string method)
        {
            return obj.GetType().InvokeMember(
                method, System.Reflection.BindingFlags.InvokeMethod,
                null, obj, null) as string;
        }

        public void Dispose()
        {
            if (_mutex == null) return;
            try { _mutex.ReleaseMutex(); } catch { }
            try { _mutex.Dispose(); } catch { }
            _mutex = null;
        }
    }
}
