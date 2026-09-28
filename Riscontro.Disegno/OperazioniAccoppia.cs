using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace Riscontro.Disegno
{
    /// <summary>
    /// ACCOPPIARE (voce GA4).
    ///
    /// ── LA VOCE ERA BLOCCATA SU UNA COSA CHE NON C'ERA ───────────────────
    ///
    /// GA4 dichiarava un'incognita: *«quali marche di selezione serva usare, la
    /// riflessione non lo dice»*, e ci appendeva la stima delle ore.
    ///
    /// **Non serve nessuna marca.** Misurato il 17/9/2026 con una sonda scritta
    /// per un'altra domanda: si selezionano le due entita' con
    /// `IEntity.Select4(append: true, null)` e si chiama `AddMate5`. Risposta:
    /// `swAddMateError_NoError`. Le marche servono ad altre famiglie — lo
    /// sformo, il riempimento — non a questa.
    ///
    /// *L'incognita che bloccava una voce da 8-10 ore e' caduta dentro una sonda
    /// che cercava altro. Vale la pena scriverlo: le sonde vanno lette tutte,
    /// non solo per la riga che si era andati a cercare.*
    ///
    /// ── COME SI NOMINANO LE DUE FACCE ────────────────────────────────────
    ///
    /// Lo stato dell'assieme da' agli utenti `C1..Cn` per i componenti, e
    /// **nessun identificativo per le loro facce**. Costruirne uno vorrebbe dire
    /// numerare le facce di ogni componente — centinaia su un assieme vero — e
    /// sarebbero **posizionali**, cioe' da rileggere a ogni modifica.
    ///
    /// Qui si usa invece il vocabolario che il progetto ha gia' per le parti: il
    /// **verso**. «la faccia in alto di C1», «quella in basso di C2». Non copre
    /// tutti i casi — una faccia obliqua non ha verso — ma copre quello che
    /// serve per posare un coperchio su una scatola, e **si dice quando non
    /// basta** invece di indovinare.
    ///
    /// ── LA MISURA, CHE NON E' IL RITORNO ─────────────────────────────────
    ///
    /// `AddMate5` torna un `Mate2` **e** scrive un `ErrorStatus`. Nessuno dei
    /// due e' la misura: si contano gli accoppiamenti dei due componenti prima e
    /// dopo, e si pretende che siano saliti. Un `Mate2` non nullo con i
    /// conteggi invariati e' il difetto che non da' errore, portato negli
    /// assiemi.
    ///
    /// **E `swAddMateError_NoError` VALE 1, non 0** (`ErrorUknown` vale 0): un
    /// `if (errore != 0)` dichiarerebbe fallito un accoppiamento riuscito.
    /// </summary>
    public partial class OperazioniSw
    {
        private Esito Accoppia(
            IModelDoc2 modello, Dictionary<string, object> a, StatoDocumento stato)
        {
            var assieme = modello as IAssemblyDoc;
            if (assieme == null)
                return Fallito(
                    "Questo non e' un assieme, e' un documento di tipo «" +
                    NomeTipoDocumento(TipoDi(modello)) + "». Gli accoppiamenti " +
                    "stanno fra componenti di un assieme.");

            object[] grezzi = null;
            try { grezzi = assieme.GetComponents(false) as object[]; } catch { }
            var componenti = StatoDocumento.ComponentiInOrdine(grezzi);
            if (componenti.Count < 2)
                return Fallito(
                    "Per accoppiare servono due componenti, e qui ce n'" +
                    (componenti.Count == 1 ? "e' uno solo" : "e' nessuno") +
                    ". Inseriscili con «inserisci_componente».");

            string uidA = Str(a, "componente_a", null);
            string uidB = Str(a, "componente_b", null);
            var compA = PerUid(componenti, uidA);
            var compB = PerUid(componenti, uidB);
            if (compA == null) return Fallito(NonTrovato(uidA, "componente_a", componenti));
            if (compB == null) return Fallito(NonTrovato(uidB, "componente_b", componenti));
            if (ReferenceEquals(compA, compB))
                return Fallito(
                    "«" + uidA + "» e «" + uidB + "» sono lo stesso componente. Un " +
                    "pezzo non si accoppia con se stesso: non vincola niente e " +
                    "l'assieme resterebbe libero come prima.");

            // ── CONCENTRICO E TANGENTE NON POSSONO RIUSCIRE QUI (voce Z-C21.2,
            // rilievo M17) ────────────────────────────────────────────────────
            //
            // Le facce si scelgono per VERSO, con IFace2.Normal (FacciaVersoDi), e
            // IFace2~Normal.html dice: «Gets the unit normal vector for any planar
            // face» e, nelle Remarks, «If the face is not a planar face, then this
            // property returns 0,0,0». Quindi le facce che arrivano qui sono
            // sempre PIANE, e fra due facce piane un concentrico o un tangente non
            // esistono: a catalogo c'erano due tipi che non potevano mai riuscire,
            // e fallivano con un errore di SOLIDWORKS che mandava a cercare
            // altrove. Si dice il perche' prima di toccare niente.
            string chiesto = (Str(a, "tipo", "coincidente") ?? "").Trim().ToLowerInvariant();
            if (chiesto == "concentrico" || chiesto == "tangente")
                return Fallito(
                    "«" + chiesto + "» vuole almeno una faccia curva (un cilindro, un foro), e qui " +
                    "le facce si scelgono per verso — alto, basso, davanti… — che vale solo per le " +
                    "facce piane: fra due facce piane un «" + chiesto + "» non esiste. Con le facce " +
                    "piane ci sono coincidente, parallelo, perpendicolare e distanza. Non ho toccato " +
                    "niente.");

            string versoA = Str(a, "faccia_a", null);
            string versoB = Str(a, "faccia_b", null);
            var facciaA = FacciaVersoDi(compA, versoA, out string perche1);
            if (facciaA == null) return Fallito("Su «" + uidA + "»: " + perche1);
            var facciaB = FacciaVersoDi(compB, versoB, out string perche2);
            if (facciaB == null) return Fallito("Su «" + uidB + "»: " + perche2);

            string nomeTipo = Str(a, "tipo", "coincidente");
            int tipo = TipoDiAccoppiamento(nomeTipo);
            if (tipo < 0)
                return Fallito(
                    "Accoppiamento «" + nomeTipo + "» sconosciuto. Ci sono: " +
                    string.Join(", ", TipiConosciuti) + ".");

            double distanza = Num(a, "distanza_mm", double.NaN);
            if (tipo == (int)swMateType_e.swMateDISTANCE && double.IsNaN(distanza))
                return Fallito(
                    "Un accoppiamento «distanza» senza «distanza_mm» non ha nessun " +
                    "valore predefinito ragionevole: sceglierne uno vorrebbe dire " +
                    "decidere al posto tuo di quanto stanno lontane due facce.");
            if (double.IsNaN(distanza)) distanza = 0;

            string allineamento = Str(a, "allineamento", "allineato");
            int align = allineamento.Equals("opposto", StringComparison.OrdinalIgnoreCase)
                ? (int)swMateAlign_e.swMateAlignANTI_ALIGNED
                : 0;   // swAlignNONE = allineato

            // ── SI CONTA PRIMA ──────────────────────────────────────────
            int primaA = AccoppiamentiDi(compA), primaB = AccoppiamentiDi(compB);

            object mate;
            int errore = 0;
            try
            {
                modello.ClearSelection2(true);
                (facciaA as IEntity).Select4(true, null);
                (facciaB as IEntity).Select4(true, null);
                mate = assieme.AddMate5(
                    tipo, align, false,
                    Unita.M(distanza), Unita.M(distanza), Unita.M(distanza),
                    0, 0, 0, 0, 0, false, false, 0, out errore);
            }
            catch (Exception ex)
            {
                return Fallito("Non sono riuscito ad accoppiare: " + ex.Message);
            }
            finally { try { modello.ClearSelection2(true); } catch { } }

            // `NoError` VALE 1. Zero e' `ErrorUknown`.
            if (errore != (int)swAddMateError_e.swAddMateError_NoError)
                return Fallito(
                    "SOLIDWORKS ha rifiutato l'accoppiamento: " + NomeErroreMate(errore) +
                    ". Le due facce sono la «" + versoA + "» di " + uidA + " e la «" +
                    versoB + "» di " + uidB + "; l'accoppiamento chiesto e' «" +
                    nomeTipo + "»" +
                    (align == 0 ? " allineato" : " opposto") + ".");

            try { modello.EditRebuild3(); } catch { }

            // ── E SI CONTA DOPO ─────────────────────────────────────────
            //
            // Le maniglie possono essere state invalidate dalla ricostruzione:
            // si rileggono per nome, come fa `inserisci_componente`.
            var dopoComp = StatoDocumento.ComponentiInOrdine(
                assieme.GetComponents(false) as object[]);
            var riA = PerNome(dopoComp, NomeDi(compA));
            var riB = PerNome(dopoComp, NomeDi(compB));
            int dopoA = AccoppiamentiDi(riA ?? compA), dopoB = AccoppiamentiDi(riB ?? compB);

            if (dopoA <= primaA && dopoB <= primaB)
                return Fallito(
                    "Mi e' stato risposto di si' (" + (mate == null ? "senza oggetto" : "con l'oggetto") +
                    ") ma gli accoppiamenti non sono aumentati: " + uidA + " ne aveva " +
                    primaA + " e ne ha " + dopoA + ", " + uidB + " ne aveva " + primaB +
                    " e ne ha " + dopoB + ". Non l'ho considerato fatto.");

            var misure = new Dictionary<string, object>
            {
                ["tipo"] = nomeTipo,
                ["accoppiamenti_prima"] = new object[] { primaA, primaB },
                ["accoppiamenti_dopo"] = new object[] { dopoA, dopoB },
            };
            if (tipo == (int)swMateType_e.swMateDISTANCE) misure["distanza_mm"] = distanza;

            bool fissoA = EFisso(riA ?? compA), fissoB = EFisso(riB ?? compB);
            misure["appoggiato_a"] = !fissoA && dopoA == 0;
            misure["appoggiato_b"] = !fissoB && dopoB == 0;

            string giudizio =
                "Accoppiati «" + versoA + "» di " + uidA + " e «" + versoB + "» di " +
                uidB + " (" + nomeTipo +
                (tipo == (int)swMateType_e.swMateDISTANCE
                    ? " " + distanza.ToString("0.###", CultureInfo.InvariantCulture) + " mm" : "") +
                "). Adesso " + uidA + " ha " + dopoA +
                (dopoA == 1 ? " accoppiamento" : " accoppiamenti") + " e " + uidB + " ne ha " +
                dopoB + ". **Quanto restino ancora liberi non te lo so dire**: SOLIDWORKS " +
                "non fornisce i gradi di liberta' residui da qui, e un accoppiamento solo " +
                "ne lascia liberi cinque su sei.";
            misure["giudizio"] = giudizio;
            _log("[agente] " + giudizio);

            return RiuscitoSenzaCreare(misure);
        }

        // =================================================================

        // Concentrico e tangente non ci sono piu' (voce Z-C21.2, M17): con le facce
        // scelte per verso non possono riuscire, e il rifiuto sta in Accoppia.
        private static readonly string[] TipiConosciuti =
            { "coincidente", "parallelo", "perpendicolare", "distanza" };

        private static int TipoDiAccoppiamento(string nome)
        {
            if (string.IsNullOrWhiteSpace(nome)) return -1;
            switch (nome.Trim().ToLowerInvariant())
            {
                case "coincidente": return (int)swMateType_e.swMateCOINCIDENT;
                case "parallelo": return (int)swMateType_e.swMatePARALLEL;
                case "perpendicolare": return (int)swMateType_e.swMatePERPENDICULAR;
                case "distanza": return (int)swMateType_e.swMateDISTANCE;
                default: return -1;
            }
        }

        /// <summary>
        /// L'errore di `AddMate5` col nome.
        ///
        /// **`NoError` vale 1 e `ErrorUknown` vale 0**, che e' il contrario di
        /// quello che chiunque si aspetta: qui i nomi tolgono l'ambiguita' anche
        /// a chi legge il messaggio.
        /// </summary>
        private static string NomeErroreMate(int e)
        {
            switch (e)
            {
                case (int)swAddMateError_e.swAddMateError_NoError: return "nessun errore";
                case (int)swAddMateError_e.swAddMateError_IncorrectMateType:
                    return "tipo di accoppiamento non adatto a queste due facce";
                case (int)swAddMateError_e.swAddMateError_IncorrectAlignment:
                    return "allineamento sbagliato: prova con «opposto»";
                case (int)swAddMateError_e.swAddMateError_IncorrectSelections:
                    return "le due facce non vanno bene per questo accoppiamento";
                case (int)swAddMateError_e.swAddMateError_OverDefinedAssembly:
                    return "l'assieme diventerebbe sovradefinito: questo vincolo e' gia' implicato dagli altri";
                case (int)swAddMateError_e.swAddMateError_IncorrectGearRatios:
                    return "rapporti di ingranaggio sbagliati";
                default:
                    return "errore sconosciuto (" + e.ToString(CultureInfo.InvariantCulture) + ")";
            }
        }

        private static IComponent2 PerUid(
            List<KeyValuePair<string, IComponent2>> componenti, string uid)
        {
            if (string.IsNullOrWhiteSpace(uid)) return null;
            var u = uid.Trim();
            if (!u.StartsWith("C", StringComparison.OrdinalIgnoreCase)) return null;
            int n;
            if (!int.TryParse(u.Substring(1), NumberStyles.Integer,
                              CultureInfo.InvariantCulture, out n)) return null;
            if (n < 1 || n > componenti.Count) return null;
            return componenti[n - 1].Value;
        }

        private static IComponent2 PerNome(
            List<KeyValuePair<string, IComponent2>> componenti, string nome)
        {
            if (string.IsNullOrWhiteSpace(nome)) return null;
            foreach (var v in componenti)
                if (string.Equals(v.Key, nome, StringComparison.Ordinal)) return v.Value;
            return null;
        }

        private static string NomeDi(IComponent2 c)
        {
            try { return c == null ? null : c.Name2; } catch { return null; }
        }

        private static bool EFisso(IComponent2 c)
        {
            try { return c != null && c.IsFixed(); } catch { return false; }
        }

        private static string NonTrovato(
            string uid, string campo, List<KeyValuePair<string, IComponent2>> componenti)
        {
            var esistono = string.Join(", ",
                componenti.Select((v, i) => "C" + (i + 1) + " («" + v.Key + "»)"));
            return string.IsNullOrWhiteSpace(uid)
                ? "Non mi hai detto quale componente: «" + campo + "» vuole un " +
                  "identificativo dallo stato. Ci sono: " + esistono + "."
                : "Non trovo il componente «" + uid + "». Ci sono: " + esistono + ".";
        }

        /// <summary>
        /// La faccia di un componente che guarda in un verso, in coordinate
        /// dell'ASSIEME.
        ///
        /// Fra piu' facce con lo stesso verso vince **la piu' grande**: e' lo
        /// stesso criterio che lo stato usa per le parti, e su un coperchio o
        /// una piastra la faccia grande e' quella che si vuole. Se ce ne fossero
        /// due larghe uguale il criterio non distingue — e allora e' meglio
        /// saperlo, perche' *un caso in cui due valori diversi danno lo stesso
        /// risultato non tabella niente*.
        /// </summary>
        private static IFace2 FacciaVersoDi(IComponent2 c, string verso, out string perche)
        {
            perche = null;
            if (string.IsNullOrWhiteSpace(verso))
            {
                perche = "non mi hai detto quale faccia. I versi sono: " +
                         string.Join(", ", VersiConosciuti) + ".";
                return null;
            }
            var voluta = Direzione(verso);
            if (voluta == null)
            {
                perche = "«" + verso + "» non e' un verso che conosco. Ci sono: " +
                         string.Join(", ", VersiConosciuti) + ".";
                return null;
            }

            object[] corpi = null;
            try { corpi = c.GetBodies3((int)swBodyType_e.swSolidBody, out _) as object[]; }
            catch { }
            if (corpi == null || corpi.Length == 0)
            {
                perche = "il componente non ha corpi solidi da cui prendere una faccia.";
                return null;
            }

            IFace2 vince = null;
            double maxArea = -1;
            int quante = 0;
            foreach (var o in corpi)
            {
                var b = o as IBody2;
                if (b == null) continue;
                var f = b.GetFirstFace() as IFace2;
                while (f != null)
                {
                    var n = ApiHelpers.NormalIfPlanar(f);   // S17: null, not 0,0,0, on a curved face
                    if (n != null &&
                        n[0] * voluta[0] + n[1] * voluta[1] + n[2] * voluta[2] > 0.99)
                    {
                        quante++;
                        double area = 0;
                        try { area = f.GetArea(); } catch { }
                        if (area > maxArea) { maxArea = area; vince = f; }
                    }
                    f = f.GetNextFace() as IFace2;
                }
            }

            if (vince == null)
            {
                perche = "non c'e' nessuna faccia piana che guardi verso «" + verso +
                         "». Il componente potrebbe essere ruotato rispetto a come lo " +
                         "immagini: i versi sono quelli dell'ASSIEME, non del pezzo.";
                return null;
            }
            return vince;
        }

        private static readonly string[] VersiConosciuti =
            { "alto", "basso", "destra", "sinistra", "davanti", "dietro" };

        private static double[] Direzione(string verso)
        {
            switch (verso.Trim().ToLowerInvariant())
            {
                case "destra": return new double[] { 1, 0, 0 };
                case "sinistra": return new double[] { -1, 0, 0 };
                case "alto": return new double[] { 0, 1, 0 };
                case "basso": return new double[] { 0, -1, 0 };
                case "davanti": return new double[] { 0, 0, 1 };
                case "dietro": return new double[] { 0, 0, -1 };
                default: return null;
            }
        }
    }
}
