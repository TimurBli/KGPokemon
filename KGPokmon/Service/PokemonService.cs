using System.Net.Http;
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

    public async Task<string> GetPokemonDataAsync(string pokemonName)
    {
        var url = $"https://bulbapedia.bulbagarden.net/wiki/{pokemonName}_(Pokémon)";

        // Ajouter un User-Agent pour imiter un navigateur Web
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

        // Sélectionner l'infobox
        var infobox = htmlDoc.DocumentNode.SelectSingleNode("//table[contains(@class, 'roundy')]");
        if (infobox == null)
        {
            Console.WriteLine("Infobox introuvable. Vérifiez la structure HTML.");
            return "Infobox non trouvée.";
        }

        string name = ExtractPokemonName(infobox);
        string type = ExtractPokemonType(infobox);
        string height = ExtractHeightOrWeight(infobox, "Height");
        string weight = ExtractHeightOrWeight(infobox, "Weight");

        // Générer les triplets RDF
        string rdfTriples = CreateRdfTriplesFormatted(pokemonName, name, type, height, weight);

        return $"Nom: {name}, Type: {type}, Taille: {height}, Poids: {weight}\n\nRDF Triples:\n{rdfTriples}";
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

    private string CreateRdfTriplesFormatted(string id, string name, string type, string height, string weight)
    {
        // Créer un nouveau graphe RDF
        var graph = new Graph();

        // Enregistrer un espace de noms pour les propriétés
        graph.NamespaceMap.AddNamespace("ex", new Uri("http://example.org/pokemon/"));
        graph.NamespaceMap.AddNamespace("prop", new Uri("http://example.org/property/"));

        // URI pour le Pokémon
        var pokemonUri = graph.CreateUriNode("ex:" + id);

        // Propriétés RDF
        var hasName = graph.CreateUriNode("prop:hasName");
        var hasType = graph.CreateUriNode("prop:hasType");
        var hasHeight = graph.CreateUriNode("prop:hasHeight");
        var hasWeight = graph.CreateUriNode("prop:hasWeight");

        // Ajouter les triplets au graphe
        graph.Assert(pokemonUri, hasName, graph.CreateLiteralNode(name));
        graph.Assert(pokemonUri, hasType, graph.CreateLiteralNode(type));
        graph.Assert(pokemonUri, hasHeight, graph.CreateLiteralNode(height));
        graph.Assert(pokemonUri, hasWeight, graph.CreateLiteralNode(weight));

        // Sérialiser le graphe en Turtle avec des lignes formatées
        var writer = new CompressingTurtleWriter { PrettyPrintMode = true, HighSpeedModePermitted = false };
        using var stringWriter = new System.IO.StringWriter();
        writer.Save(graph, stringWriter);

        return stringWriter.ToString();
    }
}
