using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using HtmlAgilityPack;
using VDS.RDF;
using VDS.RDF.Writing;

public class PokemonService
{
    private readonly HttpClient _httpClient;
    private readonly Graph _globalGraph;

    // Dictionnaire pour stocker les traductions multilingues
    private Dictionary<string, List<(string, string)>> _translations;

    public PokemonService(HttpClient httpClient)
    {
        _httpClient = httpClient;
        _globalGraph = CreateGlobalGraph();
    }

    /// <summary>
    /// Charge les traductions depuis pokedex-i18n.tsv et les stocke dans _translations.
    /// </summary>
    public async Task LoadPokemonTranslationsAsync()
    {
        // Chemin vers le fichier pokedex-i18n.tsv
        var filePath = "wwwroot/data/pokedex-i18n.tsv";
        var lines = await System.IO.File.ReadAllLinesAsync(filePath);

        var translations = new Dictionary<string, List<(string, string)>>();

        foreach (var line in lines)
        {
            var columns = line.Split('\t');
            if (columns.Length != 4) continue;

            var type = columns[0];
            var pokemonId = columns[1];      // Souvent le numéro du Pokédex, ex: "001"
            var name = columns[2];          // Le nom traduit
            var language = columns[3];      // La langue (English, French, Japanese, etc.)

            // Ne garder que les lignes qui ont "pokemon"
            if (type != "pokemon") continue;

            if (!translations.ContainsKey(pokemonId))
            {
                translations[pokemonId] = new List<(string, string)>();
            }
            translations[pokemonId].Add((name, language));
        }

        _translations = translations; // Stocke le résultat dans la variable membre
    }

    /// <summary>
    /// Retourne l'ID de Pokédex (ex: "001") si le nom anglais correspond à 'pokemonName'.
    /// </summary>
    private string FindPokemonIdByEnglishName(string pokemonName)
    {
        if (_translations == null) return null;

        // On cherche dans _translations si on trouve un entry.Key (pokedex ID) pour lequel
        // figure (pokemonName, "English") ou (pokemonName, "english")
        foreach (var entry in _translations)
        {
            var listNames = entry.Value; // liste de (nom, langue)
            foreach (var (translatedName, lang) in listNames)
            {
                // On compare sur lang == "English" et le nom traduit
                // (On force en lower pour éviter les soucis de casse)
                if (lang.ToLower() == "english"
                    && translatedName.Equals(pokemonName, System.StringComparison.OrdinalIgnoreCase))
                {
                    return entry.Key; // On retourne l'ID du Pokédex
                }
            }
        }

        return null; // Pas trouvé
    }

    /// <summary>
    /// Récupère les données du Pokémon (infobox) depuis Bulbapedia, 
    /// puis ajoute les triplets dans le graphe, 
    /// y compris les traductions si disponibles.
    /// </summary>
    public async Task<string> GetPokemonDataAsync(string pokemonName)
    {
        var url = $"https://bulbapedia.bulbagarden.net/wiki/{pokemonName}_(Pokémon)";
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64)");

        var response = await _httpClient.SendAsync(request);
        if (!response.IsSuccessStatusCode)
        {
            return $"Erreur: {response.StatusCode}";
        }

        var htmlContent = await response.Content.ReadAsStringAsync();
        var htmlDoc = new HtmlDocument();
        htmlDoc.LoadHtml(htmlContent);

        // Infobox
        var infobox = htmlDoc.DocumentNode.SelectSingleNode("//table[contains(@class, 'roundy')]");
        if (infobox == null)
        {
            return "Infobox non trouvée.";
        }

        // Extraction des infos
        string name = ExtractPokemonName(infobox);            // ex : "Bulbasaur"
        string type = ExtractPokemonType(infobox);           // ex : "Grass"
        string height = ExtractHeightOrWeight(infobox, "Height");
        string weight = ExtractHeightOrWeight(infobox, "Weight");

        // Ajout des triplets dans le graphe global
        AddTriplesToGlobalGraph(pokemonName, name, type, height, weight);

