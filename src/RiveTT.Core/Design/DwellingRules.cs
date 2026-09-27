using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace RiveTT.Core.Design;

public enum RuleSeverity { Error, Warning }

/// <summary>One rule of the charter, with its source and its thresholds.</summary>
public sealed class DwellingRule
{
    public DwellingRule(string id, string label, string source, RuleSeverity severity,
        IDictionary<string, JToken>? values = null)
    {
        Id = id;
        Label = label;
        Source = source;
        Severity = severity;
        Values = values != null ? new Dictionary<string, JToken>(values) : new Dictionary<string, JToken>();
    }

    public string Id { get; }
    public string Label { get; }
    /// <summary>Where the rule comes from, with its status: "Réglementaire (à vérifier)" is not "Charte".</summary>
    public string Source { get; }
    public RuleSeverity Severity { get; set; }
    public bool Enabled { get; set; } = true;
    public Dictionary<string, JToken> Values { get; }

    public double Number(string key) => Values[key].Value<double>();
    public string Text(string key) => Values[key].Value<string>() ?? "";
    public IReadOnlyList<string> Texts(string key) =>
        Values[key] is JArray array ? array.Select(t => t.Value<string>() ?? "").ToList() : new List<string> { Text(key) };
}

/// <summary>
/// The dwelling rules of docs/retex/2026-09-24/05_rag-plan-logement.md, as data. Every rule
/// carries the status its source document gives it: "Réglementaire" rules are to be checked
/// against the text in force (arrêté du 24 décembre 2015) before any opposable use, and
/// "Pratique agence" rules are the session's findings, still to be confirmed by the agency.
/// Thresholds can be overridden per call; a rule can be disabled, never silently.
/// </summary>
public sealed class DwellingRuleSet
{
    public const string DefaultName = "charte-club-2026";

    private const string Charte = "Charte agence";
    private const string Reglementaire = "Réglementaire (arrêté du 24/12/2015 — à vérifier sur le texte en vigueur)";
    private const string PratiqueAgence = "Pratique agence (constat du retex 2026-09-24 — à confirmer par l'agence)";
    private const string Decence = "Décret décence 2002 (à vérifier)";

    public string Name { get; private set; } = DefaultName;
    public List<DwellingRule> Rules { get; } = new();
    public List<string> OverridesApplied { get; } = new();

    public DwellingRule this[string id] => Rules.First(r => r.Id == id);

    public static DwellingRuleSet CreateDefault()
    {
        var set = new DwellingRuleSet();
        void Add(string id, string label, string source, RuleSeverity severity, object? values = null) =>
            set.Rules.Add(new DwellingRule(id, label, source, severity,
                values == null ? null : JObject.FromObject(values).Properties().ToDictionary(p => p.Name, p => p.Value)));

        // Distribution
        Add("ENTREE_OUVERTE_SEJOUR", "L'entrée s'ouvre sur le séjour, sans cloison ni porte", Charte, RuleSeverity.Error,
            new { minOpeningMm = 900 });
        Add("ENTREE_GABARIT", "Rectangle de manœuvre libre 1,20 x 2,20 m dans l'entrée", Reglementaire + " / " + Charte,
            RuleSeverity.Error, new { widthMm = 1200, depthMm = 2200 });
        Add("ETEL_ENTREE", "ETEL présent dans l'entrée", Charte, RuleSeverity.Error,
            new { familyPrefix = "ELC_ETEL", maxDistanceMm = 300 });
        Add("CIRC_INT_LARGEUR", "Largeur utile des dégagements intérieurs ≥ 0,90 m", "Réglementaire (0,90 m)",
            RuleSeverity.Error, new { minMm = 900 });
        Add("CIRC_INT_LARGEUR_MAX", "Dégagements : viser 1,00 m, pas plus", PratiqueAgence, RuleSeverity.Warning,
            new { targetMaxMm = 1000, marginMm = 100 });
        Add("RATIO_CIRCULATION", "(entrée + dégagements) / surface du logement ≤ 12 %", PratiqueAgence, RuleSeverity.Warning,
            new { max = 0.12 });
        Add("CHAMBRE_DEPUIS_DGT", "Chambres desservies par un dégagement (T3 et plus)", PratiqueAgence, RuleSeverity.Warning,
            new { fromTypology = 3 });
        Add("WC_SEPARE", "WC séparé à partir du T3", PratiqueAgence, RuleSeverity.Warning, new { fromTypology = 3 });
        Add("CUISINE_TYPO", "Équipement de cuisine selon la typologie (T1 kitchenette ; T2 évier + RF + cuisson ; T3+ + linéaire)",
            Charte, RuleSeverity.Warning,
            new
            {
                kitchenettePrefix = "SAN_Kitchenette",
                sinkPrefixes = new[] { "SAN_Meuble évier", "SAN_Kitchenette" },
                appliancePrefix = "ELC_Emplacement électroménager",
                applianceTypes = new[] { "RF", "Cuisson" },
                worktopPrefix = "MOB_Linéaire"
            });

        // Accessibility (unité de vie)
        Add("CERCLE_150_SDE", "Cercle libre Ø 1,50 m dans la salle d'eau de l'unité de vie", Reglementaire, RuleSeverity.Error,
            new { diameterMm = 1500 });
        Add("CERCLE_150_SEJOUR", "Cercle libre Ø 1,50 m dans le séjour, hors mobilier", Reglementaire, RuleSeverity.Error,
            new { diameterMm = 1500 });
        Add("CHAMBRE_PMR_LIT", "Une chambre contient le lit 1,40 x 1,90 et ses passages (3,20 x 3,10 m utiles)",
            Reglementaire + " / " + Charte, RuleSeverity.Error, new { widthMm = 3200, depthMm = 3100 });
        Add("WC_PMR", "WC de l'unité de vie avec espace d'usage (type SAN_WC · Suspendu PMR)", Reglementaire + " / " + Charte,
            RuleSeverity.Warning, new { familyPrefix = "SAN_WC", typeContains = "PMR" });
        Add("DOUCHE_PLAIN_PIED", "Douche sans ressaut dans l'unité de vie", Charte, RuleSeverity.Warning,
            new { familyPrefix = "SAN_Douche sans ressaut" });
        Add("PORTES_PASSAGE", "Portes intérieures PP 93x204, palières PP93x220 16", Charte, RuleSeverity.Warning,
            new { interiorType = "PP 93x204", landingType = "PP93x220 16", landingFamilyContains = "palière" });

        // Areas
        Add("SURF_CHAMBRE", "Chambre ≥ 9 m²", PratiqueAgence, RuleSeverity.Warning, new { minM2 = 9 });
        Add("SURF_SEJOUR_T2", "Séjour / cuisine d'un T2 ≈ 20 m²", PratiqueAgence, RuleSeverity.Warning, new { minM2 = 20 });
        Add("SURF_PIECE_PRINCIPALE", "Pièce principale ≥ 9 m²", Decence, RuleSeverity.Warning, new { minM2 = 9 });

        // Consistency
        Add("TYPOLOGIE_COHERENTE", "Nombre de chambres cohérent avec la typologie (T n = n - 1 chambres)", "RiveTT",
            RuleSeverity.Warning);
        Add("PIECES_NOMMEES", "Toutes les pièces portent un nom du vocabulaire de la charte", Charte, RuleSeverity.Warning);
        Add("SURFACE_PROGRAMME", "Surface conforme au programme (± 5 %)", "Programme", RuleSeverity.Warning,
            new { tolerance = 0.05 });
        return set;
    }

