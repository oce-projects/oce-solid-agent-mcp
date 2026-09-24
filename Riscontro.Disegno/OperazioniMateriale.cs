using System;
using System.Collections.Generic;
using SolidWorks.Interop.sldworks;

namespace Riscontro.Disegno
{
    /// <summary>
    /// Il materiale del pezzo.
    ///
    /// IL DIFETTO DA CUI NASCE QUESTO FILE
    /// Il 9 settembre 2026, dopo che il telaio finalmente veniva, il
    /// progettista ha scritto: «inoltre non ha applicato il materiale». Non era
    /// una dimenticanza del modello: nel catalogo NON C'ERA nessuna operazione
    /// che lo applicasse, e nello stato del documento il materiale non compariva
    /// — la cartella dei materiali era anzi esclusa apposta dall'albero. Il
    /// modello non poteva ne' metterlo ne' accorgersi che mancava.
    ///
    /// E NON E' UNA QUESTIONE DI COLORE
    /// Senza materiale non c'e' densita', senza densita' non c'e' massa, senza
    /// massa la distinta di taglio esce con la colonna del materiale vuota e
    /// non serve a comprare l'acciaio. La catena a valle e' gia' tutta
    /// costruita: si rompeva al primo anello, e tutti gli anelli dopo davano
    /// «null» e «0» senza segnalare niente.
    ///
    /// LA RILETTURA NON E' UNO SCRUPOLO
    /// `SetMaterialPropertyName2` restituisce void. Con un archivio o un nome
    /// che non esistono non solleva e non cambia niente: si rilegge quello di
    /// prima. Misurato. Percio' qui si rilegge SEMPRE e si confronta: se il
    /// nome tornato indietro non e' quello chiesto, il passo fallisce.
    /// </summary>
    public partial class OperazioniSw
    {
        /// <summary>
        /// Il materiale che questa officina usa salvo avviso contrario
        /// (voce Z-G3). Sta qui e non nello schema perche' lo schema lo
        /// ANNUNCIA e questo lo APPLICA: due posti per lo stesso valore
        /// divergerebbero, e il catalogo si rigenera da `Operazioni.cs`.
        /// </summary>
        private const string MaterialePredefinito = "S235JR";

        private Esito ApplicaMateriale(IModelDoc2 modello, Dictionary<string, object> a)
        {
            var parte = modello as IPartDoc;
            if (parte == null)
                return Fallito("Il materiale si applica a una parte, e questo non e' un documento di parte.");

            // ── IL PREDEFINITO DI QUESTA OFFICINA (voce Z-G3) ────────────
            //
            // Un'officina di carpenteria lavora S235JR salvo avviso
            // contrario, e chiedere ogni volta un campo di cui la risposta
            // e' sempre la stessa fa scrivere al modello una riga che non
            // e' una scelta.
            //
            // MA IL PREDEFINITO SI DICHIARA, SEMPRE. Se il ponte mettesse
            // S235JR e tacesse, il modello crederebbe di averlo scelto lui,
            // la volta dopo non lo chiederebbe nemmeno, e poi arriverebbe un
            // pezzo che andava in inox senza che nessuno sappia dove sia
            // stata presa la decisione. *«Nessuno ci ha pensato» e «qualcuno
            // ci ha pensato e ha deciso» si leggono uguali, e solo una delle
            // due si puo' migliorare.*
            string chiesto = Str(a, "materiale", "");
            bool assunto = string.IsNullOrWhiteSpace(chiesto);
            if (assunto) chiesto = MaterialePredefinito;
            string archivioChiesto = Str(a, "archivio", "");

            // La grafia esatta la trova la postazione che esegue, non il
            // modello: «S235JR» negli archivi si chiama «1.0037 (S235JR)», e
            // nella stessa libreria ci sono cinque S235 diversi.
            string perche;
            var materiale = MaterialiSolidWorks.Risolvi(
                _sw, chiesto, archivioChiesto, _log, out perche);
            if (materiale == null) return Fallito(perche);

            // Il materiale di prima serve per dirlo nel riassunto: se il pezzo
            // ne aveva gia' uno, sostituirlo in silenzio non e' cortesia.
            string prima = "";
            try { string db; prima = parte.GetMaterialPropertyName2("", out db) ?? ""; }
            catch { }

            try
            {
                parte.SetMaterialPropertyName2("", materiale.Archivio, materiale.Nome);
            }
            catch (Exception ex)
            {
                return Fallito("L'applicazione del materiale non e' riuscita: " + ex.Message);
            }

            string dopo, archivioDopo = "";
            try { dopo = parte.GetMaterialPropertyName2("", out archivioDopo) ?? ""; }
            catch (Exception ex)
            {
                return Fallito(
                    "Ho applicato il materiale ma non riesco a rileggerlo per " +
                    "controllare: " + ex.Message);
            }

            if (!string.Equals(dopo, materiale.Nome, StringComparison.Ordinal))
                return Fallito(
                    "Ho chiesto a SolidWorks il materiale «" + materiale.Nome +
                    "» dall'archivio «" + materiale.Archivio + "», e rileggendo trovo " +
                    (string.IsNullOrEmpty(dopo) ? "il vuoto" : "«" + dopo + "»") +
                    ". Non e' stato applicato.");

            var misure = new Dictionary<string, object>
            {
                ["materiale"] = materiale.Nome,
                ["archivio"] = archivioDopo,
                ["classe"] = materiale.Classe,
                // SI SCRIVE SEMPRE, anche quando il materiale e' stato
                // chiesto: un campo che compare solo nel caso strano e' un
                // campo che chi legge non impara a guardare.
                ["nota_assunto"] = assunto
                    ? "materiale: «" + MaterialePredefinito + "» — PREDEFINITO di " +
                      "questa officina, non l'hai chiesto tu. Se ti serve altro, " +
                      "scrivi «materiale» nel passo."
                    : "materiale: «" + chiesto + "» — l'hai chiesto tu.",
            };
            if (!string.IsNullOrEmpty(prima) && prima != materiale.Nome)
                misure["sostituisce"] = prima;

            // Non c'e' nessuna feature: il materiale e' una proprieta' del
            // documento. Riuscito() regge un riferimento nullo, e i passi
            // successivi non hanno niente da citare qui.
            return Riuscito(null, misure);
        }
    }
}