        return $"Données de {name} ajoutées au graphe global.";
    }

    /// <summary>
    /// Ajoute les triplets de base + les traductions (si existantes) dans _globalGraph.
    /// 'id' ici est en fait le nom du Pokémon, ex: "Bulbasaur".
    /// 'name' est le nom réel extrait (peut être identique ou diff).
    /// </summary>
    private void AddTriplesToGlobalGraph(string id, string name, string type, string height, string weight)
    {
        // On utilise 'id' comme identifiant principal
        var pokemonUri = _globalGraph.CreateUriNode("ex:" + id);

        var hasName = _globalGraph.CreateUriNode("prop:hasName");
        var hasType = _globalGraph.CreateUriNode("prop:hasType");
        var hasHeight = _globalGraph.CreateUriNode("prop:hasHeight");
        var hasWeight = _globalGraph.CreateUriNode("prop:hasWeight");
        var label = _globalGraph.CreateUriNode("rdfs:label");

        // Triplets de base
        _globalGraph.Assert(pokemonUri, hasName, _globalGraph.CreateLiteralNode(name));
        _globalGraph.Assert(pokemonUri, hasType, _globalGraph.CreateLiteralNode(type));
        _globalGraph.Assert(pokemonUri, hasHeight, _globalGraph.CreateLiteralNode(height));
        _globalGraph.Assert(pokemonUri, hasWeight, _globalGraph.CreateLiteralNode(weight));

        // Ajout des traductions, si on a chargé _translations
        if (_translations != null)
        {
            // Trouver l'ID de Pokédex correspondant au 'id' (nom anglais) 
            // (ex: "Bulbasaur" => "001") dans le dictionnaire
            var pokedexId = FindPokemonIdByEnglishName(id);
            if (!string.IsNullOrEmpty(pokedexId) && _translations.ContainsKey(pokedexId))
            {
                // On ajoute pour chaque (nomTraduit, langue)
                foreach (var (translatedName, language) in _translations[pokedexId])
                {
                    var langLc = language.ToLower();
                    if (langLc != "official roomaji" && !string.IsNullOrEmpty(langLc))
                    {
                        _globalGraph.Assert(
                            pokemonUri,
                            label,
                            _globalGraph.CreateLiteralNode(translatedName, langLc)
                        );
                    }
                }
            }
        }
    }

    /// <summary>
    /// Génère les triplets pour tous les Pokémon listés par l'API,
    /// en parcourant tous les noms depuis Bulbapedia.
    /// </summary>
    public async Task<string> GenerateAllPokemonTripletsAsync()
    {
        var pokemonNames = await GetPokemonListAsync();
        var allTriples = new List<string>();

        // Filtrage d'une catégorie
        pokemonNames = pokemonNames.Where(name => name != "Pokémon (species)").ToList();

        foreach (var pokemonName in pokemonNames)
        {
            try
            {
                // Ex : "Bulbasaur", "Pikachu", etc.
                var rdfTriples = await GetPokemonDataAsync(pokemonName);
                allTriples.Add(rdfTriples);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Erreur lors du traitement de {pokemonName} : {ex.Message}");
            }
        }

        return string.Join("\n\n", allTriples) + "\n";
    }

    /// <summary>
    /// Récupère la liste de Pokémon depuis la catégorie "Pokémon" de Bulbapedia (max 50).
    /// </summary>
    private async Task<List<string>> GetPokemonListAsync()
    {
        var url = "https://bulbapedia.bulbagarden.net/w/api.php?action=query&list=categorymembers&cmtitle=Category:Pokémon&cmlimit=50&format=json";

        var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Add("User-Agent", "Mozilla/5.0");

        var response = await _httpClient.SendAsync(request);
        if (!response.IsSuccessStatusCode)
        {
            throw new System.Exception($"Erreur lors de la récupération de la liste des Pokémon : {response.StatusCode}");
        }

        var json = await response.Content.ReadAsStringAsync();
        var pokemonNames = new List<string>();

        using (var document = JsonDocument.Parse(json))
        {
            var members = document.RootElement
                .GetProperty("query")
                .GetProperty("categorymembers");

            foreach (var member in members.EnumerateArray())
            {
                if (member.TryGetProperty("title", out var title))
                {
                    // Ex: "Bulbasaur (Pokémon)" => "Bulbasaur"
                    pokemonNames.Add(title.GetString().Replace(" (Pokémon)", ""));
                }
            }
        }

        return pokemonNames;
    }

    private string ExtractPokemonName(HtmlNode infobox)
    {
        var nameNode = infobox.SelectSingleNode(".//b[1]");
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

    private Graph CreateGlobalGraph()
    {
        var graph = new Graph();
        graph.NamespaceMap.AddNamespace("rdf", new System.Uri("http://www.w3.org/1999/02/22-rdf-syntax-ns#"));
        graph.NamespaceMap.AddNamespace("rdfs", new System.Uri("http://www.w3.org/2000/01/rdf-schema#"));
        graph.NamespaceMap.AddNamespace("xsd", new System.Uri("http://www.w3.org/2001/XMLSchema#"));
        graph.NamespaceMap.AddNamespace("ex", new System.Uri("http://example.org/pokemon/"));
        graph.NamespaceMap.AddNamespace("prop", new System.Uri("http://example.org/property/"));
        return graph;
    }

    /// <summary>
    /// Sérialise le graphe global en Turtle, l'envoie à Fuseki, puis écrit un fichier .ttl local.
    /// </summary>
    public async Task SaveGlobalGraphToFileAndSendAsync(string filePath)
    {
        var writer = new CompressingTurtleWriter { PrettyPrintMode = true, HighSpeedModePermitted = false };

        using var fileWriter = new System.IO.StreamWriter(filePath);
        writer.Save(_globalGraph, fileWriter);

        var result = await SendRdfToFusekiAsync(_globalGraph);
        System.Console.WriteLine(result);
    }

    public async Task<string> SendRdfToFusekiAsync(Graph graph)
    {
        // Sérialiser le graphe en Turtle
        var writer = new CompressingTurtleWriter();
        using var stringWriter = new System.IO.StringWriter();
        writer.Save(graph, stringWriter);

        var rdfTriples = stringWriter.ToString();

        // URL de ton endpoint Fuseki
        var fusekiEndpoint = "http://localhost:3030/Pokemon/data";

        // Préparer la requête HTTP
        var content = new StringContent(rdfTriples, Encoding.UTF8, "text/turtle");

        // Envoyer la requête POST
        var response = await _httpClient.PostAsync(fusekiEndpoint, content);

        if (response.IsSuccessStatusCode)
        {
            return "Triplets RDF envoyés avec succès à Fuseki.";
        }
        else
        {
            return $"Erreur lors de l'envoi : {response.StatusCode} - {response.ReasonPhrase}";
        }
    }
}
