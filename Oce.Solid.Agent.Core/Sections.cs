using System;
using System.Collections.Generic;
using System.Globalization;

namespace Oce.Solid.Agent.Core
{
    /// <summary>
    /// LE SEZIONI NORMALIZZATE: da due o tre quote ai contorni da estrudere.
    ///
    /// E' la ricetta di `extrude_section`. Viveva solo nel server Python
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
    public static class Sections
    {
        /// <summary>
        /// Segmenti per ogni semicerchio dell'asola.
        ///
        /// **Non e' un parametro**, ed e' voluto: se lo fosse, si potrebbe
        /// abbassare e ottenere un'asola ottagonale senza che nessuno se ne
        /// accorga.
        /// </summary>
        public const int SemicircleSegments = 24;

        /// <summary>Le sezioni che si sanno fare, e quali quote vogliono.</summary>
        public static readonly Dictionary<string, string[]> Wanted =
            new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
            {
                ["tubular"] = new[] { "width_mm", "height_mm", "wall_mm" },
                ["round"] = new[] { "diameter_mm" },
                ["tube"] = new[] { "diameter_mm", "wall_mm" },
                ["angle"] = new[] { "width_mm", "height_mm", "thickness_mm" },
                ["u"] = new[] { "width_mm", "height_mm", "thickness_mm" },
                ["t"] = new[] { "width_mm", "height_mm", "thickness_mm" },
                ["flat"] = new[] { "width_mm", "height_mm" },
                ["hexagon"] = new[] { "across_flats_mm" },
                ["slot"] = new[] { "width_mm", "height_mm" },
            };

