#pragma warning disable SKEXP0050 // Type is for evaluation purposes only and is subject to change or removal in future updates. Suppress this diagnostic to proceed.
#pragma warning disable SKEXP0001 // Type is for evaluation purposes only and is subject to change or removal in future updates. Suppress this diagnostic to proceed.
#pragma warning disable SKEXP0010 // Type is for evaluation purposes only and is subject to change or removal in future updates. Suppress this diagnostic to proceed.

using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using Microsoft.Extensions.Configuration;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.Connectors.OpenAI;
using Microsoft.SemanticKernel.Memory;
using Microsoft.SemanticKernel.Plugins.Memory;

if (args.Length < 1)
{
    Console.Error.WriteLine("Syntax: FindContradictionsSK <path to file>");
    Environment.Exit(0);
}

var htcli = new HttpClient();

const string MemoryCollectionName = "PreviousParagraphs";

var config = new ConfigurationBuilder()
    .AddUserSecrets<Program>()
    .Build();

var kernel = Kernel.CreateBuilder()
    .AddAzureOpenAIChatCompletion(
        deploymentName: config["OpenAI:DeploymentName"]!,
        endpoint: config["OpenAI:Endpoint"]!,
        apiKey: config["OpenAI:ApiKey"]!, httpClient: htcli)
    .Build();

var memoryStore = new VolatileMemoryStore();
var textMemory = new MemoryBuilder()
    .WithAzureOpenAITextEmbeddingGeneration(
        deploymentName: config["OpenAI:EmbeddingsDeploymentName"]!,
        endpoint: config["OpenAI:Endpoint"]!,
        apiKey: config["OpenAI:ApiKey"]!, httpClient: htcli)
    .WithMemoryStore(memoryStore)
    .Build();

var memoryPlugin = new TextMemoryPlugin(textMemory);
var memoryFunctions = kernel.ImportPluginFromObject(memoryPlugin);

const string skPrompt = @"
Please analyze the given documents and compare them with the new document to identify any contradictions.
Note that in detecting contradictions, only instances where the new document directly contradicts information present in the provided documents
should be considered. If the new document introduces new information not mentioned in the other documents, it should not be treated as a contradiction.
Documents:
{{recall $query}}
--------------------
New document:
{{$query}}
--------------------
Contradictions (if any):
  - contradiction 1: reason + document source link in contradiction
  - contradiction 2: reason + document source link in contradiction
...
If you do not find any contradiction OR you don't have enough information to answer start your answer with ""CONSISTENT"" without further explanation.
Provide a detailed analysis of your reasonings.
";

var arguments = new KernelArguments();

arguments[TextMemoryPlugin.CollectionParam] = MemoryCollectionName;
arguments[TextMemoryPlugin.LimitParam] = "4";
arguments[TextMemoryPlugin.RelevanceParam] = "0.8";

var chatFunction = kernel.CreateFunctionFromPrompt(skPrompt, new OpenAIPromptExecutionSettings { MaxTokens = 200, Temperature = 0.8 });

using var doc = WordprocessingDocument.Open(args[0], false);

if (doc.MainDocumentPart is null)
{
    Environment.Exit(2);
}

var styles = doc.MainDocumentPart.StyleDefinitionsPart?.Styles;
var titles = new Stack<string>();
var i = 0;

foreach (var paragraph in doc.MainDocumentPart.Document.Body?.Descendants<Paragraph>() ?? [])
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

        if (i > 0)
        {
            arguments["query"] = text;

            var memSearch = await memoryPlugin.RecallAsync(text, MemoryCollectionName, 0.8, 4);
            Console.Write("Matching documents = " + memSearch);

            var answer = await chatFunction.InvokeAsync(kernel, arguments);
            
            Console.WriteLine("Response: " + answer.ToString());

            if (!answer.ToString().Contains("CONSISTENT"))
            {
                Console.WriteLine("--- Contradiction detected! ---");
                return;
            }
        }

        await textMemory.SaveInformationAsync(MemoryCollectionName, text, i.ToString());
        await Task.Delay(3000);
    }

    i++;
}
