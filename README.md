# Pokémon Knowledge Graph

## Description

This project is a Semantic Web application that builds a Knowledge Graph (KG) from the Bulbapedia wiki. The application extracts structured data from Pokémon pages, transforms them into RDF triples, and stores them in a triplestore (Fuseki). Additionally, it provides a Linked Data interface to query and explore the graph using SPARQL and dereferenceable URIs.

## Features

1. **Data Extraction:**
   - Extracts Pokémon information (name, type, height, weight) from Bulbapedia infoboxes.
   - Generates RDF triples in Turtle format.

2. **Triplestore Integration:**
   - Stores triples in Apache Jena Fuseki.
   - Visualizes and queries the data using SPARQL.

3. **Linked Data Interface:**
   - Provides a dereferenceable URI for each Pokémon.
   - Returns data in RDF (Turtle) or HTML based on client requests.

4. **External Linking:**
   - Aligns Pokémon entities with external knowledge graphs (e.g., DBpedia).
   - Adds `owl:sameAs` links for semantic alignment.

## Prerequisites

### Tools and Libraries
- **Programming Language:** C#
- **Framework:** Blazor (ASP.NET Core)
- **Libraries:**
  - [HtmlAgilityPack](https://html-agility-pack.net/) for HTML parsing.
  - [dotNetRDF](https://github.com/dotnetrdf/dotnetrdf) for RDF handling.
  - [Apache Jena Fuseki](https://jena.apache.org/documentation/fuseki2/) as the triplestore.

## Usage

### Generate Pokémon Triples
1. Navigate to the **Generate Triples** section.
2. Click "Generate Triples" to extract data from Bulbapedia and generate RDF triples.
3. The triples are stored in the Fuseki triplestore.

### Query the Knowledge Graph
1. Open the Fuseki SPARQL interface (`http://localhost:3030/dataset/query`).
2. Run queries to explore the graph. Example:
   ```sparql
   SELECT ?subject ?predicate ?object
   WHERE {
     ?subject ?predicate ?object .
   }
   LIMIT 10
   ```

### Linked Data Interface
1. Access Linked Data URIs for Pokémon:
   - Example: `http://example.org/pokemon/Bulbasaur`
2. The response format depends on the `Accept` header:
   - `text/turtle` returns RDF.
   - `text/html` returns an HTML description.

## Project Structure

- **/Services/PokemonService.cs**: Contains the logic for data extraction, RDF generation, and Fuseki integration.
- **/Pages/Pokemon.razor**: Contains the page which sends the triplets to fuseki and tests the validity of the triplets
- **/Pages/LinkedData.razor**: Implements the Linked Data interface to visualize the data.

## References

- [Bulbapedia API Documentation](https://bulbapedia.bulbagarden.net/wiki/Bulbapedia:API)
- [Apache Jena Documentation](https://jena.apache.org/documentation/)
- [dotNetRDF Documentation](https://dotnetrdf.org/)

## Authors

- **[BALI Timur]** & **[BRUN Hugo]**  - Semantic Web Developer