        /// <summary>
        /// I contorni di una sezione, o null con il motivo.
        ///
        /// I contorni escono nella stessa forma che il parser gia' capisce, e
        /// finiscono nello stesso esecutore di `extrude_profile`: una sezione
        /// non e' un'altra operazione, e' un modo di scrivere dei contorni.
        /// </summary>
        public static List<object> Contours(
            string typ, Dictionary<string, double> m, out string reason)
        {
            reason = null;
            if (string.IsNullOrWhiteSpace(typ))
            {
                reason = "Non hai detto che sezione: " + Items() + ".";
                return null;
            }

            switch (typ.Trim().ToLowerInvariant())
            {
                case "tubular":
                {
                    double b = m["width_mm"], h = m["height_mm"], t = m["wall_mm"];
                    reason = WallIsInside(t, Math.Min(b, h), "tubolare");
                    if (reason != null) return null;
                    // Origine al centro. Due rettangoli concentrici: l'ingombro
                    // esterno sta scritto nelle coordinate, e non dipende da un
                    // verso di parete.
                    return new List<object>
                    {
                        Polygonal(Rectangle(b, h)),
                        Polygonal(Rectangle(b - 2 * t, h - 2 * t)),
                    };
                }

                case "round":
                    return new List<object> { Circle(0, 0, m["diameter_mm"]) };

                case "tube":
                {
                    double d = m["diameter_mm"], t = m["wall_mm"];
                    reason = WallIsInside(t, d, "tubo");
                    if (reason != null) return null;
                    return new List<object>
                    {
                        Circle(0, 0, d),
                        Circle(0, 0, d - 2 * t),
                    };
                }

                case "flat":
                    return new List<object>
                    {
                        Polygonal(Rectangle(m["width_mm"], m["height_mm"])),
                    };

                case "hexagon":
                {
                    // QUOTATO SULL'APERTURA IN CHIAVE, che e' come si compra e
                    // come si misura con una chiave fissa. Il diametro
                    // circoscritto e' un'altra cosa, e confonderli da' un
                    // esagono piu' grande del 15%.
                    double c = m["across_flats_mm"];
                    double r = c / Math.Sqrt(3.0);
                    var points = new List<double[]>();
                    for (int i = 0; i < 6; i++)
                    {
                        double angle = Math.PI / 6 + i * Math.PI / 3;
                        points.Add(new[] { Q(r * Math.Cos(angle)), Q(r * Math.Sin(angle)) });
                    }
                    return new List<object> { Polygonal(points) };
                }

                case "angle":
                {
                    double b = m["width_mm"], h = m["height_mm"], t = m["thickness_mm"];
                    reason = ThicknessIsInside(t, Math.Min(b, h), "un'ala", "un piatto");
                    if (reason != null) return null;
                    // Origine sullo SPIGOLO INTERNO: e' un punto che esiste sul
                    // pezzo e che in officina si misura. Le ali crescono verso
                    // +X e +Y.
                    return new List<object>
                    {
                        Polygonal(new List<double[]>
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
                    double b = m["width_mm"], h = m["height_mm"], t = m["thickness_mm"];
                    reason = ThicknessIsInside(2 * t, b, "l'anima", "un piatto")
                             ?? ThicknessIsInside(t, h, "un'ala", "un piatto");
                    if (reason != null) return null;
                    // Origine sullo spigolo esterno in basso a sinistra: le due
                    // ali salgono.
                    return new List<object>
                    {
                        Polygonal(new List<double[]>
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
                    double b = m["width_mm"], h = m["height_mm"], t = m["thickness_mm"];
                    reason = ThicknessIsInside(t, b, "l'ala", "un piatto")
                             ?? ThicknessIsInside(t, h, "l'anima", "un piatto");
                    if (reason != null) return null;
                    // Origine al centro dell'ala superiore: l'anima scende
                    // verso -Y.
                    double mb = b / 2.0, mt = t / 2.0;
                    return new List<object>
                    {
                        Polygonal(new List<double[]>
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

                case "slot":
                {
                    double b = m["width_mm"], h = m["height_mm"];
                    if (b <= h)
                    {
                        reason = string.Format(CultureInfo.InvariantCulture,
                            "Un'asola {0:0.###}x{1:0.###}: la lunghezza deve essere " +
                            "maggiore della larghezza, altrimenti e' un cerchio.", b, h);
                        return null;
                    }
                    double r = h / 2.0;
                    double center = b / 2.0 - r;

                    // I PUNTI STANNO SUL CERCHIO CIRCOSCRITTO, non su quello
                    // nominale. Un'asola con i semicerchi inscritti e' piu'
                    // STRETTA del nominale, e una vite che ci deve passare non
                    // passa. Dove si approssima, si approssima in eccesso.
                    double rp = r / Math.Cos(Math.PI / (2 * SemicircleSegments));
                    var points = new List<double[]>();
                    for (int i = 0; i <= SemicircleSegments; i++)
                    {
                        double angle = -Math.PI / 2 + Math.PI * i / SemicircleSegments;
                        points.Add(new[] { Q(center + rp * Math.Cos(angle)), Q(rp * Math.Sin(angle)) });
                    }
                    for (int i = 0; i <= SemicircleSegments; i++)
                    {
                        double angle = Math.PI / 2 + Math.PI * i / SemicircleSegments;
                        points.Add(new[] { Q(-center + rp * Math.Cos(angle)), Q(rp * Math.Sin(angle)) });
                    }
                    return new List<object> { Polygonal(points) };
                }
            }

            reason = "Sezione «" + typ + "» sconosciuta. Ci sono: " + Items() + ".";
            return null;
        }

        private static string Items()
        {
            return string.Join(", ", Wanted.Keys);
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
        private static string WallIsInside(double wall, double shorterSide, string what)
        {
            if (shorterSide - 2.0 * wall > 0.001) return null;
            return string.Format(CultureInfo.InvariantCulture,
                "Parete {0:0.###} mm su un {1} da {2:0.###} mm: non resta cavita'. " +
                "La parete deve stare STRETTAMENTE sotto la meta', cioe' sotto " +
                "{3:0.###} mm. A meta' esatta il vuoto ha area zero, SOLIDWORKS non " +
                "lo segnala ed estrude una barra piena — che pesa quattro volte " +
                "tanto ed e' verde come quella giusta.",
                wall, what, shorterSide, shorterSide / 2.0);
        }

        /// <summary>
        /// Un lato non puo' essere spesso quanto se stesso.
        ///
        /// A uguaglianza esatta due vertici coincidono, il segmento sparisce, e
        /// resta un'altra sezione: non un angolare piu' tozzo, un **piatto**.
        /// Altro peso, altra inerzia, nessun errore.
        /// </summary>
        private static string ThicknessIsInside(
            double thickness, double side, string which, string becomes)
        {
            if (thickness < side) return null;
            return string.Format(CultureInfo.InvariantCulture,
                "Spessore {0:0.###} mm su {1} da {2:0.###} mm: non e' piu' un profilo, " +
                "viene fuori {3}. Lo spessore deve stare sotto il lato.",
                thickness, which, side, becomes);
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

        private static List<double[]> Rectangle(double width, double height)
        {
            double mx = width / 2.0, my = height / 2.0;
            return new List<double[]>
            {
                new[] { Q(-mx), Q(-my) },
                new[] { Q(mx), Q(-my) },
                new[] { Q(mx), Q(my) },
                new[] { Q(-mx), Q(my) },
            };
        }

        private static Dictionary<string, object> Polygonal(List<double[]> vertices)
        {
            var v = new List<object>(vertices.Count);
            foreach (var p in vertices) v.Add(new object[] { p[0], p[1] });
            return new Dictionary<string, object>
            {
                ["kind"] = "polygonal",
                ["vertices"] = v.ToArray(),
            };
        }

        private static Dictionary<string, object> Circle(double cx, double cy, double diameter)
        {
            return new Dictionary<string, object>
            {
                ["kind"] = "circle",
                ["center"] = new object[] { Q(cx), Q(cy) },
                ["diameter_mm"] = diameter,
            };
        }
    }
}
