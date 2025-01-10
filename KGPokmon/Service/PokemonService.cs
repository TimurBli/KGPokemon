﻿using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using HtmlAgilityPack;
using VDS.RDF;
using VDS.RDF.Writing;

public class PokemonService
{
    private readonly HttpClient _httpClient;
    private readonly Graph _globalGraph;

    public PokemonService(HttpClient httpClient)
    {
        _httpClient = httpClient;
        _globalGraph = CreateGlobalGraph();
    }

    public async Task<string> GetPokemonDataAsync(string pokemonName)
    {
        var url = $"https://bulbapedia.bulbagarden.net/wiki/{pokemonName}_(Pokémon)";
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/91.0.4472.124 Safari/537.36");

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

        AddTriplesToGlobalGraph(pokemonName, name, type, height, weight);

        return $"Données de {name} ajoutées au graphe global.";
    }

    private void AddTriplesToGlobalGraph(string id, string name, string type, string height, string weight)
    {
        var pokemonUri = _globalGraph.CreateUriNode("ex:" + id);

        var hasName = _globalGraph.CreateUriNode("prop:hasName");
        var hasType = _globalGraph.CreateUriNode("prop:hasType");
        var hasHeight = _globalGraph.CreateUriNode("prop:hasHeight");
        var hasWeight = _globalGraph.CreateUriNode("prop:hasWeight");

        _globalGraph.Assert(pokemonUri, hasName, _globalGraph.CreateLiteralNode(name));
        _globalGraph.Assert(pokemonUri, hasType, _globalGraph.CreateLiteralNode(type));
        _globalGraph.Assert(pokemonUri, hasHeight, _globalGraph.CreateLiteralNode(height));
        _globalGraph.Assert(pokemonUri, hasWeight, _globalGraph.CreateLiteralNode(weight));
    }

    public void SaveGlobalGraphToFile(string filePath)
    {
        var writer = new CompressingTurtleWriter { PrettyPrintMode = true, HighSpeedModePermitted = false };

        using var fileWriter = new System.IO.StreamWriter(filePath);
        writer.Save(_globalGraph, fileWriter);
    }

    public async Task<string> GenerateAllPokemonTripletsAsync()
    {
        var pokemonNames = await GetPokemonListAsync();
        var allTriples = new List<string>();

        pokemonNames = pokemonNames.Where(name => name != "Pokémon (species)").ToList();

        foreach (var pokemonName in pokemonNames)
        {
            try
            {
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


    private async Task<List<string>> GetPokemonListAsync()
    {
        var url = "https://bulbapedia.bulbagarden.net/w/api.php?action=query&list=categorymembers&cmtitle=Category:Pokémon&cmlimit=50&format=json";

        var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Add("User-Agent", "Mozilla/5.0");

        var response = await _httpClient.SendAsync(request);

        if (!response.IsSuccessStatusCode)
        {
            throw new Exception($"Erreur lors de la récupération de la liste des Pokémon : {response.StatusCode}");
        }

        var json = await response.Content.ReadAsStringAsync();
        var pokemonNames = new List<string>();

        using (var document = JsonDocument.Parse(json))
        {
            var members = document.RootElement.GetProperty("query").GetProperty("categorymembers");
            foreach (var member in members.EnumerateArray())
            {
                if (member.TryGetProperty("title", out var title))
                {
                    pokemonNames.Add(title.GetString().Replace(" (Pokémon)", ""));
                }
            }
        }

        return pokemonNames;
    }

    //Extrait nom
    private string ExtractPokemonName(HtmlNode infobox)
    {
        var nameNode = infobox.SelectSingleNode(".//b[1]");
        return nameNode != null ? HtmlEntity.DeEntitize(nameNode.InnerText.Trim()) : "Nom non trouvé";
    }

    //Extrait type
    private string ExtractPokemonType(HtmlNode infobox)
    {
        var typeNode = infobox.SelectSingleNode(".//a[contains(@href, '(type)')]");
        return typeNode != null ? HtmlEntity.DeEntitize(typeNode.InnerText.Trim()) : "Type non trouvé";
    }

    //Extrait taille et poids
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
        return graph;
    }
}
