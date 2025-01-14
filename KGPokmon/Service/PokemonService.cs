﻿using System.Net.Http;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Threading.Tasks;
using AngleSharp.Io;
using HtmlAgilityPack;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using VDS.RDF;
using VDS.RDF.Query;
using VDS.RDF.Writing;
using HttpMethod = System.Net.Http.HttpMethod;

public class PokemonService
{
    private readonly HttpClient _httpClient;
    private readonly Graph _globalGraph;
    private readonly string fusekiEndpoint = "http://localhost:3030/Pokemon/query";

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
        var request = new HttpRequestMessage(System.Net.Http.HttpMethod.Get, url);
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
        // Utilisation de 'id' comme identifiant principal
        var pokemonUri = _globalGraph.CreateUriNode("ex:" + id);

        // Création des nœuds de propriétés
        var hasName = _globalGraph.CreateUriNode("prop:hasName");
        var hasType = _globalGraph.CreateUriNode("prop:hasType");
        var hasHeight = _globalGraph.CreateUriNode("prop:hasHeight");
        var hasWeight = _globalGraph.CreateUriNode("prop:hasWeight");
        var label = _globalGraph.CreateUriNode("rdfs:label");
        var sameAs = _globalGraph.CreateUriNode("owl:sameAs");

        // Ajout des triplets de base
        _globalGraph.Assert(pokemonUri, hasName, _globalGraph.CreateLiteralNode(name));
        _globalGraph.Assert(pokemonUri, hasType, _globalGraph.CreateLiteralNode(type));
        _globalGraph.Assert(pokemonUri, hasHeight, _globalGraph.CreateLiteralNode(height));
        _globalGraph.Assert(pokemonUri, hasWeight, _globalGraph.CreateLiteralNode(weight));

        // Ajout du triplet owl:sameAs vers DBpedia
        var dbpediaUri = _globalGraph.CreateUriNode(new Uri($"http://dbpedia.org/resource/{Uri.EscapeDataString(name)}"));
        _globalGraph.Assert(pokemonUri, sameAs, dbpediaUri);

