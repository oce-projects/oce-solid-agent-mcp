using System;
using System.Collections.Generic;
using System.Globalization;

namespace Riscontro.Disegno
{
    /// <summary>
    /// LE SEZIONI NORMALIZZATE: da due o tre quote ai contorni da estrudere.
    ///
    /// E' la ricetta di `estrudi_sezione`. Viveva solo nel server Python
    /// (`profilo_sezione`), quindi dal ponte l'operazione era **dichiarata nel
    /// catalogo e non eseguibile**: il modello poteva chiederla e si prendeva
    /// «operazione sconosciuta».
    ///
    /// OGNI SEZIONE DICHIARA DOVE CADE L'ORIGINE, e non e' un dettaglio: e' la
    /// quota da cui partiranno tutte le lavorazioni successive. Un angolare con
    /// l'origine al centro invece che sullo spigolo interno sposta ogni foro.
    ///
    /// I CONTROLLI DI QUESTA FAMIGLIA SONO FISICI, non di forma, e ognuno
    /// esiste per un difetto che SOLIDWORKS non segnala.
    /// </summary>
    public static class Sezioni
    {
        /// <summary>
        /// Segmenti per ogni semicerchio dell'asola.
        ///
        /// **Non e' un parametro**, ed e' voluto: se lo fosse, si potrebbe
        /// abbassare e ottenere un'asola ottagonale senza che nessuno se ne
        /// accorga.
        /// </summary>
        public const int SegmentiSemicerchio = 24;

        /// <summary>Le sezioni che si sanno fare, e quali quote vogliono.</summary>
        public static readonly Dictionary<string, string[]> Volute =
            new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
            {
                ["tubolare"] = new[] { "larghezza_mm", "altezza_mm", "parete_mm" },
                ["tondo"] = new[] { "diametro_mm" },
                ["tubo"] = new[] { "diametro_mm", "parete_mm" },
                ["angolare"] = new[] { "larghezza_mm", "altezza_mm", "spessore_mm" },
                ["u"] = new[] { "larghezza_mm", "altezza_mm", "spessore_mm" },
                ["t"] = new[] { "larghezza_mm", "altezza_mm", "spessore_mm" },
                ["piatto"] = new[] { "larghezza_mm", "altezza_mm" },
                ["esagono"] = new[] { "chiave_mm" },
                ["asola"] = new[] { "larghezza_mm", "altezza_mm" },
            };

