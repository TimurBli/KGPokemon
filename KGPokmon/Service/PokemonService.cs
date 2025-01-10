using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using HtmlAgilityPack;
using VDS.RDF;
using VDS.RDF.Writing;

public class PokemonService
{
    private readonly HttpClient _httpClient;

    public PokemonService(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }
public async Task<Dictionary<string, List<(string, string)>>> LoadPokemonTranslationsAsync()
{
    var translations = new Dictionary<string, List<(string, string)>>();

    // Chemin vers le fichier pokedex-i18n.tsv
    var filePath = "wwwroot/data/pokedex-i18n.tsv";

    // Lire chaque ligne du fichier
    var lines = await File.ReadAllLinesAsync(filePath);

    foreach (var line in lines)
    {
        var columns = line.Split('\t');
        if (columns.Length != 4) continue;

        var type = columns[0];
        var pokemonId = columns[1];
        var name = columns[2];
        var language = columns[3];

        if (type != "pokemon") continue;

        // Ajoute les traductions dans le dictionnaire
        if (!translations.ContainsKey(pokemonId))
        {
            translations[pokemonId] = new List<(string, string)>();
        }
        translations[pokemonId].Add((name, language));
    }

    return translations;
}

    public async Task<string> GetPokemonDataAsync(string pokemonName, Dictionary<string, List<(string, string)>> translations)
    {
        var url = $"https://bulbapedia.bulbagarden.net/wiki/{pokemonName}_(Pokémon)";

        var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Add("User-Agent", "Mozilla/5.0");

        var response = await _httpClient.SendAsync(request);
        if (!response.IsSuccessStatusCode)
        {
            return $"Erreur: {response.StatusCode}";
        }

        var htmlContent = await response.Content.ReadAsStringAsync();
        var htmlDoc = new HtmlDocument();
        htmlDoc.LoadHtml(htmlContent);

        var infobox = htmlDoc.DocumentNode.SelectSingleNode("//table[contains(@class, 'roundy')]");
        if (infobox == null)
        {
            return "Infobox non trouvée.";
        }

        string name = ExtractPokemonName(infobox);
        string type = ExtractPokemonType(infobox);
        string height = ExtractHeightOrWeight(infobox, "Height");
        string weight = ExtractHeightOrWeight(infobox, "Weight");

        // Trouver l'ID du Pokémon dans le dictionnaire des traductions
        var pokemonId = translations.FirstOrDefault(t =>
            t.Value.Any(tr => tr.Item1.Equals(pokemonName, StringComparison.OrdinalIgnoreCase))).Key;

        if (string.IsNullOrEmpty(pokemonId))
        {
            return "ID du Pokémon non trouvé.";
        }

        // Générer les triplets RDF avec les traductions
        string rdfTriples = CreateRdfTriplesFormatted(pokemonId, name, type, height, weight, translations);

        return rdfTriples;
    }




    private string ExtractPokemonName(HtmlNode infobox)
    {
        var nameNode = infobox.SelectSingleNode(".//b[1]"); // Le premier <b> contient souvent le nom anglais
        return nameNode != null ? HtmlEntity.DeEntitize(nameNode.InnerText.Trim()) : "Nom non trouvé";
    }

    private string ExtractPokemonType(HtmlNode infobox)
    {
        var typeNode = infobox.SelectSingleNode(".//a[contains(@href, '(type)')]");
        return typeNode != null ? HtmlEntity.DeEntitize(typeNode.InnerText.Trim()) : "Type non trouvé";
    }

    private string ExtractHeightOrWeight(HtmlNode infobox, string dimensionName)
    {
        var dimensionSection = infobox.SelectSingleNode($".//b[a/span[contains(text(), '{dimensionName}')]]/following-sibling::table");
        if (dimensionSection != null)
        {
            var valueCell = dimensionSection.SelectSingleNode(".//tr/td[2]");
            if (valueCell != null)
            {
                return HtmlEntity.DeEntitize(valueCell.InnerText.Trim());
            }
        }
        return $"{dimensionName} non trouvé";
    }
    public async Task<string> SendRdfToFusekiAsync(string rdfTriples)
    {
        // URL de ton endpoint Fuseki
        var fusekiEndpoint = "http://localhost:3030/Pokemon/data";

        // Préparer la requête HTTP
        var content = new StringContent(rdfTriples, Encoding.UTF8, "text/turtle");

        // Envoyer la requête POST
        var response = await _httpClient.PostAsync(fusekiEndpoint, content);

        // Vérifier la réponse
        if (response.IsSuccessStatusCode)
        {
            return "Triplets RDF envoyés avec succès à Fuseki.";
        }
        else
        {
            return $"Erreur lors de l'envoi : {response.StatusCode} - {response.ReasonPhrase}";
        }
    }

    private bool IsValidLanguageTag(string language)
    {
        // Vérifie si la balise de langue suit le format ISO 639
        return System.Globalization.CultureInfo
            .GetCultures(System.Globalization.CultureTypes.AllCultures)
            .Any(culture => culture.Name.Equals(language, StringComparison.OrdinalIgnoreCase));
    }
    private static readonly Dictionary<string, string> LanguageReplacements = new()
{
    { "official roomaji", "ja-Latn" },       // Japonais en alphabet latin
    { "Simplified Chinese", "zh-Hans" },     // Chinois simplifié
    { "Traditional Chinese", "zh-Hant" }     // Chinois traditionnel
};

    private string CreateRdfTriplesFormatted(
    string id, string name, string type, string height, string weight, Dictionary<string, List<(string, string)>> translations)
    {
        var graph = new Graph();

        // Ajouter les espaces de noms
        graph.NamespaceMap.AddNamespace("ex", new Uri("http://example.org/pokemon/"));
        graph.NamespaceMap.AddNamespace("prop", new Uri("http://example.org/property/"));

        // Créer l'URI pour le Pokémon
        var pokemonUri = graph.CreateUriNode($"ex:{id}");

        // Ajouter les triplets de base
        graph.Assert(pokemonUri, graph.CreateUriNode("prop:hasName"), graph.CreateLiteralNode(name));
        graph.Assert(pokemonUri, graph.CreateUriNode("prop:hasType"), graph.CreateLiteralNode(type));
        graph.Assert(pokemonUri, graph.CreateUriNode("prop:hasHeight"), graph.CreateLiteralNode(height));
        graph.Assert(pokemonUri, graph.CreateUriNode("prop:hasWeight"), graph.CreateLiteralNode(weight));

        // Ajouter les étiquettes multilingues
        if (translations.ContainsKey(id))
        {
            foreach (var (translatedName, language) in translations[id])
            {
                // Crée une variable locale pour stocker la balise de langue corrigée
                string correctedLanguage = language;

                // Remplace la langue si nécessaire
                if (LanguageReplacements.ContainsKey(correctedLanguage))
                {
                    correctedLanguage = LanguageReplacements[correctedLanguage];
                }

                // Vérifie si la langue est valide
                if (IsValidLanguageTag(correctedLanguage))
                {
                    graph.Assert(
                        pokemonUri,
                        graph.CreateUriNode("rdfs:label"),
                        graph.CreateLiteralNode(translatedName, correctedLanguage.ToLower())
                    );
                }
            }

        }

        // Sérialiser le graphe en Turtle
        var writer = new CompressingTurtleWriter { PrettyPrintMode = true };
        using var stringWriter = new System.IO.StringWriter();
        writer.Save(graph, stringWriter);

        return stringWriter.ToString();
    }




}