        // Ajout des traductions (si disponibles)
        if (_translations != null)
        {
            var pokedexId = FindPokemonIdByEnglishName(id);
            if (!string.IsNullOrEmpty(pokedexId) && _translations.ContainsKey(pokedexId))
            {
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
        var url = "https://bulbapedia.bulbagarden.net/w/api.php?action=query&list=categorymembers&cmtitle=Category:Pokémon&cmlimit=1000&format=json";

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
        graph.NamespaceMap.AddNamespace("rdf", new Uri("http://www.w3.org/1999/02/22-rdf-syntax-ns#"));
        graph.NamespaceMap.AddNamespace("rdfs", new Uri("http://www.w3.org/2000/01/rdf-schema#"));
        graph.NamespaceMap.AddNamespace("xsd", new Uri("http://www.w3.org/2001/XMLSchema#"));
        graph.NamespaceMap.AddNamespace("ex", new Uri("http://example.org/pokemon/"));
        graph.NamespaceMap.AddNamespace("prop", new Uri("http://example.org/property/"));
        graph.NamespaceMap.AddNamespace("owl", new Uri("http://www.w3.org/2002/07/owl#")); // Ajout de owl
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

    public async Task<string> SendShaclSchemaToFusekiAsync()
    {
        // Lire le contenu du fichier SHACL
        var filePath = "wwwroot/data/shapes.ttl";
        var schemaContent = await System.IO.File.ReadAllTextAsync(filePath);

        // Préparer la requête POST pour le endpoint /data
        var fusekiEndpoint = "http://localhost:3030/Pokemon/data";
        var content = new StringContent(schemaContent, Encoding.UTF8, "text/turtle");

        // Envoyer la requête POST
        var response = await _httpClient.PostAsync(fusekiEndpoint, content);

        if (response.IsSuccessStatusCode)
        {
            return "Schéma SHACL envoyé avec succès.";
        }
        else
        {
            var errorContent = await response.Content.ReadAsStringAsync();
            return $"Erreur lors de l'envoi du schéma : {response.StatusCode} - {response.ReasonPhrase}\n{errorContent}";
        }
    }

    //public async task<string> validatetripleswithshaclasync()
    //{
    //    var sparqlendpoint = "http://localhost:3030/pokemon/query";

    //    var sparqlquery = @"
    //    prefix sh: <http://www.w3.org/ns/shacl#>
    //    select ?focusnode ?resultpath ?value ?resultmessage
    //    where {
    //      ?report a sh:validationreport ;
    //              sh:result ?result .
    //      ?result sh:focusnode ?focusnode ;
    //              sh:resultpath ?resultpath ;
    //              sh:value ?value ;
    //              sh:resultmessage ?resultmessage .
    //    }";

    //    var request = new httprequestmessage(httpmethod.post, sparqlendpoint)
    //    {
    //        content = new stringcontent($"query={system.web.httputility.urlencode(sparqlquery)}", encoding.utf8, "application/x-www-form-urlencoded")
    //    };

    //    var response = await _httpclient.sendasync(request);

    //    if (response.issuccessstatuscode)
    //    {
    //        var resultcontent = await response.content.readasstringasync();
    //        return $"validation terminée : {resultcontent}";
    //    }
    //    else
    //    {
    //        return $"erreur lors de la validation : {response.statuscode} - {response.reasonphrase}";
    //    }
    //}

    public async Task<string> InsertInvalidTriplesAsync()
    {
        var graph = new Graph();
        graph.NamespaceMap.AddNamespace("ex", new Uri("http://example.org/pokemon/"));
        graph.NamespaceMap.AddNamespace("prop", new Uri("http://example.org/property/"));

        // Créer un triplet incorrect (manque le nom)
        var pokemonUri = graph.CreateUriNode("ex:MissingNamePokemon");
        var rdfType = graph.CreateUriNode("rdf:type");
        var hasType = graph.CreateUriNode("prop:hasType");
        graph.Assert(pokemonUri, rdfType, graph.CreateUriNode("ex:Pokemon"));
        graph.Assert(pokemonUri, hasType, graph.CreateLiteralNode("Fire"));

        // Créer un triplet incorrect (hauteur mal formatée)
        var anotherPokemonUri = graph.CreateUriNode("ex:BadHeightPokemon");
        graph.Assert(anotherPokemonUri, rdfType, graph.CreateUriNode("ex:Pokemon"));
        var hasName = graph.CreateUriNode("prop:hasName");
        var hasHeight = graph.CreateUriNode("prop:hasHeight");
        graph.Assert(anotherPokemonUri, hasName, graph.CreateLiteralNode("Charizard"));
        graph.Assert(anotherPokemonUri, hasHeight, graph.CreateLiteralNode("two meters"));  // Mauvais format

        // Envoyer à Fuseki
        var writer = new CompressingTurtleWriter();
        using var stringWriter = new System.IO.StringWriter();
        writer.Save(graph, stringWriter);

        var rdfTriples = stringWriter.ToString();
        var fusekiEndpoint = "http://localhost:3030/Pokemon/data";
        var content = new StringContent(rdfTriples, Encoding.UTF8, "text/turtle");

        var response = await _httpClient.PostAsync(fusekiEndpoint, content);
        return response.IsSuccessStatusCode ? "Triplets incorrects envoyés." : "Erreur lors de l'envoi des triplets.";
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

    //-----------------------------------------------------------------------------------


    public async Task<string> GetLinkedData(string pokemonName, string acceptHeader)
    {
        var sparqlQuery = $@"
            SELECT ?predicate ?object
            WHERE {{
                <http://example.org/pokemon/{pokemonName}> ?predicate ?object .
            }}";

        var results = await ExecuteSparqlQueryAsync(sparqlQuery);

        if (!results.Any())
        {
            return $"Aucune donnée trouvée pour {pokemonName}";
        }

        if (acceptHeader.Contains("text/turtle"))
        {
            return GenerateTurtleResponse(pokemonName, results);
        }
        else if (acceptHeader.Contains("text/html"))
        {
            return GenerateHtmlResponse(pokemonName, results);
        }

        return "Format non supporté. Utilisez text/turtle ou text/html.";
    }

    private async Task<List<(string Predicate, string Object)>> ExecuteSparqlQueryAsync(string query)
    {
        var sparqlClient = new SparqlRemoteEndpoint(new Uri(fusekiEndpoint));

        SparqlResultSet resultSet;
        try
        {
            resultSet = sparqlClient.QueryWithResultSet(query);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Erreur lors de l'exécution de la requête SPARQL : {ex.Message}");
            return new List<(string Predicate, string Object)>();
        }

        return resultSet
            .Select(result => (
                Predicate: result["predicate"].ToString(),
                Object: result["object"].ToString()))
            .ToList();
    }

    private string GenerateTurtleResponse(string pokemonName, List<(string Predicate, string Object)> results)
    {
        var graph = new Graph();
        var pokemonUri = graph.CreateUriNode(new Uri($"http://example.org/pokemon/{pokemonName}"));

        foreach (var result in results)
        {
            var predicateNode = graph.CreateUriNode(new Uri(result.Predicate));
            INode objectNode;

            if (result.Object.StartsWith("http"))
            {
                objectNode = graph.CreateUriNode(new Uri(result.Object));
            }
            else
            {
                objectNode = graph.CreateLiteralNode(result.Object);
            }

            graph.Assert(pokemonUri, predicateNode, objectNode);
        }

        var writer = new CompressingTurtleWriter();
        using var stringWriter = new System.IO.StringWriter();
        writer.Save(graph, stringWriter);

        return stringWriter.ToString();
    }


    private string GenerateHtmlResponse(string pokemonName, List<(string Predicate, string Object)> triples)
    {
        var sb = new StringBuilder();
        sb.Append($"<h1>Description de {pokemonName}</h1>");
        sb.Append("<table border='1' style='border-collapse:collapse; width:100%;'>");
        sb.Append("<tr><th>#</th><th>Propriété</th><th>Valeur</th></tr>");

        int counter = 1;

        foreach (var (Predicate, Object) in triples)
        {
            // Nettoyer la propriété et la valeur
            var cleanPredicate = Predicate.Replace("http://example.org/property/", "")
                                          .Replace("http://www.w3.org/2000/01/rdf-schema#", "rdfs:")
                                          .Replace("http://www.w3.org/2002/07/owl#", "owl:");
            var cleanValue = Object.Split("^^")[0].Trim('"');

            sb.Append("<tr>");
            sb.Append($"<td>{counter++}</td>");
            sb.Append($"<td>{HtmlEncoder.Default.Encode(cleanPredicate)}</td>");

            // Vérifier si la valeur est un lien ou un texte
            if (Object.StartsWith("http"))
            {
                sb.Append($"<td><a href='{HtmlEncoder.Default.Encode(Object)}' target='_blank'>{HtmlEncoder.Default.Encode(Object)}</a></td>");
            }
            else
            {
                sb.Append($"<td>{HtmlEncoder.Default.Encode(cleanValue)}</td>");
            }

            sb.Append("</tr>");
        }

        sb.Append("</table>");
        return sb.ToString();
    }

}