        /// <summary>
        /// I contorni di una sezione, o null con il motivo.
        ///
        /// I contorni escono nella stessa forma che il parser gia' capisce, e
        /// finiscono nello stesso esecutore di `estrudi_profilo`: una sezione
        /// non e' un'altra operazione, e' un modo di scrivere dei contorni.
        /// </summary>
        public static List<object> Contorni(
            string tipo, Dictionary<string, double> m, out string motivo)
        {
            motivo = null;
            if (string.IsNullOrWhiteSpace(tipo))
            {
                motivo = "Non hai detto che sezione: " + Elenco() + ".";
                return null;
            }

            switch (tipo.Trim().ToLowerInvariant())
            {
                case "tubolare":
                {
                    double b = m["larghezza_mm"], h = m["altezza_mm"], t = m["parete_mm"];
                    motivo = PareteStaDentro(t, Math.Min(b, h), "tubolare");
                    if (motivo != null) return null;
                    // Origine al centro. Due rettangoli concentrici: l'ingombro
                    // esterno sta scritto nelle coordinate, e non dipende da un
                    // verso di parete.
                    return new List<object>
                    {
                        Poligonale(Rettangolo(b, h)),
                        Poligonale(Rettangolo(b - 2 * t, h - 2 * t)),
                    };
                }

                case "tondo":
                    return new List<object> { Cerchio(0, 0, m["diametro_mm"]) };

                case "tubo":
                {
                    double d = m["diametro_mm"], t = m["parete_mm"];
                    motivo = PareteStaDentro(t, d, "tubo");
                    if (motivo != null) return null;
                    return new List<object>
                    {
                        Cerchio(0, 0, d),
                        Cerchio(0, 0, d - 2 * t),
                    };
                }

                case "piatto":
                    return new List<object>
                    {
                        Poligonale(Rettangolo(m["larghezza_mm"], m["altezza_mm"])),
                    };

                case "esagono":
                {
                    // QUOTATO SULL'APERTURA IN CHIAVE, che e' come si compra e
                    // come si misura con una chiave fissa. Il diametro
                    // circoscritto e' un'altra cosa, e confonderli da' un
                    // esagono piu' grande del 15%.
                    double c = m["chiave_mm"];
                    double r = c / Math.Sqrt(3.0);
                    var punti = new List<double[]>();
                    for (int i = 0; i < 6; i++)
                    {
                        double ang = Math.PI / 6 + i * Math.PI / 3;
                        punti.Add(new[] { Q(r * Math.Cos(ang)), Q(r * Math.Sin(ang)) });
                    }
                    return new List<object> { Poligonale(punti) };
                }

                case "angolare":
                {
                    double b = m["larghezza_mm"], h = m["altezza_mm"], t = m["spessore_mm"];
                    motivo = SpessoreStaDentro(t, Math.Min(b, h), "un'ala", "un piatto");
                    if (motivo != null) return null;
                    // Origine sullo SPIGOLO INTERNO: e' un punto che esiste sul
                    // pezzo e che in officina si misura. Le ali crescono verso
                    // +X e +Y.
                    return new List<object>
                    {
                        Poligonale(new List<double[]>
                        {
                            new[] { Q(0), Q(0) },
                            new[] { Q(b), Q(0) },
                            new[] { Q(b), Q(t) },
                            new[] { Q(t), Q(t) },
                            new[] { Q(t), Q(h) },
                            new[] { Q(0), Q(h) },
                        }),
                    };
                }

                case "u":
                {
                    double b = m["larghezza_mm"], h = m["altezza_mm"], t = m["spessore_mm"];
                    motivo = SpessoreStaDentro(2 * t, b, "l'anima", "un piatto")
                             ?? SpessoreStaDentro(t, h, "un'ala", "un piatto");
                    if (motivo != null) return null;
                    // Origine sullo spigolo esterno in basso a sinistra: le due
                    // ali salgono.
                    return new List<object>
                    {
                        Poligonale(new List<double[]>
                        {
                            new[] { Q(0), Q(0) },
                            new[] { Q(b), Q(0) },
                            new[] { Q(b), Q(h) },
                            new[] { Q(b - t), Q(h) },
                            new[] { Q(b - t), Q(t) },
                            new[] { Q(t), Q(t) },
                            new[] { Q(t), Q(h) },
                            new[] { Q(0), Q(h) },
                        }),
                    };
                }

                case "t":
                {
                    double b = m["larghezza_mm"], h = m["altezza_mm"], t = m["spessore_mm"];
                    motivo = SpessoreStaDentro(t, b, "l'ala", "un piatto")
                             ?? SpessoreStaDentro(t, h, "l'anima", "un piatto");
                    if (motivo != null) return null;
                    // Origine al centro dell'ala superiore: l'anima scende
                    // verso -Y.
                    double mb = b / 2.0, mt = t / 2.0;
                    return new List<object>
                    {
                        Poligonale(new List<double[]>
                        {
                            new[] { Q(-mb), Q(0) },
                            new[] { Q(mb), Q(0) },
                            new[] { Q(mb), Q(-t) },
                            new[] { Q(mt), Q(-t) },
                            new[] { Q(mt), Q(-h) },
                            new[] { Q(-mt), Q(-h) },
                            new[] { Q(-mt), Q(-t) },
                            new[] { Q(-mb), Q(-t) },
                        }),
                    };
                }

                case "asola":
                {
                    double b = m["larghezza_mm"], h = m["altezza_mm"];
                    if (b <= h)
                    {
                        motivo = string.Format(CultureInfo.InvariantCulture,
                            "Un'asola {0:0.###}x{1:0.###}: la lunghezza deve essere " +
                            "maggiore della larghezza, altrimenti e' un cerchio.", b, h);
                        return null;
                    }
                    double r = h / 2.0;
                    double centro = b / 2.0 - r;

                    // I PUNTI STANNO SUL CERCHIO CIRCOSCRITTO, non su quello
                    // nominale. Un'asola con i semicerchi inscritti e' piu'
                    // STRETTA del nominale, e una vite che ci deve passare non
                    // passa. Dove si approssima, si approssima in eccesso.
                    double rp = r / Math.Cos(Math.PI / (2 * SegmentiSemicerchio));
                    var punti = new List<double[]>();
                    for (int i = 0; i <= SegmentiSemicerchio; i++)
                    {
                        double ang = -Math.PI / 2 + Math.PI * i / SegmentiSemicerchio;
                        punti.Add(new[] { Q(centro + rp * Math.Cos(ang)), Q(rp * Math.Sin(ang)) });
                    }
                    for (int i = 0; i <= SegmentiSemicerchio; i++)
                    {
                        double ang = Math.PI / 2 + Math.PI * i / SegmentiSemicerchio;
                        punti.Add(new[] { Q(-centro + rp * Math.Cos(ang)), Q(rp * Math.Sin(ang)) });
                    }
                    return new List<object> { Poligonale(punti) };
                }
            }

            motivo = "Sezione «" + tipo + "» sconosciuta. Ci sono: " + Elenco() + ".";
            return null;
        }

