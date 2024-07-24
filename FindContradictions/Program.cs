using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using FindContradictions;
using FindContradictions.DBScan;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using OpenAI.Embeddings;

var hostBuilder = new HostBuilder().ConfigureDefaults(args);
var host = hostBuilder.Build();
var config = host.Services.GetRequiredService<IConfiguration>();

var oaiCli = new Azure.AI.OpenAI.AzureOpenAIClient(
    new Uri(config["OpenAI:Endpoint"]!), 
    new System.ClientModel.ApiKeyCredential(config["OpenAI:ApiKey"]!));
var embCli = oaiCli.GetEmbeddingClient(config["OpenAI:EmbeddingsDeploymentName"]!);
var aiCli = oaiCli.GetChatClient(config["OpenAI:DeploymentName"]!);

if (args.Length < 1)
{
    Console.Error.WriteLine("Syntax: FindContradictions <path to file>");
    Environment.Exit(0);
}

using var doc = WordprocessingDocument.Open(args[0], false);

if (doc.MainDocumentPart is null)
{
    Environment.Exit(2);
}

var styles = doc.MainDocumentPart.StyleDefinitionsPart?.Styles;

var embeddings = new List<(Paragraph p, Embedding e)>();
var titles = new Stack<string>();

doc.MainDocumentPart.Document.Body?.Descendants<Paragraph>().ToList().ForEach(paragraph =>
{
    var styleId = paragraph.Descendants<ParagraphStyleId>().FirstOrDefault();
    var style = styles?.Descendants<Style>().FirstOrDefault(x => x.StyleId?.Value == styleId?.Val);

    if ((style?.StyleName?.Val?.Value?.StartsWith("heading") ?? false))
    {
        int depth = int.Parse(style.StyleName?.Val?.Value?["heading".Length..] ?? "0");
        if (depth < titles.Count)
        {
            while (titles.Count > depth)
            {
                titles.Pop();
            }
        }
        titles.Push(paragraph.InnerText);
    }
    else if (style?.StyleName?.Val?.Value?.StartsWith("toc ") ?? false)
    {

    }
    else if (paragraph.InnerText.Trim().Length > 0)
    {
        var text = $"{string.Join('\\', titles.Reverse())}:\r\n{paragraph.InnerText}";
        Console.WriteLine($"- " + text);

        var embResult = embCli.GenerateEmbedding(text);
        embeddings.Add((paragraph, embResult));
    }
});

var dbscan = new DbscanAlgorithm<(Paragraph p, Embedding e)>((a, b) => a.e.Vector.DistanceTo(b.e.Vector) /*1 - a.e.Vector.CosAngleTo(b.e.Vector)*/);
var clusters = dbscan.ComputeClusterDbscan([.. embeddings], 0.4, 2);

foreach (var cluster in clusters.Clusters)
{
    Console.WriteLine($"Cluster {cluster.Key}:");
    foreach (var point in cluster.Value)
    {
        Console.WriteLine($"- {point.Feature.p.InnerText}");
    }

    Console.WriteLine();

    if (!cluster.Value.All(x => x.Feature.p.InnerText == cluster.Value[0].Feature.p.InnerText))
    {

        Console.WriteLine("Looking for internal contradictions:");
        /*       string prompt = $@"
       Please analyze the given paragraphs to identify any contradictions.
       Paragraphs:
       {string.Join("\r\n", cluster.Value.Select(point => "- " + point.Feature.p.InnerText))}
       --------------------
       Contradictions (if any):
         - contradiction 1: reason
         - contradiction 2: reason
       ...
       If you do not find any contradiction OR you don't have enough information to answer start your answer with ""CONSISTENT"" without further explanation.
       Provide a detailed analysis of your reasonings.
       ";*/
        string prompt = $@"
Please analyze the given paragraphs to identify any contradictions.
Paragraphs:
{string.Join("\r\n", cluster.Value.Select(point => "- " + point.Feature.p.InnerText))}
--------------------
Return all the contradictions as a JSON array of objects with the following structure:
[
    {{
        ""explanation"": ""explanation of the contradiction here""        
    }}
]

If you do not find any contradiction OR you don't have enough information to answer respond with an empty array.
The response must be a valid JSON document without Markdown annotations.";
        var response = aiCli.CompleteChat(prompt);
        Console.WriteLine(response.Value.Content[0].Text);

        /*if (!response.Value.Content[0].Text.Contains("CONSISTENT"))
        {
            Console.WriteLine("*** Contradiction found ! ***");
            return;
        }*/

        await Task.Delay(3000);
    }
}
