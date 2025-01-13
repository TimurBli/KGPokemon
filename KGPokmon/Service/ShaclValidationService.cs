using VDS.RDF;
using VDS.RDF.Shacl;
using System;
using System.IO;
using System.Threading.Tasks;

public class ShaclValidationService
{
    public async Task<string> ValidateRdfWithShaclAsync()
    {
        // Charger les triplets RDF depuis Fuseki
        var rdfGraph = new Graph();
        rdfGraph.LoadFromUri(new Uri("http://localhost:3030/Pokemon/data"));

        // Charger le fichier SHACL
        var shaclGraph = new Graph();
        shaclGraph.LoadFromFile("wwwroot/data/shapes.ttl");

        // Créer une instance de ShapesGraph pour la validation
        var shapesGraph = new ShapesGraph(shaclGraph);

        // Exécuter la validation SHACL
        var validationReport = shapesGraph.Validate(rdfGraph);

        // Vérifier si le rapport de validation est conforme
        if (validationReport.Conforms)
        {
            return "Validation réussie : tous les triplets RDF sont conformes au SHACL.";
        }
        else
        {
            // Lire les erreurs de validation et les formater
            var errors = "❌ Validation échouée :\n\n";
            foreach (var result in validationReport.Results)
            {
                errors += $"⚠️ Focus Node: {result.FocusNode}\n";
                errors += $"📍 Path: {result.ResultPath}\n";
                errors += $"❌ Value: {result.ResultValue}\n";
                errors += $"💬 Message: {result.Message}\n\n";
            }
            return errors;
        }
    }
}