    /// <summary>
    /// Applies {"RULE_ID": {"enabled": false, "severity": "warning", "&lt;value&gt;": ...}}.
    /// An unknown rule id or value key is reported, never ignored.
    /// </summary>
    public void ApplyOverrides(JObject? overrides, List<string> errors)
    {
        if (overrides == null) return;
        foreach (var property in overrides.Properties())
        {
            if (property.Name == "name")
            {
                Name = property.Value.Value<string>() ?? Name;
                continue;
            }
            var rule = Rules.FirstOrDefault(r => string.Equals(r.Id, property.Name, StringComparison.OrdinalIgnoreCase));
            if (rule == null)
            {
                errors.Add($"rules: unknown rule '{property.Name}'. Rules: {string.Join(", ", Rules.Select(r => r.Id))}");
                continue;
            }
            if (property.Value is not JObject settings)
            {
                errors.Add($"rules.{rule.Id} must be an object, e.g. {{\"enabled\": false}}");
                continue;
            }
            foreach (var setting in settings.Properties())
            {
                switch (setting.Name)
                {
                    case "enabled":
                        rule.Enabled = setting.Value.Value<bool>();
                        OverridesApplied.Add($"{rule.Id}.enabled = {rule.Enabled}");
                        break;
                    case "severity":
                        if (Enum.TryParse<RuleSeverity>(setting.Value.Value<string>(), true, out var severity))
                        {
                            rule.Severity = severity;
                            OverridesApplied.Add($"{rule.Id}.severity = {severity}");
                        }
                        else errors.Add($"rules.{rule.Id}.severity must be error or warning");
                        break;
                    default:
                        if (!rule.Values.ContainsKey(setting.Name))
                        {
                            errors.Add($"rules.{rule.Id}: unknown value '{setting.Name}'. Values: " +
                                       (rule.Values.Count == 0 ? "(none)" : string.Join(", ", rule.Values.Keys)));
                            break;
                        }
                        rule.Values[setting.Name] = setting.Value.DeepClone();
                        OverridesApplied.Add($"{rule.Id}.{setting.Name} = {setting.Value.ToString(Newtonsoft.Json.Formatting.None)}");
                        break;
                }
            }
        }
    }

    public JArray Describe() => new(Rules.Select(rule => new JObject
    {
        ["rule"] = rule.Id,
        ["label"] = rule.Label,
        ["source"] = rule.Source,
        ["severity"] = rule.Severity.ToString().ToLowerInvariant(),
        ["enabled"] = rule.Enabled,
        ["values"] = new JObject(rule.Values.Select(v => new JProperty(v.Key, v.Value)))
    }));
}