        private static string Elenco()
        {
            return string.Join(", ", Volute.Keys);
        }

        // ─────────────────────────────────────────────────────────────────
        //  I controlli fisici
        // ─────────────────────────────────────────────────────────────────

        /// <summary>
        /// LA PARETE DEVE STARE STRETTAMENTE SOTTO LA META' DEL LATO MINORE.
        ///
        /// La ragione non e' che «meta' e' troppo»: **a meta' esatta la cavita'
        /// non c'e'**. Su un tubolare 80x80 con parete 40 il contorno interno
        /// diventa quattro punti coincidenti, area zero. SOLIDWORKS non lo
        /// segnala — un vuoto di area nulla non e' un errore, per lui — ed
        /// estrude una BARRA PIENA: un tubolare 80x80x5 lungo un metro pesa
        /// 11,8 kg, la barra piena ne pesa 50. **Quattro volte il peso, feature
        /// verde, rebuild pulito.**
        ///
        /// Sopra la meta' il contorno interno si rovescia: lo prenderebbe anche
        /// il controllo dei contorni, ma con un messaggio che parla di «vuoto
        /// che sborda» a chi ha scritto una parete. Un rifiuto che non nomina
        /// la quota sbagliata e' un rifiuto che si ripete identico.
        /// </summary>
        private static string PareteStaDentro(double parete, double latoMinore, string cosa)
        {
            if (latoMinore - 2.0 * parete > 0.001) return null;
            return string.Format(CultureInfo.InvariantCulture,
                "Parete {0:0.###} mm su un {1} da {2:0.###} mm: non resta cavita'. " +
                "La parete deve stare STRETTAMENTE sotto la meta', cioe' sotto " +
                "{3:0.###} mm. A meta' esatta il vuoto ha area zero, SOLIDWORKS non " +
                "lo segnala ed estrude una barra piena — che pesa quattro volte " +
                "tanto ed e' verde come quella giusta.",
                parete, cosa, latoMinore, latoMinore / 2.0);
        }

        /// <summary>
        /// Un lato non puo' essere spesso quanto se stesso.
        ///
        /// A uguaglianza esatta due vertici coincidono, il segmento sparisce, e
        /// resta un'altra sezione: non un angolare piu' tozzo, un **piatto**.
        /// Altro peso, altra inerzia, nessun errore.
        /// </summary>
        private static string SpessoreStaDentro(
            double spessore, double lato, string quale, string diventa)
        {
            if (spessore < lato) return null;
            return string.Format(CultureInfo.InvariantCulture,
                "Spessore {0:0.###} mm su {1} da {2:0.###} mm: non e' piu' un profilo, " +
                "viene fuori {3}. Lo spessore deve stare sotto il lato.",
                spessore, quale, lato, diventa);
        }

        // ─────────────────────────────────────────────────────────────────
        //  Geometria di supporto
        // ─────────────────────────────────────────────────────────────────

        /// <summary>
        /// Una coordinata, arrotondata a un decimo di micron.
        ///
        /// Non e' cosmesi: senza, due vertici che dovrebbero coincidere
        /// differiscono all'ultimo bit e SOLIDWORKS ci costruisce un segmento
        /// lungo 1e-16 — che non da' errore e lascia il contorno aperto.
        /// </summary>
        private static double Q(double v)
        {
            return Math.Round(v, 4, MidpointRounding.AwayFromZero);
        }

        private static List<double[]> Rettangolo(double larghezza, double altezza)
        {
            double mx = larghezza / 2.0, my = altezza / 2.0;
            return new List<double[]>
            {
                new[] { Q(-mx), Q(-my) },
                new[] { Q(mx), Q(-my) },
                new[] { Q(mx), Q(my) },
                new[] { Q(-mx), Q(my) },
            };
        }

        private static Dictionary<string, object> Poligonale(List<double[]> vertici)
        {
            var v = new List<object>(vertici.Count);
            foreach (var p in vertici) v.Add(new object[] { p[0], p[1] });
            return new Dictionary<string, object>
            {
                ["tipo"] = "poligonale",
                ["vertici"] = v.ToArray(),
            };
        }

        private static Dictionary<string, object> Cerchio(double cx, double cy, double diametro)
        {
            return new Dictionary<string, object>
            {
                ["tipo"] = "cerchio",
                ["centro"] = new object[] { Q(cx), Q(cy) },
                ["diametro_mm"] = diametro,
            };
        }
    }
}
